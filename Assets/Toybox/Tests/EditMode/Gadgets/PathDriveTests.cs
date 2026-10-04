using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>A body on rails: every leg is a closed form of the time since Start.</summary>
    public class PathDriveTests : SimTest
    {
        [Test]
        public void TheBallRun_OfLevel15_TakesTheTimesTheSpecComputes()
        {
            PathDrive run = null;
            Prop ball = null;
            var ended = new List<string>();
            bool finished = false;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 120f);
                // Seated in the hopper at scale 3: its lowest point at the top of the chute.
                ball = ToyCatalog.Add(ctx, ToyId.BouncyBall, new Vector3(-18f, 6f + 1.5f, 18.25f), 3f, null, o => o.FrozenUntilGrabbed = true);
                run = new PathDrive(ctx, new PathDriveOptions
                {
                    Name = "ball run", Body = ball, Offset = Vector3.up * 1.5f, EndVelocity = new Vector3(0f, -4f, 0f),
                    Segments = new[]
                    {
                        PathSegment.Hold(0.3f),                                                                 // the gate lifts
                        PathSegment.Roll(new Vector3(-14.5f, 3.55f, 18.25f), 9.01f),                           // down the chute: (5/7) g sin 35
                        PathSegment.Ballistic(new Vector3(7.19f, -5.03f, 0f), 1.2f),                           // off the lip onto the drum
                        PathSegment.Ballistic(new Vector3(7.19f, 9.98f, 0f), 1.2f),                            // the bounce, over the books
                    },
                });
                run.SegmentEnded += i => ended.Add(i + "@" + ((ctx.Ticks + 1) * Sim.Dt).ToString("0.00"));
                run.Finished += () => finished = true;
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.IsTrue(run.Start());
            Assert.IsFalse(run.Start(), "it is already running");
            Assert.IsTrue(ball.Driven);
            Assert.AreEqual(0.3f, run.Segments[0].Duration, 1e-4f);
            Assert.AreEqual(0.97f, run.Segments[1].Duration, 0.01f, "LEVELS: 4.27 long at 9.01: 0.97 s");
            Assert.AreEqual(8.77f, run.Segments[1].Velocity(run.Segments[1].Duration).magnitude, 0.03f, "leaves the lip at 8.77");
            Assert.AreEqual(0.29f, run.Segments[2].Duration, 0.01f, "falls 2.35 to the drum in 0.29 s");
            Assert.AreEqual(-12.44f, run.Segments[2].End.x, 0.03f, "lands at X -12.44");
            Assert.AreEqual(-11.3f, run.Segments[2].Velocity(run.Segments[2].Duration).y, 0.1f, "with vy -11.3");
            Assert.AreEqual(0.907f, run.Segments[3].Duration, 0.01f, "0.92 s over the books");
            Assert.AreEqual(-5.92f, run.Segments[3].End.x, 0.08f, "onto the short arm at X -5.85");
            float apex = 1.2f + 9.98f * 9.98f / 44f;
            Assert.AreEqual(apex, run.PointAt(0.3f + run.Segments[1].Duration + run.Segments[2].Duration + 9.98f / 22f).y, 0.01f, "clearing the books (2.4) by about one");

            float highest = 0f;
            int ticks = 0;
            while (run.Running && ticks++ < 600)
            {
                Game.Tick();
                highest = Mathf.Max(highest, ball.Center.y - 1.5f);
            }
            Assert.IsTrue(finished);
            Assert.AreEqual(run.Duration + Sim.Dt, ticks * Sim.Dt, 0.04f, "the run took what its legs add up to (" + run.Duration + ")");
            Assert.AreEqual(4, ended.Count);
            StringAssert.StartsWith("0@0.30", ended[0]);
            StringAssert.StartsWith("1@1.2", ended[1]);
            Assert.Greater(highest, apex - 0.05f);
            Assert.IsFalse(ball.Driven, "handed back to physics");
            Assert.AreEqual(-4f, ball.Velocity.y, 0.5f, "with the end velocity");
            Assert.AreEqual(-5.92f, ball.Center.x, 0.1f);
            Assert.AreEqual(18.25f, ball.Center.z, 1e-3f);
        }

        [Test]
        public void EaseHoldAndSpline_MoveAKinematicBody_ExactlyOnTime()
        {
            PathDrive drive = null;
            Mover lift = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                lift = ctx.AddKinematic(BasicToys.Slab(new Vector3(2f, 0.4f, 2f), Palette.Lagoon), new Vector3(5f, 1f, 5f));
                drive = new PathDrive(ctx, new PathDriveOptions
                {
                    Mover = lift,
                    Segments = new[]
                    {
                        PathSegment.Ease(new Vector3(5f, 5f, 5f), 1f),
                        PathSegment.Hold(0.5f),
                        PathSegment.Spline(new[] { new Vector3(7f, 6f, 5f), new Vector3(9f, 5f, 5f) }, 1f),
                    },
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.IsTrue(drive.Start());
            Assert.AreEqual(2.5f, drive.Duration, 1e-4f);
            RunSeconds(0.5f);
            Assert.AreEqual(3f, lift.Position.y, 1e-3f, "half way after half the time");
            RunSeconds(0.5f);
            Assert.AreEqual(5f, lift.Position.y, 1e-3f);
            Assert.AreEqual(1, drive.Segment);
            RunSeconds(0.5f);
            Assert.Less(Vector3.Distance(new Vector3(5f, 5f, 5f), lift.Position), 1e-3f, "holding");
            RunSeconds(0.5f);
            Assert.Less(Vector3.Distance(new Vector3(7f, 6f, 5f), lift.Position), 1e-3f, "the spline passes through its points");
            RunSeconds(0.6f);
            Assert.IsFalse(drive.Running);
            Assert.Less(Vector3.Distance(new Vector3(9f, 5f, 5f), lift.Position), 1e-3f, "and ends on the last");
            Assert.AreEqual(3, drive.Segment);
        }

        [Test]
        public void AGrab_EndsTheDrive_AndAbortHandsTheBodyBackMoving()
        {
            PathDrive drive = null;
            Prop crate = null;
            bool finished = false;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 6f), new PropOptions { Name = "Crate" });
                drive = new PathDrive(ctx, new PathDriveOptions { Body = crate, Segments = new[] { PathSegment.Roll(new Vector3(0f, 0.5f, 30f), 0f, 4f) } });
                drive.Finished += () => finished = true;
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            drive.Start();
            RunSeconds(0.5f);
            Assert.AreEqual(8f, crate.Center.z, 0.01f, "rolling at 4 units a second");
            LookAt(crate.Center);
            Click();
            Assert.AreSame(crate, Game.Grabber.Held);
            Run(2);
            Assert.IsFalse(drive.Running, "taken off its rails");
            Assert.IsFalse(finished, "which is not finishing");
            Click();
            RunSeconds(0.5f);

            Assert.IsTrue(drive.Start(), "it can be started again, from wherever the body is now");
            RunSeconds(0.25f);
            drive.Abort();
            Assert.IsFalse(crate.Driven);
            Assert.AreEqual(4f, crate.Velocity.magnitude, 0.01f, "let go at the speed it had");
        }

        [Test]
        public void GhostToPlayer_LetsADrivenBodyPassThroughThePlayer()
        {
            PathDrive drive = null;
            Prop boulder = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                boulder = ctx.AddProp(BasicToys.Ball(1f), new Vector3(0f, 1f, 10f), new PropOptions { Name = "Boulder", Density = 5f });
                drive = new PathDrive(ctx, new PathDriveOptions
                {
                    Body = boulder, GhostToPlayer = true, Segments = new[] { PathSegment.Roll(new Vector3(0f, 1f, -10f), 0f, 8f) },
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            drive.Start();
            float farthest = 0f;
            while (drive.Running)
            {
                Game.Tick();
                farthest = Mathf.Max(farthest, new Vector2(Game.Player.Position.x, Game.Player.Position.z).magnitude);
            }
            Assert.Less(farthest, 0.02f, "the machine's parts do not shove the player around (moved " + farthest + ")");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.02f);
            Assert.Less(boulder.Center.z, -9.5f, "the boulder went straight through");
        }
    }
}
