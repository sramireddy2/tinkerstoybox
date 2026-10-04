using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// The game flow: the state machine (Title, Playing, Paused, LevelSelect, LevelComplete), progress kept
    /// through an injectable store, and what GameRunner makes of it - the title unless the launch names a
    /// level, no ticks and a free pointer while not playing, and who gets the clicks.
    /// </summary>
    public class GameFlowTests
    {
        // A floor, a block to grab and an exit that is out of the way. Its number tells the levels apart.
        sealed class StepLevel : LevelDefinition
        {
            public readonly int Number;
            public Prop Block;

            public StepLevel(int number) => Number = number;

            public override void Build(LevelContext ctx)
            {
                if (Number == 13) throw new InvalidOperationException("level 13 is broken");
                TestHelpers.Floor(ctx, 40f);
                Block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f), new PropOptions { Name = "Block" });
                ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                yield return bot.WalkTo(new Vector3(6f, 0f, 6f), 0.5f);
                yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
            }
        }

        static LevelList Steps(params int[] ids) => new LevelList(ids, id => new StepLevel(id));

        Game game;
        GameFlow flow;
        MemoryStore store;
        List<string> changes;
        int loads;

        GameObject host;
        GameRunner runner;
        FakeDevices devices;

        [SetUp]
        public void NewStore()
        {
            store = new MemoryStore();
            changes = new List<string>();
            loads = 0;
        }

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            flow?.Dispose();
            flow = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
        }

        GameFlow Create(LevelList levels = null)
        {
            game = Game.Create();
            game.Events.LevelLoaded += e => loads++;
            flow = new GameFlow(game, levels ?? Steps(0, 1, 2), new Progress(store));
            flow.StateChanged += c => changes.Add(c.From + ">" + c.To);
            return flow;
        }

        int Number => ((StepLevel)game.Level).Number;

        // ------------------------------------------------------------------------------------------
        // The state machine
        // ------------------------------------------------------------------------------------------

        [Test]
        public void StartsOnTheTitle_WithTheLevelToContinueStandingBehindIt()
        {
            Create();
            Assert.AreEqual(FlowState.Title, flow.State);
            Assert.IsNull(game.Level, "nothing loads until somebody asks");
            Assert.AreEqual(0, flow.ContinueLevel, "a fresh store continues with the first level");

            Assert.IsTrue(flow.ShowTitle());
            Assert.AreEqual(FlowState.Title, flow.State);
            Assert.AreEqual(0, Number);
            Assert.AreEqual(0, flow.LevelId);
            Assert.AreEqual(1, loads);
            Assert.IsFalse(flow.Playing);
            Assert.AreEqual(-1, flow.Progress.LastPlayed, "looking at the title is not playing");

            // Starting the level that stands there untouched does not build it a second time.
            Assert.IsTrue(flow.StartLevel(0));
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(1, loads);
            Assert.AreEqual(0, flow.Progress.LastPlayed);

            // Once it has been played it does.
            game.Tick();
            Assert.IsTrue(flow.StartLevel(0));
            Assert.AreEqual(2, loads);
            Assert.IsTrue(flow.StartLevel(2));
            Assert.AreEqual(2, Number);
            Assert.AreEqual(2, flow.LevelId);
            Assert.AreEqual(2, flow.Progress.LastPlayed);
            CollectionAssert.AreEqual(new[] { "Title>Playing" }, changes, "a state is announced when it changes, not when it is confirmed");
        }

        [Test]
        public void PauseAndResume_OnlyWhereTheyMakeSense()
        {
            Create();
            flow.ShowTitle();
            Assert.IsFalse(flow.Pause(), "the title cannot be paused");
            Assert.IsFalse(flow.Resume());
            flow.StartLevel(0);

            Assert.IsTrue(flow.Pause());
            Assert.AreEqual(FlowState.Paused, flow.State);
            Assert.IsFalse(flow.Pause(), "already paused");
            Assert.IsTrue(flow.Resume());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.IsFalse(flow.Resume(), "already playing");
            Assert.AreEqual(1, loads, "pausing and resuming load nothing");

            flow.Pause();
            Assert.IsTrue(flow.Restart(), "restart from the pause menu");
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, loads);
            CollectionAssert.AreEqual(new[] { "Title>Playing", "Playing>Paused", "Paused>Playing", "Playing>Paused", "Paused>Playing" }, changes);
        }

        [Test]
        public void CompletingALevel_RecordsProgress_AndMovesOnAfterTheDelay()
        {
            Create();
            flow.StartLevel(1);
            for (int i = 0; i < 90; i++) game.Tick();
            game.CompleteLevel();

            Assert.AreEqual(FlowState.LevelComplete, flow.State);
            Assert.AreSame(game.Level, flow.LastCompletion.Level);
            Assert.AreEqual(1.5f, flow.LastCompletion.Time, 1e-4f);
            Assert.IsTrue(flow.LastCompletionWasBest);
            Assert.IsTrue(flow.Progress.IsCompleted(1));
            Assert.AreEqual(1.5f, flow.Progress.BestTime(1), 1e-4f);
            Assert.IsFalse(flow.Progress.IsCompleted(0));
            Assert.Less(flow.Progress.BestTime(0), 0f);
            Assert.GreaterOrEqual(store.Saves, 1, "progress is written through at once");

            Assert.IsTrue(flow.AdvancePending);
            Assert.AreEqual(GameFlow.DefaultAdvanceDelay, flow.AdvanceIn, 1e-4f);
            flow.Update(1f);
            flow.Update(float.NaN);
            flow.Update(-5f);
            Assert.AreEqual(FlowState.LevelComplete, flow.State);
            Assert.AreEqual(GameFlow.DefaultAdvanceDelay - 1f, flow.AdvanceIn, 1e-4f, "only real time counts");
            Assert.AreEqual(1, Number);
            flow.Update(1.6f);
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, Number);
            Assert.IsFalse(flow.AdvancePending);

            // After the last level comes the first again.
            game.CompleteLevel();
            flow.Update(GameFlow.DefaultAdvanceDelay);
            Assert.AreEqual(0, Number);
            CollectionAssert.AreEqual(new[] { "Title>Playing", "Playing>LevelComplete", "LevelComplete>Playing", "Playing>LevelComplete", "LevelComplete>Playing" }, changes);
        }

        [Test]
        public void AMenuCanHoldTheNextLevelBack()
        {
            Create();
            flow.StartLevel(0);
            flow.AutoAdvance = false;
            game.CompleteLevel();
            Assert.IsFalse(flow.AdvancePending);
            flow.Update(60f);
            Assert.AreEqual(FlowState.LevelComplete, flow.State, "the card waits for its Next button");
            Assert.AreEqual(0, Number);

            Assert.IsTrue(flow.NextLevel());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(1, Number);

            // A bot does not press buttons: the runner forces the countdown while one plays.
            game.CompleteLevel();
            flow.ForceAutoAdvance = true;
            Assert.IsTrue(flow.AdvancePending);
            flow.Update(GameFlow.DefaultAdvanceDelay + 0.1f);
            Assert.AreEqual(2, Number);

            // Restart from the level-complete card plays the same level again.
            flow.ForceAutoAdvance = false;
            game.CompleteLevel();
            Assert.IsTrue(flow.Restart());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, Number);
            Assert.IsFalse(game.LevelCompleted);
        }

        [Test]
        public void BestTimesKeepTheBest()
        {
            Create();
            float[] times = { 2f, 1f, 3f };
            bool[] best = { true, true, false };
            for (int run = 0; run < times.Length; run++)
            {
                flow.StartLevel(0);
                for (int i = 0; i < TestHelpers.Ticks(times[run]); i++) game.Tick();
                game.CompleteLevel();
                Assert.AreEqual(best[run], flow.LastCompletionWasBest, "run " + run);
            }
            Assert.AreEqual(1f, flow.Progress.BestTime(0), 1e-4f);
            Assert.AreEqual(3, flow.Progress.Completions(0));
            Assert.AreEqual(1, flow.Progress.CompletedCount(flow.Levels.Ids));
        }

        [Test]
        public void ProgressPersistsThroughTheStore()
        {
            Create();
            int announced = -2;
            flow.Progress.Changed += id => announced = id;
            flow.StartLevel(0);
            for (int i = 0; i < 60; i++) game.Tick();
            game.CompleteLevel();
            Assert.AreEqual(0, announced);
            flow.NextLevel();
            Assert.AreEqual(1, flow.Progress.LastPlayed);
            flow.Dispose();
            game.Dispose();

            // A new session with the same store.
            loads = 0;
            Create();
            Assert.IsTrue(flow.Progress.IsCompleted(0));
            Assert.AreEqual(1f, flow.Progress.BestTime(0), 1e-4f);
            Assert.IsFalse(flow.Progress.IsCompleted(1));
            Assert.AreEqual(1, flow.ContinueLevel, "the title opens over the level played last");
            flow.ShowTitle();
            Assert.AreEqual(1, Number);

            // The catalogue's lock: completed levels, the first one, and the one after a completed level.
            Assert.IsTrue(flow.Progress.IsUnlocked(0, flow.Levels));
            Assert.IsTrue(flow.Progress.IsUnlocked(1, flow.Levels));
            Assert.IsFalse(flow.Progress.IsUnlocked(2, flow.Levels));
            Assert.IsFalse(flow.Progress.IsUnlocked(7, flow.Levels), "not a level of the list");

            flow.Progress.Reset(flow.Levels.Ids);
            Assert.IsFalse(flow.Progress.IsCompleted(0));
            Assert.AreEqual(-1, flow.Progress.LastPlayed);
            Assert.AreEqual(0, flow.ContinueLevel);

            // A level that is no longer in the list is not continued with.
            flow.Progress.LastPlayed = 99;
            Assert.AreEqual(0, flow.ContinueLevel);
        }

        [Test]
        public void TheCampaign_IsWhatPlayMovesThrough_OtherLevelsAreOnlyAskedFor()
        {
            // 0 is a sandbox and 99 a showroom; 1, 2 and 3 are the game.
            var levels = new LevelList(new[] { 0, 1, 2, 3, 99 }, id => new StepLevel(id), null, id => id >= 1 && id <= 3);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 99 }, levels.Ids);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, levels.Campaign);
            Assert.AreEqual(1, levels.First);
            Assert.AreEqual(3, levels.Last);
            Assert.AreEqual(1, levels.After(0), "from the sandbox play goes into the campaign");
            Assert.AreEqual(1, levels.After(3), "after the last level comes the first - not the showroom");
            Assert.AreEqual(1, levels.After(99));
            Assert.AreEqual(3, levels.Before(1));
            Assert.AreEqual(3, levels.Before(99));
            Assert.IsTrue(levels.Has(99));
            Assert.IsFalse(levels.InCampaign(99));
            Assert.AreEqual(99, levels.Resolve(LaunchOptions.FromUrl("?level=99")), "any level of the list can be asked for");
            Assert.AreEqual(1, levels.Resolve(LaunchOptions.FromUrl("")), "nothing asked for: the first of the campaign");
            Assert.AreEqual(1, levels.Resolve(LaunchOptions.FromUrl("?level=7")));

            Create(levels);
            Assert.AreEqual(1, flow.ContinueLevel);
            Assert.IsTrue(flow.ShowTitle());
            Assert.AreEqual(1, Number, "the title opens over the campaign's first level");

            // A visit to the showroom: playable, but it neither moves the bookmark nor counts as progress.
            flow.StartLevel(2);
            Assert.AreEqual(2, flow.Progress.LastPlayed);
            Assert.IsTrue(flow.StartLevel(99));
            Assert.AreEqual(99, Number);
            Assert.AreEqual(2, flow.Progress.LastPlayed);
            game.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, flow.State);
            Assert.IsFalse(flow.Progress.IsCompleted(99));
            flow.Update(GameFlow.DefaultAdvanceDelay);
            Assert.AreEqual(1, Number, "and from there play goes back into the campaign");

            Assert.IsTrue(flow.Progress.IsUnlocked(0, levels), "levels outside the campaign are never locked");
            Assert.IsTrue(flow.Progress.IsUnlocked(99, levels));
            Assert.IsTrue(flow.Progress.IsUnlocked(1, levels));
            Assert.IsFalse(flow.Progress.IsUnlocked(2, levels));

            // A filter that picks nothing leaves every level in the campaign (today's registry: only the sandbox).
            var all = new LevelList(new[] { 0, 5 }, id => new StepLevel(id), null, id => false);
            CollectionAssert.AreEqual(new[] { 0, 5 }, all.Campaign);
            LevelList registry = LevelList.FromRegistry();
            Assert.IsTrue(registry.Has(0));
            foreach (int id in registry.Campaign)
            {
                LevelEntry entry = LevelRegistry.Find(id);
                Assert.IsTrue(entry.Phase > 0 || registry.Campaign.Count == registry.Ids.Count, "the campaign is the levels with a phase, once there are any");
            }
        }

        [Test]
        public void ABotsCompletionIsNotThePlayersProgress()
        {
            Create();
            flow.RecordProgress = false;
            flow.StartLevel(1);
            game.Tick();
            game.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, flow.State, "the flow goes on as usual");
            Assert.IsFalse(flow.Progress.IsCompleted(1));
            Assert.IsFalse(flow.LastCompletionWasBest);
            Assert.AreEqual(-1, flow.Progress.LastPlayed);
            Assert.AreEqual(0, store.Count, "nothing was written");
        }

        [Test]
        public void LevelSelect_OpensFromTheMenusAndReturnsToWhereItCameFrom()
        {
            Create();
            flow.ShowTitle();
            Assert.IsTrue(flow.OpenLevelSelect());
            Assert.AreEqual(FlowState.LevelSelect, flow.State);
            Assert.IsFalse(flow.OpenLevelSelect(), "already open");
            Assert.IsTrue(flow.CloseLevelSelect());
            Assert.AreEqual(FlowState.Title, flow.State, "closed without choosing: back to the title");
            Assert.IsFalse(flow.CloseLevelSelect());

            flow.OpenLevelSelect();
            Assert.IsTrue(flow.StartLevel(2), "choosing a level plays it");
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, Number);

            Assert.IsFalse(flow.OpenLevelSelect(), "not in the middle of play: pause first");
            flow.Pause();
            Assert.IsTrue(flow.OpenLevelSelect());
            flow.CloseLevelSelect();
            Assert.AreEqual(FlowState.Paused, flow.State, "back to the pause menu");

            Assert.IsTrue(flow.ReturnToTitle());
            Assert.AreEqual(FlowState.Title, flow.State);
            Assert.IsFalse(flow.ReturnToTitle());
            Assert.AreEqual(2, Number, "the level stays behind the title");
            Assert.AreEqual(2, loads);
        }

        [Test]
        public void ALevelThatFailsToBuildIsReported_AndTheFlowSurvives()
        {
            Create(Steps(0, 13));
            var messages = new List<string>();
            game.Events.Message += e => messages.Add(e.Text);
            flow.StartLevel(0);

            LogAssert.Expect(LogType.Exception, new Regex("level 13 is broken"));
            Assert.IsFalse(flow.StartLevel(13));
            Assert.IsNull(game.Level, "the broken level does not stay behind half-built");
            Assert.AreEqual(13, flow.LevelId);
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(1, messages.Count);
            StringAssert.Contains("failed to load", messages[0]);
            Assert.DoesNotThrow(() => game.Tick());
            Assert.IsFalse(flow.Restart(), "there is nothing to restart");

            Assert.IsTrue(flow.NextLevel(), "and the next request gets out of it");
            Assert.AreEqual(0, Number);
        }

        [Test]
        public void AFaultyListenerDoesNotStopTheFlow()
        {
            Create();
            flow.StateChanged += c => throw new InvalidOperationException("menu broke");
            var after = new List<FlowState>();
            flow.StateChanged += c => after.Add(c.To);
            LogAssert.Expect(LogType.Exception, new Regex("menu broke"));
            flow.StartLevel(0);
            Assert.AreEqual(FlowState.Playing, flow.State);
            CollectionAssert.AreEqual(new[] { FlowState.Playing }, after);
        }

        [Test]
        public void ADisposedFlowDoesNothing()
        {
            Create();
            flow.StartLevel(0);
            flow.Dispose();
            Assert.IsFalse(flow.Pause());
            Assert.IsFalse(flow.StartLevel(1));
            Assert.IsFalse(flow.NextLevel());
            game.CompleteLevel();
            Assert.AreEqual(FlowState.Playing, flow.State, "it no longer listens to the game");
            Assert.IsFalse(flow.Progress.IsCompleted(0));
        }

        // ------------------------------------------------------------------------------------------
        // GameRunner on top of it
        // ------------------------------------------------------------------------------------------

        // Stands in for the game's own HUD and menus. (Not marked [Presenter]: it is handed to the runner by hand.)
        sealed class MenuProbe : IPresenter
        {
            public void Attach(Game attached, PresentationContext context) { }
            public void Frame(float dt, float alpha) { }
            public void Dispose() { }
        }

        GameRunner Begin(string url, bool withMenus = false, bool present = true)
        {
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            List<PresenterRegistry.Entry> presenters = null;
            if (withMenus)
            {
                // The game's HUD and menus exist: a stand-in that provides them, next to the two fallbacks.
                presenters = new List<PresenterRegistry.Entry>();
                foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                    if (entry.Fallback) presenters.Add(entry);
                presenters.Add(new PresenterRegistry.Entry(typeof(MenuProbe), new PresenterAttribute(500) { ProvidesHud = true }));
            }
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions
            {
                Devices = devices,
                Levels = Steps(0, 1, 2),
                Present = present,
                Store = store,
                Presenters = presenters,
            });
            game = null;   // the runner owns it
            return runner;
        }

        void Frames(int count)
        {
            for (int i = 0; i < count; i++) runner.Frame(Sim.Dt);
        }

        [Test]
        public void Runner_OpensOnTheTitle_AndAClickStartsTheGame()
        {
            Begin("?plain=1");
            Game g = runner.Game;
            int grabbed = 0;
            g.Events.PropGrabbed += e => grabbed++;
            Assert.AreEqual(FlowState.Title, runner.Flow.State);
            Assert.IsNotNull(g.Level, "the first level stands behind the title");
            Assert.AreEqual(0, runner.LevelId);
            Assert.IsTrue(runner.Hud.ClickToPlay);

            // On the title nothing moves, whatever the keyboard and the mouse do.
            int ticks = g.TickCount;
            float yaw = g.Player.Yaw;
            devices.State.MoveZ = 1f;
            devices.State.JumpPressed = true;
            devices.State.Look = new Vector2(300f, 0f);
            devices.State.LookButton = true;
            Frames(30);
            Assert.AreEqual(ticks, g.TickCount, "the simulation does not tick on the title");
            Assert.AreEqual(Vector3.zero, g.Player.Position);
            Assert.AreEqual(yaw, g.Player.Yaw);
            Assert.IsFalse(devices.PointerLocked, "and the pointer is free");
            devices.State.MoveZ = 0f;
            devices.State.LookButton = false;

            // With the stand-in HUD a click anywhere starts the game and takes the mouse. It is not a grab.
            TestHelpers.LookAt(g.Player, ((StepLevel)g.Level).Block.Center);
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.IsTrue(devices.PointerLocked);
            Frames(5);
            Assert.Greater(g.TickCount, ticks);
            Assert.AreEqual(0, grabbed, "the click that starts the game is not also a grab");
            Assert.IsFalse(g.LastInput.Jump, "and what was pressed on the title is not played");
            Assert.IsFalse(runner.Hud.ClickToPlay);

            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(1, grabbed, "the next one is");
        }

        [Test]
        public void Runner_ALaunchThatNamesALevelOrAsksForAutoplaySkipsTheTitle()
        {
            Begin("?level=1", present: false);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(1, runner.LevelId);
            int ticks = runner.Game.TickCount;
            Frames(3);
            Assert.AreEqual(ticks + 3, runner.Game.TickCount);
            Shutdown();

            Begin("?autoplay=1", present: false);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.IsTrue(runner.Autoplay);
            Shutdown();

            // (The run above left a bookmark on level 1; a fresh store opens the title over the first level.)
            store = new MemoryStore();
            Begin("", present: false);
            Assert.AreEqual(FlowState.Title, runner.Flow.State);
            Assert.AreEqual(0, runner.LevelId);
            // The debug keys work from the title too.
            devices.State.NextLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(1, runner.LevelId);
            Shutdown();

            // Turning autoplay on from the title starts the level with the bot in charge.
            Begin("", present: false);
            runner.SetAutoplay(true);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreNotSame(runner.Human, runner.Game.Input);
        }

        [Test]
        public void Runner_EscPauses_NothingTicks_ThePointerIsFree_AndAClickResumes()
        {
            Begin("?level=0&plain=1");
            Game g = runner.Game;
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(devices.PointerLocked, "setup: the mouse is captured");
            devices.State.MoveZ = 1f;
            Frames(20);
            Assert.Greater(g.Player.Position.z, 0.5f, "setup: walking");

            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            Assert.IsFalse(devices.PointerLocked, "the pointer is free while paused");
            int ticks = g.TickCount;
            Vector3 position = g.Player.Position;
            devices.State.Look = new Vector2(200f, 0f);
            float yaw = g.Player.Yaw;
            Frames(30);
            Assert.AreEqual(ticks, g.TickCount, "the simulation does not tick while paused");
            Assert.AreEqual(position, g.Player.Position);
            Assert.AreEqual(yaw, g.Player.Yaw, "and the mouse does not turn the view");
            Assert.IsTrue(runner.Hud.ClickToPlay);
            Assert.AreEqual("Paused - click to resume", runner.Hud.Prompt);

            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.IsTrue(devices.PointerLocked);
            Frames(30);
            Assert.Greater(g.TickCount, ticks);
            Assert.Greater(g.Player.Position.z, position.z + 0.3f, "W is still held: walking on");

            // A pointer the browser takes away (its own Esc, a lost tab) pauses just the same.
            devices.PointerLocked = false;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);

            // But a capture that is gone again at once was never granted (a browser that refuses the lock
            // says "locked" for a frame): that is not a pause, or such a browser could never play.
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            devices.PointerLocked = true;
            runner.Frame(Sim.Dt);
            runner.Frame(Sim.Dt);
            devices.PointerLocked = false;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State, "held for " + (2 * Sim.Dt) + " s, under GameRunner.PointerHoldTime");
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            // Esc again resumes, with or without the mouse.
            devices.RefuseLock = true;
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            // R restarts from the pause.
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            int restarts = 0;
            g.Events.LevelRestarted += e => restarts++;
            devices.State.RestartPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(1, restarts, "one R, one restart");
        }

        [Test]
        public void Runner_WhileALevelIsBeingCelebratedNothingTicks()
        {
            Begin("?level=0", present: false);
            Game g = runner.Game;
            Frames(10);
            g.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            int ticks = g.TickCount;
            Frames(60);
            Assert.AreEqual(ticks, g.TickCount);
            Assert.IsTrue(runner.AdvancePending);
            Frames(TestHelpers.Ticks(GameRunner.AdvanceDelay));
            Assert.AreEqual(1, runner.LevelId);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.Greater(g.TickCount, ticks, "the next level is being played");
        }

        [Test]
        public void Runner_WithMenus_ClicksBelongToTheMenus_AndThePointerFollowsTheState()
        {
            Begin("", withMenus: true);
            Assert.IsTrue(runner.Presentation.HudProvided);
            Assert.IsNull(runner.Hud, "the debug HUD is retired when the game has its own");
            Assert.IsNotNull(runner.Presentation.Get<MenuProbe>());
            Assert.AreEqual(FlowState.Title, runner.Flow.State);

            // A click on the title is the menu's business: it neither starts the game nor takes the mouse.
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Title, runner.Flow.State);
            Assert.IsFalse(devices.PointerLocked);

            // The menu's button starts the level; play takes the mouse.
            Assert.IsTrue(runner.Flow.StartLevel(runner.Flow.ContinueLevel));
            Assert.IsTrue(devices.PointerLocked, "entering play captures the pointer");
            int ticks = runner.Game.TickCount;
            Frames(5);
            Assert.AreEqual(ticks + 5, runner.Game.TickCount);

            // Esc: paused, pointer free for the pause menu; a click does not resume.
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            Assert.IsFalse(devices.PointerLocked);
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            Assert.IsFalse(devices.PointerLocked);
            runner.Flow.Resume();
            Assert.IsTrue(devices.PointerLocked);

            // Level complete: the pointer is free for the card's buttons, and comes back with the next level.
            runner.Flow.AutoAdvance = false;
            runner.Game.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            Assert.IsFalse(devices.PointerLocked);
            Frames(TestHelpers.Ticks(GameRunner.AdvanceDelay) + 10);
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State, "the card waits for its button");
            runner.Flow.NextLevel();
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.IsTrue(devices.PointerLocked);
        }

        [Test]
        public void Runner_KeepsThePlayersProgressInItsStore_ButNotTheBots()
        {
            Begin("?level=1", present: false);
            Frames(30);
            runner.Game.CompleteLevel();
            Assert.IsTrue(runner.Flow.Progress.IsCompleted(1));
            Assert.AreEqual(0.5f, runner.Flow.Progress.BestTime(1), 1e-3f);
            Shutdown();
            Assert.GreaterOrEqual(store.Saves, 1);

            // A new session finds it, and opens the title over the level played last.
            Begin("", present: false);
            Assert.AreEqual(FlowState.Title, runner.Flow.State);
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsTrue(runner.Flow.Progress.IsCompleted(1));
            Shutdown();

            // The bot solving a level leaves no trace in it.
            Begin("?level=0&autoplay=1", present: false);
            for (int i = 0; i < TestHelpers.Ticks(30f) && runner.LevelId == 0; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(1, runner.LevelId, "the bot solved level 0 and the runner moved on");
            Assert.IsFalse(runner.Flow.Progress.IsCompleted(0));
            Assert.AreEqual(1, runner.Flow.Progress.LastPlayed, "nor does it move the bookmark");
        }
    }
}
