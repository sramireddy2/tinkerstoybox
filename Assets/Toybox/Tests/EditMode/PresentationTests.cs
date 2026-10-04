using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Keyboard and mouse for tests. Held fields of <see cref="State"/> stay as they are set; presses, wheel
    /// steps and mouse movement are reported by the next Poll only, like the real devices do.
    /// </summary>
    public sealed class FakeDevices : IInputDevices
    {
        public DeviceFrame State = new DeviceFrame { Focused = true };
        /// <summary>The browser says no.</summary>
        public bool RefuseLock;
        public int Polls;

        bool locked;

        public bool PointerLocked
        {
            get => locked;
            set => locked = value && !RefuseLock;
        }

        public DeviceFrame Poll()
        {
            Polls++;
            DeviceFrame frame = State;
            State.JumpPressed = false;
            State.ClickPressed = false;
            State.GrabKeyPressed = false;
            State.RotatePitchPressed = false;
            State.RestartPressed = false;
            State.EscapePressed = false;
            State.PreviousLevelPressed = false;
            State.NextLevelPressed = false;
            State.AutoplayPressed = false;
            State.RotateYawSteps = 0;
            State.Look = Vector2.zero;
            return frame;
        }
    }

    public class HumanInputTests
    {
        FakeDevices devices;
        HumanInput human;
        Game game;

        [SetUp]
        public void CreateInput()
        {
            devices = new FakeDevices();
            human = new HumanInput(devices);
        }

        [TearDown]
        public void DisposeGame()
        {
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
        }

        // One rendered frame that runs the given number of ticks; returns how many of them saw the grab press.
        int Frame(int ticks)
        {
            human.Update(null);
            int grabs = 0;
            for (int i = 0; i < ticks; i++)
                if (human.Sample().GrabPressed) grabs++;
            return grabs;
        }

        [Test]
        public void PressReachesExactlyOneTick_WhateverTheFramesRun()
        {
            devices.PointerLocked = true;
            int[][] patterns =
            {
                new[] { 0, 1, 3 }, new[] { 0, 3, 1 }, new[] { 1, 0, 3 }, new[] { 1, 3, 0 }, new[] { 3, 0, 1 }, new[] { 3, 1, 0 },
                new[] { 0, 0, 0, 1 }, new[] { 3, 3, 3 }, new[] { 1, 1, 1 },
            };
            foreach (int[] pattern in patterns)
            {
                for (int pressFrame = 0; pressFrame < pattern.Length; pressFrame++)
                {
                    human.Clear();
                    var perFrame = new List<int>();
                    for (int frame = 0; frame < pattern.Length; frame++)
                    {
                        if (frame == pressFrame) devices.State.GrabKeyPressed = true;
                        perFrame.Add(Frame(pattern[frame]));
                    }
                    // Enough further frames for a press made in a frame without ticks to arrive.
                    perFrame.Add(Frame(1));
                    perFrame.Add(Frame(3));

                    string what = "frames [" + string.Join(",", pattern) + "], pressed in frame " + pressFrame;
                    int total = 0;
                    foreach (int count in perFrame) total += count;
                    Assert.AreEqual(1, total, what + ": the press must reach exactly one tick");
                    for (int frame = 0; frame < pressFrame; frame++)
                        Assert.AreEqual(0, perFrame[frame], what + ": delivered before it was pressed");
                }
            }
        }

        [Test]
        public void PressInAFrameWithoutTicks_WaitsForTheNextTick()
        {
            devices.PointerLocked = true;
            devices.State.ClickPressed = true;
            Assert.AreEqual(0, Frame(0));
            Assert.AreEqual(0, Frame(0), "still no tick");
            human.Update(null);
            Assert.IsTrue(human.Sample().GrabPressed, "the first tick after the press gets it");
            Assert.IsFalse(human.Sample().GrabPressed, "and only that one");
        }

        [Test]
        public void PressInAFrameWithThreeTicks_GoesToTheFirstOnly()
        {
            devices.PointerLocked = true;
            devices.State.ClickPressed = true;
            human.Update(null);
            Assert.IsTrue(human.Sample().GrabPressed);
            Assert.IsFalse(human.Sample().GrabPressed);
            Assert.IsFalse(human.Sample().GrabPressed);
            Assert.AreEqual(0, Frame(1));
        }

        [Test]
        public void EveryEdgeIsLatched_NotOnlyGrab()
        {
            devices.State.RestartPressed = true;
            devices.State.RotatePitchPressed = true;
            devices.State.JumpPressed = true;   // tapped and released again before the poll: Jump itself is false
            human.Update(null);
            human.Update(null);                 // a second frame without a tick must not lose them

            InputFrame first = human.Sample();
            Assert.IsTrue(first.RestartPressed);
            Assert.IsTrue(first.RotatePitch);
            Assert.IsTrue(first.Jump, "a tap between two ticks still has to make the player jump");

            InputFrame second = human.Sample();
            Assert.IsFalse(second.RestartPressed);
            Assert.IsFalse(second.RotatePitch);
            Assert.IsFalse(second.Jump);
        }

        [Test]
        public void HeldKeysAreRepeatedForEveryTick()
        {
            devices.State.MoveX = -1f;
            devices.State.MoveZ = 1f;
            devices.State.Sprint = true;
            devices.State.Jump = true;
            human.Update(null);
            for (int i = 0; i < 3; i++)
            {
                InputFrame frame = human.Sample();
                Assert.AreEqual(-1f, frame.MoveX);
                Assert.AreEqual(1f, frame.MoveZ);
                Assert.IsTrue(frame.Sprint);
                Assert.IsTrue(frame.Jump);
            }

            devices.State.MoveX = 0f;
            devices.State.MoveZ = 0f;
            devices.State.Sprint = false;
            devices.State.Jump = false;
            human.Update(null);
            InputFrame released = human.Sample();
            Assert.AreEqual(0f, released.MoveX);
            Assert.AreEqual(0f, released.MoveZ);
            Assert.IsFalse(released.Sprint);
            Assert.IsFalse(released.Jump);
        }

        [Test]
        public void WheelBurstIsPaidOutOneStepPerTick()
        {
            devices.State.RotateYawSteps = 3;
            human.Update(null);
            Assert.AreEqual(1, human.Sample().RotateYaw);
            Assert.AreEqual(1, human.Sample().RotateYaw);
            human.Update(null);
            Assert.AreEqual(1, human.Sample().RotateYaw);
            Assert.AreEqual(0, human.Sample().RotateYaw);

            devices.State.RotateYawSteps = -2;
            human.Update(null);
            Assert.AreEqual(-1, human.Sample().RotateYaw);
            Assert.AreEqual(-1, human.Sample().RotateYaw);
            Assert.AreEqual(0, human.Sample().RotateYaw);

            // Opposite notches cancel, and a runaway wheel is capped instead of spinning for seconds.
            devices.State.RotateYawSteps = 2;
            human.Update(null);
            devices.State.RotateYawSteps = -2;
            human.Update(null);
            Assert.AreEqual(0, human.Sample().RotateYaw);
            devices.State.RotateYawSteps = 500;
            human.Update(null);
            int steps = 0;
            for (int i = 0; i < 100; i++) steps += human.Sample().RotateYaw;
            Assert.AreEqual(12, steps);
        }

        [Test]
        public void ClickLocksThePointer_AndIsNotAGrab()
        {
            Assert.IsFalse(human.PointerLocked);
            devices.State.ClickPressed = true;
            human.Update(null);
            Assert.IsTrue(human.PointerLocked, "clicking the game captures the mouse");
            Assert.IsFalse(human.Sample().GrabPressed, "the click that captures the mouse must not grab");

            devices.State.ClickPressed = true;
            human.Update(null);
            Assert.IsTrue(human.Sample().GrabPressed, "with the mouse captured a click grabs");

            devices.State.EscapePressed = true;
            human.Update(null);
            Assert.IsFalse(human.PointerLocked, "Escape gives the mouse back");
        }

        [Test]
        public void GrabKeyWorksWithoutPointerLock()
        {
            devices.RefuseLock = true;
            devices.State.ClickPressed = true;
            human.Update(null);
            Assert.IsFalse(human.PointerLocked, "the lock was refused");
            Assert.IsFalse(human.Sample().GrabPressed);

            devices.State.GrabKeyPressed = true;
            human.Update(null);
            Assert.IsTrue(human.Sample().GrabPressed, "E grabs even when the mouse cannot be captured");
        }

        [Test]
        public void MouseTurnsTheViewOnlyWhileLockedOrDragging()
        {
            game = Game.Create();
            game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Player player = game.Player;
            float perPixel = human.Sensitivity;

            // Free mouse: nothing happens, however long it moves.
            for (int i = 0; i < 3; i++)
            {
                devices.State.Look = new Vector2(100f, 40f);
                human.Update(player);
            }
            Assert.AreEqual(0f, player.Yaw);
            Assert.AreEqual(0f, player.Pitch);

            // Right-button drag, the fallback when the lock is refused. The first frame only arms it.
            devices.State.LookButton = true;
            devices.State.Look = new Vector2(100f, 40f);
            human.Update(player);
            Assert.AreEqual(0f, player.Yaw, "movement from before the drag started must not count");
            devices.State.Look = new Vector2(100f, 40f);
            human.Update(player);
            Assert.AreEqual(100f * perPixel, player.Yaw, 1e-4f, "mouse right turns right");
            Assert.AreEqual(40f * perPixel, player.Pitch, 1e-4f, "mouse up looks up");
            devices.State.LookButton = false;
            devices.State.Look = new Vector2(100f, 0f);
            human.Update(player);
            Assert.AreEqual(100f * perPixel, player.Yaw, 1e-4f, "released: free mouse again");

            // Locked: every frame counts, applied at once without waiting for a tick.
            devices.State.ClickPressed = true;
            human.Update(player);
            Assert.IsTrue(human.PointerLocked);
            float yaw = player.Yaw;
            devices.State.Look = new Vector2(-50f, 0f);
            human.Update(player);
            devices.State.Look = new Vector2(-50f, 0f);
            human.Update(player);
            Assert.AreEqual(yaw - 100f * perPixel, player.Yaw, 1e-4f);
            Assert.AreEqual(0, game.TickCount, "no tick was needed for any of this");

            // A null player means a bot is in charge: the mouse leaves the view alone.
            yaw = player.Yaw;
            devices.State.Look = new Vector2(300f, 0f);
            human.Update(null);
            Assert.AreEqual(yaw, player.Yaw);

            human.InvertY = true;
            float pitch = player.Pitch;
            devices.State.Look = new Vector2(0f, 10f);
            human.Update(player);
            Assert.AreEqual(pitch - 10f * perPixel, player.Pitch, 1e-4f);
        }

        [Test]
        public void FocusLossClearsHeldKeysAndPendingPresses()
        {
            devices.PointerLocked = true;
            devices.State.MoveZ = 1f;
            devices.State.Sprint = true;
            devices.State.Jump = true;
            devices.State.GrabKeyPressed = true;
            devices.State.RestartPressed = true;
            devices.State.RotateYawSteps = 4;
            human.Update(null);

            // The window loses focus with W still down; the devices keep reporting it.
            devices.State.Focused = false;
            human.Update(null);
            InputFrame frame = human.Sample();
            Assert.AreEqual(0f, frame.MoveZ, "a key held when focus went away must not keep walking");
            Assert.IsFalse(frame.Sprint);
            Assert.IsFalse(frame.Jump);
            Assert.IsFalse(frame.GrabPressed, "presses that never reached a tick are dropped");
            Assert.IsFalse(frame.RestartPressed);
            Assert.AreEqual(0, frame.RotateYaw);

            devices.State.ClickPressed = true;
            human.Update(null);
            Assert.IsFalse(human.Sample().GrabPressed, "input is ignored while unfocused");

            devices.State.Focused = true;
            devices.State.MoveZ = 0f;
            human.Update(null);
            Assert.AreEqual(0f, human.Sample().MoveZ);
        }

        [Test]
        public void RealDevicesCanBePolledWithoutAKeyboardOrMouse()
        {
            // In a batch-mode editor there may be no devices at all; polling must cope either way.
            var real = new UnityInputDevices();
            Assert.DoesNotThrow(() => real.Poll());
            var input = new HumanInput(real);
            Assert.DoesNotThrow(() => input.Sample());
        }
    }

    public class LaunchOptionsTests
    {
        [Test]
        public void LevelAndAutoplayAreReadFromTheQuery()
        {
            LaunchOptions options = LaunchOptions.FromUrl("https://example.github.io/tinkerstoybox/?level=3&autoplay=1");
            Assert.AreEqual(3, options.Level);
            Assert.IsTrue(options.Autoplay);
            Assert.IsNull(options.LevelSlug);

            options = LaunchOptions.FromUrl("http://localhost:8080/index.html?autoplay=0&level=12");
            Assert.AreEqual(12, options.Level);
            Assert.IsFalse(options.Autoplay);
        }

        [Test]
        public void NothingToParse_GivesTheDefaults()
        {
            foreach (string url in new[] { null, "", "https://example.com/", "https://example.com/index.html", "https://example.com/?", "?&&" })
            {
                LaunchOptions options = LaunchOptions.FromUrl(url);
                Assert.AreEqual(-1, options.Level, "url: " + url);
                Assert.IsNull(options.LevelSlug, "url: " + url);
                Assert.IsFalse(options.Autoplay, "url: " + url);
            }
        }

        [Test]
        public void KeysAreCaseInsensitive_AndTheFragmentIsIgnored()
        {
            LaunchOptions options = LaunchOptions.FromUrl("https://example.com/play?LEVEL=2&AutoPlay=TRUE#level=9");
            Assert.AreEqual(2, options.Level);
            Assert.IsTrue(options.Autoplay);
        }

        [Test]
        public void AutoplayValues()
        {
            Assert.IsTrue(LaunchOptions.FromUrl("?autoplay").Autoplay, "bare flag");
            Assert.IsTrue(LaunchOptions.FromUrl("?autoplay=true").Autoplay);
            Assert.IsTrue(LaunchOptions.FromUrl("?autoplay=yes").Autoplay);
            Assert.IsTrue(LaunchOptions.FromUrl("?autoplay=on").Autoplay);
            Assert.IsFalse(LaunchOptions.FromUrl("?autoplay=").Autoplay);
            Assert.IsFalse(LaunchOptions.FromUrl("?autoplay=0").Autoplay);
            Assert.IsFalse(LaunchOptions.FromUrl("?autoplay=false").Autoplay);
            Assert.IsFalse(LaunchOptions.FromUrl("?autoplay=2").Autoplay);
            Assert.IsFalse(LaunchOptions.FromUrl("?autoplayer=1").Autoplay);
        }

        [Test]
        public void MalformedLevelsDoNotBecomeLevelZero()
        {
            Assert.AreEqual(-1, LaunchOptions.FromUrl("?level=").Level);
            Assert.AreEqual(-1, LaunchOptions.FromUrl("?level=-4").Level);
            Assert.AreEqual(-1, LaunchOptions.FromUrl("?level=3.5").Level);
            Assert.AreEqual(-1, LaunchOptions.FromUrl("?levels=3").Level);
            Assert.AreEqual(0, LaunchOptions.FromUrl("?level=0").Level);
            Assert.AreEqual(7, LaunchOptions.FromUrl("?level=%37").Level, "percent-encoded");
            Assert.AreEqual(5, LaunchOptions.FromUrl("?level=1&level=5").Level, "the last one wins");
            Assert.AreEqual(4, LaunchOptions.FromUrl("?junk&=&x=%zz&level=4").Level, "junk around it is skipped");
        }

        [Test]
        public void ALevelCanBeNamedBySlug()
        {
            LaunchOptions options = LaunchOptions.FromUrl("?level=cheese%2Dwedge&autoplay=1");
            Assert.AreEqual(-1, options.Level);
            Assert.AreEqual("cheese-wedge", options.LevelSlug);
            Assert.IsTrue(options.Autoplay);
        }

        [Test]
        public void LevelListWrapsAroundAtBothEnds()
        {
            var list = new LevelList(new[] { 5, 0, 2, 2 }, id => new AdHocLevel(ctx => { }));
            Assert.AreEqual(new[] { 0, 2, 5 }, list.Ids);
            Assert.AreEqual(0, list.First);
            Assert.AreEqual(5, list.Last);
            Assert.AreEqual(2, list.After(0));
            Assert.AreEqual(5, list.After(2));
            Assert.AreEqual(0, list.After(5), "after the last level comes the first");
            Assert.AreEqual(5, list.After(3), "an id that is not in the list still has a successor");
            Assert.AreEqual(2, list.Before(5));
            Assert.AreEqual(5, list.Before(0), "before the first level comes the last");
            Assert.IsTrue(list.Has(2));
            Assert.IsFalse(list.Has(1));
            Assert.Throws<ArgumentException>(() => list.Create(1));
            Assert.Throws<ArgumentException>(() => new LevelList(new int[0], id => null));
        }

        [Test]
        public void LaunchResolvesToAnExistingLevel()
        {
            var list = new LevelList(new[] { 0, 2, 5 }, id => new AdHocLevel(ctx => { }), slug => slug == "five" ? 5 : -1);
            Assert.AreEqual(2, list.Resolve(LaunchOptions.FromUrl("?level=2")));
            Assert.AreEqual(0, list.Resolve(LaunchOptions.FromUrl("?level=3")), "unknown id: the first level");
            Assert.AreEqual(0, list.Resolve(LaunchOptions.FromUrl("")), "no level given: the first level");
            Assert.AreEqual(5, list.Resolve(LaunchOptions.FromUrl("?level=five")));
            Assert.AreEqual(0, list.Resolve(LaunchOptions.FromUrl("?level=six")));
        }

        [Test]
        public void RegistryListKnowsTheSandbox()
        {
            LevelList list = LevelList.FromRegistry();
            Assert.IsTrue(list.Has(0));
            Assert.AreEqual(0, list.Resolve(LaunchOptions.FromUrl("?level=sandbox")));
            Assert.AreEqual("sandbox", list.Create(0).Slug);
        }
    }

    public class GameRunnerTests
    {
        // A straight walk to the exit, with a block to grab on the way. Its number tells the levels apart.
        sealed class CorridorLevel : LevelDefinition
        {
            public readonly int Number;
            public readonly bool SolveFails;
            public Prop Block;

            public CorridorLevel(int number, bool solveFails = false)
            {
                Number = number;
                SolveFails = solveFails;
            }

            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 40f);
                Block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f), new PropOptions { Name = "Block" });
                ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                if (SolveFails) throw new BotException("stuck on purpose");
                yield return bot.WalkTo(new Vector3(6f, 0f, 6f), 0.5f);
                yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
            }
        }

        GameObject host;
        GameRunner runner;
        FakeDevices devices;
        int loads;

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
        }

        static LevelList Corridors(params int[] ids) => new LevelList(ids, id => new CorridorLevel(id));

        GameRunner Begin(string url, LevelList levels = null, bool present = false)
        {
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions
            {
                Devices = devices,
                Levels = levels ?? Corridors(0, 1, 2),
                Present = present,
                // These tests are about playing; the title has tests of its own (GameFlowTests).
                SkipTitle = true,
                Store = new MemoryStore(),
            });
            loads = 0;
            runner.Game.Events.LevelLoaded += e => loads++;
            return runner;
        }

        int Number => ((CorridorLevel)runner.Game.Level).Number;

        void Frames(float seconds, float frameTime = Sim.Dt)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += frameTime) runner.Frame(frameTime);
        }

        [Test]
        public void BeginLoadsTheLevelNamedInTheUrl()
        {
            Begin("https://example.com/?level=2");
            Assert.AreEqual(2, runner.LevelId);
            Assert.AreEqual(2, Number);
            Assert.IsFalse(runner.Autoplay);
            Assert.AreSame(runner.Human, runner.Game.Input, "a human plays unless the URL asks for autoplay");
            Assert.AreSame(Game.Current, runner.Game);
        }

        [Test]
        public void BeginFallsBackToTheFirstLevel()
        {
            Begin("https://example.com/?level=99", Corridors(3, 4));
            Assert.AreEqual(3, runner.LevelId);
            Assert.AreEqual(3, Number);
        }

        [Test]
        public void CompletingALevelLoadsTheNextOneAfterAShortDelay()
        {
            Begin("");
            Assert.AreEqual(0, Number);
            Frames(0.2f);
            runner.Game.CompleteLevel();
            Assert.IsTrue(runner.AdvancePending);

            Frames(GameRunner.AdvanceDelay - 0.3f);
            Assert.AreEqual(0, Number, "the next level must not load before the delay is over");
            Assert.IsTrue(runner.Game.LevelCompleted);
            Assert.AreEqual(0, loads);

            Frames(0.6f);
            Assert.AreEqual(1, Number);
            Assert.AreEqual(1, runner.LevelId);
            Assert.AreEqual(1, loads, "exactly one load");
            Assert.IsFalse(runner.Game.LevelCompleted);
            Assert.IsFalse(runner.AdvancePending);

            Frames(GameRunner.AdvanceDelay + 1f);
            Assert.AreEqual(1, Number, "nothing advances until this level is completed too");
        }

        [Test]
        public void AfterTheLastLevelComesTheFirst()
        {
            Begin("?level=2");
            runner.Game.CompleteLevel();
            Frames(GameRunner.AdvanceDelay + 0.2f);
            Assert.AreEqual(0, Number);
        }

        [Test]
        public void RestartDuringTheDelayCancelsTheAdvance()
        {
            Begin("");
            runner.Game.CompleteLevel();
            Frames(0.5f);
            devices.State.RestartPressed = true;
            Frames(GameRunner.AdvanceDelay + 0.5f);
            Assert.AreEqual(0, Number, "R restarted the level, so there is nothing to advance from");
            Assert.AreEqual(1, loads);
            Assert.IsFalse(runner.AdvancePending);
        }

        [Test]
        public void BracketKeysStepThroughTheLevels()
        {
            Begin("");
            devices.State.NextLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(1, Number);
            runner.Frame(Sim.Dt);
            Assert.AreEqual(1, Number, "one press, one level");

            devices.State.PreviousLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(0, Number);
            devices.State.PreviousLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(2, Number, "before the first comes the last");
            devices.State.NextLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(0, Number);
        }

        [Test]
        public void ALongFrameIsClamped()
        {
            Begin("");
            int before = runner.Game.TickCount;
            runner.Frame(30f);
            int ran = runner.Game.TickCount - before;
            Assert.Greater(ran, 0);
            Assert.LessOrEqual(ran, 6, "a hidden tab must not be caught up tick by tick");

            // The level flow pauses with it instead of jumping ahead by half a minute.
            runner.Game.CompleteLevel();
            runner.Frame(30f);
            Assert.AreEqual(0, Number);
            Assert.IsTrue(runner.AdvancePending);
            int ticks = runner.Game.TickCount;
            runner.Frame(-5f);
            runner.Frame(float.NaN);
            Assert.AreEqual(ticks, runner.Game.TickCount, "nonsense frame times are no time at all");
            Assert.AreEqual(0, Number);
            Frames(GameRunner.AdvanceDelay);
            Assert.AreEqual(1, Number, "and they must not break the countdown either");
        }

        [Test]
        public void AClickReachesTheGameOnce_AcrossFramesWithZeroOneAndThreeTicks()
        {
            Begin("");
            Game game = runner.Game;
            int grabbed = 0, dropped = 0;
            game.Events.PropGrabbed += e => grabbed++;
            game.Events.PropDropped += e => dropped++;
            var level = (CorridorLevel)game.Level;
            TestHelpers.LookAt(game.Player, level.Block.Center);

            // Capture the mouse first; that click is not a grab.
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(runner.Human.PointerLocked);
            Assert.AreEqual(0, grabbed);

            // The click lands in a frame too short for a tick ...
            int ticks = game.TickCount;
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt * 0.25f);
            Assert.AreEqual(ticks, game.TickCount, "this frame was meant to run no tick");
            Assert.AreEqual(0, grabbed);

            // ... is delivered by the next frame's single tick ...
            runner.Frame(Sim.Dt);
            Assert.AreEqual(ticks + 1, game.TickCount);
            Assert.AreEqual(1, grabbed);
            Assert.AreSame(level.Block, game.Grabber.Held);

            // ... and is not seen again by any of the three ticks of the frame after.
            runner.Frame(Sim.Dt * 3f);
            Assert.AreEqual(ticks + 4, game.TickCount);
            Assert.AreEqual(1, grabbed);
            Assert.AreEqual(0, dropped, "a second delivery would have dropped the block again");
            Assert.AreSame(level.Block, game.Grabber.Held);

            // A click in a three-tick frame drops it exactly once.
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt * 3f);
            Assert.AreEqual(1, dropped);
            Assert.AreEqual(1, grabbed);
            Assert.IsNull(game.Grabber.Held);
        }

        [Test]
        public void AutoplaySolvesTheLevelAndMovesOn()
        {
            Begin("?level=0&autoplay=1");
            Assert.IsTrue(runner.Autoplay);
            Assert.AreNotSame(runner.Human, runner.Game.Input, "the bot steers");

            // Paced by the runner's own frames, exactly like the real loop.
            for (int i = 0; i < TestHelpers.Ticks(30f) && runner.LevelId == 0; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(1, Number, "the bot should have solved level 0 and the runner moved on (bot at " + runner.Game.Player.Position + ")");
            Assert.IsNull(runner.AutoplayError);
            Assert.IsTrue(runner.Autoplay, "autoplay continues on the next level");

            for (int i = 0; i < TestHelpers.Ticks(30f) && runner.LevelId == 1; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(2, Number);
        }

        [Test]
        public void AutoplayIgnoresTheMouse()
        {
            Begin("?autoplay=1");
            devices.PointerLocked = true;
            runner.Frame(Sim.Dt);
            float yaw = runner.Game.Player.Yaw;
            devices.State.Look = new Vector2(400f, 0f);
            devices.State.GrabKeyPressed = true;
            runner.Frame(0f);
            Assert.AreEqual(yaw, runner.Game.Player.Yaw, "a frame without a tick: only the mouse could have turned the view");
        }

        [Test]
        public void PTogglesAutoplay()
        {
            Begin("");
            Frames(0.5f);
            int restarts = 0;
            runner.Game.Events.LevelRestarted += e => restarts++;

            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(runner.Autoplay);
            Assert.AreEqual(1, restarts, "a solution starts from the level's initial state");
            Assert.AreEqual(1, loads);
            Assert.AreNotSame(runner.Human, runner.Game.Input);
            Frames(1f);
            Assert.Greater(runner.Game.Player.Position.z, 1f, "the bot is walking");

            // Keys pressed while the bot played must not fire once the human is back.
            devices.State.GrabKeyPressed = true;
            runner.Frame(Sim.Dt);
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsFalse(runner.Autoplay);
            Assert.AreSame(runner.Human, runner.Game.Input);
            Assert.AreEqual(1, loads, "taking over does not reload the level");
            Assert.IsFalse(runner.Game.LastInput.GrabPressed);
            Vector3 position = runner.Game.Player.Position;
            Frames(1f);
            Assert.AreEqual(position.z, runner.Game.Player.Position.z, 0.5f, "nobody is steering now");
        }

        [Test]
        public void AFailingSolveScriptDoesNotStopTheGame()
        {
            var messages = new List<string>();
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?autoplay=1"), new RunnerOptions
            {
                Devices = devices,
                Levels = new LevelList(new[] { 0 }, id => new CorridorLevel(id, solveFails: true)),
                Present = false,
                Store = new MemoryStore(),
            });
            runner.Game.Events.Message += e => messages.Add(e.Text);

            int before = runner.Game.TickCount;
            Assert.DoesNotThrow(() => Frames(0.5f));
            Assert.AreEqual(before + TestHelpers.Ticks(0.5f), runner.Game.TickCount, "the simulation keeps running");
            StringAssert.Contains("stuck on purpose", runner.AutoplayError);
            Assert.AreEqual(1, messages.Count, "the failure is reported once: " + string.Join(" | ", messages));
            Assert.IsTrue(runner.Autoplay);

            // P hands the level to the human.
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsFalse(runner.Autoplay);
            Assert.IsNull(runner.AutoplayError);
            Assert.AreSame(runner.Human, runner.Game.Input);
        }

        [Test]
        public void ALevelThatFailsToBuildIsReportedAndSurvivable()
        {
            var levels = new LevelList(new[] { 0, 1 }, id =>
                id == 1 ? (LevelDefinition)new AdHocLevel(ctx => throw new InvalidOperationException("broken level")) : new CorridorLevel(id));
            Begin("", levels);

            LogAssert.Expect(LogType.Exception, new Regex("broken level"));
            devices.State.NextLevelPressed = true;
            Assert.DoesNotThrow(() => runner.Frame(Sim.Dt));
            Assert.IsNull(runner.Game.Level, "the broken level must not stay behind half-built");
            Assert.DoesNotThrow(() => Frames(0.2f));

            devices.State.NextLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(0, Number, "and the next key press gets out of it");
        }

        [Test]
        public void PresentationFollowsTheGame()
        {
            int objectsBefore = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
            AmbientMode ambientBefore = RenderSettings.ambientMode;
            Color skyBefore = RenderSettings.ambientSkyColor;

            // The plain look, so that this is about the seam and its two stand-ins whatever presenters the game has.
            Begin("?plain=1", present: true);
            Game game = runner.Game;
            Assert.IsNotNull(runner.Rig);
            Assert.IsNotNull(runner.Hud);
            Camera camera = runner.Rig.Camera;
            Assert.IsTrue(camera.transform.IsChildOf(game.Root.transform), "the camera has to be in the simulation's scene");
            Assert.AreEqual(game.Scene, camera.scene, "outside Play Mode only a camera bound to the preview scene renders it");
            Light sun = runner.Presentation.Get<PlainLook>().Sun;
            Assert.AreEqual(LightType.Directional, sun.type);
            Assert.AreNotEqual(LightShadows.None, sun.shadows);

            // The camera sits at the interpolated eye and looks where the player looks.
            devices.PointerLocked = true;
            devices.State.MoveZ = 1f;
            runner.Frame(Sim.Dt);
            for (int i = 0; i < 30; i++)
            {
                devices.State.Look = new Vector2(20f, 5f);
                runner.Frame(Sim.Dt * 0.7f);
            }
            Assert.Greater(game.Player.Position.z, 0.5f);
            Assert.Greater(game.Player.Yaw, 10f);
            Assert.Less(Vector3.Distance(game.Player.EyeAt(game.Alpha), camera.transform.position), 1e-4f);
            Assert.Less(Quaternion.Angle(game.Player.LookRotation, camera.transform.rotation), 1e-3f);
            Assert.Less(Vector3.Distance(game.Player.Eye, camera.transform.position), Player.WalkSpeed * Sim.Dt + 1e-3f,
                "interpolation lags the eye by at most one tick");

            // HUD state: the prompt until the mouse is captured, the banner between completion and the next level.
            Assert.IsFalse(runner.Hud.ClickToPlay);
            devices.PointerLocked = false;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(runner.Hud.ClickToPlay);
            Assert.IsFalse(runner.Hud.BannerVisible);
            game.CompleteLevel();
            Assert.IsTrue(runner.Hud.BannerVisible);

            // A level load moves the simulation to a new scene; the rig goes along and keeps rendering it.
            Scene sceneBefore = game.Scene;
            Frames(GameRunner.AdvanceDelay + 0.2f);
            Assert.AreEqual(1, Number);
            Assert.AreNotEqual(sceneBefore, game.Scene);
            Assert.IsFalse(runner.Hud.BannerVisible);
            Assert.IsTrue(camera != null, "the camera survives a level change");
            Assert.AreEqual(game.Scene, camera.scene);
            Assert.AreEqual(game.Scene, camera.gameObject.scene);

            runner.Shutdown();
            Assert.IsNull(Game.Current);
            Assert.IsTrue(camera == null, "the rig is gone with the game");
            Assert.IsTrue(runner.Hud == null);
            Assert.AreEqual(ambientBefore, RenderSettings.ambientMode, "scene lighting is put back");
            Assert.AreEqual(skyBefore, RenderSettings.ambientSkyColor);
            Assert.DoesNotThrow(() => runner.Frame(Sim.Dt), "a frame after shutdown is a no-op");
            Object.DestroyImmediate(host);
            host = null;
            Assert.AreEqual(objectsBefore, Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length,
                "nothing may be left behind in the open scenes");
        }
    }

    public class ShotsTests
    {
        [TearDown]
        public void DisposeGame() => Game.Current?.Dispose();

        [Test]
        public void TimesAndSizesAreParsedLeniently()
        {
            Assert.AreEqual(new[] { 0f, 2f, 5f }, Shots.ParseTimes("0,2,5"));
            Assert.AreEqual(new[] { 0f, 1.5f, 5f }, Shots.ParseTimes(" 5; 1.5 ,0, 5, x, -3"));
            Assert.AreEqual(new[] { 0f }, Shots.ParseTimes(""));
            Assert.AreEqual(new[] { 0f }, Shots.ParseTimes(null));

            int width = 1280, height = 720;
            Shots.ParseSize("640x360", ref width, ref height);
            Assert.AreEqual(640, width);
            Assert.AreEqual(360, height);
            Shots.ParseSize("nonsense", ref width, ref height);
            Shots.ParseSize("800x", ref width, ref height);
            Assert.AreEqual(640, width);
            Assert.AreEqual(360, height);
            Shots.ParseSize("1X100000", ref width, ref height);
            Assert.AreEqual(16, width);
            Assert.AreEqual(8192, height);
        }

        [Test]
        public void CaptureRendersALitColouredLevelAndCleansUp()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");

            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxShotsTest");
            if (Directory.Exists(directory))
                foreach (string old in Directory.GetFiles(directory, "*.png")) File.Delete(old);

            List<string> files = Shots.Run(new ShotRequest
            {
                Level = 0,
                Times = new[] { 1f, 0f },
                Width = 320,
                Height = 180,
                OutputDirectory = directory,
                Overview = true,
            });

            var names = new List<string>();
            foreach (string file in files) names.Add(Path.GetFileName(file));
            Assert.AreEqual(new[] { "level00-t00.png", "level00-t00-overview.png", "level00-t01.png", "level00-t01-overview.png" }, names.ToArray());

            foreach (string file in files)
            {
                Assert.IsTrue(File.Exists(file), file);
                var image = new Texture2D(2, 2);
                try
                {
                    Assert.IsTrue(image.LoadImage(File.ReadAllBytes(file)), "not a readable PNG: " + file);
                    Assert.AreEqual(320, image.width);
                    Assert.AreEqual(180, image.height);

                    Color32[] pixels = image.GetPixels32();
                    var distinct = new HashSet<int>();
                    int magenta = 0, dark = 0, vivid = 0;
                    foreach (Color32 p in pixels)
                    {
                        distinct.Add((p.r >> 3) << 10 | (p.g >> 3) << 5 | (p.b >> 3));
                        if (p.r > 230 && p.b > 230 && p.g < 40) magenta++;
                        if (p.r < 16 && p.g < 16 && p.b < 16) dark++;
                        int max = Mathf.Max(p.r, Mathf.Max(p.g, p.b)), min = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                        if (max > 120 && max - min > 90) vivid++;
                    }
                    string name = Path.GetFileName(file);
                    Assert.Greater(distinct.Count, 40, name + ": a lit scene has many shades, a blank or unlit one a handful");
                    Assert.Less(magenta, pixels.Length / 100, name + ": magenta means a missing shader");
                    Assert.Less(dark, pixels.Length / 10, name + ": mostly black means no light");
                    Assert.Greater(vivid, 20, name + ": the toys must show their colours");
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }

            Assert.IsNull(Game.Current, "the Game must be disposed");
            Assert.AreEqual(0, SceneManager.GetActiveScene().rootCount, "the scene is left empty");
        }

        // A level that is not in the registry, whose solution gives up after a moment.
        sealed class LostBotLevel : LevelDefinition
        {
            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 30f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                yield return bot.Wait(0.2f);
                throw new BotException("lost my way");
            }
        }

        [Test]
        public void ABotErrorIsLoggedAndTheShotsAreTakenAnyway()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");

            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxShotsTest");
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Toybox\] bot error: lost my way"));
            List<string> files = Shots.Run(new ShotRequest
            {
                Level = 7,
                Definition = new LostBotLevel(),
                Times = new[] { 0f, 0.5f },
                Width = 160,
                Height = 90,
                OutputDirectory = directory,
            });

            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("level07-t00.png", Path.GetFileName(files[0]));
            Assert.AreEqual("level07-t00.5.png", Path.GetFileName(files[1]));
            foreach (string file in files) Assert.IsTrue(File.Exists(file), file);
            Assert.IsNull(Game.Current);
        }
    }
}
