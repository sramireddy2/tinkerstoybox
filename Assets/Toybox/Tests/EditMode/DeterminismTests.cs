using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    public class DeterminismTests : SimTest
    {
        const int ScenarioTicks = 600;

        // The player walks through a busy scene, jumps, and grabs and drops whatever is in front of them.
        static InputFrame Script(int tick)
        {
            var frame = new InputFrame();
            if (tick < 30) return frame;
            if (tick < 150)
            {
                frame.MoveZ = 1f;
                frame.Sprint = tick > 90;
            }
            else if (tick < 240)
            {
                frame.LookYaw = 1.5f;
                frame.MoveX = -0.5f;
                frame.MoveZ = 0.6f;
            }
            else if (tick < 400)
            {
                frame.LookYaw = -1f;
                frame.LookPitch = tick < 300 ? -0.2f : 0.25f;
                frame.MoveZ = tick % 50 < 30 ? 1f : 0f;
            }
            else
            {
                frame.MoveZ = 1f;
                frame.LookYaw = 0.4f;
            }
            frame.Jump = tick % 97 < 3;
            frame.GrabPressed = tick == 60 || tick == 200 || tick == 320 || tick == 420 || tick == 500;
            frame.RotateYaw = tick % 71 == 0 ? 1 : 0;
            return frame;
        }

        // A tower to knock over, loose toys placed by the level's Rng, and a moving platform.
        static AdHocLevel Scenario() => new AdHocLevel(ctx =>
        {
            TestHelpers.Room(ctx, 14f);
            for (int i = 0; i < 6; i++)
                ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 0.3f + i * 0.6f, 6f));
            for (int i = 0; i < 8; i++)
            {
                Vector2 spot = ctx.Rng.InsideUnitCircle() * 8f;
                float size = ctx.Rng.Range(0.3f, 0.9f);
                GameObject toy = i % 3 == 0 ? BasicToys.Ball(size * 0.5f)
                    : i % 3 == 1 ? BasicToys.Wedge(size * 1.5f, size, size)
                    : BasicToys.Cylinder(size * 0.5f, size);
                ctx.AddProp(toy, new Vector3(spot.x, 2f + i * 0.5f, spot.y + 2f), ctx.Rng.Rotation(),
                    new PropOptions { Bounciness = 0.3f });
            }
            Mover mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(3f, 0.4f, 3f)), new Vector3(-6f, 0.2f, -6f));
            ctx.OnUpdate(dt => mover.MoveTo(new Vector3(-6f + Mathf.Sin(ctx.Game.Time) * 3f, 0.2f, -6f)));
            ctx.SetSpawn(new Vector3(0f, 0f, -4f), 0f);
        });

        // Loads the scenario into the current Game, plays the script and returns everything that moved.
        List<float> PlayScenario()
        {
            Game.Input = new ScriptedInput { Script = Script };
            Game.LoadLevel(Scenario());
            TestHelpers.Run(Game, ScenarioTicks);

            var state = new List<float>();
            void Add(Vector3 v)
            {
                state.Add(v.x);
                state.Add(v.y);
                state.Add(v.z);
            }
            Add(Game.Player.Position);
            Add(Game.Player.Velocity);
            state.Add(Game.Player.Yaw);
            state.Add(Game.Player.Pitch);
            foreach (Prop prop in Game.Props)
            {
                Add(prop.Position);
                Quaternion q = prop.Rotation;
                state.Add(q.x);
                state.Add(q.y);
                state.Add(q.z);
                state.Add(q.w);
                state.Add(prop.Scale);
            }
            Add(Game.Movers[0].Position);
            state.Add(Game.Rng.Value());
            return state;
        }

        List<float> PlayScenarioInFreshGame()
        {
            Game = Game.Create(new GameOptions { Seed = 7 });
            List<float> state = PlayScenario();
            Game.Dispose();
            Game = null;
            return state;
        }

        static void AssertIdentical(List<float> expected, List<float> actual, string what)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.AreEqual(expected[i], actual[i], "state value " + i + " differs " + what);
        }

        [Test]
        public void SameScriptedRunTwiceGivesIdenticalTransforms()
        {
            List<float> first = PlayScenarioInFreshGame();
            List<float> second = PlayScenarioInFreshGame();
            AssertIdentical(first, second, "between two identical runs");
        }

        [Test]
        public void ARunDoesNotDependOnWhatWasPlayedBefore()
        {
            List<float> fresh = PlayScenarioInFreshGame();

            // The same Game first plays something else entirely, then the scenario, then restarts it.
            Game = Game.Create(new GameOptions { Seed = 7 });
            var input = new ScriptedInput();
            Game.Input = input;
            Game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx, 30f);
                for (int i = 0; i < 12; i++)
                    ctx.AddProp(BasicToys.Ball(0.3f), new Vector3(i % 4 - 1.5f, 1f + i, 3f), new PropOptions { Bounciness = 0.5f });
                ctx.SetSpawn(new Vector3(0f, 0f, -2f), 0f);
            }));
            input.Hold.MoveZ = 1f;
            input.Hold.LookYaw = 2f;
            TestHelpers.Run(Game, 200);
            Game.Player.SetScale(1.5f);
            TestHelpers.Run(Game, 20);

            AssertIdentical(fresh, PlayScenario(), "after another level was played first");
            AssertIdentical(fresh, PlayScenario(), "after the scenario itself was played before");
        }

        [Test]
        public void TheScenarioActuallyDoesSomething()
        {
            // Guards the tests above against passing because nothing moved.
            int grabs = 0, drops = 0, jumps = 0;
            Game = Game.Create(new GameOptions { Seed = 7 });
            Game.Events.PropGrabbed += e => grabs++;
            Game.Events.PropDropped += e => drops++;
            Game.Events.PlayerJumped += e => jumps++;
            var before = new List<Vector3>();
            Game.Events.LevelLoaded += e =>
            {
                foreach (Prop prop in Game.Props) before.Add(prop.Position);
            };
            PlayScenario();

            Assert.Greater(jumps, 2);
            Assert.Greater(grabs, 0, "the script should get hold of something");
            Assert.Greater(drops, 0);
            int moved = 0;
            for (int i = 0; i < before.Count; i++)
                if (Vector3.Distance(before[i], Game.Props[i].Position) > 0.5f) moved++;
            Assert.Greater(moved, 6, "most of the props should have been knocked about");
        }
    }
}
