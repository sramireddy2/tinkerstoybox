using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The conductor of a chain reaction: a run goes as far as it can, says where it stopped, and sets itself up again.</summary>
    public class MachineDirectorTests : SimTest
    {
        /// <summary>A link that counts how often the machine reset it.</summary>
        sealed class CountingLink : Gadget
        {
            readonly List<string> log;
            public CountingLink(LevelContext ctx, string name, List<string> log) : base(ctx, name) => this.log = log;
            protected override void Tick(float dt) { }

            public override void Reset()
            {
                base.Reset();
                log.Add("reset " + Name);
            }
        }

        MachineDirector director;
        PressurePlate pedal;
        Prop ball, fan;
        readonly List<string> log = new List<string>();

        void BuildMachine(LevelContext ctx)
        {
            log.Clear();
            TestHelpers.Floor(ctx);
            pedal = new PressurePlate(ctx, new PressurePlateOptions { Name = "pedal", Sensor = Zone.Cylinder(0f, 3f, 0.7f, 0f, 1f), MinMass = 2f, AcceptPlayer = true });
            ball = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(-4f, 0.5f, 8f), new PropOptions { Name = "Ball" });
            fan = ctx.AddProp(BasicToys.Block(1f), new Vector3(4f, 0.5f, 8f), new PropOptions { Name = "Fan", Grabbable = false });
            var gate = new CountingLink(ctx, "gate", log);
            var run = new CountingLink(ctx, "ball run", log);
            director = new MachineDirector(ctx, new MachineDirectorOptions
            {
                Start = pedal, Toys = new[] { ball, fan }, Links = new[] { "gate", "ball", "marble", "clock" }, Resets = new Gadget[] { gate, run },
                FizzleTimeout = 2.5f, ResetSeconds = 1.5f,
                Messages = new Dictionary<string, string>
                {
                    { "", "Nothing happens at all." },
                    { "gate", "The gate opens. Nothing rolls." },
                    { "ball", "The ball lands on a bare pivot." },
                },
            });
            director.MachineStarted += () => log.Add("started");
            director.MachineFizzled += link => log.Add("fizzled at " + (link ?? "nothing"));
            director.MachineReset += () => log.Add("machine reset");
            director.MachineDone += () => log.Add("done");
            ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
        }

        void StepOnThePedal()
        {
            Game.Player.Teleport(new Vector3(0f, 0f, 3f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => pedal.Pressed, 1f));
        }

        void StepOff()
        {
            Game.Player.Teleport(new Vector3(0f, 0f, 0f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !pedal.Pressed, 1f));
        }

        [Test]
        public void ARunThatStops_SaysWhere_ResetsItsLinks_AndGivesTheToysBack()
        {
            Build(BuildMachine);
            var said = new List<string>();
            Game.Events.Message += m => said.Add(m.Text);
            int fizzles = 0;
            Game.Events.MachineFizzled += e => fizzles++;
            Assert.AreEqual(MachineState.Idle, director.State);

            StepOnThePedal();
            Assert.AreEqual(MachineState.Running, director.State);
            Assert.AreEqual(1, director.Runs);
            Assert.IsFalse(ball.Grabbable, "the toys are locked while the machine runs");

            // The gate link fires, then nothing else does.
            RunSeconds(0.3f);
            director.Report("gate");
            RunSeconds(2.4f);
            Assert.AreEqual(MachineState.Running, director.State, "2.4 s after the last report: still waiting");
            RunSeconds(0.2f);
            Assert.AreEqual(MachineState.Resetting, director.State, "2.5 s without a report: fizzled");
            Assert.AreEqual("gate", director.LastLink);
            CollectionAssert.AreEqual(new[] { "The gate opens. Nothing rolls." }, said);
            Assert.AreEqual("The gate opens. Nothing rolls.", director.LastMessage);
            Assert.AreEqual(1, fizzles);
            CollectionAssert.AreEqual(new[] { "started", "fizzled at gate", "reset gate", "reset ball run" }, log, "every link is reset, in the order given");
            Assert.IsFalse(ball.Grabbable, "still locked while it sets itself up");

            // A press during the reset is ignored.
            StepOff();
            StepOnThePedal();
            Assert.AreEqual(1, director.Runs);

            RunSeconds(1.5f);
            Assert.AreEqual(MachineState.Idle, director.State);
            Assert.AreEqual("machine reset", log[log.Count - 1]);
            Assert.IsTrue(ball.Grabbable, "the toys are the player's again");
            Assert.IsFalse(fan.Grabbable, "and one that was not grabbable before is not now");
        }

        [Test]
        public void ARunThatReachesItsLastLink_IsDone()
        {
            Build(BuildMachine);
            StepOnThePedal();
            foreach (string link in new[] { "gate", "ball", "marble" })
            {
                RunSeconds(1f);
                director.Report(link);
                Assert.AreEqual(MachineState.Running, director.State);
            }
            RunSeconds(2f);
            director.Report("clock");
            Assert.AreEqual(MachineState.Done, director.State);
            CollectionAssert.AreEqual(new[] { "gate", "ball", "marble", "clock" }, director.Fired);
            CollectionAssert.AreEqual(new[] { "started", "done" }, log);
            RunSeconds(5f);
            Assert.AreEqual(MachineState.Done, director.State, "done is done: no fizzle afterwards");
            StepOff();
            StepOnThePedal();
            Assert.AreEqual(1, director.Runs, "and the pedal does nothing more");
        }

        [Test]
        public void AnEmptyMachine_Fizzles_AndCanBeRunAgain_AndALinkCanFailOutright()
        {
            Build(BuildMachine);
            StepOnThePedal();
            RunSeconds(2.6f);
            Assert.AreEqual(MachineState.Resetting, director.State);
            Assert.AreEqual("Nothing happens at all.", director.LastMessage);
            Assert.IsNull(director.LastLink);
            StepOff();
            RunSeconds(1.6f);
            Assert.AreEqual(MachineState.Idle, director.State);

            StepOnThePedal();
            Assert.AreEqual(2, director.Runs, "never a softlock: it runs again");
            director.Report("gate");
            director.Report("ball");
            RunSeconds(0.5f);
            director.Fail("The marble falls short. The ball isn't heavy enough.");
            Assert.AreEqual(MachineState.Resetting, director.State, "a link that knows it failed does not wait for the timeout");
            Assert.AreEqual("The marble falls short. The ball isn't heavy enough.", director.LastMessage);
            Assert.AreEqual("ball", director.LastLink);
        }
    }
}
