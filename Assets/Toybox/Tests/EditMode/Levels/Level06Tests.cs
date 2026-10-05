using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 6, "Bouncing Eraser": size is stored bounce. The eraser has to be made big enough for its
    /// bounce to clear the cabinet: 7.5 and up (LEVELS appendix B; measured, 7.1 just makes it), 10.5 is
    /// its clamp. Held against the cabinet it is 0.59 times the distance the player stands from it.
    ///
    /// Measured tables: tools/out/notes/level06-probe-*.txt (the explicit Probe tests below write them).
    /// </summary>
    public class Level06Tests : SimTest
    {
        static readonly Vector3 Outline = Level06BouncingEraser.AimPoint;
        static readonly Vector3 Home = Level06BouncingEraser.SpoolBase + Vector3.up * 1.205f;

        Level06BouncingEraser Load()
        {
            Game = Game.Create();
            Game.LoadLevel(6);
            return (Level06BouncingEraser)Game.Level;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        // The first half of the solution with the stand point and the aim of the test's choosing. The
        // eraser is picked up from the start, or from straight in front of the spool at the given distance.
        static IEnumerator TakeAndDrop(Bot bot, Level06BouncingEraser level, Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            if (pickUpFrom > 0f)
            {
                Vector3 spool = Level06BouncingEraser.SpoolBase;
                yield return bot.WalkTo(new Vector3(spool.x, 0f, spool.z - pickUpFrom), 0.03f, 20f);
                yield return bot.LookAt(level.Eraser);
            }
            yield return bot.Grab(level.Eraser);
            if ((stand - bot.Player.Position).magnitude > 0.2f) yield return bot.WalkTo(stand, 0.15f, 20f);
            yield return bot.DropAt(aim);
        }

        static IEnumerator DropAndClimb(Bot bot, Level06BouncingEraser level, Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            yield return TakeAndDrop(bot, level, stand, aim, pickUpFrom);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        Bot Drop(Level06BouncingEraser level, Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeAndDrop(bot, level, stand, aim, pickUpFrom), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            return bot;
        }

        // Runs a script that may give up half way (a bounce that does not reach leaves the bot waiting at
        // the foot of the cabinet); says whether the level was completed.
        bool Attempt(IEnumerator script, float timeoutSeconds = 90f)
        {
            try
            {
                BotRunner.Run(Game, script, timeoutSeconds);
            }
            catch (BotException)
            {
            }
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            return Game.LevelCompleted;
        }

        // The highest the player's feet get from now on.
        Func<float> WatchApex()
        {
            float apex = float.MinValue;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            return () => apex;
        }

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(6));
            Level06BouncingEraser level = Load();
            Assert.AreEqual("bouncing-eraser", level.Slug);
            Assert.AreEqual("Bouncing Eraser", level.Title);
            Assert.AreEqual(2, level.Phase);
            Assert.AreEqual("block-hall", level.Environment);
            Assert.AreEqual(0f, level.GroundY);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            StringAssert.StartsWith("Rubber remembers how to jump.", level.Blurb, "LEVELS: the blurb");
            StringAssert.Contains("way out is up", level.Blurb, "the way out cannot be seen from the floor: the card says where it is");

            Prop eraser = level.Eraser;
            Assert.AreSame(ToyCatalog.HeroOf(6), ToyCatalog.Get(ToyId.Eraser));
            Assert.AreEqual("Eraser", eraser.Name);
            Assert.AreEqual(0.8f, eraser.Scale);
            Assert.AreEqual(0.4f, eraser.MinScale);
            Assert.AreEqual(10.5f, eraser.MaxScale);
            Assert.AreEqual(GrabPose.Snap90, eraser.GrabPose);
            Assert.IsTrue(eraser.HasTag(Level06BouncingEraser.BouncyTag));
            Assert.AreEqual(0.092f, eraser.Mass, 0.005f, "0.18 x 0.8^3");
            Assert.Less(Vector3.Distance(eraser.Center, Home), 0.02f, "on its spool, 1.2 up");
            Assert.AreEqual(1f, Vector3.Dot(eraser.Transform.right, Vector3.right), 1e-4f, "long side toward the start");
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(new Vector3(0f, 15.25f, 27f), Game.Exits[0].Position);
            Assert.AreSame(eraser, level.Pad.Prop);
            Assert.AreEqual(27.05f, level.Pad.LaunchSpeed(9.5f), 0.05f, "sqrt(2 x 22 x 1.75 x 9.5)");
            Assert.AreEqual(19f, Level06BouncingEraser.BounceTop(9.5f), 1e-3f, "the rule: twice the scale above the floor");

            // It lies on its spool and stays there; the player stands a step in front of it.
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(eraser.Center, Home), 0.02f, "the eraser should rest on the spool");
            Assert.Less(Vector3.Distance(Game.Player.Position, Level06BouncingEraser.GrabSpot), 0.05f);
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            float dropped = 0f, k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Func<float> apex = WatchApex();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(0.693f, k, 0.02f, "LEVELS: picked up from 1.15 away");
            Assert.AreEqual(Level06BouncingEraser.IntendedScale, dropped, Level06BouncingEraser.IntendedScale * 0.05f, "LEVELS appendix B: the solver's drop is 9.5 within 5 %");
            Assert.AreEqual(1, level.Pad.BounceCount, "one bounce does it");
            Assert.AreEqual(27f, level.Pad.LastLaunchSpeed, 0.4f, "LEVELS appendix B: first bounce launch speed 27.0");
            Assert.AreEqual(19f, apex(), 0.8f, "LEVELS: the top of the bounce is 19 above the floor");
            Assert.Greater(Game.Player.Position.y, Level06BouncingEraser.CabinetTop - 0.05f, "the level ends on top of the cabinet");
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.Less(Game.Time, 30f, "the solution takes well under a minute");
        }

        [Test]
        public void TheStartShowsTheToyTheCabinetAndWhereTheToyGoes()
        {
            Level06BouncingEraser level = Load();
            RunSeconds(0.2f);
            Player player = Game.Player;
            Vector3 eye = player.Eye;

            // The eraser under the crosshair: the first click is the right one.
            Assert.AreSame(level.Eraser, Game.Grabber.FindTarget(), "a click at the start picks the eraser up");
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, player.Forward, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(level.Eraser, PropRef.Of(hit.collider), "the crosshair is on the eraser");
            Assert.Less(Vector3.Distance(eye, level.Eraser.Center), 1.25f, "and it is a step away");

            // The cabinet behind it, and on its face the outline: its near corner is in the picture
            // (70 degrees up and down, 102 across), with nothing in front of it.
            float foot = ToyFactory.EraserSize.x * Level06BouncingEraser.IntendedScale * 0.5f, tall = ToyFactory.EraserSize.y * Level06BouncingEraser.IntendedScale;
            foreach (Vector3 point in new[] { Outline + new Vector3(-foot, -tall * 0.5f, 0f), Outline + new Vector3(-foot * 0.6f, tall * 0.5f, 0f), Outline + new Vector3(0f, -tall * 0.5f, 0f) })
            {
                Vector3 to = point - eye;
                float yaw = Mathf.DeltaAngle(player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - player.Pitch;
                Assert.Less(Mathf.Abs(yaw), 50f, "the outline at " + point + " is off the side of the picture");
                Assert.Less(Mathf.Abs(pitch), 34f, "the outline at " + point + " is off the top of the picture");
                Assert.IsTrue(Game.PhysicsScene.Raycast(eye, to.normalized, out hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
                Assert.Less(Vector3.Distance(hit.point, point), 0.1f, "something stands in front of the outline at " + point + " (" + hit.collider.name + ")");
            }

            // From the shoe prints the whole outline, the signs above it and the cabinet's top edge are in view.
            Vector3 standEye = Level06BouncingEraser.StandPoint + Vector3.up * Player.BaseEyeHeight;
            foreach (Vector3 point in new[]
                     {
                         Outline + new Vector3(-foot, -tall * 0.5f, 0f), Outline + new Vector3(foot, -tall * 0.5f, 0f), Outline + new Vector3(0f, tall * 0.5f, 0f),
                         new Vector3(0f, 7.3f, 18f), new Vector3(0f, 11.45f, 18f),
                     })
            {
                Vector3 to = point - standEye;
                Assert.IsTrue(Game.PhysicsScene.Raycast(standEye, to.normalized, out hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
                Assert.Less(Vector3.Distance(hit.point, point), 0.1f, "from the shoe prints " + point + " is hidden by " + hit.collider.name);
                Assert.Less(Mathf.Abs(Mathf.Atan2(to.x, to.z)) * Mathf.Rad2Deg, 50f);
            }

            // Paint, fence, books and the stand mark are looks only.
            foreach (string name in new[] { "Paint", "Stand Mark", "Room", "Books" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        [Test]
        public void WalkingAndJumpingAtTheCabinet_DoesNotCompleteTheLevel()
        {
            Level06BouncingEraser level = Load();
            Input = new ScriptedInput();
            Game.Input = Input;
            float highest = 0f, farthest = float.MinValue;

            // Sprint and jump at the cabinet's face: in the middle, into both corners, and from on top of the spool.
            foreach (Vector3 from in new[] { new Vector3(0f, 0f, 10f), new Vector3(-15f, 0f, 10f), new Vector3(15f, 0f, 10f), Level06BouncingEraser.GrabSpot })
            {
                TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
                // (A test fixture's shortcut: the player could have walked there.)
                Game.Player.Teleport(from, 0f);
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                for (int i = 0; i < TestHelpers.Ticks(5f); i++)
                {
                    if (i % 20 == 0) Input.Once.Jump = true;
                    Game.Tick();
                    highest = Mathf.Max(highest, Game.Player.Position.y);
                    farthest = Mathf.Max(farthest, Game.Player.Position.z);
                }
                Input.Hold = default;
            }
            Assert.IsFalse(Game.LevelCompleted, "the exit is on top of the cabinet");
            Assert.Less(highest, 4f, "a jump, even off the spool or the little eraser, is nowhere near 14 (highest " + highest + ")");
            Assert.Less(farthest, Level06BouncingEraser.CabinetZ, "nobody gets through the cabinet's face");
            Assert.Greater(Game.Player.Position.y, -0.1f, "and nobody falls through the floor trying");
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
        }

        // LEVELS appendix B: works from 7.5 to 10.5 (the clamp). Standing 12.7 from the cabinet gives 7.5;
        // from the back wall the eraser reaches its largest size before it reaches the cabinet and hangs
        // short of it - "too far back" is harmless.
        [TestCase(5.3f, 7.5f)]
        [TestCase(2f, 9.4f)]
        [TestCase(0f, 10.5f)]
        [TestCase(-2.2f, 10.5f)]
        public void BothEndsOfTheWindow_AndTheMiddle_ReachTheTop(float standZ, float scale)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            bool completed = Attempt(DropAndClimb(bot, level, new Vector3(0f, 0f, standZ), Outline));
            Assert.AreEqual(scale, level.Eraser.Scale, 0.15f, "the size from z = " + standZ);
            Assert.IsTrue(completed, "an eraser of " + level.Eraser.Scale + " should throw the bot onto the cabinet (top of the bounce " + apex() + ", bot at " + Game.Player.Position + ")");
            Assert.AreEqual(Level06BouncingEraser.BounceTop(level.Eraser.Scale), apex(), 0.8f, "twice the scale above the floor");
            Assert.AreEqual(1, level.Pad.BounceCount);
            Assert.IsEmpty(said);
        }

        // The intended first failure: picked up and held against the cabinet without a step back the eraser
        // is 6, and the bounce ends two units under the top. Smaller still from nearer.
        [TestCase(-8.4f, 7.9f, 6.05f)]
        [TestCase(-8.4f, 7f, 6.55f)]
        [TestCase(0f, 9f, 5.3f)]
        public void AnEraserThatIsTooSmall_DoesNotReach_AndTheLevelSaysWhy(float standX, float standZ, float scale)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            bool completed = Attempt(DropAndClimb(bot, level, new Vector3(standX, 0f, standZ), new Vector3(standX, Outline.y, 18f)));
            Assert.AreEqual(scale, level.Eraser.Scale, 0.2f, "the size from z = " + standZ);
            Assert.IsFalse(completed, "an eraser of " + level.Eraser.Scale + " must not reach the top");
            Assert.GreaterOrEqual(level.Pad.BounceCount, 1, "the bot did bounce");
            Assert.Less(apex(), Level06BouncingEraser.CabinetTop, "and came up short");
            Assert.AreEqual(Level06BouncingEraser.BounceTop(level.Eraser.Scale), apex(), 0.8f, "twice the scale above the floor");
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.LowLine }, said, "one line, once");
            RunSeconds(2f);
            Assert.Less(Game.Player.Position.y, Level06BouncingEraser.CabinetTop - 1f);

            // It is in view and can be picked up again. Picked up from a few steps away it looks big, and
            // from the shoe prints it is big.
            BotRunner.Run(Game, FixIt(bot, level), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
        }

        // After a try that did not work: off the eraser (the bot is still bouncing on it), to a spot a few
        // steps in front of it, pick it up again from there, carry it to the shoe prints, hold it against
        // the outline, let go, climb.
        static IEnumerator FixIt(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.WalkTo(new Vector3(bot.Player.Position.x, 0f, 11f), 0.4f, 20f);
            yield return bot.Until(() => bot.Player.Grounded, 5f);
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(Level06BouncingEraser.StandPoint, 0.3f, 20f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        // The gate itself, with the eraser laid against the cabinet at a size of the test's choosing (a
        // test fixture's shortcut for a perfect let-go): the bounce carries 1.75 per unit of scale.
        [TestCase(5f, false)]
        [TestCase(6.8f, false)]
        [TestCase(7.5f, true)]
        [TestCase(8.5f, true)]
        [TestCase(10.5f, true)]
        public void TheBounceGoesWithTheSize(float scale, bool reaches)
        {
            Level06BouncingEraser level = Load();
            Func<float> apex = WatchApex();
            level.Eraser.Unfreeze();
            level.Eraser.SetScale(scale);
            level.Eraser.SetPose(new Vector3(0f, 0.125f * scale + 0.01f, 18f - 0.25f * scale - 0.02f), Quaternion.identity);
            RunSeconds(0.5f);
            var bot = new Bot(Game);
            BotRunner.Run(Game, bot.WalkTo(Level06BouncingEraser.StandPoint), 20f);
            bool completed = Attempt(level.Climb(bot));
            Assert.AreEqual(1.75f * scale, level.Pad.Apex(scale), 1e-3f);
            Assert.AreEqual(Level06BouncingEraser.BounceTop(scale), apex(), 0.6f, "scale " + scale + ": the top of the bounce");
            Assert.AreEqual(reaches, completed, "scale " + scale + " (top of the bounce " + apex() + ")");
        }

        [Test]
        public void BouncingAgain_GainsNothing()
        {
            Level06BouncingEraser level = Load();
            var launches = new List<float>();
            level.Pad.Bounced += (launch, impact) => launches.Add(launch);
            level.Eraser.Unfreeze();
            level.Eraser.SetScale(6f);
            level.Eraser.SetPose(new Vector3(0f, 0.76f, 16.48f), Quaternion.identity);
            RunSeconds(0.5f);
            // (A test fixture's shortcut onto its back; then one jump and no steering.)
            Game.Player.Teleport(new Vector3(0f, 1.52f, 16.5f), 0f);
            Input = new ScriptedInput();
            Game.Input = Input;
            RunSeconds(0.3f);
            Input.Once.Jump = true;
            float highest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(12f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.GreaterOrEqual(launches.Count, 4, "the eraser keeps throwing whoever keeps landing on it");
            foreach (float launch in launches) Assert.AreEqual(launches[0], launch, 1e-3f, "every launch is the same");
            Assert.Less(highest, Level06BouncingEraser.BounceTop(6f) + 0.1f, "LEVELS: re-bouncing gains no height");
            Assert.IsFalse(Game.LevelCompleted);
        }

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Level06BouncingEraser level = Load();

            // A wrong try first - too small, at the cabinet - then the restart key.
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            Drop(level, spot, new Vector3(spot.x, Outline.y, 18f));
            Assert.Greater(level.Eraser.Scale, 5f);
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreSame(level, Game.Level, "the same level, built again");
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale, "a fresh eraser");
            Assert.Less(Vector3.Distance(level.Eraser.Center, Home), 0.02f, "on its spool");
            Assert.IsNull(Game.Grabber.Held);
            Assert.Less(Vector3.Distance(Game.Player.Position, spot), 0.05f);
            Assert.AreEqual(0, level.Pad.BounceCount);

            // The right one, and a restart in the middle of the bounce.
            var bot = new Bot(Game);
            BouncePad pad = level.Pad;
            BotRunner.Run(Game, BounceThenStop(bot, level), 60f);
            Assert.AreEqual(1, pad.BounceCount);
            Assert.Greater(Game.Player.Position.y, 5f, "in the air");
            Game.RestartLevel();
            Assert.AreNotSame(pad, level.Pad);
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            Assert.Less(Vector3.Distance(Game.Player.Position, spot), 0.05f);
            RunSeconds(1f);
            Assert.Less(Game.Player.Position.y, 0.05f, "back on the floor, standing");
            Assert.Less(Game.Player.Velocity.magnitude, 0.1f);

            // And with the eraser in hand.
            BotRunner.Run(Game, bot.Grab(level.Eraser), 10f);
            BotRunner.Run(Game, bot.WalkTo(Level06BouncingEraser.StandPoint), 20f);
            BotRunner.Run(Game, bot.LookAt(Outline), 10f);
            Assert.Greater(level.Eraser.Scale, 9f, "held over the outline it is already big");
            Game.RestartLevel();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(level.Eraser.Center, Home), 0.02f, "and it stays on its spool");

            TestHelpers.PlayLevel(Game, 90f);
        }

        static IEnumerator BounceThenStop(Bot bot, Level06BouncingEraser level)
        {
            yield return TakeAndDrop(bot, level, Level06BouncingEraser.StandPoint, Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            BouncePad pad = level.Pad;
            IEnumerator climb = level.Climb(bot);
            while (pad.BounceCount == 0 && climb.MoveNext()) yield return climb.Current;
            yield return bot.Wait(0.4f);
        }

        [Test]
        public void AnEraserThatLeavesTheWorld_ComesBack()
        {
            Level06BouncingEraser level = Load();
            Prop eraser = level.Eraser;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;

            // Below the kill plane (a test fixture's shortcut: nothing in the level lets it fall there).
            eraser.Unfreeze();
            eraser.SetScale(6f);
            eraser.SetPose(new Vector3(0f, level.KillY - 5f, 5f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns);
            Assert.Less(Vector3.Distance(eraser.Center, Home), 0.02f, "back on the spool");
            Assert.AreEqual(Level06BouncingEraser.StartScale, eraser.Scale, "at the size it started with");

            // Outside the room's walls: the leash brings it back after its two seconds.
            eraser.Unfreeze();
            eraser.SetScale(3f);
            eraser.SetPose(new Vector3(40f, 2f, 10f), Quaternion.identity);
            RunSeconds(1f);
            Assert.AreEqual(1, respawns, "not before the grace time is over");
            RunSeconds(1.5f);
            Assert.AreEqual(2, respawns);
            Assert.Less(Vector3.Distance(eraser.Center, Home), 0.02f);
            Assert.AreEqual(Level06BouncingEraser.StartScale, eraser.Scale);

            // And the level is still there to be solved.
            TestHelpers.PlayLevel(Game, 90f);
        }

        // Picked up from across the room it looks small, and thrown over the cabinet's edge it lands up
        // there small: out of sight from the floor, so the level cannot be left without its toy.
        static IEnumerator ThrowItOntoTheCabinet(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.WalkTo(new Vector3(0f, 0f, -2f), 0.2f, 20f);
            yield return bot.Grab(level.Eraser);
            yield return bot.DropAt(new Vector3(0f, 24f, 27f));
        }

        [Test]
        public void AnEraserThrownOntoTheCabinet_GoesBackToTheSpool()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            var bot = new Bot(Game);
            BotRunner.Run(Game, ThrowItOntoTheCabinet(bot, level), 40f);
            CollectionAssert.Contains(said, Level06BouncingEraser.FarLine, "picked up from 14 away it looks tiny");
            float scale = level.Eraser.Scale;
            Assert.That(scale, Is.InRange(Level06BouncingEraser.CrumbScale, 4f), "small, but no crumb");
            Vector3 last = level.Eraser.Center;
            int ticks = 0;
            for (; ticks < 600 && respawns == 0; ticks++)
            {
                last = level.Eraser.Center;
                Game.Tick();
            }
            Assert.Greater(last.y, Level06BouncingEraser.CabinetTop, "it lay on the cabinet (at " + last + ")");
            Assert.Greater(last.z, Level06BouncingEraser.CabinetZ);
            Assert.GreaterOrEqual(ticks, 100, "not before the grace time is over");
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(1, level.ShelfLeash.Returns);
            Assert.Less(Vector3.Distance(level.Eraser.Center, Home), 0.02f, "back on the spool");
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            CollectionAssert.Contains(said, Level06BouncingEraser.ShelfLine);

            // From there the level is solved in the ordinary way.
            BotRunner.Run(Game, bot.WalkTo(Level06BouncingEraser.GrabSpot, 0.05f, 20f), 25f);
            BotRunner.Run(Game, bot.LookAt(level.Eraser), 10f);
            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void AnEraserOnTheCabinet_Stays_WhileThePlayerIsUpThereToo()
        {
            Level06BouncingEraser level = Load();
            Prop eraser = level.Eraser;
            // (Test fixture's shortcuts: both put on top.)
            eraser.Unfreeze();
            eraser.SetScale(2f);
            eraser.SetPose(new Vector3(6f, 14.3f, 22f), Quaternion.identity);
            Game.Player.Teleport(new Vector3(-6f, 14f, 22f), 0f);
            RunSeconds(5f);
            Assert.AreEqual(0, level.ShelfLeash.Returns, "the player can see it: it stays");
            Assert.Greater(eraser.Center.y, 14f);

            // Off the edge and down: now nobody can see it.
            Game.Player.Teleport(new Vector3(0f, 0f, 5f), 0f);
            RunSeconds(2.5f);
            Assert.AreEqual(1, level.ShelfLeash.Returns);
            Assert.Less(Vector3.Distance(eraser.Center, Home), 0.02f);
        }

        // A let-go at the feet: near means small. Too small to pick up from close enough - it goes back to
        // the spool, where a step away is close.
        [Test]
        public void ACrumb_GoesBackToTheSpool()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            var bot = new Bot(Game);
            // Picked up from four steps away, so that it looks small; put down at the feet it is smaller than it began.
            BotRunner.Run(Game, TakeAndDrop(bot, level, new Vector3(spot.x + 3f, 0f, spot.z - 4f), new Vector3(spot.x + 3f, 0f, spot.z - 2.8f), 3.5f), 30f);
            Assert.Less(level.Eraser.Scale, Level06BouncingEraser.StartScale, "let go at the feet it is a crumb");
            RunSeconds(1f);
            Assert.AreEqual(0, respawns, "not before its grace time is over");
            RunSeconds(1f);
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(1, level.CrumbLeash.Returns);
            Assert.Less(Vector3.Distance(level.Eraser.Center, Home), 0.02f);
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.FarLine, Level06BouncingEraser.CrumbLine }, said);

            // On its spool it is smaller than a crumb too, and stays.
            RunSeconds(4f);
            Assert.AreEqual(1, respawns);
            BotRunner.Run(Game, bot.WalkTo(spot, 0.05f, 20f), 25f);
            BotRunner.Run(Game, bot.LookAt(level.Eraser), 10f);
            TestHelpers.PlayLevel(Game, 90f);
        }

        // Measured from the shoe prints: picked up from 1.5 away or nearer the eraser gets big enough from
        // there or a few steps behind; from 2 away only the back wall still works, and beyond nothing does.
        [TestCase(0.75f, false)]
        [TestCase(1.1f, false)]
        [TestCase(1.5f, false)]
        [TestCase(2f, true)]
        [TestCase(3.5f, true)]
        public void APickUpFromTooFarAway_IsCalledOut(float distance, bool tooFar)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Vector3 spool = Level06BouncingEraser.SpoolBase;
            BotRunner.Run(Game, bot.WalkTo(new Vector3(spool.x, 0f, spool.z - distance), 0.03f, 20f), 25f);
            BotRunner.Run(Game, bot.LookAt(level.Eraser), 10f);
            BotRunner.Run(Game, bot.Grab(level.Eraser), 10f);
            RunSeconds(0.1f);
            Assert.AreEqual(tooFar, Game.Grabber.Ratio < Level06BouncingEraser.LooksTooSmall, "scale / distance is " + Game.Grabber.Ratio);
            Assert.AreEqual(tooFar, said.Contains(Level06BouncingEraser.FarLine), "said: " + string.Join(" | ", said));

            // What that pick-up is worth from the back wall, where the eraser is as big as it gets.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, -2.2f), 0.15f, 20f), 25f);
            BotRunner.Run(Game, bot.DropAt(Outline), 10f);
            if (tooFar) Assert.Less(level.Eraser.Scale, Level06BouncingEraser.LeastScale, "from " + distance + " away it never gets big enough");
            else Assert.GreaterOrEqual(level.Eraser.Scale, Level06BouncingEraser.LeastScale + 1f, "from " + distance + " away there is room to spare");
        }

        // Held too low the eraser meets the floor first and stays small; held level from behind the spool
        // it comes to rest on the spool. Neither is the cabinet's doing, and stepping back would not help.
        [TestCase(0f, 2f, 0f, 0.5f)]
        [TestCase(-8.4f, 2f, -8.4f, 1.55f)]
        [TestCase(-13f, 2f, -13f, 3.5f)]
        public void StoppedShortOfTheCabinet_TheLevelSaysSo(float standX, float standZ, float aimX, float aimY)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Drop(level, new Vector3(standX, 0f, standZ), new Vector3(aimX, aimY, 18f));
            Assert.Less(level.Eraser.Scale, Level06BouncingEraser.LeastScale, "it stayed small");
            float reach = float.MinValue;
            foreach (Collider collider in level.Eraser.Colliders) reach = Mathf.Max(reach, collider.bounds.max.z);
            Assert.Less(reach, Level06BouncingEraser.CabinetZ - 1f, "short of the cabinet");
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.ShortLine }, said);

            // It is still in view and can be picked up again.
            var bot = new Bot(Game);
            BotRunner.Run(Game, bot.Grab(level.Eraser), 10f);
            Assert.AreSame(level.Eraser, Game.Grabber.Held);
        }

        // Too far back is harmless - but an eraser let go low from the back wall is big enough and lies
        // half a room from the cabinet: the bounce is high enough and cannot get across.
        [Test]
        public void BigEnoughButTooFarFromTheCabinet_TheLevelSaysSo()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            bool completed = Attempt(DropAndClimb(bot, level, new Vector3(0f, 0f, -2.2f), new Vector3(0f, 0.5f, 18f)));
            Assert.GreaterOrEqual(level.Eraser.Scale, Level06BouncingEraser.LeastScale);
            Assert.Greater(apex(), Level06BouncingEraser.CabinetTop + 0.5f, "high enough");
            Assert.IsFalse(completed, "but nine units from the cabinet");
            RunSeconds(3f);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.AwayLine }, said);
        }

        static IEnumerator BounceWithoutSteering(Bot bot, Level06BouncingEraser level)
        {
            yield return TakeAndDrop(bot, level, Level06BouncingEraser.StandPoint, Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            BouncePad pad = level.Pad;
            IEnumerator climb = level.Climb(bot);
            while (pad.BounceCount == 0 && climb.MoveNext()) yield return climb.Current;
            // Hands off the keys: straight up and straight down again, and off the eraser.
            yield return bot.Until(() => pad.BounceCount >= 2, 8f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 8f), 0.5f, 10f);
        }

        [Test]
        public void HighEnoughButNotSteered_TheLevelSaysSo_Once()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Attempt(BounceWithoutSteering(bot, level));
            RunSeconds(4f);
            Assert.IsFalse(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.MissedLine }, said);
        }

        // Round the right-hand end of the eraser that lies against the cabinet, up its slanted end and on
        // to the middle of its back - walking or sprinting, with or without a stop at the top of the slope.
        static IEnumerator OntoItsBack(Bot bot, Level06BouncingEraser level, bool sprint, bool stopAtTheCrest)
        {
            yield return TakeAndDrop(bot, level, Level06BouncingEraser.StandPoint, Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            Vector3 centre = level.Eraser.Center;
            float foot = 0.75f * level.Eraser.Scale, crest = 0.45f * level.Eraser.Scale;
            yield return bot.WalkTo(new Vector3(centre.x + foot + 2f, 0f, 11f), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(centre.x + foot + 2f, 0f, centre.z), 0.3f, 20f);
            if (stopAtTheCrest)
            {
                yield return bot.WalkTo(new Vector3(centre.x + crest - 0.1f, 0f, centre.z), 0.1f, 20f, sprint);
                yield return bot.Wait(1.5f);
            }
            yield return bot.WalkTo(new Vector3(centre.x, 0f, centre.z), 0.3f, 20f, sprint);
        }

        [Test]
        public void StandingOnTheEraser_WithoutJumping_GetsANudge_Once()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, OntoItsBack(bot, level, false, false), 60f);
            Assert.AreSame(level.Eraser, Game.Player.GroundProp, "the bot stands on the eraser's back");
            Assert.AreEqual(0.25f * level.Eraser.Scale, Game.Player.Position.y, 0.1f);
            Assert.AreEqual(0, level.Pad.BounceCount, "walking up the slanted end and onto its back is no bounce");
            RunSeconds(0.5f);
            Assert.IsEmpty(said, "not at once");
            RunSeconds(2.5f);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.StandLine }, said);
            RunSeconds(6f);
            Assert.AreEqual(1, said.Count, "once");
            Assert.AreEqual(0, level.Pad.BounceCount, "standing is no bounce either");
        }

        // The slanted ends never bounce: not at a walk, not at a sprint, not with a stop at the top.
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TheSlantedEnd_IsAWayUp_NotATrampoline(bool sprint, bool stopAtTheCrest)
        {
            Level06BouncingEraser level = Load();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            BotRunner.Run(Game, OntoItsBack(bot, level, sprint, stopAtTheCrest), 60f);
            RunSeconds(1f);
            Assert.AreEqual(0, level.Pad.BounceCount, "highest " + apex() + ", bot at " + Game.Player.Position);
            Assert.AreSame(level.Eraser, Game.Player.GroundProp);
        }

        static IEnumerator FlipAndDrop(Bot bot, Level06BouncingEraser level, int flips)
        {
            yield return bot.Grab(level.Eraser);
            yield return bot.Wait(0.3f);
            yield return bot.RotateHeld(0, flips);
            yield return bot.WalkTo(Level06BouncingEraser.StandPoint, 0.15f, 20f);
            yield return bot.DropAt(Outline);
        }

        // The flip key: on its long edge it is a wall, on its back its ends overhang. Either way the
        // slanted ends lead nowhere, and a pick-up alone does not turn it back (Snap90 keeps the face).
        [TestCase(1)]
        [TestCase(2)]
        public void LetGoOnItsEdgeOrItsBack_TheLevelSaysToFlipIt(int flips)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, FlipAndDrop(bot, level, flips), 40f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => said.Count > 0, 8f), "eraser " + level.Eraser.Scale + ", up " + level.Eraser.Transform.up);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.FlipLine }, said);
            Assert.Less(level.Eraser.Transform.up.y, 0.5f);

            // Picked up again and flipped back onto its wide side it is the trampoline again.
            BotRunner.Run(Game, FlipBack(bot, level, 4 - flips), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " up " + level.Eraser.Transform.up);
        }

        static IEnumerator FlipBack(Bot bot, Level06BouncingEraser level, int flips)
        {
            yield return bot.Grab(level.Eraser);
            yield return bot.Wait(0.3f);
            yield return bot.RotateHeld(0, flips);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        // ---- The spool (the reviewer's attacks, kept as regressions) -------------------------------------------

        static IEnumerator PutItRight(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.3f, 20f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        void AssertLiesFlatOnTheFloor(Level06BouncingEraser level, string what)
        {
            Prop eraser = level.Eraser;
            // (The end of an eraser in the left half may lie on the ruler, 0.08 thick: a third of a degree.)
            Assert.Greater(eraser.Transform.up.y, 0.9998f, what + ": flat (up " + eraser.Transform.up + ", centre " + eraser.Center + ")");
            Assert.AreEqual(0.125f * eraser.Scale, eraser.Center.y, 0.06f, what + ": on the floor, not on the spool");
        }

        // Picked up a second time from close by, the eraser reaches its largest size a few steps from the
        // eye. Let go from far back it hangs in the middle of the room, and to the left of the middle the
        // spool is under it. It used to come down propped up on the spool, its slanted ends too steep to
        // walk. Now the spool gives way: the eraser lies flat, and the spool is back when it is picked up.
        [Test]
        public void ABigEraserLetGoOverTheSpool_LiesFlat_AndTheSpoolIsBackWhenItIsPickedUp()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Assert.IsTrue(level.Spool.activeSelf);
            Assert.IsFalse(level.SpoolCovered);
            // Picked up from as close as the spool allows (it looks big: 0.96), let go from the back wall,
            // the view to the left of the middle: its largest size, hanging over the spool.
            Bot bot = Drop(level, new Vector3(-3f, 0f, -2f), new Vector3(-3f, Outline.y, 18f), 0.75f);
            Assert.AreEqual(Level06BouncingEraser.MaxScale, level.Eraser.Scale, 1e-3f);
            RunSeconds(1f);
            AssertLiesFlatOnTheFloor(level, "let go over the spool");
            Bounds bounds = level.Eraser.Colliders[0].bounds;
            Vector3 spool = Level06BouncingEraser.SpoolBase;
            Assert.IsTrue(bounds.min.x < spool.x && bounds.max.x > spool.x && bounds.min.z < spool.z && bounds.max.z > spool.z, "the eraser lies where the spool stands (" + bounds + ")");
            Assert.IsTrue(level.SpoolCovered, "the spool is under it");
            Assert.IsFalse(level.Spool.activeSelf, "neither drawn nor in the way");
            Assert.IsEmpty(said, "nothing went wrong: nothing to say");

            // Picked up again, the spool stands where it stood, and is solid.
            BotRunner.Run(Game, bot.Grab(level.Eraser), 10f);
            RunSeconds(0.1f);
            Assert.IsFalse(level.SpoolCovered);
            Assert.IsTrue(level.Spool.activeSelf, "the spool is back");
            Assert.IsTrue(Game.PhysicsScene.Raycast(spool + new Vector3(-2f, 0.5f, 0f), Vector3.right, out RaycastHit hit, 3f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.IsTrue(hit.collider.transform.IsChildOf(level.Spool.transform), "the spool stops what comes at it (hit " + hit.collider.name + ")");

            // And the level is solved from there.
            BotRunner.Run(Game, PutItRight(bot, level), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center + " up " + level.Eraser.Transform.up);
        }

        // What a player does after the first try fell short: picks the eraser up again from where they stand
        // (close: it looks big now), steps back through the left half of the room, lets go. Measured before
        // the spool gave way: one such try in four came down on the spool - crooked, or balanced on it
        // level with its ends in the air and not a word said. Each of these is one that did.
        [TestCase(-8.4f, 11f, -8.4f, 4f, false)]
        [TestCase(-8.4f, 13.5f, -8.4f, 6f, false)]
        [TestCase(-8.4f, 13.5f, -4f, 5f, false)]
        [TestCase(-5f, 12.5f, -8.4f, 2f, true)]
        [TestCase(-5f, 12.5f, 0f, -2.2f, true)]
        [TestCase(-5f, 12.5f, -4f, 0f, false)]
        public void AfterTheFirstTry_PickedUpAgainAndLetGoFromFartherBack_ItComesDownFlatAndWorks(float pickUpX, float pickUpZ, float standX, float standZ, bool atTheOutline)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            var stand = new Vector3(standX, 0f, standZ);
            Vector3 aim = atTheOutline ? Outline : new Vector3(standX, Outline.y, 18f);
            BotRunner.Run(Game, FirstTryThenAgain(bot, level, new Vector3(pickUpX, 0f, pickUpZ), stand, aim), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            Assert.GreaterOrEqual(level.Eraser.Scale, Level06BouncingEraser.LeastScale, "picked up from close it is big from anywhere");
            AssertLiesFlatOnTheFloor(level, "second try");
            CollectionAssert.DoesNotContain(said, Level06BouncingEraser.CrookedLine);
            CollectionAssert.DoesNotContain(said, Level06BouncingEraser.FlipLine);
            bool completed = Attempt(level.Climb(bot));
            Assert.IsTrue(completed, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center + ", said " + string.Join(" | ", said));
        }

        // The same, let go so far back that the bounce - high enough by three units - comes down on the
        // cabinet's top edge a hand's breadth under the top (13.80). The capsule counts as standing for one
        // tick there; the level used to take that for a landing on top and said nothing.
        [Test]
        public void ABounceThatMeetsTheCabinetsEdgeJustUnderTheTop_IsToldSo()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            BotRunner.Run(Game, FirstTryThenAgain(bot, level, new Vector3(-8.4f, 0f, 13.5f), new Vector3(-8.4f, 0f, 4f), new Vector3(-8.4f, Outline.y, 18f)), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            AssertLiesFlatOnTheFloor(level, "let go over the spool");
            Assert.IsTrue(level.SpoolCovered);
            bool completed = Attempt(level.Climb(bot));
            RunSeconds(3f);
            Assert.IsFalse(completed, "seven units from the cabinet at a walk");
            Assert.Greater(apex(), Level06BouncingEraser.CabinetTop + 2f, "high enough");
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.AwayLine }, said, "one line saying why");
            Assert.Less(Game.Player.Position.y, 1f, "back on the floor");
        }

        // The spool does not come back into anybody's legs: picked up by somebody who stands in the spool's
        // place, the eraser leaves the spool out until they have stepped away.
        static IEnumerator StandWhereTheSpoolStood(Bot bot, Level06BouncingEraser level)
        {
            Vector3 spool = Level06BouncingEraser.SpoolBase;
            yield return bot.WalkTo(new Vector3(spool.x - 3f, 0f, spool.z - 6f), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(spool.x - 3f, 0f, spool.z), 0.3f, 20f);
            yield return bot.WalkTo(spool, 0.08f, 20f);
            yield return bot.Grab(level.Eraser);
            yield return bot.Wait(0.5f);
        }

        [Test]
        public void TheSpoolComesBack_OnlyOnceNobodyStandsInItsPlace()
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            // Its largest size, let go from the back wall in the middle: the thin tip of its left end comes
            // down on the spool, most of which stands clear of it.
            BotRunner.Run(Game, FirstTryThenAgain(bot, level, new Vector3(-5f, 0f, 12.5f), new Vector3(0f, 0f, -2.2f), Outline), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            AssertLiesFlatOnTheFloor(level, "tip over the spool");
            Assert.IsTrue(level.SpoolCovered);

            BotRunner.Run(Game, StandWhereTheSpoolStood(bot, level), 60f);
            Assert.AreSame(level.Eraser, Game.Grabber.Held);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level06BouncingEraser.SpoolBase), 0.3f, "the bot stands where the spool stood");
            Assert.IsTrue(level.SpoolCovered, "not into the bot's legs");
            Assert.IsFalse(level.Spool.activeSelf);
            Assert.Less(Game.Player.Position.y, 0.05f);

            BotRunner.Run(Game, bot.WalkTo(new Vector3(-4f, 0f, 6f), 0.3f, 20f), 25f);
            Assert.IsFalse(level.SpoolCovered, "back once the bot has stepped away");
            Assert.IsTrue(level.Spool.activeSelf);

            // And the level goes on from there.
            BotRunner.Run(Game, PutItRight(bot, level), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
        }

        // Let go from high up (looking almost straight up from the start) the eraser lands hard and hops
        // once, a unit high. At the top of that hop it is still for a tick, right above the spool it has
        // just come down on: the spool must not come back under it.
        [Test]
        public void LetGoHighAboveTheSpool_TheEraserHopsOnLanding_AndStillLiesFlat()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, HoldAndLetGo(bot, level, Level06BouncingEraser.GrabSpot, 0f, 80f, 0), 40f);
            Assert.Greater(level.Eraser.Center.y, 14f, "let go fifteen units up");
            float lowest = float.MaxValue, hop = 0f;
            for (int i = 0; i < 360; i++)
            {
                Game.Tick();
                float y = level.Eraser.Center.y;
                if (y < lowest) lowest = y;
                else hop = Mathf.Max(hop, y - lowest);
            }
            Assert.Greater(hop, 0.5f, "it hopped on landing");
            AssertLiesFlatOnTheFloor(level, "after the hop");
            Assert.IsTrue(level.SpoolCovered);
            Assert.IsFalse(level.Spool.activeSelf);
            Assert.IsEmpty(said);
            bool completed = Attempt(level.Climb(bot));
            Assert.IsTrue(completed, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
        }

        // Should the eraser ever end up somewhere else than over the spool it came down on, the spool is back.
        [Test]
        public void TheSpoolIsOnlyGone_WhileTheEraserLiesWhereItStands()
        {
            Level06BouncingEraser level = Load();
            Drop(level, new Vector3(-3f, 0f, -2f), new Vector3(-3f, Outline.y, 18f), 0.75f);
            RunSeconds(1f);
            Assert.IsTrue(level.SpoolCovered);
            RunSeconds(3f);
            Assert.IsTrue(level.SpoolCovered, "it stays gone for as long as the eraser lies there");
            // (A test fixture's shortcut: nothing in the level moves an eraser of 174 units of mass.)
            level.Eraser.SetPose(new Vector3(6f, 0.125f * Level06BouncingEraser.MaxScale + 0.01f, 14f), Quaternion.identity);
            RunSeconds(1f);
            Assert.IsFalse(level.SpoolCovered);
            Assert.IsTrue(level.Spool.activeSelf);
        }

        [Test]
        public void ARestartWhileTheSpoolIsUnderTheEraser_BuildsItAgain()
        {
            Level06BouncingEraser level = Load();
            Drop(level, new Vector3(-3f, 0f, -2f), new Vector3(-3f, Outline.y, 18f), 0.75f);
            RunSeconds(1f);
            Assert.IsTrue(level.SpoolCovered);
            GameObject covered = level.Spool;
            Game.RestartLevel();
            Assert.IsFalse(level.SpoolCovered);
            Assert.AreNotSame(covered, level.Spool, "a new spool");
            Assert.IsTrue(level.Spool.activeSelf);
            RunSeconds(1f);
            Assert.Less(Vector3.Distance(level.Eraser.Center, Home), 0.02f, "the eraser lies on its spool");
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            TestHelpers.PlayLevel(Game, 90f);
        }

        // Smaller than the spool is tall the eraser still lies on it: the spool only gives way to what hides it.
        [Test]
        public void ASmallEraserOnTheSpool_StaysOnIt_AndPerchedLevelIsToldToo()
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            // Let go somewhere at a size to climb, then (a test fixture's shortcut for a pose a let-go can
            // end in) balanced level on the spool at four times its size.
            BotRunner.Run(Game, TakeAndDrop(bot, level, new Vector3(0f, 0f, 9f), new Vector3(0f, Outline.y, 18f)), 30f);
            said.Clear();
            Vector3 spool = Level06BouncingEraser.SpoolBase;
            level.Eraser.SetScale(4f);
            level.Eraser.SetPose(spool + Vector3.up * (Level06BouncingEraser.SpoolHeight + 0.5f + 0.01f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => said.Count > 0, 6f), "eraser at " + level.Eraser.Center + " up " + level.Eraser.Transform.up);
            Assert.IsFalse(level.SpoolCovered, "a slab one unit thick does not hide a spool 1.1 tall");
            Assert.IsTrue(level.Spool.activeSelf);
            Assert.Greater(level.Eraser.Center.y, Level06BouncingEraser.SpoolHeight, "it lies on the spool");
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.CrookedLine }, said);
        }

        // ---- Ways up that are not a bounce -----------------------------------------------------------------------

        // Turned to point at the cabinet and let go while looking almost straight up, the eraser at its
        // largest comes down with one end on the cabinet's face: a slope of 38 degrees, ten units high. It
        // is no way up: its low end is a wall (the slanted end stands at 77 degrees there), the top is four
        // units higher than its high end, and a slope does not bounce.
        static IEnumerator UpTheLeaningEraser(Bot bot, Level06BouncingEraser level)
        {
            Bounds bounds = level.Eraser.Colliders[0].bounds;
            float x = level.Eraser.Center.x;
            // From its low end to its high end, jumping all the way, and against the cabinet at the top.
            yield return bot.WalkTo(new Vector3(x + 4f, 0f, bounds.min.z - 1.5f), 0.4f, 20f);
            yield return bot.WalkTo(new Vector3(x, 0f, bounds.min.z - 1.5f), 0.3f, 20f);
            for (int i = 0; i < 40; i++)
            {
                IEnumerator walk = bot.WalkTo(new Vector3(x, 0f, 19f), 0.3f, 0.4f);
                bool more = true;
                while (more)
                {
                    try
                    {
                        more = walk.MoveNext();
                    }
                    catch (BotException)
                    {
                        more = false;
                    }
                    if (more) yield return walk.Current;
                }
                if (i % 2 == 1) yield return bot.Jump();
            }
        }

        [TestCase(-8.4f, 7.9f)]
        [TestCase(0f, 8f)]
        public void LeanedAgainstTheCabinet_TheEraserIsNoWayUp(float standX, float standZ)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            BotRunner.Run(Game, HoldAndLetGo(bot, level, new Vector3(standX, 0f, standZ), 0f, 80f, 6), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            float top = level.Eraser.Colliders[0].bounds.max.y;
            Assert.AreEqual(Level06BouncingEraser.MaxScale, level.Eraser.Scale, 1e-3f);
            Assert.That(level.Eraser.Transform.up.y, Is.InRange(0.6f, 0.9f), "leaning on the cabinet (centre " + level.Eraser.Center + ", top " + top + ")");
            Assert.Less(top, Level06BouncingEraser.CabinetTop - 3f, "LEVELS: 15.75 long, it would have to stand at 63 degrees to reach");
            // (From the start it was let go above the spool, and never came down on it: the spool stays.)
            Assert.IsFalse(level.SpoolCovered);
            Assert.IsTrue(level.Spool.activeSelf);
            CollectionAssert.AreEqual(new[] { Level06BouncingEraser.CrookedLine }, said);

            Attempt(UpTheLeaningEraser(bot, level), 60f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(0, level.Pad.BounceCount, "a slope does not bounce");
            Assert.Less(apex(), Level06BouncingEraser.CabinetTop - 2f, "nowhere near the top (highest " + apex() + ")");
        }

        // Looking up is far away too: let go against the cabinet's face high above the floor the eraser is
        // big without a step back, drops flat and works. (Not the intended way, but the same idea.)
        [TestCase(50f, 8.5f)]
        [TestCase(65f, 10.1f)]
        public void LookingUpFromTheStart_TheEraserIsBigToo_AndComesDownFlat(float pitch, float scale)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, HoldAndLetGo(bot, level, Level06BouncingEraser.GrabSpot, 0f, pitch, 0), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            Assert.AreEqual(scale, level.Eraser.Scale, 0.2f);
            AssertLiesFlatOnTheFloor(level, "let go " + pitch + " degrees up");
            bool completed = Attempt(level.Climb(bot));
            Assert.IsTrue(completed, "bot at " + Game.Player.Position + ", said " + string.Join(" | ", said));
            Assert.IsEmpty(said);
        }

        // ---- Soft-locks --------------------------------------------------------------------------------------------

        static IEnumerator OutFromUnderIt(Bot bot, Level06BouncingEraser level, Vector3 to)
        {
            yield return bot.WalkTo(to, 0.3f, 20f);
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(Level06BouncingEraser.StandPoint, 0.3f, 20f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        // Let go straight overhead the eraser comes down round whoever stands under it (the engine lets a
        // toy that outweighs the player pass). With the back wall behind, the player is inside it and
        // cannot see it to pick it up - but walks out of it, and goes on.
        [TestCase(0f, -2.2f, 80f, 9f, 3f)]
        [TestCase(0f, 8f, 89f, 9f, 2f)]
        public void LetGoOverhead_TheEraserComesDownRoundThePlayer_WhoWalksOutAndSolvesIt(float standX, float standZ, float pitch, float outX, float outZ)
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            float highest = 0f, fastest = 0f;
            Game.Context.OnUpdate(dt =>
            {
                if (level.Pad.BounceCount > 0) return;
                highest = Mathf.Max(highest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            });
            BotRunner.Run(Game, HoldAndLetGo(bot, level, new Vector3(standX, 0f, standZ), 0f, pitch, 0), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            Assert.AreEqual(Level06BouncingEraser.MaxScale, level.Eraser.Scale, 1e-3f);
            Bounds bounds = level.Eraser.Colliders[0].bounds;
            Assert.IsTrue(bounds.Contains(Game.Player.Position + Vector3.up * 0.8f), "the eraser lies round the bot (" + bounds + ", bot at " + Game.Player.Position + ")");
            Assert.Less(highest, 0.3f, "the bot was not lifted");
            Assert.Less(fastest, Player.SprintSpeed + 0.5f, "and not thrown");

            BotRunner.Run(Game, OutFromUnderIt(bot, level, new Vector3(outX, 0f, outZ)), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
        }

        // In the air over the eraser, picked up from above and let go again: legal (a toy may be picked up
        // in the air), and it does not break the level - whatever comes of it, the eraser can be picked up
        // again and the level solved.
        static IEnumerator PickItUpInTheAir(Bot bot, Level06BouncingEraser level)
        {
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            yield return TakeAndDrop(bot, level, spot, new Vector3(spot.x, Outline.y, 18f));
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            BouncePad pad = level.Pad;
            IEnumerator climb = level.Climb(bot);
            while (pad.BounceCount == 0 && climb.MoveNext()) yield return climb.Current;
            yield return bot.Until(() => bot.Player.Velocity.y < 6f, 3f);
            yield return bot.Grab(level.Eraser);
            yield return bot.Wait(0.2f);
            yield return bot.Click();
            yield return bot.Until(() => bot.Player.Grounded, 6f);
            yield return bot.Wait(1.5f);
        }

        [Test]
        public void PickedUpInTheAirOverIt_NothingBreaks_AndTheLevelCanStillBeSolved()
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, PickItUpInTheAir(bot, level), 90f);
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(level.Eraser.Removed);
            Assert.Greater(Game.Player.Position.y, -0.1f);
            if (Game.LevelCompleted) return;
            TestHelpers.RunUntil(Game, () => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(2f);
            // Wherever it lies now (or back on its spool): to it, pick it up from close, and the ordinary way.
            BotRunner.Run(Game, FromWhereverItLies(bot, level), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
        }

        static IEnumerator FromWhereverItLies(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.Until(() => bot.Player.Grounded, 5f);
            if (bot.Player.GroundProp == level.Eraser) yield return bot.WalkTo(new Vector3(bot.Player.Position.x, 0f, 6f), 0.4f, 20f);
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.GroundProp != level.Eraser, 5f);
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(new Vector3(0f, 0f, -2.2f), 0.3f, 25f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return bot.Wait(0.5f);
            if (level.Eraser.Scale >= Level06BouncingEraser.LeastScale)
            {
                yield return level.Climb(bot);
                yield break;
            }
            // Too small from there (picked up from far off): it is back on its spool by now, or can be put there.
            yield return bot.Wait(3f);
            yield return bot.WalkTo(Level06BouncingEraser.GrabSpot, 0.05f, 25f);
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(Level06BouncingEraser.StandPoint, 0.3f, 20f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        [Test]
        public void ARestartAfterTheLevelIsCompleted_StartsItAgain()
        {
            Level06BouncingEraser level = Load();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsTrue(Game.LevelCompleted);
            Game.RestartLevel();
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level06BouncingEraser.GrabSpot), 0.05f);
            Assert.IsTrue(level.Spool.activeSelf);
            TestHelpers.PlayLevel(Game, 90f);
        }

        // ---- Turned in the hand ------------------------------------------------------------------------------------

        // The turn key: with its long side toward the cabinet the eraser stops sooner and is smaller from
        // the same spot. From the shoe prints it is still 7.1 or more: a quarter turn or less works with
        // room to spare, more than that is on the edge - and a miss there is told.
        [TestCase(1)]
        [TestCase(-2)]
        [TestCase(3)]
        [TestCase(-4)]
        [TestCase(5)]
        [TestCase(6)]
        public void TurnedInTheHand_FromTheShoePrints_ItWorksOrTheLevelSaysWhy(int turn)
        {
            Level06BouncingEraser level = Load();
            List<string> said = Listen();
            Func<float> apex = WatchApex();
            var bot = new Bot(Game);
            Vector3 to = Outline - (Level06BouncingEraser.StandPoint + Vector3.up * Player.BaseEyeHeight);
            BotRunner.Run(Game, HoldAndLetGo(bot, level, Level06BouncingEraser.StandPoint, 0f, Mathf.Atan2(to.y, to.z) * Mathf.Rad2Deg, turn), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            RunSeconds(1f);
            Assert.GreaterOrEqual(level.Eraser.Scale, 7.05f, "turned " + turn * 15 + " degrees");
            AssertLiesFlatOnTheFloor(level, "turned " + turn * 15 + " degrees");
            bool completed = Attempt(level.Climb(bot));
            RunSeconds(2f);
            if (Mathf.Abs(turn) <= 3) Assert.IsTrue(completed, "eraser " + level.Eraser.Scale + ", top of the bounce " + apex());
            if (!completed) Assert.IsTrue(said.Contains(Level06BouncingEraser.LowLine) || said.Contains(Level06BouncingEraser.MissedLine), "a miss by a hair and not a word: " + string.Join(" | ", said));
        }

        // The solution with a player's tolerances: let go from two steps beside the shoe prints, the view
        // well off the middle of the outline.
        [TestCase(1.5f, 3f, 2f, 5f)]
        [TestCase(-2f, 0.5f, -1.5f, 2.5f)]
        [TestCase(4f, -1.5f, 3f, 4.5f)]
        [TestCase(-4f, 1f, 0f, 3.5f)]
        public void ASloppySolution_StillWorks(float standX, float standZ, float aimX, float aimY)
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            bool completed = Attempt(DropAndClimb(bot, level, new Vector3(standX, 0f, standZ), new Vector3(aimX, aimY, 18f)));
            Assert.IsTrue(completed, "bot at " + Game.Player.Position + ", eraser " + level.Eraser.Scale + " at " + level.Eraser.Center);
            Assert.GreaterOrEqual(level.Eraser.Scale, Level06BouncingEraser.LeastScale);
        }

        [Test]
        public void TheWordsOfTheLevel_ArePlainAndTheHintsEscalate()
        {
            Level06BouncingEraser level = Load();
            var lines = new List<string>();
            foreach (FieldInfo field in typeof(Level06BouncingEraser).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text.Contains(" ")) lines.Add(text);
            }
            Assert.AreEqual(11, lines.Count, "the level's lines: " + string.Join(" | ", lines));
            var all = new List<string>(lines) { level.Blurb };
            all.AddRange(level.Hints);
            foreach (string text in all)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "plain characters only in: " + text);
                Assert.IsTrue(text.EndsWith("."), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                // One verb for one act, as in the levels before: toys are picked up and let go.
                foreach (string word in new[] { "grab", "take it", "taken", "drop", "release" })
                    Assert.IsFalse(lower.Contains(word), "\"" + text + "\" says \"" + word + "\"");
            }
            foreach (string line in lines) Assert.LessOrEqual(line.Length, 95, "too long for a toast: " + line);
            Assert.LessOrEqual(level.Blurb.Length, 60);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level06BouncingEraser.FarLine, "the campaign's one sentence for a pick-up from too far away");

            string first = level.Hints[0], second = level.Hints[1], third = level.Hints[2];
            foreach (string word in new[] { "outline", "shoe prints", "backward", "middle", "bigger" })
                StringAssert.DoesNotContain(word, first, "the first hint does not give the solution away");
            StringAssert.Contains("walk backward", second);
            StringAssert.DoesNotContain("shoe prints", second, "where to stand is the third hint's");
            StringAssert.Contains("from right beside it", third, "the third hint is the whole solution, the pick-up included");
            StringAssert.Contains("shoe prints", third);
            StringAssert.Contains("dashed outline", third);
            StringAssert.Contains("let go", third);
            StringAssert.Contains("jump", third);
        }

        [Test]
        public void TheRoomIsSealed()
        {
            Load();
            RunSeconds(0.1f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            float half = Level06BouncingEraser.HalfWidth, near = Level06BouncingEraser.NearZ, top = Level06BouncingEraser.WallTop;
            // The cabinet's face from the floor to its top: no gap a capsule 0.6 wide fits.
            for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.3f)
                for (float y = 0.1f; y <= 13.91f; y += 0.3f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, 17.9f), Vector3.forward, out _, 0.5f, mask, QueryTriggerInteraction.Ignore), "a way into the cabinet at x " + x + ", y " + y);
            // The side walls and the back wall up to the sky cap, though only a low fence is drawn.
            for (float y = 0.2f; y <= top - 0.09f; y += 0.5f)
            {
                for (float z = near + 0.1f; z <= 29.91f; z += 0.5f)
                {
                    if (z > 17.9f && y < 14.1f) continue;
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.left, out _, half + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at -X, z " + z + ", y " + y);
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.right, out _, half + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at +X, z " + z + ", y " + y);
                }
                for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.5f)
                {
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, 5f), Vector3.back, out _, 5f - near + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at the back, x " + x + ", y " + y);
                    if (y > 14.1f) Assert.IsTrue(scene.Raycast(new Vector3(x, y, 20f), Vector3.forward, out _, 10.05f, mask, QueryTriggerInteraction.Ignore), "a way out behind the cabinet, x " + x + ", y " + y);
                }
            }
            // The sky cap.
            for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.5f)
                for (float z = near + 0.1f; z <= 29.91f; z += 0.5f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, top - 1f, z), Vector3.up, out _, 1.05f, mask, QueryTriggerInteraction.Ignore), "a way out at the top, x " + x + ", z " + z);
        }

        [Test]
        public void NothingButTheEraser_CanBePickedUp()
        {
            Level06BouncingEraser level = Load();
            int grabbable = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) grabbable++;
            Assert.AreEqual(1, grabbable, "the eraser is the only toy");
            var bot = new Bot(Game);
            BotRunner.Run(Game, bot.WalkTo(Level06BouncingEraser.StandPoint), 20f);
            var setPieces = new[]
            {
                Level06BouncingEraser.SpoolBase + Vector3.up * 0.2f,       // the spool
                new Vector3(-14.9f, 0.7f, -1.5f), new Vector3(14.9f, 0.6f, -1.5f),   // blocks in the back corners
                new Vector3(12f, 0.4f, -1.8f), new Vector3(-15.3f, 0.08f, 5f),       // the marker, the ruler
                new Vector3(-15.5f, 3f, 6f), new Vector3(0f, 3f, -3f),               // the fence
                new Vector3(0f, 7.3f, 18f), new Vector3(5f, 3f, 18f),                // the cabinet's signs and outline
            };
            foreach (Vector3 piece in setPieces)
            {
                BotRunner.Run(Game, LookAndClick(bot, piece), 10f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            Assert.AreEqual(Level06BouncingEraser.StartScale, level.Eraser.Scale);
        }

        static IEnumerator LookAndClick(Bot bot, Vector3 point)
        {
            yield return bot.LookAt(point);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void TheSolution_PlaysTheSameEveryTime()
        {
            const int runs = 5;
            var ticks = new int[runs];
            var rest = new Vector3[runs];
            var end = new Vector3[runs];
            for (int run = 0; run < runs; run++)
            {
                Level06BouncingEraser level = Load();
                TestHelpers.PlayLevel(Game, 90f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Eraser.Center;
                end[run] = Game.Player.Position;
                Game.Dispose();
                Game = null;
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the eraser comes to rest in the same place in run " + run);
                Assert.AreEqual(end[0], end[run], "and the bot ends in the same place in run " + run);
            }

            // And once more in a game that has already been through a wrong try and a restart.
            Level06BouncingEraser again = Load();
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            Drop(again, spot, new Vector3(spot.x, Outline.y, 18f));
            RunSeconds(1f);
            Game.RestartLevel();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(ticks[0], Game.LevelTicks, "the same after a restart");
            Assert.AreEqual(rest[0], again.Eraser.Center);
        }

        // ==== Pictures of other moments than the solver's (explicit: -Filter "Level06Tests.Tour") ================

        // The level with a script of the test's choosing in place of its Solve.
        sealed class TourLevel : LevelDefinition
        {
            public readonly Level06BouncingEraser Inner = new Level06BouncingEraser();
            readonly Func<Bot, Level06BouncingEraser, IEnumerator> script;

            public TourLevel(Func<Bot, Level06BouncingEraser, IEnumerator> script)
            {
                this.script = script;
            }

            public override string Slug => Inner.Slug;
            public override string Title => Inner.Title;
            public override string Blurb => Inner.Blurb;
            public override string[] Hints => Inner.Hints;
            public override string Environment => Inner.Environment;
            public override int EnvironmentVisit => Inner.EnvironmentVisit;
            public override float GroundY => Inner.GroundY;
            public override float KillY => Inner.KillY;
            public override void Build(LevelContext ctx) => Inner.Build(ctx);
            public override IEnumerator Solve(Bot bot) => script(bot, Inner);
        }

        static void Tour(string folder, float[] times, Func<Bot, Level06BouncingEraser, IEnumerator> script)
        {
            Shots.Run(new ShotRequest
            {
                Level = 6, Definition = new TourLevel(script), Times = times, Overview = true,
                OutputDirectory = "tools/out/shots/level06/" + folder,
            });
        }

        // Looking about from the start: up the cabinet, across to the outline, back to the shoe prints.
        static IEnumerator LookAbout(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.Wait(0.5f);
            yield return bot.LookAt(new Vector3(-6f, 9f, 18f));
            yield return bot.Wait(1f);        // 1.5: up the cabinet
            yield return bot.LookAt(new Vector3(0f, 4f, 18f));
            yield return bot.Wait(1f);        // 3: across to the outline
            yield return bot.LookAt(Level06BouncingEraser.StandPoint + Vector3.up * 0.5f);
            yield return bot.Wait(1.2f);      // 4.5: back to the shoe prints
            yield return bot.Grab(level.Eraser);
            yield return bot.LookAt(new Vector3(Level06BouncingEraser.GrabSpot.x, Outline.y, 18f));
            yield return bot.Wait(1f);        // 6.5: held from beside the spool
            yield return bot.Click();
            yield return bot.Wait(1f);        // 7.5: the first try, at the cabinet
            yield return bot.WalkTo(new Vector3(-3f, 0f, 3f), 0.3f, 20f);
            yield return bot.LookAt(new Vector3(-4f, 2f, 18f));
            yield return bot.Wait(2f);
        }

        // The first try bounced on: two units short.
        static IEnumerator FirstTry(Bot bot, Level06BouncingEraser level)
        {
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            yield return TakeAndDrop(bot, level, spot, new Vector3(spot.x, Outline.y, 18f));
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return level.Climb(bot);
        }

        // From the back wall: as big as it gets, and short of the cabinet.
        static IEnumerator FromTheBackWall(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(new Vector3(0f, 0f, -2.2f), 0.15f, 20f);
            yield return bot.LookAt(Outline);
            yield return bot.Wait(1f);
            yield return bot.DropAt(Outline);
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return bot.Wait(1f);
            yield return level.Climb(bot);
        }

        [Test, Explicit]
        public void Tour_Pictures()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("no graphics device");
            Tour("look", new[] { 1.4f, 2.9f, 4.4f, 6.4f, 7.6f, 11.5f }, LookAbout);
            Tour("first", new[] { 2f, 7f, 8.2f, 8.8f, 9.6f }, FirstTry);
            Tour("back", new[] { 4.6f, 6.5f, 12f, 13.2f, 14.4f }, FromTheBackWall);
        }

        // ==== Measurements (explicit: run with -Filter "Level06Tests.Probe") ====================================

        static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        static void WriteProbe(string name, StringBuilder text)
        {
            string path = Path.Combine(Application.dataPath, "../tools/out/notes/" + name);
            File.WriteAllText(path, text.ToString());
            Debug.Log("[Toybox] probe written: " + path);
        }

        string DropRow(Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            List<string> said = Listen();
            string result;
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            try
            {
                BotRunner.Run(Game, TakeAndDrop(bot, level, stand, aim, pickUpFrom), 40f);
                float scale = level.Eraser.Scale;
                Vector3 at = level.Eraser.Center;
                int ticks = 0;
                while (ticks < 600 && !(ticks > 10 && Level06BouncingEraser.AtRest(level.Eraser)))
                {
                    Game.Tick();
                    ticks++;
                }
                Transform t = level.Eraser.Transform;
                result = F(k) + " | " + F(scale) + " | " + F(at) + " | " + F(level.Eraser.Center) + " | " + F(t.up.y) + " | " +
                         F(Mathf.DeltaAngle(0f, t.eulerAngles.y)) + " | " + F(ticks / 60f) + " | " + string.Join(" / ", said);
            }
            catch (Exception e)
            {
                result = "FAILED " + e.Message;
            }
            Game.Dispose();
            Game = null;
            return F(stand) + " | " + F(aim) + " | " + result;
        }

        [Test, Explicit]
        public void Probe_ScaleAgainstStandPointAndAim()
        {
            var text = new StringBuilder();
            text.AppendLine("stand | aim | k | scale at the let-go | centre then | centre at rest | up.y | yaw | s to rest | said");
            float lane = Level06BouncingEraser.GrabSpot.x;
            var stands = new List<Vector3> { Level06BouncingEraser.GrabSpot };
            foreach (float z in new[] { 6.5f, 6f, 5.4f, 4f, 2f, 0f, -1.5f, -2.2f }) stands.Add(new Vector3(0f, 0f, z));
            foreach (float z in new[] { 6f, 4f, 2f, 0f, -2.2f }) stands.Add(new Vector3(lane, 0f, z));
            foreach (float z in new[] { 2f, -2.2f }) stands.Add(new Vector3(-4f, 0f, z));
            foreach (float z in new[] { 2f, -2.2f }) stands.Add(new Vector3(-2f, 0f, z));
            foreach (float z in new[] { 2f, -2.2f }) stands.Add(new Vector3(6f, 0f, z));
            foreach (float z in new[] { 2f, -1f }) stands.Add(new Vector3(10f, 0f, z));
            stands.Add(new Vector3(-13f, 0f, 2f));
            foreach (Vector3 stand in stands)
            {
                var aims = new List<Vector3> { Outline, new Vector3(0f, 1.55f, 18f), new Vector3(0f, 0.5f, 18f), new Vector3(0f, 6f, 18f) };
                if (Mathf.Abs(stand.x) > 0.5f)
                {
                    aims.Add(new Vector3(stand.x, 3.5f, 18f));
                    aims.Add(new Vector3(stand.x, 1.55f, 18f));
                }
                foreach (Vector3 aim in aims) text.AppendLine(DropRow(stand, aim));
            }
            WriteProbe("level06-probe-scale.txt", text);
        }

        [Test, Explicit]
        public void Probe_PickUpDistance()
        {
            var text = new StringBuilder();
            text.AppendLine("picked up from | stand | aim | k | scale at the let-go | centre then | centre at rest | up.y | yaw | s to rest | said");
            foreach (float from in new[] { 0.75f, 0.9f, 1.1f, 1.3f, 1.5f, 1.75f, 2f, 2.5f, 3.5f, 5f })
                foreach (float z in new[] { 5f, 2f, 0f, -2.2f })
                    text.AppendLine(F(from) + " | " + DropRow(new Vector3(0f, 0f, z), Outline, from));
            WriteProbe("level06-probe-pickup.txt", text);
        }

        [Test, Explicit]
        public void Probe_BounceAgainstStandPoint()
        {
            var text = new StringBuilder();
            text.AppendLine("stand | aim | scale | eraser centre | launch | apex y | end pos | completed | time | said | error");
            var tries = new List<KeyValuePair<Vector3, Vector3>>();
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            tries.Add(new KeyValuePair<Vector3, Vector3>(spot, new Vector3(spot.x, 3.5f, 18f)));
            tries.Add(new KeyValuePair<Vector3, Vector3>(spot, new Vector3(spot.x, 1.55f, 18f)));
            tries.Add(new KeyValuePair<Vector3, Vector3>(spot, Outline));
            foreach (float z in new[] { 7f, 6.5f, 6.2f, 6f, 5.8f, 5.4f, 5f, 4f, 2f, 0f, -1.5f, -2.2f }) tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0f, z), Outline));
            foreach (float z in new[] { 2f, -2.2f })
            {
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0f, z), new Vector3(0f, 1.55f, 18f)));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0f, z), new Vector3(0f, 0.5f, 18f)));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(spot.x, 0f, z), Outline));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(spot.x, 0f, z), new Vector3(spot.x, 3.5f, 18f)));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(spot.x, 0f, z), new Vector3(spot.x, 1.55f, 18f)));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(10f, 0f, z), Outline));
                tries.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(10f, 0f, z), new Vector3(10f, 3.5f, 18f)));
            }
            foreach (KeyValuePair<Vector3, Vector3> entry in tries)
            {
                Level06BouncingEraser level = Load();
                var bot = new Bot(Game);
                List<string> said = Listen();
                Func<float> apex = WatchApex();
                string error = "";
                try
                {
                    BotRunner.Run(Game, DropAndClimb(bot, level, entry.Key, entry.Value), 90f);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
                if (!Game.LevelCompleted) RunSeconds(3f);
                text.AppendLine(F(entry.Key) + " | " + F(entry.Value) + " | " + F(level.Eraser.Scale) + " | " + F(level.Eraser.Center) + " | " + F(level.Pad.LastLaunchSpeed) + " | " + F(apex()) + " | " +
                                F(Game.Player.Position) + " | " + Game.LevelCompleted + " | " + F(Game.Time) + " | " + string.Join(" / ", said) + " | " + error);
                Game.Dispose();
                Game = null;
            }
            WriteProbe("level06-probe-bounce.txt", text);
        }

        // ==== The reviewer's measurements (explicit: -Filter "Level06Tests.Review_Probe") =======================

        // Picks the eraser up from the start, turns it, walks to the stand point, looks along a direction
        // given in degrees and lets go.
        static IEnumerator HoldAndLetGo(Bot bot, Level06BouncingEraser level, Vector3 stand, float yaw, float pitch, int turn, int flips = 0, float pickUpFrom = 0f)
        {
            if (pickUpFrom > 0f)
            {
                Vector3 spool = Level06BouncingEraser.SpoolBase;
                yield return bot.WalkTo(new Vector3(spool.x, 0f, spool.z - pickUpFrom), 0.03f, 20f);
                yield return bot.LookAt(level.Eraser);
            }
            yield return bot.Grab(level.Eraser);
            if (turn != 0 || flips != 0)
            {
                yield return bot.Wait(0.3f);
                yield return bot.RotateHeld(turn, flips);
            }
            if ((stand - bot.Player.Position).magnitude > 0.2f) yield return bot.WalkTo(stand, 0.15f, 20f);
            Vector3 eye = bot.Player.Eye;
            yield return bot.LookAt(eye + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * 40f);
            yield return bot.Wait(0.2f);
            yield return bot.Click();
        }

        string AttackRow(Vector3 stand, float yaw, float pitch, int turn, int flips = 0, float pickUpFrom = 0f)
        {
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            List<string> said = Listen();
            float highest = float.MinValue, unbounced = float.MinValue;
            Game.Context.OnUpdate(dt =>
            {
                highest = Mathf.Max(highest, Game.Player.Position.y);
                if (level.Pad.BounceCount == 0 && Game.Player.Grounded) unbounced = Mathf.Max(unbounced, Game.Player.Position.y);
            });
            string head = F(stand) + " | " + F(yaw) + " | " + F(pitch) + " | " + turn + "/" + flips + " | ";
            string result;
            try
            {
                BotRunner.Run(Game, HoldAndLetGo(bot, level, stand, yaw, pitch, turn, flips, pickUpFrom), 40f);
                float scale = level.Eraser.Scale;
                Vector3 then = level.Eraser.Center;
                int ticks = 0;
                while (ticks < 600 && !(ticks > 10 && Level06BouncingEraser.AtRest(level.Eraser)))
                {
                    Game.Tick();
                    ticks++;
                }
                RunSeconds(0.7f);
                Transform t = level.Eraser.Transform;
                float top = float.MinValue;
                foreach (Collider collider in level.Eraser.Colliders) top = Mathf.Max(top, collider.bounds.max.y);
                result = F(scale) + " | " + F(then) + " | " + F(level.Eraser.Center) + " | " + F(t.up.y) + " | " + F(t.right) + " | " + F(top) + " | " + F(Game.Player.Position) + " | ";
                string error = "";
                try
                {
                    BotRunner.Run(Game, level.Climb(bot), 60f);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
                if (!Game.LevelCompleted) RunSeconds(3f);
                result += Game.LevelCompleted + " | " + level.Pad.BounceCount + " | " + F(highest) + " | " + F(unbounced) + " | " + F(Game.Player.Position) + " | " + string.Join(" / ", said) + " | " + error;
            }
            catch (Exception e)
            {
                result = "FAILED " + e.Message;
            }
            Game.Dispose();
            Game = null;
            return head + result;
        }

        const string AttackHead = "stand | yaw | pitch | turn/flip | scale | centre then | centre at rest | up.y | long axis | top y | player after let-go | completed | bounces | highest | highest grounded before a bounce | player end | said | error";

        [Test, Explicit]
        public void Review_Probe_LookingUp()
        {
            var text = new StringBuilder();
            text.AppendLine(AttackHead);
            var stands = new[]
            {
                Level06BouncingEraser.GrabSpot, new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 8f), new Vector3(0f, 0f, 13f), new Vector3(0f, 0f, 16.5f),
                new Vector3(0f, 0f, -2.2f), new Vector3(-8.4f, 0f, 2f), new Vector3(10f, 0f, 5f), new Vector3(-13f, 0f, 12f), new Vector3(13f, 0f, 12f),
            };
            foreach (int turn in new[] { 0, 6, 3 })
                foreach (Vector3 stand in stands)
                    foreach (float pitch in new[] { 20f, 35f, 50f, 65f, 80f, 89f })
                        text.AppendLine(AttackRow(stand, 0f, pitch, turn));
            WriteProbe("level06-review-probe-lookingup.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_Turned()
        {
            var text = new StringBuilder();
            text.AppendLine(AttackHead);
            foreach (Vector3 stand in new[] { Level06BouncingEraser.StandPoint, new Vector3(0f, 0f, -2.2f), new Vector3(0f, 0f, 5f) })
                for (int turn = -6; turn <= 6; turn++)
                    text.AppendLine(AttackRow(stand, 0f, 5.5f, turn));
            // Sideways looks: into the side walls and the corners.
            foreach (Vector3 stand in new[] { Level06BouncingEraser.StandPoint, new Vector3(0f, 0f, 10f), Level06BouncingEraser.GrabSpot })
                foreach (float yaw in new[] { -90f, -60f, -30f, 30f, 60f, 90f, 150f, 180f })
                    foreach (float pitch in new[] { 5f, 30f })
                        text.AppendLine(AttackRow(stand, yaw, pitch, 0));
            WriteProbe("level06-review-probe-turned.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_Jitter()
        {
            var text = new StringBuilder();
            text.AppendLine(AttackHead);
            int runs = 0, completed = 0;
            foreach (float x in new[] { -3f, -1.5f, 0f, 1.5f, 3f })
                foreach (float z in new[] { 0.5f, 2f, 3.5f })
                    foreach (float aimX in new[] { -4f, 0f, 4f })
                        foreach (float aimY in new[] { 2.5f, 3.5f, 4.5f })
                        {
                            var stand = new Vector3(x, 0f, z);
                            Vector3 to = new Vector3(aimX, aimY, 18f) - (stand + Vector3.up * Player.BaseEyeHeight);
                            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                            string row = AttackRow(stand, yaw, pitch, 0);
                            runs++;
                            if (row.Contains("| True |")) completed++;
                            else text.AppendLine(row);
                        }
            text.AppendLine("completed " + completed + " of " + runs + " (only the others are listed)");
            WriteProbe("level06-review-probe-jitter.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_Threshold()
        {
            var text = new StringBuilder();
            text.AppendLine(AttackHead);
            for (float z = 7f; z >= 4.45f; z -= 0.1f)
            {
                var stand = new Vector3(0f, 0f, z);
                Vector3 to = Outline - (stand + Vector3.up * Player.BaseEyeHeight);
                text.AppendLine(AttackRow(stand, 0f, Mathf.Atan2(to.y, to.z) * Mathf.Rad2Deg, 0));
            }
            WriteProbe("level06-review-probe-threshold.txt", text);
        }

        // The first try (from the start, straight at the cabinet: too small), then what a player does about
        // it: picks the eraser up again from where they stand, steps back, lets go.
        static IEnumerator FirstTryThenAgain(Bot bot, Level06BouncingEraser level, Vector3 pickUpAt, Vector3 stand, Vector3 aim)
        {
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            yield return TakeAndDrop(bot, level, spot, new Vector3(spot.x, Outline.y, 18f));
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            // (Round the spool.)
            yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, spot.z), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, 10.5f), 0.3f, 20f);
            yield return bot.WalkTo(pickUpAt, 0.2f, 20f);
            yield return bot.Grab(level.Eraser);
            if (pickUpAt.z > 9.5f && stand.z < 9.5f && Mathf.Abs(stand.x - spot.x) < 1.5f) yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, 9f), 0.3f, 20f);
            yield return bot.WalkTo(stand, 0.15f, 20f);
            yield return bot.LookAt(aim);
            yield return bot.Wait(0.2f);
            yield return bot.Click();
        }

        [Test, Explicit]
        public void Review_Probe_Recovery()
        {
            var text = new StringBuilder();
            text.AppendLine("picked up at | stand | aim | k | scale | centre at rest | up.y | top y | completed | bounces | highest | said | error");
            var pickUps = new[] { new Vector3(-8.4f, 0f, 11f), new Vector3(-8.4f, 0f, 13.5f), new Vector3(-5f, 0f, 12.5f), new Vector3(-2.5f, 0f, 14f) };
            var stands = new[]
            {
                new Vector3(-8.4f, 0f, 10f), new Vector3(-8.4f, 0f, 6f), new Vector3(-8.4f, 0f, 4f), new Vector3(-8.4f, 0f, 2f), new Vector3(-8.4f, 0f, 0f), new Vector3(-8.4f, 0f, -2.2f),
                new Vector3(-4f, 0f, 5f), new Vector3(-4f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -2.2f), new Vector3(-12f, 0f, 3f), new Vector3(5f, 0f, 3f),
            };
            foreach (Vector3 pickUp in pickUps)
                foreach (Vector3 stand in stands)
                    foreach (bool atOutline in new[] { false, true })
                    {
                        Vector3 aim = atOutline ? Outline : new Vector3(stand.x, Outline.y, 18f);
                        Level06BouncingEraser level = Load();
                        var bot = new Bot(Game);
                        List<string> said = Listen();
                        Func<float> apex = WatchApex();
                        float k = 0f;
                        Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
                        string error = "";
                        try
                        {
                            BotRunner.Run(Game, FirstTryThenAgain(bot, level, pickUp, stand, aim), 60f);
                            int ticks = 0;
                            while (ticks < 600 && !(ticks > 10 && Level06BouncingEraser.AtRest(level.Eraser)))
                            {
                                Game.Tick();
                                ticks++;
                            }
                            RunSeconds(0.7f);
                        }
                        catch (Exception e)
                        {
                            error = "LET-GO " + e.Message;
                        }
                        Transform t = level.Eraser.Transform;
                        float top = float.MinValue;
                        foreach (Collider collider in level.Eraser.Colliders) top = Mathf.Max(top, collider.bounds.max.y);
                        string row = F(pickUp) + " | " + F(stand) + " | " + F(aim) + " | " + F(k) + " | " + F(level.Eraser.Scale) + " | " + F(level.Eraser.Center) + " | " + F(t.up.y) + " | " + F(top) + " | ";
                        if (error.Length == 0)
                        {
                            try
                            {
                                BotRunner.Run(Game, level.Climb(bot), 60f);
                            }
                            catch (Exception e)
                            {
                                error = e.Message;
                            }
                            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
                            if (!Game.LevelCompleted) RunSeconds(3f);
                        }
                        text.AppendLine(row + Game.LevelCompleted + " | " + level.Pad.BounceCount + " | " + F(apex()) + " | " + string.Join(" / ", said) + " | " + error);
                        Game.Dispose();
                        Game = null;
                    }
            WriteProbe("level06-review-probe-recovery.txt", text);
        }

        // ==== The reviewer's pictures (explicit: -Filter "Level06Tests.Review_Tour") =============================

        static readonly List<float> Marks = new List<float>();

        static void Mark(Bot bot, float after = 0f) => Marks.Add(bot.Game.Time + after);

        // The second try that used to end on the spool: held over it, falling, lying flat, from the side,
        // and the bounce off it.
        static IEnumerator OverTheSpool(Bot bot, Level06BouncingEraser level)
        {
            Vector3 spot = Level06BouncingEraser.GrabSpot;
            yield return TakeAndDrop(bot, level, spot, new Vector3(spot.x, Outline.y, 18f));
            yield return bot.Until(() => Level06BouncingEraser.AtRest(level.Eraser), 8f);
            yield return bot.Wait(0.6f);
            Mark(bot);                                   // the first try, lying against the cabinet
            yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, spot.z), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, 10.5f), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(-8.4f, 0f, 13.5f), 0.2f, 20f);
            yield return bot.Grab(level.Eraser);
            yield return bot.WalkTo(new Vector3(spot.x + 1.6f, 0f, 9f), 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(-8.4f, 0f, 6f), 0.15f, 20f);
            yield return bot.LookAt(new Vector3(-8.4f, Outline.y, 18f));
            yield return bot.Wait(0.6f);
            Mark(bot, -0.1f);                            // held over the spool
            yield return bot.Click();
            Mark(bot, 0.1f);                             // falling
            Mark(bot, 0.25f);
            yield return bot.Wait(1.5f);
            Mark(bot);                                   // lying flat
            yield return bot.WalkTo(new Vector3(-1f, 0f, 5f), 0.3f, 20f);
            yield return bot.LookAt(new Vector3(-8.4f, 0.6f, 9f));
            yield return bot.Wait(0.5f);
            Mark(bot);                                   // from the side: where the spool stood
            yield return level.Climb(bot);
        }

        // Looking up from the start, and the look at the card.
        static IEnumerator LookUp(Bot bot, Level06BouncingEraser level)
        {
            yield return bot.Wait(0.3f);
            yield return bot.LookAt(new Vector3(-4f, 10f, 18f));
            yield return bot.Wait(0.4f);
            Mark(bot);
            yield return bot.Grab(level.Eraser);
            yield return bot.LookAt(Level06BouncingEraser.StandPoint + Vector3.up * 0.3f);
            yield return bot.Wait(0.4f);
            Mark(bot);                                   // turned round: the shoe prints
            yield return bot.WalkTo(Level06BouncingEraser.StandPoint, 0.2f, 20f);
            yield return bot.LookAt(Outline);
            yield return bot.Wait(0.4f);
        }

        static float[] MarksOf(Func<Bot, Level06BouncingEraser, IEnumerator> script)
        {
            Marks.Clear();
            Game game = Game.Create();
            game.LoadLevel(6);
            var bot = new Bot(game);
            try
            {
                BotRunner.Run(game, script(bot, (Level06BouncingEraser)game.Level), 120f);
            }
            catch (BotException)
            {
            }
            game.Dispose();
            float[] times = Marks.ToArray();
            for (int i = 0; i < times.Length; i++) times[i] = Mathf.Round(times[i] * 100f) / 100f;
            return times;
        }

        [Test, Explicit]
        public void Review_Tour_Pictures()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("no graphics device");
            float[] spool = MarksOf(OverTheSpool), up = MarksOf(LookUp);
            Debug.Log("[Toybox] review tour marks: spool " + string.Join(", ", spool) + " | up " + string.Join(", ", up));
            Tour("review/spool", spool, OverTheSpool);
            Tour("review/up", up, LookUp);
        }

        // Onto the spool with the eraser in hand, and let go at the feet: a crumb that goes back to the spool
        // the bot is standing on.
        static IEnumerator OntoTheSpool(Bot bot, Level06BouncingEraser level)
        {
            Vector3 spool = Level06BouncingEraser.SpoolBase;
            yield return bot.Grab(level.Eraser);
            yield return bot.LookAt(new Vector3(spool.x, 1.5f, spool.z + 6f));
            for (int i = 0; i < 4 && bot.Player.Position.y < 1f; i++)
            {
                IEnumerator walk = bot.WalkTo(spool, 0.05f, 0.25f);
                bool more = true;
                while (more)
                {
                    try
                    {
                        more = walk.MoveNext();
                    }
                    catch (BotException)
                    {
                        more = false;
                    }
                    if (more) yield return walk.Current;
                }
                yield return bot.Jump();
                for (int t = 0; t < 40; t++)
                {
                    IEnumerator step = bot.WalkTo(spool, 0.05f, 0.05f);
                    bool go = true;
                    try
                    {
                        go = step.MoveNext();
                    }
                    catch (BotException)
                    {
                        go = false;
                    }
                    if (go) yield return step.Current;
                    else yield return bot.Wait(0.017f);
                }
            }
        }

        [Test, Explicit]
        public void Review_Probe_OnTheSpool()
        {
            var text = new StringBuilder();
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            try
            {
                BotRunner.Run(Game, OntoTheSpool(bot, level), 30f);
            }
            catch (Exception e)
            {
                text.AppendLine("ERROR " + e.Message);
            }
            text.AppendLine("on the spool? player " + F(Game.Player.Position) + " grounded " + Game.Player.Grounded + " on " + (Game.Player.GroundCollider != null ? Game.Player.GroundCollider.name : "-") + " holding " + (Game.Grabber.Held != null));
            if (Game.Grabber.Held != null)
            {
                BotRunner.Run(Game, bot.LookAt(Game.Player.Position + new Vector3(0.2f, -1f, 0.6f)), 5f);
                BotRunner.Run(Game, bot.Click(), 5f);
            }
            float highest = 0f, fastest = 0f;
            for (int i = 0; i < 300; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                if (i % 20 == 0) text.AppendLine("t " + F(i / 60f) + " eraser " + F(level.Eraser.Center) + " x" + F(level.Eraser.Scale) + " frozen " + level.Eraser.Frozen + " player " + F(Game.Player.Position) + " v " + F(Game.Player.Velocity) + " respawns " + respawns);
            }
            text.AppendLine("highest " + F(highest) + " fastest " + F(fastest) + " said " + string.Join(" / ", said));
            try
            {
                BotRunner.Run(Game, bot.Grab(level.Eraser), 10f);
                text.AppendLine("picked up again: k " + F(Game.Grabber.Ratio) + " player " + F(Game.Player.Position));
            }
            catch (Exception e)
            {
                text.AppendLine("ERROR " + e.Message);
            }
            WriteProbe("level06-review-probe-onthespool.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_HighDrop()
        {
            var text = new StringBuilder();
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            List<string> said = Listen();
            BotRunner.Run(Game, HoldAndLetGo(bot, level, Level06BouncingEraser.GrabSpot, 0f, 80f, 0), 40f);
            Transform t = level.Eraser.Transform;
            for (int i = 0; i < 150; i++)
            {
                text.AppendLine("tick " + i + " centre " + F(level.Eraser.Center) + " up " + t.up.ToString("0.0000") + " right " + F(t.right) + " v " + F(level.Eraser.Velocity) + " covered " + level.SpoolCovered + " spool " + level.Spool.activeSelf);
                Game.Tick();
            }
            text.AppendLine("said " + string.Join(" / ", said));
            WriteProbe("level06-review-probe-highdrop.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_SilentMiss()
        {
            var text = new StringBuilder();
            Level06BouncingEraser level = Load();
            var bot = new Bot(Game);
            List<string> said = Listen();
            BotRunner.Run(Game, FirstTryThenAgain(bot, level, new Vector3(-8.4f, 0f, 13.5f), new Vector3(-8.4f, 0f, 4f), new Vector3(-8.4f, 3.5f, 18f)), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelTicks > 20 && Level06BouncingEraser.AtRest(level.Eraser), 8f);
            Func<string, object> field = name => typeof(Level06BouncingEraser).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(level);
            int tick = 0;
            Game.Context.OnUpdate(dt =>
            {
                tick++;
                if (tick % 10 == 0 || level.Pad.BounceCount > 0 && tick % 3 == 0)
                    text.AppendLine("t " + F(Game.Time) + " player " + F(Game.Player.Position) + " v " + F(Game.Player.Velocity) + " grounded " + Game.Player.Grounded + " on " + (Game.Player.GroundProp != null ? Game.Player.GroundProp.Name : Game.Player.GroundCollider != null ? Game.Player.GroundCollider.name : "-") +
                                    " bounces " + level.Pad.BounceCount + " inFlight " + field("inFlight") + " flightTicks " + field("flightTicks") + " missSaid " + field("missSaid") + " lowSaid " + field("lowSaid") + " said " + said.Count);
            });
            try
            {
                BotRunner.Run(Game, level.Climb(bot), 60f);
            }
            catch (Exception e)
            {
                text.AppendLine("ERROR " + e.Message);
            }
            RunSeconds(3f);
            text.AppendLine("said: " + string.Join(" / ", said));
            WriteProbe("level06-review-probe-silentmiss.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_OneRecovery()
        {
            var text = new StringBuilder();
            var cases = new[]
            {
                new[] { new Vector3(-8.4f, 0f, 11f), new Vector3(-8.4f, 0f, 4f), new Vector3(-8.4f, 3.5f, 18f) },
                new[] { new Vector3(-8.4f, 0f, 13.5f), new Vector3(-8.4f, 0f, 6f), new Vector3(-8.4f, 3.5f, 18f) },
                new[] { new Vector3(-5f, 0f, 12.5f), new Vector3(0f, 0f, -2.2f), Outline },
                new[] { new Vector3(-8.4f, 0f, 13.5f), new Vector3(-4f, 0f, 5f), new Vector3(-4f, 3.5f, 18f) },
                new[] { new Vector3(-5f, 0f, 12.5f), new Vector3(-8.4f, 0f, 2f), Outline },
            };
            foreach (Vector3[] c in cases)
            {
                Level06BouncingEraser level = Load();
                var bot = new Bot(Game);
                List<string> said = Listen();
                BotRunner.Run(Game, FirstTryThenAgain(bot, level, c[0], c[1], c[2]), 60f);
                Transform t = level.Eraser.Transform;
                text.AppendLine("pick-up " + F(c[0]) + " stand " + F(c[1]) + " aim " + F(c[2]) + ": let go at " + F(level.Eraser.Center) + " scale " + F(level.Eraser.Scale) +
                                " up " + F(t.up) + " right " + F(t.right) + " covered " + level.SpoolCovered + " player " + F(Game.Player.Position));
                for (int i = 0; i < 240; i++)
                {
                    Game.Tick();
                    if (i % 12 == 0) text.AppendLine("  t " + F(i / 60f) + " centre " + F(level.Eraser.Center) + " up " + F(t.up) + " v " + F(level.Eraser.Velocity) + " covered " + level.SpoolCovered);
                }
                Bounds bounds = level.Eraser.Colliders[0].bounds;
                text.AppendLine("  bounds " + F(bounds.min) + " .. " + F(bounds.max) + " said " + string.Join(" / ", said));
                var hits = new Collider[32];
                int n = Game.PhysicsScene.OverlapBox(bounds.center, bounds.extents + Vector3.one * 0.05f, hits, Quaternion.identity, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (PropRef.Of(hits[i]) != level.Eraser) text.AppendLine("  near: " + hits[i].name + " " + F(hits[i].bounds.min) + " .. " + F(hits[i].bounds.max));
                Game.Dispose();
                Game = null;
            }
            WriteProbe("level06-review-probe-onerecovery.txt", text);
        }

        [Test, Explicit]
        public void Review_Probe_ClosePickUp()
        {
            var text = new StringBuilder();
            text.AppendLine(AttackHead);
            foreach (float from in new[] { 0.72f, 0.9f })
                foreach (float x in new[] { 0f, -4f, -8.4f, 6f })
                    foreach (float z in new[] { 12f, 8f, 5f, 2f, 0f, -2.2f })
                    {
                        var stand = new Vector3(x, 0f, z);
                        Vector3 to = new Vector3(x, 3.5f, 18f) - (stand + Vector3.up * Player.BaseEyeHeight);
                        text.AppendLine(F(from) + " | " + AttackRow(stand, 0f, Mathf.Atan2(to.y, to.z) * Mathf.Rad2Deg, 0, 0, from));
                    }
            WriteProbe("level06-review-probe-closepickup.txt", text);
        }
    }
}
