using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using UnityEngine;
using L = Toybox.Levels.Level03ShrinkingApple;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 3, "Shrinking the Apple" (LEVELS.md): the bot's solution, what the first picture shows, the
    /// drop from anywhere beside the cup, the scale window, the shut box, restarts at awkward moments, and
    /// the leash that brings a lost apple back.
    /// </summary>
    public class Level03Tests : SimTest
    {
        const float CupX = L.CupX, CupZ = L.CupZ;
        static readonly Vector3 CupFoot = new Vector3(CupX, 0f, CupZ);

        L Level => (L)Game.Level;

        Bot Load()
        {
            Game?.Dispose();
            Game = Game.Create();
            Game.LoadLevel(3);
            return new Bot(Game);
        }

        static float FromTheCup(Vector3 point) => new Vector2(point.x - CupX, point.z - CupZ).magnitude;

        // Where the apple ended up: IN (on the button, the flap opens), MOUTH (too big, held by the throat),
        // RIM (at rest on the cup's rim), SHELF (the leash took it back), out (anywhere else in the box).
        string Outcome()
        {
            L level = Level;
            Prop apple = level.Apple;
            Vector3 c = apple.Center;
            float r = FromTheCup(c);
            string where;
            if (level.Button.Pressed) where = "IN";
            else if (apple.Frozen) where = "SHELF";
            else if (r < L.ThroatRadius && c.y < L.ThroatY + 0.15f && Funnel.Passes(apple.Radius, L.ThroatRadius)) where = "TUBE";
            else if (r < L.MouthRadius - 0.05f && c.y > L.ThroatY && c.y < 2f) where = "MOUTH";
            else if (r < L.CupOuterRadius + 0.15f && c.y > L.RimY && c.y < 1.6f) where = "RIM";
            else where = "out";
            return where + " " + apple.Scale.ToString("0.00");
        }

        // ---- The solution -------------------------------------------------------------------------------

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            L level = Level;
            Assert.AreEqual("shrinking-apple", level.Slug);
            Assert.AreEqual(1, level.Phase);
            Assert.AreEqual("cardboard-box", level.Environment);
            Assert.AreEqual(3, level.Hints.Length);
            Prop apple = level.Apple;
            Assert.IsTrue(apple.Frozen, "the apple waits on the shelf");
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f);
            Assert.AreEqual(1, Game.Props.Count, "the apple is the only thing that can be picked up");
            Assert.IsTrue(level.Exit.Locked, "the exit opens with the flap");

            var drops = new List<PropHoldEvent>();
            Game.Events.PropDropped += drops.Add;
            TestHelpers.PlayLevel(Game, 60f);

            Assert.AreEqual(1, drops.Count, "one grab and one drop solve it");
            Assert.AreEqual(38.4f, drops[0].GrabDistance, 0.3f, "the apple is taken from 38 away");
            Assert.AreEqual(0.45f, apple.Scale, 0.03f, "let go over the cup from beside it, it is a marble of 0.45");
            Assert.IsTrue(level.Button.Pressed);
            Assert.AreSame(apple, level.Button.PressedBy);
            Assert.IsTrue(level.Flap.IsOpen);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(FromTheCup(apple.Center), 0.12f, "the marble lies on the button");
            Assert.AreEqual(L.ButtonY + apple.Radius, apple.Center.y, 0.02f);
            Assert.Less(Game.Time, 8f, "the bot needs about four seconds");
        }

        [Test]
        public void TheSolver_SolvesItFiveTimesInARow_TheSameWayEveryTime()
        {
            float first = 0f, firstTime = 0f;
            for (int run = 0; run < 5; run++)
            {
                Load();
                TestHelpers.PlayLevel(Game, 60f);
                float scale = Level.Apple.Scale;
                if (run == 0)
                {
                    first = scale;
                    firstTime = Game.Time;
                }
                Assert.AreEqual(first, scale, 1e-5f, "run " + run + ": the simulation is deterministic");
                Assert.AreEqual(firstTime, Game.Time, 1e-4f, "run " + run);
            }

            // And five more times in the same game, restarting from the level's solved state.
            for (int run = 0; run < 5; run++)
            {
                Game.RestartLevel();
                Assert.IsFalse(Game.LevelCompleted);
                TestHelpers.PlayLevel(Game, 60f);
                Assert.AreEqual(first, Level.Apple.Scale, 1e-3f, "restart " + run);
            }
        }

        // ---- The first picture --------------------------------------------------------------------------

        // Where a point is in the picture: (0, 0) the middle, +-1 the edges (70 degrees high, 16 : 9).
        static Vector2 InThePicture(Player player, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(player.LookRotation) * (point - player.Eye);
            float tan = Mathf.Tan(35f * Mathf.Deg2Rad);
            return local.z <= 0.01f ? new Vector2(99f, 99f) : new Vector2(local.x / (local.z * tan * 16f / 9f), local.y / (local.z * tan));
        }

        bool Sees(Vector3 point, out RaycastHit hit)
        {
            Vector3 eye = Game.Player.Eye;
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out hit, to.magnitude + 0.5f, Layers.SolidMask, QueryTriggerInteraction.Ignore);
        }

        [Test]
        public void FromTheSpawn_TheApple_TheCup_ItsButton_AndTheFlap_AreAllInThePicture()
        {
            Load();
            Player player = Game.Player;
            Prop apple = Level.Apple;
            Assert.AreEqual(L.SpawnPitch, player.Pitch, 1e-3f);
            Assert.Less(Vector3.Distance(player.Position, L.SpawnPoint), 0.05f);

            // The apple, over the low wall. Seen from below, the shelf's front edge hides its underside; its
            // middle and everything above are in plain view, and inside the picture.
            foreach (float offset in new[] { 0f, 0.5f, 0.9f })
            {
                Vector3 point = apple.Center + Vector3.up * (apple.Radius * offset);
                Assert.IsTrue(Sees(point, out RaycastHit hit));
                Assert.AreSame(apple, PropRef.Of(hit.collider), "the eye sees the apple at height " + point.y + ", not " + hit.collider.name);
                Vector2 at = InThePicture(player, point);
                Assert.Less(Mathf.Abs(at.x), 0.2f, "the apple is in the middle of the picture");
                Assert.That(at.y, Is.InRange(0.5f, 0.93f), "and in its upper part (height " + point.y + " is at " + at.y + ")");
            }
            // Its outline, too: the top of a ball this near is not the point above its middle but where the
            // line of sight grazes it.
            Vector3 toApple = apple.Center - player.Eye;
            float top = Mathf.Atan2(toApple.y, new Vector2(toApple.x, toApple.z).magnitude) * Mathf.Rad2Deg + Mathf.Asin(apple.Radius / toApple.magnitude) * Mathf.Rad2Deg;
            Assert.Less(Mathf.Tan((top - player.Pitch) * Mathf.Deg2Rad) / Mathf.Tan(35f * Mathf.Deg2Rad), 0.95f, "the whole apple is under the top edge of the picture");

            // The cup: its whole rim is in the picture, lower right.
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                var rim = new Vector3(CupX + Mathf.Cos(angle) * L.MouthRadius, L.RimY, CupZ + Mathf.Sin(angle) * L.MouthRadius);
                Vector2 at = InThePicture(player, rim);
                Assert.That(at.x, Is.InRange(0f, 0.7f), "rim point " + i + " is at " + at);
                Assert.That(at.y, Is.InRange(-0.9f, -0.3f), "rim point " + i + " is at " + at);
            }
            // The lit button at the bottom of it: the far half of the cap is seen over the near rim.
            Vector3 away = (CupFoot - L.SpawnPoint).normalized;
            Vector3 cap = new Vector3(CupX, L.ButtonY + 0.04f, CupZ) + away * 0.15f;
            Assert.IsTrue(Sees(cap, out RaycastHit floor));
            Assert.Less(FromTheCup(floor.point), L.ThroatRadius + 0.02f, "the line of sight to the button ends in the tube, at " + floor.point);
            Assert.Less(floor.point.y, L.ButtonY + 0.1f, "on its floor (it reached " + floor.point + ")");

            // The flap's signal bar, on the right.
            var bar = new Vector3(L.HalfX - 0.02f, 1.9f, 0f);
            Vector2 barAt = InThePicture(player, bar);
            Assert.That(barAt.x, Is.InRange(0.3f, 0.95f), "the bar is at " + barAt);
            Assert.That(Mathf.Abs(barAt.y), Is.LessThan(0.5f));
            Assert.IsTrue(Sees(bar, out RaycastHit flapHit));
            Assert.AreEqual("Flap", flapHit.collider.name, "nothing stands between the eye and the flap");

            TestHelpers.LookAt(player, apple.Center);
            Assert.AreSame(apple, Game.Grabber.FindTarget(), "looking at it, a click takes it");
        }

        // ---- The drop, from anywhere a player would stand ------------------------------------------------

        static Vector3 Beside(float bearing, float distance) =>
            CupFoot + new Vector3(Mathf.Sin(bearing * Mathf.Deg2Rad), 0f, Mathf.Cos(bearing * Mathf.Deg2Rad)) * distance;

        static readonly Vector3 AtTheButton = new Vector3(CupX, L.ButtonY + 0.02f, CupZ);
        static readonly Vector3 AtTheRim = new Vector3(CupX, L.RimY, CupZ);

        static IEnumerator DirectDrop(Bot bot, L level, Vector3 stand, Vector3 aim)
        {
            yield return L.WalkRoundTheCup(bot, stand, 0.08f);
            yield return bot.Grab(level.Apple);
            yield return bot.Wait(0.2f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(2.5f);
        }

        // Standing anywhere round the cup, as far as 1.8 from its middle, and looking at the button or into
        // the throat: the apple goes in and the flap opens. (The builder's cup - rim 0.6, a flat lip - only
        // took it from within 1.25, and from 1.4 the marble came to rest on the lip or beside the cup.)
        [Test]
        public void FromAnywhereBesideTheCup_LetGoOverTheButton_TheAppleGoesIn()
        {
            var failures = new List<string>();
            foreach (float bearing in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
            foreach (float distance in new[] { 1.05f, 1.3f, 1.55f, 1.8f })
            {
                Vector3 stand = Beside(bearing, distance);
                if (Mathf.Abs(stand.x) > L.HalfX - 0.35f || Mathf.Abs(stand.z) > L.HalfZ - 0.35f) continue;
                foreach (Vector3 aim in new[] { AtTheButton, L.CupAim })
                {
                    Bot bot = Load();
                    BotRunner.Run(Game, DirectDrop(bot, Level, stand, aim), 30f);
                    string outcome = Outcome();
                    if (!outcome.StartsWith("IN")) failures.Add("bearing " + bearing + ", " + distance + " away, aim y " + aim.y + ": " + outcome);
                    else Assert.That(Level.Apple.Scale, Is.InRange(0.36f, 0.54f), "bearing " + bearing + ", " + distance + " away");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // Looking at the middle of the cup at rim height the apple comes out bigger: 0.48 from beside the cup,
        // and from 1.6 on it stops against the near rim at 0.56 .. 0.63 whatever the distance. That fits too.
        [Test]
        public void LetGoOverTheMiddleOfTheCup_ItFits_UpToOnePointSixAway()
        {
            var failures = new List<string>();
            float biggest = 0f;
            foreach (float bearing in new[] { 0f, 90f, 180f, 270f })
            foreach (float distance in new[] { 1.05f, 1.2f, 1.4f, 1.6f })
            {
                Vector3 stand = Beside(bearing, distance);
                if (Mathf.Abs(stand.x) > L.HalfX - 0.35f) continue;
                Bot bot = Load();
                BotRunner.Run(Game, DirectDrop(bot, Level, stand, AtTheRim), 30f);
                string outcome = Outcome();
                biggest = Mathf.Max(biggest, Level.Apple.Scale);
                if (!outcome.StartsWith("IN")) failures.Add("bearing " + bearing + ", " + distance + " away: " + outcome);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Less(biggest, L.ThroatRadius * 2f - 0.015f, "the biggest of them clears the throat with room to spare");
        }

        // Whatever the stand point and the aim, the apple never comes to rest on the cup's rim: the mouth is
        // the whole top of the cup. It is in the cup, in its mouth (too big), or on the floor of the box.
        [Test]
        public void NoDrop_LeavesTheApple_OnTheRimOfTheCup()
        {
            var seen = new Dictionary<string, int>();
            foreach (float bearing in new[] { 20f, 200f, 290f })
            foreach (float distance in new[] { 1.1f, 1.45f, 1.7f, 2f, 2.5f })
            {
                Vector3 stand = Beside(bearing, distance);
                // (From nearer the low wall than this, the wall hides the apple.)
                if (Mathf.Abs(stand.x) > L.HalfX - 0.35f || stand.z < -L.HalfZ + 0.35f || stand.z > 1f) continue;
                Vector3 toward = (CupFoot - stand).normalized;
                // The near slope, the near rim, the far slope, the far rim.
                foreach (Vector3 aim in new[] { AtTheRim - toward * 0.5f + Vector3.down * 0.07f, AtTheRim - toward * L.MouthRadius, AtTheRim + toward * 0.5f + Vector3.down * 0.07f, AtTheRim + toward * L.MouthRadius })
                {
                    Bot bot = Load();
                    BotRunner.Run(Game, DirectDrop(bot, Level, stand, aim), 30f);
                    string outcome = Outcome();
                    string kind = outcome.Split(' ')[0];
                    seen.TryGetValue(kind, out int count);
                    seen[kind] = count + 1;
                    Assert.AreNotEqual("RIM", kind, "bearing " + bearing + ", " + distance + " away, aim " + aim + ": " + outcome + " at " + Level.Apple.Center);
                    Assert.IsFalse(Level.Leash.OutOfBounds(Level.Apple), "it stays in the box");
                    if (kind == "MOUTH") Assert.GreaterOrEqual(Level.Apple.Scale, L.ThroatRadius * 2f, "only an apple that is too big stays in the mouth");
                }
            }
            Assert.IsTrue(seen.ContainsKey("IN") && seen.ContainsKey("MOUTH") && seen.ContainsKey("out"), "the aims cover all three results");
        }

        // The one drop in sixty that did: a held apple that meets the rim on its way is let go right over it,
        // and came to rest balanced on the edge (0.36 across, 1.45 away, aimed at the near slope). The rim
        // tips whatever balances on it into the cup.
        [TestCase(0.2f, 30f)]
        [TestCase(0.36f, 71f)]
        [TestCase(0.5f, 200f)]
        [TestCase(0.9f, 135f)]
        public void AnAppleBalancedOnTheRim_IsTippedIntoTheCup(float scale, float bearing)
        {
            Load();
            L level = Level;
            Prop apple = level.Apple;
            apple.Unfreeze();
            apple.SetScale(scale);
            // Fixture: right over the rim's edge, touching it.
            Vector3 rim = Beside(bearing, L.MouthRadius + 0.004f);
            apple.SetPose(new Vector3(rim.x, L.RimY + scale * 0.5f + 0.003f, rim.z), Quaternion.identity);
            RunSeconds(3f);
            string outcome = Outcome();
            if (scale < L.ThroatRadius * 2f) Assert.IsTrue(level.Button.Pressed, "a marble of " + scale + " put on the rim is " + outcome + " at " + apple.Center);
            else StringAssert.StartsWith("MOUTH", outcome, "an apple of " + scale + " put on the rim rolls into the mouth (it is at " + apple.Center + ")");
        }

        // The brief's own route: look straight down at your feet, let go, and carry the marble to the cup.
        [TestCase(0f)]
        [TestCase(0.35f)]
        [TestCase(1f)]
        public void TwoSteps_AMarbleMadeAtTheFeet_IsCarriedToTheCup(float inFront)
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            float atTheFeet = 0f;
            BotRunner.Run(Game, FeetThenCup(bot, level, inFront, s => atTheFeet = s), 40f);

            Assert.That(atTheFeet, Is.InRange(0.33f, 0.47f), "let go over the floor at the feet the apple is a marble of about 0.4");
            Assert.IsTrue(level.Button.Pressed, "carried to the cup it presses the button (it is " + apple.Scale + " now, at " + apple.Center + ")");
            Assert.That(apple.Scale, Is.InRange(0.3f, 0.55f));
            TestHelpers.PlayLevel(Game, 30f);
        }

        static IEnumerator FeetThenCup(Bot bot, L level, float inFront, Action<float> report)
        {
            // From the spawn: take the apple and look at the floor at the feet, or just in front of them.
            yield return bot.Grab(level.Apple);
            yield return bot.DropAt(bot.Player.Position + new Vector3(0f, 0f, inFront));
            yield return bot.Until(() => level.Apple.Velocity.sqrMagnitude < 0.01f && level.Apple.Center.y < 0.5f, 4f);
            report(level.Apple.Scale);
            // Pick the marble up where it lies and carry it to the cup.
            yield return bot.Grab(level.Apple);
            yield return bot.WalkTo(L.StandPoint, 0.15f);
            yield return bot.DropAt(L.CupAim);
            yield return bot.Until(() => level.Button.Pressed, 4f);
        }

        static IEnumerator FeetThenCarry(Bot bot, L level, Vector3 feetOffset, Vector3 pickFrom, Vector3 stand, Vector3 aim)
        {
            Prop apple = level.Apple;
            yield return bot.Grab(apple);
            yield return bot.DropAt(bot.Player.Position + feetOffset);
            yield return bot.Until(() => apple.Velocity.sqrMagnitude < 0.01f && apple.Center.y < 1f, 5f);
            yield return L.WalkRoundTheCup(bot, pickFrom, 0.15f);
            yield return bot.Grab(apple);
            yield return L.WalkRoundTheCup(bot, stand, 0.08f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(2.5f);
        }

        // The marble does not have to be picked up from where it was made. Taken from across the box it is
        // a pea by the time it is over the cup - and a pea presses the button too. (With the spec's button,
        // which wanted 0.27, every one of the far pick-ups below ended "too light".)
        [Test]
        public void TwoSteps_WhereverTheMarbleIsPickedUpFrom_ItPressesTheButton()
        {
            var failures = new List<string>();
            float smallest = 99f;
            foreach (Vector3 pick in new[] { L.SpawnPoint, new Vector3(-1.5f, 0f, -2f), new Vector3(-2.5f, 0f, 0f), new Vector3(-2.2f, 0f, -0.5f) })
            foreach (float distance in new[] { 1.05f, 1.3f, 1.6f })
            foreach (Vector3 aim in new[] { AtTheButton, L.CupAim, AtTheRim })
            {
                Bot bot = Load();
                BotRunner.Run(Game, FeetThenCarry(bot, Level, new Vector3(0f, 0f, 0.35f), pick, Beside(200f, distance), aim), 40f);
                string outcome = Outcome();
                smallest = Mathf.Min(smallest, Level.Apple.Scale);
                if (!outcome.StartsWith("IN")) failures.Add("picked up from " + pick + ", let go " + distance + " away, aim y " + aim.y + ": " + outcome);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Less(smallest, 0.2f, "the far pick-ups made peas (the smallest was " + smallest + ")");
        }

        // From the far corner by the low wall the marble is more than six away: over the cup it would have to
        // be smaller than the apple can get, so the hold does not reach the cup and the pea comes down on the
        // floor. It is a pea, in the box, in plain view: walked up to and carried over, it presses the button.
        [Test]
        public void AMarbleTakenFromTheFarthestCorner_EndsAsAPeaOnTheFloor_AndThatIsNotTheEndOfIt()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            BotRunner.Run(Game, FeetThenCarry(bot, level, new Vector3(0f, 0f, 0.35f), new Vector3(-2.5f, 0f, 3.3f), Beside(200f, 1.2f), AtTheButton), 40f);
            Assert.Less(apple.Scale, 0.2f, "a pea (it is " + Outcome() + " at " + apple.Center + ")");
            Assert.IsFalse(level.Leash.OutOfBounds(apple));
            Assert.IsFalse(apple.Frozen);
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(0, level.Leash.Returns, "without the leash's help");
        }

        // The same pea held over the button from right beside the cup: the button is nearer than the pea can
        // be brought (a toy at its smallest cannot be let go nearer than it was picked up from, as it looks),
        // so it flies past the cup and lies on the floor beyond it. The level says why, once, and what to do;
        // walked up to and carried over, it goes in.
        [Test]
        public void APeaThatFliesPastTheCup_IsToldWhy_AndCarriedOverFromCloseBy()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            BotRunner.Run(Game, FeetThenCarry(bot, level, new Vector3(0f, 0f, 0.35f), new Vector3(-2.5f, 0f, 3.3f), Beside(200f, 1.05f), AtTheButton), 40f);
            string where = Outcome() + " at " + apple.Center.ToString("F2") + ", " + FromTheCup(apple.Center).ToString("0.00") + " from the cup's axis";
            Assert.Less(apple.Scale, L.PeaScale, "a pea (" + where + ")");
            Assert.IsFalse(level.Button.Pressed, where);
            Assert.Greater(FromTheCup(apple.Center), L.CupOuterRadius, "it lies on the floor beside the cup (" + where + ")");
            Assert.IsFalse(level.Leash.OutOfBounds(apple));
            Assert.AreEqual(1, said.FindAll(text => text == L.PeaLine).Count, where + ": " + string.Join(" | ", said));

            RunSeconds(3f);
            Assert.AreEqual(1, said.FindAll(text => text == L.PeaLine).Count, "said once, not again while it lies there");
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(0, level.Leash.Returns, "without the leash's help");
            Assert.AreEqual(1, said.FindAll(text => text == L.PeaLine).Count, "picked up from beside it and carried over, nothing more is said: " + string.Join(" | ", said));
        }

        // A pea that goes into the cup, or a marble put where the view asked for it, hears nothing of the kind.
        [Test]
        public void ThePeaLine_IsOnlyForAPeaThatMissedTheCup()
        {
            Bot bot = Load();
            L level = Level;
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            DropIntoTheCup(L.SmallestApple);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Button.Pressed, 4f));
            RunSeconds(2f);
            Assert.IsFalse(said.Contains(L.PeaLine), "a pea on the button: " + string.Join(" | ", said));

            bot = Load();
            level = Level;
            said.Clear();
            Game.Events.Message += e => said.Add(e.Text);
            BotRunner.Run(Game, FeetThenCup(bot, level, 0.35f, s => { }), 40f);
            RunSeconds(2f);
            Assert.IsFalse(said.Contains(L.PeaLine), "the two-step route with a marble of 0.4: " + string.Join(" | ", said));
        }

        // ---- The scale window: whatever passes the throat (0.64) presses the button ----------------------

        // Puts the apple over the cup at a scale, as a test fixture would (this is not player input).
        void DropIntoTheCup(float scale)
        {
            Prop apple = Level.Apple;
            apple.Unfreeze();
            apple.SetScale(scale);
            apple.SetPose(new Vector3(CupX + 0.15f, L.RimY + 0.2f + scale * 0.5f, CupZ - 0.1f), Quaternion.identity);
        }

        [TestCase(0.1f)]
        [TestCase(0.15f)]
        [TestCase(0.27f)]
        [TestCase(0.40f)]
        [TestCase(0.50f)]
        [TestCase(0.62f)]
        public void AMarbleThatPassesTheThroat_PressesTheButton_AndTheFlapFalls(float scale)
        {
            Bot bot = Load();
            L level = Level;
            int rejected = 0;
            level.Button.OnRejected += (prop, load) => rejected++;
            DropIntoTheCup(scale);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Button.Pressed, 4f),
                "a marble of " + scale + " (mass " + level.Apple.Mass + ") presses the button; it is at " + level.Apple.Center);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsOpen, 2f), "the flap falls open within its 0.8 s");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Apple.Velocity.sqrMagnitude < 0.01f, 3f));
            Assert.AreEqual(L.ButtonY + scale * 0.5f, level.Apple.Center.y, 0.02f, "it went down the tube and lies on the button");
            Assert.AreEqual(0, rejected, "nothing that lies on the button is too light for it");
            Assert.IsFalse(Game.LevelCompleted, "the way is open, the player still has to walk it");

            // The button is latched: whatever happens to the marble now, the box stays open.
            BotRunner.Run(Game, WalkOut(bot), 20f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        static IEnumerator WalkOut(Bot bot)
        {
            yield return L.WalkRoundTheCup(bot, new Vector3(2.5f, 0f, 0f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 0f), 0.5f);
            yield return bot.Until(() => bot.Game.LevelCompleted, 2f);
        }

        [Test]
        public void TheAppleCannotBeMadeSmallerThanAPea_AndAPeaIsEnough()
        {
            Load();
            Prop apple = Level.Apple;
            Assert.AreEqual(L.SmallestApple, apple.MinScale, 1e-5f);
            apple.Unfreeze();
            apple.SetScale(0.01f);
            Assert.AreEqual(L.SmallestApple, apple.Scale, 1e-5f, "the clamp");
            Assert.GreaterOrEqual(apple.Mass, L.ButtonMass, "its weight at that size presses the button");
            Assert.IsTrue(Funnel.Passes(apple.Radius, L.ThroatRadius));
        }

        [TestCase(0.66f)]
        [TestCase(0.8f)]
        [TestCase(1.4f)]
        [TestCase(1.85f)]
        public void AnAppleThatIsTooBig_SitsInTheMouth_AndTheLevelSaysSo(float scale)
        {
            Load();
            L level = Level;
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            DropIntoTheCup(scale);
            RunSeconds(4f);
            Prop apple = level.Apple;
            Assert.IsFalse(level.Button.Pressed, "an apple of " + scale + " does not pass the throat (radius " + L.ThroatRadius + ")");
            Assert.IsFalse(level.Flap.IsOpen);
            Assert.Greater(apple.Center.y, L.ThroatY, "it sits in the mouth");
            Assert.Less(FromTheCup(apple.Center), 0.3f, "over the throat");
            // (The funnel's hard gate carries a ball on the throat's rim by cancelling gravity tick by tick: its
            // speed flickers between 0 and one tick of gravity, 0.37, a little more for a big one. It moves 0.007.)
            Assert.Less(apple.Velocity.magnitude, 0.6f, "at rest");
            Vector3 before = apple.Center;
            RunSeconds(1f);
            Assert.Less(Vector3.Distance(before, apple.Center), 0.1f, "it stays where it is");
            Assert.AreEqual(1, said.FindAll(text => text == L.TooBigLine).Count, "the level says what is wrong, once");
            Assert.IsFalse(level.Leash.OutOfBounds(apple), "and the leash leaves it there");

            // The level's own solver takes it from there.
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void GrownAcrossTheBox_TheAppleIsTooBig_AndTheSolverStillFindsTheWay()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            BotRunner.Run(Game, FromAcrossTheBox(bot, level), 30f);
            Assert.Greater(apple.Scale, 0.7f, "let go at the cup from across the box the apple is far too big");
            Assert.IsFalse(level.Button.Pressed);
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 60f);
            Assert.That(apple.Scale, Is.InRange(0.2f, 0.6f));
        }

        static IEnumerator FromAcrossTheBox(Bot bot, L level)
        {
            yield return bot.WalkTo(new Vector3(-2f, 0f, -2.6f));
            yield return bot.Grab(level.Apple);
            yield return bot.DropAt(AtTheRim);
            yield return bot.Wait(3f);
        }

        // ---- No way round the puzzle --------------------------------------------------------------------

        [Test]
        public void WalkingAndJumpingAtTheExit_DoesNotCompleteTheLevel()
        {
            Bot bot = Load();
            L level = Level;
            Player player = Game.Player;

            // Straight at the exit: the bot ends up against the shut flap and gets no further.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(2.4f, 0f, 0.6f)), 10f);
            Assert.Throws<BotException>(() => BotRunner.Run(Game, bot.WalkTo(new Vector3(6f, 0f, 0.6f), 0.5f, 4f), 10f));
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(player.Position.x, 3f, "the shut flap stops the capsule");

            // Sprinting and jumping at the flap and at the low wall.
            float highest = 0f;
            Action track = () => highest = Mathf.Max(highest, player.Position.y);
            BotRunner.Run(Game, Push(bot, new Vector3(8f, 0f, 0.6f), 3f, true, track), 10f);
            BotRunner.Run(Game, Push(bot, new Vector3(0f, 0f, 9f), 4f, true, track), 10f);
            Assert.Less(highest, 1.3f, "a jump from the floor rises 1.25");

            Assert.IsFalse(Game.LevelCompleted, "the exit is outside the box");
            Assert.IsFalse(level.Flap.IsOpen);
            Assert.IsTrue(level.Exit.Locked);
            Assert.Less(Mathf.Abs(player.Position.x), 3f);
            Assert.Less(Mathf.Abs(player.Position.z), 4f, "still inside the box (at " + player.Position + ")");
        }

        // Runs at a point for a while, jumping whenever the bot is on the ground.
        static IEnumerator Push(Bot bot, Vector3 toward, float seconds, bool sprint, Action eachTick)
        {
            IEnumerator walk = bot.WalkTo(toward, 0.05f, seconds + 10f, sprint);
            for (int tick = TestHelpers.Ticks(seconds); tick > 0 && walk.MoveNext(); tick--)
            {
                if (bot.Player.Grounded && tick % 4 == 0) bot.Jump().MoveNext();
                eachTick?.Invoke();
                yield return null;
            }
        }

        // A hop from beside the cup onto it, steering for its middle all the way down, until the bot stands there.
        static IEnumerator OntoTheCup(Bot bot)
        {
            Player player = bot.Player;
            yield return L.WalkRoundTheCup(bot, CupFoot + new Vector3(-1.15f, 0f, 0f), 0.08f);
            yield return bot.LookAt(new Vector3(CupX, 1.5f, CupZ));
            yield return bot.Jump(false);
            int standing = 0;
            for (int tick = 0; tick < 240 && standing < 20; tick++)
            {
                if (FromTheCup(player.Position) > 0.1f) bot.WalkTo(CupFoot, 0.1f, 1f).MoveNext();
                standing = player.Grounded && player.Position.y > 0.05f ? standing + 1 : 0;
                yield return null;
            }
        }

        // The cup is the one thing in the box to stand on. The player's capsule is about as wide as the
        // tube: whoever steps into the cup slides down into it and stands on the button, which does not
        // care. The tube is 0.18 deep, more than a step: walking does not get out of it, a jump does. And a
        // jump from the cup is no higher than a jump from the floor: far under the lowest wall (4).
        [Test]
        public void StandingInTheCup_PressesNothing_TrapsNobody_AndLeadsNowhere()
        {
            Bot bot = Load();
            L level = Level;
            Player player = Game.Player;
            BotRunner.Run(Game, OntoTheCup(bot), 20f);
            Assert.IsTrue(player.Grounded, "the bot stands in the cup, at " + player.Position);
            Assert.Less(FromTheCup(player.Position), L.MouthRadius);
            Assert.Greater(player.Position.y, 0.05f);

            // On to its middle: down the tube, onto the button.
            RunSeconds(1f);
            Assert.IsTrue(player.Grounded);
            Assert.Less(FromTheCup(player.Position), 0.1f, "the bot is in the tube, at " + player.Position);
            Assert.AreEqual(L.ButtonY, player.Position.y, 0.03f, "standing on the button");
            Assert.IsFalse(level.Button.Pressed, "the player is not something the button counts");

            // Jumping about in it: never higher than a jump from its rim.
            float highest = 0f;
            BotRunner.Run(Game, Push(bot, new Vector3(CupX, 0f, CupZ + 0.2f), 2f, false, () => highest = Mathf.Max(highest, player.Position.y)), 10f);
            Assert.Less(highest, L.RimY + 1.3f, "a jump from the cup stays far under the lowest wall");
            Assert.IsFalse(level.Button.Pressed);

            // Out again, by a jump, and on with the level.
            BotRunner.Run(Game, Push(bot, new Vector3(-2f, 0f, CupZ), 1.5f, false, null), 10f);
            RunSeconds(1f);
            Assert.Greater(FromTheCup(player.Position), L.CupOuterRadius + 0.25f, "the bot got out of the cup (it is at " + player.Position + ")");
            Assert.AreEqual(0f, player.Position.y, 0.02f);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // Standing IN the cup, the nearest thing there is are one's own feet: the apple let go there is a
        // marble that falls through the player onto the button.
        [Test]
        public void StandingInTheCup_AndLettingGoAtOnesFeet_AlsoSolvesIt()
        {
            Bot bot = Load();
            L level = Level;
            BotRunner.Run(Game, InTheCupDrop(bot, level), 30f);
            Assert.IsTrue(level.Button.Pressed, "the apple is " + Outcome() + " at " + level.Apple.Center + ", the bot at " + Game.Player.Position);
            Assert.That(level.Apple.Scale, Is.InRange(0.3f, 0.6f));
            TestHelpers.PlayLevel(Game, 60f);
        }

        static IEnumerator InTheCupDrop(Bot bot, L level)
        {
            yield return OntoTheCup(bot);
            yield return bot.Grab(level.Apple);
            yield return bot.Wait(0.2f);
            yield return bot.DropAt(AtTheButton);
            yield return bot.Wait(2f);
        }

        // The one bypass the real simulation allows (the spec's "a bigger apple cannot be mounted" does not
        // hold): jump off the apple, take it in the air, let go at the top of the jump - it comes down bigger
        // under one's own feet. Six jumps make a 1.85 apple 4.6 tall, higher than the low wall.
        [Test]
        public void RidingAGrowingAppleOverTheWall_EndsBackInTheBox_NotAtTheExit()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            Player player = Game.Player;
            int respawns = 0;
            Vector3 putBack = default;
            var said = new List<string>();
            Game.Events.PlayerRespawned += e =>
            {
                respawns++;
                putBack = e.To;
            };
            Game.Events.Message += e => said.Add(e.Text);

            // Fixture: an apple of 1.85 and the player on top of it.
            apple.Unfreeze();
            apple.SetScale(1.85f);
            apple.SetPose(new Vector3(-0.6f, 0.925f, 1f), Quaternion.identity);
            RunSeconds(0.5f);
            player.Teleport(apple.Center + Vector3.up * (apple.Radius + 0.02f), 0f, -89f);
            RunSeconds(0.5f);
            Assert.AreSame(apple, player.GroundProp);

            BotRunner.Run(Game, Surf(bot, apple), 40f);
            Assert.Greater(apple.Scale, 4.2f, "the apple grew under the bot's feet");
            Assert.Greater(player.Position.y, L.LowWall, "and the bot stands above the low wall");

            // Off the top and over the wall.
            float farthest = 0f;
            BotRunner.Run(Game, Push(bot, new Vector3(-0.6f, 0f, 12f), 3f, true, () => farthest = Mathf.Max(farthest, player.Position.z)), 10f);
            Assert.Greater(farthest, 4.7f, "the bot got out of the box, over the +Z wall");
            Assert.AreEqual(1, respawns, "and was put back at the start");
            Assert.IsTrue(said.Contains(L.OutsideLine));
            Assert.Less(Vector3.Distance(putBack, L.SpawnPoint), 0.05f, "at the start (it was put at " + putBack + ")");
            Assert.Less(Mathf.Abs(player.Position.x), 3f);
            Assert.Less(Mathf.Abs(player.Position.z), 4f, "it is inside the box again (at " + player.Position + ")");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(level.Exit.Locked, "the exit only opens with the flap");

            // Nothing is lost by it: the level still solves.
            TestHelpers.PlayLevel(Game, 60f);
            Assert.IsFalse(level.Exit.Locked);
        }

        static IEnumerator Surf(Bot bot, Prop apple)
        {
            Player player = bot.Player;
            for (int jump = 0; jump < 8 && apple.Scale < 4.5f; jump++)
            {
                yield return bot.Until(() => player.Grounded, 3f);
                yield return bot.LookAt(apple);
                yield return bot.Jump(false);
                // In the air the apple is no longer what the bot stands on: a click takes it.
                yield return bot.Until(() => bot.Game.Grabber.FindTarget() == apple, 1f);
                yield return bot.Click();
                yield return bot.LookAt(player.Position + Vector3.down * 5f, 1f);
                yield return bot.Until(() => player.Velocity.y <= 0.2f, 2f);
                yield return bot.Click();
                yield return bot.Until(() => player.Grounded, 3f);
                yield return bot.Wait(0.3f);
            }
        }

        // An apple as big as the box: lying in it (5.9), or on its walls as a lid (6.04). Nobody is crushed
        // or shut in, and the solver gets on with it. A still bigger one rolls off or is taken back.
        [TestCase(3f)]
        [TestCase(5.9f)]
        [TestCase(6.04f)]
        [TestCase(8f)]
        public void AnAppleAsBigAsTheBox_IsNotTheEndOfTheLevel(float scale)
        {
            Load();
            L level = Level;
            Prop apple = level.Apple;
            apple.Unfreeze();
            apple.SetScale(scale);
            apple.SetPose(new Vector3(0f, 12f, 0f), Quaternion.identity);
            RunSeconds(6f);
            Assert.IsTrue(apple.Frozen || !level.Leash.OutOfBounds(apple), "it is in the box or back on the shelf (at " + apple.Center + ")");
            TestHelpers.PlayLevel(Game, 60f);
            Assert.That(apple.Scale, Is.InRange(L.SmallestApple, L.ThroatRadius * 2f));
        }

        // Where the apple can be taken from: the back two thirds of the box. Nearer the low wall than about
        // z = 1.2 the wall itself hides it (the arrows printed on that wall point up at it).
        [Test]
        public void TheAppleCanBeTaken_FromAnywhereInTheBackTwoThirdsOfTheBox()
        {
            foreach (float x in new[] { -2.6f, -1.2f, 0f, 2.6f })
            foreach (float z in new[] { -3.6f, -2f, 0f, 1f })
            {
                Bot bot = Load();
                var stand = new Vector3(x, 0f, z);
                if (FromTheCup(stand) < 1.2f) continue;
                Assert.DoesNotThrow(() => BotRunner.Run(Game, WalkAndTake(bot, Level.Apple, stand), 30f), "from " + stand);
                Assert.AreSame(Level.Apple, Game.Grabber.Held);
                Assert.That(Game.Grabber.Ratio, Is.InRange(0.28f, 0.32f), "it is 38 .. 40 away from everywhere: the same apple in the hand wherever it is taken");
            }

            // From right under the low wall there is nothing to take.
            Bot last = Load();
            BotRunner.Run(Game, last.WalkTo(new Vector3(0f, 0f, 3.2f)), 10f);
            Assert.Throws<BotException>(() => BotRunner.Run(Game, last.Grab(Level.Apple, 2f), 10f));
        }

        static IEnumerator WalkAndTake(Bot bot, Prop apple, Vector3 stand)
        {
            yield return L.WalkRoundTheCup(bot, stand, 0.2f);
            yield return bot.Grab(apple);
        }

        // Whoever is put back at the start while holding the apple (the keepers outside the box do that)
        // still holds it, and goes on from there.
        [Test]
        public void PutBackAtTheStart_WithTheAppleInHand_TheLevelGoesOn()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            Player player = Game.Player;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, bot.Grab(apple), 10f);
            // Fixture: outside the box, beside the shelf wall.
            player.Teleport(new Vector3(-8f, 0f, 2f), 0f, 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 2f), "the keeper outside the box takes the player back");
            Assert.Less(Vector3.Distance(player.Position, L.SpawnPoint), 0.2f);
            Assert.AreSame(apple, Game.Grabber.Held, "the apple is still in hand");
            Assert.IsFalse(Game.LevelCompleted);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // On top of a wall one is not "outside", but from there the exit is still locked and a jump down the
        // outside ends at the start. (Nothing but the growing apple gets anybody up there.)
        [TestCase(3.15f, 6f, 0f, 6f, 0f)]
        [TestCase(0f, 4f, 4.15f, 0f, 12f)]
        [TestCase(-3.15f, 6f, 0f, -9f, 0f)]
        public void FromTheTopOfAWall_AJumpOutside_EndsAtTheStart(float x, float y, float z, float towardX, float towardZ)
        {
            Bot bot = Load();
            L level = Level;
            Player player = Game.Player;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            // Fixture: on top of the wall.
            player.Teleport(new Vector3(x, y + 0.02f, z), 0f, 0f);
            RunSeconds(1f);
            Assert.AreEqual(0, respawns, "standing on a wall is allowed (at " + player.Position + ")");
            Assert.Greater(player.Position.y, y - 0.1f);

            BotRunner.Run(Game, Push(bot, new Vector3(towardX, 0f, towardZ), 2.5f, true, null), 10f);
            RunSeconds(1f);
            Assert.AreEqual(1, respawns, "outside the box the keeper takes the player back");
            Assert.IsFalse(Game.LevelCompleted, "even through the exit's own box (it is locked)");
            Assert.IsTrue(level.Exit.Locked);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // The cup is a shell with nothing inside: a pea that got through its cone would lie in the body, out
        // of sight for good. Small and fast toys are swept, so however far a pea falls it stays on the cone.
        [Test]
        public void APeaFallingOntoTheCup_FromHighUp_GoesDownTheTube_NotThroughTheCone()
        {
            foreach (float scale in new[] { L.SmallestApple, 0.2f, 0.4f })
            foreach (float height in new[] { 2f, 6f })
            foreach (float off in new[] { 0f, 0.25f, 0.45f, 0.62f })
            {
                Load();
                L level = Level;
                Prop apple = level.Apple;
                apple.Unfreeze();
                apple.SetScale(scale);
                apple.SetPose(new Vector3(CupX + off * 0.6f, height, CupZ + off * 0.8f), Quaternion.identity);
                RunSeconds(4f);
                Vector3 at = apple.Center;
                bool inTheBody = FromTheCup(at) < L.CupOuterRadius - apple.Radius * 0.5f && !level.Button.Pressed && at.y < L.RimY;
                Assert.IsFalse(inTheBody, "a pea of " + scale + " dropped from " + height + ", " + off + " off the axis, lies at " + at);
                Assert.IsTrue(level.Button.Pressed || FromTheCup(at) > L.CupOuterRadius,
                    "it rolled onto the button, or bounced off the cone and lies on the floor (a pea of " + scale + " from " + height + ", " + off + " off the axis, is at " + at + ")");
                if (off < 0.3f) Assert.IsTrue(level.Button.Pressed, "over the tube it goes straight in (a pea of " + scale + " from " + height + ")");
            }
        }

        // ---- Restart and soft-locks ---------------------------------------------------------------------

        void AssertFresh(L level)
        {
            Prop apple = level.Apple;
            Assert.AreSame(level, Game.Level, "a restart builds the same level again");
            Assert.IsTrue(apple.Frozen);
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(apple.Center, L.AppleOrigin), 1e-3f);
            Assert.IsFalse(level.Button.Pressed);
            Assert.IsTrue(level.Flap.IsClosed);
            Assert.IsTrue(level.Exit.Locked);
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.SpawnPoint), 0.05f);
        }

        [Test]
        public void RestartingAtAwkwardMoments_PutsEverythingBack_AndItSolvesAgain()
        {
            Bot bot = Load();
            L level = Level;

            // With a marble lying on the floor.
            BotRunner.Run(Game, HalfWay(bot, level), 20f);
            Assert.IsFalse(level.Apple.Frozen);
            Assert.Less(level.Apple.Scale, 1f, "half way: a marble lies on the floor of the box");
            Game.RestartLevel();
            AssertFresh(level);

            // With the apple in hand.
            bot = new Bot(Game);
            BotRunner.Run(Game, bot.Grab(level.Apple), 10f);
            Assert.AreSame(level.Apple, Game.Grabber.Held);
            Game.RestartLevel();
            AssertFresh(level);

            // With the apple in the air over the cup.
            bot = new Bot(Game);
            BotRunner.Run(Game, UpToTheDrop(bot, level), 20f);
            Assert.IsFalse(level.Button.Pressed);
            Game.RestartLevel();
            AssertFresh(level);

            // While the flap is falling.
            bot = new Bot(Game);
            BotRunner.Run(Game, UpToTheDrop(bot, level), 20f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Button.Pressed, 3f));
            RunSeconds(0.3f);
            Assert.IsFalse(level.Flap.IsOpen, "the flap is on its way down");
            Assert.IsFalse(level.Flap.IsClosed);
            Game.RestartLevel();
            AssertFresh(level);

            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(0.45f, level.Apple.Scale, 0.03f);

            // And once more after it has been solved.
            Game.RestartLevel();
            AssertFresh(level);
            TestHelpers.PlayLevel(Game, 60f);
        }

        static IEnumerator HalfWay(Bot bot, L level)
        {
            yield return bot.WalkTo(new Vector3(-1f, 0f, -1f));
            yield return bot.Grab(level.Apple);
            yield return bot.DropAt(new Vector3(-1.5f, 0f, 0.5f));
            yield return bot.Wait(1f);
        }

        static IEnumerator UpToTheDrop(Bot bot, L level)
        {
            yield return bot.WalkTo(L.StandPoint, 0.15f);
            yield return bot.Grab(level.Apple);
            yield return bot.DropAt(L.CupAim);
        }

        [Test]
        public void AnAppleThrownOutOfTheBox_IsBackOnTheShelf_TwoSecondsLater()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;

            // Taken from the middle of the box and let go under the open sky, over the low wall.
            BotRunner.Run(Game, ThrowOut(bot, level), 20f);
            Assert.IsFalse(apple.Frozen);
            Assert.IsTrue(level.Leash.OutOfBounds(apple), "the apple is outside the box, at " + apple.Center);
            int ticks = 0;
            while (!apple.Frozen && ticks++ < TestHelpers.Ticks(4f)) Game.Tick();
            Assert.IsTrue(apple.Frozen, "the leash brought it back");
            Assert.AreEqual(2f, ticks * Sim.Dt, 0.2f, "after its grace of two seconds");
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(1, level.Leash.Returns);
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f, "at full size");
            Assert.Less(Vector3.Distance(apple.Center, L.AppleOrigin), 1e-3f, "on the shelf");

            TestHelpers.PlayLevel(Game, 60f);
        }

        // Open sky between the box and the shelf: an apple let go up there grows to its clamp and comes down
        // outside the box.
        static readonly Vector3 SkyAim = new Vector3(0f, 40f, 12f);

        static IEnumerator ThrowOut(Bot bot, L level)
        {
            yield return bot.WalkTo(new Vector3(0f, 0f, -1f));
            yield return bot.Grab(level.Apple);
            yield return bot.DropAt(SkyAim);
        }

        // Soft-lock found in review. A marble carried to the low wall, taken again from the back of the box
        // (it looks tiny from there) and let go at the wall above the shelf lands ON the shelf, 2.7 across,
        // behind the shelf's front edge: out of sight from everywhere in the box. The builder's leash allowed
        // everything within 8 of the apple's origin, so it stayed there for good.
        [Test]
        public void AnAppleLeftSmallOnTheShelf_OutOfSight_IsTakenBack()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            float landed = 0f;
            Vector3 where = default;
            BotRunner.Run(Game, OntoTheShelf(bot, level, (s, at) => { landed = s; where = at; }), 60f);
            Assert.That(landed, Is.InRange(1.5f, 6f), "the apple came down on the shelf as a ball of " + landed + " at " + where);
            Assert.Greater(where.z, 24f, "behind the shelf's front edge (it was let go at " + where + ")");
            Assert.Greater(where.y, L.ShelfTop);

            Assert.IsTrue(TestHelpers.RunUntil(Game, () => apple.Frozen, 6f), "the leash takes it back (it is at " + apple.Center + ", scale " + apple.Scale + ")");
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(apple.Center, L.AppleOrigin), 1e-3f);
            TestHelpers.PlayLevel(Game, 60f);
        }

        static IEnumerator OntoTheShelf(Bot bot, L level, Action<float, Vector3> report)
        {
            Prop apple = level.Apple;
            yield return bot.Grab(apple);
            yield return bot.DropAt(bot.Player.Position + new Vector3(0f, 0f, 0.35f));
            yield return bot.Wait(1f);
            // Carry the marble to the low wall and leave it there.
            yield return bot.Grab(apple);
            yield return bot.WalkTo(new Vector3(-0.3f, 0f, 3.2f), 0.2f);
            yield return bot.DropAt(new Vector3(-0.3f, 0f, 3.6f));
            yield return bot.Wait(1f);
            // From the back of the box it is seven away: take it and let go at the wall above the shelf.
            yield return bot.WalkTo(new Vector3(-0.3f, 0f, -3.5f), 0.2f);
            yield return bot.Grab(apple);
            yield return bot.DropAt(new Vector3(0f, 30f, 36f));
            report(apple.Scale, apple.Center);
        }

        // Let go again straight after the grab, the apple is not where it was: the hold ends where the apple
        // first touches something, and that is the shelf's front edge. It hangs in front of the shelf, 8
        // across, falls to the den floor - and is back on the shelf two seconds later.
        [Test]
        public void LetGoStraightAfterTheGrab_TheAppleFallsOffItsShelf_AndIsPutBack()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            BotRunner.Run(Game, GrabAndLetGo(bot, apple), 20f);
            Assert.IsFalse(apple.Frozen, "it was taken");
            Assert.That(apple.Scale, Is.InRange(6f, 11.4f), "and let go in front of the shelf, at " + apple.Center);
            Assert.Less(apple.Center.z, L.AppleOrigin.z);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => apple.Frozen, 5f), "the leash takes it back (it is at " + apple.Center + ")");
            Assert.AreEqual(1, level.Leash.Returns);
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f);
            TestHelpers.PlayLevel(Game, 60f);
        }

        static IEnumerator GrabAndLetGo(Bot bot, Prop apple)
        {
            yield return bot.Grab(apple);
            yield return bot.Wait(0.2f);
            yield return bot.Drop();
        }

        // Found in review: the builder's leash box reached over the +X wall (x 3.3, y 6.5), so a marble on
        // top of that wall was "inside". On top of any wall it is out of reach, and now it is out of bounds.
        [TestCase(3.15f, 6.3f, 2.5f)]
        [TestCase(3.15f, 6.3f, 0f)]
        [TestCase(-3.15f, 6.3f, 0f)]
        [TestCase(0f, 4.3f, 4.15f)]
        [TestCase(0f, 6.3f, -4.15f)]
        public void AMarbleOnTopOfAWall_IsTakenBack(float x, float y, float z)
        {
            Load();
            L level = Level;
            Prop apple = level.Apple;
            apple.Unfreeze();
            apple.SetScale(0.4f);
            apple.SetPose(new Vector3(x, y, z), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => apple.Frozen, 5f), "a marble put on the wall at " + new Vector3(x, y, z) + " is at " + apple.Center + " after 5 s");
            Assert.AreEqual(1, level.Leash.Returns);
        }

        // In the flap's recess (the wall is 0.3 thick, the flap 0.08) a marble is in plain view and in reach.
        [Test]
        public void AMarbleInTheFlapsRecess_StaysThere_AndCanBeTaken()
        {
            Bot bot = Load();
            L level = Level;
            Prop apple = level.Apple;
            apple.Unfreeze();
            apple.SetScale(0.3f);
            apple.SetPose(new Vector3(3.06f, 0.2f, 0.5f), Quaternion.identity);
            RunSeconds(4f);
            Assert.IsFalse(apple.Frozen, "the leash leaves it alone (it lies at " + apple.Center + ")");
            Assert.AreEqual(0, level.Leash.Returns);
            BotRunner.Run(Game, bot.Grab(apple), 10f);
            BotRunner.Run(Game, bot.Drop(), 10f);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // Wherever a marble lies on the floor of the box, it can be walked up to and taken, and the level's
        // own solver does just that: the cup stands free, nothing is behind it. (The builder's cup stood 0.5
        // from the wall, and a marble behind it could only be clicked from one side.)
        [Test]
        public void AMarbleAnywhereOnTheFloor_IsWalkedUpTo_Taken_AndCarriedToTheCup()
        {
            var spots = new[]
            {
                new Vector3(2.8f, 0.2f, -3.8f), new Vector3(-2.8f, 0.2f, 3.8f), new Vector3(2.8f, 0.2f, 3.8f), new Vector3(-2.8f, 0.2f, -3.8f),
                new Vector3(2.1f, 0.2f, CupZ), new Vector3(CupX, 0.2f, CupZ + 0.95f), new Vector3(CupX + 0.3f, 0.2f, CupZ - 0.9f),
                new Vector3(2.8f, 0.2f, -1.2f), new Vector3(0f, 0.2f, 3.8f), new Vector3(2.85f, 0.2f, 0.4f),
            };
            foreach (float scale in new[] { 0.1f, 0.4f })
            foreach (Vector3 spot in spots)
            {
                Load();
                Prop apple = Level.Apple;
                apple.Unfreeze();
                apple.SetScale(scale);
                apple.SetPose(spot, Quaternion.identity);
                RunSeconds(1.5f);
                Vector3 lies = apple.Center;
                var drops = new List<PropHoldEvent>();
                Game.Events.PropDropped += drops.Add;
                Assert.DoesNotThrow(() => TestHelpers.PlayLevel(Game, 60f), "a marble of " + scale + " lying at " + lies);
                Assert.AreEqual(1, drops.Count, "one pick-up and one drop (the marble lay at " + lies + ")");
                Assert.AreEqual(0, Level.Leash.Returns);
            }
        }

        [Test]
        public void AnAppleBelowTheKillPlane_Respawns_SoNothingIsEverLost()
        {
            Load();
            L level = Level;
            Prop apple = level.Apple;
            apple.Unfreeze();
            apple.SetScale(0.4f);
            apple.SetPose(new Vector3(40f, Game.KillY - 1f, 0f), Quaternion.identity);
            Game.Tick();
            Assert.IsTrue(apple.Frozen, "out of the world: back on the shelf");
            Assert.AreEqual(11.4f, apple.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(apple.Center, L.AppleOrigin), 1e-3f);
            TestHelpers.PlayLevel(Game, 60f);
        }
    }
}
