using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>A socket takes a tagged toy of the right size (and heading) and eases it to its seat.</summary>
    public class SocketTests : SimTest
    {
        static PropOptions Plug(float scale, string name) => new PropOptions { Name = name, Scale = scale, Tags = new[] { "plug" } };

        [Test]
        public void APropInTheWindow_IsTakenInTheAir_AndEasedToItsSeat()
        {
            Socket socket = null;
            Prop small = null, stranger = null, good = null;
            var rejected = new List<string>();
            var seated = new List<Prop>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Cylinder(0f, 8f, 3f, 0f, 10f), MinScale = 1f, MaxScale = 2f,
                    SeatPose = s => new Pose(new Vector3(0f, 0.5f * s, 8f), Quaternion.Euler(0f, 45f, 0f)),
                    EaseSeconds = 0.25f, LockOnSeat = true, Lamp = new Vector3(0f, 4f, 8f),
                });
                socket.OnRejected += (p, fit) => rejected.Add(p.Name + " " + fit);
                socket.OnSeated += seated.Add;
                small = ctx.AddProp(BasicToys.Block(1f), new Vector3(1.5f, 3f, 8f), Plug(0.5f, "Small"));
                stranger = ctx.AddProp(BasicToys.Block(1f), new Vector3(-1.5f, 3f, 8f), new PropOptions { Name = "Stranger", Scale = 1.5f });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(2f);
            Assert.IsFalse(socket.Seated);
            CollectionAssert.AreEqual(new[] { "Small TooSmall" }, rejected, "rejected once, and the untagged prop is not even looked at");
            Assert.IsFalse(stranger.Driven);
            Assert.AreEqual(FitState.TooBig, socket.Fit(2.5f));
            Assert.AreEqual(FitState.Good, socket.Fit(1.5f));

            good = Game.Context.AddProp(BasicToys.Block(1f), new Vector3(1.2f, 6f, 9f), Plug(1.5f, "Good"));
            Run(2);
            Assert.IsTrue(socket.Seating, "taken at once: nothing has to land");
            Assert.IsTrue(good.Driven);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => socket.Seated, 1f));
            Assert.LessOrEqual(Game.LevelTicks * Sim.Dt, 2f + 0.4f, "seated a quarter of a second after it was released");
            Assert.AreSame(good, socket.SeatedProp);
            Assert.AreEqual(1, seated.Count);
            Assert.Less(Vector3.Distance(good.Position, new Vector3(0f, 0.75f, 8f)), 1e-3f, "at the seat pose for its scale");
            Assert.Less(Quaternion.Angle(good.Rotation, Quaternion.Euler(0f, 45f, 0f)), 0.1f);
            Assert.IsFalse(good.Grabbable, "LockOnSeat");
            Assert.AreEqual(Art.Palette.Go, socket.Signal);

            RunSeconds(1f);
            Assert.Less(Vector3.Distance(good.Position, new Vector3(0f, 0.75f, 8f)), 1e-3f, "and it stays there");
        }

        [Test]
        public void ThenFallTo_LowersTheProp_UntilItsTopIsAtTheHeight()
        {
            // The well of Level 2 in small: a plug that is eased onto the axis, then drops until its top is flush.
            Socket socket = null;
            Prop plug = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 80f, -9f);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Cylinder(0f, 8f, 4f, -1f, 18f), MinScale = 2f, MaxScale = 5f,
                    SeatPose = s => new Pose(new Vector3(0f, 6f, 8f), Quaternion.identity),
                    EaseSeconds = 0.35f, ThenFallTo = 0f, LockOnSeat = true,
                });
                plug = ctx.AddProp(BasicToys.Cylinder(0.5f, 1f), new Vector3(1.2f, 5f, 8.5f), Quaternion.Euler(12f, 0f, 8f), Plug(4f, "Plug"));
                ctx.SetSpawn(new Vector3(0f, -9f, 0f), 0f);
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => socket.Seated, 3f));
            GadgetKit.VerticalExtent(plug, out _, out float top);
            Assert.AreEqual(0f, top, 0.01f, "its top is flush with the floor level asked for");
            Assert.AreEqual(0f, plug.Position.x, 1e-3f, "on the axis");
            Assert.Less(Quaternion.Angle(plug.Rotation, Quaternion.identity), 0.1f, "upright");
            float seconds = Game.LevelTicks * Sim.Dt;
            // 0.35 s of ease, then 8 units of free fall: sqrt(2 * 8 / 22) = 0.85 s.
            Assert.AreEqual(0.35f + 0.85f, seconds, 0.1f);
        }

        [Test]
        public void TheHeadingTest_RejectsAPropThatPointsTheWrongWay()
        {
            Socket socket = null;
            Prop ruler = null;
            var rejected = new List<FitState>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Box(new Vector3(0f, 1f, 6f), new Vector3(6f, 2f, 6f)), MinScale = 0.8f, MaxScale = 1.2f,
                    YawAxis = Vector3.forward, YawReference = Vector3.right, YawTolerance = 30f,
                    SeatPose = s => new Pose(new Vector3(0f, 0.1f, 6f), Quaternion.Euler(0f, 90f, 0f)),
                });
                socket.OnRejected += (p, fit) => rejected.Add(fit);
                // Long axis along +Z: its "cap end" points away from +X.
                ruler = ctx.AddProp(BasicToys.Block(new Vector3(0.6f, 0.2f, 3f)), new Vector3(0f, 0.2f, 6f), Plug(1f, "Ruler"));
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(1f);
            Assert.IsFalse(socket.Seated);
            CollectionAssert.AreEqual(new[] { FitState.Backwards }, rejected);
            Assert.AreEqual(FitState.Backwards, socket.Fit(ruler));

            // Turned to within the tolerance (70 degrees: 20 off +X) it is taken. It has to leave first: once per drop.
            ruler.SetPose(new Vector3(0f, 0.2f, -6f), Quaternion.Euler(0f, 70f, 0f));
            Run(2);
            ruler.SetPose(new Vector3(0.5f, 0.2f, 6.5f), Quaternion.Euler(0f, 70f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => socket.Seated, 2f));
            Assert.Less(Quaternion.Angle(ruler.Rotation, Quaternion.Euler(0f, 90f, 0f)), 0.1f, "eased to the seat's heading");
        }

        [Test]
        public void TheKeyhole_ShrinksAndTurnsTheKey()
        {
            Socket socket = null;
            Prop key = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Box(new Vector3(0f, 1.65f, 5.6f), new Vector3(1.2f, 1.2f, 0.9f)), MinScale = 0.06f, MaxScale = 0.5f,
                    SeatPose = s => new Pose(new Vector3(0f, 1.65f, 5.9f), Quaternion.identity), SeatScale = s => 0.15f,
                    EaseSeconds = 0.25f, ThenTurnDegrees = 90f, ThenTurnAxis = Vector3.forward, ThenTurnSeconds = 0.4f, LockOnSeat = true,
                });
                key = ctx.AddProp(BasicToys.Block(new Vector3(0.7f, 0.04f, 2f)), new Vector3(0.1f, 1.7f, 5.5f), Plug(0.3f, "Key"));
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => socket.Seated, 2f));
            Assert.AreEqual(0.65f, Game.LevelTicks * Sim.Dt, 0.06f, "a quarter second in, 0.4 s to turn");
            Assert.AreEqual(0.15f, key.Scale, 1e-4f);
            Assert.Less(Quaternion.Angle(key.Rotation, Quaternion.Euler(0f, 0f, 90f)), 0.5f, "turned a quarter about its own long axis");
        }

        [Test]
        public void AGrab_UnseatsAnUnlockedProp_AndReleaseHandsItBack()
        {
            Socket socket = null;
            Prop plug = null;
            int unseated = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Box(new Vector3(0f, 1.5f, 7f), new Vector3(6f, 3f, 10f)), MinScale = 0.5f, MaxScale = 2f,
                    SeatPose = s => new Pose(new Vector3(0f, 0.5f * s + 1f, 5f), Quaternion.identity),
                });
                socket.OnUnseated += p => unseated++;
                plug = ctx.AddProp(BasicToys.Block(1f), new Vector3(0.4f, 0.5f, 5f), Plug(1f, "Plug"));
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => socket.Seated, 2f));
            Assert.IsTrue(plug.Grabbable, "not locked: still the player's to take");

            var bot = new Bot(Game);
            IEnumerator Script()
            {
                yield return bot.Grab(plug);
                yield return bot.Wait(0.1f);
                Assert.IsFalse(socket.Seated, "a grab unseats it");
                Assert.AreEqual(1, unseated);
                // Put back, it is seated again.
                yield return bot.DropAt(new Vector3(0f, 1f, 5f));
                yield return bot.Until(() => socket.Seated, 2f);
            }
            BotRunner.Run(Game, Script(), 20f);

            socket.Release(new Vector3(0f, 0f, 2f));
            Assert.IsFalse(socket.Seated);
            Assert.AreEqual(2, unseated);
            Assert.IsFalse(plug.Driven);
            RunSeconds(1f);
            Assert.IsFalse(socket.Seated, "a released prop is not taken again until it has left or been picked up");
            Assert.Less(plug.Position.y, 0.5f * plug.Scale + 0.1f, "it fell back onto the floor");
        }
    }
}
