using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 4, "Domino Effect": the domino has to be made heavy (scale 3.08 and up) and stood on the
    /// sloping board with room to fall. From the balcony edge that is a view pitch of -12 to -3 degrees
    /// (LEVELS: -10 to -3, scale 3.6 to 5.3).
    ///
    /// The second half of the file is the review's: the pick-up distance (the one thing that used to go
    /// wrong without a word), what the level says about each kind of failed try, the ways round the wall
    /// that do not exist, a domino falling on the player, and a player's tolerances.
    /// </summary>
    public class Level04Tests : SimTest
    {
        Level04DominoEffect Load()
        {
            Game = Game.Create();
            Game.LoadLevel(4);
            return (Level04DominoEffect)Game.Level;
        }

        // The first half of the solution with the release at a view pitch of the test's choosing (and the
        // domino turned in the hand, if asked): everything through input, as a player would.
        static IEnumerator PickUpAndDrop(Bot bot, Level04DominoEffect level, float pitchDegrees, int yawSteps = 0)
        {
            yield return bot.WalkTo(Level04DominoEffect.PickupSpot, 0.05f);
            yield return bot.LookAt(level.Domino);
            yield return bot.Grab(level.Domino);
            if (yawSteps != 0) yield return bot.RotateHeld(yawSteps);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f);
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            yield return bot.DropAt(bot.Player.Eye + new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * 10f);
        }

        Bot Drop(Level04DominoEffect level, float pitchDegrees, int yawSteps = 0)
        {
            var bot = new Bot(Game);
            BotRunner.Run(Game, PickUpAndDrop(bot, level, pitchDegrees, yawSteps), 30f);
            return bot;
        }

        void WalkOut(Level04DominoEffect level, Bot bot)
        {
            BotRunner.Run(Game, level.WalkOut(bot), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "the way through the arch should be open (bot at " + Game.Player.Position + ", domino at " + level.Domino.Center + ")");
        }

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(4));
            Level04DominoEffect level = Load();
            Assert.AreEqual("domino-effect", level.Slug);
            Assert.AreEqual("Domino Effect", level.Title);
            Assert.AreEqual(1, level.Phase);
            Assert.AreEqual("block-hall", level.Environment);
            Assert.AreEqual(-2.5f, level.GroundY);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.IsNotEmpty(level.Blurb);

            Prop domino = level.Domino;
            Assert.AreEqual(0.5f, domino.Scale);
            Assert.AreEqual(0.3f, domino.MinScale);
            Assert.AreEqual(6f, domino.MaxScale);
            Assert.AreEqual(GrabPose.Upright, domino.GrabPose);
            Assert.IsTrue(domino.HasTag(Level04DominoEffect.SmasherTag));
            Assert.AreEqual(0.06f, domino.Mass, 0.005f, "0.48 x 0.5^3");
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.IsFalse(level.Barricade.Broken);

            // It stands on its pedestal and stays there.
            Vector3 before = domino.Center;
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(before, domino.Center), 0.02f, "the domino should rest on the pedestal");
            Assert.AreEqual(Level04DominoEffect.SpawnPoint.y, Game.Player.Position.y, 0.05f, "the player stands on the balcony");
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Level04DominoEffect level = Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsTrue(level.Barricade.Broken);
            Assert.AreSame(level.Domino, level.Barricade.BrokenBy);
            Assert.AreEqual(Level04DominoEffect.IntendedScale, dropped, Level04DominoEffect.IntendedScale * 0.05f, "LEVELS appendix B: the solver's drop is 4.1 within 5 %");
            Assert.GreaterOrEqual(level.Barricade.BreakSpeed, 5f, "LEVELS appendix B: Broke speed >= 5");
            Assert.Less(Game.Time, 40f, "the solution takes well under a minute");
        }

        [Test]
        public void TheStartShowsTheToyTheWallAndWhereTheToyGoes()
        {
            Level04DominoEffect level = Load();
            RunSeconds(0.2f);
            Vector3 eye = Game.Player.Eye;

            // The domino on its pedestal, from the spawn.
            Vector3 toDomino = level.Domino.Center - eye;
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, toDomino.normalized, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(level.Domino, PropRef.Of(hit.collider), "the domino is in plain view from the spawn");
            Assert.Less(Vector3.Angle(Game.Player.Forward, toDomino), 25f, "and near the middle of the picture");

            // The barricade behind it.
            Vector3 toWall = new Vector3(1f, 6f, Level04DominoEffect.BoardEnd) - eye;
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, toWall.normalized, out hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.IsTrue(hit.collider.transform.IsChildOf(level.Barricade.Body.transform), "the barricade is in plain view from the spawn (hit " + hit.collider.name + ")");

            // From the balcony edge the painted footprint on the board is in view, with nothing in the way.
            Vector3 edgeEye = Level04DominoEffect.EdgeSpot + Vector3.up * Player.BaseEyeHeight;
            var footprint = new Vector3(0f, Level04DominoEffect.BoardY(Level04DominoEffect.FootprintZ), Level04DominoEffect.FootprintZ);
            Vector3 toFootprint = footprint - edgeEye;
            Assert.IsTrue(Game.PhysicsScene.Raycast(edgeEye, toFootprint.normalized, out hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.Less(Vector3.Distance(hit.point, footprint), 0.1f, "the footprint on the board is seen from the balcony edge (hit " + hit.collider.name + " at " + hit.point + ")");

            // The paint itself is looks only.
            foreach (string name in new[] { "Painted Footprint", "Stand Mark Pedestal", "Stand Mark Edge", "Tower Roofs", "Blocks -X", "Blocks +X Board" })
            {
                Transform paint = Game.LevelRoot.Find(name);
                Assert.IsNotNull(paint, name);
                Assert.AreEqual(0, paint.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(paint.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        [Test]
        public void WalkingAndJumpingAtTheWall_DoesNotCompleteTheLevel()
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, DownToTheWall(bot), 30f);
            Assert.Less(Game.Player.Position.y, -2f, "the bot stands at the foot of the board");

            // Run and jump at the barricade: in the middle and into both corners.
            float farthest = Game.Player.Position.z;
            foreach (float x in new[] { 0f, -5.4f, 5.4f })
            {
                Input = new ScriptedInput();
                Game.Input = Input;
                TestHelpers.LookAt(Game.Player, new Vector3(x, Game.Player.Eye.y, 40f));
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                for (int i = 0; i < TestHelpers.Ticks(4f); i++)
                {
                    if (i % 20 == 0) Input.Once.Jump = true;
                    Game.Tick();
                    farthest = Mathf.Max(farthest, Game.Player.Position.z);
                }
            }
            Assert.IsFalse(Game.LevelCompleted, "the exit is behind the barricade");
            Assert.IsFalse(level.Barricade.Broken, "the player cannot break it");
            Assert.Less(farthest, Level04DominoEffect.BoardEnd, "nobody gets past the face of the barricade");
            Assert.Greater(Game.Player.Position.y, -3f, "and nobody falls through the floor trying");
        }

        static IEnumerator DownToTheWall(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(6f, 3f, 9.5f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.2f));
            yield return bot.WalkTo(new Vector3(0f, 0f, 16f));
            yield return bot.WalkTo(new Vector3(0f, -2.5f, 24.2f), 0.3f, 15f);
        }

        // LEVELS, tolerances: pitch -10 gives 3.6 ("breaks, just"), -3 gives 5.3 ("breaks, just").
        [TestCase(-10f, 3.6f)]
        [TestCase(-3f, 5.3f)]
        public void BothEndsOfTheWindow_BreakThrough(float pitch, float scale)
        {
            Level04DominoEffect level = Load();
            Bot bot = Drop(level, pitch);
            Assert.AreEqual(scale, level.Domino.Scale, scale * 0.05f, "the scale LEVELS gives for a pitch of " + pitch);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f),
                "a domino of " + level.Domino.Scale + " dropped at pitch " + pitch + " should break through (it is at " + level.Domino.Center + ")");
            Assert.GreaterOrEqual(level.Barricade.BreakSpeed, Level04DominoEffect.BarricadeMinSpeed);
            WalkOut(level, bot);
        }

        // Too light on the board, standing on the flat floor, and against the wall with no room to fall.
        [TestCase(-17f)]
        [TestCase(-24f)]
        [TestCase(0f)]
        public void AWrongSizeOrPlace_DoesNotBreakThrough(float pitch)
        {
            Level04DominoEffect level = Load();
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            Drop(level, pitch);
            float scale = level.Domino.Scale;
            RunSeconds(8f);
            Assert.IsFalse(level.Barricade.Broken, "a domino of " + scale + " dropped at pitch " + pitch + " must not break the barricade");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count, "one line per try: " + string.Join(" | ", said));
            if (pitch < -20f)
            {
                Assert.Less(level.Domino.Center.z, Level04DominoEffect.BoardStart, "it came down on the flat hall floor");
                Assert.Greater(Vector3.Dot(level.Domino.Transform.up, Vector3.up), 0.99f, "where it simply stands");
                CollectionAssert.Contains(said, Level04DominoEffect.OnTheFlat, "and the level says why");
            }
            else if (pitch < -12f)
            {
                Assert.Less(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass, "too light: " + scale);
                Assert.Less(Vector3.Dot(level.Domino.Transform.up, Vector3.up), 0.5f, "it fell over on the board");
                CollectionAssert.Contains(said, Level04DominoEffect.FellShort, "short of the wall, and the level says so");
            }
            else
            {
                Assert.Greater(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass * 4f, "heavy enough several times over: " + scale);
                Assert.Greater(Vector3.Dot(level.Domino.Transform.up, Vector3.up), 0.9f, "but it only leans on the wall");
                CollectionAssert.Contains(said, Level04DominoEffect.NoRoom, "and the level says why");
            }

            // It is still in view and can be taken again.
            var bot = new Bot(Game);
            BotRunner.Run(Game, bot.Grab(level.Domino), 10f);
            Assert.AreSame(level.Domino, Game.Grabber.Held);
        }

        // The gate itself: 0.48 s^3 against a minimum mass of 14 is a scale of 3.08.
        [TestCase(3.0f, false)]
        [TestCase(3.2f, true)]
        [TestCase(4.1f, true)]
        public void TheWallGivesWayToMass(float scale, bool breaks)
        {
            Level04DominoEffect level = Load();
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            // Stood on the board a little below the footprint (a test fixture's shortcut for a perfect drop).
            const float uphillEdge = 20f;
            level.Domino.SetScale(scale);
            level.Domino.SetPose(new Vector3(0f, Level04DominoEffect.BoardY(uphillEdge) + 0.01f + scale, uphillEdge + 0.15f * scale), Quaternion.identity);
            RunSeconds(6f);
            Assert.AreEqual(breaks, level.Barricade.Broken, "scale " + scale + ", mass " + level.Domino.Mass);
            if (breaks) return;
            Assert.Less(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass);
            CollectionAssert.Contains(said, Level04DominoEffect.TooLight, "it bonked, and the level says why the wall held");
        }

        // Turns of the held domino, 15 degrees each: 30 and 45 degrees off square, and edge-on to the wall.
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(6)]
        public void ADominoHeldAskew_StillBreaksThrough_AndLeavesAWayPast(int turns)
        {
            Level04DominoEffect level = Load();
            Bot bot = Drop(level, -7.5f, turns);
            Assert.Greater(Quaternion.Angle(level.Domino.Rotation, Quaternion.identity), turns * 15f - 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f), "the askew domino is at " + level.Domino.Center);
            WalkOut(level, bot);
        }

        // The solution with a player's tolerances: the domino taken from a loose 1.2 to 1.5 away without
        // squaring up to it first (the click lands as soon as the crosshair touches it, some 10 degrees off),
        // and let go from a loosely chosen spot near the balcony edge.
        static IEnumerator SloppySolve(Bot bot, Level04DominoEffect level)
        {
            yield return bot.WalkTo(Level04DominoEffect.PickupSpot, 0.3f);
            yield return bot.Grab(level.Domino);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot + new Vector3(0.8f, 0f, -0.4f), 0.3f);
            yield return bot.DropAt(Level04DominoEffect.AimPoint);
            yield return bot.Until(() => level.Barricade.Broken, 8f);
            yield return level.WalkOut(bot);
        }

        [Test]
        public void ASloppySolution_StillWorks()
        {
            Level04DominoEffect level = Load();
            float yaw = 0f, scale = 0f;
            Game.Events.PropDropped += e =>
            {
                yaw = Mathf.DeltaAngle(0f, e.Prop.Rotation.eulerAngles.y);
                scale = e.NewScale;
            };
            var bot = new Bot(Game);
            BotRunner.Run(Game, SloppySolve(bot, level), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", domino " + level.Domino.Scale + " at " + level.Domino.Center);
            Assert.Greater(Mathf.Abs(yaw), 5f, "the domino was let go askew");
            Assert.Greater(Mathf.Abs(scale - Level04DominoEffect.IntendedScale), 0.05f, "and not at the solver's size");
        }

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Level04DominoEffect level = Load();

            // A wrong drop first, then the restart key.
            Drop(level, -17f);
            RunSeconds(2f);
            Assert.Greater(level.Domino.Scale, 2f);
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreSame(level, Game.Level, "the same level, built again");
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, level.Domino.Scale, "a fresh domino");
            Assert.Less(Vector3.Distance(level.Domino.Center, new Vector3(-3f, 4.4f, 8f)), 0.05f, "on its pedestal");
            Assert.IsNull(Game.Grabber.Held);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level04DominoEffect.SpawnPoint), 0.05f);

            // Break the wall, then restart again while the domino is still falling through the arch.
            Drop(level, -7.5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f));
            Breakable broken = level.Barricade;
            Game.RestartLevel();
            Assert.AreNotSame(broken, level.Barricade);
            Assert.IsFalse(level.Barricade.Broken, "the barricade stands again");
            Assert.IsTrue(level.Barricade.Body.GetComponentInChildren<Collider>().enabled);

            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsTrue(level.Barricade.Broken);
        }

        [Test]
        public void ADominoThatLeavesTheWorld_ComesBack()
        {
            Level04DominoEffect level = Load();
            Prop domino = level.Domino;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            var pedestal = new Vector3(-3f, 4.4f, 8f);

            // Below the kill plane (a test fixture's shortcut: nothing in the level lets it fall there).
            domino.SetScale(3f);
            domino.SetPose(new Vector3(0f, level.KillY - 5f, 0f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns);
            Assert.Less(Vector3.Distance(domino.Center, pedestal), 0.05f, "back on the pedestal");
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, domino.Scale, "at the size it started with");

            // Outside the hall's walls: the leash brings it back after its two seconds.
            domino.SetScale(2f);
            domino.SetPose(new Vector3(20f, 0f, 10f), Quaternion.identity);
            RunSeconds(1f);
            Assert.AreEqual(1, respawns, "not before the grace time is over");
            RunSeconds(1.5f);
            Assert.AreEqual(2, respawns);
            Assert.Less(Vector3.Distance(domino.Center, pedestal), 0.05f);
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, domino.Scale);

            // And the level is still there to be solved.
            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void TheBalconyCanBeRegained_ByTheTwoBlocks()
        {
            Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, DownAndUpAgain(bot), 40f);
            RunSeconds(0.5f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(Level04DominoEffect.BalconyTop, Game.Player.Position.y, 0.05f, "back on the balcony (at " + Game.Player.Position + ")");
            Assert.Less(Game.Player.Position.z, Level04DominoEffect.BalconyEdge);
        }

        [Test]
        public void TheSolution_PlaysTheSameEveryTime()
        {
            const int runs = 5;
            var ticks = new int[runs];
            var rest = new Vector3[runs];
            var speed = new float[runs];
            for (int run = 0; run < runs; run++)
            {
                Level04DominoEffect level = Load();
                TestHelpers.PlayLevel(Game, 90f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Domino.Center;
                speed[run] = level.Barricade.BreakSpeed;
                Game.Dispose();
                Game = null;
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the domino comes to rest in the same place in run " + run);
                Assert.AreEqual(speed[0], speed[run], "after the same hit in run " + run);
            }

            // And once more in a game that has already been through a wrong try and a restart.
            Level04DominoEffect again = Load();
            Drop(again, -17f);
            RunSeconds(3f);
            Game.RestartLevel();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(Level04DominoEffect.IntendedScale, dropped, 0.05f, "the same drop after a restart");
            Assert.AreEqual(speed[0], again.Barricade.BreakSpeed, 0.5f, "and the same hit");
        }

        static IEnumerator DownAndUpAgain(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(6f, 3f, 9.5f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.5f));
            yield return UpTheBlocks(bot);
        }

        // From the hall floor at the foot of the low block back onto the balcony.
        static IEnumerator UpTheBlocks(Bot bot)
        {
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y < 0.05f, 3f);
            // Three hops of 1.0: the low block (its face at z = 13), the high block (11.5), the balcony (10).
            float[] face = { 13f, 11.5f, 10f }, landing = { 12.25f, 10.75f, 9f };
            for (int i = 0; i < face.Length; i++)
            {
                float height = bot.Player.Position.y;
                yield return bot.WalkTo(new Vector3(6f, 0f, face[i] + 0.42f), 0.08f);
                yield return bot.LookAt(new Vector3(6f, bot.Player.Eye.y, landing[i]));
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(6f, 0f, landing[i]), 0.25f, 3f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                if (bot.Player.Position.y < height + 0.9f) yield break;
            }
        }

        // ==== Review: attacks, soft-locks and tolerances =======================================================

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        // In front of the pedestal, that far from the domino, squared up to it. 11: from the spawn, where the level starts.
        static IEnumerator TakeFrom(Bot bot, Level04DominoEffect level, float distance)
        {
            if (distance < 10f) yield return bot.WalkTo(new Vector3(-3f, 3f, 8f - distance), 0.03f);
            yield return bot.LookAt(level.Domino);
            yield return bot.Grab(level.Domino);
        }

        // The view that stands the held domino with its foot at (x, z) of the board: its centre is one scale
        // above the foot, and its scale is the held ratio times the distance from the eye to that centre.
        static Vector3 AimForFoot(Bot bot, float x, float z)
        {
            Vector3 eye = bot.Player.Eye;
            float k = bot.Game.Grabber.Ratio;
            var foot = new Vector3(x, Level04DominoEffect.BoardY(z), z);
            float s = 1f;
            for (int i = 0; i < 12; i++) s = k * Vector3.Distance(eye, foot + Vector3.up * s);
            return foot + Vector3.up * s;
        }

        static IEnumerator DropOnFoot(Bot bot, float x, float z)
        {
            yield return bot.DropAt(AimForFoot(bot, x, z));
        }

        static IEnumerator DropAtPitch(Bot bot, float pitchDegrees, float heading = 1f)
        {
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            yield return bot.DropAt(bot.Player.Eye + new Vector3(0f, Mathf.Sin(pitch), heading * Mathf.Cos(pitch)) * 10f);
        }

        // How much of the arch is left free beside the domino that lies in it: the wider side.
        static float FreeBeside(Level04DominoEffect level)
        {
            float left = Level04DominoEffect.ArchHalfWidth, right = Level04DominoEffect.ArchHalfWidth;
            foreach (Collider collider in level.Domino.Colliders)
            {
                Bounds b = collider.bounds;
                if (b.max.z < Level04DominoEffect.BoardEnd - 1.5f || b.min.z > Level04DominoEffect.BoardEnd + Level04DominoEffect.ArchDepth + 1.5f) continue;
                left = Mathf.Min(left, b.min.x + Level04DominoEffect.ArchHalfWidth);
                right = Mathf.Min(right, Level04DominoEffect.ArchHalfWidth - b.max.x);
            }
            return Mathf.Max(left, right);
        }

        static readonly Vector3 Pedestal = new Vector3(-3f, 4.4f, 8f);

        // ---- The pick-up: the one way a try used to go quietly wrong -------------------------------------------

        // Measured from the balcony edge: taken from 1.3 or nearer the whole footprint works, from 1.45 only its
        // far end, from 2.5 nothing, and from the spawn the domino never passes 0.73. The level says so on the grab.
        [TestCase(0.8f, false)]
        [TestCase(1.25f, false)]
        [TestCase(1.6f, true)]
        [TestCase(2.5f, true)]
        [TestCase(11f, true)]
        public void ADominoTakenFromTooFarAway_IsCalledOut(float distance, bool tooFar)
        {
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeFrom(bot, level, distance), 30f);
            RunSeconds(0.1f);
            Assert.AreEqual(tooFar, Game.Grabber.Ratio < Level04DominoEffect.LooksTooSmall, "scale / distance is " + Game.Grabber.Ratio);
            Assert.AreEqual(tooFar, said.Contains(Level04DominoEffect.SmallLine), "said: " + string.Join(" | ", said));
        }

        static IEnumerator FromTheSpawnOntoTheFootprint(Bot bot, Level04DominoEffect level)
        {
            yield return TakeFrom(bot, level, 11f);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f);
            yield return DropOnFoot(bot, 0f, Level04DominoEffect.FootprintZ);
        }

        [Test]
        public void AFarPickUp_StoodOnTheFootprint_IsACrumb_AndGoesBackToThePedestal()
        {
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            var bot = new Bot(Game);
            BotRunner.Run(Game, FromTheSpawnOntoTheFootprint(bot, level), 30f);
            Assert.Less(level.Domino.Scale, Level04DominoEffect.CrumbScale, "taken from the spawn the domino looks tiny, and on the footprint it is tiny");
            Assert.Greater(level.Domino.Center.z, Level04DominoEffect.BoardStart, "it is down on the board");
            RunSeconds(1f);
            Assert.AreEqual(0, respawns, "not before its grace time is over");
            RunSeconds(1.5f);
            Assert.AreEqual(1, respawns, "a crumb goes back");
            Assert.AreEqual(1, level.Crumbs.Returns);
            Assert.Less(Vector3.Distance(level.Domino.Center, Pedestal), 0.05f, "to the pedestal");
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, level.Domino.Scale);
            CollectionAssert.Contains(said, Level04DominoEffect.SmallLine);
            CollectionAssert.Contains(said, Level04DominoEffect.CrumbLine);
            Assert.IsFalse(level.Barricade.Broken);

            // On its pedestal it is smaller than a crumb too, and stays: that is where it can be taken from beside.
            RunSeconds(4f);
            Assert.AreEqual(1, respawns);

            // From there the level is solved in the ordinary way.
            BotRunner.Run(Game, bot.WalkTo(Level04DominoEffect.SpawnPoint), 20f);
            TestHelpers.PlayLevel(Game, 90f);
        }

        static IEnumerator FromTheSpawnAgainstTheWall(Bot bot, Level04DominoEffect level)
        {
            yield return TakeFrom(bot, level, 11f);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f);
            yield return DropAtPitch(bot, -8f);
        }

        // Down the blocks and the board to the little domino at the wall, take it from a step away, carry it
        // back up to the balcony edge and stand it on the footprint.
        static IEnumerator FetchItAndSolve(Bot bot, Level04DominoEffect level)
        {
            yield return bot.WalkTo(new Vector3(6f, 3f, 9.5f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.2f));
            yield return bot.WalkTo(new Vector3(level.Domino.Center.x, 0f, 16f));
            yield return bot.WalkTo(new Vector3(level.Domino.Center.x, -2.5f, level.Domino.Center.z - 1.1f), 0.05f, 15f);
            yield return bot.LookAt(level.Domino);
            yield return bot.Grab(level.Domino);
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.5f), 0.3f, 15f);
            yield return UpTheBlocks(bot);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f);
            yield return DropOnFoot(bot, 0f, Level04DominoEffect.FootprintZ);
            yield return bot.Until(() => level.Barricade.Broken, 8f);
            yield return level.WalkOut(bot);
        }

        [Test]
        public void AFarPickUp_LetGoAgainstTheWall_CanBeFetchedAndUsed_WithoutARestart()
        {
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, FromTheSpawnAgainstTheWall(bot, level), 30f);
            RunSeconds(4f);
            Assert.IsFalse(level.Barricade.Broken);
            Assert.That(level.Domino.Scale, Is.InRange(Level04DominoEffect.CrumbScale, 0.8f), "all the way down the hall it is still small");
            Assert.Greater(level.Domino.Center.z, 24f, "it leans on the wall");
            CollectionAssert.Contains(said, Level04DominoEffect.SmallLine);
            CollectionAssert.Contains(said, Level04DominoEffect.TooLight);
            said.Clear();

            BotRunner.Run(Game, FetchItAndSolve(bot, level), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position + ", domino " + level.Domino.Scale + " at " + level.Domino.Center);
            CollectionAssert.DoesNotContain(said, Level04DominoEffect.SmallLine, "taken from a step away it looks big enough");
        }

        // ---- What the level says about a try that fails --------------------------------------------------------

        static IEnumerator LayItFlatOnTheBoard(Bot bot, Level04DominoEffect level)
        {
            yield return TakeFrom(bot, level, 1.1f);
            yield return bot.RotateHeld(0, 1);
            yield return bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f);
            yield return DropOnFoot(bot, 0f, Level04DominoEffect.FootprintZ);
        }

        static IEnumerator TakeItAgainAndStandIt(Bot bot, Level04DominoEffect level)
        {
            yield return bot.Grab(level.Domino);
            yield return bot.Wait(0.3f);
            yield return DropOnFoot(bot, 0f, Level04DominoEffect.FootprintZ);
        }

        [Test]
        public void LaidFlatOnTheBoard_ItHasNowhereToFall_AndTakenAgainItStandsUp()
        {
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, LayItFlatOnTheBoard(bot, level), 30f);
            RunSeconds(6f);
            Assert.IsFalse(level.Barricade.Broken, "a slab lying on the board");
            Assert.Less(Mathf.Abs(level.Domino.Transform.up.y), 0.5f);
            Assert.Greater(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass);
            CollectionAssert.Contains(said, Level04DominoEffect.LyingFlat);

            // GrabPose.Upright: in the hand it is on its foot again.
            BotRunner.Run(Game, TakeItAgainAndStandIt(bot, level), 30f);
            Assert.Greater(level.Domino.Transform.up.y, 0.99f, "it was let go upright");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f), "domino " + level.Domino.Scale + " at " + level.Domino.Center);
            WalkOut(level, bot);
        }

        [Test]
        public void HeavyEnoughButShort_TheLevelSaysToStandItFartherDown()
        {
            // Taken from as near as the pedestal allows and let go low: 3.8 across, but 9.8 from the wall.
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeFrom(bot, level, 0.72f), 30f);
            BotRunner.Run(Game, bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f), 30f);
            BotRunner.Run(Game, DropAtPitch(bot, -7f), 30f);
            RunSeconds(8f);
            Assert.IsFalse(level.Barricade.Broken);
            Assert.Greater(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass);
            CollectionAssert.AreEqual(new[] { Level04DominoEffect.FellShort }, said);
        }

        [Test]
        public void TheWordsOfTheLevel_ArePlainAndTheHintsEscalate()
        {
            Level04DominoEffect level = Load();
            var lines = new List<string>(level.Hints)
            {
                level.Blurb, Level04DominoEffect.SmallLine, Level04DominoEffect.CrumbLine, Level04DominoEffect.TooLight, Level04DominoEffect.NoRoom,
                Level04DominoEffect.FellShort, Level04DominoEffect.OnTheFlat, Level04DominoEffect.LyingFlat,
            };
            foreach (string line in lines)
            {
                // The UI font has no em dash: it drew the one in the first hint as two hyphens.
                foreach (char c in line) Assert.Less((int)c, 127, "plain characters only in: " + line);
                Assert.LessOrEqual(line.Length, 150, line);
            }
            Assert.LessOrEqual(level.Blurb.Length, 60);
            string first = level.Hints[0], second = level.Hints[1], third = level.Hints[2];
            StringAssert.DoesNotContain("footprint", first, "the first hint does not give the solution away");
            StringAssert.DoesNotContain("board", first);
            StringAssert.DoesNotContain("balcony", first);
            StringAssert.Contains("tilted board", second);
            StringAssert.Contains("footprint", second);
            StringAssert.DoesNotContain("balcony", second, "where to stand is the third hint's");
            StringAssert.Contains("right beside", third, "the third hint is the whole solution, the pick-up included");
            StringAssert.Contains("balcony edge", third);
            StringAssert.Contains("footprint", third);
        }

        // ---- No way round --------------------------------------------------------------------------------------

        [Test]
        public void TheHallIsSealed_WhileTheBarricadeStands()
        {
            Load();
            RunSeconds(0.1f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.SolidMask;
            // The arch wall with the barricade in it, from the floor to the sky cap: no gap a capsule 0.6 wide fits.
            for (float x = -6.9f; x <= 6.91f; x += 0.3f)
                for (float y = -2.4f; y <= 13.91f; y += 0.3f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, 24.9f), Vector3.forward, out _, 0.5f, mask, QueryTriggerInteraction.Ignore),
                        "a way through the arch wall at x " + x + ", y " + y);
            // The side walls, although only their lower rows of blocks are drawn.
            for (float z = -5.9f; z <= 24.91f; z += 0.4f)
                for (float y = -2.4f; y <= 13.91f; y += 0.4f)
                {
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.left, out _, 7.05f, mask, QueryTriggerInteraction.Ignore), "a way out at -X, z " + z + ", y " + y);
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.right, out _, 7.05f, mask, QueryTriggerInteraction.Ignore), "a way out at +X, z " + z + ", y " + y);
                }
            // The back wall and the sky cap.
            for (float x = -6.9f; x <= 6.91f; x += 0.4f)
            {
                for (float y = 3.1f; y <= 13.91f; y += 0.4f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, 0f), Vector3.back, out _, 6.05f, mask, QueryTriggerInteraction.Ignore), "a way out at the back, x " + x + ", y " + y);
                for (float z = -5.9f; z <= 24.91f; z += 0.4f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, 13f, z), Vector3.up, out _, 1.05f, mask, QueryTriggerInteraction.Ignore), "a way out at the top, x " + x + ", z " + z);
            }
        }

        static IEnumerator LookAndClick(Bot bot, Vector3 point)
        {
            yield return bot.LookAt(point);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void NothingButTheDomino_CanBePickedUp()
        {
            Level04DominoEffect level = Load();
            int grabbable = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) grabbable++;
            Assert.AreEqual(1, grabbable, "the domino is the only toy");

            // A click on every set piece, from the spawn and from the balcony edge.
            var setPieces = new[]
            {
                new Vector3(-5.9f, 3.9f, 4.6f), new Vector3(-5.9f, 3.5f, 1.7f),     // the wooden dominoes
                new Vector3(5.5f, 3.7f, 5.2f), new Vector3(5.45f, 4.85f, 5.15f),    // the block pile
                new Vector3(-3f, 3.2f, 7.6f),                                       // the pedestal
                new Vector3(2f, 5f, 25f), new Vector3(0f, 0.5f, 25f),               // the barricade and its peephole
                new Vector3(6f, 1.9f, 10.5f), new Vector3(-5.9f, 0.6f, 11.3f),      // the stair blocks, the blocks in the hall
                new Vector3(-6.75f, 14.5f, 25.75f), new Vector3(0f, 0f, 18.5f),     // a tower roof, the painted footprint
            };
            var bot = new Bot(Game);
            foreach (Vector3 from in new[] { Level04DominoEffect.SpawnPoint, Level04DominoEffect.EdgeSpot })
            {
                BotRunner.Run(Game, bot.WalkTo(from, 0.2f), 20f);
                foreach (Vector3 piece in setPieces)
                {
                    BotRunner.Run(Game, LookAndClick(bot, piece), 10f);
                    Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " from " + from + " picked something up");
                }
            }
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, level.Domino.Scale);
        }

        // ---- Nothing crushes the player ------------------------------------------------------------------------

        // The other way to do it: carry the domino down, stand at the foot of the wall and stand it on the
        // board above. It topples toward the wall - and onto whoever stands there.
        static IEnumerator StandItUphillFromTheFootOfTheWall(Bot bot, Level04DominoEffect level, float pitchDegrees)
        {
            yield return TakeFrom(bot, level, 1.1f);
            yield return bot.WalkTo(new Vector3(6f, 3f, 9.5f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.2f));
            yield return bot.WalkTo(new Vector3(0f, 0f, 15.5f));
            yield return bot.WalkTo(new Vector3(0f, -2.5f, 24.3f), 0.1f, 15f);
            yield return DropAtPitch(bot, pitchDegrees, -1f);
        }

        [TestCase(55f)]
        [TestCase(60f)]
        public void ADominoThatFallsOnThePlayer_PassesThroughThem_AndTheWayIsOpen(float pitch)
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, StandItUphillFromTheFootOfTheWall(bot, level, pitch), 60f);
            Assert.Greater(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass * 3f, "a domino of " + level.Domino.Scale);
            Vector3 stood = Game.Player.Position;
            float fastest = 0f, lowest = float.MaxValue;
            for (int i = 0; i < TestHelpers.Ticks(6f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
            }
            Assert.IsTrue(level.Barricade.Broken, "it fell over the player onto the wall");
            // Before the review this flung the player through the arch at 35 to 50 units a second.
            Assert.Less(fastest, 2f, "the player was not thrown");
            Assert.Greater(lowest, stood.y - 0.1f, "nor pressed into the board");
            Assert.Less(Vector3.Distance(stood, Game.Player.Position), 0.5f);

            // They stand inside the fallen domino now, and simply walk out of it and on to the exit.
            Assert.IsTrue(TouchesNothing(level), "the domino lies round the player and does not touch them");
            BotRunner.Run(Game, bot.WalkTo(Level04DominoEffect.ExitCenter, 0.5f, 15f), 20f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "bot at " + Game.Player.Position);
            Assert.IsFalse(TouchesNothing(level), "once they have stepped out of it, it is solid again");
        }

        // Is the domino set to pass through the player just now?
        bool TouchesNothing(Level04DominoEffect level)
        {
            foreach (Collider collider in level.Domino.Colliders)
                if (!Physics.GetIgnoreCollision(collider, Game.Player.Collider)) return false;
            return true;
        }

        // A domino that is too light for the wall, toppling onto a player who stands against it: lighter than
        // the player it bumps them, heavier it passes through them. Neither pushes anybody through the wall.
        [TestCase(1.8f, 0f)]
        [TestCase(2.9f, 0f)]
        [TestCase(2.9f, 4.6f)]
        [TestCase(2.9f, -4.6f)]
        public void ALightDominoFallingOnThePlayerAtTheWall_DoesNotPushThemThrough(float scale, float x)
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, DownToTheWall(bot), 30f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(x, -2.5f, 24.6f), 0.1f, 10f), 15f);
            // Stood on the board so that its tip comes down at the foot of the wall (a test fixture's shortcut).
            float uphillEdge = Level04DominoEffect.BoardEnd - 0.2f - 2f * scale - 0.3f * scale;
            level.Domino.SetScale(scale);
            level.Domino.SetPose(new Vector3(x, Level04DominoEffect.BoardY(uphillEdge) + 0.01f + scale, uphillEdge + 0.15f * scale), Quaternion.identity);
            float farthest = 0f, fastest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(6f); i++)
            {
                Game.Tick();
                farthest = Mathf.Max(farthest, Game.Player.Position.z);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            }
            Assert.Less(level.Domino.Transform.up.y, 0.5f, "it fell over");
            Assert.IsFalse(level.Barricade.Broken, "too light: " + level.Domino.Mass);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthest, Level04DominoEffect.BoardEnd - 0.25f, "the player stays on this side of the wall");
            Assert.Less(fastest, 9f, "and is not thrown about");
            Assert.Greater(Game.Player.Position.y, -3f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, -1f, 19f), 0.4f, 10f), 15f);
        }

        // ---- A player's tolerances -----------------------------------------------------------------------------

        static IEnumerator TakeStandAndPlace(Bot bot, Level04DominoEffect level, float distance, Vector2 stand, Vector2 foot)
        {
            yield return TakeFrom(bot, level, distance);
            yield return bot.WalkTo(new Vector3(stand.x, 3f, 6.3f), 0.15f);
            yield return bot.WalkTo(new Vector3(stand.x, 3f, stand.y), 0.1f);
            yield return DropOnFoot(bot, foot.x, Level04DominoEffect.FootprintZ + foot.y);
        }

        // Taken from anywhere "right beside it" (0.72, as near as the pedestal allows, to 1.25 away), let go from
        // anywhere near the balcony edge, with its foot anywhere on the painted footprint: the wall comes down,
        // and there is a way past. (From 0.72 the domino reaches its largest size, 6, before it gets there.)
        [Test]
        public void TakenFromBeside_FromAnywhereNearTheEdge_AFootOnTheFootprint_BreaksThrough()
        {
            var failures = new List<string>();
            int tries = 0;
            foreach (float distance in new[] { 0.72f, 0.8f, 1.1f, 1.25f })
                foreach (Vector2 stand in new[] { new Vector2(-1.5f, 9.6f), new Vector2(0f, 8.6f), new Vector2(2.5f, 9.6f), new Vector2(5.5f, 9.6f), new Vector2(-5.5f, 8.6f) })
                    foreach (Vector2 foot in new[] { new Vector2(0f, 0f), new Vector2(-1f, -0.6f), new Vector2(1f, 0.6f) })
                    {
                        Level04DominoEffect level = Load();
                        var bot = new Bot(Game);
                        BotRunner.Run(Game, TakeStandAndPlace(bot, level, distance, stand, foot), 40f);
                        float scale = level.Domino.Scale;
                        string what = "taken from " + distance + ", stood at " + stand + ", foot at " + foot + ", scale " + scale.ToString("0.00");
                        tries++;
                        if (!TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f)) failures.Add(what + ": the wall held");
                        else
                        {
                            RunSeconds(5f);
                            float free = FreeBeside(level);
                            if (free < 1.2f) failures.Add(what + ": only " + free.ToString("0.0") + " free beside the fallen domino");
                        }
                        Game.Dispose();
                        Game = null;
                    }
            Assert.IsEmpty(failures, failures.Count + " of " + tries + ":\n" + string.Join("\n", failures));
        }

        // ---- Getting about -------------------------------------------------------------------------------------

        static IEnumerator StepOffTheBalcony(Bot bot, float x)
        {
            yield return bot.WalkTo(new Vector3(x, 3f, 9.3f), 0.15f);
            yield return bot.WalkTo(new Vector3(x, 0f, 11.2f), 0.4f, 4f);
            yield return bot.Until(() => bot.Player.Grounded, 3f);
            yield return bot.Wait(0.3f);
        }

        // Off the balcony edge everywhere along it - onto the stair blocks, beside and onto the spare blocks,
        // into the corners - and back: nowhere does the player end up wedged.
        [Test]
        public void SteppingOffTheBalconyAnywhere_NeverTrapsThePlayer()
        {
            foreach (float x in new[] { -6.7f, -6.1f, -5.3f, -4.6f, -3f, 0f, 3f, 4.6f, 5.3f, 6.7f })
            {
                Load();
                var bot = new Bot(Game);
                BotRunner.Run(Game, StepOffTheBalcony(bot, x), 30f);
                Assert.Less(Game.Player.Position.y, 2.1f, "off the balcony at x " + x + " (at " + Game.Player.Position + ")");
                Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 13.5f), 0.4f, 8f), 12f), "wedged after stepping off at x " + x);
                BotRunner.Run(Game, bot.WalkTo(new Vector3(6f, 0f, 14.5f), 0.3f, 8f), 12f);
                BotRunner.Run(Game, UpTheBlocks(bot), 30f);
                RunSeconds(0.5f);
                Assert.AreEqual(Level04DominoEffect.BalconyTop, Game.Player.Position.y, 0.05f, "and back on the balcony (at " + Game.Player.Position + ")");
                Game.Dispose();
                Game = null;
            }
        }

        [Test]
        public void RestartingWithTheDominoInHand_StartsClean()
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeFrom(bot, level, 1.1f), 30f);
            BotRunner.Run(Game, bot.WalkTo(Level04DominoEffect.EdgeSpot, 0.1f), 30f);
            BotRunner.Run(Game, bot.LookAt(Level04DominoEffect.AimPoint), 10f);
            Assert.AreSame(level.Domino, Game.Grabber.Held);
            Assert.Greater(level.Domino.Scale, 3f, "held over the footprint it is already big");

            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(Level04DominoEffect.DominoStartScale, level.Domino.Scale);
            Assert.Less(Vector3.Distance(level.Domino.Center, Pedestal), 0.05f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level04DominoEffect.SpawnPoint), 0.05f);
            RunSeconds(3f);
            Assert.Less(Vector3.Distance(level.Domino.Center, Pedestal), 0.05f, "and it stays on its pedestal");
            TestHelpers.PlayLevel(Game, 90f);
        }

        // ---- Tricks ---------------------------------------------------------------------------------------------

        static IEnumerator CatchItFallingAndStandItAtTheWall(Bot bot, Level04DominoEffect level)
        {
            yield return bot.Until(() => level.Domino.Transform.up.y < 0.9f, 4f);
            yield return bot.Grab(level.Domino);
            yield return DropAtPitch(bot, 0f);
        }

        // The domino is caught while it topples (it is moving at speed toward the wall by then) and let go
        // again right against the wall. A hold starts from rest: it keeps none of that speed.
        [Test]
        public void CaughtWhileFalling_AndLetGoAtTheWall_ItHasNoSpeedLeft()
        {
            Level04DominoEffect level = Load();
            List<string> said = Listen();
            Bot bot = Drop(level, -7.5f);
            BotRunner.Run(Game, CatchItFallingAndStandItAtTheWall(bot, level), 30f);
            Assert.IsFalse(level.Barricade.Broken, "it was caught before it reached the wall");
            Assert.Greater(level.Domino.Mass, Level04DominoEffect.BarricadeMinMass * 4f, "at the wall it is as big as it gets: " + level.Domino.Scale);
            RunSeconds(6f);
            Assert.IsFalse(level.Barricade.Broken, "stood against the wall it only leans, whatever it was doing before");
            Assert.Greater(level.Domino.Transform.up.y, 0.9f);
            CollectionAssert.Contains(said, Level04DominoEffect.NoRoom);
        }

        [Test]
        public void RestartingWhileStandingInsideTheFallenDomino_StartsClean()
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, StandItUphillFromTheFootOfTheWall(bot, level, 55f), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Barricade.Broken, 8f));
            RunSeconds(3f);
            Assert.IsTrue(TouchesNothing(level), "the player stands inside the fallen domino");
            Game.RestartLevel();
            Assert.IsFalse(level.Barricade.Broken);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level04DominoEffect.SpawnPoint), 0.05f);
            RunSeconds(0.5f);
            Assert.IsFalse(TouchesNothing(level), "the new domino is solid to the player, like any toy");
            TestHelpers.PlayLevel(Game, 90f);
        }

        // A fallen domino as a step: on top of it at the foot of the wall the player is a little higher, and
        // the arch is still walled up to the lintel, and the lintel reaches the sky cap.
        [Test]
        public void StandingOnAFallenDominoAtTheWall_GetsNobodyOver()
        {
            Level04DominoEffect level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, DownToTheWall(bot), 30f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(4.5f, -2.5f, 23.2f), 0.2f, 10f), 15f);
            // Too light for the wall, toppled so that it lies with its tip against it (a test fixture's shortcut).
            const float scale = 2.9f;
            float uphillEdge = Level04DominoEffect.BoardEnd - 0.2f - 2.3f * scale;
            level.Domino.SetScale(scale);
            level.Domino.SetPose(new Vector3(0f, Level04DominoEffect.BoardY(uphillEdge) + 0.01f + scale, uphillEdge + 0.15f * scale), Quaternion.identity);
            RunSeconds(5f);
            Assert.Less(level.Domino.Transform.up.y, 0.5f, "it lies on the board");
            Assert.IsFalse(level.Barricade.Broken);

            // Onto it from the side, then along it at the wall, jumping all the way.
            float highest = float.MinValue, farthest = float.MinValue;
            foreach (Vector3 toward in new[] { new Vector3(0f, 0f, 23.2f), new Vector3(0f, 0f, 40f), new Vector3(-3f, 0f, 40f), new Vector3(3f, 0f, 40f) })
            {
                Input = new ScriptedInput();
                Game.Input = Input;
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                for (int i = 0; i < TestHelpers.Ticks(2.5f); i++)
                {
                    TestHelpers.LookAt(Game.Player, new Vector3(toward.x, Game.Player.Eye.y, toward.z));
                    if (i % 20 == 0) Input.Once.Jump = true;
                    Game.Tick();
                    highest = Mathf.Max(highest, Game.Player.Position.y - Level04DominoEffect.BoardY(Game.Player.Position.z));
                    farthest = Mathf.Max(farthest, Game.Player.Position.z);
                }
            }
            Assert.Greater(highest, 0.3f * scale + 1f, "the player did get on top of the domino and jumped from it");
            // In the tick in which a sprint jump arrives at the wall the capsule is up to that tick's travel
            // inside its face (measured here: 0.06 at 7.9 units a second; the wall is 1.2 thick), and is out
            // again in the next. How far depends on where in the tick it arrives, so that is the bound.
            Assert.Less(farthest, Level04DominoEffect.BoardEnd - Player.BaseRadius + Player.SprintSpeed * Sim.Dt, "and is still on this side of the wall");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsFalse(level.Barricade.Broken);
        }
    }
}
