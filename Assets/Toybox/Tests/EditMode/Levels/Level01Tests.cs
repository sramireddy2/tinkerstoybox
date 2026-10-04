using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Levels;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 1, The Cheese Wedge. The numbers asserted here were measured in the simulation (the logs are
    /// tools/out/notes/level01-build.md and level01-review.md): taken from the spawn and let go from the edge
    /// the wedge is 9.46, it works from 5.5 up to the clamp of 14, and the far stack's face takes the
    /// crosshair anywhere above its lowest third - or on the exit mark and the sun behind it.
    /// </summary>
    public class Level01Tests : SimTest
    {
        static readonly Vector3 Spool = new Vector3(0f, 5.1f, -3f);
        /// <summary>The player starts one unit in front of the spool, where the solver takes the cheese from.</summary>
        static readonly Vector3 Spawn = Level01CheeseWedge.GrabSpot;
        /// <summary>Six units from the spool: where LEVELS.md had the player start.</summary>
        static readonly Vector3 FarOff = new Vector3(0f, 4f, -9f);
        static readonly Vector3 Edge = Level01CheeseWedge.EdgeSpot;
        static readonly Vector3 Outline = Level01CheeseWedge.Aim;

        Level01CheeseWedge level;
        Bot bot;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(1);
            level = (Level01CheeseWedge)Game.Level;
            bot = new Bot(Game);
        }

        void Unload()
        {
            Game?.Dispose();
            Game = null;
        }

        /// <summary>
        /// Runs a bot script tick by tick, through the BotRunner (which refuses a script that touches the
        /// simulation). Returns null if the script ran to its end, else what stopped it.
        /// </summary>
        string Drive(IEnumerator script, float timeoutSeconds = 60f, Action eachTick = null)
        {
            try
            {
                var runner = new BotRunner(script, bot, Game);
                int limit = TestHelpers.Ticks(timeoutSeconds);
                while (runner.Advance())
                {
                    if (limit-- <= 0) return "still running after " + timeoutSeconds + " s";
                    Game.Tick();
                    eachTick?.Invoke();
                }
                return null;
            }
            catch (BotException e)
            {
                return e.Message;
            }
        }

        static IEnumerator Seq(params IEnumerator[] steps)
        {
            foreach (IEnumerator step in steps) yield return step;
        }

        /// <summary>From the spawn's side of the spool to the edge: round it, on the side the drop is made from.</summary>
        IEnumerator RoundTheSpool(float towardX = 1f) => bot.WalkTo(new Vector3(towardX < 0f ? -1.3f : 1.3f, 4f, -3.2f), 0.3f);

        // The intended solution with other numbers: where the cheese is taken from (how big it looks),
        // where it is let go from, and what the crosshair is on.
        IEnumerator Attempt(Vector3 grabFrom, Vector3 dropFrom, Vector3 aim)
        {
            yield return level.Take(bot, grabFrom);
            if (dropFrom.z > -3.6f) yield return RoundTheSpool(dropFrom.x);
            yield return level.Place(bot, dropFrom, aim);
            yield return level.Climb(bot);
        }

        /// <summary>The cheese in hand already: let go from a spot at an aim, then down the book and up the cheese.</summary>
        IEnumerator Again(Vector3 dropFrom, Vector3 aim)
        {
            yield return bot.Grab(level.Wedge);
            yield return level.Place(bot, dropFrom, aim);
            yield return level.Climb(bot);
        }

        static Vector3 Local(Prop wedge, float x, float y, float z)
        {
            Vector3 half = ToyFactory.CheeseWedgeSize * 0.5f;
            return wedge.Transform.TransformPoint(new Vector3(x * half.x, y * half.y, z * half.z));
        }

        /// <summary>The middle of the top of the wedge's tall end.</summary>
        static Vector3 Crest(Prop wedge) => Local(wedge, 0f, 1f, 1f);

        /// <summary>The middle of its knife edge.</summary>
        static Vector3 Toe(Prop wedge) => Local(wedge, 0f, -1f, -1f);

        /// <summary>Level direction from the knife edge to the tall end.</summary>
        static Vector3 Rise(Prop wedge)
        {
            Vector3 rise = wedge.Rotation * Vector3.forward;
            rise.y = 0f;
            return rise.normalized;
        }

        string Where() =>
            "bot at " + Game.Player.Position.ToString("F2") + ", wedge " + level.Wedge.Scale.ToString("0.00") + " at " + level.Wedge.Center.ToString("F2") +
            ", crest " + Crest(level.Wedge).ToString("F2");

        bool Completes(IEnumerator script, out string why)
        {
            why = Drive(script);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            why = (why ?? "the script ended") + " (" + Where() + ")";
            return Game.LevelCompleted;
        }

        List<string> Lines()
        {
            var lines = new List<string>();
            Game.Events.Message += e => lines.Add(e.Text);
            return lines;
        }

        // ---- The level as specified ---------------------------------------------------------------------

        [Test]
        public void TheLevel_IsTheOneOfTheCampaignDocument()
        {
            Load();
            Assert.AreEqual("cheese-wedge", level.Slug);
            Assert.AreEqual("The Cheese Wedge", level.Title);
            Assert.AreEqual(1, level.Phase);
            Assert.AreEqual("sunny-rug", level.Environment);
            Assert.AreEqual(0f, level.GroundY);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual("Things are as big as they look. Pick up the cheese.", level.Blurb);

            // Three hints that go one step further each: what a hold does, where to look, the whole solution.
            Assert.AreEqual(3, level.Hints.Length);
            StringAssert.DoesNotContain("far", level.Hints[0].ToLowerInvariant());
            StringAssert.Contains("Far away means bigger", level.Hints[1]);
            StringAssert.DoesNotContain("edge", level.Hints[1]);
            StringAssert.Contains("edge", level.Hints[2]);
            StringAssert.Contains("outline", level.Hints[2]);
            StringAssert.Contains("leaning book", level.Hints[2]);

            Prop wedge = level.Wedge;
            Assert.AreEqual(1, Game.Props.Count, "the cheese is the only prop");
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f);
            Assert.AreEqual(0.2f, wedge.MinScale, 1e-5f);
            Assert.AreEqual(14f, wedge.MaxScale, 1e-5f);
            Assert.AreEqual(GrabPose.Upright, wedge.GrabPose);
            Assert.Less((wedge.Center - Spool).magnitude, 1e-3f, "on the spool");
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.Less((Game.Exits[0].Position - new Vector3(0f, 5f, 42f)).magnitude, 1e-3f);

            // The spawn is the one deliberate departure from LEVELS.md: one step from the spool (its own
            // pedestal rule), not six.
            Assert.Less((Game.Player.Position - new Vector3(0f, 4f, -4f)).magnitude, 1e-3f);
            Assert.AreEqual(0f, Game.Player.Yaw, 1e-3f);
            Assert.AreEqual(Level01CheeseWedge.SpawnPitch, Game.Player.Pitch, 1e-3f);

            // It stays on its pedestal until it is taken.
            int returns = 0;
            Game.Events.LeashReturned += e => returns++;
            RunSeconds(4f);
            Assert.AreEqual(0, returns, "nothing fetches it back while it is there");
            Assert.Less((wedge.Center - Spool).magnitude, 0.02f, "the cheese rests on the spool");
            Assert.AreEqual(0f, Quaternion.Angle(wedge.Rotation, Quaternion.identity), 0.5f, "thin end toward the spawn");
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f);
        }

        [Test]
        public void FromTheSpawn_TheCheeseTheFarStackAndTheExitAreInView_AndTheFirstClickIsACloseOne()
        {
            Load();
            Vector3 eye = Game.Player.Eye, view = Game.Player.Forward;
            Assert.IsTrue(Sees(eye, level.Wedge.Center, out RaycastHit hit));
            Assert.AreSame(level.Wedge, PropRef.Of(hit.collider), "the cheese");

            // The face the wedge stops against, where its outline is painted: both sides of the outline, and
            // its middle above the cheese (which, from here, sits right in front of the foot of that face).
            foreach (Vector2 at in new[] { new Vector2(-3.7f, 2f), new Vector2(3.7f, 2f), new Vector2(0f, 3.8f) })
            {
                Assert.IsTrue(Sees(eye, new Vector3(at.x, at.y, 30.5f), out hit), "at " + at);
                Assert.AreEqual("Book Stack B", hit.collider.name, "at " + at);
                Assert.AreEqual(30f, hit.point.z, 0.01f);
            }

            // Nothing stands between the eye and the exit mark.
            Vector3 exit = Level01CheeseWedge.ExitCentre;
            Assert.IsFalse(Game.PhysicsScene.Raycast(eye, (exit - eye).normalized, Vector3.Distance(eye, exit), Layers.SolidMask, QueryTriggerInteraction.Ignore),
                "the exit is in plain view");

            // All three are in the picture without turning the head, at the narrowest field of view the
            // settings allow (50 degrees, 25 either side of the crosshair): the cheese under the crosshair,
            // the far stack and the exit above it.
            Assert.AreSame(level.Wedge, Game.Grabber.FindTarget(), "the crosshair starts on the cheese: a click takes it");
            Assert.Less(Vector3.Angle(view, level.Wedge.Center - eye), 8f, "the cheese");
            Assert.Less(Vector3.Angle(view, new Vector3(0f, 2f, 30f) - eye), 22f, "the far stack's face");
            Assert.Less(Vector3.Angle(view, exit - eye), 22f, "the exit");

            // How big it looks is decided by the first click, and that click is made from here.
            float distance = Vector3.Distance(eye, level.Wedge.Center);
            Assert.Less(distance, 1.2f, "one step from the pedestal");
            Assert.Greater(level.Wedge.Scale / distance, 1.5f * Level01CheeseWedge.LooksTooSmall, "the default grab looks big enough, with room to spare");
        }

        bool Sees(Vector3 eye, Vector3 point, out RaycastHit hit) =>
            Game.PhysicsScene.Raycast(eye, (point - eye).normalized, out hit, Vector3.Distance(eye, point) + 0.1f, Layers.SolidMask, QueryTriggerInteraction.Ignore);

        [Test]
        public void TheSetDressing_IsDrawnOnly()
        {
            Load();
            foreach (string name in new[] { "Chest", "Chest Sun Side", "Books", "Clutter" })
            {
                Transform set = Game.LevelRoot.Find(name);
                Assert.IsNotNull(set, name);
                Assert.Greater(set.GetComponentsInChildren<Renderer>().Length, 0, name);
                Assert.AreEqual(0, set.GetComponentsInChildren<Collider>().Length, name + " has no colliders of its own");
            }
            // ART_BIBLE 12.1: level statics are merged per material, at most 12 draws.
            int draws = 0;
            foreach (Renderer renderer in Game.LevelRoot.GetComponentsInChildren<Renderer>())
                if (renderer.GetComponentInParent<PropRef>() == null) draws += renderer.sharedMaterials.Length;
            Assert.LessOrEqual(draws, 12, "everything the level draws besides the cheese");
        }

        [Test]
        public void TheChestsWalls_ThrowNoShadowIntoTheChest_SoTheHillsidesOwnShadowIsSeen()
        {
            Load();
            // The sun stands to the left and ahead, 45 degrees up: a 12 high wall on that side would shade
            // three quarters of the rug.
            Vector3 sun = Game.Environment.SunDirection;
            Assert.Greater(sun.y, 0.5f);

            foreach (MeshRenderer renderer in Game.LevelRoot.Find("Chest Sun Side").GetComponentsInChildren<MeshRenderer>())
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, renderer.name);

            // Whatever of the chest does cast: no point of it has its shadow on the rug inside the chest.
            int points = 0;
            foreach (MeshRenderer renderer in Game.LevelRoot.Find("Chest").GetComponentsInChildren<MeshRenderer>())
            {
                Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode, renderer.name);
                foreach (Vector3 vertex in renderer.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Vector3 at = renderer.transform.TransformPoint(vertex);
                    Vector3 shadow = at - sun * (at.y / sun.y);
                    bool inside = Mathf.Abs(shadow.x) < Level01CheeseWedge.HalfWidth - 0.01f &&
                                  shadow.z > Level01CheeseWedge.NearZ + 0.01f && shadow.z < Level01CheeseWedge.FarZ - 0.01f;
                    Assert.IsFalse(inside, "the shadow of " + at + " falls at " + shadow);
                    points++;
                }
            }
            Assert.Greater(points, 0, "two of the four walls still cast, outward");

            // The books and the toys cast as ever: the shadow is the cue to true size.
            foreach (string name in new[] { "Books", "Clutter" })
                foreach (MeshRenderer renderer in Game.LevelRoot.Find(name).GetComponentsInChildren<MeshRenderer>())
                    Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode, name);
        }

        // ---- The solution ---------------------------------------------------------------------------------

        [Test]
        public void TheBotSolvesTheLevel()
        {
            Load();
            int hops = 0;
            float dropped = 0f;
            List<string> lines = Lines();
            Game.Events.PlayerJumped += e => hops++;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            float solved = 0f;
            Game.Events.LevelCompleted += e => solved = e.Time;
            TestHelpers.PlayLevel(Game, 90f);
            Debug.Log("[Toybox] level 1 solved in " + solved.ToString("0.00") + " s; the wedge is " + dropped.ToString("0.00") + " at " + level.Wedge.Center);
            Assert.Less(solved, 30f, "the bot's solve takes about a quarter of a minute");

            // LEVELS.md, appendix B: 9.4 within 5 %.
            Assert.That(dropped, Is.InRange(9.4f * 0.95f, 9.4f * 1.05f), "the scale the intended drop gives");
            Assert.AreEqual(dropped, level.Wedge.Scale, 1e-4f);
            Prop wedge = level.Wedge;
            Vector3 crest = Crest(wedge);
            Assert.AreEqual(0.5f * wedge.Scale, crest.y, 0.05f, "the wedge lies flat on the rug");
            Assert.That(crest.z, Is.InRange(29.4f, 30f), "its tall end is against the goal stack");
            Assert.AreEqual(0, hops, "the intended route needs no jump: down the leaning book, up the cheese, a step down onto the stack");
            Assert.Greater(wedge.Mass, 10f * Player.Mass, "nothing the player can shove");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.HoldLine }, lines, "one line, at the grab");
        }

        [Test]
        public void TheSolve_IsTheSameFiveTimesInARow()
        {
            float time = 0f, scale = 0f;
            Vector3 centre = default;
            for (int run = 0; run < 5; run++)
            {
                Load();
                float solved = 0f;
                Game.Events.LevelCompleted += e => solved = e.Time;
                TestHelpers.PlayLevel(Game, 90f);
                if (run == 0)
                {
                    time = solved;
                    scale = level.Wedge.Scale;
                    centre = level.Wedge.Center;
                }
                else
                {
                    Assert.AreEqual(time, solved, 0.02f, "run " + run);
                    Assert.AreEqual(scale, level.Wedge.Scale, 1e-3f, "run " + run);
                    Assert.Less((centre - level.Wedge.Center).magnitude, 0.01f, "run " + run);
                }
                Unload();
            }
        }

        // A player's hand is not the bot's: the drop made from a little to either side of the solver's spot,
        // nearer to the edge or further from it, with the crosshair anywhere on the middle of the outline.
        [Test]
        public void ASmallChangeOfStandPointOrAim_ChangesNothing()
        {
            var failed = new List<string>();
            float least = float.MaxValue, most = 0f;
            foreach (Vector2 stand in new[] { new Vector2(1f, -0.4f), new Vector2(2f, -0.6f), new Vector2(3f, -1.2f) })
            foreach (float x in new[] { -1.5f, 0f, 1.5f })
            foreach (float y in new[] { 1.8f, 2.4f, 3.4f })
            {
                Load();
                if (!Completes(Attempt(Spawn, new Vector3(stand.x, 4f, stand.y), new Vector3(x, y, 30f)), out string why))
                    failed.Add("from " + stand + " at (" + x + ", " + y + "): " + why);
                least = Mathf.Min(least, level.Wedge.Scale);
                most = Mathf.Max(most, level.Wedge.Scale);
                Unload();
            }
            Assert.IsEmpty(failed, string.Join("\n", failed));
            Assert.That(least, Is.InRange(9f, 9.9f), "the far stack stops it at the same size");
            Assert.That(most, Is.InRange(9f, 9.9f), "the far stack stops it at the same size");
        }

        [TestCase(-5.5f)]   // the head of the leaning book
        [TestCase(-3f)]
        [TestCase(0f)]
        [TestCase(5f)]
        public void FromAnywhereAlongTheEdge_AimedAtTheMiddleOfTheOutline_Works(float x)
        {
            Load();
            Assert.IsTrue(Completes(Attempt(Spawn, new Vector3(x, 4f, -0.4f), new Vector3(0f, 2.4f, 30f)), out string why), why);
        }

        // ---- No way round it ------------------------------------------------------------------------------

        [Test]
        public void WithoutTheCheese_TheExitIsOutOfReach()
        {
            Load();
            float highest = 0f, farthestUp = float.MinValue;
            string stopped = Drive(Bypass(), 60f, () =>
            {
                Vector3 p = Game.Player.Position;
                if (p.z > 20f) highest = Mathf.Max(highest, p.y);
                if (p.y > 3.9f) farthestUp = Mathf.Max(farthestUp, p.z);
            });
            Assert.IsNotNull(stopped, "the walk to the exit has to give up");
            StringAssert.Contains("WalkTo timed out", stopped);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthestUp, 9f, "a sprint jump off the start stack comes down far short of the 30 unit gap");
            Assert.Less(highest, 1.5f, "the goal stack's face is 4 high; a jump reaches 1.25");
            Assert.Less(Game.Player.Position.y, 0.1f, "the bot is still on the rug");
            Assert.AreEqual(0.4f, level.Wedge.Scale, 1e-5f, "and the cheese was never touched");
        }

        IEnumerator Bypass()
        {
            // A running jump off the edge of the start stack (the walk ends at full speed, 0.3 from the edge)...
            yield return bot.WalkTo(new Vector3(2f, 4f, -6f), 0.3f);
            yield return bot.WalkTo(new Vector3(2f, 4f, 0.45f), 0.75f, 5f, sprint: true);
            yield return bot.Jump();
            // ... on across the rug to the foot of the goal stack, jumping at its face...
            yield return bot.WalkTo(new Vector3(0f, 0f, 29.4f), 0.3f, 15f, sprint: true);
            for (int i = 0; i < 3; i++)
            {
                yield return bot.Jump();
                yield return bot.Wait(0.9f);
            }
            // ... and simply walking at the exit.
            yield return bot.WalkTo(Level01CheeseWedge.ExitCentre, 0.5f, 4f);
        }

        [Test]
        public void TheBlockAtTheEdge_IsHoppedOnto_AndLeadsNowhere()
        {
            Load();
            // The highest thing on the start stack a jump reaches: a block 1.2 high, right at the edge.
            var block = new Vector3(5.9f, 4f, -1.4f);
            Assert.IsNull(Drive(Seq(bot.WalkTo(new Vector3(4.7f, 4f, -1.5f), 0.1f), bot.WalkTo(block, 0.95f, 2f), bot.Jump(), bot.WalkTo(block, 0.25f, 1.5f))));
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 1f);
            Assert.AreEqual(5.2f, Game.Player.Position.y, 0.05f, "standing on the block");

            float farthestUp = float.MinValue;
            Drive(Seq(bot.WalkTo(new Vector3(5.9f, 4f, -0.9f), 0.15f, 2f, sprint: true), bot.Jump(), bot.WalkTo(new Vector3(5.9f, 0f, 20f), 0.5f, 4f, sprint: true)), 10f,
                () => { if (Game.Player.Position.y > 3.9f) farthestUp = Mathf.Max(farthestUp, Game.Player.Position.z); });
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthestUp, 10f, "a sprint jump off the block comes down on the rug, twenty short of the far stack");

            // And from the rug the far stack is as sheer as ever.
            float highest = 0f;
            Drive(Seq(bot.WalkTo(new Vector3(5f, 0f, 29.4f), 0.3f, 10f, sprint: true), bot.Jump(), bot.Wait(0.9f), bot.WalkTo(Level01CheeseWedge.ExitCentre, 0.5f, 2f)), 20f,
                () => { if (Game.Player.Position.z > 20f) highest = Mathf.Max(highest, Game.Player.Position.y); });
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(highest, 1.5f);
        }

        [Test]
        public void TheLeaningBook_IsWalkedBothWays_SoTheRugIsNeverATrap()
        {
            Load();
            Assert.IsNull(Drive(DownAndUp()));
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(4f, Game.Player.Position.y, 0.02f, "back on the start stack");
            Assert.Less(Game.Player.Position.z, 0f);
        }

        IEnumerator DownAndUp()
        {
            yield return level.Descend(bot);
            yield return bot.Until(() => Game.Player.Grounded && Game.Player.Position.y < 0.02f, 2f);
            yield return bot.WalkTo(new Vector3(-5.5f, 0f, 8.5f), 0.2f);
            yield return bot.WalkTo(new Vector3(-5.5f, 4f, -2f), 0.3f, 8f);
        }

        [Test]
        public void TheSpaceUnderTheLeaningBook_IsNotATrap_ForThePlayerOrTheCheese()
        {
            Load();
            Prop wedge = level.Wedge;
            // Down the book, back along its open side and in under its high end.
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), level.Descend(bot), bot.WalkTo(new Vector3(-3f, 0f, 9f), 0.3f), bot.WalkTo(new Vector3(-3f, 0f, 1.5f), 0.3f),
                bot.WalkTo(new Vector3(-5.5f, 0f, 1.5f), 0.2f))));
            Assert.Less(Game.Player.Position.y, 0.05f, "on the rug, under the book");

            // Under the book the ceiling comes down to meet the rug. The player walks in until it stops him,
            // jumps there, and walks out again.
            string stopped = Drive(bot.WalkTo(new Vector3(-5.5f, 0f, 6.5f), 0.2f, 3f));
            Assert.IsNotNull(stopped, "the book's underside stops the walk");
            Assert.Less(Game.Player.Position.z, 5f);
            Assert.IsNull(Drive(Seq(bot.Jump(), bot.Wait(1f), bot.WalkTo(new Vector3(-1f, 0f, 3f), 0.2f, 6f))), "out from under the book");

            // The cheese let go under there cannot be seen from the start stack...
            Assert.IsNull(Drive(Seq(bot.DropAt(new Vector3(-5.5f, 0.3f, 1.5f)), bot.Until(() => Level01CheeseWedge.AtRest(wedge), 5f))));
            Vector3 c = wedge.Center;
            Assert.That(c.x, Is.InRange(-7f, -4f), "under the leaning book: " + c);
            Assert.That(c.z, Is.InRange(0f, 8f), "under the leaning book: " + c);
            Assert.GreaterOrEqual(wedge.Scale, Level01CheeseWedge.CrumbScale, "big enough to stay where it was put");
            Assert.IsNull(Drive(Seq(bot.WalkTo(new Vector3(-5.5f, 0f, 9f), 0.3f), bot.WalkTo(new Vector3(-5.5f, 4f, -2f), 0.3f, 8f))));
            Assert.IsNotNull(Drive(bot.Grab(wedge, 2f)), "the book hides it from up here");
            Assert.IsNull(Game.Grabber.Held);

            // ... and from the rug it is in plain view.
            Assert.IsNull(Drive(Seq(level.Descend(bot), bot.WalkTo(new Vector3(-1f, 0f, 4f), 0.3f), bot.Grab(wedge))));
            Assert.AreSame(wedge, Game.Grabber.Held);
        }

        // ---- The scale window (LEVELS.md, appendix B: works from 5.6 to the clamp, 14) ------------------------

        [Test]
        public void ASmallerWedge_Works_WithAHop()
        {
            Load();
            int hops = 0;
            Game.Events.PlayerJumped += e => hops++;
            // Taken from 1.8 away instead of 1 the cheese looks just over half as big, and lands as a wedge
            // whose top is 1 below the stack. (The window's low end is 5.5, where the hop clears the stack by
            // 0.01; at 5.4 it does not. The level calls a hold that gives less than 6.3 too small.)
            Assert.IsTrue(Completes(Attempt(new Vector3(0f, 4f, -4.8f), Edge, Outline), out string why), why);
            Assert.That(level.Wedge.Scale, Is.InRange(5.8f, 6.3f), "near the low end of the window");
            Assert.Greater(hops, 0, "the last unit is a hop");
        }

        [Test]
        public void AHoldTheLevelDoesNotCallTooSmall_MakesAHillsideThatWorks()
        {
            Load();
            List<string> lines = Lines();
            // The farthest pick-up that still gets the teaching line and not the warning.
            Assert.IsTrue(Completes(Attempt(new Vector3(0f, 4f, -4.65f), Edge, Outline), out string why), why);
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.HoldLine }, lines);
            Assert.That(level.Wedge.Scale, Is.InRange(6.2f, 7f));
        }

        [Test]
        public void TheHighEndOfTheScaleWindow_Works_AtTheClamp()
        {
            Load();
            // Taken from as near as the spool allows and let go from eight back, it reaches the clamp a
            // little short of the far stack and comes down in front of it.
            Vector3 grabFrom = new Vector3(0f, 4f, -3.7f), dropFrom = new Vector3(2f, 4f, -8f);
            Assert.IsTrue(Completes(Attempt(grabFrom, dropFrom, new Vector3(0f, 5.55f, 30f)), out string why), why);
            Assert.AreEqual(14f, level.Wedge.Scale, 1e-3f, "the clamp");
            Assert.Less(Level01CheeseWedge.GapFar - Crest(level.Wedge).z, 2f, "its tall end is within a hop of the stack");
        }

        [Test]
        public void AWedgeThatIsClearlyTooSmall_DoesNotReach()
        {
            Load();
            float highest = 0f;
            // Taken from 2.8 away: a wedge of 4 against the stack, its top 2 below the stack's.
            Vector3 grabFrom = new Vector3(0f, 4f, -5.8f);
            string stopped = Drive(Attempt(grabFrom, Edge, Outline), 60f, () =>
            {
                if (Game.Player.Position.z > 20f) highest = Mathf.Max(highest, Game.Player.Position.y);
            });
            Assert.That(level.Wedge.Scale, Is.InRange(3.7f, 4.4f));
            Assert.That(Crest(level.Wedge).z, Is.InRange(29.4f, 30f), "it does lie against the stack");
            Assert.IsNotNull(stopped, "the climb has to give up");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(highest, 2f, "the bot did climb it and jump");
            Assert.Less(highest, 3.8f, "but came nowhere near the top of the stack");
        }

        // ---- Forgiving: where the crosshair is, where the player stands -----------------------------------

        // Heights on the far stack's face (its top is 4), on the exit mark above it (4 to 6 on the far wall is
        // 4 to 5.3 here) and on the sun behind that (up to 7 on the far wall, 6.5 here): -7.5 to +1.8 degrees
        // of pitch from the edge.
        [TestCase(1.5f)]
        [TestCase(2f)]
        [TestCase(3f)]
        [TestCase(4f)]
        [TestCase(5f)]
        [TestCase(6.5f)]
        public void AnywhereOnTheFarStacksFace_AboveItsLowestThird_AndOnTheExitMarkAndTheSun_Works(float y)
        {
            Load();
            Assert.IsTrue(Completes(Attempt(Spawn, Edge, new Vector3(0f, y, 30f)), out string why), why);
            if (y >= 2f) Assert.AreEqual(9.43f, level.Wedge.Scale, 0.15f, "the face stops the wedge at the same size whatever the pitch");
        }

        [Test]
        public void TheSunOnTheFarWall_IsNoTallerThanWhatCanBeAimedAt()
        {
            Load();
            // The top of the painted sun, seen from the edge: the highest thing there is to aim the cheese at.
            Bounds sunDisc = default;
            bool found = false;
            foreach (MeshRenderer renderer in Game.LevelRoot.Find("Chest Sun Side").GetComponentsInChildren<MeshRenderer>())
            {
                // The walls and the rim band run the length of the chest; the disc does not.
                if (renderer.bounds.size.x > 8f || renderer.bounds.size.z > 1f) continue;
                sunDisc = renderer.bounds;
                found = true;
            }
            Assert.IsTrue(found, "the sun disc is the one small painted piece of the chest");
            Assert.AreEqual(Level01CheeseWedge.FarZ, sunDisc.center.z, 0.1f, "on the far wall");
            Assert.That(sunDisc.max.y, Is.InRange(6f, 6.7f), "from the edge a crosshair up to 7 on the far wall still seats the wedge; above it the cheese goes over");
            Assert.IsTrue(Completes(Attempt(Spawn, Edge, new Vector3(0f, sunDisc.max.y - 0.05f, sunDisc.center.z)), out string why), why);
        }

        [TestCase(-3f)]
        [TestCase(3f)]
        public void OffToTheSide_Works(float x)
        {
            Load();
            Assert.IsTrue(Completes(Attempt(Spawn, Edge, new Vector3(x, 2f, 30f)), out string why), why);
        }

        [Test]
        public void LettingGoFromWhereItWasTaken_WorksToo()
        {
            Load();
            // Never moving from the spawn: the far stack is four units further away than from the edge, so
            // the wedge is bigger.
            Assert.IsTrue(Completes(Attempt(Spawn, Spawn, new Vector3(0f, 4f, 30f)), out string why), why);
            Assert.That(level.Wedge.Scale, Is.InRange(9.9f, 11f));
        }

        // ---- How big it looks: where the cheese is taken from ----------------------------------------------

        [Test]
        public void TakenFromFarOff_TheCheeseStaysSmall_TheLevelSaysWhy_AndItIsPutRightFromTheRug()
        {
            Load();
            List<string> lines = Lines();

            // Six units from the spool the cheese looks a sixth as big as from beside it; against the far
            // stack that is a wedge 2 long. The level says what went wrong at the grab itself.
            string stopped = Drive(Attempt(FarOff, Edge, Outline));
            Assert.That(level.Wedge.Scale, Is.InRange(1.6f, 2.4f));
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.SmallLine }, lines);
            Assert.IsNotNull(stopped, "the climb has to give up");
            Assert.IsFalse(Game.LevelCompleted);

            // Not a dead end: on the rug it is taken from close by - now it looks big - and held up against
            // the stack from twenty units back.
            Assert.IsTrue(Completes(FromTheRug(), out string why), why);
            Assert.Greater(level.Wedge.Scale, 8f);
            Assert.AreEqual(Level01CheeseWedge.HoldLine, lines[lines.Count - 1], "a hold that can work gets the teaching line");
            Assert.AreEqual(2, lines.Count);
        }

        IEnumerator FromTheRug()
        {
            Prop wedge = level.Wedge;
            yield return bot.WalkTo(new Vector3(0f, 0f, 26f), 0.3f, 10f);
            yield return bot.Grab(wedge);
            yield return bot.WalkTo(new Vector3(0f, 0f, 10f), 0.3f, 15f);
            yield return bot.DropAt(new Vector3(0f, 3.6f, 30f));
            yield return bot.Until(() => Level01CheeseWedge.AtRest(wedge), 8f);
            yield return level.Ascend(bot);
        }

        [TestCase(-4f, true)]       // the spawn, one unit from the spool
        [TestCase(-4.6f, true)]     // 1.6 away
        [TestCase(-5f, false)]      // 2 away: a wedge of 5.4, a hair too low for the hop
        [TestCase(-9f, false)]      // six away
        public void TheLevelTellsAHoldThatCanWork_FromOneThatCannot(float z, bool canWork)
        {
            Load();
            List<string> lines = Lines();
            Assert.IsNull(Drive(level.Take(bot, new Vector3(0f, 4f, z))));
            CollectionAssert.AreEqual(new[] { canWork ? Level01CheeseWedge.HoldLine : Level01CheeseWedge.SmallLine }, lines);
            Assert.AreEqual(canWork, Game.Grabber.Ratio >= Level01CheeseWedge.LooksTooSmall);
        }

        [Test]
        public void ACrumbTooSmallToUse_GoesBackToItsSpool()
        {
            Load();
            Prop wedge = level.Wedge;
            List<string> lines = Lines();
            int returns = 0;
            Game.Events.LeashReturned += e => returns++;

            // Taken from six away and put down three away it is half the size it started with. From a floor
            // nothing that small can be made to look big: "pick it up from closer" could not be followed.
            Assert.IsNull(Drive(Seq(level.Take(bot, FarOff), bot.DropAt(new Vector3(0f, 4f, -6f)), bot.Wait(0.5f))));
            Assert.Less(wedge.Scale, 0.3f);
            Assert.AreEqual(0, returns, "it is given a moment");
            RunSeconds(1.5f);
            Assert.AreEqual(1, returns);
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f, "at the size the level gave it");
            Assert.Less((wedge.Center - Spool).magnitude, 0.05f, "on the spool");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.SmallLine, Level01CheeseWedge.CrumbLine }, lines);

            // Once is enough: on its spool it stays, and from there the level is solved as ever.
            RunSeconds(3f);
            Assert.AreEqual(1, returns);
            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void ASmallCheeseStrandedOnTheFarStack_ComesBackWhenItIsPutDown()
        {
            Load();
            Prop wedge = level.Wedge;
            List<string> lines = Lines();
            int returns = 0;
            Game.Events.LeashReturned += e => returns++;

            // Taken from six away and aimed over the far stack's edge: a wedge of 2 or 3 on top of the far
            // stack, where the player cannot go. "Closer" is not on offer.
            Assert.IsNull(Drive(Seq(level.Take(bot, FarOff), RoundTheSpool(), level.Place(bot, Edge, new Vector3(0f, 4.6f, 38f)))));
            Assert.Greater(wedge.Center.z, 30f, "over the far stack: " + wedge.Center);
            Assert.Greater(wedge.Center.y, 4f, "on top of it: " + wedge.Center);
            Assert.That(wedge.Scale, Is.InRange(Level01CheeseWedge.CrumbScale, 4f));
            RunSeconds(3f);
            Assert.AreEqual(0, returns, "big enough to stay where it was put");

            // It is in view from the start stack. Taken from there it looks tiny; put down a few steps away it
            // is a crumb, and the crumb goes back to its spool. (Nearer than three units a hold this small
            // cannot be put down at all - the cheese has a smallest size - and the click leaves it where it
            // could last lie.)
            string stopped = Drive(Seq(bot.Grab(wedge), bot.DropAt(new Vector3(2f, 4f, -5f)), bot.Until(() => returns > 0, 5f)));
            Assert.IsNull(stopped, stopped + " (" + Where() + ", held " + wedge.Held + ", ratio " + Game.Grabber.Ratio + ", valid " + Game.Grabber.PlacementValid + ")");
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f);
            Assert.Less((wedge.Center - Spool).magnitude, 0.05f, "back on the spool");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.SmallLine, Level01CheeseWedge.SmallLine, Level01CheeseWedge.CrumbLine }, lines);
            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void TheSpoolTheBlocksAndTheDominoes_AreNotToys()
        {
            Load();
            // Clicked on from where the player starts: the pedestal under the cheese, the block at the edge,
            // the dominoes behind, the leaning book's head, the far stack and the exit mark.
            foreach (Vector3 at in new[]
            {
                new Vector3(0f, 4.5f, -3f), new Vector3(5.9f, 4.6f, -1.4f), new Vector3(5.5f, 5f, -10.9f), new Vector3(-5.6f, 4.75f, -10.5f),
                new Vector3(-5.5f, 3.9f, 0.3f), new Vector3(0f, 2f, 30f), Level01CheeseWedge.ExitCentre,
            })
            {
                Assert.IsNull(Drive(Seq(bot.LookAt(at), bot.Click(), bot.Wait(0.1f))));
                Assert.IsNull(Game.Grabber.Held, "a click at " + at + " took something");
            }
            Assert.AreEqual(0.4f, level.Wedge.Scale, 1e-5f);
            Assert.Less((level.Wedge.Center - Spool).magnitude, 0.02f, "and the cheese is where it was");
        }

        [Test]
        public void TheCrumbKnockedOffItsSpool_GoesBackToo()
        {
            Load();
            Prop wedge = level.Wedge;
            List<string> lines = Lines();
            int returns = 0;
            Game.Events.LeashReturned += e => returns++;

            // Walking at the spool does nothing; jumping onto it kicks the crumb to the floor, where it lies
            // upside down and can only be taken from right above it.
            Assert.IsNotNull(Drive(bot.WalkTo(new Vector3(0f, 4f, -3f), 0.1f, 2f)), "the spool is in the way");
            float off = 0f;
            Assert.IsNull(Drive(Seq(bot.Jump(), bot.WalkTo(new Vector3(0f, 4f, -2f), 0.1f, 2f), bot.Until(() => returns > 0, 4f)), 10f,
                () => off = Mathf.Max(off, (wedge.Center - Spool).magnitude)));
            Assert.Greater(off, 1f, "the jump knocked it to the floor");
            Assert.AreEqual(1, returns);
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f);
            Assert.Less((wedge.Center - Spool).magnitude, 0.05f, "back on the spool");
            Assert.AreEqual(0f, Quaternion.Angle(wedge.Rotation, Quaternion.identity), 0.5f, "thin end toward the spawn again");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.SpoolLine }, lines);

            Assert.IsNull(Drive(RoundTheSpool()));
            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void ACheesePutDownBigEnough_StaysWhereItIsPut_AndIsTakenAgain()
        {
            Load();
            Prop wedge = level.Wedge;
            int returns = 0;
            Game.Events.LeashReturned += e => returns++;
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), bot.DropAt(new Vector3(1.5f, 4f, -4f)), bot.Until(() => Level01CheeseWedge.AtRest(wedge), 5f))));
            Assert.Greater(wedge.Scale, Level01CheeseWedge.CrumbScale);
            Vector3 put = wedge.Center;
            RunSeconds(4f);
            Assert.AreEqual(0, returns);
            Assert.Less((wedge.Center - put).magnitude, 0.05f);

            // Taken again from the same spot it looks exactly as big as before, and makes the same hillside.
            Assert.IsNull(Drive(bot.Grab(wedge)));
            Assert.GreaterOrEqual(Game.Grabber.Ratio, 1.4f * Level01CheeseWedge.LooksTooSmall);
            Assert.IsTrue(Completes(Seq(RoundTheSpool(), level.Place(bot, Edge, Outline), level.Climb(bot)), out string why), why);
            Assert.That(level.Wedge.Scale, Is.InRange(8.9f, 9.9f));
        }

        // ---- Which way round it is --------------------------------------------------------------------------

        [Test]
        public void TakenFromBehindTheSpool_ItLandsAsACliff_TheLevelSaysSo_AndTurnedRoundItWorks()
        {
            Load();
            Prop wedge = level.Wedge;
            List<string> lines = Lines();

            // The hold keeps the cheese turned the way it was when it was taken. From behind the spool its
            // tall end is toward the player, and that is how it lands.
            Assert.IsNull(Drive(Seq(RoundTheSpool(), bot.WalkTo(new Vector3(0f, 4f, -1.9f), 0.05f), bot.Grab(wedge), level.Place(bot, Edge, Outline))));
            Assert.Greater(wedge.Scale, 8f, "a hillside all the same");
            Assert.Less(Rise(wedge).z, -0.9f, "its tall end toward the start stack");
            Assert.Greater(Level01CheeseWedge.GapFar - Crest(wedge).z, 8f, "and nowhere near the far stack");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.HoldLine, Level01CheeseWedge.BackwardLine }, lines);

            // Taken again and turned half round (Q twelve times), it is the hillside.
            Assert.IsTrue(Completes(Seq(bot.Grab(wedge), bot.RotateHeld(12), level.Place(bot, Edge, Outline), level.Climb(bot)), out string why), why);
            Assert.Greater(Rise(wedge).z, 0.9f);
            Assert.AreEqual(2, lines.Count, "nothing more to say");
        }

        [Test]
        public void LyingSideOn_ItIsClimbedAlongItsCrest()
        {
            Load();
            Prop wedge = level.Wedge;
            List<string> lines = Lines();

            // Taken from beside the spool it lands with its slope rising toward a side wall and one end of
            // its crest against the far stack.
            Assert.IsNull(Drive(Seq(bot.WalkTo(new Vector3(1.1f, 4f, -3f), 0.05f), bot.Grab(wedge), level.Place(bot, Edge, Outline))));
            Assert.Less(Mathf.Abs(Rise(wedge).z), 0.5f, "side-on");
            CollectionAssert.AreEqual(new[] { Level01CheeseWedge.HoldLine }, lines, "no warning: this one can be climbed");

            Vector3 along = Rise(wedge), toe = Toe(wedge), crest = Crest(wedge);
            Vector3 a = Local(wedge, -1f, 1f, 1f), b = Local(wedge, 1f, 1f, 1f);
            Vector3 end = a.z > b.z ? a : b;
            Assert.Less(Level01CheeseWedge.GapFar - end.z, 0.5f, "the crest's far end touches the stack");
            Assert.Greater(end.y, Level01CheeseWedge.StackTop, "and is above it");

            // Up the slope, along the top to the stack, and over.
            Assert.IsTrue(Completes(Seq(
                level.Descend(bot),
                bot.WalkTo(toe - along, 0.3f, 15f),
                bot.WalkTo(crest - along * 0.5f, 0.2f, 15f),
                bot.WalkTo(new Vector3(end.x, 0f, end.z - 0.4f) - along * 0.5f, 0.2f, 10f),
                bot.WalkTo(new Vector3(end.x - along.x * 0.5f, 4f, 31f), 0.75f, 5f),
                bot.Jump(),
                bot.WalkTo(Level01CheeseWedge.ExitCentre, 0.5f, 10f)), out string why), why);
        }

        [Test]
        public void StoodOnItsEndWithF_ItIsRightedByTakingItAgain()
        {
            Load();
            Prop wedge = level.Wedge;
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), bot.RotateHeld(0, 1), RoundTheSpool(), level.Place(bot, Edge, Outline))));
            Assert.Less((wedge.Rotation * Vector3.up).y, 0.3f, "standing on its tall end, a tower in the middle of the rug");
            Assert.Greater(wedge.Scale, 3f);
            Assert.IsTrue(Completes(Seq(bot.Grab(wedge), bot.Wait(0.5f), level.Place(bot, Edge, Outline), level.Climb(bot)), out string why), why);
            Assert.Greater((wedge.Rotation * Vector3.up).y, 0.99f, "taking it rights it");
            Assert.That(wedge.Scale, Is.InRange(8.9f, 9.9f));
        }

        // ---- No soft lock ---------------------------------------------------------------------------------

        [TestCase(0.3f)]    // the cheese in hand, at the spawn
        [TestCase(1f)]      // in hand at the edge
        [TestCase(1.68f)]   // let go, still falling
        [TestCase(3f)]      // the hillside in place, the bot on its way to the leaning book
        [TestCase(5f)]      // on the leaning book
        [TestCase(8f)]      // on the rug
        [TestCase(10.6f)]   // on the crest
        [TestCase(11.5f)]   // on the far stack, short of the exit
        public void RestartingMidSolve_AndSolvingAgain_Works(float seconds)
        {
            Load();
            int said = 0;
            Game.Events.Message += e => said++;
            var runner = new BotRunner(level.Solve(bot), bot, Game);
            for (int i = TestHelpers.Ticks(seconds); i > 0 && runner.Advance(); i--) Game.Tick();
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(1, said, "the teaching line was said at the first grab");
            Assert.IsTrue(level.Wedge.Held || level.Wedge.Scale > 5f, "the solve was under way");

            Game.RestartLevel();
            Assert.AreSame(level, Game.Level);
            Prop wedge = level.Wedge;
            Assert.AreEqual(1, Game.Props.Count);
            Assert.AreSame(wedge, Game.Props[0]);
            Assert.IsFalse(wedge.Held);
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f);
            Assert.Less((wedge.Center - Spool).magnitude, 1e-3f);
            Assert.Less((Game.Player.Position - Spawn).magnitude, 1e-3f);
            Assert.AreEqual(Level01CheeseWedge.SpawnPitch, Game.Player.Pitch, 1e-3f);

            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(2, said, "and again after the restart: Build set the level's own state up anew");
            Assert.That(wedge.Scale, Is.InRange(9.4f * 0.95f, 9.4f * 1.05f));
        }

        [Test]
        public void ACheeseThatFallsOutOfTheWorld_ComesBackToTheSpool()
        {
            Load();
            Prop wedge = level.Wedge;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            // Test fixture, not player input: a hillside of cheese below the kill plane.
            wedge.SetScale(9f);
            wedge.SetPose(new Vector3(3f, Game.KillY - 5f, 10f), Quaternion.Euler(40f, 10f, 0f));
            Game.Tick();
            Assert.AreEqual(1, respawned);
            RunSeconds(1f);
            Assert.AreEqual(0.4f, wedge.Scale, 1e-5f, "at the size the level gave it");
            Assert.Less((wedge.Center - Spool).magnitude, 0.02f, "on the spool");
            TestHelpers.PlayLevel(Game, 90f);
        }

        // Aimed over the chest's walls and at the open sky, the cheese stays in the chest, in view, and can be taken again.
        [TestCase(40f, 9f, 10f)]
        [TestCase(-40f, 9f, 20f)]
        [TestCase(0f, 60f, 20f)]
        [TestCase(0f, 10f, 90f)]
        public void TheCheeseCannotLeaveTheChest(float x, float y, float z)
        {
            Load();
            Prop wedge = level.Wedge;
            Assert.IsNull(Drive(ThrowAt(new Vector3(x, y, z))));
            Vector3 c = wedge.Center;
            Assert.That(c.x, Is.InRange(-7f, 7f), "inside the chest: " + c);
            Assert.That(c.z, Is.InRange(-12f, 46f), "inside the chest: " + c);
            Assert.That(c.y, Is.InRange(0f, 12f), "inside the chest: " + c);
            Assert.LessOrEqual(wedge.Scale, 14f);
            Assert.IsNull(Drive(bot.Grab(wedge)), "wherever it lies, it can be seen and taken again");
            Assert.AreSame(wedge, Game.Grabber.Held);
        }

        IEnumerator ThrowAt(Vector3 point)
        {
            yield return level.Take(bot, Spawn);
            yield return bot.DropAt(point);
            yield return bot.Until(() => Level01CheeseWedge.AtRest(level.Wedge), 10f);
        }

        // Two ways a hillside comes down short of the far stack. The crosshair on the rug in front of it: the
        // wedge's base meets the rug first. And from beside a side wall, the crosshair on that side's end of
        // the outline: the wedge's side meets the wall first. Taken again from the same spot it looks exactly
        // as big as before, and the middle of the outline puts it right.
        [TestCase(2f, -0.6f, 0f, 0f, 27f)]
        [TestCase(-5.5f, -0.4f, -3f, 2.4f, 30f)]
        public void AWedgeDroppedShort_IsTakenAgainFromTheSameSpot_AndPlacedRight(float standX, float standZ, float aimX, float aimY, float aimZ)
        {
            Load();
            var stand = new Vector3(standX, 4f, standZ);
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), RoundTheSpool(standX), level.Place(bot, stand, new Vector3(aimX, aimY, aimZ)))));
            Assert.Greater(level.Wedge.Scale, 6.5f, "a hillside");
            Assert.Greater(Level01CheeseWedge.GapFar - Crest(level.Wedge).z, 4f, "short of the stack");
            Assert.IsTrue(Completes(Again(stand, new Vector3(0f, 3f, 30f)), out string why), why);
            Assert.That(level.Wedge.Scale, Is.InRange(8.9f, 9.9f));
        }

        // The most likely first miss gets a word, once the hillside lies still: the crosshair on the rug in
        // front of the far stack or on the lowest part of its face. One that stops a hop short of the stack,
        // and the intended drop, get none.
        [TestCase(0f, 27f, true)]       // on the rug, three in front of the stack
        [TestCase(1f, 30f, true)]       // the foot of the face: about 3.5 short
        [TestCase(1.5f, 30f, false)]    // 1.5 short: a hop
        [TestCase(2.4f, 25.3f, false)]  // the solver's aim
        public void AHillsideThatFallsShortOfTheFarStack_IsToldSo_Once(float aimY, float aimZ, bool told)
        {
            Load();
            List<string> lines = Lines();
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), RoundTheSpool(), level.Place(bot, Edge, new Vector3(0f, aimY, aimZ)), bot.Wait(1f))));
            float gap = Level01CheeseWedge.GapFar - Crest(level.Wedge).z;
            Assert.AreEqual(told, gap > Level01CheeseWedge.ShortGap, "the crest stopped " + gap.ToString("0.00") + " short (" + Where() + ")");
            Assert.AreEqual(told ? 1 : 0, lines.FindAll(line => line == Level01CheeseWedge.ShortLine).Count, string.Join(" | ", lines));
            if (!told) return;

            // Standing about says nothing more; taken again and held over the outline it is the hillside, and nothing is said of it.
            Assert.IsNull(Drive(bot.Wait(3f)));
            Assert.IsTrue(Completes(Again(Edge, Outline), out string why), why);
            Assert.AreEqual(1, lines.FindAll(line => line == Level01CheeseWedge.ShortLine).Count, string.Join(" | ", lines));
        }

        [Test]
        public void AWedgeThrownOnTopOfTheFarStack_IsTakenBackFromTheStartStack()
        {
            Load();
            Prop wedge = level.Wedge;
            // The crosshair well above the far stack, on the wall behind it: the cheese goes over the edge.
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), RoundTheSpool(), level.Place(bot, Edge, new Vector3(0f, 8f, 30f)))));
            Assert.Greater(wedge.Center.z, 30f, "over the far stack");
            Assert.Greater(wedge.Center.y, 6f, "on top of it");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(Completes(Again(Edge, Outline), out string why), why);
            Assert.That(wedge.Scale, Is.InRange(8.9f, 9.9f), "it looked as big as before, and the far stack's face made it the same hillside");
        }

        [Test]
        public void TheSlotBetweenAShortHillsideAndTheFarStack_IsWalkedOutOf()
        {
            Load();
            Prop wedge = level.Wedge;
            // The crosshair a little low on the far stack's face: the tall end stops one and a half short.
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), RoundTheSpool(), level.Place(bot, Edge, new Vector3(0f, 1.5f, 30f)), level.Descend(bot))));
            float slot = Level01CheeseWedge.GapFar - Crest(wedge).z;
            Assert.That(slot, Is.InRange(1f, 2f));

            // Walking straight off the crest drops the player into the slot at the foot of the stack...
            Vector3 along = Rise(wedge);
            string stopped = Drive(Seq(bot.WalkTo(Toe(wedge) - along, 0.3f, 15f), bot.WalkTo(Crest(wedge) - along * 0.5f, 0.2f, 15f), bot.WalkTo(Level01CheeseWedge.ExitCentre, 0.5f, 4f)));
            Assert.IsNotNull(stopped);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(Game.Player.Position.y, 0.1f, "on the rug");
            Assert.Greater(Game.Player.Position.z, 28.5f, "between the cheese and the stack");

            // ... which is open at both ends: out, round, up again and over with a hop.
            Assert.IsTrue(Completes(Seq(bot.WalkTo(new Vector3(5.8f, 0f, 29.3f), 0.3f, 6f), level.Ascend(bot)), out string why), why);
        }

        [Test]
        public void AHillsideLetGoAtThePlayersFeet_DoesNotPinHim()
        {
            Load();
            Prop wedge = level.Wedge;
            Assert.IsNull(Drive(Seq(level.Take(bot, Spawn), RoundTheSpool(), level.Place(bot, Edge, Outline), level.Descend(bot), bot.WalkTo(new Vector3(0f, 0f, 14f), 0.3f))));
            // Taken from the rug, six from its toe, and let go looking at the rug a step ahead.
            Assert.IsNull(Drive(Seq(bot.Grab(wedge), bot.DropAt(new Vector3(0f, 0f, 14.9f)), bot.Wait(2f))));
            Assert.Less((wedge.Center - Game.Player.Position).magnitude, 2f, "it came down round the player's feet");
            Assert.IsNull(Drive(bot.WalkTo(new Vector3(0f, 0f, 6f), 0.3f, 6f)), "and he walks away from it");
            Assert.IsNull(Drive(bot.Grab(wedge)));
            Assert.AreSame(wedge, Game.Grabber.Held);
        }
    }
}
