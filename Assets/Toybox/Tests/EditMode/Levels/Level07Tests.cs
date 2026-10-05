using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 7, "The Teeter-Totter": the pebble has to be made heavy and let go over the high end of the
    /// ruler while the player stands on the bullseye of the low end; in the air they push toward the books.
    ///
    /// Measured (tools/out/notes/level07-build.md): the solver's boulder is 4.51 across (LEVELS: 4.6);
    /// from the bullseye a boulder of 2.7 and up reaches the top of the books, the rule's own threshold
    /// (apex 11.6) is 3.0, the clamp is 7; held against the far wall at a view pitch of 12 to 55 degrees it
    /// lands on the high end; picked up from within 2.7 of the pebble it comes out heavy enough.
    /// </summary>
    public class Level07Tests : SimTest
    {
        static readonly Vector3 Perch = new Vector3(-2.1f, 2.6f, 0.7f);
        /// <summary>On the shelf between the bobbin and the book: as near the pebble as a walk-up pick-up usually is.</summary>
        static readonly Vector3 BobbinFoot = new Vector3(-2.6f, 0f, 0f);
        /// <summary>On the shelf in front of the book's corner: the way from the bobbin to the foot of the ruler.</summary>
        static readonly Vector3 Corner = new Vector3(-1.75f, 0f, -0.6f);

        Level07TeeterTotter level;
        Bot bot;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(7);
            level = (Level07TeeterTotter)Game.Level;
            bot = new Bot(Game);
        }

        void Unload()
        {
            Game.Dispose();
            Game = null;
        }

        // Pumps a bot script for at most this long. True if it ran to its end; a command that fails (a flight
        // that never lands on the books) ends it too, and that is not an error here.
        bool Drive(IEnumerator script, float seconds)
        {
            var runner = new BotRunner(script, bot, Game);
            try
            {
                for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
                {
                    if (!runner.Advance()) return true;
                    Game.Tick();
                }
            }
            catch (BotException)
            {
            }
            return false;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        bool OnTheBooks => Game.Player.Grounded && Game.Player.Position.y > Level07TeeterTotter.TowerTop - 0.5f;

        bool PebbleIsHome => level.Pebble.Frozen && Vector3.Distance(level.Pebble.Center, Perch) < 0.02f &&
                             Mathf.Abs(level.Pebble.Scale - Level07TeeterTotter.StartScale) < 1e-4f;

        // The pebble as a boulder of a given size, let go against the far wall over the high end of the ruler
        // (a test fixture's shortcut for a perfect drop).
        void BoulderOverTheHighEnd(float scale, float x = 0f)
        {
            float z = Level07TeeterTotter.BackstopZ - scale * 0.5f - 0.02f;
            float top = Level07TeeterTotter.Pivot.y + (z - Level07TeeterTotter.Pivot.z) * 0.18f + Level07TeeterTotter.RulerThickness;
            level.Pebble.Unfreeze();
            level.Pebble.SetScale(scale);
            level.Pebble.SetPose(new Vector3(x, top + scale * 0.5f * 1.02f + 0.4f, z), Quaternion.identity);
        }

        // Lets the held pebble go along a view direction given by yaw and pitch, as seen from where the bot stands.
        IEnumerator LetGoAt(float yawDegrees, float pitchDegrees)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad, pitch = pitchDegrees * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            yield return bot.DropAt(bot.Player.Eye + direction * 10f);
        }

        // The solution with the pick-up at the start and the release at a view of the test's choosing.
        IEnumerator SolveLookingAt(float yawDegrees, float pitchDegrees)
        {
            yield return bot.Grab(level.Pebble);
            yield return level.Mount(bot);
            yield return LetGoAt(yawDegrees, pitchDegrees);
            yield return level.Fly(bot);
        }

        // From the shelf, wherever a failed try left the bot, with the pebble back on its bobbin and the ruler at
        // rest: to the bobbin's foot, pick the pebble up there, and the rest of the solution.
        IEnumerator SolveFromTheShelf()
        {
            yield return bot.Until(() => bot.Player.Grounded, 6f);
            yield return ToTheFootOfTheRuler();
            // (The ruler does not come down on a player who stands under its edge: it waits until they have left.)
            yield return bot.Until(() => level.Seesaw.State == SeesawState.Rest, 6f);
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return bot.WalkTo(BobbinFoot, 0.08f, 8f);
            yield return bot.Grab(level.Pebble);
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return level.Mount(bot);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
            yield return level.Fly(bot);
        }

        /// <summary>On the shelf at the foot of the ruler, against the spines of the books: a step to the side of the ruler's line.</summary>
        static readonly Vector3 BesideTheFoot = new Vector3(1.15f, 0f, -1.3f);
        /// <summary>
        /// On the shelf beyond the bobbin, well to the side of the ruler: from here the boulder on the high end is in
        /// plain view past the ruler's raised end (from the ruler's own line the raised end hides it).
        /// </summary>
        static readonly Vector3 WestOfTheRuler = new Vector3(-3.2f, 0f, 2f);

        // Out of wherever a flight ended (the ruler itself, raised or not) to the shelf at the ruler's foot.
        IEnumerator ToTheFootOfTheRuler()
        {
            Vector3 at = bot.Player.Position;
            if (Mathf.Abs(at.x) <= 1.5f && at.z > 0f) yield return bot.WalkTo(new Vector3(at.x, 0f, -1.3f), 0.25f, 8f);
            yield return bot.WalkTo(new Vector3(0f, 0f, -1.3f), 0.25f, 8f);
        }

        // With the boulder lying on the high end and the ruler tipped: pick the boulder up from the shelf well to
        // the side of the ruler (from right behind its raised end the ruler hides the boulder), wait for the ruler
        // to come back down, and do it again from the bullseye.
        IEnumerator AgainWithTheBoulder()
        {
            yield return bot.Until(() => bot.Player.Grounded, 4f);
            yield return ToTheFootOfTheRuler();
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return bot.WalkTo(BobbinFoot, 0.15f, 8f);
            yield return bot.WalkTo(WestOfTheRuler, 0.25f, 8f);
            yield return bot.Grab(level.Pebble);
            yield return bot.Until(() => level.Seesaw.State == SeesawState.Rest, 5f);
            yield return bot.WalkTo(BobbinFoot, 0.15f, 8f);
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return level.Mount(bot);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
            yield return level.Fly(bot);
        }

        void AssertCompleted(string what)
        {
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, what + " (bot at " + Game.Player.Position + ", pebble " + level.Pebble.Scale + " at " + level.Pebble.Center + ", ruler " + level.Seesaw.State + ")");
        }

        // ---- What the level is ---------------------------------------------------------------------------------

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(7));
            Load();
            Assert.AreEqual("teeter-totter", level.Slug);
            Assert.AreEqual("The Teeter-Totter", level.Title);
            Assert.AreEqual(2, level.Phase);
            Assert.AreEqual("high-shelf", level.Environment);
            Assert.AreEqual(0f, level.GroundY);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.IsNotEmpty(level.Blurb);

            Prop pebble = level.Pebble;
            Assert.AreEqual("Pebble", pebble.Name);
            Assert.AreEqual(0.6f, pebble.Scale, 1e-4f);
            Assert.AreEqual(0.2f, pebble.MinScale, 1e-4f);
            Assert.AreEqual(7f, pebble.MaxScale, 1e-4f);
            Assert.AreEqual(GrabPose.Keep, pebble.GrabPose);
            Assert.IsTrue(pebble.HasTag(Level07TeeterTotter.WeightTag));
            Assert.IsTrue(pebble.Frozen, "a sphere on a flat top: frozen until it is picked up");
            Assert.AreEqual(1.571f * 0.216f, pebble.Mass, 0.005f, "1.571 x 0.6^3");
            Assert.Less(Vector3.Distance(pebble.Center, Perch), 0.01f, "on its bobbin, an arm's length from the bullseye");
            int toys = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) toys++;
            Assert.AreEqual(1, toys, "the pebble is the only toy");

            Seesaw ruler = level.Seesaw;
            Assert.AreEqual(10.4f, ruler.RestAngle, 0.1f, "the long end lies on the shelf");
            Assert.AreEqual(-21.1f, ruler.TippedAngle, 0.1f);
            Assert.AreEqual(2.7f, ruler.StrikeTravel, 0.01f);
            Assert.AreEqual(SeesawState.Rest, ruler.State);
            Assert.Less(Vector3.Distance(level.SeatPoint, new Vector3(0f, 0.32f, 0.82f)), 0.03f, "the bullseye, 9.3 out on the long arm");
            Assert.AreEqual(0.89f, ruler.F(151f, Player.Mass), 0.005f, "the rule of LEVELS: f = (151 - 6) / (151 + 12)");

            // The rule's own threshold for "onto the books": an apex of 11.6 from the bullseye is a boulder of 3.0.
            Assert.AreEqual(3f, level.LeastScale, 0.03f);
            Assert.AreEqual(level.LeastMass, level.MassAt(level.LeastScale), 0.01f);
            Assert.AreEqual(Level07TeeterTotter.TowerTop + Level07TeeterTotter.Clearance, level.ApexFor(level.LeastMass, Level07TeeterTotter.SeatArm), 0.01f);
            Assert.AreEqual(13.7f, level.ApexFor(151f, Level07TeeterTotter.SeatArm), 0.1f, "LEVELS: the intended boulder throws to 13.7");

            Assert.AreEqual(1, Game.Exits.Count);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(7f, 12.25f, -2f)), 1e-3f, "the exit is on top of the books");

            // Nothing moves by itself.
            RunSeconds(2f);
            Assert.IsTrue(PebbleIsHome);
            Assert.AreEqual(SeesawState.Rest, ruler.State);
            Assert.AreEqual(ruler.RestAngle, ruler.Angle, 1e-4f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level07TeeterTotter.SpawnPoint), 0.03f, "the player stands on the book beside the bobbin");
            Assert.AreEqual(Level07TeeterTotter.StepTop, Game.Player.Position.y, 0.02f);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            float dropped = 0f, mass = 0f, f = 0f, launch = 0f, launchedAt = -1f, landedAt = -1f, apex = 0f;
            List<string> said = Listen();
            Game.Events.PropDropped += e => dropped = e.NewScale;
            level.Seesaw.SeesawStruck += (m, value) =>
            {
                if (f > 0f) return;
                mass = m;
                f = value;
            };
            level.Seesaw.SeesawLaunched += (rider, speed) =>
            {
                launch = speed;
                launchedAt = Game.Time;
            };
            Game.Context.OnUpdate(dt =>
            {
                apex = Mathf.Max(apex, Game.Player.Position.y);
                if (launchedAt >= 0f && landedAt < 0f && OnTheBooks) landedAt = Game.Time;
            });
            TestHelpers.PlayLevel(Game, 60f);

            Assert.AreEqual(Level07TeeterTotter.OutlineScale, dropped, Level07TeeterTotter.OutlineScale * 0.05f, "LEVELS appendix B: the solver's boulder is 4.6 within 5 %");
            Assert.AreEqual(level.MassAt(dropped), mass, 1f, "the strike load is the boulder");
            Assert.AreEqual(0.89f, f, 0.03f, "LEVELS appendix B: SeesawStruck with f = 0.89 +- 0.03");
            Assert.AreEqual(level.Seesaw.LaunchSpeed(f, Level07TeeterTotter.SeatArm), launch, 0.6f, "thrown from the bullseye");
            Assert.That(apex, Is.InRange(13f, 14.5f), "the rule promises 13.7");
            Assert.That(landedAt - launchedAt, Is.InRange(0.5f, 3f), "LEVELS appendix B: grounded above 10.5 within 3 s of the launch");
            Assert.Less(Game.Time, 15f, "the solution takes seconds");
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State, "the boulder keeps the high end down");
            Assert.AreEqual(dropped, level.Pebble.Scale, 1e-3f, "and stays where it did its job");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said, "one line: what the flight wants");
        }

        // What the first picture has in it, from where the player stands at the start.
        [Test]
        public void TheStartShowsTheToyTheLeverAndWhereTheToyGoes()
        {
            Load();
            Game.Tick();
            Player player = Game.Player;
            Vector3 eye = player.Eye;

            // The pebble is under the crosshair, close: a click takes it, and it looks as big as the solution needs.
            Assert.AreSame(level.Pebble, Game.Grabber.FindTarget(), "a click at the start picks the pebble up");
            Assert.Less(Vector3.Angle(player.Forward, level.Pebble.Center - eye), 8f);
            float distance = Vector3.Distance(eye, level.Pebble.Center);
            Assert.AreEqual(1.75f, distance, 0.02f, "LEVELS: picked up from 1.75 away");
            Assert.AreEqual(0.343f, Level07TeeterTotter.StartScale / distance, 0.004f);
            Assert.Greater(Level07TeeterTotter.StartScale / distance, Level07TeeterTotter.LooksTooSmall + 0.08f);

            // In the picture and not hidden: the bullseye, the marker, the high end of the ruler, the outline on
            // the far wall, the spines of the books beside the bullseye.
            var sights = new Dictionary<string, Vector3>
            {
                { "the bullseye", level.SeatPoint + Vector3.up * 0.03f },
                { "the marker", new Vector3(-1.8f, 1.2f, 9.3f) },
                { "the high end of the ruler", level.Seesaw.PointAt(-4.5f, Level07TeeterTotter.RulerThickness + 0.03f, level.Seesaw.RestAngle) + Vector3.left },
                { "the outline", Level07TeeterTotter.OutlineCentre + Vector3.back * 0.05f },
                { "the books", new Vector3(Level07TeeterTotter.TowerMinX - 0.05f, 4f, 1.5f) },
            };
            Quaternion toView = Quaternion.Inverse(player.LookRotation);
            float tanV = Mathf.Tan(35f * Mathf.Deg2Rad), tanH = tanV * 16f / 9f;
            foreach (KeyValuePair<string, Vector3> sight in sights)
            {
                Vector3 local = toView * (sight.Value - eye);
                Assert.Greater(local.z, 0f, sight.Key + " is in front of the player");
                Assert.Less(Mathf.Abs(local.x / local.z), tanH * 0.97f, sight.Key + " is in the picture (across)");
                Assert.Less(Mathf.Abs(local.y / local.z), tanV * 0.97f, sight.Key + " is in the picture (up and down)");
                // The first thing along the line of sight is the thing itself.
                Vector3 to = sight.Value - eye;
                bool blocked = Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                               Vector3.Distance(hit.point, sight.Value) > 0.4f;
                Assert.IsFalse(blocked, sight.Key + " is hidden by " + (blocked ? hit.collider.name + " at " + hit.point : ""));
            }

            // Paint and looks have no colliders: what stops a toy is what the level put there to stop it.
            foreach (string name in new[] { "Paint", "Stand Mark", "Books", "Step Book Looks", "Kerbs", "Clutter" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        // ---- No way round --------------------------------------------------------------------------------------

        [Test]
        public void WalkingAndJumping_NeverReachesTheExit()
        {
            Load();
            float highest = 0f;
            Game.Context.OnUpdate(dt => highest = Mathf.Max(highest, Game.Player.Position.y));

            // At the foot of the books, running and jumping at their spines under the exit.
            Assert.IsTrue(Drive(bot.WalkTo(new Vector3(1.15f, 0f, -1.5f), 0.3f, 10f), 12f));
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            foreach (Vector3 toward in new[] { new Vector3(7f, 0f, -2f), new Vector3(7f, 0f, 3f), new Vector3(4f, 0f, -5.5f) })
                for (int i = 0; i < TestHelpers.Ticks(2.5f); i++)
                {
                    TestHelpers.LookAt(Game.Player, new Vector3(toward.x, Game.Player.Eye.y, toward.z));
                    if (i % 20 == 0) Input.Once.Jump = true;
                    Game.Tick();
                }
            Assert.IsFalse(Game.LevelCompleted);
            // (Along the spines the feet find the ruler's edge, which rises toward the marker: a jump from it is still a jump.)
            Assert.Less(highest, 3.5f, "a jump at the foot of the books is a jump");
            Assert.Less(Game.Player.Position.x, Level07TeeterTotter.TowerMinX, "nobody gets into the books");

            // Along the ruler with nothing on its high end: it tips under the player, gently, and that is all.
            bot = new Bot(Game);
            Assert.IsTrue(Drive(bot.WalkTo(BesideTheFoot, 0.25f, 8f), 10f), "back to the foot of the ruler (bot at " + Game.Player.Position + ")");
            Assert.IsTrue(Drive(level.Mount(bot), 20f), "onto the ruler over its tip, without a jump (bot at " + Game.Player.Position + ")");
            Assert.AreEqual(level.SeatPoint.y, Game.Player.Position.y, 0.1f);
            Assert.IsTrue(Drive(bot.WalkTo(new Vector3(0f, 0f, 13.8f), 0.3f, 10f), 12f), "out along the short arm (bot at " + Game.Player.Position + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Tipped, 4f));
            Assert.IsFalse(level.Seesaw.Launched, "it lowered the player; it threw nobody");
            Drive(bot.Jump(false), 1f);
            RunSeconds(1.5f);
            Assert.Less(highest, 4f);
            Assert.IsFalse(Game.LevelCompleted);

            // The gate itself: even the raised end of the ruler plus a jump is far short of the top of the books,
            // and so is the top of the biggest boulder.
            float raisedEnd = level.Seesaw.PointAt(Level07TeeterTotter.LongArm, Level07TeeterTotter.RulerThickness, level.Seesaw.TippedAngle).y;
            Assert.Less(raisedEnd + 1.3f, Level07TeeterTotter.TowerTop - 4f, "the raised end is " + raisedEnd);
            Assert.Less(Level07TeeterTotter.MaxScale + 1.3f, Level07TeeterTotter.TowerTop - 2f);
        }

        [Test]
        public void TheShelfCornerIsSealed()
        {
            Load();
            RunSeconds(0.1f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            const float top = Level07TeeterTotter.WallTop, half = Level07TeeterTotter.HalfWidth;
            // The four sides, from the shelf to the sky cap - the two that are drawn and the two that are open to
            // the room. (From above everything that stands in the level, so that only the walls can answer.)
            for (float y = 0.4f; y <= top - 0.3f; y += 0.6f)
            {
                for (float z = Level07TeeterTotter.NearZ + 0.2f; z <= Level07TeeterTotter.BackstopZ - 0.2f; z += 0.6f)
                {
                    // (Where the books stand against a side, the side is looked at above them.)
                    float beside = z > Level07TeeterTotter.TowerMaxZ + 0.3f ? y : Mathf.Max(y, Level07TeeterTotter.TowerTop + 0.3f);
                    Assert.IsTrue(scene.Raycast(new Vector3(-half + 0.5f, y, z), Vector3.left, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at -X, y " + y + ", z " + z);
                    Assert.IsTrue(scene.Raycast(new Vector3(half - 0.5f, beside, z), Vector3.right, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at +X, y " + beside + ", z " + z);
                }
                for (float x = -half + 0.2f; x <= half - 0.2f; x += 0.6f)
                {
                    float behind = x < Level07TeeterTotter.TowerMinX - 0.3f ? y : Mathf.Max(y, Level07TeeterTotter.TowerTop + 0.3f);
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, Level07TeeterTotter.BackstopZ - 0.5f), Vector3.forward, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at +Z, x " + x + ", y " + y);
                    Assert.IsTrue(scene.Raycast(new Vector3(x, behind, Level07TeeterTotter.NearZ + 0.5f), Vector3.back, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at -Z, x " + x + ", y " + behind);
                }
            }
            for (float x = -half + 0.2f; x <= half - 0.2f; x += 0.7f)
                for (float z = Level07TeeterTotter.NearZ + 0.2f; z <= Level07TeeterTotter.BackstopZ - 0.2f; z += 0.7f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, top - 0.5f, z), Vector3.up, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at the top, x " + x + ", z " + z);

            // The books: one sheer face from the shelf to their top, with nothing to stand on on the way up.
            for (float y = 0.1f; y <= Level07TeeterTotter.TowerTop - 0.05f; y += 0.25f)
                for (float z = Level07TeeterTotter.NearZ + 0.1f; z <= Level07TeeterTotter.TowerMaxZ - 0.1f; z += 0.5f)
                {
                    Assert.IsTrue(scene.Raycast(new Vector3(Level07TeeterTotter.TowerMinX - 0.4f, y, z), Vector3.right, out RaycastHit hit, 1f, mask, QueryTriggerInteraction.Ignore));
                    Assert.AreEqual(Level07TeeterTotter.TowerMinX, hit.point.x, 1e-3f, "the face of the books at y " + y + ", z " + z);
                }
        }

        static IEnumerator LookAndClick(Bot bot, Vector3 point)
        {
            yield return bot.LookAt(point);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void NothingButThePebble_CanBePickedUp()
        {
            Load();
            var setPieces = new[]
            {
                new Vector3(-1.2f, 1.2f, 9.2f),                                   // the marker
                new Vector3(0f, 1.2f, 5f), level.SeatPoint,                       // the ruler, its bullseye
                Level07TeeterTotter.BobbinBase + Vector3.up * 1.2f,               // the bobbin
                new Vector3(-8.7f, 0.9f, 13.4f), new Vector3(-8.8f, 0.7f, -4.3f), // blocks in the corners
                new Vector3(Level07TeeterTotter.TowerMinX, 4f, 2f), new Vector3(0f, 6.6f, 15.5f), // the books, the outline
                Level07TeeterTotter.StepCentre + new Vector3(0.5f, 0f, -1.2f),    // the book underfoot
            };
            foreach (Vector3 piece in setPieces)
            {
                Assert.IsTrue(Drive(LookAndClick(bot, piece), 10f));
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            Assert.IsTrue(PebbleIsHome);
        }

        // ---- The scale window ------------------------------------------------------------------------------------

        // LEVELS appendix B: works from 3.3 (hard 3.0) to 7, the clamp. Measured: from 2.7.
        [TestCase(3.3f)]
        [TestCase(7f)]
        public void BothEndsOfTheWindow_ThrowThePlayerOntoTheBooks(float scale)
        {
            Load();
            List<string> said = Listen();
            float f = 0f;
            level.Seesaw.SeesawStruck += (m, value) =>
            {
                if (f <= 0f) f = value;
            };
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BoulderOverTheHighEnd(scale);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("a boulder of " + scale + " throws the player onto the books");
            Assert.AreEqual(level.Seesaw.F(level.MassAt(scale), Player.Mass), f, 2e-3f, "by the rule");
            Assert.GreaterOrEqual(level.ApexFor(level.MassAt(scale), Level07TeeterTotter.SeatArm), Level07TeeterTotter.TowerTop + Level07TeeterTotter.Clearance);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
            Assert.AreEqual(scale, level.Pebble.Scale, 1e-3f, "the boulder stays on the high end");
        }

        [TestCase(1.5f)]
        [TestCase(2.4f)]
        public void AClearlyWrongSize_DoesNot_AndThePebbleGoesHome(float scale)
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BoulderOverTheHighEnd(scale);
            Assert.IsFalse(Drive(level.Fly(bot), 10f), "the flight does not end on the books");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(apex, Level07TeeterTotter.TowerTop - 1f, "thrown to " + apex + ": visibly not enough");
            Assert.Less(level.MassAt(scale), level.LeastMass);
            RunSeconds(3f);
            Assert.IsTrue(PebbleIsHome, "what is too light goes back to the bobbin (it is at " + level.Pebble.Center + ")");
            Assert.AreEqual(1, level.Leash.Returns);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.ShortLine }, said, "and the level says why");

            // Nothing is lost: from where that left the player the level is solved in the ordinary way.
            BotRunner.Run(Game, SolveFromTheShelf(), 60f);
            AssertCompleted("solved after a try that was too light");
            Assert.AreEqual(1, level.Leash.Returns);
        }

        // The pebble let go against the far wall from the bullseye: LEVELS gives a view pitch of 13 to 50 degrees
        // for the solver's pick-up (measured: 12 to 55).
        [TestCase(13f)]
        [TestCase(30f)]
        [TestCase(50f)]
        public void HeldAgainstTheFarWall_AnywhereInThePitchBand_ItWorks(float pitch)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, SolveLookingAt(0f, pitch), 60f);
            AssertCompleted("let go at a pitch of " + pitch + " (boulder " + dropped + ")");
            Assert.That(dropped, Is.InRange(3.3f, 6.5f), "the wall, not the clamp, decides the size");
        }

        [TestCase(6f)]
        [TestCase(-6f)]
        public void ALittleOffToTheSide_ItStillLandsOnTheHighEnd(float yaw)
        {
            Load();
            BotRunner.Run(Game, SolveLookingAt(yaw, 18f), 60f);
            AssertCompleted("let go " + yaw + " degrees off the ruler's line");
        }

        // ---- Tries that fail, and what the level says ------------------------------------------------------------

        [Test]
        public void HeldTooLow_ItLandsOnThePlayersOwnEnd_AndGoesHome()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Assert.IsFalse(Drive(SolveLookingAt(0f, 4f), 12f), "nothing is thrown");
            Assert.That(dropped, Is.InRange(1f, 2.8f), "it stopped on the ruler in front of the marker, small");
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);
            RunSeconds(2f);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.OwnEndLine }, said);
            Assert.IsFalse(Game.LevelCompleted);

            // The player is still on the bullseye; the pebble is an arm's length away on its bobbin: again, higher.
            Assert.IsTrue(Drive(bot.Grab(level.Pebble), 5f));
            Assert.That(Game.Grabber.Ratio, Is.InRange(0.25f, 0.29f), "picked up from the bullseye it looks big enough (LEVELS: 0.27)");
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("picked up from the bullseye and held against the outline");
            Assert.That(level.Pebble.Scale, Is.InRange(3.4f, 4.2f), "LEVELS: from the seat itself 3.6 to 5.1");
        }

        [Test]
        public void LetGoFarOffToTheSide_ItMissesTheRuler_AndGoesHome()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Assert.IsFalse(Drive(SolveLookingAt(-25f, 18f), 12f));
            Assert.Greater(dropped, 3f, "a boulder all the same");
            RunSeconds(2f);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.MissedLine }, said);
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);
        }

        static IEnumerator LetGoFromTheBook(Bot bot, Level07TeeterTotter level)
        {
            yield return bot.Grab(level.Pebble);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
        }

        // LEVELS: "Dropped the boulder first and walked to the seat afterwards: the long end is up; nothing
        // launches. Grab the boulder, stand on the bullseye, and drop it again."
        [Test]
        public void LetGoWithNobodyOnTheLowEnd_TheRulerFlips_AndTheBoulderIsUsedAgain()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, LetGoFromTheBook(bot, level), 20f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Tipped, 4f), "the boulder (" + level.Pebble.Scale + " at " + level.Pebble.Center + ") tips the ruler");
            Assert.IsFalse(level.Seesaw.Launched);
            float boulder = level.Pebble.Scale;
            Assert.Greater(boulder, 4f);
            RunSeconds(4f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.NobodyLine }, said);
            Assert.AreEqual(boulder, level.Pebble.Scale, 1e-3f, "heavy enough and on the high end: it stays");
            Assert.AreEqual(0, level.Leash.Returns);
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State);

            said.Clear();
            Assert.IsTrue(Drive(bot.WalkTo(new Vector3(-1.75f, 0f, -1.3f), 0.25f, 8f), 10f));
            BotRunner.Run(Game, AgainWithTheBoulder(), 60f);
            AssertCompleted("the boulder picked up again and let go from the bullseye");
            CollectionAssert.DoesNotContain(said, Level07TeeterTotter.FarLine, "picked up from across the shelf a boulder looks big enough");
            CollectionAssert.DoesNotContain(said, Level07TeeterTotter.NobodyLine);
        }

        [Test]
        public void StandingShortOfTheBullseye_IsToldSo_AndTheBoulderStays()
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            BotRunner.Run(Game, level.Mount(bot), 20f);
            // Two and a bit units in from the tip: LEVELS, "two units in from the tip costs a fifth of the height".
            BotRunner.Run(Game, bot.WalkTo(level.Seesaw.PointAt(7f, Level07TeeterTotter.RulerThickness, level.Seesaw.RestAngle), 0.08f, 8f), 10f);
            RunSeconds(0.3f);
            BoulderOverTheHighEnd(4.5f);
            Assert.IsFalse(Drive(level.Fly(bot), 10f));
            Assert.Less(apex, Level07TeeterTotter.TowerTop - 1f, "thrown to " + apex);
            RunSeconds(3f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.OffSeatLine }, said);
            Assert.AreEqual(4.5f, level.Pebble.Scale, 1e-3f, "the boulder is good: it stays on the high end");

            BotRunner.Run(Game, AgainWithTheBoulder(), 60f);
            AssertCompleted("the same boulder from the bullseye");
        }

        // LEVELS: "Standing on the short end with the boulder on the long end: a quarter of the height." Here the
        // ruler only ever throws its long end; the other way round nothing happens, and the level says so.
        [Test]
        public void TheOtherWayRound_NothingIsThrown_AndTheLevelSaysWhichEndIsWhich()
        {
            Load();
            List<string> said = Listen();
            float highest = 0f;
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 13.8f), 0.3f, 10f), 12f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Tipped, 4f), "the ruler tips under the player on its short end");
            Game.Context.OnUpdate(dt => highest = Mathf.Max(highest, Game.Player.Position.y));
            // The pebble, grown, on the raised long end (a test fixture's shortcut for a hold let go there).
            level.Pebble.Unfreeze();
            level.Pebble.SetScale(2f);
            level.Pebble.SetPose(level.Seesaw.PointAt(8.6f, Level07TeeterTotter.RulerThickness + 1.05f, level.Seesaw.TippedAngle), Quaternion.identity);
            RunSeconds(1.2f);
            Assert.IsFalse(PebbleIsHome);
            RunSeconds(1f);
            Assert.IsTrue(PebbleIsHome, "it is of no use there: back to the bobbin");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.WrongEndLine }, said);
            Assert.IsFalse(level.Seesaw.Launched);
            Assert.Less(highest, 1.5f, "nobody went anywhere");
            Assert.IsFalse(Game.LevelCompleted);
        }

        // The flight as a first-time player flies it: nothing at all.
        static IEnumerator FlyWithoutSteering(Bot bot, Level07TeeterTotter level)
        {
            yield return bot.Until(() => level.Seesaw.Launched, 4f);
            yield return bot.Wait(0.5f);
            yield return bot.Until(() => bot.Player.Grounded, 5f);
        }

        // On the raised end of the tipped ruler, where a flight without steering ends: pick the boulder up from
        // there, come down with the ruler, and let go again from the bullseye.
        IEnumerator AgainFromTheRaisedEnd()
        {
            yield return bot.Grab(level.Pebble);
            yield return bot.Until(() => level.Seesaw.State == SeesawState.Rest && bot.Player.Grounded, 6f);
            yield return bot.WalkTo(level.SeatPoint, 0.12f, 6f);
            yield return bot.Until(() => GadgetKit.PlayerStandsOn(Game, level.Seesaw.Mover.Body), 2f);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
            yield return level.Fly(bot);
        }

        [Test]
        public void ThrownHighEnoughWithoutSteering_ThePlayerComesBackDown_AndIsToldToSteer()
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, FlyWithoutSteering(bot, level), 20f);
            Assert.Greater(apex, Level07TeeterTotter.TowerTop + 2f, "thrown well over the top of the books (" + apex + ")");
            Assert.IsFalse(OnTheBooks);
            Assert.Less(Mathf.Abs(Game.Player.Position.x), 1.5f, "straight up and straight down: back on the ruler (at " + Game.Player.Position + ")");
            RunSeconds(3f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine, Level07TeeterTotter.SteerLine }, said);
            Assert.Greater(level.Pebble.Scale, 4f, "the boulder stays where it is");
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State);

            float before = level.Pebble.Scale;
            BotRunner.Run(Game, AgainFromTheRaisedEnd(), 60f);
            AssertCompleted("the same boulder again, this time steering");
            Assert.AreEqual(before, level.Pebble.Scale, before * 0.25f, "picked up from the ruler's end and held against the same wall it is about as big as before");
        }

        // ---- The pick-up ----------------------------------------------------------------------------------------

        static IEnumerator TakeFromTheBackOfTheBook(Bot bot, Level07TeeterTotter level)
        {
            // Where LEVELS has the player start: (-5, -3.5), five units from the pebble.
            yield return bot.WalkTo(new Vector3(-5f, 0f, -3.5f), 0.1f, 8f);
            yield return bot.Grab(level.Pebble);
            yield return bot.WalkTo(Level07TeeterTotter.SpawnPoint, 0.3f, 8f);
            yield return level.Mount(bot);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
        }

        [Test]
        public void PickedUpFromTooFarAway_ItIsCalledOut_ComesOutTooLight_AndGoesHome()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f, ratio = 0f;
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, TakeFromTheBackOfTheBook(bot, level), 40f);
            Assert.That(ratio, Is.InRange(0.1f, 0.13f), "from five units away it looks tiny");
            Assert.That(dropped, Is.InRange(1.3f, 2.2f), "and against the far wall it is no boulder");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FarLine }, said, "said at the pick-up, in the campaign's words");
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level07TeeterTotter.FarLine);
            Assert.IsFalse(Drive(level.Fly(bot), 8f));
            RunSeconds(1f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FarLine, Level07TeeterTotter.TooLightLine }, said);

            BotRunner.Run(Game, SolveFromTheShelf(), 60f);
            AssertCompleted("picked up from the bobbin's foot it works");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FarLine, Level07TeeterTotter.TooLightLine, Level07TeeterTotter.FlyLine }, said, "no further complaint");
        }

        // As near as a player can get to the pebble on its perch, it never looks so big that it would reach its
        // largest size before the far wall (LEVELS: "the clamp is never the limiter").
        [Test]
        public void FromAsCloseAsAnyoneCanGet_ThePebbleStillStopsOnTheWall()
        {
            var ratios = new List<float>();
            // Against the bobbin from three sides of the shelf, and at the very edge of the book.
            var routes = new[]
            {
                new[] { BobbinFoot, Level07TeeterTotter.BobbinBase },
                new[] { BobbinFoot, new Vector3(-2.9f, 0f, 1.6f), Level07TeeterTotter.BobbinBase + new Vector3(0f, 0f, 1.5f), Level07TeeterTotter.BobbinBase },
                new[] { Corner, Level07TeeterTotter.BobbinBase },
                new[] { Level07TeeterTotter.SpawnPoint + (Level07TeeterTotter.BobbinBase - Level07TeeterTotter.SpawnPoint).normalized * 0.45f },
            };
            foreach (Vector3[] route in routes)
            {
                Load();
                foreach (Vector3 point in route) Drive(bot.WalkTo(point, 0.04f, 3f), 4f);
                Assert.IsTrue(Drive(bot.Grab(level.Pebble), 5f), "the pebble can be picked up from " + Game.Player.Position);
                ratios.Add(Game.Grabber.Ratio);
                Assert.Less(Game.Grabber.Ratio, 0.52f, "from " + Game.Player.Position + " it looks " + Game.Grabber.Ratio);
                Unload();
            }
            ratios.Sort();
            Assert.Greater(ratios[ratios.Count - 1], 0.42f, "the routes did get close: " + string.Join(", ", ratios));

            // And the nearest of them, carried to the bullseye: stopped by the wall, under the clamp, and it works.
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Drive(bot.WalkTo(BobbinFoot, 0.04f, 3f), 4f);
            Drive(bot.WalkTo(Level07TeeterTotter.BobbinBase, 0.04f, 3f), 4f);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.WalkTo(BobbinFoot, 0.15f, 5f), 8f);
            BotRunner.Run(Game, bot.WalkTo(Corner, 0.2f, 5f), 8f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            Assert.That(dropped, Is.InRange(5f, Level07TeeterTotter.MaxScale - 0.5f), "big, and still short of its largest size");
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("the biggest boulder a pick-up from the perch gives");
        }

        // ---- Restart, respawn, replay ----------------------------------------------------------------------------

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Load();
            // A wrong try first, then the restart key while the pebble lies on the ruler.
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, LetGoAt(0f, 4f), 10f);
            RunSeconds(0.5f);
            Assert.Greater(level.Pebble.Scale, 1f);
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreSame(level, Game.Level, "the same level, built again");
            Assert.IsTrue(PebbleIsHome, "a fresh pebble on its bobbin");
            Assert.IsNull(Game.Grabber.Held);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level07TeeterTotter.SpawnPoint), 0.05f);
            Assert.AreEqual(0, level.Leash.Returns, "a new leash");

            // Again, and restart in the middle of the swing with the player on the rising end.
            bot = new Bot(Game);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Swing && level.Seesaw.Angle < 0f, 4f));
            Seesaw swinging = level.Seesaw;
            Game.RestartLevel();
            Assert.AreNotSame(swinging, level.Seesaw);
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);
            Assert.AreEqual(level.Seesaw.RestAngle, level.Seesaw.Angle, 1e-4f);
            Assert.IsTrue(PebbleIsHome);
            RunSeconds(1f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level07TeeterTotter.SpawnPoint), 0.05f, "nothing of the old swing reaches the new start");

            // And once more from the top of the books, after the boulder has done its job.
            TestHelpers.PlayLevel(Game, 60f);
            Game.RestartLevel();
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(PebbleIsHome);
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void APebbleThatLeavesTheWorld_OrLiesWhereItIsNoUse_ComesBack()
        {
            Load();
            List<string> said = Listen();
            Prop pebble = level.Pebble;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;

            // Below the kill plane (a test fixture's shortcut: nothing in the level lets it fall there).
            pebble.Unfreeze();
            pebble.SetScale(3f);
            pebble.SetPose(new Vector3(0f, level.KillY - 5f, 0f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns);
            Assert.IsTrue(PebbleIsHome, "back on the bobbin at the size it started with, and frozen again");
            RunSeconds(1f);
            Assert.IsTrue(PebbleIsHome, "where it stays");

            // On top of the books, out of sight from the shelf: the leash brings it back after a second and a half.
            pebble.Unfreeze();
            pebble.SetScale(2f);
            pebble.SetPose(new Vector3(6f, Level07TeeterTotter.TowerTop + 1.2f, 2f), Quaternion.identity);
            RunSeconds(1.2f);
            Assert.AreEqual(1, respawns, "not before its grace time is over");
            RunSeconds(0.6f);
            Assert.AreEqual(2, respawns);
            Assert.AreEqual(1, level.Leash.Returns);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.MissedLine }, said);

            // A crumb on the shelf.
            said.Clear();
            pebble.Unfreeze();
            pebble.SetScale(0.3f);
            pebble.SetPose(new Vector3(-6f, 0.5f, 6f), Quaternion.identity);
            RunSeconds(2f);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.BackLine }, said);

            // And the level is still there to be solved.
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void TheSolution_PlaysTheSameEveryTime()
        {
            const int runs = 4;
            var ticks = new int[runs];
            var rest = new Vector3[runs];
            var launch = new float[runs];
            for (int run = 0; run < runs; run++)
            {
                Load();
                int index = run;
                level.Seesaw.SeesawLaunched += (rider, speed) => launch[index] = speed;
                TestHelpers.PlayLevel(Game, 60f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Pebble.Center;
                Unload();
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the boulder comes to rest in the same place in run " + run);
                Assert.AreEqual(launch[0], launch[run], "after the same launch in run " + run);
            }

            // And once more in a game that has already been through a wrong try and a restart.
            Load();
            Drive(SolveLookingAt(0f, 4f), 6f);
            Game.RestartLevel();
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(ticks[0], Game.LevelTicks, "the same solve after a restart");
        }

        // ---- The review's attacks (tools/out/notes/level07-review.md) ---------------------------------------------

        // The solution up to the click by the bot; from the click on, a player's hands: the view stays on the far
        // wall, "right" (moveX 1) is toward the books. lean: seconds after the click at which the keys go down
        // (negative: before it). jumpAt: a tap on jump that long after the boulder has landed on the high end (the
        // ruler swings for half a second from then). boulder: instead of the click, a boulder of that size appears
        // over the high end (a fixture's perfect let-go).
        // True if the player stood on the books at any time.
        bool ByHand(float lean, float moveX = 1f, float moveZ = 0f, bool sprint = false, float jumpAt = -1f, float boulder = 0f, float seconds = 7f)
        {
            if (boulder <= 0f) BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.LookAt(Level07TeeterTotter.OutlineCentre), 5f);
            Input = new ScriptedInput();
            Game.Input = Input;
            int before = lean < 0f ? TestHelpers.Ticks(-lean) : 0;
            bool onBooks = false;
            int landed = -1, now = 0;
            level.Seesaw.SeesawStruck += (m, f) =>
            {
                if (landed < 0) landed = now;
            };
            for (int i = -before; i < TestHelpers.Ticks(seconds); i++)
            {
                now = i;
                Input.Hold = default;
                if (i * Sim.Dt >= lean - 1e-4f)
                {
                    Input.Hold.MoveX = moveX;
                    Input.Hold.MoveZ = moveZ;
                    Input.Hold.Sprint = sprint;
                }
                if (i == 0)
                {
                    if (boulder > 0f) BoulderOverTheHighEnd(boulder);
                    else Input.Once.GrabPressed = true;
                }
                if (jumpAt >= 0f && landed >= 0 && i == landed + TestHelpers.Ticks(jumpAt)) Input.Once.Jump = true;
                Game.Tick();
                onBooks |= OnTheBooks;
            }
            bot = new Bot(Game);
            return onBooks;
        }

        // Whatever a failed try left behind: the level is still there to be solved.
        void SolveWithWhatIsThere(string what)
        {
            bot = new Bot(Game);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 6f);
            RunSeconds(2.5f);
            BotRunner.Run(Game, PebbleIsHome ? SolveFromTheShelf() : AgainWithTheBoulder(), 90f);
            AssertCompleted(what);
        }

        // Found by the review: with half a unit between the ruler and the books, a push toward the books while the
        // ruler was still swinging (0.3 to 0.55 s after the click) walked the player off its edge - no throw, or a
        // wild one past the books - and nothing was said. The spines now stand against the ruler: leaning on them
        // is still standing on it. The push may begin at the click or as late as 0.9 s after the throw.
        [TestCase(0f, false)]
        [TestCase(0.15f, false)]
        [TestCase(0.3f, false)]
        [TestCase(0.45f, false)]
        [TestCase(0.6f, false)]
        [TestCase(0.9f, false)]
        [TestCase(1.3f, false)]
        [TestCase(1.6f, false)]
        [TestCase(0f, true)]
        [TestCase(0.3f, true)]
        [TestCase(0.45f, true)]
        public void PushingTowardTheBooks_FromTheClickOrFromMidAir_EndsOnTheBooks(float lean, bool sprint)
        {
            Load();
            List<string> said = Listen();
            Assert.IsTrue(ByHand(lean, 1f, 0f, sprint), "pushing toward the books from " + lean + " s after the click (ended at " + Game.Player.Position + ")");
            Assert.IsTrue(OnTheBooks, "and staying there");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
            Assert.Greater(level.Pebble.Scale, 4f);
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State);
        }

        // Added by the review: what the flight wants used to be said when the boulder landed, half a second before
        // the throw. It is said when the boulder is let go over the high end with the player in place - it falls
        // for half a second first - and not a second time when it lands.
        [Test]
        public void WhatTheFlightWants_IsSaidAsTheBoulderIsLetGo_WhileThereIsTimeToReadIt()
        {
            Load();
            List<string> said = Listen();
            int saidAt = -1, launchedAt = -1;
            Game.Events.Message += e =>
            {
                if (saidAt < 0 && e.Text == Level07TeeterTotter.FlyLine) saidAt = Game.LevelTicks;
            };
            level.Seesaw.SeesawLaunched += (rider, speed) => launchedAt = Game.LevelTicks;
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            RunSeconds(0.1f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said, "said at the let-go");
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State, "the boulder is still falling");
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("the solution");
            Assert.Greater((launchedAt - saidAt) * Sim.Dt, 0.8f, "most of a second's notice before the throw");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said, "and not twice");
        }

        // Leaning on the spines from before the boulder lands (it is on its way: a fixture's let-go), with the
        // lightest boulders the rule passes and with the heaviest.
        [TestCase(3.05f, -0.6f)]
        [TestCase(3.05f, 0.2f)]
        [TestCase(3.3f, -0.6f)]
        [TestCase(4.5f, -0.6f)]
        [TestCase(7f, -0.6f)]
        public void LeaningOnTheBooksBeforeTheBoulderLands_IsStillStandingOnTheRuler(float boulder, float lean)
        {
            Load();
            List<string> said = Listen();
            Assert.IsTrue(ByHand(lean, 1f, 0f, false, -1f, boulder), "a boulder of " + boulder + ", leaning from " + lean + " (ended at " + Game.Player.Position + ")");
            Assert.IsTrue(OnTheBooks);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
            Assert.AreEqual(boulder, level.Pebble.Scale, 1e-3f, "the boulder stays");
        }

        // Found by the review: a jump while the ruler swung lost the throw (the ruler passed through a player who
        // was in the air over it) and nothing was said; the level then explained it (LeftLine). The seesaw has been
        // put right since (SeesawTests.ARiderWhoJumpsWhileItSwings...): the ruler takes its rider along from under
        // their feet and throws them with everybody else. With a push toward the books that ends on the books.
        [TestCase(0.05f)]
        [TestCase(0.18f)]
        [TestCase(0.27f)]
        [TestCase(0.33f)]
        [TestCase(0.4f)]
        public void AJumpWhileTheRulerSwings_DoesNotLoseTheThrow(float jumpAt)
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            Assert.IsTrue(ByHand(0.6f, 1f, 0f, false, jumpAt), "a jump " + jumpAt + " s into the swing, then a push toward the books (apex " + apex + ", ended at " + Game.Player.Position + ")");
            Assert.IsTrue(OnTheBooks, "and staying there");
            Assert.Greater(apex, Level07TeeterTotter.TowerTop + 1f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
            Assert.Greater(level.Pebble.Scale, 4f);
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State);
            Assert.AreEqual(0, level.Leash.Returns);
        }

        // The same jump without the push: thrown as high as ever, back down beside the books, and told the one
        // thing that was missing - not that the ruler came up without them.
        [Test]
        public void AJumpWhileTheRulerSwings_WithoutAPush_IsThrownAndToldToSteer()
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            Assert.IsFalse(ByHand(99f, 1f, 0f, false, 0.18f), "nobody steered toward the books");
            Assert.Greater(apex, Level07TeeterTotter.TowerTop + 1f, "thrown all the same (to " + apex + ")");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine, Level07TeeterTotter.SteerLine }, said);
            Assert.AreEqual(0, level.Leash.Returns, "the boulder is good: it stays on the high end");

            SolveWithWhatIsThere("the same boulder again, with a push this time");
        }

        // A jump in the last tenth of a second of the swing takes the ruler's speed along: higher than the throw.
        // With a push toward the books that ends on them too, and then there is nothing to complain about.
        [Test]
        public void AJumpAtTheVeryEndOfTheSwing_GoesHigher_AndNothingIsSaidAgainstIt()
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            Assert.IsTrue(ByHand(0f, 1f, 0f, false, 0.48f), "on the books (ended at " + Game.Player.Position + ", apex " + apex + ")");
            Assert.Greater(apex, 15.5f, "higher than the ruler alone throws (14)");
            Assert.Less(apex, Level07TeeterTotter.WallTop - 1.5f, "the sky cap is the limit (head at 20)");
            CollectionAssert.DoesNotContain(said, Level07TeeterTotter.LeftLine);
            CollectionAssert.DoesNotContain(said, Level07TeeterTotter.SteerLine);
        }

        // Walking off the ruler while it swings - away from the books, or backward off its tip.
        [TestCase(-1f, 0f, 0.3f)]
        [TestCase(0f, -1f, 0.3f)]
        public void WalkingOffTheRulerWhileItSwings_IsToldSo(float moveX, float moveZ, float lean)
        {
            Load();
            List<string> said = Listen();
            Assert.IsFalse(ByHand(lean, moveX, moveZ));
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine, Level07TeeterTotter.LeftLine }, said);
            Assert.Greater(level.Pebble.Scale, 4f);
            SolveWithWhatIsThere("again, staying on the ruler");
        }

        // Walking toward the marker while it swings: thrown, but from where the ruler throws less.
        [Test]
        public void WalkingTowardTheMarkerWhileItSwings_IsThrownLower_AndToldWhy()
        {
            Load();
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            Assert.IsFalse(ByHand(0f, 0f, 1f));
            Assert.Less(apex, Level07TeeterTotter.TowerTop, "thrown to " + apex);
            CollectionAssert.Contains(said, Level07TeeterTotter.OffSeatLine);
            CollectionAssert.DoesNotContain(said, Level07TeeterTotter.SteerLine, "it was not high enough: steering is not what was missing");
            Assert.Greater(level.Pebble.Scale, 4f);
        }

        // Thrown and steering away from the books: the one thing left to say.
        [Test]
        public void ThrownAndSteeringAwayFromTheBooks_IsToldWhichWay()
        {
            Load();
            List<string> said = Listen();
            Assert.IsFalse(ByHand(0.9f, -1f, 0f));
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine, Level07TeeterTotter.SteerLine }, said);
        }

        static IEnumerator ThrowFromTheBook(Bot bot, Level07TeeterTotter level)
        {
            yield return bot.Grab(level.Pebble);
            yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
            yield return bot.Until(() => level.Seesaw.State == SeesawState.Tipped, 4f);
            yield return bot.Wait(1f);
        }

        IEnumerator PickUpTheBoulderFrom(Vector3 stand, Vector3? aim)
        {
            yield return bot.WalkTo(new Vector3(-1.9f, 0f, -1.4f), 0.3f, 8f);
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return bot.WalkTo(BobbinFoot, 0.15f, 8f);
            yield return bot.WalkTo(WestOfTheRuler, 0.25f, 8f);
            yield return bot.WalkTo(stand, 0.25f, 8f);
            yield return bot.Grab(level.Pebble);
            yield return bot.Until(() => level.Seesaw.State == SeesawState.Rest, 5f);
            yield return bot.WalkTo(WestOfTheRuler, 0.25f, 8f);
            yield return bot.WalkTo(BobbinFoot, 0.15f, 8f);
            yield return bot.WalkTo(Corner, 0.2f, 8f);
            yield return level.Mount(bot);
            if (aim.HasValue) yield return bot.DropAt(aim.Value);
            else yield return LetGoAt(0f, 35f);
        }

        // Found by the review: a boulder picked up again from near by looks so big that, held toward the far wall
        // from the bullseye, it meets the player's own end of the ruler (or its largest size) first. The level
        // used to answer "hold the pebble against the far wall" - which is what the player had done.
        [TestCase(false)]
        [TestCase(true)]
        public void TheBoulderPickedUpAgainFromTooNear_IsCalledOut_AndGoesHome(bool aimHigh)
        {
            Load();
            BotRunner.Run(Game, ThrowFromTheBook(bot, level), 20f);
            Assert.Greater(level.Pebble.Scale, 4f);
            List<string> said = Listen();
            float ratio = 0f, dropped = 0f;
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, PickUpTheBoulderFrom(new Vector3(-5f, 0f, 8f), aimHigh ? (Vector3?)null : Level07TeeterTotter.OutlineCentre), 60f);
            Assert.Greater(ratio, Level07TeeterTotter.LooksTooBig + 0.05f, "from a few steps away the boulder fills the view");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.NearLine }, said, "said at the pick-up");
            Assert.IsFalse(Drive(level.Fly(bot), 5f), "nothing is thrown");
            RunSeconds(1f);
            Assert.IsTrue(PebbleIsHome, "it came down on the player's own end: back to the bobbin");
            // (Held at the outline it reaches its largest size over the marker; held high it meets the spines beside the player.)
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.NearLine, Level07TeeterTotter.TooBigLine }, said);
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);

            // The player is still on the bullseye and the pebble is an arm's length away at its own size.
            said.Clear();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("picked up from the bullseye it looks right again");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
        }

        // On its bobbin the pebble never looks too big, and a boulder picked up from the ruler's raised end or from
        // the shelf at its foot does not either: the near line is for the middle of the shelf only.
        [Test]
        public void TheNearLine_IsNotSaidForAnyPickUpThatWorks()
        {
            Load();
            List<string> said = Listen();
            Drive(bot.WalkTo(BobbinFoot, 0.04f, 3f), 4f);
            Drive(bot.WalkTo(Level07TeeterTotter.BobbinBase, 0.04f, 3f), 4f);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            Assert.Less(Game.Grabber.Ratio, Level07TeeterTotter.LooksTooBig - 0.05f, "as near as anyone gets to the bobbin");
            CollectionAssert.IsEmpty(said);
        }

        // Found by the review (a consequence of the spines standing against the ruler): held from right beside the
        // books the pebble catches on their spines at arm's length.
        [Test]
        public void HeldFromRightBesideTheBooks_ItCatchesOnTheirSpines_AndTheLevelSaysSo()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            Drive(bot.WalkTo(level.SeatPoint + Vector3.right * 1.5f, 0.05f, 2f), 2.5f);
            Assert.That(Game.Player.Position.x, Is.InRange(1.3f, 1.42f), "against the spines, both feet on the ruler");
            Assert.IsTrue(GadgetKit.PlayerStandsOn(Game, level.Seesaw.Mover.Body));
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            Assert.Less(dropped, 1.5f, "it stopped on the books an arm's length away");
            RunSeconds(2.5f);
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.BooksLine }, said);

            // From the middle of the bullseye the way to the far wall is clear.
            BotRunner.Run(Game, bot.WalkTo(level.SeatPoint, 0.12f, 5f), 6f);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("from the middle of the bullseye");
        }

        static IEnumerator ThrowAtTheBooks(Bot bot, Level07TeeterTotter level, Vector3 aim)
        {
            yield return bot.Grab(level.Pebble);
            yield return bot.WalkTo(new Vector3(-3.2f, 0f, 2f), 0.25f, 8f);
            yield return bot.WalkTo(new Vector3(-8.5f, 0f, 6f), 0.25f, 8f);
            yield return bot.DropAt(aim);
        }

        // The top of the books cannot be reached with the pebble from the shelf: whatever is held toward it stops
        // on the spines (first touch wins), comes down on the ruler and goes home.
        [TestCase(9.9f, 14f, 0f)]
        [TestCase(9.9f, 16f, -3f)]
        [TestCase(6f, 19.9f, 0f)]
        public void ThrownAtTheTopOfTheBooksFromAcrossTheShelf_ThePebbleStopsOnTheirSpines_AndComesBack(float x, float y, float z)
        {
            Load();
            List<string> said = Listen();
            float highest = 0f;
            BotRunner.Run(Game, ThrowAtTheBooks(bot, level, new Vector3(x, y, z)), 30f);
            Vector3 at = level.Pebble.Center;
            Assert.Less(at.x, Level07TeeterTotter.TowerMinX - level.Pebble.Scale * 0.4f, "stopped on this side of the spines (at " + at + ", " + level.Pebble.Scale + " across)");
            for (int i = 0; i < TestHelpers.Ticks(3.5f); i++)
            {
                Game.Tick();
                if (!level.Pebble.Frozen && level.Pebble.Center.x > Level07TeeterTotter.TowerMinX) highest = Mathf.Max(highest, level.Pebble.Center.y);
            }
            Assert.AreEqual(0f, highest, "it was never over the books");
            Assert.IsTrue(PebbleIsHome);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.BooksLine }, said);
            Assert.IsFalse(Game.LevelCompleted);
        }

        static IEnumerator PickUpAndLetGoAtOnce(Bot bot, Level07TeeterTotter level)
        {
            yield return bot.Grab(level.Pebble);
            yield return bot.Click();
        }

        // After a flight without steering the player stands on the ruler's raised end with the boulder in view.
        // A pick-up and a second click right away (a double click) puts the boulder back while the ruler has
        // only just begun to come back: next to no swing, nobody is thrown. (Let go a breath later - the boulder
        // falls for half a second - the ruler is down again in time and the throw is the full one.)
        [Test]
        public void LetGoAgainBeforeTheRulerIsBackDown_IsToldToWait()
        {
            Load();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, FlyWithoutSteering(bot, level), 20f);
            RunSeconds(1f);
            List<string> said = Listen();
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            BotRunner.Run(Game, PickUpAndLetGoAtOnce(bot, level), 10f);
            RunSeconds(3f);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.EarlyLine }, said);
            Assert.Less(apex, Level07TeeterTotter.TowerTop - 1.5f, "a hop off the raised end, not a throw (apex " + apex + ")");
            Assert.IsFalse(Game.LevelCompleted);
            SolveWithWhatIsThere("after a let-go that came too early");
        }

        // The same a little later: picked up from the raised end and held against the outline at once. The ruler
        // comes down with the player while the boulder falls, and the throw is as good as the first.
        [Test]
        public void PickedUpFromTheRaisedEndAndLetGoWithoutWaiting_TheThrowIsAFullOne()
        {
            Load();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, FlyWithoutSteering(bot, level), 20f);
            RunSeconds(1f);
            List<string> said = Listen();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("let go again without waiting for the ruler");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
        }

        // Found by the review: the leash allowed anything on the bobbin's top, so a pebble let go there as a crumb
        // stayed a crumb - and from the bobbin a crumb can never be made heavy enough.
        [Test]
        public void LetGoOnItsOwnBobbinAtAnotherSize_ThePebbleIsPutBackAtItsOwn()
        {
            Load();
            List<string> said = Listen();
            Prop pebble = level.Pebble;
            pebble.Unfreeze();
            pebble.SetScale(Level07TeeterTotter.MinScale);
            pebble.SetPose(Level07TeeterTotter.BobbinBase + Vector3.up * (Level07TeeterTotter.BobbinHeight + 0.11f), Quaternion.identity);
            RunSeconds(1f);
            Assert.IsFalse(PebbleIsHome);
            Assert.Less(Vector3.Distance(pebble.Center, Perch), 0.3f, "it lies on the bobbin (at " + pebble.Center + ")");
            RunSeconds(1f);
            Assert.IsTrue(PebbleIsHome, "at its own size again, and frozen");
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.BackLine }, said);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // Under the high end when the boulder comes down: the ruler and the boulder pass through the player, who
        // walks out from under them; the boulder stays and is used again.
        [TestCase(0f, 13f)]
        [TestCase(0.8f, 14.3f)]
        [TestCase(-1f, 11.8f)]
        public void StandingUnderTheHighEndWhenTheBoulderLands_ThePlayerWalksOut(float x, float z)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-1.9f, 0f, -1.4f), 0.3f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(Corner, 0.2f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(BobbinFoot, 0.15f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(WestOfTheRuler, 0.25f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-3.5f, 0f, z), 0.25f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(x, 0f, z), 0.25f, 8f), 10f);
            BoulderOverTheHighEnd(4.5f);
            RunSeconds(3f);
            Assert.AreEqual(SeesawState.Tipped, level.Seesaw.State);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.NobodyLine }, said);
            Assert.AreEqual(4.5f, level.Pebble.Scale, 1e-3f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-3.5f, 0f, z), 0.3f, 6f), 8f);
            Assert.Less(Game.Player.Position.x, -3f, "out from under the ruler and the boulder");
            BotRunner.Run(Game, bot.WalkTo(WestOfTheRuler, 0.25f, 8f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.Until(() => level.Seesaw.State == SeesawState.Rest, 5f), 6f);
            BotRunner.Run(Game, bot.WalkTo(BobbinFoot, 0.15f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(Corner, 0.2f, 8f), 10f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("the boulder used again from the bullseye");
        }

        // Found by the review: the seesaw struck a second time when the boulder came to rest on the tipped ruler,
        // so the sound and the shake could come twice. One let-go is one strike on the game's channel - with the
        // solver's boulder and with the smallest and the biggest that work.
        [TestCase(0f)]
        [TestCase(3.05f)]
        [TestCase(7f)]
        public void OneLetGo_IsOneStrike_AndOneThrow(float boulder)
        {
            Load();
            int strikes = 0, throws = 0;
            Game.Events.SeesawStruck += e => strikes++;
            Game.Events.SeesawLaunched += e => throws++;
            Assert.IsTrue(ByHand(0f, 1f, 0f, false, -1f, boulder, 9f), "on the books (ended at " + Game.Player.Position + ")");
            Assert.AreEqual(1, strikes, "strikes heard");
            Assert.AreEqual(1, throws, "throws");
            Assert.AreEqual(0, level.Leash.Returns, "the boulder stays where it did its job");
        }

        // The same jump while leaning on the spines (the capsule's axis is a tenth of a unit inside the ruler's
        // edge there): it used to end on the shelf under the ruler's raised end. Now it is a throw like any other.
        [Test]
        public void AJumpWhileLeaningOnTheBooks_IsStillThrownOntoThem()
        {
            Load();
            List<string> said = Listen();
            Assert.IsTrue(ByHand(0f, 1f, 0f, false, 0.05f), "ended at " + Game.Player.Position);
            Assert.IsTrue(OnTheBooks);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said);
        }

        // The boulder picked up again while the ruler swings: nothing drives the ruler any more. The player gets
        // the little hop the ruler had in it, the ruler swings back, and nothing is said about a try that was
        // called off - the boulder is in the hand, to be let go again.
        [Test]
        public void TheBoulderPickedUpAgainInMidSwing_CallsTheThrowOff_AndNothingIsSaid()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Swing, 3f), "the boulder lands and the ruler swings");
            RunSeconds(0.15f);
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            BotRunner.Run(Game, bot.Grab(level.Pebble), 2f);
            Assert.IsTrue(level.Pebble.Held, "picked up again in mid-swing");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Seesaw.State == SeesawState.Rest && Game.Player.Grounded, 6f), "the ruler swings back (at " + level.Seesaw.Angle + ")");
            RunSeconds(1f);
            Assert.Less(apex, Level07TeeterTotter.TowerTop - 2f, "no throw to speak of (" + apex + ")");
            Assert.IsFalse(OnTheBooks);
            CollectionAssert.AreEqual(new[] { Level07TeeterTotter.FlyLine }, said, "nothing is said about it");
            Assert.AreEqual(0, level.Leash.Returns);

            // And the try can be made again with the boulder that is in the hand.
            IEnumerator Again()
            {
                yield return level.Mount(bot);
                yield return bot.DropAt(Level07TeeterTotter.OutlineCentre);
                yield return level.Fly(bot);
            }
            BotRunner.Run(Game, Again(), 60f);
            AssertCompleted("the boulder let go again");
        }

        // The floor behind the far end of the books is reached by stepping off the ruler's edge. It is not a trap:
        // the way back leads under the ruler's high end (the low end lies on the shelf from the books to the marker).
        [Test]
        public void BehindTheBooks_IsNotATrap_TheWayBackLeadsUnderTheHighEnd()
        {
            Load();
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 7f), 0.15f, 8f), 10f);
            Drive(bot.WalkTo(new Vector3(3.5f, 0f, 7f), 0.3f, 4f), 5f);
            RunSeconds(0.5f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Greater(Game.Player.Position.x, 1.8f, "off the ruler on the books' side (at " + Game.Player.Position + ")");
            Assert.Less(Game.Player.Position.y, 0.05f);
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);

            BotRunner.Run(Game, bot.WalkTo(new Vector3(4f, 0f, 13f), 0.3f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-4f, 0f, 13f), 0.3f, 8f), 10f);
            Assert.Less(Game.Player.Position.x, -3.5f, "under the high end to the other side");
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State, "the ruler did not mind");
            BotRunner.Run(Game, bot.WalkTo(WestOfTheRuler, 0.25f, 8f), 10f);
            BotRunner.Run(Game, bot.WalkTo(BobbinFoot, 0.08f, 8f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, bot.WalkTo(Corner, 0.2f, 8f), 10f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("solved after a walk round the back of the books");
        }

        // Nothing on the shelf is a step up the books: the tallest thing a player can stand on, and a jump from it.
        [Test]
        public void NothingOnTheShelfIsAStepUpTheBooks()
        {
            Load();
            RunSeconds(0.1f);
            float tallest = 0f;
            string which = "";
            foreach (Collider collider in Game.LevelRoot.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || collider.gameObject.layer != Layers.Default) continue;
                Bounds bounds = collider.bounds;
                // The walls, the sky cap and the books themselves are not steps.
                if (bounds.max.y >= Level07TeeterTotter.TowerTop - 0.01f) continue;
                if (bounds.max.y > tallest)
                {
                    tallest = bounds.max.y;
                    which = collider.name;
                }
            }
            Assert.Greater(tallest, 2f, "the set pieces were found (" + which + ")");
            Assert.Less(tallest + 1.3f, Level07TeeterTotter.TowerTop - 4f, "the tallest step is " + which + " at " + tallest);
            // The ruler's raised end, and the top of the biggest boulder lying on the high end: no step either.
            float raisedEnd = level.Seesaw.PointAt(Level07TeeterTotter.LongArm, Level07TeeterTotter.RulerThickness, level.Seesaw.TippedAngle).y;
            Assert.Less(raisedEnd + 1.3f, Level07TeeterTotter.TowerTop - 4f);
            Assert.Less(Level07TeeterTotter.MaxScale + Level07TeeterTotter.RulerThickness + 1.3f, Level07TeeterTotter.TowerTop - 2f);
            // And nothing but the pebble moves when it is pushed: every other prop is fixed or driven by the level.
            foreach (Prop prop in Game.Props)
                if (prop != level.Pebble) Assert.AreNotEqual(PropBody.Dynamic, prop.BodyKind, prop.Name + " could be pushed about");
        }

        // ---- Robustness ------------------------------------------------------------------------------------------

        // "Cover the outline with it": let go with the crosshair anywhere in the middle of the outline, not just
        // at its centre (the outline is 2.3 in radius; these are up to 1.4 off).
        [TestCase(0f, 0f)]
        [TestCase(1.4f, 0f)]
        [TestCase(-1.4f, 0f)]
        [TestCase(0f, 1.4f)]
        [TestCase(0f, -1.4f)]
        [TestCase(1f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(-1f, -1f)]
        public void LetGoAnywhereInTheMiddleOfTheOutline_ItWorks(float dx, float dy)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre + new Vector3(dx, dy, 0f)), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted("let go " + dx + ", " + dy + " off the outline's centre (boulder " + dropped + ")");
            Assert.That(dropped, Is.InRange(4.3f, 5f), "about the size of the outline");
        }

        // The same with the biggest and the smallest pebble a sensible pick-up gives: from the bobbin's foot it
        // looks half again as big as from the start, from the bullseye a fifth smaller.
        [TestCase(true, 1.4f, 0f)]
        [TestCase(true, -1.4f, 0f)]
        [TestCase(true, 0f, 1.4f)]
        [TestCase(true, 0f, -1.4f)]
        [TestCase(false, 1.4f, 0f)]
        [TestCase(false, -1.4f, 0f)]
        [TestCase(false, 0f, 1.4f)]
        [TestCase(false, 0f, -1.4f)]
        public void LetGoOffCentre_WithTheNearestAndTheFarthestSensiblePickUp_ItWorks(bool nearest, float dx, float dy)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            if (nearest)
            {
                Drive(bot.WalkTo(BobbinFoot, 0.04f, 3f), 4f);
                Drive(bot.WalkTo(Level07TeeterTotter.BobbinBase, 0.04f, 3f), 4f);
                BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
                BotRunner.Run(Game, bot.WalkTo(BobbinFoot, 0.15f, 5f), 8f);
                BotRunner.Run(Game, bot.WalkTo(Corner, 0.2f, 5f), 8f);
                BotRunner.Run(Game, level.Mount(bot), 20f);
            }
            else
            {
                BotRunner.Run(Game, level.Mount(bot), 20f);
                BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            }
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre + new Vector3(dx, dy, 0f)), 10f);
            BotRunner.Run(Game, level.Fly(bot), 20f);
            AssertCompleted((nearest ? "nearest" : "farthest") + " pick-up (looks " + Game.Grabber.Ratio + "), let go " + dx + ", " + dy + " off centre (boulder " + dropped + ")");
        }

        [Test]
        public void TheSolver_FiveTimesInARow_AndAcrossRestarts_TakesTheSameTicks()
        {
            int ticks = 0;
            for (int run = 0; run < 5; run++)
            {
                Load();
                TestHelpers.PlayLevel(Game, 60f);
                if (run == 0) ticks = Game.LevelTicks;
                Assert.AreEqual(ticks, Game.LevelTicks, "fresh game, run " + run);
                // And again in the same game, twice, each time after the restart key's doing.
                for (int again = 0; again < 2; again++)
                {
                    Game.RestartLevel();
                    Assert.IsFalse(Game.LevelCompleted);
                    Assert.IsTrue(PebbleIsHome);
                    TestHelpers.PlayLevel(Game, 60f);
                    Assert.AreEqual(ticks, Game.LevelTicks, "run " + run + ", after restart " + again);
                }
                Unload();
            }
            Assert.That(ticks * Sim.Dt, Is.InRange(3f, 5f), "the solution takes about four seconds");
        }

        [Test]
        public void RestartingWithThePebbleInHand_OrInMidAir_LeavesNothingBehind()
        {
            Load();
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            Assert.AreSame(level.Pebble, Game.Grabber.Held);
            Game.RestartLevel();
            Assert.IsNull(Game.Grabber.Held, "nothing in hand after a restart");
            Assert.IsTrue(PebbleIsHome);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level07TeeterTotter.SpawnPoint), 0.05f);

            // Thrown, and the restart key at the top of the flight.
            bot = new Bot(Game);
            BotRunner.Run(Game, bot.Grab(level.Pebble), 5f);
            BotRunner.Run(Game, level.Mount(bot), 20f);
            BotRunner.Run(Game, bot.DropAt(Level07TeeterTotter.OutlineCentre), 10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Position.y > 12f, 4f), "in the air");
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            List<string> said = Listen();
            Assert.IsTrue(PebbleIsHome);
            Assert.AreEqual(SeesawState.Rest, level.Seesaw.State);
            RunSeconds(4f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level07TeeterTotter.SpawnPoint), 0.05f, "the old flight does not carry over");
            CollectionAssert.IsEmpty(said, "and nothing of the old try is commented on");
            Assert.AreEqual(0, level.Leash.Returns);
            TestHelpers.PlayLevel(Game, 60f);
        }

        // ---- Words -----------------------------------------------------------------------------------------------

        [Test]
        public void TheWordsOfTheLevel_ArePlain_AndTheHintsEscalate()
        {
            Load();
            var lines = new List<string>();
            foreach (FieldInfo field in typeof(Level07TeeterTotter).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text != null && text.Contains(" ")) lines.Add(text);
            }
            Assert.GreaterOrEqual(lines.Count, 10, "the level's lines were found");
            var all = new List<string>(lines) { level.Blurb };
            all.AddRange(level.Hints);
            foreach (string text in all)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "plain characters only in: " + text);
                Assert.IsTrue(text.EndsWith(".") || text.EndsWith("!") || text.EndsWith("?"), "a sentence: " + text);
                Assert.AreEqual(text.Trim(), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("grab") || lower.Contains("take it") || lower.Contains("taken"), "the levels say \"pick up\": " + text);
                Assert.IsFalse(lower.Contains("drop") || lower.Contains("release"), "the levels say \"let go\": " + text);
            }
            foreach (string line in lines) Assert.LessOrEqual(line.Length, 95, "too long for a toast: " + line);
            Assert.LessOrEqual(level.Blurb.Length, 60, "the blurb fits the level card");

            string first = level.Hints[0], second = level.Hints[1], third = level.Hints[2];
            foreach (string word in new[] { "pebble", "bullseye", "outline", "wall", "books" })
                StringAssert.DoesNotContain(word, first, "the first hint does not give the solution away");
            StringAssert.Contains("bullseye", second);
            StringAssert.Contains("pebble", second);
            StringAssert.DoesNotContain("outline", second, "where exactly to hold it is the third hint's");
            StringAssert.DoesNotContain("let go", second);
            StringAssert.Contains("Pick the pebble up", third, "the third hint is the whole solution, the pick-up included");
            StringAssert.Contains("bullseye", third);
            StringAssert.Contains("outline", third);
            StringAssert.Contains("let go", third);
            StringAssert.Contains("push toward", third, "and the flight");
        }
    }
}
