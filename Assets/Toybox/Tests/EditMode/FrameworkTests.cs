using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toybox.Tests
{
    public class FrameworkTests : SimTest
    {
        // Objects in the scenes that are open in the editor. The simulation must never leave anything there.
        static int ObjectsInOpenScenes() =>
            UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;

        [Test]
        public void TriggerFiresEnterAndExitForThePlayer()
        {
            Trigger trigger = null;
            var log = new List<string>();
            var events = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                trigger = ctx.AddTrigger("gate", Volume.Box(2f, 3f, 2f), new Vector3(0f, 1.5f, 6f));
                trigger.OnEnter += e => log.Add("enter:" + (e.IsPlayer ? "player" : e.Prop.Name));
                trigger.OnExit += e => log.Add("exit:" + (e.IsPlayer ? "player" : e.Prop.Name));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.TriggerEntered += e => events.Add("enter:" + e.Trigger.Name + ":" + e.IsPlayer);
            Game.Events.TriggerExited += e => events.Add("exit:" + e.Trigger.Name + ":" + e.IsPlayer);
            Run(20);
            Assert.IsFalse(trigger.PlayerInside);
            Assert.IsEmpty(log);

            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => trigger.PlayerInside, 3f), "the player never entered");
            Assert.AreEqual(new[] { "enter:player" }, log.ToArray());
            // The capsule (radius 0.3) touches the volume's near face at z = 5.
            Assert.AreEqual(4.7f, Game.Player.Position.z, 0.15f);

            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !trigger.PlayerInside, 3f), "the player never left");
            Assert.AreEqual(new[] { "enter:player", "exit:player" }, log.ToArray());
            Assert.AreEqual(new[] { "enter:gate:True", "exit:gate:True" }, events.ToArray());
            RunSeconds(0.5f);
            Assert.AreEqual(2, log.Count, "enter and exit fire once each");
        }

        [Test]
        public void TriggerFiresEnterAndExitForProps()
        {
            Trigger trigger = null;
            Prop ball = null, block = null;
            var log = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                trigger = ctx.AddTrigger("pad", Volume.Sphere(1f), new Vector3(5f, 0.5f, 5f));
                trigger.OnEnter += e => log.Add("enter:" + (e.IsPlayer ? "player" : e.Prop.Name));
                trigger.OnExit += e => log.Add("exit:" + (e.IsPlayer ? "player" : e.Prop.Name));
                ball = ctx.AddProp(BasicToys.Ball(0.3f), new Vector3(5f, 3f, 5f), new PropOptions { Name = "ball" });
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(-5f, 0.25f, 5f), new PropOptions { Name = "block" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsEmpty(trigger.PropsInside);

            // The ball falls into the volume.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => trigger.Contains(ball), 2f), "the ball never entered");
            Assert.AreEqual(new[] { "enter:ball" }, log.ToArray());

            block.SetPose(new Vector3(5.5f, 0.25f, 5f), Quaternion.identity);
            Run(2);
            Assert.AreEqual(new[] { "enter:ball", "enter:block" }, log.ToArray());
            Assert.AreEqual(new[] { ball, block }, new List<Prop>(trigger.PropsInside).ToArray(), "ordered by prop id");
            Assert.IsTrue(trigger.Occupied);
            Assert.IsFalse(trigger.PlayerInside);

            ball.SetPose(new Vector3(-8f, 0.3f, -8f), Quaternion.identity);
            Run(2);
            Assert.AreEqual(new[] { "enter:ball", "enter:block", "exit:ball" }, log.ToArray());
            Assert.AreEqual(1, trigger.PropsInside.Count);

            // Disabling empties it.
            trigger.Enabled = false;
            Run(1);
            Assert.AreEqual("exit:block", log[log.Count - 1]);
            Assert.IsFalse(trigger.Occupied);
        }

        [Test]
        public void LockedExitDoesNotCompleteAndUnlockedCompletesExactlyOnce()
        {
            Exit exit = null;
            int completed = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                exit = ctx.AddExit(new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
                exit.Lock();
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.LevelCompleted += e => completed++;

            RunSeconds(1f);
            Assert.IsTrue(exit.Trigger.PlayerInside);
            Assert.IsFalse(Game.LevelCompleted, "a locked exit must not complete the level");
            Assert.AreEqual(0, completed);

            exit.Unlock();
            Game.Tick();
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);

            RunSeconds(1f);
            Game.CompleteLevel();
            Assert.AreEqual(1, completed, "LevelCompleted fired more than once");
        }

        [Test]
        public void ReloadingALevelLeavesNothingBehind()
        {
            int disposed = 0;
            int updates = 0;
            Action<LevelContext> build = ctx =>
            {
                TestHelpers.Room(ctx, 10f);
                for (int i = 0; i < 5; i++)
                    ctx.AddProp(BasicToys.Block(0.5f), new Vector3(i - 2f, 0.25f, 4f));
                ctx.AddProp(BasicToys.Wedge(1f, 1f, 1f), new Vector3(0f, 0.5f, 6f));
                ctx.AddKinematic(BasicToys.Slab(new Vector3(2f, 0.5f, 2f)), new Vector3(5f, 2f, 5f));
                ctx.AddTrigger("t", Volume.Sphere(1f), new Vector3(0f, 1f, 0f));
                ctx.AddExit(new Vector3(8f, 1.5f, 8f), new Vector3(2f, 3f, 2f));
                ctx.OnUpdate(dt => updates++);
                ctx.OnDispose(() => disposed++);
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            };
            int outside = ObjectsInOpenScenes();
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            var level = new AdHocLevel(build);

            Game.LoadLevel(level);
            RunSeconds(0.5f);
            int transforms = Game.Root.GetComponentsInChildren<Transform>(true).Length;
            int colliders = Game.Root.GetComponentsInChildren<Collider>(true).Length;
            int bodies = Game.Root.GetComponentsInChildren<Rigidbody>(true).Length;
            Scene firstScene = Game.Scene;
            Assert.AreEqual(1, firstScene.rootCount, "everything hangs under Game.Root");
            Assert.AreEqual(outside, ObjectsInOpenScenes(), "building the level left objects in the open scene");
            Transform firstRoot = Game.LevelRoot;
            Prop firstProp = Game.Props[0];
            Assert.AreEqual(6, Game.Props.Count);
            Assert.AreEqual(2, Game.Triggers.Count);
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(1, Game.Movers.Count);

            // Grab something so a held prop is part of what has to be cleaned up.
            LookAt(Game.Props[2].Center);
            Click();
            Assert.IsNotNull(Game.Grabber.Held);
            updates = 0;

            Game.LoadLevel(level);
            Assert.AreEqual(1, disposed, "OnDispose hooks run on unload");
            Assert.IsTrue(firstRoot == null, "the first level's root still exists");
            Assert.IsTrue(firstProp.Removed);
            Assert.IsTrue(firstProp.GameObject == null);
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(0f, Game.Time);
            Assert.AreEqual(6, Game.Props.Count);
            Assert.AreEqual(2, Game.Triggers.Count);
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(1, Game.Movers.Count);
            Assert.AreEqual(transforms, Game.Root.GetComponentsInChildren<Transform>(true).Length);
            Assert.AreEqual(colliders, Game.Root.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(bodies, Game.Root.GetComponentsInChildren<Rigidbody>(true).Length);
            Assert.AreNotEqual(firstScene, Game.Scene, "every load gets a fresh scene");
            Assert.IsFalse(firstScene.IsValid() && firstScene.isLoaded, "the first load's scene is still open");
            Assert.AreEqual(1, Game.Scene.rootCount);
            Assert.AreEqual(outside, ObjectsInOpenScenes());

            Run(10);
            Assert.AreEqual(10, updates, "only the new level's update hook may run");
            Assert.AreEqual(2, level.BuildCount);
        }

        [Test]
        public void RestartRebuildsTheLevelAndReseeds()
        {
            Prop block = null;
            var rolls = new List<float>();
            var events = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                rolls.Add(ctx.Rng.Value());
                ctx.SetSpawn(new Vector3(1f, 0f, 1f), 0f);
            });
            Game.Events.LevelRestarted += e => events.Add("restarted");
            Game.Events.LevelLoaded += e => events.Add("loaded");
            Prop original = block;

            Input.Hold.MoveZ = 1f;
            RunSeconds(1f);
            Assert.Greater(Game.Player.Position.z, 3f);
            Game.Rng.Value();

            Input.Hold = default;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreEqual(new[] { "restarted", "loaded" }, events.ToArray());
            Assert.AreNotSame(original, block);
            Assert.IsTrue(original.Removed);
            Assert.AreEqual(1, Game.Props.Count);
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 1f), Game.Player.Position), 0.05f);
            Assert.AreEqual(Sim.Dt, Game.Time, 1e-6f, "time restarts with the level");
            Assert.AreEqual(2, rolls.Count);
            Assert.AreEqual(rolls[0], rolls[1], "the level's random stream is reseeded on every load");
        }

        [Test]
        public void DisposeRestoresGlobalPhysicsSettings()
        {
            SimulationMode mode = Physics.simulationMode;
            Vector3 gravity = Physics.gravity;
            int iterations = Physics.defaultSolverIterations;
            int velocityIterations = Physics.defaultSolverVelocityIterations;
            float depenetration = Physics.defaultMaxDepenetrationVelocity;
            var matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
                for (int b = 0; b < 32; b++)
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
            int objects = ObjectsInOpenScenes();

            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f));
            });
            Scene scene = Game.Scene;
            Assert.IsTrue(Game.PhysicsScene.IsValid());
            Assert.AreNotEqual(Physics.defaultPhysicsScene, Game.PhysicsScene, "the simulation has a physics scene of its own");
            Assert.AreSame(Game, Game.Current);
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);
            Assert.AreEqual(new Vector3(0f, -22f, 0f), Physics.gravity);
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Default));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Prop));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Player));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.Trigger, Layers.Player));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.Prop, Layers.Player));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.Prop, Layers.Default));
            Assert.Throws<InvalidOperationException>(() => Game.Create(), "a second Game must be refused");
            Run(5);

            Game.Dispose();
            Assert.IsNull(Game.Current);
            Assert.IsTrue(Game.IsDisposed);
            Assert.AreEqual(mode, Physics.simulationMode);
            Assert.AreEqual(gravity, Physics.gravity);
            Assert.AreEqual(iterations, Physics.defaultSolverIterations);
            Assert.AreEqual(velocityIterations, Physics.defaultSolverVelocityIterations);
            Assert.AreEqual(depenetration, Physics.defaultMaxDepenetrationVelocity);
            for (int a = 0; a < 32; a++)
                for (int b = 0; b < 32; b++)
                    Assert.AreEqual(matrix[a, b], Physics.GetIgnoreLayerCollision(a, b), "layer pair " + a + "/" + b);
            Assert.AreEqual(objects, ObjectsInOpenScenes(), "Dispose left GameObjects behind");
            Assert.IsFalse(scene.IsValid() && scene.isLoaded, "Dispose left the simulation's scene open");
            Assert.Throws<ObjectDisposedException>(() => Game.Tick());
            Game.Dispose();

            // And a new Game can be created right away.
            Game = Game.Create();
            Assert.AreSame(Game, Game.Current);
        }

        [Test]
        public void ALevelThatFailsToBuildIsUnloaded()
        {
            Game = Game.Create();
            var broken = new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f));
                throw new InvalidOperationException("level bug");
            });
            Assert.Throws<InvalidOperationException>(() => Game.LoadLevel(broken));
            Assert.IsNull(Game.Level);
            Assert.IsNull(Game.LevelRoot);
            Assert.AreEqual(0, Game.Props.Count);
            Assert.AreEqual(1, Game.Root.transform.childCount, "only the player remains");

            // The Game is still usable.
            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Run(30);
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void ANonIsolatedGameLivesInTheOpenScene()
        {
            int objects = ObjectsInOpenScenes();
            Prop block = null;
            Game = Game.Create(new GameOptions { Isolated = false });
            Game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 3f, 3f));
            }));
            Assert.AreEqual(Physics.defaultPhysicsScene, Game.PhysicsScene);
            Assert.AreEqual(SceneManager.GetActiveScene(), Game.Scene);
            Assert.AreEqual(Game.Scene, Game.Root.scene);
            Assert.Greater(ObjectsInOpenScenes(), objects);

            RunSeconds(2f);
            Assert.IsTrue(Game.Player.Grounded, "the player should have landed on the floor");
            Assert.AreEqual(0.5f, block.Center.y, 0.02f, "the block should have fallen onto the floor");

            Game.Dispose();
            Assert.AreEqual(objects, ObjectsInOpenScenes(), "Dispose left GameObjects in the open scene");
        }

        [Test]
        public void EventsRaisedDuringATickArriveAfterTheTick()
        {
            var order = new List<string>();
            Trigger trigger = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                trigger = ctx.AddTrigger("here", Volume.Sphere(2f), new Vector3(0f, 1f, 0f));
                trigger.OnEnter += e => order.Add("trigger callback at tick " + ctx.Game.LevelTicks);
                ctx.Say("built");
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.TriggerEntered += e => order.Add("event at tick " + Game.LevelTicks);
            Game.Events.Message += e => order.Add("message " + e.Text);
            Game.Tick();
            // The callback runs inside the tick (before the tick counter advances), the event after it.
            Assert.AreEqual(new[] { "trigger callback at tick 0", "event at tick 1" }, order.ToArray());

            order.Clear();
            Game.Context.Say("hello", 2f);
            Assert.AreEqual(new[] { "message hello" }, order.ToArray(), "outside a tick events are delivered immediately");
        }

        [Test]
        public void StepRunsDueTicksAndReturnsAlpha()
        {
            Build(ctx => TestHelpers.Floor(ctx));
            int start = Game.TickCount;

            float alpha = Game.Step(Sim.Dt * 0.5f);
            Assert.AreEqual(start, Game.TickCount, "half a step is not due yet");
            Assert.AreEqual(0.5f, alpha, 1e-3f);

            alpha = Game.Step(Sim.Dt * 0.75f);
            Assert.AreEqual(start + 1, Game.TickCount);
            Assert.AreEqual(0.25f, alpha, 1e-3f);

            alpha = Game.Step(Sim.Dt * 2f);
            Assert.AreEqual(start + 3, Game.TickCount);
            Assert.AreEqual(0.25f, alpha, 1e-3f);

            // A long stall is not replayed in full.
            alpha = Game.Step(5f);
            Assert.AreEqual(start + 3 + 4, Game.TickCount, "catch-up is capped at MaxCatchUpTicks");
            Assert.That(alpha, Is.InRange(0f, 1f));
        }

        [Test]
        public void LevelRegistryFindsLevelsSortedById()
        {
            Assert.Greater(LevelRegistry.All.Count, 0);
            for (int i = 1; i < LevelRegistry.All.Count; i++)
                Assert.Less(LevelRegistry.All[i - 1].Id, LevelRegistry.All[i].Id);
            LevelDefinition sandbox = LevelRegistry.Get(0);
            Assert.AreEqual(0, sandbox.Id);
            Assert.AreEqual("sandbox", sandbox.Slug);
            Assert.AreNotSame(sandbox, LevelRegistry.Get(0), "Get returns a fresh instance");
            Assert.Throws<ArgumentException>(() => LevelRegistry.Get(-12345));
        }

        [Test]
        public void RngIsDeterministicPerSeed()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            var c = new Rng(43);
            bool differs = false;
            for (int i = 0; i < 100; i++)
            {
                float value = a.Value();
                Assert.AreEqual(value, b.Value());
                Assert.That(value, Is.InRange(0f, 0.99999994f));
                if (value != c.Value()) differs = true;
            }
            Assert.IsTrue(differs);
            for (int i = 0; i < 100; i++)
                Assert.That(a.Range(3, 7), Is.InRange(3, 6));
        }
    }
}
