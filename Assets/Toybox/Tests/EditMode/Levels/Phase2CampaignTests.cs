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
    /// Levels 5 to 9 as the second part of the campaign (LEVELS.md, phase 2): their place after the first
    /// four, what the game does from the title to the ninth "Next" with the real level list, the
    /// catalogue's cards, what is remembered - and the things the five levels have to agree on with each
    /// other and with phase 1, because a player meets them one after the other: the toy to pick up is in
    /// view at the start and is the only thing that can be picked up; three hints that fit the pause card
    /// at full size; the same words for the same thing.
    /// Each level's own behaviour is in LevelNNTests; phase 1's half of this is Phase1CampaignTests.
    /// </summary>
    public class Phase2CampaignTests
    {
        static readonly int[] Batch = { 5, 6, 7, 8, 9 };
        static readonly string[] Slugs = { "fan-feather", "bouncing-eraser", "teeter-totter", "funnel-physics", "moving-train" };
        static readonly string[] Titles = { "The Fan and the Feather", "Bouncing Eraser", "The Teeter-Totter", "Funnel Physics", "The Moving Train" };
        static readonly string[] Rooms = { "sunny-rug", "block-hall", "high-shelf", "pegboard-workbench", "high-shelf" };
        static readonly ToyId[] Heroes = { ToyId.Feather, ToyId.Eraser, ToyId.Pebble, ToyId.Marble, ToyId.Plank };
        static readonly UiGlyph[] Glyphs = { UiGlyph.Feather, UiGlyph.Eraser, UiGlyph.Pebble, UiGlyph.Marble, UiGlyph.Plank };
        /// <summary>How many toys can be picked up: one, except the three marbles of Level 8.</summary>
        static readonly int[] Toys = { 1, 1, 1, 3, 1 };

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

        // Completes the level that is up and presses Next on its card.
        void CompleteAndGoOn()
        {
            Frames(20);
            runner.Game.CompleteLevel();
            Frames();
            Assert.AreEqual(FlowState.LevelComplete, State);
            Seconds(0.8f);
            Press(menu.Complete.Next);
        }

        // ---- Order ---------------------------------------------------------------------------------------

        [Test]
        public void TheCampaign_GoesOnWithTheFiveLevelsOfPhaseTwo_InOrder()
        {
            LevelList levels = LevelList.FromRegistry();
            Assert.AreEqual(1, levels.First, "the game begins at level 1");
            Assert.GreaterOrEqual(levels.Campaign.Count, 9, "nine levels so far");
            for (int i = 0; i < 9; i++) Assert.AreEqual(i + 1, levels.Campaign[i], "place " + (i + 1) + " of the campaign");
            Assert.AreEqual(Batch[0], levels.After(4), "phase 2 follows phase 1");
            for (int i = 0; i < Batch.Length; i++)
            {
                int id = Batch[i];
                LevelEntry entry = LevelRegistry.Find(id);
                Assert.IsNotNull(entry, "level " + id + " is registered");
                Assert.AreEqual(2, entry.Phase, "level " + id + " is a phase 2 level");
                Assert.AreEqual(Slugs[i], entry.Slug);
                Assert.AreEqual(Titles[i], entry.Title);
                LevelDefinition level = levels.Create(id);
                Assert.AreEqual(Rooms[i], level.Environment, Titles[i] + ": the room the art bible gives it");
                Assert.AreEqual(Rooms[i], EnvironmentPreset.KeyForLevel(id, true));
                Assert.AreEqual(id, levels.Resolve(LaunchOptions.FromUrl("?level=" + Slugs[i])), "reachable by its slug");
                Assert.AreEqual(id, levels.Resolve(LaunchOptions.FromUrl("?level=" + id)), "and by its number");
                Assert.IsTrue(levels.InCampaign(id));
                if (i + 1 < Batch.Length) Assert.AreEqual(Batch[i + 1], levels.After(id), "after level " + id);
            }
            // Whatever comes after the last level that exists is a level of the campaign (the first one, until phase 3 is built).
            Assert.IsTrue(levels.InCampaign(levels.After(levels.Last)));
        }

        // ---- From the title to the ninth "Next" ------------------------------------------------------------

        [Test]
        public void FromTheTitle_ThroughBothPhases_EachCompletionOffersTheNext_AndTheCatalogueFillsUp()
        {
            Begin("");
            LevelList levels = runner.Levels;
            Assert.AreEqual(FlowState.Title, State, "a fresh game opens on the title");
            Assert.AreEqual(1, runner.LevelId, "with level 1 standing behind it");
            Press(menu.Title.Play);
            Assert.AreEqual(FlowState.Playing, State);

            for (int id = 1; id <= 9; id++)
            {
                Assert.AreEqual(id, runner.LevelId, "the level that is up");
                Assert.AreEqual(LevelRegistry.Find(id).Title, runner.Game.Level.Title);
                Assert.IsFalse(runner.Flow.Progress.IsCompleted(id));
                Frames(20);
                runner.Game.CompleteLevel();
                Frames();
                Assert.AreEqual(FlowState.LevelComplete, State, "level " + id);
                Assert.AreSame(menu.Complete, menu.Current);
                Seconds(0.8f);
                Assert.AreEqual(LevelRegistry.Find(id).Title.ToUpperInvariant(), menu.Complete.Title, "the card names the level that was collected");
                Assert.IsTrue(runner.Flow.Progress.IsCompleted(id), "and it is collected");
                int next = levels.After(id);
                if (id < 9) Assert.AreEqual(id + 1, next, "the level after " + id);
                Assert.IsTrue(runner.Flow.Progress.IsUnlocked(next, levels), "which is open now");

                Press(menu.Complete.Next);
                Assert.AreEqual(FlowState.Playing, State, "Next plays on");
                Assert.AreEqual(next, runner.LevelId, "with the level after level " + id);
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
            StringAssert.StartsWith("9 of ", catalogue.CountText);
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
        public void Progress_IsWrittenToTheStore_AndANewSessionContinuesInPhaseTwo()
        {
            Begin("");
            Press(menu.Title.Play);
            for (int id = 1; id <= 6; id++) CompleteAndGoOn();
            Assert.AreEqual(7, runner.LevelId);
            Assert.Greater(store.Saves, 0, "every completion is written through (the browser only keeps what is saved)");
            End();

            // The same store, another session: the title stands over the level the player had reached.
            Begin("");
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(7, runner.LevelId, "the level to continue with");
            Progress progress = runner.Flow.Progress;
            for (int id = 1; id <= 6; id++) Assert.IsTrue(progress.IsCompleted(id), "level " + id + " is collected");
            Assert.IsFalse(progress.IsCompleted(7));
            Press(menu.Title.LevelsButton);
            CatalogueMenu catalogue = menu.Catalogue;
            Assert.IsTrue(catalogue.Find(5).Completed && catalogue.Find(6).Completed);
            Assert.IsTrue(catalogue.Find(7).Unlocked, "the level after the last one collected is open");
            Assert.IsFalse(catalogue.Find(7).Completed);
            Assert.IsFalse(catalogue.Find(8).Unlocked, "the one after that is not");
            Assert.IsFalse(catalogue.Find(8).Button.Interactable);
            Assert.IsFalse(catalogue.Find(9).Unlocked);

            // A card that is open starts its level.
            Press(catalogue.Find(5).Button);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(5, runner.LevelId);
            End();

            // Watching the bot play is not the player's progress.
            store = new MemoryStore();
            Begin("?level=7&autoplay=1");
            runner.Game.CompleteLevel();
            Frames();
            Assert.IsFalse(runner.Flow.Progress.IsCompleted(7));
            Assert.AreEqual(-1, runner.Flow.Progress.LastPlayed);
        }

        // ---- The bot plays the batch through ---------------------------------------------------------------

        /// <summary>Game seconds from the load to LevelCompleted with the level's own solver (measured; half a second either way is allowed).</summary>
        public static readonly float[] SolveSeconds = { 17.35f, 12.12f, 3.68f, 20.5f, 11.75f };
        /// <summary>The size of each toy when the solver has let it go, in the order of the level's props (measured; 2 % either way).</summary>
        static readonly float[][] SolverScales =
        {
            new[] { 9.33f }, new[] { 9.37f }, new[] { 4.63f }, new[] { 0.651f, 1.221f, 2.413f }, new[] { 4.086f },
        };

        // One Game, the five levels one after the other, as the runner loads them: nothing a level leaves
        // behind (ignored collisions, cached meshes, physics materials, subscriptions) may reach the next.
        // Then the same again, backwards, to catch what only shows on a second visit.
        [Test]
        public void TheBotSolvesTheFiveLevels_OneAfterTheOther_InOneGame()
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
                    BotRunner.Run(game, game.Level.Solve(bot), 90f);
                    TestHelpers.RunUntil(game, () => game.LevelCompleted, 2f);
                }
                finally
                {
                    game.Events.LevelCompleted -= onCompleted;
                    game.Events.Message -= onMessage;
                }
                Assert.IsTrue(game.LevelCompleted, Titles[index] + ": the solver completes it (bot at " + game.Player.Position + ")");
                Assert.AreEqual(1, completed, Titles[index] + ": completed once");
                Assert.AreEqual(SolveSeconds[index], solved, 0.5f, Titles[index] + ": the solve takes as long as it did when it was measured");
                // What LEVELS.md (Appendix E) says the solver makes of each toy.
                var sizes = new List<float>();
                foreach (Prop prop in game.Props)
                    if (prop.HasTag("marble") || prop.Grabbable) sizes.Add(prop.Scale);
                Assert.AreEqual(SolverScales[index].Length, sizes.Count, Titles[index] + ": its toys");
                for (int t = 0; t < sizes.Count; t++)
                    Assert.AreEqual(SolverScales[index][t], sizes[t], SolverScales[index][t] * 0.02f, Titles[index] + ": toy " + (t + 1) + " as the solver let it go");
                // The simulation is deterministic: after another level, and on a second visit, the same solve to the tick.
                if (visit < Batch.Length) first[index] = solved;
                else Assert.AreEqual(first[index], solved, 1e-4f, Titles[index] + ": the same solve on the second visit");
                report.Append(Titles[index]).Append(": ").Append(solved.ToString("0.00")).Append(" s, toys ").Append(string.Join(" / ", sizes.ConvertAll(x => x.ToString("0.000")))).Append(", said [").Append(string.Join(" | ", said)).Append("]; ");
            }
            Debug.Log("[Toybox] phase 2 solved in one game: " + report);
        }

        // ---- What the five levels agree on -----------------------------------------------------------------

        LevelDefinition Load(int id)
        {
            game?.Dispose();
            game = Game.Create();
            game.LoadLevel(id);
            return game.Level;
        }

        [Test]
        public void EachLevel_ShowsItsHeroToyAtTheStart_AndNothingElseCanBePickedUp()
        {
            for (int i = 0; i < Batch.Length; i++)
            {
                LevelDefinition level = Load(Batch[i]);
                ToyDef hero = ToyCatalog.HeroOf(Batch[i]);
                Assert.AreEqual(Heroes[i], hero.Id, Titles[i]);

                var toys = new List<Prop>();
                foreach (Prop prop in game.Props)
                    if (prop.Grabbable) toys.Add(prop);
                Assert.AreEqual(Toys[i], toys.Count, Titles[i] + ": what can be picked up");

                Dip dip = Palette.DipOf(level.Environment);
                Player player = game.Player;
                float nearest = 180f;
                foreach (Prop toy in toys)
                {
                    // Candy colour, rim and pool mean "you can lift this" (ART_BIBLE 2.5): every toy is the level's
                    // hero toy, in a candy colour the room allows.
                    ToyInfo info = ToyInfo.Of(toy.GameObject);
                    Assert.IsNotNull(info, Titles[i] + ": " + toy.Name + " is tagged for the look");
                    Assert.AreEqual(hero.Recipe, info.Recipe, Titles[i] + ": " + toy.Name + " is the level's hero toy");
                    Assert.IsTrue(Palette.IsCandy(info.Candy), Titles[i] + ": " + toy.Name + " wears a candy colour");
                    Assert.IsTrue(dip.Allows(info.Candy), Titles[i] + ": " + toy.Name + " does not wear the room's banned colour");
                    if (toys.Count == 1)
                    {
                        Assert.AreEqual(hero.Name, toy.Name, Titles[i]);
                        Assert.IsTrue(Palette.Same(hero.ColorIn(dip), info.Candy), Titles[i] + ": the colour of the catalog in this room");
                    }
                    nearest = Mathf.Min(nearest, Vector3.Angle(player.Forward, toy.Center - player.Eye));
                }
                if (toys.Count == 1)
                {
                    // The first picture has the toy under the crosshair, and the first click picks it up.
                    Assert.Less(nearest, 12f, Titles[i] + ": the toy is " + nearest.ToString("0") + " degrees off the crosshair at the start");
                    Assert.AreSame(toys[0], game.Grabber.FindTarget(), Titles[i] + ": a click at the start picks the toy up");
                }
                else
                {
                    // Several toys: the first picture is of all of them and of where they go. The two that lie at
                    // the player's feet are picked up from the spot, with a turn of the head and no step.
                    int inView = 0, atHand = 0;
                    Vector3 start = player.Position, ahead = player.Forward;
                    foreach (Prop toy in toys)
                        if (Vector3.Angle(ahead, toy.Center - player.Eye) < 40f) inView++;
                    Assert.AreEqual(toys.Count, inView, Titles[i] + ": every toy is in the first picture");
                    var bot = new Bot(game);
                    foreach (Prop toy in toys)
                    {
                        if ((toy.Center - player.Eye).magnitude > 6f) continue;
                        BotRunner.Run(game, bot.LookAt(toy), 5f);
                        Assert.AreSame(toy, game.Grabber.FindTarget(), Titles[i] + ": " + toy.Name + " can be picked up from the start");
                        atHand++;
                    }
                    Assert.GreaterOrEqual(atHand, 2, Titles[i] + ": toys within reach of the start");
                    Assert.Less(Vector3.Distance(start, player.Position), 0.01f, Titles[i] + ": without a step");
                }

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

        /// <summary>A toast is one or two short sentences: three lines of the toast's 520-wide box at the most.</summary>
        public const int LongestLine = 110;

        [Test]
        public void TheFiveLevels_SpeakWithOneVoice_AndWithPhaseOnes()
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
                foreach (string line in lines)
                    Assert.LessOrEqual(line.Length, LongestLine, Titles[i] + ": \"" + line + "\" is too long for a toast");
                // The first hint is a nudge: it does not say where to stand or what to cover. The last is the recipe.
                string nudge = level.Hints[0].ToLowerInvariant();
                Assert.IsFalse(nudge.Contains("outline") || nudge.Contains("shoe prints") || nudge.Contains("bullseye") || nudge.Contains("edge"),
                    Titles[i] + ": the first hint gives the place away: \"" + level.Hints[0] + "\"");
                StringAssert.Contains("let go", level.Hints[2], Titles[i] + ": the last hint says when to let go");
                StringAssert.Contains("Pick", level.Hints[2], Titles[i] + ": and begins with the pick-up");

                // The sentence for a pick-up from too far away is phase 1's, word for word, in every level.
                FieldInfo far = level.GetType().GetField("FarLine", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(far, Titles[i] + ": has a line for a pick-up from too far away");
                Assert.AreEqual(Level01CheeseWedge.SmallLine, (string)far.GetRawConstantValue(), Titles[i]);
            }
            Assert.Greater(lineCount, 40, "the levels' lines were found");
        }

        // The pause card shows a hint in a panel of fixed size. A label shrinks its type to fit (ART_BIBLE 10.1);
        // a hint that needs that is set smaller than the same panel one level earlier. Every hint of the batch
        // is set at the full body size, and none is cut off.
        [Test]
        public void EveryHint_FitsThePauseCardsPanel_AtFullSize()
        {
            for (int i = 0; i < Batch.Length; i++)
            {
                End();
                Begin("?level=" + Batch[i]);
                Frames(30);
                devices.State.EscapePressed = true;
                Frames();
                devices.State.EscapePressed = false;
                PauseMenu pause = menu.Pause;
                Assert.AreSame(pause, menu.Current, Titles[i] + ": paused");
                string[] hints = runner.Game.Level.Hints;
                float room = pause.HintText.rectTransform.rect.height;
                for (int h = 0; h < hints.Length; h++)
                {
                    pause.NextHint();
                    Frames();
                    Assert.AreEqual(hints[h], pause.HintText.text);
                    pause.HintText.ForceMeshUpdate();
                    float size = pause.HintText.fontSize;
                    int lineCount = pause.HintText.textInfo.lineCount;
                    Debug.Log("[Toybox] " + Titles[i] + ", hint " + (h + 1) + ": " + hints[h].Length + " characters, " + lineCount + " lines at size " + size.ToString("0.0") +
                              " (" + pause.HintText.preferredHeight.ToString("0") + " of " + room.ToString("0") + " high)");
                    Assert.Greater(lineCount, 0, "the text was laid out");
                    Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, Titles[i] + ": hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                    Assert.IsFalse(pause.HintText.isTextOverflowing, Titles[i] + ": hint " + (h + 1) + " is cut off");
                }
            }
        }
    }
}
