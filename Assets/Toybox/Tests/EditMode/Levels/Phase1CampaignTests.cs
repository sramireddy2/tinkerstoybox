using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// The first four levels as a campaign (LEVELS.md, phase 1): their order, what the game does from the
    /// title to the fourth "Next" with the real level list, the catalogue's cards, what is remembered - and
    /// the things the four levels have to agree on, because a player meets them one after the other: one
    /// toy to pick up, in view at the start; three hints; the same words for the same thing.
    /// Each level's own behaviour is in LevelNNTests.
    /// </summary>
    public class Phase1CampaignTests
    {
        static readonly int[] Batch = { 1, 2, 3, 4 };
        static readonly string[] Slugs = { "cheese-wedge", "thimble-chasm", "shrinking-apple", "domino-effect" };
        static readonly string[] Titles = { "The Cheese Wedge", "The Thimble Chasm", "Shrinking the Apple", "Domino Effect" };
        static readonly string[] Rooms = { "sunny-rug", "pegboard-workbench", "cardboard-box", "block-hall" };
        static readonly ToyId[] Heroes = { ToyId.CheeseWedge, ToyId.Thimble, ToyId.Apple, ToyId.Domino };
        static readonly UiGlyph[] Glyphs = { UiGlyph.Wedge, UiGlyph.Thimble, UiGlyph.Apple, UiGlyph.Domino };

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
        public void TheCampaign_BeginsWithTheFourLevelsOfPhaseOne_InOrder()
        {
            LevelList levels = LevelList.FromRegistry();
            Assert.AreEqual(1, levels.First, "the game begins at level 1");
            for (int i = 0; i < Batch.Length; i++)
            {
                int id = Batch[i];
                Assert.AreEqual(id, levels.Campaign[i], "place " + (i + 1) + " of the campaign");
                LevelEntry entry = LevelRegistry.Find(id);
                Assert.IsNotNull(entry, "level " + id + " is registered");
                Assert.AreEqual(1, entry.Phase, "level " + id + " is a phase 1 level");
                Assert.AreEqual(Slugs[i], entry.Slug);
                Assert.AreEqual(Titles[i], entry.Title);
                LevelDefinition level = levels.Create(id);
                Assert.AreEqual(Rooms[i], level.Environment, Titles[i] + ": the room the art bible gives it");
                Assert.AreEqual(Rooms[i], EnvironmentPreset.KeyForLevel(id, true));
                Assert.AreEqual(i + 1 < Batch.Length ? Batch[i + 1] : levels.After(id), levels.After(id));
                Assert.AreEqual(id, levels.Resolve(LaunchOptions.FromUrl("?level=" + Slugs[i])), "reachable by its slug");
            }
            for (int i = 0; i + 1 < Batch.Length; i++) Assert.AreEqual(Batch[i + 1], levels.After(Batch[i]), "after level " + Batch[i]);
            // The sandbox, the gallery and the showroom can be asked for, and are no part of the walk through.
            foreach (int extra in new[] { 0, 98, 99 })
            {
                Assert.IsTrue(levels.Has(extra), "level " + extra + " exists");
                Assert.IsFalse(levels.InCampaign(extra), "level " + extra + " is not in the campaign");
            }
        }

        // ---- From the title to the fourth "Next" -----------------------------------------------------------

        [Test]
        public void FromTheTitle_TheGameBeginsAtLevelOne_EachCompletionOffersTheNext_AndTheCatalogueFillsUp()
        {
            Begin("");
            LevelList levels = runner.Levels;
            Assert.AreEqual(FlowState.Title, State, "a fresh game opens on the title");
            Assert.AreEqual(1, runner.LevelId, "with level 1 standing behind it");
            Assert.AreSame(menu.Title, menu.Current);
            int ticks = runner.Game.TickCount;
            Frames(10);
            Assert.AreEqual(ticks, runner.Game.TickCount, "nothing moves behind the title");

            Press(menu.Title.Play);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(1, runner.LevelId);
            Assert.AreEqual(Titles[0], runner.Game.Level.Title);
            Assert.AreEqual(1, runner.Flow.Progress.LastPlayed, "the bookmark is on level 1");

            for (int i = 0; i < Batch.Length; i++)
            {
                int id = Batch[i];
                Assert.AreEqual(id, runner.LevelId);
                Assert.IsFalse(runner.Flow.Progress.IsCompleted(id));
                Frames(20);
                runner.Game.CompleteLevel();
                Frames();
                Assert.AreEqual(FlowState.LevelComplete, State, Titles[i]);
                Assert.AreSame(menu.Complete, menu.Current);
                Seconds(0.8f);
                Assert.AreEqual(Titles[i].ToUpperInvariant(), menu.Complete.Title, "the card names the level that was collected");
                Assert.IsTrue(runner.Flow.Progress.IsCompleted(id), "and it is collected");
                Assert.GreaterOrEqual(runner.Flow.Progress.BestTime(id), 0f);
                int next = levels.After(id);
                Assert.IsTrue(runner.Flow.Progress.IsUnlocked(next, levels), "which opens level " + next);

                Press(menu.Complete.Next);
                Assert.AreEqual(FlowState.Playing, State, "Next plays on");
                Assert.AreEqual(next, runner.LevelId, "with the level after " + Titles[i]);
                Assert.AreEqual(next, runner.Flow.Progress.LastPlayed);
            }

            // The catalogue: every level of the batch on its card, with its hero toy in the room's hero colour.
            devices.State.EscapePressed = true;
            Frames();
            devices.State.EscapePressed = false;
            Assert.AreEqual(FlowState.Paused, State);
            Press(menu.Pause.LevelSelect);
            Assert.AreEqual(FlowState.LevelSelect, State);
            CatalogueMenu catalogue = menu.Catalogue;
            Assert.AreEqual(levels.Campaign.Count, catalogue.Entries.Count, "one card for each level of the campaign");
            StringAssert.StartsWith(Batch.Length + " of ", catalogue.CountText);
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
        }

        // ---- What is remembered ----------------------------------------------------------------------------

        [Test]
        public void Progress_IsWrittenToTheStore_AndANewSessionContinuesWhereTheLastOneStopped()
        {
            Begin("");
            Press(menu.Title.Play);
            for (int i = 0; i < 2; i++)
            {
                Frames(20);
                runner.Game.CompleteLevel();
                Frames();
                Seconds(0.8f);
                Press(menu.Complete.Next);
            }
            Assert.AreEqual(3, runner.LevelId);
            Assert.Greater(store.Saves, 0, "every completion is written through (the browser only keeps what is saved)");
            End();

            // The same store, another session: the title stands over the level the player had reached.
            Begin("");
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(3, runner.LevelId, "the level to continue with");
            Progress progress = runner.Flow.Progress;
            Assert.IsTrue(progress.IsCompleted(1) && progress.IsCompleted(2));
            Assert.IsFalse(progress.IsCompleted(3));
            Press(menu.Title.LevelsButton);
            CatalogueMenu catalogue = menu.Catalogue;
            Assert.IsTrue(catalogue.Find(1).Completed && catalogue.Find(2).Completed);
            Assert.IsTrue(catalogue.Find(3).Unlocked, "the level after the last one collected is open");
            Assert.IsFalse(catalogue.Find(3).Completed);
            Assert.IsFalse(catalogue.Find(4).Unlocked, "the one after that is not");
            Assert.IsFalse(catalogue.Find(4).Button.Interactable);

            // A card that is open starts its level.
            Press(catalogue.Find(2).Button);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(2, runner.LevelId);
            End();

            // Watching the bot play is not the player's progress.
            store = new MemoryStore();
            Begin("?level=1&autoplay=1");
            runner.Game.CompleteLevel();
            Frames();
            Assert.IsFalse(runner.Flow.Progress.IsCompleted(1));
            Assert.AreEqual(-1, runner.Flow.Progress.LastPlayed);
        }

        // ---- The bot plays the batch through ---------------------------------------------------------------

        /// <summary>Game seconds from the load to LevelCompleted with the level's own solver (measured; half a second either way is allowed).</summary>
        static readonly float[] SolveSeconds = { 12.63f, 9.28f, 3.77f, 14.18f };

        // One Game, the four levels one after the other, as the runner loads them: nothing a level leaves
        // behind (ignored collisions, cached meshes, physics materials, subscriptions) may reach the next.
        // Then the same again, backwards, to catch what only shows on a second visit.
        [Test]
        public void TheBotSolvesTheFourLevels_OneAfterTheOther_InOneGame()
        {
            game = Game.Create();
            var order = new List<int>(Batch);
            for (int i = Batch.Length - 1; i >= 0; i--) order.Add(Batch[i]);
            var report = new System.Text.StringBuilder();
            foreach (int id in order)
            {
                int index = Array.IndexOf(Batch, id);
                game.LoadLevel(id);
                Assert.AreEqual(Titles[index], game.Level.Title);
                int completed = 0, messages = 0;
                float solved = -1f;
                Action<LevelEvent> onCompleted = e =>
                {
                    completed++;
                    solved = e.Time;
                };
                Action<MessageEvent> onMessage = e => messages++;
                game.Events.LevelCompleted += onCompleted;
                game.Events.Message += onMessage;
                try
                {
                    var bot = new Bot(game);
                    BotRunner.Run(game, game.Level.Solve(bot), 60f);
                    TestHelpers.RunUntil(game, () => game.LevelCompleted, 2f);
                }
                finally
                {
                    game.Events.LevelCompleted -= onCompleted;
                    game.Events.Message -= onMessage;
                }
                Assert.IsTrue(game.LevelCompleted, Titles[index] + ": the solver completes it (bot at " + game.Player.Position + ")");
                Assert.AreEqual(1, completed, Titles[index] + ": completed once");
                // The simulation is deterministic: after another level, and on a second visit, the same solve to the tick.
                Assert.AreEqual(SolveSeconds[index], solved, 0.5f, Titles[index] + ": the solve takes as long as it did when it was measured");
                report.Append(Titles[index]).Append(": ").Append(solved.ToString("0.00")).Append(" s, ").Append(messages).Append(" lines; ");
            }
            Debug.Log("[Toybox] phase 1 solved in one game: " + report);
        }

        // ---- What the four levels agree on -----------------------------------------------------------------

        T Load<T>(int id) where T : LevelDefinition
        {
            game?.Dispose();
            game = Game.Create();
            game.LoadLevel(id);
            return (T)game.Level;
        }

        [Test]
        public void EachLevel_HasOneToyToPickUp_ItsHero_InViewFromTheStart()
        {
            for (int i = 0; i < Batch.Length; i++)
            {
                LevelDefinition level = Load<LevelDefinition>(Batch[i]);
                ToyDef hero = ToyCatalog.HeroOf(Batch[i]);
                Assert.AreEqual(Heroes[i], hero.Id, Titles[i]);

                var toys = new List<Prop>();
                foreach (Prop prop in game.Props)
                    if (prop.Grabbable) toys.Add(prop);
                Assert.AreEqual(1, toys.Count, Titles[i] + ": one thing can be picked up");
                Prop toy = toys[0];
                Assert.AreEqual(hero.Name, toy.Name, Titles[i] + ": and it is the level's hero toy");

                // Candy colour, rim and pool mean "you can lift this" (ART_BIBLE 2.5): the toy wears the colour
                // the catalog gives it in this room, and the room's banned colour is not it.
                Dip dip = Palette.DipOf(level.Environment);
                ToyInfo info = ToyInfo.Of(toy.GameObject);
                Assert.IsNotNull(info, Titles[i] + ": the toy is tagged for the look");
                Assert.IsTrue(Palette.IsCandy(info.Candy), Titles[i] + ": a candy colour");
                Assert.IsTrue(Palette.Same(hero.ColorIn(dip), info.Candy), Titles[i] + ": the colour of the catalog in this room");
                Assert.IsTrue(dip.Allows(info.Candy), Titles[i] + ": not the room's banned colour");

                // The first picture has the toy in it.
                Player player = game.Player;
                float off = Vector3.Angle(player.Forward, toy.Center - player.Eye);
                Assert.Less(off, 35f, Titles[i] + ": the toy is " + off.ToString("0") + " degrees off the crosshair at the start");

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
        public void TheFourLevels_SpeakWithOneVoice()
        {
            int lineCount = 0;
            for (int i = 0; i < Batch.Length; i++)
            {
                LevelDefinition level = LevelRegistry.Get(Batch[i]);
                Assert.AreEqual(3, level.Hints.Length, Titles[i] + ": three hints, from a nudge to the recipe");
                Assert.IsNotEmpty(level.Blurb);
                Assert.LessOrEqual(level.Blurb.Length, 60, Titles[i] + ": the blurb fits the level card");

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
                // A line is a toast: one or two short sentences.
                foreach (string line in lines)
                    Assert.LessOrEqual(line.Length, 95, Titles[i] + ": \"" + line + "\" is too long for a toast");
                // The recipe hint ends with the act itself.
                StringAssert.Contains("let go", level.Hints[2], Titles[i] + ": the last hint says when to let go");
            }
            Assert.Greater(lineCount, 12, "the levels' lines were found");

            // The one rule all of phase 1 rests on is said in the same words wherever it is said.
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level02ThimbleChasm.FarLine);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level04DominoEffect.SmallLine);
            StringAssert.Contains("Pick it up from closer", Level01CheeseWedge.SmallLine);
            // Its mirror image in the level that runs the mechanic the other way.
            StringAssert.Contains("Pick it up from farther off", Level03ShrinkingApple.TooBigLine);
        }
    }
}
