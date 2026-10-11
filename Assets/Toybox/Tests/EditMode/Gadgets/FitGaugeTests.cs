using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The gauge reads the held toy's true size against a window: the one sanctioned cue for it.</summary>
    public class FitGaugeTests : SimTest
    {
        [Test]
        public void WhileAToyIsHeldNear_TheGaugeSaysHowItWouldFit()
        {
            FitGauge gauge = null;
            Socket socket = null;
            Prop block = null;
            var states = new List<FitState>();
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f, 12f);
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "plug", Capture = Zone.Sphere(new Vector3(0f, -50f, 0f), 1f), MinScale = 1f, MaxScale = 2f,
                    SeatPose = s => new Pose(Vector3.zero, Quaternion.identity),
                });
                gauge = new FitGauge(ctx, new FitGaugeOptions
                {
                    Target = socket, Tag = "plug", Near = Zone.MinMax(new Vector3(-20f, 0f, 2f), new Vector3(20f, 12f, 20f)), Lamp = new Vector3(0f, 5f, 19f),
                });
                gauge.Changed += states.Add;
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 1.5f, 2f), new PropOptions { Name = "Block", Scale = 0.5f, Tags = new[] { "plug" }, MaxScale = 30f });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(0.2f);
            Assert.AreEqual(FitState.Idle, gauge.State, "nothing is held");

            // Grabbed 2 away at scale 0.5: k = 0.25.
            LookAt(block.Center);
            Click();
            Assert.AreSame(block, Game.Grabber.Held);

            // Straight down at the floor right ahead: tiny, and not near the target.
            LookAt(new Vector3(0f, 0f, 1.5f));
            Run(2);
            Assert.AreEqual(FitState.Idle, gauge.State, "held, but not inside the zone of the gauge");

            // At the floor 6 ahead: about 1.2.
            LookAt(new Vector3(0f, 0f, 6f));
            Run(2);
            Assert.AreEqual(FitState.Good, gauge.State, "scale " + block.Scale);
            Assert.AreEqual(block.Scale, gauge.Scale, 1e-4f);

            // At the floor 3.6 ahead: too small. At the far wall: too big.
            LookAt(new Vector3(0f, 0f, 3.6f));
            Run(2);
            Assert.AreEqual(FitState.TooSmall, gauge.State, "scale " + block.Scale);
            LookAt(new Vector3(0f, 3f, 20f));
            Run(2);
            Assert.AreEqual(FitState.TooBig, gauge.State, "scale " + block.Scale);

            Click();
            Run(2);
            Assert.AreEqual(FitState.Idle, gauge.State, "let go: a gauge only speaks about what is in the hand");
            CollectionAssert.AreEqual(new[] { FitState.TooSmall, FitState.Idle, FitState.Good, FitState.TooSmall, FitState.TooBig, FitState.Idle }, states,
                "too small as it is picked up, idle while held away from the target, then the three verdicts, idle again when let go");
        }

        [Test]
        public void WithoutASocket_ItJudgesAgainstAScaleWindow()
        {
            FitGauge gauge = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                gauge = new FitGauge(ctx, new FitGaugeOptions { MinScale = 0.5f, MaxScale = 0.76f, Near = Zone.Sphere(Vector3.zero, 5f) });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.AreEqual(FitState.TooSmall, gauge.Fit(0.4f));
            Assert.AreEqual(FitState.Good, gauge.Fit(0.64f));
            Assert.AreEqual(FitState.TooBig, gauge.Fit(0.8f));
        }

        // A window that ends where the toy's own limit is (Level 15's hopper: the ball grows to 3, the hopper
        // takes up to 3). Stopped by its limit the held toy used to be ratio * (3 / ratio), which in 32 bits
        // is 3.00000024 in one pick-up of twenty: the gauge blinked "too big" at a toy the station takes
        // (the final review, browser-build-1: 15 of 300 pick-ups here). A held toy is never over its limit
        // (a hair under it, it may be: that fits).
        [Test]
        public void AToyHeldAtItsOwnLimit_ReadsGood_WhereTheWindowEndsAtThatLimit([Values(3f, 3.5f, 1.2f)] float limit)
        {
            int atTheLimit = 0;
            for (int k = 0; k < 300; k++)
            {
                float distance = 1.2f + k * 0.0371f;
                FitGauge gauge = null;
                Prop ball = null;
                Game?.Dispose();
                Game = null;
                Build(ctx =>
                {
                    TestHelpers.Floor(ctx, 400f);
                    ball = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(0f, 0.3f, distance), new PropOptions { Scale = 0.6f, MinScale = 0.6f, MaxScale = limit, FrozenUntilGrabbed = true });
                    gauge = new FitGauge(ctx, new FitGaugeOptions { MinScale = limit * 0.75f, MaxScale = limit, Near = Zone.Box(new Vector3(0f, 100f, 0f), new Vector3(1000f, 400f, 1000f)) });
                    ctx.SetSpawn(Vector3.zero, 0f);
                });
                Run(2);
                LookAt(ball.Center);
                Click();
                Assert.AreSame(ball, Game.Grabber.Held, "picked up from " + distance);
                // Up into the open air, where nothing but its own limit stops it.
                Game.Player.Pitch = 50f;
                Run(3);
                string what = "picked up from " + distance.ToString("R") + ": looks " + Game.Grabber.Ratio.ToString("R") + ", is " + ball.Scale.ToString("R");
                Assert.Less(Game.Grabber.HoldDistance, PerspectiveGrabber.MaxHoldDistance - 1f, "stopped by its limit - " + what);
                Assert.That(ball.Scale, Is.InRange(limit - 1e-5f, limit), what);
                Assert.AreEqual(FitState.Good, gauge.State, what);
                atTheLimit++;
            }
            Assert.AreEqual(300, atTheLimit);
        }

        // The same in Level 15 itself, where it was seen: the ball (limit 3, hopper window to 3) and the
        // dominoes (limit 3.5, lane window to 3.5), each picked up from forty places two centimetres apart
        // and held from the solver's stand at the solver's aim. Before: "too big" for 4 of the 40 balls.
        [Test]
        public void InLevel15_AToyHeldAtItsLimit_IsNeverTooBigForItsStation([Values(0, 1)] int toy)
        {
            int atTheLimit = 0;
            for (int k = 0; k < 40; k++)
            {
                Game?.Dispose();
                Game = null;
                Input = new ScriptedInput();
                Game = Game.Create(new GameOptions { Input = Input });
                Game.LoadLevel(15);
                var level = (Toybox.Levels.Level15RubeGoldberg)Game.Level;
                Prop prop = toy == 0 ? level.Ball : level.Dominoes;
                Vector3 from = toy == 0 ? Toybox.Levels.Level15RubeGoldberg.BallPickup : Toybox.Levels.Level15RubeGoldberg.SpawnPoint;
                Vector3 stand = toy == 0 ? Toybox.Levels.Level15RubeGoldberg.BallStand : Toybox.Levels.Level15RubeGoldberg.DominoStand;
                Vector3 aim = toy == 0 ? Toybox.Levels.Level15RubeGoldberg.BallAim : Toybox.Levels.Level15RubeGoldberg.DominoAim;
                FitGauge gauge = level.Gauges[toy == 0 ? 0 : 3];
                float limit = prop.MaxScale;
                Assert.AreEqual(limit, toy == 0 ? Toybox.Levels.Level15RubeGoldberg.HopperMaxScale : Toybox.Levels.Level15RubeGoldberg.LaneMaxScale,
                    "the station's window ends at the toy's own limit: that is what this test is about");

                Run(2);
                // (A fixture's shortcut for the walk: a step back or forward of where the solver picks it up.)
                Game.Player.Teleport(from + new Vector3(0f, 0f, -0.4f + 0.02f * k));
                Run(2);
                LookAt(prop.Center);
                Click();
                if (Game.Grabber.Held != prop) continue;
                Game.Player.Teleport(stand);
                LookAt(aim);
                Run(12);
                string what = "pick-up " + k + ": looks " + Game.Grabber.Ratio.ToString("R") + ", is " + prop.Scale.ToString("R") + " of " + limit.ToString("R");
                Assert.LessOrEqual(prop.Scale, limit, what);
                Assert.AreNotEqual(FitState.TooBig, gauge.State, what);
                if (prop.Scale > limit - 1e-5f)
                {
                    atTheLimit++;
                    Assert.AreEqual(FitState.Good, gauge.State, what);
                }
            }
            Assert.Greater(atTheLimit, 4, "the toy was held at its limit often enough to tell");
        }
    }
}
