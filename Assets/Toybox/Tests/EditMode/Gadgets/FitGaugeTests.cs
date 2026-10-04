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
    }
}
