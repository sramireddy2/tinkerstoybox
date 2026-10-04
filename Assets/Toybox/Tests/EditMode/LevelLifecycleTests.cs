using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// The level framework over a level's whole life (M1 review): a level written with the public API only
    /// and solved by its bot, loading and restarting, completion and exits, triggers and removed props,
    /// movers, events around a level switch, replays, and Game.Step. FrameworkTests has the basics.
    /// </summary>
    public class LevelLifecycleTests : SimTest
    {
        static int CountAll<T>() where T : UnityEngine.Object => Resources.FindObjectsOfTypeAll<T>().Length;

        // ------------------------------------------------------------------------------------------
        // A level written the way a level author would, with the public API only:
        // a trigger (pressure plate), a kinematic mover (ferry), a checkpoint and a locked exit.
        // ------------------------------------------------------------------------------------------

        [Level(901, "test-ferry", "Test Ferry")]
        sealed class FerryLevel : LevelDefinition
        {
            public Mover Ferry;
            public Trigger Plate;
            public Trigger Checkpoint;
            public Exit Exit;
            public Prop Crate;

            // A registered level stands in a room (sunny-rug by default). Its floor goes below the kill plane,
            // or it would close the gap the ferry crosses.
            public override float GroundY => -40f;

            public override void Build(LevelContext ctx)
            {
                // Near floor: z in [-6, 6]. Far floor: z in [16, 28]. The gap is 10 wide.
                ctx.AddStatic(BasicToys.Slab(new Vector3(12f, 1f, 12f)), new Vector3(0f, -0.5f, 0f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(12f, 1f, 12f)), new Vector3(0f, -0.5f, 22f));

                // The ferry's top is flush with the floors; it shuttles between the two edges.
                Ferry = ctx.AddKinematic(BasicToys.Slab(new Vector3(3f, 0.4f, 3f), Palette.Lagoon), new Vector3(0f, -0.2f, 7.5f));
                Mover ferry = Ferry;
                ctx.OnUpdate(dt =>
                {
                    float z = 11f - 3.5f * Mathf.Cos(ctx.Game.Time * 0.9f);
                    ferry.MoveTo(new Vector3(0f, -0.2f, z));
                });

                Crate = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(-3f, 0.3f, 21f), new PropOptions { Name = "Crate" });

                Exit = ctx.AddExit(new Vector3(0f, 1.5f, 27f), new Vector3(4f, 3f, 2f));
                Exit.Lock();
                Exit exit = Exit;

                Plate = ctx.AddTrigger("plate", Volume.Box(1.6f, 0.4f, 1.6f), new Vector3(3f, 0.2f, 21f));
                Plate.SensesPlayer = false;
                Trigger plate = Plate;
                plate.OnEnter += e => exit.Unlock();
                plate.OnExit += e =>
                {
                    if (plate.PropsInside.Count == 0) exit.Lock();
                };

                Checkpoint = ctx.AddCheckpoint(Volume.Box(12f, 4f, 2f), new Vector3(0f, 2f, 17.5f));
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                Game game = bot.Game;
                yield return bot.WalkTo(new Vector3(0f, 0f, 4.5f));
                // Wait for the ferry to dock on the near side, step on, ride across.
                yield return bot.Until(() => Ferry.Position.z < 7.6f, 10f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 7.5f), 0.4f);
                yield return bot.Until(() => Ferry.Position.z > 14.3f, 10f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 18f));
                yield return bot.Grab(Crate);
                yield return bot.DropAt(new Vector3(3f, 0.3f, 21f));
                yield return bot.Until(() => !Exit.Locked, 3f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 27f), 0.5f);
                yield return bot.Until(() => game.LevelCompleted, 3f);
            }
        }

        [Test]
        public void AuthoredLevel_FerryPlateCheckpointLockedExit_IsSolvedByItsBot()
        {
            var level = new FerryLevel();
            Game = Game.Create();
            Game.LoadLevel(level);
            var entered = new List<string>();
            Game.Events.TriggerEntered += e => entered.Add(e.Trigger.Name + ":" + (e.IsPlayer ? "player" : e.Prop.Name));

            TestHelpers.PlayLevel(Game, 60f);

            Assert.IsFalse(level.Exit.Locked);
            Assert.IsTrue(level.Plate.Contains(level.Crate), "the crate should rest on the plate");
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 17.5f), Game.Player.CheckpointPosition), 1e-4f,
                "the checkpoint on the far side should be the respawn point");
            CollectionAssert.Contains(entered, "Checkpoint:player");
            CollectionAssert.Contains(entered, "plate:Crate");
            CollectionAssert.Contains(entered, "Exit:player");
            CollectionAssert.DoesNotContain(entered, "plate:player");
        }

        [Test]
        public void AuthoredLevel_LockedExitHoldsUntilThePlateIsPressed_AndRelocksWhenTheCrateIsLifted()
        {
            var level = new FerryLevel();
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(level);

            // Stand in the exit while it is locked.
            Game.Player.Teleport(new Vector3(0f, 0f, 27f), 180f);
            RunSeconds(0.5f);
            Assert.IsTrue(level.Exit.Trigger.PlayerInside);
            Assert.IsTrue(level.Exit.Locked);
            Assert.IsFalse(Game.LevelCompleted);

            // Look at the crate and carry it onto the plate, without leaving the exit.
            LookAt(level.Crate.Center);
            Click();
            Assert.AreSame(level.Crate, Game.Grabber.Held);
            LookAt(new Vector3(3f, 0.3f, 21f));
            Run(2);
            Assert.IsFalse(level.Plate.Occupied, "a held prop must not press the plate");
            Assert.IsFalse(Game.LevelCompleted);
            Click();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f), "dropping the crate on the plate should unlock the exit");

            // Lifting it again locks the exit again (the level stays completed).
            LookAt(level.Crate.Center);
            Click();
            Assert.AreSame(level.Crate, Game.Grabber.Held);
            Run(2);
            Assert.IsTrue(level.Exit.Locked);
        }

        [Test]
        public void AuthoredLevel_FallingIntoTheGapAfterTheCheckpointRespawnsThere()
        {
            var level = new FerryLevel();
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(level);
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;

            Game.Player.Teleport(new Vector3(0f, 0f, 20f), 180f);
            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 6f), "the player should have walked off the edge and respawned");
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 17.5f), Game.Player.Position), 0.05f);
            Assert.AreEqual(180f, Mathf.Abs(Game.Player.Yaw), 0.01f, "respawn faces the way the player looked when touching the checkpoint");
        }

        // ------------------------------------------------------------------------------------------
        // Lifecycle: loading, restarting, completing, disposing
        // ------------------------------------------------------------------------------------------

        [Test]
        public void LoadingTheSameLevelTwentyTimesLeaksNothing()
        {
            Mover mover = null;
            int updates = 0, disposed = 0;
            var level = new AdHocLevel(ctx =>
            {
                TestHelpers.Room(ctx, 10f);
                for (int i = 0; i < 4; i++)
                    ctx.AddProp(BasicToys.Block(0.5f), new Vector3(i - 2f, 0.25f, 4f));
                ctx.AddProp(BasicToys.Wedge(1f, 1f, 1f), new Vector3(0f, 0.5f, 6f));
                ctx.AddProp(BasicToys.Cylinder(0.4f, 1f), new Vector3(3f, 0.5f, 6f));
                ctx.AddProp(BasicToys.Ball(0.4f), new Vector3(-3f, 0.4f, 6f), new PropOptions { Body = PropBody.Fixed });
                mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(2f, 0.5f, 2f)), new Vector3(5f, 2f, 5f));
                Mover m = mover;
                ctx.OnUpdate(dt =>
                {
                    updates++;
                    m.MoveTo(new Vector3(5f, 2f + Mathf.Sin(ctx.Game.Time), 5f));
                });
                ctx.OnDispose(() => disposed++);
                ctx.AddTrigger("t", Volume.Sphere(1f), new Vector3(0f, 1f, 0f));
                ctx.AddCheckpoint(Volume.Box(2f, 2f, 2f), new Vector3(-5f, 1f, -5f));
                ctx.AddExit(new Vector3(8f, 1.5f, 8f), new Vector3(2f, 3f, 2f));
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            });
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });

            void Play()
            {
                Game.LoadLevel(level);
                Run(5);
                LookAt(Game.Props[1].Center);
                Click();
                Assert.IsNotNull(Game.Grabber.Held);
                Run(3);
            }

            Play();
            Play();
            int gameObjects = CountAll<GameObject>();
            int physicsMaterials = CountAll<PhysicsMaterial>();
            int meshes = CountAll<Mesh>();
            int materials = CountAll<Material>();
            int bodies = CountAll<Rigidbody>();
            int colliders = CountAll<Collider>();
            int previewScenes = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            int underRoot = Game.Root.GetComponentsInChildren<Transform>(true).Length;

            for (int i = 0; i < 20; i++) Play();

            Assert.AreEqual(underRoot, Game.Root.GetComponentsInChildren<Transform>(true).Length, "transforms under Game.Root");
            Assert.AreEqual(gameObjects, CountAll<GameObject>(), "GameObjects");
            Assert.AreEqual(physicsMaterials, CountAll<PhysicsMaterial>(), "PhysicsMaterials");
            Assert.AreEqual(meshes, CountAll<Mesh>(), "Meshes");
            Assert.AreEqual(materials, CountAll<Material>(), "Materials");
            Assert.AreEqual(bodies, CountAll<Rigidbody>(), "Rigidbodies");
            Assert.AreEqual(colliders, CountAll<Collider>(), "Colliders");
            Assert.AreEqual(previewScenes, UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount, "preview scenes");
            Assert.AreEqual(22, level.BuildCount);
            Assert.AreEqual(21, disposed);
            Assert.AreEqual(7, Game.Props.Count);
            Assert.AreEqual(3, Game.Triggers.Count);
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(1, Game.Movers.Count);

            updates = 0;
            Run(10);
            Assert.AreEqual(10, updates, "exactly one update hook must be alive");
        }

        [Test]
        public void RestartWhileHoldingLeavesACleanLevel()
        {
            Prop crate = null;
            var held = new List<Prop>();
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 12f);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.PropHeld += e => held.Add(e.Prop);
            Prop first = crate;
            LookAt(crate.Center);
            Click();
            LookAt(new Vector3(4f, 0f, 9f));
            Run(5);
            Assert.AreSame(first, Game.Grabber.Held);
            Assert.Greater(first.Scale, 1.5f);

            held.Clear();
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(Game.Grabber.IsHolding);
            Assert.AreNotSame(first, crate);
            Assert.IsTrue(first.Removed);
            Assert.AreEqual(1f, crate.Scale);
            Assert.IsFalse(crate.Held);
            Assert.AreEqual(Layers.Prop, crate.GameObject.layer);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0.25f, 3f), crate.Center), 0.01f);
            Assert.IsEmpty(held, "PropHeld fired for a prop after the restart");
            Assert.AreEqual(0f, Game.Player.Yaw);
            Assert.AreEqual(0f, Game.Player.Pitch);

            // A restart and a click in the same frame: the click applies to the new level.
            LookAt(crate.Center);
            Input.Once.RestartPressed = true;
            Input.Once.GrabPressed = true;
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held, "the view was reset by the restart, so the click hits nothing");
            Run(30);
        }

        [Test]
        public void CompletingTwiceRaisesOnceAndARestartArmsItAgain()
        {
            Exit exit = null;
            var completions = new List<int>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                exit = ctx.AddExit(new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.LevelCompleted += e => completions.Add(e.Ticks);
            Run(30);
            Assert.AreEqual(1, completions.Count);
            Game.CompleteLevel();
            Game.Context.Complete();
            Assert.AreEqual(1, completions.Count);
            Assert.IsTrue(Game.LevelCompleted);

            Game.RestartLevel();
            Assert.IsFalse(Game.LevelCompleted);
            Run(3);
            Assert.AreEqual(2, completions.Count, "after a restart the level can be completed again");
        }

        [Test]
        public void ExitSemantics()
        {
            Exit exit = null;
            Prop crate = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                exit = ctx.AddExit(new Vector3(0f, 1.5f, 10f), new Vector3(3f, 3f, 3f));
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 10f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsFalse(exit.Locked, "exits start unlocked");
            Run(30);
            Assert.IsFalse(Game.LevelCompleted, "a prop inside the exit must not complete the level");
            Assert.IsEmpty(exit.Trigger.PropsInside);

            exit.Lock();
            Game.Player.Teleport(new Vector3(0f, 0f, 9f));
            Run(5);
            Assert.IsTrue(exit.Trigger.PlayerInside);
            Assert.IsFalse(Game.LevelCompleted);
            exit.Unlock();
            Assert.IsFalse(Game.LevelCompleted, "unlocking takes effect on the next tick");
            Run(1);
            Assert.IsTrue(Game.LevelCompleted);
            exit.Lock();
            Run(1);
            Assert.IsTrue(Game.LevelCompleted, "completion is not undone");
        }

        [Test]
        public void DisposeRestoresPhysicsSettingsAfterATickThrew()
        {
            SimulationMode mode = Physics.simulationMode;
            Vector3 gravity = Physics.gravity;
            int iterations = Physics.defaultSolverIterations;
            bool heldVsDefault = Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Default);
            int objects = CountAll<GameObject>();
            int scenes = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;

            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f));
                ctx.OnUpdate(dt =>
                {
                    if (ctx.Game.LevelTicks == 3) throw new InvalidOperationException("level bug");
                });
            });
            Assert.Throws<InvalidOperationException>(() => Run(10));
            Assert.AreEqual(3, Game.LevelTicks, "the throwing tick does not count");
            // The game is still usable after the exception...
            Assert.Throws<InvalidOperationException>(() => Game.Tick());
            // ...and disposing it puts everything back.
            Game.Dispose();
            Assert.IsNull(Game.Current);
            Assert.AreEqual(mode, Physics.simulationMode);
            Assert.AreEqual(gravity, Physics.gravity);
            Assert.AreEqual(iterations, Physics.defaultSolverIterations);
            Assert.AreEqual(heldVsDefault, Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Default));
            Assert.AreEqual(objects, CountAll<GameObject>());
            Assert.AreEqual(scenes, UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount);
        }

        [Test]
        public void EventsOfTheTickThatSwitchesLevelsArriveBeforeTheSwitch()
        {
            var problems = new List<string>();
            Prop crate = null;
            var next = new AdHocLevel(ctx => TestHelpers.Floor(ctx));
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f), new PropOptions { Name = "crate" });
                Trigger door = ctx.AddTrigger("door", Volume.Box(4f, 3f, 1f), new Vector3(0f, 1.5f, 8f));
                door.SensesProps = false;
                // A level that loads its successor from inside the tick (documented: takes effect when the tick ends).
                door.OnEnter += e => ctx.Game.LoadLevel(next);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.TriggerEntered += e =>
            {
                if (e.Trigger.GameObject == null) problems.Add("TriggerEntered(" + e.Trigger.Name + ") delivered after its trigger was destroyed");
            };
            Game.Events.PropHeld += e =>
            {
                if (e.Prop.Removed) problems.Add("PropHeld(" + e.Prop.Name + ") delivered after the prop was destroyed");
            };

            LookAt(crate.Center);
            Click();
            Assert.AreSame(crate, Game.Grabber.Held);
            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Level == next, 5f), "the door should have loaded the next level");
            Assert.IsEmpty(problems, string.Join("; ", problems));
        }

        [Test]
        public void ALevelsEventSubscriptionsEndWithTheLevel()
        {
            int drops = 0, grabs = 0, seenByPresentation = 0;
            Prop crate = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                // The only way for a level or gadget to hear about a drop, a landing or a respawn.
                ctx.Game.Events.PropDropped += e => drops++;
                // A gadget that starts listening later, from inside a tick.
                ctx.OnUpdate(dt =>
                {
                    if (ctx.Ticks == 2) ctx.Game.Events.PropGrabbed += e => grabs++;
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            // Presentation subscribes outside ticks and stays subscribed across levels.
            Game.Events.PropDropped += e => seenByPresentation++;
            Run(5);
            Game.RestartLevel();
            Run(5);
            Game.RestartLevel();
            Run(5);
            LookAt(crate.Center);
            Click();
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(1, drops, "handlers registered by the two previous builds of the level are still being called");
            Assert.AreEqual(1, grabs, "handlers registered during ticks of the two previous builds are still being called");
            Assert.AreEqual(1, seenByPresentation, "a subscription made outside a tick is not the level's and must survive restarts");
        }

        // ------------------------------------------------------------------------------------------
        // Triggers
        // ------------------------------------------------------------------------------------------

        [Test]
        public void TriggerSeesGrabDropTeleportAndRespawn()
        {
            Trigger pad = null;
            Prop crate = null;
            var log = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                pad = ctx.AddTrigger("pad", Volume.Box(2f, 2f, 2f), new Vector3(0f, 1f, 6f));
                pad.OnEnter += e => log.Add("enter:" + (e.IsPlayer ? "player" : e.Prop.Name));
                pad.OnExit += e => log.Add("exit:" + (e.IsPlayer ? "player" : e.Prop.Name));
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 6f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            }, -5f);
            Run(2);
            Assert.AreEqual(new[] { "enter:crate" }, log.ToArray());

            // Grabbing the prop takes it out of the trigger; carrying it around inside does nothing.
            LookAt(crate.Center);
            Click();
            Assert.AreSame(crate, Game.Grabber.Held);
            Assert.AreEqual(new[] { "enter:crate", "exit:crate" }, log.ToArray());
            Run(10);
            Assert.AreEqual(2, log.Count, "a held prop must not be sensed");
            Assert.IsFalse(pad.Occupied);

            // Dropping it there brings it back in.
            Click();
            Run(1);
            Assert.AreEqual("enter:crate", log[log.Count - 1]);
            Assert.AreEqual(3, log.Count);

            // Teleporting the player in and out.
            Game.Player.Teleport(new Vector3(0.8f, 0f, 6.8f));
            Run(1);
            Assert.AreEqual("enter:player", log[log.Count - 1]);
            Game.Player.Teleport(new Vector3(0f, 0f, -6f));
            Run(1);
            Assert.AreEqual("exit:player", log[log.Count - 1]);
            Assert.AreEqual(5, log.Count);

            // The prop falls out of the world and respawns inside the trigger: exit, then enter.
            crate.SetPose(new Vector3(60f, -4.9f, 0f), Quaternion.identity);
            Run(1);
            Assert.AreEqual("exit:crate", log[log.Count - 1]);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => pad.Contains(crate), 1f));
            Assert.AreEqual("enter:crate", log[log.Count - 1]);
            Assert.AreEqual(7, log.Count);
        }

        [Test]
        public void ARemovedPropLeavesEveryTriggerAtOnce()
        {
            Trigger pad = null;
            Prop crate = null, other = null;
            float weighed = -1f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                pad = ctx.AddTrigger("pad", Volume.Box(2f, 2f, 2f), new Vector3(0f, 1f, 6f));
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(-0.4f, 0.25f, 6f), new PropOptions { Name = "crate" });
                other = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0.4f, 0.25f, 6f), new PropOptions { Name = "other" });
                Trigger scale = pad;
                Prop victim = crate;
                // Gadget 1 consumes a prop; gadget 2 (a weighing plate) reads what is on it. Both are ordinary
                // update hooks; the order they were registered in must not matter.
                ctx.OnUpdate(dt =>
                {
                    if (ctx.Game.LevelTicks == 5) ctx.RemoveProp(victim);
                });
                ctx.OnUpdate(dt =>
                {
                    float total = 0f;
                    foreach (Prop prop in scale.PropsInside) total += prop.Mass;
                    weighed = total;
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.DoesNotThrow(() => Run(10), "Trigger.PropsInside handed a destroyed prop to level code");
            Assert.AreEqual(other.Mass, weighed, 1e-5f);
        }

        [Test]
        public void ATriggerParentedToARemovedPropGoesWithIt()
        {
            Prop magnet = null;
            Trigger field = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                magnet = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 6f), new PropOptions { Name = "magnet" });
                // Trigger.Transform: "Parent it to a moving object to make the trigger follow."
                field = ctx.AddTrigger("field", Volume.Sphere(2f), magnet.Center);
                field.Transform.SetParent(magnet.Transform, true);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(5);
            Assert.IsTrue(field.Contains(magnet));
            Game.Context.RemoveProp(magnet);
            Assert.DoesNotThrow(() => Run(1), "the tick after the prop was removed");
            Assert.DoesNotThrow(() => Run(1), "and every tick after that");
            Assert.IsFalse(field.Occupied);
        }

        [Test]
        public void RemovingAPropInsideATriggerFiresExitOnce()
        {
            Trigger pad = null;
            Prop crate = null;
            var log = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                pad = ctx.AddTrigger("pad", Volume.Box(2f, 2f, 2f), new Vector3(0f, 1f, 6f));
                pad.OnEnter += e => log.Add("enter:" + e.Prop.Name);
                pad.OnExit += e => log.Add("exit:" + e.Prop.Name + ":" + e.Prop.Removed);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 6f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(2);
            Game.Context.RemoveProp(crate);
            Assert.AreEqual(0, Game.Props.Count);
            Run(3);
            Assert.AreEqual(new[] { "enter:crate", "exit:crate:True" }, log.ToArray());
            Assert.IsFalse(pad.Occupied);
        }

        // ------------------------------------------------------------------------------------------
        // Kinematic movers
        // ------------------------------------------------------------------------------------------

        [Test]
        public void MoverCarriesASleepingPropAndATriggerParentedToItFollows()
        {
            Mover mover = null;
            Prop crate = null;
            Trigger deck = null;
            float startAt = 4f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(4f, 0.4f, 4f)), new Vector3(0f, 1.2f, 8f));
                Mover m = mover;
                crate = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 1.7f, 8f), new PropOptions { Name = "crate" });
                deck = ctx.AddTrigger("deck", Volume.Box(4f, 1f, 4f), new Vector3(0f, 1.9f, 8f));
                deck.Transform.SetParent(mover.Transform, true);
                ctx.OnUpdate(dt =>
                {
                    // Whole ticks, so the float rounding of Game.Time cannot drop the last step.
                    int tick = ctx.Game.LevelTicks - Mathf.RoundToInt(startAt * 60f);
                    if (tick > 0 && tick <= 180) m.MoveTo(new Vector3(2f * tick * Sim.Dt, 1.2f, 8f));
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            RunSeconds(3.9f);
            Assert.IsTrue(crate.Body.IsSleeping(), "the crate should have gone to sleep on the resting platform");
            Assert.IsTrue(deck.Contains(crate));
            Assert.AreEqual(Vector3.zero, mover.Velocity);

            RunSeconds(1.6f);
            Assert.AreEqual(2f, mover.Velocity.x, 1e-3f, "the mover reports the velocity its MoveTo implies");
            RunSeconds(2f);
            Assert.AreEqual(6f, mover.Position.x, 1e-3f);
            Assert.AreEqual(Vector3.zero, mover.Velocity, "a tick without MoveTo means rest");
            Assert.AreEqual(6f, crate.Center.x, 0.25f, "the crate should have ridden the platform");
            Assert.AreEqual(6f, deck.Transform.position.x, 1e-3f, "a trigger parented to the mover follows it");
            Assert.IsTrue(deck.Contains(crate));
        }

        // ------------------------------------------------------------------------------------------
        // Replays
        // ------------------------------------------------------------------------------------------

        struct SolveResult
        {
            public int CompletedAtTick;
            public int TicksRun;
            public Vector3 Player;
            public Vector3 PlankPosition;
            public Quaternion PlankRotation;
            public float PlankScale;
        }

        SolveResult SolveSandbox()
        {
            int completedAt = -1;
            Action<LevelEvent> handler = e => completedAt = e.Ticks;
            Game.Events.LevelCompleted += handler;
            var solver = new Bot(Game);
            BotRunner.Run(Game, Game.Level.Solve(solver), 60f);
            Game.Events.LevelCompleted -= handler;
            Prop plank = Game.Props[0];
            return new SolveResult
            {
                CompletedAtTick = completedAt,
                TicksRun = Game.LevelTicks,
                Player = Game.Player.Position,
                PlankPosition = plank.Position,
                PlankRotation = plank.Rotation,
                PlankScale = plank.Scale,
            };
        }

        static void AssertSame(SolveResult a, SolveResult b, string what)
        {
            Assert.AreEqual(a.CompletedAtTick, b.CompletedAtTick, "completion tick " + what);
            Assert.AreEqual(a.TicksRun, b.TicksRun, "ticks run " + what);
            Assert.IsTrue(a.Player.Equals(b.Player), "player position " + what + ": " + a.Player.ToString("R") + " vs " + b.Player.ToString("R"));
            Assert.IsTrue(a.PlankPosition.Equals(b.PlankPosition), "plank position " + what);
            Assert.IsTrue(a.PlankRotation.Equals(b.PlankRotation), "plank rotation " + what);
            Assert.AreEqual(a.PlankScale, b.PlankScale, "plank scale " + what);
        }

        [Test]
        public void TheSandboxSolutionReplaysIdenticallyAfterARestartAndInAFreshGame()
        {
            Game = Game.Create();
            Game.LoadLevel(0);
            SolveResult first = SolveSandbox();
            Assert.Greater(first.CompletedAtTick, 0, "the sandbox solver should complete the level");

            Game.RestartLevel();
            Assert.AreEqual(0, Game.LevelTicks);
            SolveResult afterRestart = SolveSandbox();
            AssertSame(first, afterRestart, "after a restart");

            // Play something else in between, then come back.
            Game.LoadLevel(new FerryLevel());
            TestHelpers.PlayLevel(Game, 60f);
            Game.LoadLevel(0);
            SolveResult afterAnotherLevel = SolveSandbox();
            AssertSame(first, afterAnotherLevel, "after another level");

            Game.Dispose();
            Game = Game.Create();
            Game.LoadLevel(0);
            SolveResult fresh = SolveSandbox();
            AssertSame(first, fresh, "in a fresh Game");
        }

        [Test]
        public void ALevelWithMoverTriggerAndExitReplaysIdentically()
        {
            int Solve(out Vector3 crateAt)
            {
                int completedAt = -1;
                Action<LevelEvent> handler = e => completedAt = e.Ticks;
                Game.Events.LevelCompleted += handler;
                var solver = new Bot(Game);
                BotRunner.Run(Game, Game.Level.Solve(solver), 60f);
                Game.Events.LevelCompleted -= handler;
                crateAt = ((FerryLevel)Game.Level).Crate.Position;
                return completedAt;
            }

            Game = Game.Create();
            Game.LoadLevel(new FerryLevel());
            int first = Solve(out Vector3 firstCrate);
            Assert.Greater(first, 0);
            Game.RestartLevel();
            int second = Solve(out Vector3 secondCrate);
            Game.Dispose();
            Game = Game.Create();
            Game.LoadLevel(0);
            TestHelpers.Run(Game, 100);
            Game.LoadLevel(new FerryLevel());
            int third = Solve(out Vector3 thirdCrate);

            Assert.AreEqual(first, second, "after a restart");
            Assert.AreEqual(first, third, "in a fresh Game after another level");
            Assert.IsTrue(firstCrate.Equals(secondCrate) && firstCrate.Equals(thirdCrate), "the crate ended somewhere else");
        }

        [Test]
        public void PropsSpawnedAndRemovedDuringTicksKeepTheListsConsistent()
        {
            Trigger sink = null;
            int spawned = 0, removed = 0;
            var level = new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A dispenser drops a ball every 10 ticks; a sink in the floor removes whatever touches it.
                sink = ctx.AddTrigger("sink", Volume.Box(4f, 0.4f, 4f), new Vector3(0f, 0.2f, 6f));
                sink.SensesPlayer = false;
                sink.OnEnter += e =>
                {
                    ctx.RemoveProp(e.Prop);
                    removed++;
                };
                ctx.OnUpdate(dt =>
                {
                    if (ctx.Game.LevelTicks % 10 != 0) return;
                    ctx.AddProp(BasicToys.Ball(0.2f), new Vector3(ctx.Rng.Range(-1f, 1f), 3f, 6f + ctx.Rng.Range(-1f, 1f)));
                    spawned++;
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(level);
            Run(20);
            int gameObjects = CountAll<GameObject>() - Game.Props.Count * 2;
            int materials = CountAll<PhysicsMaterial>() - Game.Props.Count;

            Run(600);
            Assert.Greater(removed, 50);
            Assert.AreEqual(spawned - removed, Game.Props.Count, "live props");
            foreach (Prop prop in Game.Props) Assert.IsFalse(prop.Removed, "a removed prop is still listed after the tick");
            Assert.IsEmpty(sink.PropsInside);
            var ids = new HashSet<int>();
            foreach (Prop prop in Game.Props) Assert.IsTrue(ids.Add(prop.Id), "duplicate prop id");
            Assert.AreEqual(gameObjects, CountAll<GameObject>() - Game.Props.Count * 2, "GameObjects leaked by removed props");
            Assert.AreEqual(materials, CountAll<PhysicsMaterial>() - Game.Props.Count, "PhysicsMaterials leaked by removed props");

            Game.LoadLevel(level);
            Assert.AreEqual(0, Game.Props.Count);
        }

        [Test]
        public void TheRandomStreamDependsOnlyOnSeedAndLevelId()
        {
            var rolls = new List<float>();
            Action<LevelContext> build = ctx =>
            {
                TestHelpers.Floor(ctx);
                for (int i = 0; i < 4; i++) rolls.Add(ctx.Rng.Value());
            };
            Game = Game.Create(new GameOptions { Seed = 5 });
            Game.LoadLevel(new AdHocLevel(build));
            for (int i = 0; i < 37; i++) Game.Rng.NextUInt();
            TestHelpers.Run(Game, 10);
            Game.LoadLevel(new AdHocLevel(build));
            Game.LoadLevel(0);
            float sandboxRoll = Game.Rng.Value();
            Game.LoadLevel(new AdHocLevel(build));
            Game.Dispose();

            Game = Game.Create(new GameOptions { Seed = 6 });
            Game.LoadLevel(new AdHocLevel(build));
            Game.LoadLevel(0);
            float sandboxRollOtherSeed = Game.Rng.Value();

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(rolls[i], rolls[4 + i], "second load, same seed");
                Assert.AreEqual(rolls[i], rolls[8 + i], "third load, after another level");
            }
            Assert.AreNotEqual(rolls[0], rolls[12], "another seed gives another stream");
            Assert.AreNotEqual(rolls[0], sandboxRoll, "another level id gives another stream");
            Assert.AreNotEqual(sandboxRoll, sandboxRollOtherSeed);
        }

        [Test]
        public void LoadingAnUnknownLevelIdKeepsTheCurrentLevel()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
            });
            LevelDefinition level = Game.Level;
            Run(5);
            Assert.Throws<ArgumentException>(() => Game.LoadLevel(12345));
            Assert.AreSame(level, Game.Level);
            Assert.AreEqual(1, Game.Props.Count);
            Assert.AreEqual(5, Game.LevelTicks);
            Run(5);
        }

        [Test]
        public void ARestartRequestedInsideATickHappensWhenTheTickEnds()
        {
            var events = new List<string>();
            AdHocLevel level = null;
            Prop crate = null;
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            level = new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(3f, 0.25f, 3f), new PropOptions { Name = "crate" });
                Trigger laser = ctx.AddTrigger("laser", Volume.Box(6f, 3f, 0.2f), new Vector3(0f, 1.5f, 5f));
                laser.SensesProps = false;
                laser.OnEnter += e => ctx.Game.RestartLevel();
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.LoadLevel(level);
            Game.Events.LevelRestarted += e => events.Add("restarted@" + e.Ticks);
            Game.Events.LevelLoaded += e => events.Add("loaded@" + e.Ticks);
            Prop first = crate;

            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.BuildCount == 2, 3f), "the laser should have restarted the level");
            Assert.AreEqual(2, events.Count);
            StringAssert.StartsWith("restarted@", events[0]);
            Assert.AreEqual("loaded@0", events[1]);
            Assert.AreEqual(0, Game.LevelTicks);
            Assert.IsTrue(first.Removed);
            Assert.AreNotSame(first, crate);
            Assert.Less(Game.Player.Position.magnitude, 0.05f, "back at the spawn");
            Assert.AreEqual(Vector3.zero, Game.Player.Velocity);
            Input.Hold = default;
            Run(30);
            Assert.AreEqual(2, level.BuildCount, "exactly one restart");
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void AdvancingToTheNextLevelFromTheCompletionEventWorksDuringStep()
        {
            var order = new List<string>();
            var second = new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(new Vector3(5f, 0f, 5f), 90f);
            });
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddExit(new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.LevelCompleted += e =>
            {
                order.Add("completed");
                Game.LoadLevel(second);
            };
            Game.Events.LevelLoaded += e => order.Add("loaded:" + (e.Level == second ? "second" : "other"));
            Game.Events.TriggerEntered += e => order.Add("entered:" + e.Trigger.Name + ":" + (e.Trigger.GameObject != null ? "alive" : "destroyed"));

            int before = Game.TickCount;
            Assert.DoesNotThrow(() => Game.Step(Sim.Dt * 3f));
            Assert.AreEqual(3, Game.TickCount - before);
            Assert.AreSame(second, Game.Level);
            Assert.AreEqual(2, Game.LevelTicks, "the two remaining catch-up ticks ran in the new level");
            Assert.AreEqual(new[] { "entered:Exit:alive", "completed", "loaded:second" }, order.ToArray());
            Assert.Less(Vector3.Distance(new Vector3(5f, 0f, 5f), Game.Player.Position), 0.05f);
        }

        [Test]
        public void AnAttachedScriptPlaysLikeARunScript()
        {
            Game = Game.Create();
            Game.LoadLevel(0);
            SolveResult run = SolveSandbox();

            Game.RestartLevel();
            int completedAt = -1;
            float scaleAtCompletion = 0f;
            Game.Events.LevelCompleted += e =>
            {
                completedAt = e.Ticks;
                scaleAtCompletion = Game.Props[0].Scale;
            };
            var solver = new Bot(Game);
            BotRunner runner = BotRunner.Attach(Game, solver, Game.Level.Solve(solver));
            Assert.AreSame(runner, Game.Input);
            for (int i = 0; i < 3600 && !runner.Finished; i++) Game.Step(Sim.Dt);
            Assert.IsTrue(runner.Finished);
            Assert.AreEqual(run.CompletedAtTick, completedAt, "autoplay (Attach + Step) must replay the test run (Run) tick for tick");
            Assert.AreEqual(run.PlankScale, scaleAtCompletion);
        }

        [Test]
        public void RespawningInsideAPropLeftOnTheSpawnDoesNotTrapThePlayer()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 10f), new PropOptions { Name = "block" });
                ctx.SetSpawn(Vector3.zero, 0f);
            }, -5f);
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Run(10);
            // The player made the block big and left it standing on the spawn point...
            block.SetScale(4f);
            block.SetPose(new Vector3(0f, 2f, 0f), Quaternion.identity);
            Game.Player.Teleport(new Vector3(8f, 0f, 8f));
            Run(30);
            // ...and later fell out of the world.
            Game.Player.Teleport(new Vector3(8f, -6f, 8f));
            Run(1);
            Assert.AreEqual(1, respawns);

            // A block that heavy would squeeze the player out at many times their running speed. It lets
            // them through instead (the same rule as for a prop let go around them): they stand inside it.
            float lowest = float.MaxValue, fastest = 0f;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            }
            Assert.Greater(lowest, -0.2f, "the player was pushed into the floor");
            Assert.Less(fastest, Player.SprintSpeed, "the player was shot out of the block");
            Assert.AreEqual(1, respawns, "the player kept falling out of the world after respawning inside the block");
            Assert.IsTrue(Game.Player.Grounded, "the player should be standing somewhere");

            // And they simply walk out of it. After that it is solid for them again.
            Input.Hold.MoveZ = 1f;
            RunSeconds(1.5f);
            Input.Hold = default;
            Vector3 p = Game.Player.Position;
            bool insideBlock = Mathf.Abs(p.x - block.Center.x) < 1.9f && Mathf.Abs(p.z - block.Center.z) < 1.9f && p.y < block.Center.y + 1.9f && p.y > -0.5f;
            Assert.IsFalse(insideBlock, "the player is stuck inside the block (at " + p.ToString("F2") + ")");
            Assert.IsFalse(Physics.GetIgnoreCollision(block.Colliders[0], Game.Player.Collider), "once outside, the block collides with the player again");
            Game.Player.Yaw = 180f;
            Input.Hold.MoveZ = 1f;
            RunSeconds(2f);
            Assert.Greater(Game.Player.Position.z, block.Center.z + 2f, "the player walked back into the block");
            Assert.AreEqual(1, respawns);
        }

        // ------------------------------------------------------------------------------------------
        // Authoring surface
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ExitsChainAndAnnounceTheirLock()
        {
            Exit first = null, second = null;
            var changes = new List<string>();
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.Events.ExitLockChanged += e => changes.Add(e.Exit.Name + (e.Locked ? ":locked" : ":unlocked"));
            Game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                first = ctx.AddExit(new Vector3(0f, 1.5f, 10f), new Vector3(2f, 3f, 2f), "Gate").Lock();
                second = ctx.AddExit(new Vector3(8f, 1.5f, 10f), new Vector3(2f, 3f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }));
            Assert.IsTrue(first.Locked);
            Assert.AreEqual("Gate", first.Name);
            Assert.AreEqual("Gate", first.Trigger.Name);
            Assert.AreEqual("Exit", second.Name, "an exit without a name of its own is called Exit");
            Assert.IsFalse(second.Locked, "exits start unlocked");
            Assert.AreEqual(new[] { "Gate:locked" }, changes.ToArray());

            Assert.AreSame(first, first.Unlock());
            first.Unlock();
            first.Locked = false;
            Assert.AreEqual(new[] { "Gate:locked", "Gate:unlocked" }, changes.ToArray(), "only a change is announced");
            second.Lock();
            Assert.AreEqual("Exit:locked", changes[changes.Count - 1]);
        }

        [Test]
        public void ACheckpointRespawnsOnTheFloorWhereverItsVolumeSits()
        {
            Trigger standing = null, centred = null, floating = null, named = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A step: the floor is 0.5 higher beyond z = 20.
                TestHelpers.Box(ctx, new Vector3(0f, 0.25f, 30f), new Vector3(40f, 0.5f, 20f));
                // The volume stands on the floor; it is centered on the floor; it hangs in the air.
                standing = ctx.AddCheckpoint(Volume.Box(4f, 4f, 2f), new Vector3(0f, 2f, 6f));
                centred = ctx.AddCheckpoint(Volume.Box(4f, 4f, 2f), new Vector3(0f, 0.5f, 24f), "Upper");
                floating = ctx.AddCheckpoint(Volume.Sphere(1f), new Vector3(12f, 3f, 6f));
                named = ctx.AddCheckpoint(Volume.Box(2f, 2f, 2f), new Vector3(-12f, 1f, 6f), new Vector3(-12f, 0f, 9f), 90f, "Side");
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.AreEqual("Checkpoint", standing.Name);
            Assert.AreEqual("Upper", centred.Name);
            Assert.AreEqual("Side", named.Name);

            Game.Player.Teleport(new Vector3(0f, 0f, 6f));
            Run(2);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 6f), Game.Player.CheckpointPosition), 1e-3f);

            // Centered on the (higher) floor: the respawn point is on that floor, not half a volume below it.
            Game.Player.Teleport(new Vector3(0f, 0.5f, 24f));
            Run(2);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0.5f, 24f), Game.Player.CheckpointPosition), 1e-3f);
            Game.Player.Respawn();
            RunSeconds(1f);
            Assert.IsTrue(Game.Player.Grounded, "respawned under the floor");
            Assert.AreEqual(0.5f, Game.Player.Position.y, 0.02f);

            // No floor inside the volume: its bottom center, as before.
            Game.Player.Teleport(new Vector3(12f, 2.5f, 6f));
            Run(1);
            Assert.Less(Vector3.Distance(new Vector3(12f, 2f, 6f), Game.Player.CheckpointPosition), 1e-3f);

            Game.Player.Teleport(new Vector3(-12f, 0f, 6f));
            Run(2);
            Assert.Less(Vector3.Distance(new Vector3(-12f, 0f, 9f), Game.Player.CheckpointPosition), 1e-3f);
        }

        [Test]
        public void ATriggerCanBeRemoved()
        {
            Trigger pad = null;
            Prop crate = null;
            var log = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                pad = ctx.AddTrigger("pad", Volume.Box(4f, 2f, 4f), new Vector3(0f, 1f, 0f));
                pad.OnEnter += e => log.Add("enter:" + (e.IsPlayer ? "player" : e.Prop.Name));
                pad.OnExit += e => log.Add("exit:" + (e.IsPlayer ? "player" : e.Prop.Name));
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(1f, 0.25f, 1f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
                // The level's clock.
                ctx.OnUpdate(dt => Assert.AreEqual(ctx.Game.Time, ctx.Time));
            });
            Run(3);
            Assert.AreEqual(3, Game.Context.Ticks);
            Assert.AreEqual(3 * Sim.Dt, Game.Context.Time, 1e-6f);
            Assert.IsTrue(pad.PlayerInside && pad.Contains(crate));
            Assert.AreEqual(1, Game.Triggers.Count);

            Game.Context.RemoveTrigger(pad);
            Assert.IsTrue(pad.Removed);
            Assert.IsFalse(pad.Occupied, "whatever was inside leaves");
            Assert.AreEqual(new[] { "enter:player", "enter:crate", "exit:player", "exit:crate" }, log.ToArray());
            Assert.AreEqual(0, Game.Triggers.Count);
            Assert.IsTrue(pad.GameObject == null, "the volume is gone");
            Assert.DoesNotThrow(() => Run(5));
            Assert.AreEqual(4, log.Count, "a removed trigger senses nothing");
            Game.Context.RemoveTrigger(pad);
        }

        [Test]
        public void AListenerMayDisposeTheGameInTheMiddleOfAStep()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddExit(new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game game = Game;
            game.Events.LevelCompleted += e => game.Dispose();
            int before = game.TickCount;
            // Four ticks are due; the first completes the level and its listener disposes the Game.
            Assert.DoesNotThrow(() => game.Step(Sim.Dt * 4f));
            Assert.IsTrue(game.IsDisposed);
            Assert.AreEqual(1, game.TickCount - before);
            Assert.IsNull(Game.Current);
            Game = null;
        }

        // ------------------------------------------------------------------------------------------
        // Step
        // ------------------------------------------------------------------------------------------

        [Test]
        public void StepAtSixtyHertzRunsExactlyOneTickPerFrame()
        {
            Build(ctx => TestHelpers.Floor(ctx));
            int start = Game.TickCount;
            for (int i = 0; i < 600; i++) Game.Step(1f / 60f);
            Assert.AreEqual(600, Game.TickCount - start);

            // 144 Hz for one second.
            start = Game.TickCount;
            for (int i = 0; i < 144; i++) Game.Step(1f / 144f);
            Assert.That(Game.TickCount - start, Is.InRange(59, 61));

            // 20 fps: three ticks a frame, no backlog.
            start = Game.TickCount;
            for (int i = 0; i < 20; i++) Game.Step(0.05f);
            Assert.That(Game.TickCount - start, Is.InRange(59, 61));

            // 5 fps: capped at four ticks a frame, and the backlog does not grow.
            start = Game.TickCount;
            for (int i = 0; i < 10; i++) Game.Step(0.2f);
            Assert.AreEqual(40, Game.TickCount - start);
            Game.Step(0f);
            Assert.AreEqual(40, Game.TickCount - start, "a dropped backlog must not be replayed later");
            Assert.That(Game.Alpha, Is.InRange(0f, 1f));
        }
    }
}
