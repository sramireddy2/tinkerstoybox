using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Toys;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// The seam between the simulation (60 ticks a second) and what the player sees and touches (any frame
    /// rate, a real mouse, a browser): the held prop and a ridden platform against the camera on frames
    /// that run no tick, the pointer lock and the cursor, and the runner's loop under a hostile session
    /// (M1 review). PresentationTests has the unit tests of the same classes.
    ///
    /// One thing no editor can show: in a Web build WebGLInput.stickyCursorLock is true by default, so
    /// Cursor.lockState would stay Locked after the browser has released the mouse (Esc, focus loss).
    /// UnityInputDevices turns it off behind UNITY_WEBGL; the test here can only check that the line exists.
    /// </summary>
    public class PlatformTests
    {
        // Pixels per degree at the centre of a 1080p screen with the rig's 60 degree vertical field of view.
        const float PixelsPerDegree = 540f / 0.57735f * Mathf.Deg2Rad;

        // An open yard with one block to pick up. The exit is far away, so nothing completes by accident.
        sealed class YardLevel : LevelDefinition
        {
            public readonly int Number;
            public Prop Block;

            public YardLevel(int number) => Number = number;

            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 80f);
                Block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f), new PropOptions { Name = "Block" });
                // Far off in a corner, so that nothing ever gets between the player and the block in hand.
                ctx.AddProp(BasicToys.Ball(0.4f), new Vector3(-30f, 0.4f, -30f), new PropOptions { Name = "Ball" });
                ctx.AddProp(BasicToys.Cylinder(0.5f, 1.2f), new Vector3(-33f, 0.6f, -30f), new PropOptions { Name = "Drum" });
                ctx.AddTrigger("zone", Volume.Box(2f, 2f, 2f), new Vector3(-30f, 1f, -33f));
                ctx.AddExit(new Vector3(30f, 1f, 30f), new Vector3(2f, 2f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }
        }

        GameObject host;
        GameRunner runner;
        FakeDevices devices;

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
        }

        GameRunner Begin(string url, LevelList levels, bool present)
        {
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions { Devices = devices, Levels = levels, Present = present });
            return runner;
        }

        static LevelList Yards(params int[] ids) => new LevelList(ids, id => new YardLevel(id));

        // Captures the mouse, aims at the block and clicks: exactly what a player does.
        Prop GrabBlock()
        {
            Game game = runner.Game;
            var level = (YardLevel)game.Level;
            devices.PointerLocked = true;
            runner.Frame(Sim.Dt);
            TestHelpers.LookAt(game.Player, level.Block.Center);
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreSame(level.Block, game.Grabber.Held, "setup: the block should be in hand");
            Assert.IsTrue(game.Grabber.PlacementValid, "setup: the block has room");
            return level.Block;
        }

        // Angle between the crosshair (the middle of the picture the camera takes) and the held prop's centre.
        // (Vector3.Angle bottoms out at about 0.03 degrees for nearly parallel vectors; this does not.)
        static float CrosshairError(Camera camera, Prop held)
        {
            Vector3 forward = camera.transform.forward;
            Vector3 to = held.Center - camera.transform.position;
            return Mathf.Atan2(Vector3.Cross(forward, to).magnitude, Vector3.Dot(forward, to)) * Mathf.Rad2Deg;
        }

        // Turns the view with the mouse at a steady rate and returns the worst crosshair error of any rendered frame.
        float TurnWithTheMouse(Camera camera, Prop held, float seconds, float frameTime, float degreesPerSecond, out int framesWithoutTick)
        {
            float worst = 0f;
            framesWithoutTick = 0;
            int frames = Mathf.RoundToInt(seconds / frameTime);
            float pixels = degreesPerSecond * frameTime / runner.Human.Sensitivity;
            for (int i = 0; i < frames; i++)
            {
                int ticks = runner.Game.TickCount;
                devices.State.Look = new Vector2(pixels, 0f);
                runner.Frame(frameTime);
                if (runner.Game.TickCount == ticks) framesWithoutTick++;
                Assert.AreSame(held, runner.Game.Grabber.Held);
                Assert.IsTrue(runner.Game.Grabber.PlacementValid, "the yard is open, the block always has room");
                worst = Mathf.Max(worst, CrosshairError(camera, held));
            }
            return worst;
        }

        // ------------------------------------------------------------------------------------------------
        // The view answers the mouse at the display rate (HumanInput.Update -> player.AddLook, then
        // CameraRig.Apply); the simulation only puts the held prop on the view ray once per tick. On every
        // rendered frame that runs no tick - more than half of them on a 144 Hz display - the picture turns.
        // CameraRig.Apply therefore draws the held prop on the crosshair of the frame's own camera
        // (PerspectiveGrabber.Present), so the mechanic's promise holds at the display, not just per tick.
        // ------------------------------------------------------------------------------------------------
        [Test]
        public void HeldPropStaysOnTheCrosshair_WhenTheViewTurnsOnFramesWithoutATick()
        {
            Begin("", Yards(0), present: true);
            Prop held = GrabBlock();
            Camera camera = runner.Rig.Camera;

            // Control: one tick per frame. The look is applied before the tick, the prop follows exactly.
            float at60 = TurnWithTheMouse(camera, held, 1f, Sim.Dt, 120f, out int idle60);
            // The same turn of the wrist on a 144 Hz display.
            float at144 = TurnWithTheMouse(camera, held, 1f, 1f / 144f, 120f, out int idle144);
            // And on a 75 Hz display, where one frame in five runs no tick.
            float at75 = TurnWithTheMouse(camera, held, 1f, 1f / 75f, 120f, out int idle75);

            Debug.Log("platform: turning 120 deg/s with a prop in hand - 60 Hz: worst crosshair error " +
                      at60.ToString("0.000") + " deg (" + idle60 + " frames without a tick); 144 Hz: " + at144.ToString("0.000") +
                      " deg = " + (at144 * PixelsPerDegree).ToString("0") + " px at 1080p (" + idle144 + " of 144 frames without a tick); " +
                      "75 Hz: " + at75.ToString("0.000") + " deg = " + (at75 * PixelsPerDegree).ToString("0") + " px (" + idle75 +
                      " of 75 frames without a tick)");

            Assert.AreEqual(0, idle60, "control: at 60 Hz every frame runs a tick");
            Assert.Less(at60, 0.05f, "control: with one tick per frame the held prop stays on the crosshair");
            Assert.Greater(idle144, 60, "at 144 Hz most frames run no tick");
            Assert.Less(at144, 0.25f,
                "the held prop must stay on the crosshair in every rendered frame; it was off by " + at144.ToString("0.00") +
                " degrees (" + (at144 * PixelsPerDegree).ToString("0") + " px at 1080p), snapping back at every tick");
        }

        // The same seam, seen while walking: the camera is drawn between two ticks (Player.EyeAt(alpha)). A
        // held prop left where the last tick put it would shake sideways against the crosshair by up to one
        // tick of travel whenever the display is not locked to the tick rate.
        [Test]
        public void HeldPropStaysOnTheCrosshair_WhileStrafingOnA144HzDisplay()
        {
            Begin("", Yards(0), present: true);
            Prop held = GrabBlock();
            Camera camera = runner.Rig.Camera;
            Game game = runner.Game;

            devices.State.MoveX = 1f;
            for (int i = 0; i < 72; i++) runner.Frame(1f / 144f);   // half a second to reach walking speed
            Assert.AreEqual(Player.WalkSpeed, game.Player.Velocity.magnitude, 0.2f, "setup: strafing at walking speed");

            float worst = 0f, best = float.MaxValue;
            for (int i = 0; i < 144; i++)
            {
                runner.Frame(1f / 144f);
                Assert.AreSame(held, game.Grabber.Held);
                Assert.IsTrue(game.Grabber.PlacementValid, "the yard is open, the block always has room");
                Assert.AreEqual(5.1f, game.Grabber.HoldDistance, 0.3f, "nothing gets in the block's way");
                float error = CrosshairError(camera, held);
                worst = Mathf.Max(worst, error);
                best = Mathf.Min(best, error);
            }

            Debug.Log("platform: strafing at 5 u/s with a prop in hand at 144 Hz - crosshair error between " +
                      best.ToString("0.000") + " and " + worst.ToString("0.000") + " deg (" + (worst * PixelsPerDegree).ToString("0") +
                      " px at 1080p), hold distance " + game.Grabber.HoldDistance.ToString("0.00"));

            Assert.Less(worst, 0.25f,
                "the held prop must stay on the crosshair while the player walks; from frame to frame it moved between " +
                best.ToString("0.00") + " and " + worst.ToString("0.00") + " degrees off (hold distance " +
                game.Grabber.HoldDistance.ToString("0.0") + "; the closer the prop, the worse)");
        }

        // A platform that carries the player along +X at a steady 3 units per second.
        sealed class FerryRideLevel : LevelDefinition
        {
            public const float Speed = 3f;
            public Mover Ferry;

            public override void Build(LevelContext ctx)
            {
                Vector3 start = new Vector3(-20f, -0.25f, 0f);
                Ferry = ctx.AddKinematic(BasicToys.Slab(new Vector3(6f, 0.5f, 6f)), start);
                Mover ferry = Ferry;
                Game game = ctx.Game;
                ctx.OnUpdate(dt => ferry.MoveTo(start + Vector3.right * (Speed * (game.LevelTicks + 1) * Sim.Dt)));
                ctx.AddExit(new Vector3(60f, 30f, 60f), new Vector3(1f, 1f, 1f));
                ctx.SetSpawn(new Vector3(-20f, 0f, 0f), 0f);
            }
        }

        // How far the platform moves against the camera, from frame to frame, while the player just stands on it.
        float FerryShake(Camera camera, Mover ferry, float frameTime, int frames)
        {
            float least = float.MaxValue, most = float.MinValue;
            for (int i = 0; i < frames; i++)
            {
                runner.Frame(frameTime);
                float offset = ferry.Transform.position.x - camera.transform.position.x;
                least = Mathf.Min(least, offset);
                most = Mathf.Max(most, offset);
            }
            return most - least;
        }

        // The same seam once more: only the camera is drawn between two ticks, the platform under the
        // player's feet at its last tick. The camera therefore interpolates the player's movement relative
        // to what they stand on (Player.EyeAt), or the platform would shake against the picture by up to
        // one tick of travel, at the beat between display rate and tick rate.
        [Test]
        public void ThePlatformUnderThePlayerIsSteadyAgainstTheCamera_OnA144HzDisplay()
        {
            Begin("", new LevelList(new[] { 0 }, id => new FerryRideLevel()), present: true);
            Game game = runner.Game;
            Mover ferry = ((FerryRideLevel)game.Level).Ferry;
            Camera camera = runner.Rig.Camera;

            for (int i = 0; i < 60; i++) runner.Frame(Sim.Dt);
            Assert.IsTrue(game.Player.Grounded, "setup: the player stands on the ferry");
            Assert.AreEqual(FerryRideLevel.Speed, game.Player.Velocity.x, 0.05f, "setup: and rides along with it");

            float at60 = FerryShake(camera, ferry, Sim.Dt, 60);
            float at144 = FerryShake(camera, ferry, 1f / 144f, 144);
            float degrees = Mathf.Atan2(at144, game.Player.EyeHeight) * Mathf.Rad2Deg;

            Debug.Log("platform: riding a platform at 3 u/s - it moves against the camera by " +
                      at60.ToString("0.0000") + " units at 60 Hz and by " + at144.ToString("0.0000") + " units at 144 Hz (" +
                      degrees.ToString("0.00") + " deg = " + (degrees * PixelsPerDegree).ToString("0") + " px at 1080p when looking down at it)");

            Assert.Less(at60, 0.002f, "control: with one tick per frame the platform is steady under the camera");
            Assert.Less(at144, 0.01f,
                "a platform the player stands on must not move against the camera; it shook by " + at144.ToString("0.000") +
                " units (" + (degrees * PixelsPerDegree).ToString("0") + " px at 1080p when looking down at it)");
        }

        // ------------------------------------------------------------------------------------------------
        // The cursor is hidden exactly while the pointer lock is held. A lock that is refused, or taken away
        // from outside - the very case the right-button fallback and the "click to play" prompt exist for -
        // must leave the cursor visible, also after the runner has shut down.
        // A batch-mode editor refuses the lock just like a browser that says no, so the real class shows it.
        // ------------------------------------------------------------------------------------------------
        [Test]
        public void APointerLockThatIsNotGrantedLeavesTheCursorVisible()
        {
            CursorLockMode lockBefore = Cursor.lockState;
            bool visibleBefore = Cursor.visible;
            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                var real = new UnityInputDevices();
                var human = new HumanInput(real);

                // What HumanInput.Update does when the player clicks while the pointer is free.
                real.PointerLocked = true;
                Assume.That(real.PointerLocked, Is.False,
                    "this editor granted the lock; the test needs an environment that refuses it, as a batch-mode editor does");
                bool visibleRightAfter = Cursor.visible;

                // The following frames read the lock back as not held and carry on with the fallback ...
                for (int i = 0; i < 3; i++) human.Update(null);
                bool visibleLater = Cursor.visible;
                // ... and shutting down "gives the mouse back".
                human.ReleasePointer();
                bool visibleAfterRelease = Cursor.visible;

                Debug.Log("platform: lock refused (Cursor.lockState " + Cursor.lockState + ") - cursor visible right after: " +
                          visibleRightAfter + ", three frames later: " + visibleLater + ", after ReleasePointer: " + visibleAfterRelease);
                Assert.IsTrue(visibleRightAfter && visibleLater && visibleAfterRelease,
                    "the lock was not granted, so the mouse is free and its cursor must be visible; visible right after the request: " +
                    visibleRightAfter + ", three frames later: " + visibleLater + ", after HumanInput.ReleasePointer(): " + visibleAfterRelease);
            }
            finally
            {
                Cursor.lockState = lockBefore;
                Cursor.visible = visibleBefore;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // The runner's loop, the tools and the build settings
        // ------------------------------------------------------------------------------------------------

        // ?level=sandbox&autoplay=1 through the real registry, paced by frames that run zero, one or several
        // ticks and by a hidden-tab hitch, while a human leans on the keyboard and shakes the mouse.
        [Test]
        public void UrlAutoplaySolvesTheRealFirstLevel_ThroughUnevenFramesAndHumanNoise()
        {
            Begin("https://example.github.io/tinkerstoybox/?level=sandbox&autoplay=1", null, present: true);
            Game game = runner.Game;
            Assert.IsTrue(runner.Autoplay);
            Assert.AreEqual("sandbox", game.Level.Slug);
            // Unity does not call Update outside Play Mode; the HUD's per-frame physics queries are part of
            // what must not disturb the simulation, so they are made by hand.
            System.Reflection.MethodInfo hudUpdate = typeof(Toybox.UI.DebugHud).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(hudUpdate, "DebugHud.Update");

            int completed = 0, loaded = 0, restarted = 0;
            float completedAt = -1f;
            game.Events.LevelCompleted += e => { completed++; completedAt = e.Time; };
            game.Events.LevelLoaded += e => loaded++;
            game.Events.LevelRestarted += e => restarted++;

            float[] pattern = { 1f / 144f, 1f / 30f, 0.25f, 1f / 60f, 0f, 1f / 75f, 0.004f, 5f, float.NaN, -1f };
            devices.PointerLocked = true;
            for (int i = 0; i < 20000 && completed == 0; i++)
            {
                devices.State.Look = new Vector2(37f, -11f);
                devices.State.MoveX = 1f;
                devices.State.MoveZ = -1f;
                devices.State.Sprint = true;
                devices.State.Jump = i % 7 < 3;
                devices.State.JumpPressed = i % 7 == 0;
                devices.State.GrabKeyPressed = i % 5 == 0;
                devices.State.ClickPressed = i % 11 == 0;
                devices.State.RestartPressed = i % 13 == 0;
                devices.State.RotatePitchPressed = i % 3 == 0;
                devices.State.RotateYawSteps = 1;
                runner.Frame(pattern[i % pattern.Length]);
                hudUpdate.Invoke(runner.Hud, null);
            }

            Assert.AreEqual(1, completed, "the bot should have solved the sandbox through the runner's own loop (bot at " +
                                          game.Player.Position + ", error: " + runner.AutoplayError + ")");
            Assert.IsNull(runner.AutoplayError);
            Assert.AreEqual(0, restarted, "the human's R must not reach the game while the bot plays");
            Assert.AreEqual(0, loaded, "nothing reloads before the level is completed");

            // The same script pumped tick by tick, as the level's own test does, takes exactly as long.
            runner.Shutdown();
            Game reference = Game.Create();
            float referenceTime = -1f;
            reference.Events.LevelCompleted += e => referenceTime = e.Time;
            reference.LoadLevel(LevelRegistry.Get(0));
            TestHelpers.PlayLevel(reference);
            reference.Dispose();
            Assert.AreEqual(referenceTime, completedAt, 1e-4f, "paced by uneven frames the solution must replay tick for tick");
        }

        // A level whose solution loses its way after a moment, and one that cannot even be built.
        sealed class LostLevel : LevelDefinition
        {
            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 40f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f), new PropOptions { Name = "Block" });
                ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                yield return bot.Wait(0.3f);
                throw new BotException("lost on purpose");
            }
        }

        sealed class WalkLevel : LevelDefinition
        {
            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 40f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f), new PropOptions { Name = "Block" });
                ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                yield return bot.WalkTo(new Vector3(6f, 0f, 6f), 0.5f);
                yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
            }
        }

        // Ten minutes of a player mashing every key at every frame rate, across good, lost and broken
        // levels: no frame may throw, the Game must stay alive, and nothing may pile up.
        [Test]
        public void RunnerSurvivesAFuzzedSession()
        {
            var levels = new LevelList(new[] { 0, 1, 2, 3, 4 }, id =>
            {
                switch (id)
                {
                    case 1: return new LostLevel();
                    case 2: return new AdHocLevel(ctx => throw new InvalidOperationException("broken on purpose"));
                    case 3: return new FerryRideLevel();
                    case 4: return new WalkLevel();
                    default: return new YardLevel(id);
                }
            });

            bool ignoring = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Begin("?level=4&autoplay=1", levels, present: true);
                Game game = runner.Game;
                int loads = 0, completions = 0;
                game.Events.LevelLoaded += e => loads++;
                game.Events.LevelCompleted += e => completions++;

                // Warm every cache, then take the baseline on a level that builds.
                for (int id = 0; id < 5; id++)
                {
                    runner.LoadLevel(id);
                    runner.Frame(Sim.Dt);
                }
                runner.LoadLevel(0);
                Dictionary<string, int> before = CountObjects();

                var random = new System.Random(20261003);
                float[] frameTimes = { 0f, 0.001f, 1f / 240f, 1f / 144f, 1f / 60f, 1f / 30f, 0.07f, 0.5f, 30f, float.NaN, -3f };
                for (int frame = 0; frame < 12000; frame++)
                {
                    devices.State.Focused = random.Next(40) != 0;
                    devices.State.MoveX = random.Next(3) - 1;
                    devices.State.MoveZ = random.Next(3) - 1;
                    devices.State.Sprint = random.Next(2) == 0;
                    devices.State.Jump = random.Next(4) == 0;
                    devices.State.JumpPressed = random.Next(6) == 0;
                    devices.State.ClickPressed = random.Next(5) == 0;
                    devices.State.GrabKeyPressed = random.Next(7) == 0;
                    devices.State.RotatePitchPressed = random.Next(9) == 0;
                    devices.State.RotateYawSteps = random.Next(5) - 2;
                    devices.State.RestartPressed = random.Next(150) == 0;
                    devices.State.EscapePressed = random.Next(60) == 0;
                    devices.State.NextLevelPressed = random.Next(200) == 0;
                    devices.State.PreviousLevelPressed = random.Next(260) == 0;
                    devices.State.AutoplayPressed = random.Next(120) == 0;
                    devices.State.LookButton = random.Next(3) == 0;
                    devices.State.Look = new Vector2(random.Next(-400, 401), random.Next(-200, 201));
                    devices.RefuseLock = random.Next(10) == 0;

                    float frameTime = frameTimes[random.Next(frameTimes.Length)];
                    try
                    {
                        runner.Frame(frameTime);
                    }
                    catch (Exception e)
                    {
                        Assert.Fail("frame " + frame + " (level " + runner.LevelId + ", autoplay " + runner.Autoplay + ", dt " + frameTime + ") threw " + e);
                    }

                    Assert.IsFalse(game.IsDisposed);
                    Assert.AreSame(game, Game.Current);
                    Player player = game.Player;
                    Assert.IsFalse(float.IsNaN(player.Position.x + player.Position.y + player.Position.z), "frame " + frame + ": the player is nowhere");
                    Assert.IsFalse(float.IsNaN(player.Yaw + player.Pitch), "frame " + frame + ": the view is nowhere");
                    Assert.LessOrEqual(Mathf.Abs(player.Pitch), Player.MaxPitch);
                    Vector3 camera = runner.Rig.Camera.transform.position;
                    Assert.Less(Vector3.Distance(camera, player.EyeAt(game.Alpha)), 1e-3f, "frame " + frame + ": the camera is at the eye");
                    if (!runner.Autoplay) Assert.AreSame(runner.Human, game.Input, "frame " + frame + ": a human steers when autoplay is off");
                    else if (game.Level != null) Assert.AreNotSame(runner.Human, game.Input, "frame " + frame + ": the bot steers when autoplay is on");
                }

                Debug.Log("platform: fuzzed session - " + game.TickCount + " ticks, " + loads + " level loads, " + completions + " completions");
                Assert.Greater(loads, 50, "the session should have gone through many level loads");
                Assert.Greater(completions, 0, "and completed some levels");

                if (runner.Autoplay) runner.SetAutoplay(false);
                runner.LoadLevel(0);
                Assert.AreEqual("", Differences(before, CountObjects()), "objects left behind by the session");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = ignoring;
            }
        }

        static readonly Type[] CountedTypes =
        {
            typeof(GameObject), typeof(Material), typeof(Mesh), typeof(PhysicsMaterial), typeof(Camera), typeof(Light),
            typeof(Texture), typeof(MonoBehaviour),
        };

        static Dictionary<string, int> CountObjects()
        {
            var counts = new Dictionary<string, int>();
            foreach (Type type in CountedTypes) counts[type.Name] = Resources.FindObjectsOfTypeAll(type).Length;
            counts["preview scenes"] = EditorSceneManager.previewSceneCount;
            counts["loaded scenes"] = SceneManager.sceneCount;
            return counts;
        }

        static string Differences(Dictionary<string, int> before, Dictionary<string, int> after)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<string, int> entry in before)
                if (after[entry.Key] != entry.Value) lines.Add(entry.Key + " " + entry.Value + " -> " + after[entry.Key]);
            return string.Join(", ", lines);
        }

        // A long session walks through many levels and restarts; nothing may pile up on the way.
        [Test]
        public void WalkingThroughLevelsLeaksNoUnityObjects()
        {
            Begin("", Yards(0, 1, 2), present: true);
            Game game = runner.Game;

            // One full round first, so everything that is cached for good (materials, meshes) exists.
            for (int i = 0; i < 3; i++)
            {
                GrabBlock();
                devices.State.NextLevelPressed = true;
                runner.Frame(Sim.Dt);
            }
            Dictionary<string, int> before = CountObjects();

            for (int round = 0; round < 4; round++)
            {
                // By the debug key, with a prop in hand.
                GrabBlock();
                devices.State.NextLevelPressed = true;
                runner.Frame(Sim.Dt);
                // By R.
                GrabBlock();
                devices.State.RestartPressed = true;
                runner.Frame(Sim.Dt);
                // By completing the level and waiting for the runner to move on.
                int id = runner.LevelId;
                game.CompleteLevel();
                for (int i = 0; i < 400 && runner.LevelId == id; i++) runner.Frame(Sim.Dt);
                Assert.AreNotEqual(id, runner.LevelId, "the runner moves on after a completion");
                // By turning autoplay on and off again.
                devices.State.AutoplayPressed = true;
                runner.Frame(Sim.Dt);
                devices.State.AutoplayPressed = true;
                runner.Frame(Sim.Dt);
                Assert.IsFalse(runner.Autoplay);
            }

            Assert.AreEqual("", Differences(before, CountObjects()), "objects left behind by level changes");

            Dictionary<string, int> running = CountObjects();
            runner.Shutdown();
            Object.DestroyImmediate(host);
            host = null;
            Dictionary<string, int> after = CountObjects();
            Assert.Less(after["GameObject"], running["GameObject"]);
            Assert.AreEqual(running["preview scenes"] - 1, after["preview scenes"], "the simulation's scene is closed on shutdown");
            Assert.AreEqual(running["Camera"] - 1, after["Camera"], "the rig's camera is gone");
            Assert.IsNull(Game.Current);
        }

        // Shots must leave the editor as it found it: later tests share the process.
        [Test]
        public void ShotsLeaveGlobalStateClean()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");

            // Shots opens a new empty scene; do that first so the baseline is taken in the same situation.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxTestShots");
            var request = new ShotRequest { Level = 0, Times = new[] { 0f, 1f }, Width = 160, Height = 90, OutputDirectory = directory, Overview = true };
            // Once to warm every cache (materials, meshes, the pipeline's own resources).
            Shots.Run(request);

            Dictionary<string, int> before = CountObjects();
            int renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
            SimulationMode mode = Physics.simulationMode;
            Vector3 gravity = Physics.gravity;
            int iterations = Physics.defaultSolverIterations;
            bool propPlayer = Physics.GetIgnoreLayerCollision(Layers.Prop, Layers.Player);
            bool heldDefault = Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Default);
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            AmbientMode ambient = RenderSettings.ambientMode;
            Color sky = RenderSettings.ambientSkyColor;
            float shadowDistance = ShadowDistance();
            RenderTexture active = RenderTexture.active;

            List<string> files = Shots.Run(request);
            Assert.AreEqual(4, files.Count);

            Assert.AreEqual("", Differences(before, CountObjects()), "objects left behind by Shots.Run");
            Assert.AreEqual(renderTextures, Resources.FindObjectsOfTypeAll<RenderTexture>().Length, "render textures left behind");
            Assert.IsNull(Game.Current);
            Assert.AreEqual(mode, Physics.simulationMode);
            Assert.AreEqual(gravity, Physics.gravity);
            Assert.AreEqual(iterations, Physics.defaultSolverIterations);
            Assert.AreEqual(propPlayer, Physics.GetIgnoreLayerCollision(Layers.Prop, Layers.Player));
            Assert.AreEqual(heldDefault, Physics.GetIgnoreLayerCollision(Layers.Held, Layers.Default));
            Assert.AreEqual(asyncCompilation, ShaderUtil.allowAsyncCompilation);
            Assert.AreEqual(ambient, RenderSettings.ambientMode);
            Assert.AreEqual(sky, RenderSettings.ambientSkyColor);
            Assert.AreEqual(shadowDistance, ShadowDistance(), "the overview widens the pipeline's shadow distance and must put it back");
            Assert.AreSame(active, RenderTexture.active);
            Assert.AreEqual(0, SceneManager.GetActiveScene().rootCount);

            // Two runs of the same request give the same pictures: the capture is reproducible.
            byte[] first = File.ReadAllBytes(files[2]);
            List<string> again = Shots.Run(request);
            CollectionAssert.AreEqual(first, File.ReadAllBytes(again[2]), "the same request must render the same picture");
        }

        static float ShadowDistance()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return -1f;
            SerializedProperty property = new SerializedObject(pipeline).FindProperty("m_ShadowDistance");
            return property != null ? property.floatValue : -1f;
        }

        // The runner is the only owner of the Game: Shutdown gives back the pointer and every global.
        [Test]
        public void ShutdownReleasesThePointerAndRestoresPhysics_AndIsRepeatable()
        {
            SimulationMode mode = Physics.simulationMode;
            Vector3 gravity = Physics.gravity;

            for (int run = 0; run < 2; run++)
            {
                Begin("?autoplay=1", Yards(0, 1), present: true);
                devices.State.ClickPressed = true;
                runner.Frame(Sim.Dt);
                Assert.IsTrue(devices.PointerLocked, "a click captures the mouse even while the bot plays");
                Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);

                runner.Shutdown();
                runner.Shutdown();
                Assert.IsFalse(devices.PointerLocked, "shutting down gives the mouse back");
                Assert.IsNull(Game.Current);
                Assert.AreEqual(mode, Physics.simulationMode);
                Assert.AreEqual(gravity, Physics.gravity);
                Assert.DoesNotThrow(() => runner.Frame(Sim.Dt));
                Assert.IsFalse(runner.LoadLevel(0), "a runner that was shut down loads nothing");
                Assert.DoesNotThrow(() => runner.SetAutoplay(false));
                Object.DestroyImmediate(host);
                host = null;
                runner = null;
            }
        }

        static void Message(MonoBehaviour target, string name)
        {
            System.Reflection.MethodInfo method = target.GetType().GetMethod(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, target.GetType().Name + "." + name);
            try
            {
                method.Invoke(target, null);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                Assert.Fail(target.GetType().Name + "." + name + " threw " + e.InnerException);
            }
        }

        // Unity calls none of the lifecycle methods outside Play Mode, so the path the build takes - Bootstrap.Awake,
        // then the runner's Start / Update / OnApplicationQuit / OnDestroy with the real devices, the real registry
        // and the page address - is walked by hand here.
        [Test]
        public void BootstrapAndTheRunnersUnityMessages_StartRunAndShutDownCleanly()
        {
            SimulationMode mode = Physics.simulationMode;
            int gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            int cameras = Resources.FindObjectsOfTypeAll<Camera>().Length;
            int previewScenes = EditorSceneManager.previewSceneCount;
            CursorLockMode lockBefore = Cursor.lockState;
            bool visibleBefore = Cursor.visible;

            var bootstrapObject = new GameObject("Test Bootstrap") { hideFlags = HideFlags.DontSave };
            GameRunner created = null;
            try
            {
                Message(bootstrapObject.AddComponent<Bootstrap>(), "Awake");
                GameRunner[] runners = Object.FindObjectsByType<GameRunner>(FindObjectsInactive.Include);
                Assert.AreEqual(1, runners.Length, "the bootstrap creates exactly one runner");
                created = runners[0];
                Assert.IsNull(created.Game, "nothing begins before Start");

                Message(created, "Start");
                Assert.IsNotNull(created.Game);
                Assert.AreSame(Game.Current, created.Game);
                Assert.IsNotNull(created.Rig, "the build gets a camera");
                Assert.IsNotNull(created.Hud, "and a HUD");
                Assert.AreEqual("", Application.absoluteURL, "the editor has no page address");
                Assert.AreEqual(created.Levels.First, created.LevelId, "without a page address the first registered level loads");
                Assert.IsNotNull(created.Game.Level);
                Assert.IsFalse(created.Autoplay);
                Assert.AreSame(created.Human, created.Game.Input);
                Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);

                // A second Start (Unity never sends one, a careless caller might) must not begin twice.
                Message(created, "Start");
                for (int i = 0; i < 10; i++) Message(created, "Update");
                Assert.AreEqual(lockBefore, Cursor.lockState, "nobody clicked: the pointer is left alone");
                Assert.AreEqual(visibleBefore, Cursor.visible);

                Message(created, "OnApplicationQuit");
                Assert.IsNull(Game.Current, "quitting disposes the Game");
                Assert.AreEqual(mode, Physics.simulationMode, "and gives global physics back");
                Message(created, "OnDestroy");
                Message(created, "Update");
            }
            finally
            {
                if (created != null)
                {
                    created.Shutdown();
                    Object.DestroyImmediate(created.gameObject);
                }
                Object.DestroyImmediate(bootstrapObject);
                Game.Current?.Dispose();
                Cursor.lockState = lockBefore;
                Cursor.visible = visibleBefore;
            }

            Assert.AreEqual(gameObjects, Resources.FindObjectsOfTypeAll<GameObject>().Length, "game objects left behind");
            Assert.AreEqual(cameras, Resources.FindObjectsOfTypeAll<Camera>().Length, "cameras left behind");
            Assert.AreEqual(previewScenes, EditorSceneManager.previewSceneCount, "simulation scenes left behind");
        }

        // The simulation must not know the presentation: it is one assembly, so nothing but this enforces it.
        [Test]
        public void SimulationSourcesDoNotReferencePresentation()
        {
            string runtime = Path.Combine(Application.dataPath, "Toybox", "Runtime");
            string[] simulation = { "Engine", "Toys", "Art", "Gadgets", "Levels" };
            string[] forbidden =
            {
                "Toybox.Platform", "Toybox.Render", "Toybox.UI", "Toybox.Audio", "UnityEngine.InputSystem",
                "Time.deltaTime", "Time.time", "Time.unscaledTime", "Time.unscaledDeltaTime", "Time.fixedDeltaTime",
                "UnityEngine.Random", "Camera.main", "Cursor.", "Input.Get", "Shader.Find(",
            };
            var offences = new List<string>();
            foreach (string folder in simulation)
            {
                string path = Path.Combine(runtime, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                {
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].TrimStart();
                        if (line.StartsWith("//")) continue;
                        foreach (string word in forbidden)
                        {
                            if (!line.Contains(word)) continue;
                            // The one sanctioned fallback: ToyMaterials loads its template from Resources first.
                            if (word == "Shader.Find(" && Path.GetFileName(file) == "ToyMaterials.cs") continue;
                            offences.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + word);
                        }
                    }
                }
            }
            Assert.AreEqual("", string.Join("; ", offences), "simulation code must not depend on presentation or on Unity's clock");

            // And what the build needs at runtime is where the runtime looks for it.
            Assert.IsNotNull(Resources.Load<Material>("Materials/ToyLit"), "the template material must ship in Resources");
            Assert.IsNotNull(Resources.Load<Material>("Materials/ToyLit").shader);
            StringAssert.Contains("Universal Render Pipeline", Resources.Load<Material>("Materials/ToyLit").shader.name);
            Assert.IsTrue(File.Exists(Path.Combine(Application.dataPath, "Toybox", "link.xml")), "levels are found by reflection");
        }

        // The Web build cannot be made or run from a test. These pin down the two settings it depends on.
        [Test]
        public void TheWebBuildCatchesAllExceptionsAndFollowsTheBrowsersPointerLock()
        {
            // GameRunner reports a level that fails to build and a solve script that goes wrong, and GameEvents
            // isolates a faulty listener - all with catch blocks. With "explicitly thrown exceptions only" a
            // null reference in level code would not be an exception at all in the browser.
            Assert.AreEqual(WebGLExceptionSupport.FullWithoutStacktrace, PlayerSettings.WebGL.exceptionSupport,
                "ProjectSetup.ConfigurePlayer sets this; run it after changing it");

            // WebGLInput only exists in the Web player's assemblies, so the line that makes Cursor.lockState
            // follow the browser sits behind a define and is compiled by no editor.
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Toybox", "Runtime", "Platform", "InputDevices.cs"));
            StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
            StringAssert.Contains("WebGLInput.stickyCursorLock = false;", source);
        }
    }
}
