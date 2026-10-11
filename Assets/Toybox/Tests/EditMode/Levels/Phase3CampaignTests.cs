using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Levels 10 to 15 as the last part of the campaign (LEVELS.md, phase 3), and the campaign as a whole
    /// now that it is complete: fifteen levels in order from the title to an ending - the last level's card
    /// says "Finish" and leads to the ending screen, never back round to level 1 - with the catalogue full,
    /// everything saved, and a fresh game after it starting at level 1 with the catalogue kept. The bot
    /// plays all fifteen in one game, by autoplay, and arrives at the ending.
    ///
    /// Then the things the six levels have to agree on with each other and with the nine before them: the
    /// hero toy under the crosshair at the start, in a candy colour the room allows; three hints that fit
    /// the pause card at full size; the same words for the same thing.
    /// Each level's own behaviour is in LevelNNTests; the first two parts are Phase1CampaignTests and
    /// Phase2CampaignTests; the ending screen itself is in UiTests (MenuTests) and GameFlowTests.
    /// </summary>
    public class Phase3CampaignTests
    {
        static readonly int[] Batch = { 10, 11, 12, 13, 14, 15 };
        static readonly string[] Slugs = { "matryoshka-boxes", "blocking-lasers", "the-keyhole", "fishbowl", "infinite-hallway", "rube-goldberg" };
        static readonly string[] Titles = { "Matryoshka Boxes", "Blocking Lasers", "The Keyhole", "Escaping the Fishbowl", "The Infinite Hallway", "The Rube Goldberg Machine" };
        static readonly string[] Rooms = { "cardboard-box", "pegboard-workbench", "high-shelf", "cardboard-box", "night-light", "night-light" };
        static readonly ToyId[] Heroes = { ToyId.GiftBox, ToyId.PlayingCard, ToyId.Key, ToyId.Sponge, ToyId.Doorway, ToyId.DominoSet };
        static readonly UiGlyph[] Glyphs = { UiGlyph.GiftBox, UiGlyph.Card, UiGlyph.Key, UiGlyph.Sponge, UiGlyph.Doorway, UiGlyph.Gear };
        /// <summary>
        /// How many toys can be picked up at the start: the one gift box (the two inside it come out later),
        /// the card, the key and the spool, the sponge, the door, and the machine's four parts.
        /// </summary>
        static readonly int[] Toys = { 1, 1, 2, 1, 1, 4 };

        /// <summary>The whole campaign.</summary>
        public const int Count = 15;

        GameObject host;
        GameRunner runner;
        FakeDevices devices;
        MemoryStore store;
        MenuPresenter menu;
        Game game;

        [SetUp]
        public void NewStores()
        {
            store = new MemoryStore();
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
        }

        [TearDown]
        public void Shutdown()
        {
            End();
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        // The real runner with the real level list, the game's own HUD and menus, fake devices and a store in memory.
        void Begin(string url)
        {
            host = new GameObject("Campaign Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions { Devices = devices, Store = store, Presenters = UiTestKit.Own() });
            menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
        }

        // The end of a session: the runner shuts down as it does when the page is left.
        void End()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            menu = null;
        }

        void Frames(int count = 1)
        {
            for (int i = 0; i < count; i++) runner.Frame(Sim.Dt);
        }

        void Seconds(float seconds) => Frames(TestHelpers.Ticks(seconds));

        void Press(UiButton button)
        {
            button.Activate();
            Frames();
        }

        FlowState State => runner.Flow.State;

        // ---- Order ---------------------------------------------------------------------------------------

        [Test]
        public void TheCampaign_IsFifteenLevels_InOrder_AndPhaseThreeClosesIt()
        {
            LevelList levels = LevelList.FromRegistry();
            Assert.AreEqual(Count, levels.Campaign.Count, "fifteen levels");
            for (int i = 0; i < Count; i++) Assert.AreEqual(i + 1, levels.Campaign[i], "place " + (i + 1) + " of the campaign");
            Assert.AreEqual(1, levels.First, "the game begins at level 1");
            Assert.AreEqual(Count, levels.Last, "and ends with level 15");
            Assert.AreEqual(Batch[0], levels.After(9), "phase 3 follows phase 2");
            for (int i = 0; i < Batch.Length; i++)
            {
                int id = Batch[i];
                LevelEntry entry = LevelRegistry.Find(id);
                Assert.IsNotNull(entry, "level " + id + " is registered");
                Assert.AreEqual(3, entry.Phase, "level " + id + " is a phase 3 level");
                Assert.AreEqual(Slugs[i], entry.Slug);
                Assert.AreEqual(Titles[i], entry.Title);
                LevelDefinition level = levels.Create(id);
                Assert.AreEqual(Rooms[i], level.Environment, Titles[i] + ": the room the art bible gives it");
                Assert.AreEqual(Rooms[i], EnvironmentPreset.KeyForLevel(id, true));
                Assert.AreEqual(id, levels.Resolve(LaunchOptions.FromUrl("?level=" + Slugs[i])), "reachable by its slug");
                Assert.AreEqual(id, levels.Resolve(LaunchOptions.FromUrl("?level=" + id)), "and by its number");
                Assert.IsTrue(levels.InCampaign(id));
                if (i + 1 < Batch.Length) Assert.AreEqual(Batch[i + 1], levels.After(id), "after level " + id);
                Assert.AreEqual(Heroes[i], ToyCatalog.HeroOf(id).Id, Titles[i] + ": its hero toy");
                Assert.AreEqual(Glyphs[i], UiAtlas.GlyphFor(Slugs[i], id), Titles[i] + ": and that toy's picture on its card");
            }
            // Every campaign level is one of the three phases, in step with its number.
            for (int id = 1; id <= Count; id++)
                Assert.AreEqual(id <= 4 ? 1 : id <= 9 ? 2 : 3, LevelRegistry.Find(id).Phase, "level " + id);
            // The sandbox and the two showrooms are reached by address only.
            foreach (int id in levels.Ids)
                if (id < 1 || id > Count) Assert.IsFalse(levels.InCampaign(id), "level " + id + " is no part of the campaign");
        }

        // ---- From the title to the ending ------------------------------------------------------------------

        // Completes the level that is up (as its exit would) and waits for the card's buttons.
        void Complete(float after = 0.4f)
        {
            Seconds(after);
            runner.Game.CompleteLevel();
            Frames();
            Assert.AreEqual(FlowState.LevelComplete, State);
            Seconds(0.8f);
        }

        [Test]
        public void FromTheTitle_ThroughAllFifteen_ToTheEnding_AndAFreshGameStartsAtLevelOne_WithTheCatalogueKept()
        {
            Begin("");
            LevelList levels = runner.Levels;
            Assert.AreEqual(FlowState.Title, State, "a fresh game opens on the title");
            Assert.AreEqual(1, runner.LevelId, "with level 1 standing behind it");
            Press(menu.Title.Play);
            Assert.AreEqual(FlowState.Playing, State);

            var loaded = new List<int>();
            runner.Game.Events.LevelLoaded += e => loaded.Add(e.Id);
            float total = 0f;
            for (int id = 1; id <= Count; id++)
            {
                Assert.AreEqual(id, runner.LevelId, "the level that is up");
                Assert.AreEqual(LevelRegistry.Find(id).Title, runner.Game.Level.Title);
                Assert.IsFalse(runner.Flow.Progress.IsCompleted(id));
                Complete(0.2f + 0.1f * id);
                total += runner.Flow.LastCompletion.Time;
                Assert.AreSame(menu.Complete, menu.Current);
                Assert.AreEqual(LevelRegistry.Find(id).Title.ToUpperInvariant(), menu.Complete.Title, "the card names the level that was collected");
                Assert.IsTrue(runner.Flow.Progress.IsCompleted(id), "and it is collected");
                if (id < Count)
                {
                    Assert.AreEqual(CompleteMenu.NextLabel, menu.Complete.Next.Label.text, "level " + id);
                    Assert.IsTrue(runner.Flow.Progress.IsUnlocked(id + 1, levels), "the next level is open now");
                    Press(menu.Complete.Next);
                    Assert.AreEqual(FlowState.Playing, State, "Next plays on");
                    Assert.AreEqual(id + 1, runner.LevelId, "with the level after level " + id);
                    Assert.AreEqual(id + 1, runner.Flow.Progress.LastPlayed);
                }
            }
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, loaded, "each level once, in order");

            // The last level's card: "Finish". It leads to the ending - nothing is loaded, nothing wraps round.
            Assert.AreEqual(CompleteMenu.FinishLabel, menu.Complete.Next.Label.text);
            Press(menu.Complete.Next);
            Assert.AreEqual(FlowState.Ending, State);
            Assert.AreEqual(Count, runner.LevelId);
            Assert.AreEqual(14, loaded.Count, "level 1 was not loaded again");
            EndingMenu ending = menu.Ending;
            Assert.AreSame(ending, menu.Current);
            Seconds(ending.InfoAt + 0.3f);
            Assert.AreEqual("THE END", ending.HeadingText);
            Assert.AreEqual("15 OF 15 COLLECTED", ending.CountText);
            Assert.IsTrue(ending.Stamp.Tween.IsShown, "the catalogue is full: COLLECTED");
            Assert.AreEqual(EndingMenu.Thanks, ending.ThanksText);
            Assert.AreEqual(EndingMenu.FullLine, ending.LineText);
            StringAssert.Contains(UiKit.Time(total), ending.StatsText, "the fifteen times added up");
            StringAssert.Contains("0 pick-ups", ending.StatsText, "nothing was picked up in this run");
            Assert.AreEqual(Count, ending.Entries.Count);
            for (int id = 1; id <= Count; id++)
            {
                // The ending's cards are the catalogue's: the level's room, its number, its hero toy in the room's hero colour.
                EndingMenu.Entry card = ending.Find(id);
                LevelEntry entry = LevelRegistry.Find(id);
                Assert.IsTrue(card.Collected && card.Card.Tween.IsShown, entry.Title);
                Dip dip = Palette.DipOf(EnvironmentPreset.KeyForLevel(id, true));
                Assert.AreSame(dip, card.Dip, entry.Title + ": the card is the colour of the level's room");
                Assert.AreEqual(UiAtlas.GlyphFor(entry.Slug, id), card.Hero, entry.Title + ": its hero toy");
                Assert.AreSame(UiAtlas.Sprite(card.Hero), card.Glyph.sprite);
                Assert.IsTrue(Palette.Same(dip.Hero, card.Glyph.color), entry.Title + ": in the room's hero colour");
            }

            // What is saved: fifteen collected, the ending counted, the bookmark back on level 1.
            Progress progress = runner.Flow.Progress;
            Assert.AreEqual(Count, progress.CompletedCount(levels.Campaign));
            Assert.AreEqual(1, progress.Endings);
            Assert.AreEqual(1, progress.LastPlayed);
            Assert.AreEqual(total, progress.TotalTime(levels.Campaign), 1e-3f);
            int saves = store.Saves;
            Assert.Greater(saves, Count, "every completion and the ending are written through");

            // The catalogue from the ending: fifteen cards, all collected, the batch with its hero toys.
            Press(ending.LevelsButton);
            Assert.AreEqual(FlowState.LevelSelect, State);
            CatalogueMenu catalogue = menu.Catalogue;
            Assert.AreEqual(Count, catalogue.Entries.Count, "one card for each level of the campaign");
            Assert.AreEqual("15 of 15 collected", catalogue.CountText);
            for (int i = 0; i < Batch.Length; i++)
            {
                CatalogueMenu.Entry card = catalogue.Find(Batch[i]);
                Assert.IsNotNull(card, "a card for level " + Batch[i]);
                Assert.IsTrue(card.Unlocked && card.Completed, Titles[i] + " is collected");
                Assert.AreEqual(Titles[i], card.Name.text);
                Assert.AreEqual(Batch[i].ToString("00"), card.Number.text);
                Assert.AreEqual(Glyphs[i], card.Hero, Titles[i] + ": the hero toy on its card");
                Assert.AreSame(UiAtlas.Sprite(Glyphs[i]), card.Glyph.sprite);
                Dip dip = Palette.DipOf(Rooms[i]);
                Assert.AreSame(dip, card.Dip, "the card is the colour of the level's room");
                Assert.IsTrue(Palette.Same(dip.Hero, card.Glyph.color), "and the toy on it the room's hero colour");
                Assert.IsTrue(card.Stamp.Tween.IsShown, "with its COLLECTED sticker");
                Assert.IsNotEmpty(card.Best.text, "and a best time");
            }
            menu.Cancel();
            Frames();
            Assert.AreEqual(FlowState.Ending, State, "Back returns to the ending");
            Seconds(menu.Ending.InfoAt + 0.2f);

            // Back to the title: it stands over level 1.
            Press(menu.Ending.TitleButton);
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(1, runner.LevelId);
            End();

            // Another session with the same store: a fresh game starts at level 1, and the catalogue is kept.
            Begin("");
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(1, runner.LevelId, "the title stands over the first level");
            Press(menu.Title.LevelsButton);
            Assert.AreEqual("15 of 15 collected", menu.Catalogue.CountText);
            foreach (CatalogueMenu.Entry card in menu.Catalogue.Entries)
            {
                Assert.IsTrue(card.Unlocked && card.Completed, "level " + card.Id + " is still collected");
                Assert.IsTrue(card.Button.Interactable, "and can be played again from its card");
            }
            menu.Cancel();
            Frames();
            Press(menu.Title.Play);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(1, runner.LevelId, "Click to play: level 1");
            Assert.AreEqual(1, runner.Flow.Progress.Endings);

            // Playing on from there does not end the game early: after level 1 comes level 2.
            Complete();
            Assert.AreEqual(CompleteMenu.NextLabel, menu.Complete.Next.Label.text);
            Press(menu.Complete.Next);
            Assert.AreEqual(2, runner.LevelId);
            Assert.AreEqual(2, runner.Flow.Progress.LastPlayed);
        }

        [Test]
        public void Progress_IsWrittenToTheStore_AndANewSessionContinuesInPhaseThree()
        {
            Begin("");
            Press(menu.Title.Play);
            for (int id = 1; id <= 11; id++)
            {
                Complete();
                Press(menu.Complete.Next);
            }
            Assert.AreEqual(12, runner.LevelId);
            Assert.Greater(store.Saves, 0, "every completion is written through (the browser only keeps what is saved)");
            End();

            // The same store, another session: the title stands over the level the player had reached.
            Begin("");
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(12, runner.LevelId, "the level to continue with");
            Progress progress = runner.Flow.Progress;
            for (int id = 1; id <= 11; id++) Assert.IsTrue(progress.IsCompleted(id), "level " + id + " is collected");
            Assert.IsFalse(progress.IsCompleted(12));
            Assert.AreEqual(0, progress.Endings);
            Press(menu.Title.LevelsButton);
            CatalogueMenu catalogue = menu.Catalogue;
            StringAssert.StartsWith("11 of 15", catalogue.CountText);
            Assert.IsTrue(catalogue.Find(10).Completed && catalogue.Find(11).Completed);
            Assert.IsTrue(catalogue.Find(12).Unlocked, "the level after the last one collected is open");
            Assert.IsFalse(catalogue.Find(12).Completed);
            Assert.IsFalse(catalogue.Find(13).Unlocked, "the one after that is not");
            Assert.IsFalse(catalogue.Find(13).Button.Interactable);
            Assert.IsFalse(catalogue.Find(15).Unlocked);

            // A card that is open starts its level.
            Press(catalogue.Find(10).Button);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(10, runner.LevelId);
            End();

            // Somebody who goes straight to the last level by its address still gets an ending - an honest one.
            store = new MemoryStore();
            Begin("?level=15");
            Complete();
            Press(menu.Complete.Next);
            Assert.AreEqual(FlowState.Ending, State);
            Seconds(menu.Ending.InfoAt + 0.2f);
            Assert.AreEqual("1 OF 15 COLLECTED", menu.Ending.CountText);
            Assert.IsFalse(menu.Ending.Stamp.Tween.IsShown);
            Assert.AreEqual("14 toys are still waiting in the catalogue.", menu.Ending.LineText);
        }

        // ---- The bot plays the batch, and the whole campaign -----------------------------------------------

        /// <summary>
        /// Game seconds from the load to LevelCompleted with each level's own solver, levels 1 to 15 (measured;
        /// half a second either way is allowed). LEVELS.md, Appendices D, E and F.
        /// </summary>
        public static readonly float[] SolveSeconds =
        {
            12.63f, 9.28f, 3.77f, 14.18f, 17.35f, 12.12f, 3.68f, 20.5f, 11.75f, 17.53f, 6.1f, 13.97f, 14.32f, 9.95f, 40.75f,
        };

        /// <summary>
        /// The size of each toy when the batch's solvers are done (measured; 2 % either way): what can be
        /// picked up, in the order of the level's props - and, where the level has taken its toys out of the
        /// player's hands by then, the toys by their tags (Level 12: key in the lock, spool in the drawer;
        /// Level 15: dominoes, ball, ruler, fan in the machine).
        /// </summary>
        static readonly float[][] SolverScales =
        {
            new[] { 1.912f, 0.971f, 1.081f }, new[] { 5.947f }, new[] { 0.15f, 9.162f }, new[] { 1.862f }, new[] { 1.319f }, new[] { 3.103f, 3.0f, 1.012f, 0.924f },
        };

        // One Game, the six levels one after the other, as the runner loads them: nothing a level leaves
        // behind (ignored collisions, cached meshes, physics materials, subscriptions) may reach the next.
        // Then the same again, backwards, to catch what only shows on a second visit.
        [Test]
        public void TheBotSolvesTheSixLevels_OneAfterTheOther_InOneGame()
        {
            game = Game.Create();
            var order = new List<int>(Batch);
            for (int i = Batch.Length - 1; i >= 0; i--) order.Add(Batch[i]);
            var report = new System.Text.StringBuilder();
            var first = new float[Batch.Length];
            for (int visit = 0; visit < order.Count; visit++)
            {
                int id = order[visit];
                int index = Array.IndexOf(Batch, id);
                game.LoadLevel(id);
                Assert.AreEqual(Titles[index], game.Level.Title);
                int completed = 0;
                float solved = -1f;
                var said = new List<string>();
                Action<LevelEvent> onCompleted = e =>
                {
                    completed++;
                    solved = e.Time;
                };
                Action<MessageEvent> onMessage = e => said.Add(e.Text);
                game.Events.LevelCompleted += onCompleted;
                game.Events.Message += onMessage;
                try
                {
                    var bot = new Bot(game);
                    BotRunner.Run(game, game.Level.Solve(bot), 120f);
                    TestHelpers.RunUntil(game, () => game.LevelCompleted, 2f);
                }
                finally
                {
                    game.Events.LevelCompleted -= onCompleted;
                    game.Events.Message -= onMessage;
                }
                Assert.IsTrue(game.LevelCompleted, Titles[index] + ": the solver completes it (bot at " + game.Player.Position + ")");
                Assert.AreEqual(1, completed, Titles[index] + ": completed once");
                var sizes = new List<float>();
                string[] tags = id == 12 ? new[] { Level12Keyhole.KeyTag, Level12Keyhole.SpoolTag }
                    : id == 15 ? new[] { Level15RubeGoldberg.DominoTag, Level15RubeGoldberg.BallTag, Level15RubeGoldberg.RulerTag, Level15RubeGoldberg.FanTag }
                    : null;
                if (tags == null)
                {
                    foreach (Prop prop in game.Props)
                        if (prop.Grabbable && !prop.Removed) sizes.Add(prop.Scale);
                }
                else
                {
                    foreach (string tag in tags)
                        foreach (Prop prop in game.Props)
                            if (prop.HasTag(tag) && !prop.Removed) sizes.Add(prop.Scale);
                }
                report.Append(Titles[index]).Append(": ").Append(solved.ToString("0.00")).Append(" s, toys ").Append(string.Join(" / ", sizes.ConvertAll(x => x.ToString("0.000")))).Append(", said [").Append(string.Join(" | ", said)).Append("]; ");
                Debug.Log("[Toybox] phase 3, " + Titles[index] + ": " + solved.ToString("0.00") + " s, toys " + string.Join(" / ", sizes.ConvertAll(x => x.ToString("0.000"))) + ", said [" + string.Join(" | ", said) + "]");
                Assert.AreEqual(SolveSeconds[id - 1], solved, 0.5f, Titles[index] + ": the solve takes as long as it did when it was measured");
                // What LEVELS.md (Appendix F) says the solver makes of each toy.
                Assert.AreEqual(SolverScales[index].Length, sizes.Count, Titles[index] + ": its toys");
                for (int t = 0; t < sizes.Count; t++)
                    Assert.AreEqual(SolverScales[index][t], sizes[t], SolverScales[index][t] * 0.02f, Titles[index] + ": toy " + (t + 1) + " as the solver left it");
                // The simulation is deterministic: after another level, and on a second visit, the same solve to the tick.
                if (visit < Batch.Length) first[index] = solved;
                else Assert.AreEqual(first[index], solved, 1e-4f, Titles[index] + ": the same solve on the second visit");
            }
            Debug.Log("[Toybox] phase 3 solved in one game: " + report);
        }

        // The whole game, as "?autoplay=1" plays it: the real runner, the HUD and the menus, the bot at the
        // controls from level 1. Every level is completed once, in order, each in the time its own test
        // measures; after the fifteenth nothing is loaded and the ending is up. Nothing of it is saved.
        [Test]
        public void Autoplay_PlaysTheWholeCampaign_OneToFifteen_InOneGame_AndArrivesAtTheEnding()
        {
            Begin("?autoplay=1");
            Assert.IsTrue(runner.Autoplay);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(1, runner.LevelId, "autoplay without a level: from the start");

            var loaded = new List<int> { runner.LevelId };
            var completed = new List<int>();
            var times = new List<float>();
            runner.Game.Events.LevelLoaded += e => loaded.Add(e.Id);
            runner.Game.Events.LevelCompleted += e =>
            {
                completed.Add(e.Id);
                times.Add(e.Time);
            };

            float budget = 0f;
            foreach (float seconds in SolveSeconds) budget += seconds + GameRunner.AdvanceDelay + 6f;
            int frames = 0, limit = TestHelpers.Ticks(budget);
            bool cardSeen = false;
            while (State != FlowState.Ending && frames < limit && runner.AutoplayError == null)
            {
                runner.Frame(Sim.Dt);
                frames++;
                cardSeen |= menu.Current == menu.Complete;
            }
            string trail = "loaded [" + string.Join(", ", loaded) + "], completed [" + string.Join(", ", completed) + "], at level " + runner.LevelId +
                           " (" + (runner.Game.Level != null ? runner.Game.Level.Title : "none") + "), bot at " + runner.Game.Player.Position;
            Assert.IsNull(runner.AutoplayError, "the bot got stuck: " + runner.AutoplayError + "; " + trail);
            Assert.AreEqual(FlowState.Ending, State, "the campaign did not end within " + budget.ToString("0") + " s: " + trail);

            var expected = new int[Count];
            for (int i = 0; i < Count; i++) expected[i] = i + 1;
            CollectionAssert.AreEqual(expected, loaded, "every level loaded once, in order - and level 1 not a second time");
            CollectionAssert.AreEqual(expected, completed, "every level completed once, in order");
            Assert.IsTrue(cardSeen, "with the complete card in between");
            Assert.AreEqual(Count, runner.LevelId, "the last level stays behind the ending");
            var report = new System.Text.StringBuilder();
            float total = 0f;
            for (int i = 0; i < Count; i++)
            {
                report.Append(i + 1).Append(": ").Append(times[i].ToString("0.00")).Append(" s; ");
                total += times[i];
            }
            Debug.Log("[Toybox] the campaign by autoplay in one game: " + report + "total " + total.ToString("0.0") + " s of play, " + frames + " frames");
            for (int i = 0; i < Count; i++)
                Assert.AreEqual(SolveSeconds[i], times[i], 0.5f, LevelRegistry.Find(i + 1).Title + ": as long as its own solver takes");

            // The ending is the bot's: fifteen played, nothing stamped, nothing kept.
            Frames();
            EndingMenu ending = menu.Ending;
            Assert.AreSame(ending, menu.Current);
            Seconds(ending.InfoAt + 0.3f);
            Assert.AreEqual("15 OF 15 PLAYED", ending.CountText);
            Assert.AreEqual(EndingMenu.WatchedThanks, ending.ThanksText);
            Assert.IsFalse(ending.Stamp.Tween.IsShown);
            Assert.IsTrue(ending.Report.Watched);
            Assert.AreEqual(total, ending.Report.Seconds, 1e-2f);
            StringAssert.Contains(UiKit.Time(ending.Report.Seconds), ending.StatsText);
            Assert.GreaterOrEqual(ending.Report.PickUps, 15, "the bot's pick-ups are counted in its own ending");
            Assert.AreEqual(0, store.Count, "and the bot leaves nothing in the player's store");
            Seconds(8f);
            Assert.AreEqual(FlowState.Ending, State, "the ending stays up");
            Assert.AreEqual(Count, loaded.Count);

            // P hands the game back: the player is in the last level, at its start, and the store is still empty.
            devices.State.AutoplayPressed = true;
            Frames();
            Assert.IsFalse(runner.Autoplay);
            Assert.AreEqual(FlowState.Ending, State, "turning the bot off changes nothing by itself");
            Press(ending.Again);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(1, runner.LevelId, "Play again: level 1");
            Assert.AreSame(runner.Human, runner.Game.Input);
        }

        // LEVELS, Level 15: "The bell rings and the night preset turns to dawn." The last thing the game shows
        // of a level is a room that is waking: night until the machine is done, morning by the time the
        // player is out of the door.
        [Test]
        public void TheLastLevel_EndsInTheMorning_TheNightTurnsToDawnWhenTheAlarmRings()
        {
            game = Game.Create();
            game.LoadLevel(Count);
            Presentation presentation = Presentation.Create(game, new PresentationOptions { Presenters = RoomFixture.Only(typeof(LightingRig)) });
            try
            {
                var rig = presentation.Get<LightingRig>();
                Assert.IsNotNull(rig);
                Assert.IsTrue(game.Environment.Preset.Night, "the last level is a night level");
                float rang = -1f, before = 0f;
                game.Events.MachineDone += e => rang = game.Time;
                var bot = new Bot(game);
                var script = new BotRunner(game.Level.Solve(bot));
                for (int tick = 0; tick < TestHelpers.Ticks(90f) && !game.LevelCompleted; tick++)
                {
                    if (script != null && !script.Advance()) script = null;
                    game.Tick();
                    presentation.Frame(Sim.Dt, 1f);
                    if (rang < 0f) before = Mathf.Max(before, rig.Dawn);
                }
                Assert.IsTrue(game.LevelCompleted, "the solver completes the level");
                Assert.Greater(rang, 0f, "the alarm rang");
                Assert.AreEqual(0f, before, "night until then");
                Assert.Greater(game.Time - rang, LightingRig.DawnSeconds, "the walk to the door is longer than the dawn");
                Assert.AreEqual(1f, rig.Dawn, "morning by the time the player is out");
                Assert.AreEqual(1f, rig.Values.ToyGlowGain, 1e-5f, "the toys have handed their light back");
                Debug.Log("[Toybox] level 15: the alarm rang at " + rang.ToString("0.00") + " s, out at " + game.Time.ToString("0.00") + " s");

                // Played again, it is night again.
                game.RestartLevel();
                presentation.Frame(Sim.Dt, 1f);
                Assert.AreEqual(0f, rig.Dawn);
            }
            finally
            {
                presentation.Dispose();
            }
        }

        // When every solver picks its toys up and lets them go: the times the contact sheets are taken at
        // (tools/out/shots/phase-3). Writes tools/out/notes/phase-3-holds.txt.
        [Test, Explicit]
        public void Tour_HoldTimes()
        {
            game = Game.Create();
            var table = new System.Text.StringBuilder();
            for (int id = 1; id <= Count; id++)
            {
                game.LoadLevel(id);
                table.AppendLine("Level " + id + " " + game.Level.Title);
                Action<PropHoldEvent> onGrab = e => table.AppendLine("  " + game.Time.ToString("0.00") + "  pick up " + e.Prop.Name + " at " + e.OldScale.ToString("0.000") + " from " + e.GrabDistance.ToString("0.00"));
                Action<PropHoldEvent> onDrop = e => table.AppendLine("  " + game.Time.ToString("0.00") + "  let go  " + e.Prop.Name + " at " + e.NewScale.ToString("0.000") + " at " + e.DropDistance.ToString("0.00"));
                Action<LevelEvent> onDone = e => table.AppendLine("  " + e.Time.ToString("0.00") + "  completed");
                game.Events.PropGrabbed += onGrab;
                game.Events.PropDropped += onDrop;
                game.Events.LevelCompleted += onDone;
                try
                {
                    var bot = new Bot(game);
                    BotRunner.Run(game, game.Level.Solve(bot), 120f);
                    TestHelpers.RunUntil(game, () => game.LevelCompleted, 2f);
                }
                finally
                {
                    game.Events.PropGrabbed -= onGrab;
                    game.Events.PropDropped -= onDrop;
                    game.Events.LevelCompleted -= onDone;
                }
            }
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "tools/out/notes/phase-3-holds.txt");
            System.IO.File.WriteAllText(path, table.ToString());
            Debug.Log("[Toybox] hold times written to " + path);
        }

        // ---- What the six levels agree on ------------------------------------------------------------------

        LevelDefinition Load(int id)
        {
            game?.Dispose();
            game = Game.Create();
            game.LoadLevel(id);
            return game.Level;
        }

        [Test]
        public void EachLevel_ShowsItsHeroToyUnderTheCrosshairAtTheStart_InACandyColourTheRoomAllows()
        {
            for (int i = 0; i < Batch.Length; i++)
            {
                LevelDefinition level = Load(Batch[i]);
                ToyDef hero = ToyCatalog.HeroOf(Batch[i]);
                Assert.AreEqual(Heroes[i], hero.Id, Titles[i]);

                var toys = new List<Prop>();
                foreach (Prop prop in game.Props)
                    if (prop.Grabbable && prop.GameObject.activeInHierarchy) toys.Add(prop);
                Assert.AreEqual(Toys[i], toys.Count, Titles[i] + ": what can be picked up at the start");

                Dip dip = Palette.DipOf(level.Environment);
                Player player = game.Player;
                Prop heroToy = null;
                foreach (Prop toy in toys)
                {
                    // Candy colour, rim and pool mean "you can lift this" (ART_BIBLE 2.5): everything that can be
                    // picked up wears a candy colour, and never the one the room bans.
                    ToyInfo info = ToyInfo.Of(toy.GameObject);
                    Assert.IsNotNull(info, Titles[i] + ": " + toy.Name + " is tagged for the look");
                    Assert.IsTrue(Palette.IsCandy(info.Candy), Titles[i] + ": " + toy.Name + " wears a candy colour");
                    Assert.IsTrue(dip.Allows(info.Candy), Titles[i] + ": " + toy.Name + " does not wear the room's banned colour");
                    if (info.Recipe == hero.Recipe && toy.Name == hero.Name && heroToy == null) heroToy = toy;
                }
                Assert.IsNotNull(heroToy, Titles[i] + ": the level's hero toy (" + hero.Name + ") is one of them");

                // The first picture has the hero toy under the crosshair, and the first click picks it up. (A toy
                // that fills the lower half of the picture - Level 13's sponge, two units big and a step away - has
                // the crosshair on its far edge and its middle well below it.)
                float off = Vector3.Angle(player.Forward, heroToy.Center - player.Eye);
                Assert.Less(off, Batch[i] == 13 ? 25f : 12f, Titles[i] + ": the toy is " + off.ToString("0") + " degrees off the crosshair at the start");
                Assert.AreSame(heroToy, game.Grabber.FindTarget(), Titles[i] + ": a click at the start picks the toy up");

                // One way out, in the exit's own language (the presenter draws every exit the same way).
                Assert.AreEqual(1, game.Exits.Count, Titles[i] + ": one exit");
                Assert.IsFalse(game.LevelCompleted);
            }
        }

        // Every sentence a level can say: its blurb, its hints, and its public lines (constants with a space in them).
        static List<string> Sentences(LevelDefinition level, out List<string> lines)
        {
            var all = new List<string> { level.Blurb };
            all.AddRange(level.Hints);
            lines = new List<string>();
            foreach (FieldInfo field in level.GetType().GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text != null && text.Contains(" ")) lines.Add(text);
            }
            all.AddRange(lines);
            return all;
        }

        [Test]
        public void TheSixLevels_SpeakWithOneVoice_AndWithTheNineBeforeThem()
        {
            int lineCount = 0;
            for (int i = 0; i < Batch.Length; i++)
            {
                LevelDefinition level = LevelRegistry.Get(Batch[i]);
                Assert.AreEqual(3, level.Hints.Length, Titles[i] + ": three hints, from a nudge to the recipe");
                Assert.IsNotEmpty(level.Blurb);

                List<string> all = Sentences(level, out List<string> lines);
                lineCount += lines.Count;
                foreach (string text in all)
                {
                    foreach (char c in text)
                        Assert.IsTrue(c >= ' ' && c <= '~', Titles[i] + ": \"" + text + "\" has a character the UI font may not have ('" + c + "')");
                    Assert.IsTrue(text.EndsWith(".") || text.EndsWith("?") || text.EndsWith("!"), Titles[i] + ": \"" + text + "\" is a sentence");
                    Assert.AreEqual(text.Trim(), text);
                    StringAssert.DoesNotContain("  ", text);
                    // One verb for one act: toys are picked up and let go.
                    string lower = text.ToLowerInvariant();
                    Assert.IsFalse(lower.Contains("grab"), Titles[i] + ": \"" + text + "\" - the levels say \"pick up\"");
                    Assert.IsFalse(lower.Contains("take it") || lower.Contains("taken"), Titles[i] + ": \"" + text + "\" - the levels say \"pick up\"");
                    Assert.IsFalse(lower.Contains("drop") || lower.Contains("release"), Titles[i] + ": \"" + text + "\" - the levels say \"let go\"");
                }
                foreach (string line in lines)
                    Assert.LessOrEqual(line.Length, Phase2CampaignTests.LongestLine, Titles[i] + ": \"" + line + "\" is too long for a toast");
                // The first hint is a nudge: it says nothing of where to stand. The last is the recipe: it begins
                // with the pick-up and says when to let go.
                string nudge = level.Hints[0].ToLowerInvariant();
                Assert.IsFalse(nudge.Contains("shoe prints") || nudge.Contains("edge") || nudge.Contains("step back") || nudge.Contains("stand on") || nudge.Contains("stand at"),
                    Titles[i] + ": the first hint gives the place away: \"" + level.Hints[0] + "\"");
                StringAssert.Contains("let go", level.Hints[2].ToLowerInvariant(), Titles[i] + ": the last hint says when to let go");
                StringAssert.StartsWith("Pick", level.Hints[2], Titles[i] + ": and begins with the pick-up");

                // The sentence for a pick-up from too far away is Level 1's, word for word, in every level.
                FieldInfo far = level.GetType().GetField("FarLine", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(far, Titles[i] + ": has a line for a pick-up from too far away");
                Assert.AreEqual(Level01CheeseWedge.SmallLine, (string)far.GetRawConstantValue(), Titles[i]);
            }
            Assert.Greater(lineCount, 80, "the levels' lines were found");
        }

        // The pause card shows a hint in a panel of fixed size, and the level card shows the blurb in a box
        // of two lines. A label shrinks its type to fit (ART_BIBLE 10.1); a text that needs that is set
        // smaller than the same box one level earlier. For all fifteen levels: every hint and every blurb is
        // set at the full body size, and none is cut off.
        [Test]
        public void EveryHintAndEveryBlurb_OfTheWholeCampaign_IsSetAtFullSize()
        {
            for (int id = 1; id <= Count; id++)
            {
                End();
                Begin("?level=" + id);
                string title = runner.Game.Level.Title;
                Frames(30);
                HudPresenter hud = runner.Presentation.Get<HudPresenter>();
                Assert.AreEqual(runner.Game.Level.Blurb, hud.LevelCard.Blurb, title + ": the level card shows the blurb");
                TMPro.TextMeshProUGUI blurb = null;
                foreach (TMPro.TextMeshProUGUI label in hud.Root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                    if (label.name == "Blurb") blurb = label;
                Assert.IsNotNull(blurb, "the level card's blurb label");
                blurb.ForceMeshUpdate();
                Assert.GreaterOrEqual(blurb.fontSize, UiTheme.BodySize - 0.05f, title + ": the blurb is set smaller to fit (" + blurb.text.Length + " characters): \"" + blurb.text + "\"");
                Assert.LessOrEqual(blurb.textInfo.lineCount, 2, title + ": the blurb is two lines at the most");
                Assert.IsFalse(blurb.isTextOverflowing, title + ": the blurb is cut off");

                devices.State.EscapePressed = true;
                Frames();
                devices.State.EscapePressed = false;
                PauseMenu pause = menu.Pause;
                Assert.AreSame(pause, menu.Current, title + ": paused");
                string[] hints = runner.Game.Level.Hints;
                Assert.AreEqual(3, hints.Length, title);
                for (int h = 0; h < hints.Length; h++)
                {
                    pause.NextHint();
                    Frames();
                    Assert.AreEqual(hints[h], pause.HintText.text);
                    pause.HintText.ForceMeshUpdate();
                    float size = pause.HintText.fontSize;
                    int lineCount = pause.HintText.textInfo.lineCount;
                    Assert.Greater(lineCount, 0, "the text was laid out");
                    Assert.LessOrEqual(lineCount, 5, title + ": hint " + (h + 1) + " is more than five lines");
                    Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, title + ": hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                    Assert.IsFalse(pause.HintText.isTextOverflowing, title + ": hint " + (h + 1) + " is cut off");
                }
            }
        }
    }
}
