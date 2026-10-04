using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// What milestone 2 asked of the simulation: the PropImpact event, the grabber's focus candidate, the
    /// hold payloads and LevelUnloading (ART_BIBLE 13), and the engine requests of LEVELS 0.4 - GrabPose,
    /// BeginDrive / EndDrive, FrozenUntilGrabbed, AllowPitch, KeepUpright and the bot's slow radius.
    /// (GroundY is in EnvironmentTests.)
    /// </summary>
    public class EngineAsksTests : SimTest
    {
        // ------------------------------------------------------------------------------------------
        // PropImpact
        // ------------------------------------------------------------------------------------------

        [Test]
        public void PropImpact_ADroppedBlockReportsItsLanding_AndThenNothing()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 3.5f, 6f), new PropOptions { Name = "Block" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var impacts = new List<PropImpactEvent>();
            Game.Events.PropImpact += e => impacts.Add(e);

            RunSeconds(0.4f);
            Assert.AreEqual(0, impacts.Count, "free fall is not an impact");
            RunSeconds(2.6f);
            Assert.GreaterOrEqual(impacts.Count, 1, "the landing");
            Assert.LessOrEqual(impacts.Count, 3, "one landing, perhaps a bounce - not a stream");

            PropImpactEvent landing = impacts[0];
            Assert.AreSame(block, landing.Prop);
            // It fell 3 units: v = sqrt(2 g h).
            Assert.AreEqual(Mathf.Sqrt(2f * Game.Gravity * 3f), landing.Speed, 1f);
            Assert.AreEqual(block.Mass, landing.Mass, 1e-5f);
            Assert.Greater(Vector3.Dot(landing.Normal, Vector3.up), 0.95f, "the push of a landing points up");
            Assert.Less(landing.Point.y, 0.3f, "the point is on the side of the block that hit");
            Assert.Less(Vector2.Distance(new Vector2(landing.Point.x, landing.Point.z), new Vector2(0f, 6f)), 0.8f);

            int count = impacts.Count;
            RunSeconds(2f);
            Assert.AreEqual(count, impacts.Count, "a resting prop reports nothing");
        }

        [Test]
        public void PropImpact_HasASpeedThreshold_AndIgnoresHeldAndGentlyPlacedProps()
        {
            Prop low = null, high = null, carried = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // 0.02 above the floor: lands at under 1 unit per second. 0.5 above: at 4.7.
                low = ctx.AddProp(BasicToys.Block(1f), new Vector3(-4f, 0.52f, 6f), new PropOptions { Name = "Low" });
                high = ctx.AddProp(BasicToys.Block(1f), new Vector3(4f, 1f, 6f), new PropOptions { Name = "High" });
                carried = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 0.3f, 3f), new PropOptions { Name = "Carried" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var hit = new List<Prop>();
            Game.Events.PropImpact += e => hit.Add(e.Prop);

            RunSeconds(1.5f);
            CollectionAssert.Contains(hit, high);
            CollectionAssert.DoesNotContain(hit, low, "below " + Prop.ImpactSpeed + " units per second is not an impact");

            // Pick the small block up, carry it across the floor and put it down: the mechanic places it a hair
            // above the surface, which is no impact either.
            hit.Clear();
            LookAt(carried.Center);
            Click();
            Assert.AreSame(carried, Game.Grabber.Held);
            LookAt(new Vector3(1f, 0f, 5f));
            RunSeconds(0.5f);
            Click();
            Assert.IsNull(Game.Grabber.Held);
            RunSeconds(1f);
            CollectionAssert.IsEmpty(hit, "holding and putting down makes no impact");
        }

        [Test]
        public void PropImpact_AForceFromLevelCodeIsNotAnImpact()
        {
            Prop ball = null;
            bool blowing = true;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ball = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(0f, 6f, 6f), new PropOptions { Name = "Ball" });
                Prop pushed = ball;
                // A wind stronger than any real one: 200 units per second squared, sideways. Each tick it changes
                // the ball's velocity by more than the impact threshold.
                ctx.OnUpdate(dt =>
                {
                    if (blowing) pushed.Body.AddForce(Vector3.right * (200f * pushed.Mass));
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var impacts = new List<PropImpactEvent>();
            Game.Events.PropImpact += e => impacts.Add(e);

            Run(12);
            Assert.Greater(ball.Velocity.x, 30f, "setup: the wind is blowing");
            Assert.Greater(200f * Sim.Dt, Prop.ImpactSpeed, "setup: and it is stronger per tick than the threshold");
            CollectionAssert.IsEmpty(impacts, "a push is not a collision");

            blowing = false;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => impacts.Count > 0, 3f), "the landing still is");
            Assert.AreSame(ball, impacts[0].Prop);
        }

        static List<string> RecordImpacts(Game game, float seconds)
        {
            var log = new List<string>();
            game.Events.PropImpact += e => log.Add(game.LevelTicks + " " + e.Prop.Name + " " + e.Speed.ToString("R") + " " + e.Mass.ToString("R") + " " +
                                                   e.Point.x.ToString("R") + " " + e.Point.y.ToString("R") + " " + e.Point.z.ToString("R") + " " +
                                                   e.Normal.x.ToString("R") + " " + e.Normal.y.ToString("R") + " " + e.Normal.z.ToString("R"));
            TestHelpers.RunSeconds(game, seconds);
            return log;
        }

        static void Tumble(LevelContext ctx)
        {
            TestHelpers.Floor(ctx);
            TestHelpers.Box(ctx, new Vector3(0f, 0.5f, 8f), new Vector3(3f, 1f, 3f));
            ctx.AddProp(BasicToys.Block(1f), new Vector3(0.4f, 4f, 8f), Quaternion.Euler(20f, 30f, 10f), new PropOptions { Name = "A" });
            ctx.AddProp(BasicToys.Block(new Vector3(0.5f, 2f, 0.5f)), new Vector3(-0.9f, 6f, 7.6f), Quaternion.Euler(0f, 0f, 35f), new PropOptions { Name = "B" });
            ctx.AddProp(BasicToys.Ball(0.4f), new Vector3(0.2f, 8f, 8.3f), new PropOptions { Name = "C", Bounciness = 0.6f });
            ctx.AddProp(BasicToys.Cylinder(0.5f, 1.2f), new Vector3(1.2f, 10f, 8f), Quaternion.Euler(60f, 0f, 0f), new PropOptions { Name = "D" });
            ctx.SetSpawn(Vector3.zero, 0f);
        }

        [Test]
        public void PropImpact_IsDeterministic()
        {
            Game = Game.Create();
            Game.LoadLevel(new AdHocLevel(Tumble));
            List<string> first = RecordImpacts(Game, 5f);
            Game.Dispose();

            Game = Game.Create();
            Game.LoadLevel(new AdHocLevel(Tumble));
            List<string> second = RecordImpacts(Game, 5f);

            Assert.Greater(first.Count, 4, "four props tumbling onto a box and the floor");
            CollectionAssert.AreEqual(first, second, "the same level raises the same impacts, bit for bit");

            // And once more after a restart in the same Game.
            Game.RestartLevel();
            var third = new List<string>();
            Game.Events.PropImpact += e => third.Add(Game.LevelTicks + " " + e.Prop.Name + " " + e.Speed.ToString("R"));
            RunSeconds(5f);
            // (Game is this fixture's; the listener of RecordImpacts is still subscribed and harmless.)
            Assert.AreEqual(first.Count, third.Count);
            for (int i = 0; i < first.Count; i++) StringAssert.StartsWith(third[i], first[i]);
        }

        // ------------------------------------------------------------------------------------------
        // Focus candidate and hold payloads
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Focus_IsThePropUnderTheAim_ComputedAtMostOncePerTick()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f), new PropOptions { Name = "Block" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            PerspectiveGrabber grabber = Game.Grabber;
            LookAt(block.Center);
            Game.Tick();

            int queries = grabber.FocusQueries;
            for (int i = 0; i < 20; i++) Assert.AreSame(block, grabber.Focus);
            Assert.AreEqual(queries + 1, grabber.FocusQueries, "twenty frames between two ticks cost one search");
            Assert.AreSame(grabber.FindTarget(), grabber.Focus, "it is the prop a click would take");

            Game.Tick();
            Assert.AreSame(block, grabber.Focus);
            Assert.AreEqual(queries + 2, grabber.FocusQueries, "a new tick, a new search");

            // Looking away: the answer of this tick stands until the next one.
            LookAt(new Vector3(30f, 20f, 5f));
            Assert.AreSame(block, grabber.Focus);
            Game.Tick();
            Assert.IsNull(grabber.Focus);

            // Nothing is in focus while something is held, and that costs nothing.
            LookAt(block.Center);
            Click();
            Assert.AreSame(block, grabber.Held);
            queries = grabber.FocusQueries;
            Assert.IsNull(grabber.Focus);
            Game.Tick();
            Assert.IsNull(grabber.Focus);
            Assert.AreEqual(queries, grabber.FocusQueries);
            Click();
            Assert.IsNull(grabber.Held);

            // A prop that stops being grabbable, or is removed, is not handed out from the cache.
            Game.Tick();
            LookAt(block.Center);
            Game.Tick();
            Assert.AreSame(block, grabber.Focus);
            block.Grabbable = false;
            Assert.IsNull(grabber.Focus);
            block.Grabbable = true;
            Game.Tick();
            Assert.AreSame(block, grabber.Focus);
            Game.Context.RemoveProp(block);
            Assert.IsNull(grabber.Focus);
        }

        [Test]
        public void HoldEvents_CarryGrabDistance_ScaleAtGrab_AndCurrentScale()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.4f, 4f), new PropOptions { Name = "Block", Scale = 0.8f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            PropHoldEvent grabbed = default, held = default, dropped = default;
            int holds = 0;
            Game.Events.PropGrabbed += e => grabbed = e;
            Game.Events.PropHeld += e => { held = e; holds++; };
            Game.Events.PropDropped += e => dropped = e;
            Run(5);

            float distance = Vector3.Distance(Game.Player.Eye, block.Center);
            LookAt(block.Center);
            Click();
            Assert.AreSame(block, grabbed.Prop);
            Assert.AreEqual(0.8f, grabbed.GrabScale, 1e-5f);
            Assert.AreEqual(distance, grabbed.GrabDistance, 1e-3f);
            Assert.AreEqual(0.8f, Game.Grabber.GrabScale, 1e-5f);
            Assert.AreEqual(distance, Game.Grabber.GrabDistance, 1e-3f);
            Assert.AreEqual(1, holds, "PropHeld is raised in the tick of the grab too");

            // Carried out along the floor it lands further away and is bigger.
            LookAt(new Vector3(0f, 0.6f, 16f));
            Game.Tick();
            Assert.AreSame(block, held.Prop);
            Assert.AreEqual(0.8f, held.GrabScale, 1e-5f, "the scale at the grab stays in every payload");
            Assert.AreEqual(distance, held.GrabDistance, 1e-3f);
            Assert.AreEqual(block.Scale, held.Scale, 1e-5f, "the current scale");
            Assert.Greater(held.Scale, 1.5f);
            Assert.AreEqual(held.Scale / 0.8f, held.Factor, 1e-4f);
            Assert.AreEqual(Game.Grabber.HoldDistance, held.Distance, 1e-3f);
            Assert.AreEqual(held.Distance / held.GrabDistance, held.Factor, 1e-3f, "scale goes with distance");
            Assert.AreEqual(block.Radius, held.Radius, 1e-4f);

            Click();
            Assert.AreSame(block, dropped.Prop);
            Assert.AreEqual(0.8f, dropped.GrabScale, 1e-5f);
            Assert.AreEqual(block.Scale, dropped.Scale, 1e-5f);
            Assert.AreEqual(held.Scale, dropped.Scale, 1e-3f);
            Assert.AreEqual(0f, Game.Grabber.GrabScale, "nothing is held any more");
        }

        // ------------------------------------------------------------------------------------------
        // LevelUnloading
        // ------------------------------------------------------------------------------------------

        [Test]
        public void LevelUnloading_IsRaisedWhileTheLevelStillExists()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var log = new List<string>();
            Game.Events.LevelUnloading += e =>
                log.Add("unloading " + (Game.Level == e.Level && Game.Props.Count == 1 && !Game.Props[0].Removed && Game.Props[0].GameObject != null ? "intact" : "TORN DOWN"));
            Game.Events.LevelLoaded += e => log.Add("loaded");
            Game.Events.LevelRestarted += e => log.Add("restarted");

            Game.RestartLevel();
            CollectionAssert.AreEqual(new[] { "restarted", "unloading intact", "loaded" }, log);

            log.Clear();
            Game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(0f, 0.5f, 5f));
            }));
            CollectionAssert.AreEqual(new[] { "unloading intact", "loaded" }, log);

            // Asked for from inside a tick, the switch - and with it the event - waits for the tick to end.
            log.Clear();
            bool asked = false;
            Game.Context.OnUpdate(dt =>
            {
                if (asked) return;
                asked = true;
                Game.RestartLevel();
                log.Add("asked");
            });
            Game.Tick();
            CollectionAssert.AreEqual(new[] { "asked", "restarted", "unloading intact", "loaded" }, log);

            log.Clear();
            Game.Dispose();
            CollectionAssert.AreEqual(new[] { "unloading intact" }, log, "disposing the Game unloads the level too");
        }

        // ------------------------------------------------------------------------------------------
        // LEVELS 0.4, request 1: GrabPose
        // ------------------------------------------------------------------------------------------

        Prop FloatingBlock(Quaternion rotation, GrabPose pose, bool allowPitch = true)
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // Fixed: it stays where it is put, tilted in mid-air, and can be grabbed.
                block = ctx.AddProp(BasicToys.Block(new Vector3(0.6f, 1.2f, 0.4f)), new Vector3(0f, 1.5f, 4f), rotation,
                    new PropOptions { Name = "Block", Body = PropBody.Fixed, GrabPose = pose, AllowPitch = allowPitch });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            LookAt(block.Center);
            return block;
        }

        static float Tilt(Prop prop) => Vector3.Angle(prop.Transform.up, Vector3.up);

        [Test]
        public void GrabPose_Keep_LeavesTheTiltAlone()
        {
            Prop block = FloatingBlock(Quaternion.Euler(0f, 20f, 35f), GrabPose.Keep);
            Click();
            Run(PerspectiveGrabber.PoseTicks + 5);
            Assert.AreEqual(35f, Tilt(block), 0.01f);
        }

        [Test]
        public void GrabPose_Upright_EasesOntoTheAuthoredUpAxis_Within150Milliseconds()
        {
            Prop block = FloatingBlock(Quaternion.Euler(0f, 20f, 90f), GrabPose.Upright);
            Assert.AreEqual(90f, Tilt(block), 0.01f, "setup: lying on its side");
            Vector3 heading = block.Transform.forward;

            Click();
            float afterOneTick = Tilt(block);
            Assert.Less(afterOneTick, 90f, "the ease has begun");
            Assert.Greater(afterOneTick, 60f, "but it is an ease, not a snap");
            Run(3);
            Assert.Less(Tilt(block), afterOneTick);
            Assert.Greater(Tilt(block), 5f);
            Run(PerspectiveGrabber.PoseTicks - 4);
            Assert.AreEqual(0f, Tilt(block), 0.05f, "upright after " + PerspectiveGrabber.PoseTicks + " ticks of holding");
            Assert.Less(Vector3.Angle(heading, block.Transform.forward), 0.5f, "the heading is kept");
            Assert.AreEqual(0.15f, PerspectiveGrabber.PoseTicks * Sim.Dt, 1e-4f);

            // F still pitches it a quarter turn afterwards: the pose is where the hold starts, not a lock.
            Input.Once.RotatePitch = true;
            Game.Tick();
            Assert.AreEqual(90f, Tilt(block), 0.05f);
        }

        [Test]
        public void GrabPose_Upright_TurnsAnUpsideDownPropOver()
        {
            Prop block = FloatingBlock(Quaternion.Euler(180f, 0f, 0f), GrabPose.Upright);
            Assert.AreEqual(180f, Tilt(block), 0.01f);
            Click();
            Run(PerspectiveGrabber.PoseTicks);
            Assert.AreEqual(0f, Tilt(block), 0.05f);
        }

        [Test]
        public void GrabPose_Snap90_StandsTheNearestAxisUp()
        {
            // 25 degrees off: its own up axis is the nearest.
            Prop block = FloatingBlock(Quaternion.Euler(25f, 40f, 0f), GrabPose.Snap90);
            Click();
            Run(PerspectiveGrabber.PoseTicks);
            Assert.AreEqual(0f, Tilt(block), 0.05f);
            Game.Dispose();
            Game = null;

            // 70 degrees over: now its forward axis points most nearly up, and that is the one that is stood up.
            block = FloatingBlock(Quaternion.Euler(-70f, 40f, 0f), GrabPose.Snap90);
            Assert.AreEqual(70f, Tilt(block), 0.01f);
            Click();
            Run(PerspectiveGrabber.PoseTicks);
            Assert.AreEqual(90f, Tilt(block), 0.05f, "it lies flat, a quarter turn from upright");
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(block.Transform.forward, Vector3.up)), 1e-4f, "with its forward axis straight up or down");
        }

        [Test]
        public void GrabPose_ADropBeforeTheEaseIsOverStillPutsThePropDownStraight()
        {
            Prop block = FloatingBlock(Quaternion.Euler(0f, 0f, 60f), GrabPose.Upright);
            Click();
            Assert.Greater(Tilt(block), 30f);
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(0f, Tilt(block), 0.05f);
        }

        // ------------------------------------------------------------------------------------------
        // LEVELS 0.4, request 4 and 5: AllowPitch, KeepUpright
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AllowPitch_False_MakesThePitchKeyDoNothing()
        {
            Prop block = FloatingBlock(Quaternion.identity, GrabPose.Keep, allowPitch: false);
            Assert.IsFalse(block.AllowPitch);
            Click();
            Input.Once.RotatePitch = true;
            Game.Tick();
            Assert.AreEqual(0f, Tilt(block), 0.01f, "F does nothing; the toy keeps world up");
            Quaternion before = block.Rotation;
            Input.Once.RotateYaw = 1;
            Game.Tick();
            Assert.AreEqual(PerspectiveGrabber.YawStepDegrees, Quaternion.Angle(before, block.Rotation), 0.01f, "Q still turns it");
            Assert.AreEqual(0f, Tilt(block), 0.01f);

            // The same key on an ordinary prop.
            Game.Dispose();
            Game = null;
            block = FloatingBlock(Quaternion.identity, GrabPose.Keep);
            Assert.IsTrue(block.AllowPitch);
            Click();
            Input.Once.RotatePitch = true;
            Game.Tick();
            Assert.AreEqual(90f, Tilt(block), 0.01f);
        }

        [Test]
        public void KeepUpright_FreezesRotationAboutXAndZ()
        {
            Prop free = null, upright = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                free = ctx.AddProp(BasicToys.Block(new Vector3(0.5f, 2f, 0.5f)), new Vector3(-3f, 1f, 6f), new PropOptions { Name = "Free" });
                upright = ctx.AddProp(BasicToys.Block(new Vector3(0.5f, 2f, 0.5f)), new Vector3(3f, 1f, 6f), new PropOptions { Name = "Upright", KeepUpright = true });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsTrue(upright.KeepUpright);
            Assert.IsFalse(free.KeepUpright);
            Run(10);
            // A good shove at the top of each.
            free.Body.AddForceAtPosition(Vector3.forward * 4f * free.Mass, free.Center + Vector3.up, ForceMode.Impulse);
            upright.Body.AddForceAtPosition(Vector3.forward * 4f * upright.Mass, upright.Center + Vector3.up, ForceMode.Impulse);
            RunSeconds(2f);
            Assert.Greater(Tilt(free), 60f, "control: the free block was knocked over");
            Assert.Less(Tilt(upright), 0.5f, "the upright one slid but did not tip");
            Assert.Greater(upright.Position.z, 6.2f, "it did move");

            // It can still turn about its own up axis.
            float yaw = upright.Rotation.eulerAngles.y;
            upright.Body.WakeUp();
            upright.Body.AddTorque(Vector3.up * 6f, ForceMode.VelocityChange);
            Run(10);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, upright.Rotation.eulerAngles.y)), 0.3f, "friction stops it soon, but it turned");
            Assert.Less(Tilt(upright), 0.5f);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ, upright.Body.constraints);
            Assert.AreEqual(RigidbodyConstraints.None, free.Body.constraints);
        }

        // ------------------------------------------------------------------------------------------
        // LEVELS 0.4, request 3: FrozenUntilGrabbed
        // ------------------------------------------------------------------------------------------

        [Test]
        public void FrozenUntilGrabbed_StaysPutUntilTheFirstGrab_ThenIsDynamic()
        {
            Prop ball = null, plain = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // Nothing under it: only being frozen keeps it in the air.
                ball = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(0f, 2f, 5f), new PropOptions { Name = "Ball", FrozenUntilGrabbed = true });
                plain = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(4f, 2f, 5f), new PropOptions { Name = "Plain" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsTrue(ball.Frozen);
            Assert.AreEqual(PropBody.Dynamic, ball.BodyKind, "it is a dynamic prop that has not started yet");
            Assert.IsTrue(ball.Grabbable);
            RunSeconds(1f);
            Assert.AreEqual(new Vector3(0f, 2f, 5f), ball.Position, "frozen at the authored pose");
            Assert.AreEqual(Vector3.zero, ball.Velocity);
            Assert.Less(plain.Position.y, 0.6f, "control: an ordinary ball fell");

            LookAt(ball.Center);
            Click();
            Assert.AreSame(ball, Game.Grabber.Held);
            Assert.IsFalse(ball.Frozen, "the first grab thaws it");
            LookAt(new Vector3(0f, 0f, 4f));
            Game.Tick();
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(ball.Body.isKinematic);
            RunSeconds(1.5f);
            Assert.Less(ball.Position.y, 0.5f * ball.Scale + 0.1f, "after the drop it falls like any prop");

            ball.Respawn();
            Assert.IsTrue(ball.Frozen, "a respawn puts it back on its pedestal, frozen");
            RunSeconds(0.5f);
            Assert.AreEqual(new Vector3(0f, 2f, 5f), ball.Position);

            ball.Unfreeze();
            Assert.IsFalse(ball.Frozen);
            RunSeconds(1f);
            Assert.Less(ball.Position.y, 0.6f, "Unfreeze lets it go without a grab");
        }

        [Test]
        public void FrozenUntilGrabbed_OnlyMeansSomethingForDynamicProps()
        {
            Prop platform = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                platform = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 2f, 5f), new PropOptions { Body = PropBody.Kinematic, FrozenUntilGrabbed = true });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsFalse(platform.Frozen);
            Assert.IsNotNull(platform.Mover);
        }

        // ------------------------------------------------------------------------------------------
        // LEVELS 0.4, request 2: BeginDrive / EndDrive
        // ------------------------------------------------------------------------------------------

        sealed class RaftLevel : LevelDefinition
        {
            public Prop Raft, Pebble;
            public Vector3 Velocity = new Vector3(2f, 0f, 0f);
            public float PlayerZ;

            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx);
                // Far lighter than an eighth of the player: a "light prop" while it is simulated.
                Raft = ctx.AddProp(BasicToys.Block(new Vector3(3f, 0.4f, 3f)), new Vector3(0f, 0.2f, 0f), new PropOptions { Name = "Raft", Density = 0.01f, Friction = 0.9f });
                Pebble = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(8f, 0.3f, 3f), new PropOptions { Name = "Pebble", Density = 0.1f });
                Prop raft = Raft;
                ctx.OnUpdate(dt =>
                {
                    // What a gadget does: one MoveTo per tick while it has the prop.
                    if (raft.Driven) raft.Mover.MoveTo(raft.Position + Velocity * dt);
                });
                ctx.SetSpawn(new Vector3(0f, 0.4f, PlayerZ), 0f);
            }
        }

        RaftLevel LoadRaft(float playerZ = 0f)
        {
            var level = new RaftLevel { PlayerZ = playerZ };
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(level);
            return level;
        }

        [Test]
        public void BeginDrive_MovesADynamicPropThroughAMover_AndRidersInheritItsVelocity()
        {
            RaftLevel level = LoadRaft();
            Prop raft = level.Raft;
            RunSeconds(0.5f);
            Assert.Less(raft.Mass, Player.Mass / 8f, "setup: a light prop");
            Assert.AreSame(raft, Game.Player.GroundProp, "setup: the player stands on the raft");
            Assert.IsNull(raft.Mover);
            Assert.IsFalse(raft.Driven);

            Mover mover = raft.BeginDrive();
            Assert.IsNotNull(mover);
            Assert.IsTrue(raft.Driven);
            Assert.AreSame(mover, raft.Mover);
            Assert.AreSame(mover, raft.BeginDrive(), "asking twice is harmless");
            Assert.IsTrue(raft.Body.isKinematic);
            Assert.AreEqual(PropBody.Dynamic, raft.BodyKind, "it is still a Dynamic prop, on loan");
            CollectionAssert.Contains(Game.Movers, mover);
            Assert.AreEqual(Layers.Prop, raft.GameObject.layer, "triggers go on sensing it");

            float raftStart = raft.Position.x, playerStart = Game.Player.Position.x;
            RunSeconds(1.5f);
            Assert.AreEqual(3f, raft.Position.x - raftStart, 0.05f, "driven at 2 units per second");
            Assert.AreEqual(2f, raft.Velocity.x, 1e-3f, "prop.Velocity is the mover's");
            Assert.AreEqual(3f, Game.Player.Position.x - playerStart, 0.15f, "the rider goes along");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreSame(raft, Game.Player.GroundProp, "a driven prop is ground, whatever its mass");
            Assert.AreEqual(0.4f, Game.Player.Position.y, 0.02f, "and the player neither sinks into it nor hops");

            // Handing it back with the mover's velocity: it keeps going and is simulated again.
            raft.EndDrive(mover.Velocity);
            Assert.IsFalse(raft.Driven);
            Assert.IsNull(raft.Mover);
            Assert.IsFalse(raft.Body.isKinematic);
            Assert.AreEqual(2f, raft.Body.linearVelocity.x, 1e-3f);
            Assert.AreEqual(2f, raft.Velocity.x, 1e-3f);
            raft.EndDrive(Vector3.zero);
            Assert.IsFalse(raft.Driven, "ending twice is harmless");
            Assert.AreEqual(2f, raft.Body.linearVelocity.x, 1e-3f, "and changes nothing");

            // A prop that is thrown rather than set down: it leaves with the velocity it was handed back with.
            Prop pebble = level.Pebble;
            Assert.IsNotNull(pebble.BeginDrive());
            Run(2);
            float height = pebble.Position.y;
            pebble.EndDrive(new Vector3(0f, 6f, 0f));
            Run(5);
            Assert.Greater(pebble.Position.y, height + 0.3f, "it carries on with the velocity it was given");
        }

        [Test]
        public void BeginDrive_ADrivenLightPropIsNotShovedAside()
        {
            RaftLevel level = LoadRaft(playerZ: 3f);
            Prop pebble = level.Pebble;
            Assert.Less(pebble.Mass, Player.Mass / 8f, "setup: light enough to be kicked");
            Game.Player.Teleport(new Vector3(5f, 0f, 3f), 90f);
            RunSeconds(0.3f);

            // Simulated, it is kicked along the floor by a walking player.
            Input.Hold.MoveZ = 1f;
            RunSeconds(1f);
            Assert.Greater(pebble.Position.x, 8.5f, "control: a light dynamic prop is shoved aside");

            // Driven (and held still by its gadget), it stands like a wall.
            Input.Hold.MoveZ = 0f;
            Game.Player.Teleport(new Vector3(pebble.Position.x - 3f, 0f, 3f), 90f);
            RunSeconds(0.3f);
            Assert.IsNotNull(pebble.BeginDrive());
            Vector3 at = pebble.Position;
            Input.Hold.MoveZ = 1f;
            RunSeconds(1.5f);
            Assert.Less(Vector3.Distance(at, pebble.Position), 1e-4f, "a driven prop does not give way");
            Assert.Less(Game.Player.Position.x, at.x - 0.3f - 0.25f, "the player is stopped by it");
        }

        [Test]
        public void BeginDrive_AGrabEndsTheDrive_AndTheLoanHasItsLimits()
        {
            RaftLevel level = LoadRaft(playerZ: -6f);
            Prop raft = level.Raft;
            level.Velocity = Vector3.zero;
            RunSeconds(0.3f);
            Mover mover = raft.BeginDrive();
            Game.Tick();

            LookAt(raft.Center);
            Click();
            Assert.AreSame(raft, Game.Grabber.Held, "a driven prop can be grabbed");
            Assert.IsFalse(raft.Driven, "and the grab ends the drive");
            Assert.IsNull(raft.Mover);
            Assert.IsNull(raft.BeginDrive(), "a held prop cannot be driven");
            Vector3 heldAt = raft.Position;
            mover.MoveTo(heldAt + Vector3.right * 5f);
            Game.Tick();
            Assert.Less(raft.Position.x, heldAt.x + 0.5f, "the old mover has no say over a prop that was taken away");

            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(raft.Body.isKinematic, "dropped, it is an ordinary dynamic prop again");
            Assert.IsFalse(raft.Driven);

            // Respawning and removing end a drive as well.
            Assert.IsNotNull(raft.BeginDrive());
            raft.Respawn();
            Assert.IsFalse(raft.Driven);
            Assert.IsFalse(raft.Body.isKinematic);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0.2f, 0f), raft.Position), 1e-4f);

            Mover again = raft.BeginDrive();
            Assert.AreSame(mover, again, "the prop keeps one mover for all its drives");
            Game.Context.RemoveProp(raft);
            CollectionAssert.DoesNotContain(Game.Movers, mover);
            Assert.IsNull(raft.BeginDrive(), "a removed prop cannot be driven");

            Prop fixedProp = Game.Context.AddProp(BasicToys.Block(1f), new Vector3(5f, 0.5f, 5f), new PropOptions { Body = PropBody.Fixed });
            Assert.Throws<InvalidOperationException>(() => fixedProp.BeginDrive(), "only Dynamic props are lent out");
        }

        // ------------------------------------------------------------------------------------------
        // LEVELS 0.4, request 7: the bot's slow radius goes with the player's scale
        // ------------------------------------------------------------------------------------------

        float ThrottleAt(float playerScale, float distance)
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsTrue(Game.Player.SetScale(playerScale));
            var bot = new Bot(Game);
            IEnumerator walk = bot.WalkTo(new Vector3(0f, 0f, distance), 0.02f);
            Assert.IsTrue(walk.MoveNext());
            InputFrame frame = bot.Sample();
            Game.Dispose();
            Game = null;
            return new Vector2(frame.MoveX, frame.MoveZ).magnitude;
        }

        [Test]
        public void Bot_SlowRadiusScalesWithThePlayer()
        {
            Assert.AreEqual(1f, ThrottleAt(1f, 1.2f), 1e-4f, "at normal size 1.2 units is outside the slow radius of " + Bot.SlowRadius);
            Assert.AreEqual(1.2f / (Bot.SlowRadius * 3f), ThrottleAt(3f, 1.2f), 1e-4f, "three times the size, three times the radius");
            Assert.AreEqual(0.1f / (Bot.SlowRadius * 0.3f), ThrottleAt(0.3f, 0.1f), 1e-4f, "and a shrunken player only eases off close in");
            Assert.AreEqual(1f, ThrottleAt(0.3f, 0.3f), 1e-4f);
        }

        [Test]
        public void Bot_AShrunkenPlayerArrivesWithinATightTolerance()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsTrue(Game.Player.SetScale(0.3f));
            Run(5);
            var bot = new Bot(Game);
            BotRunner.Run(Game, Script(bot), 20f);
            Assert.Less(Vector2.Distance(new Vector2(Game.Player.Position.x, Game.Player.Position.z), new Vector2(1f, 2f)), 0.03f);
        }

        static IEnumerator Script(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(1f, 0f, 2f), 0.02f);
            yield return bot.Wait(0.2f);
        }
    }
}
