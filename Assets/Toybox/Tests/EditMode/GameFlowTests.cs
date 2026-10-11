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
    /// The game flow: the state machine (Title, Playing, Paused, LevelSelect, LevelComplete, Ending), progress kept
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

            // After the last level comes the ending - not the first level again.
            Assert.IsTrue(flow.AtLastLevel);
            game.CompleteLevel();
            flow.Update(GameFlow.DefaultAdvanceDelay);
            Assert.AreEqual(FlowState.Ending, flow.State);
            Assert.AreEqual(2, Number, "the last level stays loaded behind the ending");
            Assert.IsFalse(flow.AdvancePending);
            flow.Update(60f);
            Assert.AreEqual(FlowState.Ending, flow.State, "the ending waits");
            CollectionAssert.AreEqual(new[] { "Title>Playing", "Playing>LevelComplete", "LevelComplete>Playing", "Playing>LevelComplete", "LevelComplete>Ending" }, changes);
        }

        void PickUp(int times = 1)
        {
            for (int i = 0; i < times; i++) game.Events.RaisePropGrabbed(new PropHoldEvent());
        }

        // The campaign is 1, 2, 3; 0 is a sandbox and 99 a showroom.
        static LevelList Campaign() => new LevelList(new[] { 0, 1, 2, 3, 99 }, id => new StepLevel(id), null, id => id >= 1 && id <= 3);

        [Test]
        public void AfterTheLastLevel_ComesTheEnding_AndAFreshGameStartsAtTheBeginning_WithEverythingKept()
        {
            Create(Campaign());
            flow.AutoAdvance = false;
            flow.ShowTitle();
            flow.StartLevel(flow.LevelId);
            Assert.AreEqual(1, Number);
            Assert.IsFalse(flow.AtLastLevel);

            // Level 1: a second of play and two pick-ups. Advance is the card's "Next".
            for (int i = 0; i < 60; i++) game.Tick();
            PickUp(2);
            Assert.AreEqual(2, flow.PickUps);
            game.CompleteLevel();
            Assert.AreEqual(2, flow.Progress.PickUps(1), "the pick-ups of a best run are kept with its time");
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, Number);
            Assert.AreEqual(0, flow.PickUps, "a new level, a new count");

            // Level 2: half a second, nothing picked up. Then again, slower, with a pick-up: the best run stands.
            for (int i = 0; i < 30; i++) game.Tick();
            game.CompleteLevel();
            Assert.IsTrue(flow.Restart());
            for (int i = 0; i < 90; i++) game.Tick();
            PickUp();
            game.CompleteLevel();
            Assert.IsFalse(flow.LastCompletionWasBest);
            Assert.AreEqual(0.5f, flow.Progress.BestTime(2), 1e-4f);
            Assert.AreEqual(0, flow.Progress.PickUps(2));
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(3, Number);
            Assert.IsTrue(flow.AtLastLevel);
            Assert.AreEqual(0, flow.Progress.Endings);

            // Level 3 is the last. While it is played "Advance" is still the step to the next level ...
            for (int i = 0; i < 90; i++) game.Tick();
            PickUp();
            int loadsBefore = loads;
            game.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, flow.State);
            Assert.AreEqual(3, flow.Progress.LastPlayed);
            // ... and from its complete card it is the ending.
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(FlowState.Ending, flow.State);
            Assert.AreEqual("LevelComplete>Ending", changes[changes.Count - 1]);
            Assert.AreEqual(3, Number, "the last level stays loaded behind the ending");
            Assert.AreEqual(loadsBefore, loads, "nothing was loaded: the campaign does not wrap round");
            Assert.IsFalse(flow.Playing);
            Assert.IsFalse(flow.AdvancePending);
            Assert.AreEqual(1, flow.Progress.Endings, "the ending is remembered");
            Assert.AreEqual(1, flow.Progress.LastPlayed, "and the bookmark is back on the first level");
            Assert.AreEqual(1, flow.ContinueLevel);

            // What the ending tells: all three, the best runs added up.
            CampaignReport report = flow.Report();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, report.Levels);
            CollectionAssert.AreEqual(new[] { true, true, true }, report.Collected);
            Assert.IsTrue(report.Full);
            Assert.IsFalse(report.Watched);
            Assert.AreEqual(3, report.CollectedCount);
            Assert.AreEqual(1f + 0.5f + 1.5f, report.Seconds, 1e-3f);
            Assert.AreEqual(2 + 0 + 1, report.PickUps);
            Assert.AreEqual(report.Seconds, flow.Progress.TotalTime(flow.Levels.Campaign), 1e-4f);
            Assert.AreEqual(report.PickUps, flow.Progress.TotalPickUps(flow.Levels.Campaign));

            // The ending is a place to rest: it cannot be paused or resumed, and nothing moves on by itself.
            Assert.IsFalse(flow.Pause());
            Assert.IsFalse(flow.Resume());
            Assert.IsFalse(flow.ShowEnding(), "already there");
            flow.ForceAutoAdvance = true;
            flow.Update(60f);
            flow.ForceAutoAdvance = false;
            Assert.AreEqual(FlowState.Ending, flow.State);
            Assert.AreEqual(1, flow.Progress.Endings);
            // The catalogue opens from it, and closes back to it.
            Assert.IsTrue(flow.OpenLevelSelect());
            Assert.AreEqual(FlowState.LevelSelect, flow.State);
            Assert.IsTrue(flow.CloseLevelSelect());
            Assert.AreEqual(FlowState.Ending, flow.State);

            // Back to the title: it stands over the first level, and "Click to play" is a fresh game.
            Assert.IsTrue(flow.ReturnToTitle());
            Assert.AreEqual(FlowState.Title, flow.State);
            Assert.AreEqual(1, Number);
            Assert.AreEqual(1, flow.LevelId);
            Assert.AreEqual(0, game.LevelTicks);
            int before = loads;
            Assert.IsTrue(flow.StartLevel(flow.LevelId));
            Assert.AreEqual(before, loads, "the level that stands behind the title is the one that is played");
            Assert.AreEqual(FlowState.Playing, flow.State);
            for (int id = 1; id <= 3; id++) Assert.IsTrue(flow.Progress.IsCompleted(id), "level " + id + " is still collected");
            flow.Dispose();
            game.Dispose();

            // Another session with the same store: the title opens over level 1, and the catalogue is as it was left.
            Create(Campaign());
            Assert.AreEqual(1, flow.ContinueLevel);
            flow.ShowTitle();
            Assert.AreEqual(1, Number);
            Assert.AreEqual(1, flow.Progress.Endings);
            Assert.IsTrue(flow.Report().Full);
            for (int id = 1; id <= 3; id++) Assert.IsTrue(flow.Progress.IsUnlocked(id, flow.Levels));
            Assert.AreEqual(0.5f, flow.Progress.BestTime(2), 1e-4f);

            // Playing the campaign through again ends it again; forgetting everything forgets that too.
            flow.AutoAdvance = false;
            flow.StartLevel(3);
            game.CompleteLevel();
            flow.Advance();
            Assert.AreEqual(2, flow.Progress.Endings);
            flow.Progress.Reset(flow.Levels.Ids);
            Assert.AreEqual(0, flow.Progress.Endings);
            Assert.AreEqual(-1, flow.Progress.PickUps(1));
            Assert.IsFalse(flow.Report().Full);
            Assert.AreEqual(0, flow.Report().CollectedCount);
        }

        [Test]
        public void TheEnding_IsOnlyForTheCampaignsLastLevel_AndCountsWhatIsCollected()
        {
            Create(Campaign());
            flow.AutoAdvance = false;

            // The showroom lies after the last level in the list, but it is no part of the campaign.
            flow.StartLevel(99);
            Assert.IsFalse(flow.AtLastLevel);
            game.CompleteLevel();
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(1, Number, "from the showroom play goes into the campaign");

            // The level keys still step round the list (they are for looking at levels, not for playing the game).
            flow.StartLevel(3);
            Assert.IsTrue(flow.NextLevel());
            Assert.AreEqual(1, Number);
            flow.StartLevel(3);
            Assert.IsTrue(flow.Advance(), "the last level is not completed: Advance is the next level");
            Assert.AreEqual(1, Number);

            // Somebody who went straight to the last level (by address) reaches the ending with one toy collected.
            flow.StartLevel(3);
            for (int i = 0; i < 120; i++) game.Tick();
            game.CompleteLevel();
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(FlowState.Ending, flow.State);
            CampaignReport report = flow.Report();
            Assert.IsFalse(report.Full);
            Assert.AreEqual(1, report.CollectedCount);
            Assert.AreEqual(3, report.Total);
            CollectionAssert.AreEqual(new[] { false, false, true }, report.Collected);
            Assert.AreEqual(2f, report.Seconds, 1e-3f);

            // From the ending the level keys and the catalogue's cards start a level as from anywhere else.
            Assert.IsTrue(flow.StartLevel(2));
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(2, Number);
            Assert.AreEqual(2, flow.Progress.LastPlayed);
        }

        [Test]
        public void ABotsRunToTheEnding_IsItsOwn_AndLeavesNothingInTheStore()
        {
            Create(Campaign());
            // What the runner sets while a bot plays.
            flow.RecordProgress = false;
            flow.ForceAutoAdvance = true;
            flow.StartLevel(2);
            for (int i = 0; i < 60; i++) game.Tick();
            PickUp(3);
            game.CompleteLevel();
            flow.Update(GameFlow.DefaultAdvanceDelay);
            Assert.AreEqual(3, Number);
            for (int i = 0; i < 30; i++) game.Tick();
            PickUp();
            game.CompleteLevel();
            flow.Update(GameFlow.DefaultAdvanceDelay);
            Assert.AreEqual(FlowState.Ending, flow.State, "the countdown ends the campaign too");
            Assert.AreEqual(3, Number);

            Assert.AreEqual(0, store.Count, "nothing of it is the player's");
            Assert.AreEqual(0, flow.Progress.Endings);
            Assert.AreEqual(-1, flow.Progress.LastPlayed);
            CampaignReport report = flow.Report();
            Assert.IsTrue(report.Watched);
            CollectionAssert.AreEqual(new[] { false, true, true }, report.Collected, "what the bot played since the game began");
            Assert.AreEqual(1.5f, report.Seconds, 1e-3f);
            Assert.AreEqual(4, report.PickUps);

            // The player takes over: the report is their own progress again.
            flow.RecordProgress = true;
            Assert.IsFalse(flow.Report().Watched);
            Assert.AreEqual(0, flow.Report().CollectedCount);
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

        // The level-complete card is the natural place to stop for the day. The bookmark used to move only
        // with "Next" (or the card's countdown): whoever closed the tab on the card came back to the level
        // they had just finished, and nothing on the title said that it was done.
        [Test]
        public void StoppingAtTheCard_TheNextSessionContinuesWithTheLevelAfterTheOneJustFinished()
        {
            Create(Campaign());
            flow.AutoAdvance = false;
            flow.StartLevel(1);
            for (int i = 0; i < 60; i++) game.Tick();
            game.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, flow.State);
            Assert.AreEqual(1, flow.LevelId, "the card is still the finished level's");
            Assert.AreEqual(2, flow.Progress.LastPlayed, "but the bookmark is on the next one already");
            // The tab is closed with the card up.
            flow.Dispose();
            game.Dispose();

            loads = 0;
            Create(Campaign());
            Assert.AreEqual(2, flow.ContinueLevel);
            Assert.IsTrue(flow.ShowTitle());
            Assert.AreEqual(2, Number, "the title opens over the next level");
            Assert.IsTrue(flow.Progress.IsCompleted(1));
            Assert.IsTrue(flow.Progress.IsUnlocked(2, flow.Levels));
            Assert.IsTrue(flow.StartLevel(flow.LevelId));
            Assert.AreEqual(2, Number);

            // In the session itself nothing changes: Again on the card plays the finished level once more
            // (and leaves the bookmark where it is), Next goes on.
            for (int i = 0; i < 30; i++) game.Tick();
            game.CompleteLevel();
            Assert.AreEqual(3, flow.Progress.LastPlayed);
            Assert.IsTrue(flow.Restart());
            Assert.AreEqual(2, Number);
            Assert.AreEqual(3, flow.Progress.LastPlayed);
            game.CompleteLevel();
            Assert.IsTrue(flow.Advance());
            Assert.AreEqual(3, Number);
            Assert.AreEqual(3, flow.Progress.LastPlayed);
        }

        // The last level's bookmark stays on it until Finish: the ending is what puts it back to the first
        // (After() would wrap round to the first at once, and the session after would never see the ending).
        [Test]
        public void StoppingAtTheLastLevelsCard_TheBookmarkStaysOnTheLastLevel()
        {
            Create(Campaign());
            flow.AutoAdvance = false;
            flow.StartLevel(3);
            for (int i = 0; i < 60; i++) game.Tick();
            game.CompleteLevel();
            Assert.IsTrue(flow.AtLastLevel);
            Assert.AreEqual(3, flow.Progress.LastPlayed);
            flow.Dispose();
            game.Dispose();

            Create(Campaign());
            Assert.AreEqual(3, flow.ContinueLevel);
            Assert.IsTrue(flow.ShowTitle());
            Assert.AreEqual(3, Number);
            Assert.AreEqual(0, flow.Progress.Endings);
        }

        // Neither a level outside the campaign nor a bot's run moves it.
        [Test]
        public void ACompletionOutsideTheCampaign_OrByABot_LeavesTheBookmarkAlone()
        {
            Create(Campaign());
            flow.AutoAdvance = false;
            flow.StartLevel(2);
            Assert.AreEqual(2, flow.Progress.LastPlayed);
            flow.StartLevel(0);
            game.CompleteLevel();
            Assert.AreEqual(2, flow.Progress.LastPlayed, "the sandbox");
            flow.StartLevel(99);
            game.CompleteLevel();
            Assert.AreEqual(2, flow.Progress.LastPlayed, "the showroom");

            flow.RecordProgress = false;
            flow.StartLevel(1);
            game.CompleteLevel();
            Assert.AreEqual(2, flow.Progress.LastPlayed, "a bot");
            Assert.IsFalse(flow.Progress.IsCompleted(1));
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
            /// <summary>What a button of the menu does in the frame it is pressed.</summary>
            public Action OnFrame;

            public void Attach(Game attached, PresentationContext context) { }
            public void Frame(float dt, float alpha) => OnFrame?.Invoke();
            public void Dispose() { }
        }

        // (The debug keys are on unless a test says otherwise: several tests step through levels with them.)
        GameRunner Begin(string url, bool withMenus = false, bool present = true, bool? debugKeys = true)
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
                DebugKeys = debugKeys,
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
            // Nobody touched anything through the countdown: the next level waits for its player
            // (Runner_ALevelTheCardMovedOnToByItself_WaitsForItsPlayer), and goes at the first key.
            Assert.AreEqual(ticks, g.TickCount);
            devices.State.MoveZ = 1f;
            Frames(5);
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

            // A new session finds it, and opens the title over the level after the one that was finished.
            Begin("", present: false);
            Assert.AreEqual(FlowState.Title, runner.Flow.State);
            Assert.AreEqual(2, runner.LevelId);
            Assert.IsTrue(runner.Flow.Progress.IsCompleted(1));
            Shutdown();

            // The bot solving a level leaves no trace in it.
            Begin("?level=0&autoplay=1", present: false);
            for (int i = 0; i < TestHelpers.Ticks(30f) && runner.LevelId == 0; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(1, runner.LevelId, "the bot solved level 0 and the runner moved on");
            Assert.IsFalse(runner.Flow.Progress.IsCompleted(0));
            Assert.AreEqual(2, runner.Flow.Progress.LastPlayed, "nor does it move the bookmark");
        }

        // ------------------------------------------------------------------------------------------
        // The debug keys, R, and the level that begins with nobody there (the final review)
        // ------------------------------------------------------------------------------------------

        // [ ] and P are for looking at levels. In the published game they did what a stray key does: ] on
        // the title put the bookmark on a level that was never unlocked, [ went to the last level, P - the
        // pause key of many games - threw the level away and handed the campaign to the bot.
        [Test]
        public void Runner_TheDebugKeysDoNothing_UnlessTheLaunchAsksForThem()
        {
            Assert.IsFalse(GameRunner.DebugKeysFor(LaunchOptions.Default), "the game as it is published");
            Assert.IsFalse(GameRunner.DebugKeysFor(LaunchOptions.FromUrl("?level=3&plain=1")));
            Assert.IsFalse(GameRunner.DebugKeysFor(LaunchOptions.FromUrl("?debug=0")));
            Assert.IsTrue(GameRunner.DebugKeysFor(LaunchOptions.FromUrl("?debug=1")));
            Assert.IsTrue(GameRunner.DebugKeysFor(LaunchOptions.FromUrl("?level=2&debug")), "a bare debug counts as on");
            Assert.IsTrue(GameRunner.DebugKeysFor(LaunchOptions.FromUrl("?autoplay=1")), "P takes over from the bot");
            Assert.IsTrue(LaunchOptions.FromUrl("?debug=true").Debug);
            Assert.IsFalse(LaunchOptions.Default.Debug);

            foreach (FlowState state in new[] { FlowState.Title, FlowState.Playing, FlowState.Paused, FlowState.LevelComplete })
            {
                store = new MemoryStore();
                Begin("", present: false, debugKeys: null);
                Assert.IsFalse(runner.DebugKeys);
                Game g = runner.Game;
                if (state != FlowState.Title) runner.Flow.StartLevel(0);
                if (state == FlowState.Paused) runner.Flow.Pause();
                if (state == FlowState.LevelComplete)
                {
                    runner.Flow.AutoAdvance = false;
                    g.CompleteLevel();
                }
                Frames(3);
                Assert.AreEqual(state, runner.Flow.State);
                int bookmark = runner.Flow.Progress.LastPlayed, writes = store.Count, ticks = g.LevelTicks, restarts = 0, loaded = 0;
                g.Events.LevelRestarted += e => restarts++;
                g.Events.LevelLoaded += e => loaded++;
                foreach (int key in new[] { 0, 1, 2, 2 })
                {
                    devices.State.NextLevelPressed = key == 0;
                    devices.State.PreviousLevelPressed = key == 1;
                    devices.State.AutoplayPressed = key == 2;
                    runner.Frame(Sim.Dt);
                    Assert.AreEqual(state, runner.Flow.State, state + ", key " + key);
                    Assert.AreEqual(0, runner.LevelId, state + ", key " + key);
                    Assert.IsFalse(runner.Autoplay, state + ", key " + key);
                    Assert.AreSame(runner.Human, g.Input);
                }
                Assert.AreEqual(0, restarts + loaded, state + ": nothing was loaded or started over");
                Assert.AreEqual(bookmark, runner.Flow.Progress.LastPlayed, state + ": the bookmark is where it was");
                Assert.AreEqual(writes, store.Count, state + ": nothing was written");
                if (state == FlowState.Playing) Assert.AreEqual(ticks + 4, g.LevelTicks, "the level goes on undisturbed");
                Shutdown();
            }

            // Asked for, they are what they were.
            store = new MemoryStore();
            Begin("?debug=1", present: false, debugKeys: null);
            Assert.IsTrue(runner.DebugKeys);
            devices.State.NextLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(1, runner.LevelId);
            devices.State.PreviousLevelPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(0, runner.LevelId);
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(runner.Autoplay);
            Shutdown();

            // And a launch that starts with the bot has the key that takes over from it.
            Begin("?autoplay=1", present: false, debugKeys: null);
            Assert.IsTrue(runner.DebugKeys);
            Assert.IsTrue(runner.Autoplay);
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsFalse(runner.Autoplay);
            Assert.AreSame(runner.Human, runner.Game.Input);
        }

        // P, a while of the bot, P again and a walk into the exit: that run was kept as the player's best
        // (the review: "best 1.15, pick-ups 1, level 2 unlocked"). A level the bot has had its hands on is
        // nobody's progress; the next one, played from its start, is the player's again.
        [Test]
        public void Runner_ALevelTheBotHasPlayedPartOf_IsNotThePlayersCompletion()
        {
            Begin("?level=1", present: false);
            Game g = runner.Game;
            Frames(10);
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(runner.Autoplay);
            Assert.IsFalse(runner.Flow.RecordProgress);
            Frames(40);
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsFalse(runner.Autoplay, "the player has taken over");
            Assert.AreSame(runner.Human, g.Input);
            Assert.IsFalse(runner.Flow.RecordProgress, "but the level is not theirs");
            Frames(5);
            int written = store.Count;
            g.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            Assert.IsFalse(runner.Flow.Progress.IsCompleted(1));
            Assert.Less(runner.Flow.Progress.BestTime(1), 0f);
            Assert.IsFalse(runner.Flow.LastCompletionWasBest);
            Assert.AreEqual(written, store.Count, "nothing was written");

            // Again on the card: the level from its start, by the player alone.
            devices.State.RestartPressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.IsTrue(runner.Flow.RecordProgress);
            Frames(30);
            g.CompleteLevel();
            Assert.IsTrue(runner.Flow.Progress.IsCompleted(1));
            Assert.AreEqual(0.5f, runner.Flow.Progress.BestTime(1), 0.02f);
        }

        // R sits between E (pick up) and F (flip), and it used to start the level over on the tick of the
        // press: a slip threw away everything placed so far. While playing it is a hold of half a second
        // now, and a touch says so.
        [Test]
        public void Runner_RStartsTheLevelOver_OnlyWhenItIsHeld()
        {
            Begin("?level=0", present: false);
            Game g = runner.Game;
            int restarts = 0;
            var said = new List<string>();
            g.Events.LevelRestarted += e => restarts++;
            g.Events.Message += e => said.Add(e.Text);
            Frames(30);

            // A touch: a frame or two of R.
            devices.State.RestartPressed = true;
            devices.State.RestartHeld = true;
            runner.Frame(Sim.Dt);
            runner.Frame(Sim.Dt);
            devices.State.RestartHeld = false;
            Frames(60);
            Assert.AreEqual(0, restarts, "a touch of R restarts nothing");
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.Greater(g.LevelTicks, 80, "the level went on");
            CollectionAssert.AreEqual(new[] { GameRunner.HoldRestartLine }, said, "and is told what R is");

            // Three touches in a row are three touches, not a hold.
            for (int i = 0; i < 3; i++)
            {
                devices.State.RestartPressed = true;
                devices.State.RestartHeld = true;
                Frames(12);
                devices.State.RestartHeld = false;
                Frames(3);
            }
            Assert.AreEqual(0, restarts);

            // Held: nothing for 0.4 s, then one restart - and no second one, however long it stays down.
            devices.State.RestartPressed = true;
            devices.State.RestartHeld = true;
            Frames(TestHelpers.Ticks(0.4f));
            Assert.AreEqual(0, restarts, "not yet");
            Assert.That(runner.Human.RestartHold, Is.InRange(0.7f, 0.9f), "the hold can be shown");
            Frames(TestHelpers.Ticks(0.2f));
            Assert.AreEqual(1, restarts, "held for " + HumanInput.RestartHoldSeconds + " s: the level starts over");
            Assert.Less(g.LevelTicks, 10);
            Assert.AreEqual(0f, runner.Human.RestartHold);
            Frames(TestHelpers.Ticks(2f));
            Assert.AreEqual(1, restarts, "one hold, one restart");
            devices.State.RestartHeld = false;
            Frames(2);

            // A frame that took seconds (the tab was hidden) is not a held key.
            devices.State.RestartPressed = true;
            devices.State.RestartHeld = true;
            runner.Frame(Sim.Dt);
            runner.Frame(30f);
            Assert.AreEqual(1, restarts);
            devices.State.RestartHeld = false;
            Frames(2);

            // On the pause card R is a choice, not a slip: at once, as before. And the R that is still down
            // from there is not the start of a hold in the level.
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            devices.State.RestartPressed = true;
            devices.State.RestartHeld = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(2, restarts);
            Frames(TestHelpers.Ticks(1.5f));
            Assert.AreEqual(2, restarts, "R held on from the pause card does not restart again");
            devices.State.RestartHeld = false;
            Frames(2);

            // While the bot plays, R is not the player's to press.
            runner.SetAutoplay(true);
            int before = restarts;
            devices.State.RestartPressed = true;
            devices.State.RestartHeld = true;
            Frames(TestHelpers.Ticks(1f));
            Assert.AreEqual(before, restarts);
        }

        // The card that moves on by itself used to start the next level's clock with nobody there: thirty
        // seconds at the kettle were thirty seconds of the level's first - and so best - time. Now a level
        // that begins without a key, a click or a move of the mouse stands still until there is one.
        [Test]
        public void Runner_ALevelTheCardMovedOnToByItself_WaitsForItsPlayer()
        {
            Begin("?level=0", present: false);
            Game g = runner.Game;
            // The browser gives no pointer without a click.
            devices.RefuseLock = true;
            Frames(10);
            g.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            Frames(TestHelpers.Ticks(GameRunner.AdvanceDelay) + 2);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State, "the countdown ran out");
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsTrue(runner.AwaitingPlayer);

            // Thirty seconds of nobody.
            Frames(TestHelpers.Ticks(30f));
            Assert.AreEqual(0, g.LevelTicks, "the level's clock has not started");
            Assert.AreEqual(0f, g.Time);
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);

            // The mouse moves: here they are.
            devices.State.Look = new Vector2(3f, 0f);
            runner.Frame(Sim.Dt);
            Assert.IsFalse(runner.AwaitingPlayer);
            Frames(29);
            Assert.AreEqual(30, g.LevelTicks);
            g.CompleteLevel();
            Assert.AreEqual(0.5f, runner.Flow.Progress.BestTime(1), 1e-3f, "half a second of play is what is kept");
            Assert.AreEqual(0.5f, runner.Flow.LastCompletion.Time, 1e-3f);
        }

        [Test]
        public void Runner_ALevelSomebodyMovedOnTo_OrTheBotPlays_StartsAtOnce()
        {
            // A key held through the countdown: somebody is there.
            Begin("?level=0", present: false);
            Game g = runner.Game;
            Frames(10);
            g.CompleteLevel();
            devices.State.MoveZ = 1f;
            Frames(TestHelpers.Ticks(GameRunner.AdvanceDelay) + 2);
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsFalse(runner.AwaitingPlayer);
            Assert.Greater(g.LevelTicks, 0);
            Shutdown();

            // Next on the card, by a click.
            Begin("?level=0", withMenus: true);
            g = runner.Game;
            runner.Flow.AutoAdvance = false;
            Frames(10);
            g.CompleteLevel();
            Frames(20);
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            // (The menu presses its Next pill in the frame the click arrives in.)
            MenuProbe probe = runner.Presentation.Get<MenuProbe>();
            probe.OnFrame = () => runner.Flow.Advance();
            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            probe.OnFrame = null;
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsFalse(runner.AwaitingPlayer, "a click is a sign of life");
            Frames(5);
            Assert.AreEqual(5, g.LevelTicks);
            Shutdown();

            // The bot needs nobody.
            Begin("?level=0&autoplay=1", present: false);
            for (int i = 0; i < TestHelpers.Ticks(40f) && runner.LevelId == 0; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsFalse(runner.AwaitingPlayer);
            int ticks = runner.Game.LevelTicks;
            Frames(10);
            Assert.AreEqual(ticks + 10, runner.Game.LevelTicks);
            Shutdown();

            // And a level that is started over, or chosen, while another one waits does not wait.
            Begin("?level=0", present: false);
            g = runner.Game;
            Frames(10);
            g.CompleteLevel();
            Frames(TestHelpers.Ticks(GameRunner.AdvanceDelay) + 2);
            Assert.IsTrue(runner.AwaitingPlayer);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            Assert.IsFalse(runner.AwaitingPlayer);
            runner.Flow.Resume();
            Frames(5);
            Assert.AreEqual(5, g.LevelTicks);
        }

    }
}
