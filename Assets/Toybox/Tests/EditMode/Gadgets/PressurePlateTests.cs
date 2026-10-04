using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The pressure plate weighs what is on it; mass goes with scale cubed, so size decides.</summary>
    public class PressurePlateTests : SimTest
    {
        static PropOptions Crate(float scale, string name = "Crate") =>
            new PropOptions { Name = name, Scale = scale, Density = 1f, Tags = new[] { "weight" } };

        [Test]
        public void AProp_LatchesThePlate_OnlyAboveTheMassThreshold()
        {
            PressurePlate plate = null;
            Prop light = null, heavy = null;
            var rejected = new List<float>();
            int pressed = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                plate = new PressurePlate(ctx, new PressurePlateOptions
                {
                    Sensor = Zone.Cylinder(0f, 5f, 1.2f, 0f, 1.5f), MinMass = 1f, Mode = PlateMode.Heaviest, Settle = 0.2f, Latch = true,
                });
                plate.OnPressed += p => pressed++;
                plate.OnRejected += (p, load) => rejected.Add(load);
                // A unit cube of density 1 weighs scale cubed: 0.51 at 0.8, 1.73 at 1.2.
                light = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 1.2f, 5f), Crate(0.8f, "Light"));
                ctx.SetSpawn(new Vector3(0f, 0f, -5f), 0f);
            });

            RunSeconds(2f);
            Assert.IsFalse(plate.Pressed, "0.51 does not press a plate that wants 1");
            Assert.AreEqual(1, rejected.Count, "a settled prop that is too light is rejected exactly once");
            Assert.AreEqual(0.512f, rejected[0], 0.01f);
            Assert.AreEqual(0.512f, plate.Load01, 0.01f, "the cap travels in proportion");

            Game.Context.RemoveProp(light);
            heavy = Game.Context.AddProp(BasicToys.Block(1f), new Vector3(0f, 1.5f, 5f), Crate(1.2f, "Heavy"));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => plate.Pressed, 3f), "1.73 presses it");
            Assert.AreSame(heavy, plate.PressedBy);
            Assert.AreEqual(1, pressed);

            // Latched: taking the weight away changes nothing.
            Game.Context.RemoveProp(heavy);
            RunSeconds(1f);
            Assert.IsTrue(plate.Pressed, "a latched plate stays down");
            Assert.AreEqual(1, pressed);
        }

        [Test]
        public void SumAddsUp_HeaviestDoesNot()
        {
            PressurePlate sum = null, heaviest = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                sum = new PressurePlate(ctx, new PressurePlateOptions { Name = "sum", Sensor = Zone.Box(new Vector3(-5f, 1f, 0f), new Vector3(4f, 2f, 4f)), MinMass = 1f, Mode = PlateMode.Sum });
                heaviest = new PressurePlate(ctx, new PressurePlateOptions { Name = "heaviest", Sensor = Zone.Box(new Vector3(5f, 1f, 0f), new Vector3(4f, 2f, 4f)), MinMass = 1f, Mode = PlateMode.Heaviest });
                for (int side = -1; side <= 1; side += 2)
                {
                    // Two crates of 0.61 each: 1.23 together.
                    ctx.AddProp(BasicToys.Block(1f), new Vector3(side * 5f - 0.7f, 0.45f, 0f), Crate(0.85f));
                    ctx.AddProp(BasicToys.Block(1f), new Vector3(side * 5f + 0.7f, 0.45f, 0f), Crate(0.85f));
                }
                ctx.SetSpawn(new Vector3(0f, 0f, -8f), 0f);
            });
            RunSeconds(1f);
            Assert.IsTrue(sum.Pressed, "two crates of 0.61 press a plate of 1 that sums");
            Assert.AreEqual(1.228f, sum.Load, 0.02f);
            Assert.IsFalse(heaviest.Pressed, "but not one that only takes the heaviest");
            Assert.AreEqual(0.614f, heaviest.Load, 0.02f);
        }

        [Test]
        public void ThePlayerIsIgnored_UnlessThePlateIsAPedal()
        {
            PressurePlate button = null, pedal = null;
            int releases = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                button = new PressurePlate(ctx, new PressurePlateOptions { Name = "button", Sensor = Zone.Cylinder(0f, 3f, 0.8f, 0f, 1f), MinMass = 0.04f });
                pedal = new PressurePlate(ctx, new PressurePlateOptions { Name = "pedal", Sensor = Zone.Cylinder(0f, 6f, 0.8f, 0f, 1f), MinMass = 2f, AcceptPlayer = true });
                pedal.OnReleased += () => releases++;
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            var bot = new Bot(Game);
            int pressedAt = -1, leftAt = -1, releasedAt = -1;
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 3f), 0.2f);
                yield return bot.Wait(0.5f);
                Assert.IsFalse(button.Pressed, "the player cannot press a plate that does not accept them");
                yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.2f);
                yield return bot.Until(() => pedal.Pressed, 1f);
                pressedAt = Game.LevelTicks;
                Assert.IsNull(pedal.PressedBy, "pressed by the player, not by a prop");
                yield return bot.WalkTo(new Vector3(0f, 0f, 9f), 0.2f);
                leftAt = Game.LevelTicks;
                yield return bot.Until(() => !pedal.Pressed, 2f);
                releasedAt = Game.LevelTicks;
            }
            BotRunner.Run(Game, Script(), 30f);
            Assert.Greater(pressedAt, 0);
            Assert.AreEqual(1, releases);
            Assert.LessOrEqual((releasedAt - leftAt) * Sim.Dt, 0.5f, "it comes back up soon after the player has left");
        }

        [Test]
        public void TheBot_GrowsACrate_UntilItIsHeavyEnough()
        {
            // The mechanic and the plate together: the same crate is rejected small and accepted big.
            PressurePlate plate = null;
            Prop crate = null;
            int rejections = 0;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 12f, 10f);
                plate = new PressurePlate(ctx, new PressurePlateOptions
                {
                    Sensor = Zone.Box(new Vector3(0f, 2f, 9f), new Vector3(6f, 4f, 6f)), MinMass = 1f, Settle = 0.2f, Latch = true, LockProp = true,
                });
                plate.OnRejected += (p, load) => rejections++;
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.2f, 2f), Crate(0.4f));
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            var bot = new Bot(Game);
            float firstScale = 0f;
            IEnumerator Script()
            {
                // Carried to the plate and put down at the feet it stays small: 0.064 or less.
                yield return bot.Grab(crate);
                yield return bot.WalkTo(new Vector3(0f, 0f, 7.5f));
                yield return bot.DropAt(new Vector3(0f, 0f, 8.3f));
                yield return bot.Until(() => rejections > 0, 3f);
                firstScale = crate.Scale;
                Assert.IsFalse(plate.Pressed);
                // Picked up from close and pressed against the far wall it grows past scale 1 (mass 1).
                yield return bot.Grab(crate);
                yield return bot.WalkTo(new Vector3(0f, 0f, 1f));
                yield return bot.DropAt(new Vector3(0f, 2.5f, 12f));
                yield return bot.Until(() => plate.Pressed, 4f);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 40f);
            Assert.Less(firstScale * firstScale * firstScale, 1f);
            Assert.GreaterOrEqual(crate.Mass, 1f, "the crate that pressed it weighs at least MinMass");
            Assert.AreSame(crate, plate.LockedProp);
            Assert.IsFalse(crate.Grabbable, "a locked prop never serves twice");
            Assert.IsTrue(crate.Driven);
            Assert.AreEqual(0f, crate.Center.x, 0.02f, "eased to the middle of the plate");
            Assert.AreEqual(9f, crate.Center.z, 0.02f);

            plate.Reset();
            Assert.IsFalse(plate.Pressed);
            Assert.IsTrue(crate.Grabbable, "Reset lets go of it");
            Assert.IsFalse(crate.Driven);
        }

        [Test]
        public void PressAndRelease_ReachTheGameEvents_ForTheButtonSound()
        {
            PressurePlate plate = null;
            Prop crate = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                plate = new PressurePlate(ctx, new PressurePlateOptions { Sensor = Zone.Cylinder(0f, 5f, 1f, 0f, 1.5f), MinMass = 0.5f, Debounce = 0.1f });
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f), Crate(1f));
                ctx.SetSpawn(new Vector3(0f, 0f, -5f), 0f);
            });
            var log = new List<string>();
            Game.Events.PlatePressed += e => log.Add("pressed " + (e.Prop != null ? e.Prop.Name : "player") + " at " + e.Position.z.ToString("0"));
            Game.Events.PlateReleased += e => log.Add("released");
            RunSeconds(0.5f);
            Game.Context.RemoveProp(crate);
            RunSeconds(0.5f);
            CollectionAssert.AreEqual(new[] { "pressed Crate at 5", "released" }, log);
        }
    }
}
