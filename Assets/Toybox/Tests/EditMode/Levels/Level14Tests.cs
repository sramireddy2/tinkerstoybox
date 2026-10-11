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
using Toybox.Platform;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 14, "The Infinite Hallway": the toy resizes you. Three gates and no size that passes two of them
    /// in a row: the mouse-hole (a player of 0.45 or less), the ledge (2.5 or more) and the way out (less
    /// than 2). The door is put down three times and walked through three times: small, big, medium
    /// (LEVELS appendix B: 0.28 / 3.2 / 1.35; works from 0.1 to 0.45, from 2.5 to 3.2, from 0.1 to below 2).
    ///
    /// Measured tables: tools/out/notes/level14-probe-*.txt (the explicit Probe tests at the end write them).
    /// Where a test puts the player or the door somewhere by hand it says "fixture": a bot may not.
    /// </summary>
    public class Level14Tests : SimTest
    {
        /// <summary>The floor of the ledge, and the top of the kerb along its edge (the height of its face).</summary>
        const float Ledge = Level14InfiniteHallway.TerraceTop, Face = Level14InfiniteHallway.LedgeFace;
        static readonly Vector3 StandA = Level14InfiniteHallway.StandA, StandB = Level14InfiniteHallway.StandB, StandT = Level14InfiniteHallway.StandT;

        Level14InfiniteHallway Load()
        {
            Game = Game.Create();
            Game.LoadLevel(14);
            return (Level14InfiniteHallway)Game.Level;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

        static void Write(string name, StringBuilder text)
        {
            string path = Path.Combine(Application.dataPath, "..", "tools", "out", "notes", name);
            File.WriteAllText(path, text.ToString());
            Debug.Log("[Toybox] wrote " + path);
        }

        /// <summary>
        /// The longest a measuring test may keep the editor, in seconds of the clock on the wall: the editor is
        /// shared, and a sweep of several hundred games that has gone slow must not hold it for half an hour.
        /// </summary>
        const float ProbeBudget = 100f;

        // Called before every step of a sweep. Leaves a note of the step that is about to run (so that a step
        // that never comes back can be named afterwards) and ends the sweep, with what it has so far written
        // down, when it has used up its time.
        void Budget(System.Diagnostics.Stopwatch clock, string name, StringBuilder soFar, string step)
        {
            string folder = Path.Combine(Application.dataPath, "..", "tools", "out", "notes");
            File.WriteAllText(Path.Combine(folder, "level14-probe-progress.txt"), name + ": " + step + " at " + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\n");
            if (clock.Elapsed.TotalSeconds < ProbeBudget) return;
            soFar.AppendLine();
            soFar.AppendLine("STOPPED after " + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s, before: " + step);
            Write(name, soFar);
            DisposeGame();
            Assert.Inconclusive("the sweep used up its " + ProbeBudget + " s (stopped before " + step + "); what it had is in " + name);
        }

        string Where(Level14InfiniteHallway level) =>
            "bot " + F(Game.Player.Scale) + " at " + F(Game.Player.Position) + ", door " + F(level.Door.Scale) + " at " + F(level.Door.Position);

        // Runs a script that may give up half way (a gate that does not let the bot through leaves it
        // walking at a wall until its command times out). True if it ran to its end.
        bool Runs(IEnumerator script, float timeoutSeconds = 60f)
        {
            try
            {
                BotRunner.Run(Game, script, timeoutSeconds);
                return true;
            }
            catch (BotException e)
            {
                Debug.Log("[Toybox] the bot gave up: " + e.Message);
                return false;
            }
        }

        // The same, and says whether the level was completed.
        bool Attempt(IEnumerator script, float timeoutSeconds = 90f)
        {
            Runs(script, timeoutSeconds);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            return Game.LevelCompleted;
        }

        // ---- Scripts (input only) -----------------------------------------------------------------------------

        // The door picked up from straight in front of where it stands at the start, so many units away, and
        // let go on the spot looking down the hall at a pitch.
        static IEnumerator PickUpAndLetGo(Bot bot, Level14InfiniteHallway level, float from, float pitch)
        {
            Vector3 start = Level14InfiniteHallway.DoorStart;
            yield return bot.WalkTo(new Vector3(start.x, 0f, start.z - from), 0.03f, 10f);
            yield return bot.Grab(level.Door);
            yield return bot.DropAt(bot.Player.Eye + Level14InfiniteHallway.View(0f, pitch) * 2f);
            yield return bot.Until(() => level.Portal.Settled, 3f);
        }

        // Up to the mouse-hole and through it into the second hall - if it lets the bot.
        static IEnumerator ThroughTheHole(Bot bot)
        {
            float size = bot.Player.Scale;
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.4f - 0.5f * size), 0.05f, 20f, true);
            yield return bot.LookAt(new Vector3(0f, bot.Player.EyeHeight, Level14InfiniteHallway.HallBZ + 3f));
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.HallBZ + 1f), 0.1f, 4f, true);
        }

        // Lets go of the door in the hand looking that way, and waits for it to stand.
        static IEnumerator LetGo(Bot bot, Level14InfiniteHallway level, float yaw, float pitch)
        {
            yield return bot.DropAt(bot.Player.Eye + Level14InfiniteHallway.View(yaw, pitch) * 5f);
            yield return bot.Until(() => level.Portal.Settled, 4f);
        }

        // Picks the door up from where the bot stands and lets go of it looking that way.
        static IEnumerator PickUpThenLetGo(Bot bot, Level14InfiniteHallway level, float yaw, float pitch)
        {
            yield return bot.Grab(level.Door);
            yield return LetGo(bot, level, yaw, pitch);
        }

        // The mouse-sized bot beside the little door: picks it up from so far beyond it, carries it through
        // the mouse-hole to the shoe prints of the second hall and lets go of it there looking up.
        static IEnumerator CarryThroughAndLetGo(Bot bot, Level14InfiniteHallway level, float from, float pitch, int turns = 0)
        {
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * from, 0.03f, 6f);
            yield return bot.Grab(level.Door);
            if (turns != 0) yield return bot.RotateHeld(turns);
            yield return bot.LookAt(new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ));
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.5f), 0.05f, 15f, true);
            yield return bot.WalkTo(StandB, 0.05f, 15f, true);
            yield return LetGo(bot, level, 0f, pitch);
        }

        // ---- What the level is ------------------------------------------------------------------------------

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(14));
            Level14InfiniteHallway level = Load();
            Assert.AreEqual("infinite-hallway", level.Slug);
            Assert.AreEqual("The Infinite Hallway", level.Title);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("night-light", level.Environment);
            Assert.AreEqual(-Level14InfiniteHallway.FloorThickness, level.GroundY, "the hallway stands on its own base board (LEVELS: 0)");
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.AreEqual("This door fits everyone. That's the problem, and the answer.", level.Blurb, "LEVELS: the blurb");

            // The door: the catalog's doorway with LEVELS' options, standing in the middle of the first hall.
            Prop door = level.Door;
            Assert.AreSame(ToyCatalog.HeroOf(14), ToyCatalog.Get(ToyId.Doorway));
            Assert.AreEqual("Doorway", door.Name);
            Assert.AreEqual(PropBody.Fixed, door.BodyKind);
            Assert.IsTrue(door.Grabbable);
            Assert.AreEqual(1f, door.Scale);
            Assert.AreEqual(0.1f, door.MinScale);
            Assert.AreEqual(3.2f, door.MaxScale);
            Assert.IsFalse(door.AllowPitch, "it cannot be laid flat");
            Assert.IsTrue(door.SquareOnGrab, "in the hand it is square to the view");
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 8f), door.Position), 1e-4f, "LEVELS: standing at (0, 0, 8)");
            Assert.Less(Vector3.Distance(Vector3.back, door.Rotation * Vector3.forward), 1e-4f, "facing the start");
            foreach (Collider collider in door.Colliders)
                Assert.AreEqual(Layers.PlayerMask, collider.excludeLayers.value & Layers.PlayerMask, collider.name + " never touches the player");

            // The gadgets.
            PortalDoorway portal = level.Portal;
            Assert.AreSame(door, portal.Prop);
            Assert.AreEqual(1f, portal.TargetScale);
            Assert.AreEqual(new Vector3(-3.5f, 0f, 1.5f), level.RecallA.Position);
            Assert.AreEqual(new Vector3(-3.5f, 0f, 16.5f), level.RecallB.Position);
            Assert.AreEqual(0.6f, level.RecallA.Radius);
            Assert.AreEqual(0.6f, level.RecallB.Radius);

            // The way out: in the tunnel at the back of the ledge, wholly inside it.
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreSame(level.Exit, Game.Exits[0]);
            Assert.AreEqual(new Vector3(0f, Ledge + 1.5f, 33.8f), level.Exit.Position);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Greater(level.Exit.Position.z - Level14InfiniteHallway.ExitSize.z * 0.5f, Level14InfiniteHallway.TunnelZ + 0.75f, "the exit begins 0.8 inside the tunnel: a giant at the end wall cannot touch it");

            // The gates (LEVELS: 0.45, 2.5, 2.0), and no size that passes the ledge and the way out.
            Assert.AreEqual(0.45f, Level14InfiniteHallway.MouseScale);
            Assert.AreEqual(2.5f, Level14InfiniteHallway.LedgeScale);
            Assert.AreEqual(2f, Level14InfiniteHallway.ExitScale);
            Assert.Greater(Level14InfiniteHallway.LedgeLimit, Level14InfiniteHallway.ExitLimit + 0.4f, "whoever can jump the ledge cannot get out, and the other way round");
            Assert.AreEqual(Level14InfiniteHallway.HoleHeight, Player.BaseHeight * Level14InfiniteHallway.MouseLimit, 0.02f, "the mouse-hole is as high as the tallest player it lets in");
            Assert.AreEqual(Level14InfiniteHallway.ExitHeight, Player.BaseHeight * Level14InfiniteHallway.ExitLimit, 0.03f, "and so is the way out");

            // The player: at the start of the hall, their own size, on the floor; the door is a portal already.
            RunSeconds(1f);
            Player player = Game.Player;
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.Spawn, player.Position), 0.02f, "LEVELS: spawn (0, 0, 2)");
            Assert.AreEqual(1f, player.Scale);
            Assert.IsTrue(player.Grounded);
            Assert.IsTrue(portal.Settled);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 8f), door.Position), 1e-3f, "the door stays where it stands");

            // What the door makes of the player is its own size, within the player's limits.
            door.SetScale(0.1f);
            Assert.AreEqual(0.3f, portal.TargetScale, 1e-5f, "LEVELS: the player clamp, 0.3");
            door.SetScale(3.2f);
            Assert.AreEqual(3.2f, portal.TargetScale, 1e-5f);
        }

        [Test]
        public void TheBotCompletesTheLevel_SmallBigMedium()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var looked = new List<float>();
            var letGo = new List<float>();
            var trips = new List<float>();
            Game.Events.PropGrabbed += e => looked.Add(e.OldScale / e.GrabDistance);
            Game.Events.PropDropped += e => letGo.Add(e.NewScale);
            level.Portal.Crossed += (from, to) => trips.Add(to);
            TestHelpers.PlayLevel(Game, 90f);

            Assert.AreEqual(3, letGo.Count, "the door is put down three times");
            Assert.AreEqual(0.28f, letGo[0], 0.28f * 0.05f, "LEVELS appendix B: small, 0.28 within 5 %");
            Assert.AreEqual(3.2f, letGo[1], 1e-3f, "big: against nothing but its own limit");
            Assert.AreEqual(1.35f, letGo[2], 1.35f * 0.05f, "medium, 1.35 within 5 %");
            Assert.AreEqual(3, trips.Count, "and walked through three times");
            Assert.AreEqual(0.3f, trips[0], 1e-4f, "a door of 0.28 makes a player of 0.3: as small as they get");
            Assert.AreEqual(3.2f, trips[1], 1e-4f);
            Assert.AreEqual(letGo[2], trips[2], 1e-4f);
            Assert.AreEqual(3, level.Portal.Crossings);
            Assert.AreEqual(1f / 6f, looked[0], 0.01f, "picked up from the start, six units off");
            Assert.AreEqual(0.65f, looked[1], 0.08f, "LEVELS: the mouse picks it up from 0.43 away, k 0.65");
            Assert.AreEqual(0.36f, looked[2], 0.04f, "LEVELS: from the ledge, k 0.36");

            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine }, said, "the intended solution is told one thing: how to see past the door while carrying it");
            Assert.AreEqual(Level14InfiniteHallway.Place.Tunnel, Level14InfiniteHallway.PlaceOf(Game.Player.Position), "the level ends in the way out");
            Assert.Less(Game.Time, 30f, "the solution takes well under a minute");
        }

        // ---- The first picture ------------------------------------------------------------------------------

        // Where a point is in the picture: x and y from -1 (left, bottom) to 1 (right, top); z is how far ahead.
        static Vector3 OnScreen(Player player, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(player.LookRotation) * (point - player.Eye);
            float tanV = Mathf.Tan(35f * Mathf.Deg2Rad), tanH = tanV * 16f / 9f;
            return new Vector3(local.x / local.z / tanH, local.y / local.z / tanV, local.z);
        }

        // The level card lies over the top left of the picture for the first seconds.
        static bool UnderTheCard(Vector3 screen) => screen.x < -0.2f && screen.y > 0.5f;

        [Test]
        public void TheStartShowsTheDoor_TheMouseHoleThroughIt_AndWhereTheDoorGoes()
        {
            Level14InfiniteHallway level = Load();
            RunSeconds(0.2f);
            Player player = Game.Player;
            Vector3 eye = player.Eye;
            Prop door = level.Door;

            // The door under the crosshair, though its middle is a hole: the first click is the right one.
            Assert.AreSame(door, Game.Grabber.FindTarget(), "a click at the start picks the door up");
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, player.Forward, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(door, PropRef.Of(hit.collider), "the crosshair is on the door");
            Assert.AreEqual(6f, hit.distance, 0.2f, "six units off: it looks small from here");

            // All of it in the picture, clear of the level card.
            Vector2 half = new Vector2(ToyFactory.DoorwaySize.x, ToyFactory.DoorwaySize.y) * 0.5f;
            foreach (Vector3 corner in new[] { new Vector3(-half.x, 0f, 0f), new Vector3(half.x, 0f, 0f), new Vector3(-half.x, 2f * half.y, 0f), new Vector3(half.x, 2f * half.y, 0f) })
            {
                Vector3 screen = OnScreen(player, door.Position + corner);
                Assert.Less(Mathf.Abs(screen.x), 0.9f, "the door's corner " + corner + " is off the side of the picture");
                Assert.Less(Mathf.Abs(screen.y), 0.9f, "the door's corner " + corner + " is off the top or the bottom of the picture");
                Assert.IsFalse(UnderTheCard(screen), "the door's corner " + corner + " is under the level card");
            }

            // The problem is seen through the door: the mouse-hole in the wall behind it, and the signs above it.
            var hole = new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ);
            foreach (Vector3 point in new[] { hole + Vector3.forward * 0.5f, hole + Vector3.up * 1.25f, hole + Vector3.up * 2.15f })
            {
                Vector3 to = point - eye;
                bool blocked = Game.PhysicsScene.Raycast(eye, to.normalized, out hit, to.magnitude - 0.05f, Layers.DefaultMask, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(blocked, "something stands between the start and " + point + (blocked ? " (" + hit.collider.name + ")" : ""));
                Vector3 screen = OnScreen(player, point);
                Assert.Less(Mathf.Abs(screen.x), 0.1f, point + " is seen through the door");
                Assert.Less(Mathf.Abs(screen.y), 0.3f, point + " is seen through the door");
                // Through its opening, not past it.
                Vector3 atDoor = eye + to * ((door.Position.z - eye.z) / to.z);
                Assert.Less(Mathf.Abs(atDoor.x), ToyFactory.DoorwayOpening.x * 0.5f);
                Assert.Less(atDoor.y, ToyFactory.DoorwayOpening.y);
            }

            // Where to stand and where the door goes: the shoe prints and the dashes, on the floor before the
            // mouse-hole - in the same frame, the door's.
            foreach (Vector3 point in new[] { StandA, Level14InfiniteHallway.FootA })
            {
                Vector3 screen = OnScreen(player, point);
                Assert.Less(Mathf.Abs(screen.x), 0.1f, point + " is seen through the door");
                Assert.Less(Mathf.Abs(screen.y), 0.3f, point + " is seen through the door");
                Vector3 to = point - eye;
                Assert.IsTrue(Game.PhysicsScene.Raycast(eye, to.normalized, out hit, 200f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
                Assert.Less(Vector3.Distance(hit.point, point), 0.1f, "something stands in front of " + point + " (" + hit.collider.name + ")");
                Vector3 atDoor = eye + to * ((door.Position.z - eye.z) / to.z);
                Assert.Less(Mathf.Abs(atDoor.x), ToyFactory.DoorwayOpening.x * 0.5f);
                Assert.Greater(atDoor.y, 0.05f, "over the door's threshold");
            }
            Assert.Less(Level14InfiniteHallway.WallZ - Level14InfiniteHallway.FootA.z, 2f, "the little door comes to stand two steps from the mouse-hole");

            // Paint, paper, marks and lamps are looks only.
            foreach (string name in new[] { "Hallway", "Paper", "Dashes", "Stand Mark A", "Stand Mark B", "Stand Mark T", "Lamps", "Glows" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        // ---- No way round ---------------------------------------------------------------------------------------

        [Test]
        public void WalkingAtTheExit_WithoutTheDoor_DoesNotCompleteTheLevel()
        {
            Level14InfiniteHallway level = Load();
            var bot = new Bot(Game);
            Assert.IsFalse(Attempt(level.GoOut(bot), 30f), "the way out is two gates away");
            Assert.Less(Game.Player.Position.z, Level14InfiniteHallway.WallZ, "the bot is still in the first hall");
            Assert.AreEqual(1f, Game.Player.Scale);
        }

        // Sprints at whatever is ahead for a while, jumping all the time; returns the highest and the farthest the feet got.
        Vector2 Charge(float yaw, float seconds)
        {
            Game.Player.Yaw = yaw;
            Game.Player.Pitch = 0f;
            Input.Hold = new InputFrame { MoveZ = 1f, Sprint = true };
            float highest = float.MinValue, farthest = float.MinValue;
            for (int i = 0; i < TestHelpers.Ticks(seconds); i++)
            {
                if (i % 20 == 0) Input.Once.Jump = true;
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                farthest = Mathf.Max(farthest, Game.Player.Position.z);
            }
            Input.Hold = default;
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
            return new Vector2(highest, farthest);
        }

        [Test]
        public void EveryGate_StopsWhoeverIsTheWrongSize_HoweverTheyRunAndJump()
        {
            LoadScripted();
            Player player = Game.Player;

            // The mouse-hole, at the size the player starts with: straight at it, and into both corners.
            foreach (float x in new[] { 0f, -4f, 4f })
            {
                Stand(new Vector3(x, 0f, 9f), 1f);
                Vector2 got = Charge(x == 0f ? 0f : -Mathf.Sign(x) * 30f, 4f);
                Assert.Less(got.y, Level14InfiniteHallway.WallZ - 0.1f, "nobody of size 1 gets into the dividing wall (from x " + x + "): the feet are 0.3 from its face, less what a sprint presses in for a tick");
                Assert.Less(got.x, 1.4f, "a jump is a jump");
            }
            // A size that all but fits.
            Stand(new Vector3(0f, 0f, 12f), 0.5f);
            Assert.Less(Charge(0f, 4f).y, Level14InfiniteHallway.WallZ, "0.5 is too big for the mouse-hole");

            // The ledge (fixture: in the second hall without having been small): at the starting size, as a mouse,
            // and at the biggest size that still gets out.
            foreach (float size in new[] { 1f, 0.3f, 1.95f, 2.3f })
            {
                Stand(new Vector3(0f, 0f, 20f), size);
                Vector2 got = Charge(0f, 4f);
                Assert.Less(got.x, Face - 0.3f, "a player of " + size + " does not get up the ledge (highest " + got.x + ")");
                Assert.Less(got.y, Level14InfiniteHallway.TerraceZ, "nor into it");
                Stand(new Vector3(-4f, 0f, 20f), size);
                Assert.Less(Charge(20f, 4f).x, Face - 0.3f, "nor in the corner");
            }

            // The way out (fixture: on the ledge): a giant, and the smallest size that can jump the ledge.
            foreach (float size in new[] { 3.2f, 2.5f })
            {
                Stand(new Vector3(0f, Ledge, 27f), size);
                Vector2 got = Charge(0f, 4f);
                Assert.Less(got.y, Level14InfiniteHallway.EndZ, "a player of " + size + " does not get into the way out");
                Stand(new Vector3(-3f, Ledge, 28f), size);
                Charge(40f, 3f);
                Charge(0f, 2f);
                Assert.IsFalse(Game.LevelCompleted, "a player of " + size + " got out");
            }
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(player.Position.y, Ledge - 0.1f, "and nobody falls through the floor trying");
        }

        // ---- The three gates, by the size of the door (LEVELS appendix B) ------------------------------------------------

        // Small: works from 0.1 (the door's least size) to 0.45.
        [TestCase(0.1f, 0.3f, true)]
        [TestCase(0.28f, 0.3f, true)]
        [TestCase(0.45f, 0.45f, true)]
        [TestCase(0.5f, 0.5f, false)]
        [TestCase(0.8f, 0.8f, false)]
        public void TheMouseHole_LetsThroughWhoeverCameOutOfADoorOfUpTo045(float doorScale, float playerScale, bool through)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, 0f, 5f), doorScale);                       // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Through(bot)), "walking into a door of " + doorScale);
            Assert.AreEqual(playerScale, Game.Player.Scale, 1e-4f, "the size of the door, within the player's limits");
            Assert.AreEqual(PortalResult.Through, level.Portal.LastResult);
            bool passed = Runs(ThroughTheHole(bot), 40f);
            Assert.AreEqual(through, passed, "a player of " + playerScale + " and the mouse-hole (" + Where(level) + ")");
            if (through)
            {
                Assert.Greater(Game.Player.Position.z, Level14InfiniteHallway.HallBZ + 0.5f);
                Assert.IsEmpty(said);
            }
            else
            {
                Assert.Less(Game.Player.Position.z, Level14InfiniteHallway.WallZ);
                CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StillBigLine }, said, "the level says why");
            }
            Assert.IsFalse(Game.LevelCompleted);
        }

        // Big: works from 2.5 to 3.2 (the door's largest size).
        [TestCase(3.2f, true)]
        [TestCase(2.5f, true)]
        [TestCase(2.3f, false)]
        [TestCase(1f, false)]
        public void TheLedge_IsJumpedByWhoeverCameOutOfADoorOf25OrMore(float doorScale, bool up)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            Stand(StandB, 0.3f);                                                        // fixture: a mouse in the second hall
            StandDoor(level, Level14InfiniteHallway.FootB, doorScale);                  // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Through(bot)), "walking into a door of " + doorScale);
            Assert.AreEqual(doorScale, Game.Player.Scale, 1e-4f);
            bool climbed = Runs(level.Climb(bot), 40f);
            Assert.AreEqual(up, climbed, "a player of " + doorScale + " and the ledge (" + Where(level) + ")");
            if (up)
            {
                Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f);
                Assert.IsTrue(Game.Player.Grounded);
                Assert.IsEmpty(said);
            }
            else
            {
                Assert.Less(Game.Player.Position.y, 0.5f, "back on the floor of the hall");
                Assert.Less(Game.Player.Position.z, Level14InfiniteHallway.TerraceZ);
                CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StillShortLine }, said, "the level says why");
            }
            // And nobody who is up there this way is out: the way out is for the next size.
            if (up) Assert.IsFalse(Attempt(level.GoOut(bot), 20f), "a player of " + doorScale + " got out");
        }

        // Medium: works from 0.1 (the door's least size) to below 2.
        [TestCase(0.1f, 0.3f, true)]
        [TestCase(1.35f, 1.35f, true)]
        [TestCase(1.95f, 1.95f, true)]
        [TestCase(2.1f, 2.1f, false)]
        [TestCase(2.6f, 2.6f, false)]
        public void TheWayOut_LetsThroughWhoeverCameOutOfADoorSmallerThan2(float doorScale, float playerScale, bool gotOut)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, Ledge, 28.3f), doorScale);                 // fixture: the door on the ledge
            Stand(new Vector3(0f, Ledge, 26.2f), 3.2f);                                 // fixture: a giant in front of it
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Through(bot)), "walking into a door of " + doorScale);
            Assert.AreEqual(playerScale, Game.Player.Scale, 1e-4f);
            Assert.AreEqual(PortalResult.Through, level.Portal.LastResult);
            bool completed = Attempt(level.GoOut(bot), 30f);
            Assert.AreEqual(gotOut, completed, "a player of " + playerScale + " and the way out (" + Where(level) + ")");
            if (gotOut) Assert.IsEmpty(said);
            else
            {
                Assert.Less(Game.Player.Position.z, Level14InfiniteHallway.EndZ);
                CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StillTallLine }, said, "the level says why");
            }
        }

        // ---- The three trips, by where the door is picked up from and where the player looks ---------------------------

        // Small. The door is as small as it looks when it is let go at the feet: from across the hall that is
        // small enough; from close by, or let go looking along the floor, it is not (measured:
        // level14-probe-small.txt).
        [TestCase(6f, -45f, 0.281f, true)]
        [TestCase(6f, -30f, 0.364f, true)]
        [TestCase(5f, -60f, 0.276f, true)]
        [TestCase(7.5f, -60f, 0.199f, true)]
        [TestCase(3f, -75f, 0.371f, true)]
        [TestCase(5f, -20f, 0.522f, false)]
        [TestCase(2f, -60f, 0.514f, false)]
        [TestCase(1f, -89f, 0.671f, false)]
        public void TheFirstTrip_FromAcrossTheHallAtTheFeet_IsSmallEnough_FromCloseByItIsNot(float from, float pitch, float scale, bool through)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpAndLetGo(bot, level, from, pitch)));
            Assert.AreEqual(scale, level.Door.Scale, 0.02f, "the door picked up from " + from + " away and let go at " + pitch);
            float looked = 1f / Mathf.Sqrt(from * from + 0.09f);
            Assert.AreEqual(looked, level.Ratio, looked * 0.03f, "how big it looked: its size over the distance from the eye to its middle");
            Assert.IsTrue(Runs(level.Through(bot)));
            Assert.AreEqual(Mathf.Max(0.3f, level.Door.Scale), Game.Player.Scale, 1e-4f);
            bool passed = Runs(ThroughTheHole(bot), 40f);
            Assert.AreEqual(through, passed, Where(level));
            if (through) Assert.IsEmpty(said, "nothing to tell off");
            else CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StillBigLine }, said);
        }

        // LEVELS: "walk through anyway and do it again from the smaller size. That is the recursion, and it
        // always converges."
        [TestCase(2f, -60f)]
        [TestCase(1f, -89f)]
        [TestCase(5f, -20f)]
        public void ATripThatIsNotSmallEnough_IsMadeAgainFromTheSizeItGave_AndThatOneIs(float from, float pitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpAndLetGo(bot, level, from, pitch)));
            Assert.IsTrue(Runs(level.Through(bot)));
            float first = Game.Player.Scale;
            Assert.Greater(first, Level14InfiniteHallway.MouseLimit);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StillBigLine }, said);

            // Again: a few steps on, round, pick it up, let go at the feet, walk in.
            Assert.IsTrue(Runs(AgainAtTheFeet(bot, level, 2.5f)));
            Assert.Less(Game.Player.Scale, first * 0.7f, "every trip at the feet makes the player smaller");
            Assert.LessOrEqual(Game.Player.Scale, Level14InfiniteHallway.MouseScale, "from " + first + " one more trip is enough");
            Assert.IsTrue(Runs(ThroughTheHole(bot), 40f), Where(level));
            Assert.AreEqual(1, said.Count, "and nothing more is said");
        }

        // So many of the bot's own sizes on from the door, round, pick it up, let go of it at the feet, walk in.
        static IEnumerator AgainAtTheFeet(Bot bot, Level14InfiniteHallway level, float sizesAway)
        {
            Player player = bot.Player;
            Vector3 away = player.Position - level.Door.Position;
            away.y = 0f;
            yield return bot.WalkTo(level.Door.Position + away.normalized * (sizesAway * player.Scale), 0.05f * player.Scale, 8f);
            yield return bot.Grab(level.Door);
            yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(player.Yaw, -60f) * 2f);
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return level.Through(bot);
        }

        // Big. A mouse in the second hall (fixture) with a door ahead that looks so big (fixture: its middle
        // at eye height, so that its size over its distance is k), which the bot picks up and lets go of
        // looking up at a pitch (measured: level14-probe-big.txt). It only ever gets as big as it looks, and
        // one that looks too big keeps its foot on the floor.
        [TestCase(0.6f, 60f, 3.2f, true, null)]
        [TestCase(0.4f, 50f, 3.2f, true, null)]
        [TestCase(0.36f, 89f, 2.6f, true, null)]
        [TestCase(0.8f, 80f, 3.2f, true, null)]
        [TestCase(0.25f, 70f, 2.1f, false, Level14InfiniteHallway.FarLine)]
        [TestCase(0.15f, 89f, 1.33f, false, Level14InfiniteHallway.FarLine)]
        [TestCase(1.2f, 80f, 1.08f, false, Level14InfiniteHallway.NearLine)]
        [TestCase(0.6f, 30f, 1.11f, false, null)]
        public void TheSecondTrip_ADoorThatLooksBigEnough_LetGoLookingUp_IsBigEnough(float k, float pitch, float scale, bool up, string line)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            Stand(StandB, 0.3f);                                                        // fixture
            float doorScale = Game.Player.EyeHeight / 1.25f;
            StandDoor(level, StandB + Vector3.forward * (doorScale / k), doorScale);    // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpThenLetGo(bot, level, 0f, pitch)));
            Assert.AreEqual(k, level.Ratio, k * 0.03f, "how big it looked");
            Assert.AreEqual(scale, level.Door.Scale, scale * 0.06f, "the door that looked " + k + " let go at " + pitch);
            if (line != null) CollectionAssert.AreEqual(new[] { line }, said, "said when it was picked up");
            else Assert.IsEmpty(said);

            Assert.IsTrue(Runs(level.Through(bot)), Where(level));
            Assert.AreEqual(level.Door.Scale, Game.Player.Scale, 1e-4f);
            int expected = line != null ? 1 : 0;
            // Let go so far along the hall that a giant has no room between the door and the ledge (0.4 at
            // 50 degrees): they come back out of the near side, a giant all the same, and are told.
            bool turnedBack = level.Portal.LastResult == PortalResult.TurnedBack;
            if (turnedBack)
            {
                Assert.AreEqual(Level14InfiniteHallway.TurnedLine, said[said.Count - 1]);
                expected++;
            }
            bool climbed = Runs(level.Climb(bot), 40f);
            Assert.AreEqual(up, climbed, Where(level));
            if (!up && !turnedBack)
            {
                Assert.AreEqual(Level14InfiniteHallway.StillShortLine, said[said.Count - 1], "after the trip the level says where that leaves the player");
                expected++;
            }
            Assert.AreEqual(expected, said.Count, string.Join(" | ", said));
        }

        // LEVELS: "walk through that and repeat from the new eye height. Two passes at worst."
        [TestCase(0.6f, 30f)]
        [TestCase(0.25f, 70f)]
        public void ATripThatIsNotBigEnough_IsMadeAgainFromTheSizeItGave_AndThatOneIs(float k, float pitch)
        {
            Level14InfiniteHallway level = Load();
            Stand(StandB, 0.3f);                                                        // fixture
            float doorScale = Game.Player.EyeHeight / 1.25f;
            StandDoor(level, StandB + Vector3.forward * (doorScale / k), doorScale);    // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpThenLetGo(bot, level, 0f, pitch)));
            Assert.IsTrue(Runs(level.Through(bot)));
            float first = Game.Player.Scale;
            Assert.Less(first, Level14InfiniteHallway.LedgeLimit);
            Assert.Greater(first, 0.9f);

            // Again: back through the door (it is the bot's own size now: just a door), a step and a half
            // of its own before it, pick it up, look up the hall and let go.
            Assert.IsTrue(Runs(AgainLookingUp(bot, level)), Where(level));
            Assert.GreaterOrEqual(level.Door.Scale, Level14InfiniteHallway.LedgeScale, "from " + first + " the door grows big enough");
            Assert.AreEqual(level.Door.Scale, Game.Player.Scale, 1e-4f);
            Assert.IsTrue(Runs(level.Climb(bot), 40f), Where(level));
            Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f);
        }

        static IEnumerator AgainLookingUp(Bot bot, Level14InfiniteHallway level)
        {
            Player player = bot.Player;
            float z = Mathf.Max(level.Door.Position.z - 1.5f * player.Scale, Level14InfiniteHallway.HallBZ + player.Radius + 0.2f);
            yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.05f * player.Scale, 8f);
            yield return PickUpThenLetGo(bot, level, 0f, 60f);
            yield return level.Through(bot);
        }

        // Medium. A giant on the ledge's shoe prints (fixture) and the big door down in the hall where the
        // solver's stands, or nearer the wall (fixture). From up there it looks small; let go at the feet it
        // is a door for the way out, wherever at the feet (measured: level14-probe-medium.txt).
        [TestCase(19f, -60f)]
        [TestCase(19f, -45f)]
        [TestCase(19f, -89f)]
        [TestCase(16.5f, -60f)]
        [TestCase(21.5f, -75f)]
        public void TheThirdTrip_FromTheLedge_LetGoAtTheFeet_IsADoorForTheWayOut(float doorZ, float pitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, 0f, doorZ), 3.2f);                         // fixture
            Stand(StandT, 3.2f);                                                        // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpThenLetGo(bot, level, 0f, pitch)));
            Assert.Less(level.Door.Scale, 1.9f, "LEVELS: the ledge's floor gives 1.2 to 1.7");
            Assert.Greater(level.Door.Scale, 0.9f);
            Assert.AreEqual(Ledge, level.Door.Position.y, 0.01f, "it stands on the ledge");
            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.GoOut(bot)), 40f), Where(level));
            Assert.AreEqual(level.Door.Scale, Game.Player.Scale, 1e-4f);
            Assert.IsEmpty(said);
        }

        static IEnumerator Chain(params IEnumerator[] scripts)
        {
            foreach (IEnumerator script in scripts) yield return script;
        }

        // Let go looking at the end wall the door comes to stand against it: nobody as big as a giant gets
        // into its opening there (LEVELS has this aim as one that works; measured, it does not). The level
        // says so, and the door let go at the feet next is the one.
        [Test]
        public void TheThirdTrip_LetGoAgainstTheEndWall_CannotBeWalkedInto_AndTheLevelSaysSo()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, 0f, 19f), 3.2f);                           // fixture
            Stand(StandT, 3.2f);                                                        // fixture
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpThenLetGo(bot, level, 0f, -15f)));
            Assert.Greater(level.Door.Position.z, 30.3f, "against the end wall");
            Assert.Less(level.Door.Scale, 2f);
            Assert.IsFalse(level.CanCross(Game, Game.Player), "no room for a giant in its opening");
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StuckLine }, said);
            Assert.IsFalse(Runs(level.Through(bot), 20f), "and walking at it does nothing");
            Assert.AreEqual(3.2f, Game.Player.Scale);

            Assert.IsTrue(Attempt(Chain(bot.WalkTo(StandT, 0.3f, 6f), level.Medium(bot)), 40f), Where(level));
            Assert.AreEqual(1, said.Count);
        }

        // ---- What the level says ------------------------------------------------------------------------------

        // Picked up from four of its own sizes away the little door looks small, and carried into the second
        // hall it does not grow enough there. Nothing is said in the first hall (the player does not know
        // yet what the door is for next); it is said where it shows. The trip after that one is enough.
        [Test]
        public void CarriedInLookingSmall_ItDoesNotGrowEnough_TheLevelSaysWhy_AndTheNextTripDoesIt()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Small(bot)));
            Assert.AreEqual(0.3f, Game.Player.Scale);
            Assert.IsTrue(Runs(CarryThroughAndLetGo(bot, level, 1.2f, 60f)), Where(level));
            Assert.Less(level.Ratio, Level14InfiniteHallway.HidesTheWay, "it looked small");
            Assert.Less(level.Door.Scale, Level14InfiniteHallway.LedgeLimit);
            Assert.Greater(level.Door.Scale, 1.2f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.FarLine }, said, "said when it stands there too small - not before, not about the view");
            Assert.IsTrue(Runs(level.Through(bot)));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.FarLine, Level14InfiniteHallway.StillShortLine }, said);
            Assert.IsFalse(Runs(level.Climb(bot), 30f), "not tall enough: " + Where(level));

            Assert.IsTrue(Runs(AgainLookingUp(bot, level)), Where(level));
            Assert.GreaterOrEqual(Game.Player.Scale, Level14InfiniteHallway.LedgeScale);
            Assert.IsTrue(Attempt(Chain(level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            Assert.AreEqual(2, said.Count, "nothing more was said: " + string.Join(" | ", said));
        }

        // The level's one tip: in the hand the door's opening is closed, and picked up from near enough for the
        // next step it hides the mouse-hole. Turned edge-on it is a post. Carried like that, everything works.
        [Test]
        public void CarriedEdgeOn_AsTheLevelSuggests_EverythingStillWorks()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Small(bot)));
            Assert.IsEmpty(said);
            Assert.IsTrue(Runs(CarryThroughAndLetGo(bot, level, 0.42f, 60f, 6)), Where(level));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine }, said);
            Assert.AreEqual(3.2f, level.Door.Scale, 0.01f);
            Vector3 normal = level.Portal.Normal;
            Assert.Greater(Mathf.Abs(normal.x), 0.99f, "it stands edge-on to the hall: " + normal);
            Assert.IsTrue(level.CanCross(Game, Game.Player));

            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            Assert.AreEqual(1, said.Count, "nothing more was said: " + string.Join(" | ", said));
        }

        [Test]
        public void TheTipAboutTurningTheDoor_IsSaidOnce_ToAMouseInTheFirstHall_AboutADoorThatHidesTheWay()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            // At the starting size, from the start: nothing.
            Assert.IsTrue(Runs(level.Small(bot)));
            Assert.IsEmpty(said);
            // The mouse, from a step away: it fills the picture.
            Assert.IsTrue(Runs(Chain(bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 4f), bot.Grab(level.Door))));
            Assert.Greater(level.Ratio, Level14InfiniteHallway.HidesTheWay);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine }, said);
            // Put down and picked up again: not twice.
            Assert.IsTrue(Runs(Chain(bot.Drop(), bot.Until(() => level.Portal.Settled, 3f), bot.Wait(7f), bot.Grab(level.Door))));
            Assert.AreEqual(1, said.Count, string.Join(" | ", said));
        }

        // The door let go against a wall stands with its back to it: there is no getting into its opening.
        [Test]
        public void ADoorLetGoAgainstAWall_CannotBeWalkedInto_AndTheLevelSaysSo_ThenItIsSolved()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(AgainstTheDividingWall(bot, level)));
            Assert.Greater(level.Door.Position.z, Level14InfiniteHallway.WallZ - 0.25f, "against the dividing wall: " + Where(level));
            Assert.AreEqual(0f, level.Door.Position.y, 0.01f, "it came down to the floor");
            Assert.IsFalse(level.CanCross(Game, Game.Player));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.StuckLine }, said);

            TestHelpers.PlayLevel(Game, 90f);
        }

        static IEnumerator AgainstTheDividingWall(Bot bot, Level14InfiniteHallway level)
        {
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(new Vector3(2f, 0f, 10f), 0.1f);
            yield return bot.DropAt(new Vector3(2f, 1.2f, Level14InfiniteHallway.WallZ));
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return bot.Wait(0.2f);
            // Back to where the door looks small from.
            yield return bot.WalkTo(new Vector3(1f, 0f, 7.5f), 0.1f);
        }

        // LEVELS: "if the far side has no room for the new size the door returns the player out of the side
        // they came in".
        [Test]
        public void NoRoomBeyondTheDoor_ThePlayerComesBackOutTheWayTheyWentIn_AndIsTold()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, 0f, 12.9f), 3.2f);                         // fixture: a giant's door a step from the dividing wall
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Through(bot)), Where(level));
            Assert.AreEqual(PortalResult.TurnedBack, level.Portal.LastResult);
            Assert.AreEqual(3.2f, Game.Player.Scale);
            Assert.Less(Game.Player.Position.z, 12.9f, "on the side they came from");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(180f, Game.Player.Yaw)), 25f, "turned round");
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnedLine }, said);
            RunSeconds(1f);
            Assert.IsTrue(Game.Player.Grounded, "standing, a giant, with the wall ahead where it was");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.05f);
        }

        // LEVELS: "if neither side has room it does nothing and shows no". A door is as wide as what it makes
        // of whoever walks into it, so walls never leave both of its sides without room; a floor can: edge-on on
        // the kerb with its middle out over the hall, there is nothing to stand on at either end of the trip
        // (fixture: let go from the ledge it comes to stand farther in).
        [Test]
        public void NoRoomOnEitherSide_NothingHappens_AndThePlayerIsTold()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            Stand(new Vector3(-2.6f, Ledge, 26.05f), 3.2f, 90f);                        // fixture: a giant on the ledge, by its edge
            StandDoor(level, new Vector3(0f, Face, 24.4f), 1.3f, 90f);                  // fixture: the door edge-on, its foot on the kerb
            Assert.AreEqual(Face, level.Door.Position.y, 0.01f, "it stands on the kerb");
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(bot.WalkTo(new Vector3(2.6f, Ledge, 26.05f), 0.3f, 4f), bot.Wait(0.5f))), Where(level));
            Assert.AreEqual(PortalResult.Blocked, level.Portal.LastResult);
            Assert.AreEqual(3.2f, Game.Player.Scale);
            Assert.AreEqual(0, level.Portal.Crossings);
            Assert.AreEqual(1, level.Trips, "it counts as a try");
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.BlockedLine }, said);
            Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f, "and the giant is where they were: on the ledge");
        }

        // ---- Nothing is lost ----------------------------------------------------------------------------------

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Level14InfiniteHallway level = Load();
            var bot = new Bot(Game);

            // A mouse with the door in the hand, on the way to the mouse-hole: the restart key.
            Assert.IsTrue(Runs(Chain(MouseWithTheDoorInHand(bot, level), bot.WalkTo(new Vector3(0f, 0f, 9f), 0.1f, 10f))));
            Assert.AreEqual(0.3f, Game.Player.Scale);
            Assert.AreSame(level.Door, Game.Grabber.Held);
            Prop oldDoor = level.Door;
            PortalDoorway oldPortal = level.Portal;
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreSame(level, Game.Level, "the same level, built again");
            Assert.AreNotSame(oldDoor, level.Door);
            Assert.AreNotSame(oldPortal, level.Portal);
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(1f, Game.Player.Scale, "the player is their own size again");
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.Spawn, Game.Player.Position), 0.05f);
            Assert.AreEqual(1f, level.Door.Scale, "a fresh door");
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.DoorStart, level.Door.Position), 1e-3f);
            Assert.AreEqual(0, level.Portal.Crossings);
            Assert.AreEqual(0f, level.Ratio);

            // A giant on the ledge, the big door down in the hall.
            bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(level.Small(bot), level.Big(bot), level.Climb(bot))));
            Assert.AreEqual(3.2f, Game.Player.Scale);
            Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f);
            Game.RestartLevel();
            Assert.AreEqual(1f, Game.Player.Scale);
            Assert.AreEqual(1f, level.Door.Scale);
            RunSeconds(1f);
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.Spawn, Game.Player.Position), 0.05f, "standing at the start");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.IsTrue(level.Portal.Settled);
            Assert.AreSame(level.Door, Game.Grabber.FindTarget(), "the door is under the crosshair again");

            // In the middle of the last trip.
            Assert.IsTrue(Runs(Chain(level.Small(bot), level.Big(bot), level.Climb(bot), bot.Grab(level.Door))));
            Game.RestartLevel();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(1f, Game.Player.Scale);

            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void ARestartAfterTheLevelIsCompleted_StartsItAgain_AtThePlayersOwnSize()
        {
            Level14InfiniteHallway level = Load();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.Greater(Game.Player.Scale, 1.2f, "the level ends at the size of the last door");
            Game.RestartLevel();
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreSame(level, Game.Level);
            Assert.AreEqual(1f, Game.Player.Scale, "nobody takes a size along");
            Assert.AreEqual(1f, level.Door.Scale);
            Assert.AreEqual(0, level.Trips);
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.Spawn, Game.Player.Position), 0.05f);
            TestHelpers.PlayLevel(Game, 90f);
            // Nor into another level.
            Game.LoadLevel(14);
            Assert.AreEqual(1f, Game.Player.Scale);
            Assert.AreNotSame(level, Game.Level);
        }

        // The level makes meshes of its own (the merged sets, the glows) and a collider on the door: built again
        // and again, nothing of an earlier build is left behind.
        [Test]
        public void BuiltAgainAndAgain_TheLevelLeavesNothingBehind()
        {
            string Count() => "objects " + Resources.FindObjectsOfTypeAll(typeof(GameObject)).Length + ", meshes " + Resources.FindObjectsOfTypeAll(typeof(Mesh)).Length +
                              ", materials " + Resources.FindObjectsOfTypeAll(typeof(Material)).Length;
            Level14InfiniteHallway level = Load();
            RunSeconds(0.2f);
            // Once for everything that is made once (shared meshes, materials).
            Game.RestartLevel();
            RunSeconds(0.2f);
            string before = Count();
            for (int i = 0; i < 4; i++)
            {
                Game.RestartLevel();
                RunSeconds(0.2f);
            }
            Assert.AreEqual(before, Count());
            Assert.AreSame(level, Game.Level);
        }

        // The solution done less neatly: let go from somewhere else in the first hall and at another pitch, the
        // little door picked up from nearer or farther, the big one let go looking steeper or flatter, the last
        // from another spot on the ledge.
        [TestCase(0f, 4.5f, -60f, 0.5f, 50f, 0f, 26.6f, -75f)]
        [TestCase(1.5f, 10f, -35f, 0.6f, 75f, 1f, 27f, -50f)]
        [TestCase(-2f, 6f, -50f, 0.36f, 85f, -1.5f, 26.2f, -89f)]
        public void ASloppySolution_StillWorks(float standX, float standZ, float smallPitch, float mouseFrom, float bigPitch, float ledgeX, float ledgeZ, float mediumPitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            bool completed = Attempt(Sloppy(bot, level, new Vector3(standX, 0f, standZ), smallPitch, mouseFrom, bigPitch, new Vector3(ledgeX, Ledge, ledgeZ), mediumPitch));
            Assert.IsTrue(completed, Where(level) + "; said: " + string.Join(" | ", said));
            Assert.AreEqual(3, level.Trips, "three trips");
            foreach (string line in said) Assert.AreEqual(Level14InfiniteHallway.TurnLine, line, "nothing but the tip was said");
        }

        static IEnumerator Sloppy(Bot bot, Level14InfiniteHallway level, Vector3 stand, float smallPitch, float mouseFrom, float bigPitch, Vector3 ledgeStand, float mediumPitch)
        {
            Player player = bot.Player;
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(stand, 0.1f);
            yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(0f, smallPitch) * 2f);
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return level.Through(bot);
            yield return CarryThroughAndLetGo(bot, level, mouseFrom, bigPitch);
            yield return level.Through(bot);
            yield return level.Climb(bot);
            yield return bot.WalkTo(ledgeStand, 0.2f, 6f);
            yield return PickUpThenLetGo(bot, level, 0f, mediumPitch);
            yield return level.Through(bot);
            yield return level.GoOut(bot);
        }

        static IEnumerator MouseWithTheDoorInHand(Bot bot, Level14InfiniteHallway level)
        {
            yield return level.Small(bot);
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 4f);
            yield return bot.Grab(level.Door);
        }

        // Nothing the player can do takes the door out of the hallway (walls to the sky cap all round); should
        // it get out all the same, it comes back to where it started, at the size it started with.
        [Test]
        public void ADoorThatLeavesTheHallway_ComesBack()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            Prop door = level.Door;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;

            // Beyond the walls (fixture).
            door.SetScale(0.5f);
            door.SetPose(new Vector3(30f, 2f, 5f), Quaternion.identity);
            RunSeconds(1f);
            Assert.AreEqual(0, respawns, "not before a second and a half");
            RunSeconds(1f);
            Assert.AreEqual(1, respawns);
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.DoorStart, door.Position), 0.01f, "back where it started");
            Assert.AreEqual(1f, door.Scale, "at the size it started with");
            Assert.Less(Vector3.Distance(Vector3.back, door.Rotation * Vector3.forward), 1e-3f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.BackLine }, said);

            // Out of the bottom of the world (fixture: there is no hole in the floor).
            door.SetScale(2f);
            door.SetPose(new Vector3(0f, level.KillY - 5f, 5f), Quaternion.identity);
            RunSeconds(2f);
            Assert.AreEqual(2, respawns);
            Assert.Less(Vector3.Distance(Level14InfiniteHallway.DoorStart, door.Position), 0.01f);
            Assert.AreEqual(1f, door.Scale);
            RunSeconds(0.5f);
            Assert.IsTrue(level.Portal.Settled);

            // And the level is still there to be solved.
            TestHelpers.PlayLevel(Game, 90f);
        }

        static IEnumerator OntoThePlate(Bot bot, RecallPad pad, PortalDoorway portal)
        {
            int before = pad.Recalls;
            yield return bot.WalkTo(pad.Position, 0.15f * Mathf.Min(1f, bot.Player.Scale), 20f);
            yield return bot.Until(() => pad.Recalls > before, 2f);
            // The door was put down a finger above the floor: it comes to stand in the next ticks.
            yield return bot.Wait(0.2f);
            yield return bot.Until(() => portal.Settled, 2f);
        }

        // LEVELS: the door can end up beyond the mouse-hole while the player is too big to follow. Out of
        // their sight there, the level says where the plates are, and the plate of the first hall brings it back.
        [Test]
        public void TheDoorBeyondTheMouseHole_OutOfSight_IsCalledBackFromThePlateOfTheFirstHall_AndTheLevelIsSolved()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(3f, 0f, 20f), 0.3f);                           // fixture: in the second hall, off to one side
            RunSeconds(1f);
            Assert.IsEmpty(said, "not before a second and a half");
            RunSeconds(1f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.LostLine }, said);
            RunSeconds(8f);
            Assert.AreEqual(1, said.Count, "said once");

            var bot = new Bot(Game);
            Assert.IsTrue(Runs(OntoThePlate(bot, level.RecallA, level.Portal)), Where(level));
            Assert.AreEqual(1, level.RecallA.Recalls);
            Assert.AreEqual(1f, level.Door.Scale, "it comes at the size the player is");
            Assert.Less(Vector3.Distance(level.RecallA.Position, level.Door.Position), 1.6f, "in front of them, at the plate");
            Assert.AreEqual(0f, level.Door.Position.y, 0.01f);
            Assert.AreEqual(Level14InfiniteHallway.Place.HallA, Level14InfiniteHallway.PlaceOf(level.Door.Position));
            RunSeconds(3f);
            Assert.AreEqual(1, said.Count, "nothing more to say");

            // Across the hall from it, it looks small again: the solution from there.
            Assert.IsTrue(Attempt(Chain(bot.WalkTo(new Vector3(2.2f, 0f, 1.5f), 0.2f), level.Solve(bot))), Where(level));
        }

        // The same from the other side: a mouse in the second hall, the door up on the ledge where it cannot be
        // seen from the floor. The plate of the second hall brings it back at the size of a mouse; from right in
        // front of the nose it looks too big, from a step back it is the door for the ledge.
        [Test]
        public void TheDoorUpOnTheLedge_OutOfSight_IsCalledBackFromThePlateOfTheSecondHall_AndTheLevelIsSolved()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            Stand(StandB, 0.3f);                                                        // fixture
            StandDoor(level, new Vector3(3f, Ledge, 29f), 0.2f);                        // fixture
            RunSeconds(2f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.LostLine }, said);

            var bot = new Bot(Game);
            Assert.IsTrue(Runs(OntoThePlate(bot, level.RecallB, level.Portal)), Where(level));
            Assert.AreEqual(0.3f, level.Door.Scale, 1e-4f, "it comes at the size the player is");
            Assert.AreEqual(Level14InfiniteHallway.Place.HallB, Level14InfiniteHallway.PlaceOf(level.Door.Position));
            Assert.AreEqual(0f, level.Door.Position.y, 0.01f);

            // Picked up from where it was put: too near.
            Assert.IsTrue(Runs(bot.Grab(level.Door)));
            Assert.Greater(level.Ratio, Level14InfiniteHallway.LooksTooBig(0.3f));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.LostLine, Level14InfiniteHallway.NearLine }, said);
            Assert.IsTrue(Runs(Chain(bot.Drop(), bot.Until(() => level.Portal.Settled, 3f))));

            // From a step back.
            Assert.IsTrue(Attempt(Chain(StepBackAndCarryToThePrints(bot, level), level.Through(bot), level.Climb(bot), level.Medium(bot)), 90f), Where(level));
            Assert.AreEqual(2, said.Count, "nothing more was said: " + string.Join(" | ", said));
        }

        static IEnumerator StepBackAndCarryToThePrints(Bot bot, Level14InfiniteHallway level)
        {
            Player player = bot.Player;
            Vector3 away = player.Position - level.Door.Position;
            away.y = 0f;
            yield return bot.WalkTo(level.Door.Position + away.normalized * (2f * level.Door.Scale), 0.03f, 6f);
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(StandB, 0.05f, 15f);
            yield return LetGo(bot, level, 0f, 60f);
        }

        // Beyond the mouse-hole but in sight through it: no plate is needed, it can be picked up from where the
        // player stands. The level says that it cannot be walked to.
        [Test]
        public void TheDoorBeyondTheMouseHole_InSight_ThePlayerIsToldToPickItUpAgain_AndCan()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            StandDoor(level, new Vector3(0f, 0f, 20f), 0.3f);                           // fixture: seen through the mouse-hole
            RunSeconds(2f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.ReachLine }, said);
            RunSeconds(8f);
            Assert.AreEqual(1, said.Count, "said once");
            // Picked up through the mouse-hole, eighteen units off, and let go at the feet: it cannot be as small
            // as it looks (a door is never less than 0.1), so it stays where it last could be - small, in this hall.
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(PickUpThenLetGo(bot, level, 0f, -45f)), "picked up through the mouse-hole: " + Where(level));
            Assert.Less(level.Door.Scale, 0.2f, Where(level));
            Assert.GreaterOrEqual(level.Door.Scale, Level14InfiniteHallway.DoorMinScale);
            Assert.AreEqual(Level14InfiniteHallway.Place.HallA, Level14InfiniteHallway.PlaceOf(level.Door.Position));
            Assert.IsTrue(Runs(level.Through(bot)), Where(level));
            Assert.AreEqual(0.3f, Game.Player.Scale);
            Assert.AreEqual(1, said.Count);
        }

        // ---- The words ----------------------------------------------------------------------------------------

        [Test]
        public void TheWordsOfTheLevel_ArePlainAndTheHintsEscalate()
        {
            Level14InfiniteHallway level = Load();
            var lines = new List<string>();
            foreach (FieldInfo field in typeof(Level14InfiniteHallway).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text.Contains(" ")) lines.Add(text);
            }
            Assert.AreEqual(16, lines.Count, "the level's lines: " + string.Join(" | ", lines));
            var all = new List<string>(lines) { level.Blurb };
            all.AddRange(level.Hints);
            foreach (string text in all)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "plain characters only in: " + text);
                Assert.IsTrue(text.EndsWith("."), text);
                Assert.AreEqual(text.Trim(), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                // One verb for one act, as in the levels before: toys are picked up and let go.
                foreach (string word in new[] { "grab", "take it", "taken", "drop", "release" })
                    Assert.IsFalse(lower.Contains(word), "\"" + text + "\" says \"" + word + "\"");
            }
            foreach (string line in lines) Assert.LessOrEqual(line.Length, 110, "too long for a toast: " + line);
            Assert.LessOrEqual(level.Blurb.Length, 60);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level14InfiniteHallway.FarLine, "the campaign's one sentence for a pick-up from too far away");

            string first = level.Hints[0], second = level.Hints[1], third = level.Hints[2];
            foreach (string hint in level.Hints) Assert.LessOrEqual(hint.Length, 200, "five lines on the pause card: " + hint);
            // The first is the rule and no more; the second names the first gate and the recursion; the third is the recipe.
            foreach (string word in new[] { "mouse", "ledge", "feet", "afar", "outline", "shoe prints", "edge", "plate" })
                StringAssert.DoesNotContain(word, first.ToLowerInvariant(), "the first hint does not give the solution away");
            StringAssert.Contains("the size the door is", first);
            StringAssert.Contains("mouse-hole", second);
            StringAssert.Contains("once more", second);
            StringAssert.DoesNotContain("ledge", second, "the second gate is the third hint's");
            StringAssert.StartsWith("Pick the door up from afar", third, "the third hint is the whole solution, the pick-up included");
            StringAssert.Contains("let go at your feet", third);
            StringAssert.Contains("from close", third);
            StringAssert.Contains("mouse-hole", third);
            StringAssert.Contains("looking straight up", third);
            StringAssert.Contains("ledge", third);
            StringAssert.Contains("face the way out", third, "on the ledge the door is picked up looking back at the hall: let go like that, it leads over the edge");
            StringAssert.Contains("from farther off", Level14InfiniteHallway.StillBigLine, "picked up from right behind it the door hardly gets smaller: the line says what does");
        }

        // ---- The room -----------------------------------------------------------------------------------------

        [Test]
        public void TheHallwayIsSealed()
        {
            Load();
            RunSeconds(0.1f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            const float half = Level14InfiniteHallway.HalfWidth, top = Level14InfiniteHallway.WallTop;
            const float wall = Level14InfiniteHallway.WallZ, face = Level14InfiniteHallway.TerraceZ, end = Level14InfiniteHallway.EndZ;
            bool Hit(Vector3 from, Vector3 direction, float reach) => scene.Raycast(from, direction, out _, reach, mask, QueryTriggerInteraction.Ignore);

            for (float x = -half + 0.13f; x <= half - 0.09f; x += 0.3f)
            {
                for (float y = 0.1f; y <= top - 0.09f; y += 0.3f)
                {
                    // The back wall; the dividing wall but for the mouse-hole; the ledge's face; the end wall but for the way out.
                    Assert.IsTrue(Hit(new Vector3(x, y, 1f), Vector3.back, 1.05f), "a way out at the back, x " + x + ", y " + y);
                    bool hole = Mathf.Abs(x) < Level14InfiniteHallway.HoleHalfWidth && y < Level14InfiniteHallway.HoleHeight;
                    Assert.AreNotEqual(hole, Hit(new Vector3(x, y, wall - 0.3f), Vector3.forward, 0.6f), "the dividing wall at x " + x + ", y " + y);
                    Assert.AreNotEqual(hole, Hit(new Vector3(x, y, wall + 1.3f), Vector3.back, 0.6f), "the dividing wall from behind at x " + x + ", y " + y);
                    if (y < Ledge - 0.05f) Assert.IsTrue(Hit(new Vector3(x, y, face - 0.3f), Vector3.forward, 0.35f), "a way into the ledge at x " + x + ", y " + y);
                    else if (y > Ledge + 0.05f)
                    {
                        bool wayOut = Mathf.Abs(x) < Level14InfiniteHallway.ExitHalfWidth && y < Ledge + Level14InfiniteHallway.ExitHeight;
                        Assert.AreNotEqual(wayOut, Hit(new Vector3(x, y, end - 0.3f), Vector3.forward, 0.6f), "the end wall at x " + x + ", y " + y);
                    }
                }
                // The floors and the sky cap.
                for (float z = 0.13f; z <= end - 0.09f; z += 0.3f)
                {
                    bool inWall = z > wall - 0.01f && z < wall + 1.01f;
                    float floor = z < face ? 0f : Ledge;
                    if (!inWall) Assert.IsTrue(Hit(new Vector3(x, floor + 0.3f, z), Vector3.down, 0.35f), "a hole in the floor at x " + x + ", z " + z);
                    Assert.IsTrue(Hit(new Vector3(x, top - 0.5f, z), Vector3.up, 0.55f), "a way out at the top, x " + x + ", z " + z);
                }
            }
            // The side walls, from the floor to the sky cap.
            for (float z = 0.13f; z <= end - 0.09f; z += 0.3f)
            {
                if (z > wall - 0.01f && z < wall + 1.01f) continue;
                for (float y = (z < face ? 0f : Ledge) + 0.1f; y <= top - 0.09f; y += 0.3f)
                {
                    Assert.IsTrue(Hit(new Vector3(half - 0.3f, y, z), Vector3.right, 0.35f), "a way out at +X, z " + z + ", y " + y);
                    Assert.IsTrue(Hit(new Vector3(-half + 0.3f, y, z), Vector3.left, 0.35f), "a way out at -X, z " + z + ", y " + y);
                }
            }
            // The way out is a dead end: floor, walls, ceiling and back.
            float e = Level14InfiniteHallway.ExitHalfWidth;
            for (float z = end + 0.1f; z <= Level14InfiniteHallway.TunnelEnd - 0.09f; z += 0.3f)
            {
                for (float y = Ledge + 0.1f; y <= Ledge + Level14InfiniteHallway.ExitHeight - 0.09f; y += 0.3f)
                {
                    Assert.IsTrue(Hit(new Vector3(0f, y, z), Vector3.right, e + 0.05f), "the tunnel at +X, z " + z + ", y " + y);
                    Assert.IsTrue(Hit(new Vector3(0f, y, z), Vector3.left, e + 0.05f), "the tunnel at -X, z " + z + ", y " + y);
                    Assert.IsTrue(Hit(new Vector3(0f, y, z), Vector3.forward, Level14InfiniteHallway.TunnelEnd - z + 0.05f), "the tunnel's end, y " + y);
                }
                for (float x = -e + 0.1f; x <= e - 0.09f; x += 0.3f)
                {
                    Assert.IsTrue(Hit(new Vector3(x, Ledge + 0.3f, z), Vector3.down, 0.35f), "the tunnel's floor at x " + x + ", z " + z);
                    Assert.IsTrue(Hit(new Vector3(x, Ledge + 0.3f, z), Vector3.up, Level14InfiniteHallway.ExitHeight - 0.25f), "the tunnel's ceiling at x " + x + ", z " + z);
                }
            }
        }

        [Test]
        public void NothingButTheDoor_CanBePickedUp()
        {
            Level14InfiniteHallway level = Load();
            int grabbable = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) grabbable++;
            Assert.AreEqual(1, grabbable, "the door is the only toy");
            var bot = new Bot(Game);
            var setPieces = new[]
            {
                new Vector3(4.1f, 0.6f, 0.9f), new Vector3(2.5f, 0.35f, 0.6f),           // the blocks in the corner
                new Vector3(-4.1f, 0.5f, 13.1f),                                        // the spool
                level.RecallA.Position + Vector3.up * 0.02f,                            // the plate
                new Vector3(4.99f, 3f, 5f), new Vector3(-4.99f, 6.4f, 3.5f),            // a papered door, a lamp
                new Vector3(-3f, 0.5f, 13.99f), new Vector3(2.5f, 2.4f, 13.99f),        // the dividing wall, low and high
            };
            foreach (Vector3 piece in setPieces)
            {
                BotRunner.Run(Game, LookAndClick(bot, piece), 10f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            // The marks on the floor beyond the door, from beside them (from the start they are seen through it).
            BotRunner.Run(Game, bot.WalkTo(new Vector3(2f, 0f, 9.5f), 0.2f), 10f);
            foreach (Vector3 mark in new[] { StandA + Vector3.up * 0.01f, Level14InfiniteHallway.FootA, new Vector3(0f, 2.55f, 13.99f), new Vector3(0f, 1.65f, 13.99f) })
            {
                BotRunner.Run(Game, LookAndClick(bot, mark), 10f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + mark + " picked something up");
            }
            Assert.AreEqual(1f, level.Door.Scale);
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
            const int runs = 3;
            var ticks = new int[runs];
            var rest = new Vector3[runs];
            var end = new Vector3[runs];
            var size = new float[runs];
            for (int run = 0; run < runs; run++)
            {
                Level14InfiniteHallway level = Load();
                TestHelpers.PlayLevel(Game, 90f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Door.Position;
                end[run] = Game.Player.Position;
                size[run] = Game.Player.Scale;
                Game.Dispose();
                Game = null;
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the door stands in the same place in run " + run);
                Assert.AreEqual(end[0], end[run], "the bot ends in the same place in run " + run);
                Assert.AreEqual(size[0], size[run], "at the same size");
            }

            // And once more in a game that has already been through half a solution and a restart.
            Level14InfiniteHallway again = Load();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(again.Small(bot), again.Big(bot))));
            Game.RestartLevel();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(ticks[0], Game.LevelTicks, "the same after a restart");
            Assert.AreEqual(rest[0], again.Door.Position);
        }

        // ---- Fixture shortcuts (the test puts things where it wants them; a bot may not) --------------------

        // The player at a size, standing at a point. (Shrinks before the move, grows after it.)
        void Stand(Vector3 feet, float scale, float yaw = 0f, float pitch = 0f)
        {
            Player player = Game.Player;
            if (scale < player.Scale) player.SetScale(scale);
            player.Teleport(feet, yaw, pitch);
            if (scale > player.Scale) Assert.IsTrue(player.SetScale(scale), "room for a player of " + scale + " at " + feet);
            Run(2);
        }

        // The door at a size, standing at a point, facing -Z.
        void StandDoor(Level14InfiniteHallway level, Vector3 at, float scale, float yaw = 180f)
        {
            level.Door.SetScale(scale);
            level.Door.SetPose(at, Quaternion.Euler(0f, yaw, 0f));
            Run(3);
        }

        bool PickUp(Level14InfiniteHallway level)
        {
            LookAt(level.Door.Center);
            Click();
            if (Game.Grabber.Held == level.Door) return true;
            Player player = Game.Player;
            bool any = Game.PhysicsScene.Raycast(player.Eye, player.Forward, out RaycastHit hit, 150f, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            Debug.Log("[Toybox] pick-up failed: eye " + F(player.Eye) + " forward " + F(player.Forward) + " door " + F(level.Door.Scale) + " at " + F(level.Door.Position) + " centre " + F(level.Door.Center) +
                      "; the ray hits " + (any ? hit.collider.name + " at " + F(hit.point) : "nothing") + "; held " + (Game.Grabber.Held != null ? Game.Grabber.Held.Name : "nothing") +
                      "; ground " + (player.GroundCollider != null ? player.GroundCollider.name : "none") + " grounded " + player.Grounded);
            return false;
        }

        // Lets go of the held door with the view turned to a yaw and a pitch; returns its size once it stands.
        float LetGoByHand(Level14InfiniteHallway level, float yaw, float pitch)
        {
            Game.Player.Yaw = yaw;
            Game.Player.Pitch = pitch;
            Run(2);
            Click();
            TestHelpers.RunUntil(Game, () => level.Portal.Settled, 4f);
            return level.Door.Scale;
        }

        // Walks straight ahead (the keys held down) for a while; the input is let go of afterwards.
        void Walk(float seconds, float yaw, bool sprint = false, bool jump = false)
        {
            Game.Player.Yaw = yaw;
            Game.Player.Pitch = 0f;
            Input.Hold = new InputFrame { MoveZ = 1f, Sprint = sprint };
            if (jump) Input.Once.Jump = true;
            RunSeconds(seconds);
            Input.Hold = default;
        }

        Level14InfiniteHallway LoadScripted()
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(14);
            return (Level14InfiniteHallway)Game.Level;
        }

        // ==== Pictures of other moments than the solver's (explicit: -Filter "Level14Tests.Tour") ================

        // The level with a script of the test's choosing in place of its Solve.
        sealed class TourLevel : LevelDefinition
        {
            public readonly Level14InfiniteHallway Inner = new Level14InfiniteHallway();
            readonly Func<Bot, Level14InfiniteHallway, IEnumerator> script;

            public TourLevel(Func<Bot, Level14InfiniteHallway, IEnumerator> script)
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

        static readonly List<float> Marks = new List<float>();

        // A moment to take a picture of: the bot holds still for half a second, the picture is taken in the middle of it.
        static IEnumerator Mark(Bot bot)
        {
            Marks.Add(bot.Game.Time + 0.25f);
            yield return bot.Wait(0.5f);
        }

        // Plays the script once to learn when its marks are, then once more for the pictures.
        void Tour(string folder, Func<Bot, Level14InfiniteHallway, IEnumerator> script)
        {
            Marks.Clear();
            Level14InfiniteHallway level = Load();
            BotRunner.Run(Game, script(new Bot(Game), level), 180f);
            float[] times = Marks.ToArray();
            DisposeGame();
            Debug.Log("[Toybox] tour " + folder + ": " + string.Join(", ", Array.ConvertAll(times, t => t.ToString("0.00", CultureInfo.InvariantCulture))));
            UiCapture.Request = "hud";
            try
            {
                Shots.Run(new ShotRequest { Level = 14, Definition = new TourLevel(script), Times = times, OutputDirectory = "tools/out/shots/level14/" + folder });
            }
            finally
            {
                UiCapture.Reset();
                Marks.Clear();
            }
        }

        // The start: a look about, the door as it stands, then the first trip made slowly.
        static IEnumerator TourStart(Bot bot, Level14InfiniteHallway level)
        {
            Player player = bot.Player;
            yield return bot.Wait(4f);
            yield return Mark(bot);                          // 1: the first picture, the level card gone
            yield return bot.WalkTo(new Vector3(0f, 0f, 6.3f), 0.1f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 1.3f);
            yield return Mark(bot);                          // 2: a step from the door: it is just a door
            yield return bot.WalkTo(new Vector3(0f, 0f, 11.5f), 0.1f);
            yield return bot.LookAt(new Vector3(0f, 0.9f, Level14InfiniteHallway.WallZ));
            yield return Mark(bot);                          // 3: the mouse-hole and its signs
            yield return bot.Grab(level.Door);
            yield return Mark(bot);                          // 4: in the hand, from three and a half away
            yield return bot.LookAt(player.Eye + Level14InfiniteHallway.View(180f, -55f) * 2f);
            yield return Mark(bot);                          // 5: held at the feet
            yield return bot.Click();
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return Mark(bot);                          // 6: let go
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * 1.5f, 0.1f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 0.3f);
            yield return Mark(bot);                          // 7: looking through it from a step back
        }

        // The mouse: the hall at that size, the door carried edge-on to the mouse-hole, the second hall.
        static IEnumerator TourMouse(Bot bot, Level14InfiniteHallway level)
        {
            Player player = bot.Player;
            var hole = new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ);
            yield return level.Small(bot);
            yield return bot.Wait(2.5f);
            yield return bot.LookAt(hole);
            yield return Mark(bot);                          // 1: the hall, vast; the mouse-hole is a doorway
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 4f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 0.35f);
            yield return Mark(bot);                          // 2: the little door from a step away
            yield return bot.Grab(level.Door);
            yield return bot.LookAt(hole);
            yield return Mark(bot);                          // 3: in the hand, face-on: the tip
            yield return bot.RotateHeld(6);
            yield return Mark(bot);                          // 4: edge-on
            yield return bot.WalkTo(new Vector3(0f, 0f, 12.6f), 0.05f, 15f, true);
            yield return Mark(bot);                          // 5: at the mouse-hole
            yield return bot.WalkTo(StandB, 0.05f, 15f, true);
            yield return bot.LookAt(new Vector3(0f, 2f, Level14InfiniteHallway.TerraceZ));
            yield return Mark(bot);                          // 6: the second hall: the ledge
            yield return bot.RotateHeld(-6);
            yield return bot.LookAt(player.Eye + Level14InfiniteHallway.View(0f, 60f) * 5f);
            yield return Mark(bot);                          // 7: looking up with the door in the hand
            yield return bot.Click();
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 0.4f);
            yield return Mark(bot);                          // 8: at the foot of the big door
            yield return bot.WalkTo(new Vector3(-3.2f, 0f, 16f), 0.05f, 8f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 3f);
            yield return Mark(bot);                          // 9: all of it, from the corner
            yield return bot.LookAt(Level14InfiniteHallway.PadB + new Vector3(0f, 0f, 0.5f));
            yield return Mark(bot);                          // 10: the plate
        }

        // The giant: the hall from up there, the ledge, the way out that is too small, the door picked up from above.
        static IEnumerator TourGiant(Bot bot, Level14InfiniteHallway level)
        {
            Player player = bot.Player;
            yield return level.Small(bot);
            yield return level.Big(bot);
            yield return bot.LookAt(new Vector3(0f, 2f, Level14InfiniteHallway.TerraceZ));
            yield return Mark(bot);                          // 1: out of the door: the ledge is a step
            yield return bot.LookAt(new Vector3(0f, 0f, Level14InfiniteHallway.HallBZ));
            yield return Mark(bot);                          // 2: back at the mouse-hole, through the door
            yield return level.Climb(bot);
            yield return bot.LookAt(Level14InfiniteHallway.ExitCentre);
            yield return Mark(bot);                          // 3: the way out from the shoe prints
            yield return bot.WalkTo(new Vector3(0f, Ledge, 29.6f), 0.3f, 6f);
            yield return Mark(bot);                          // 4: at the way out, a giant
            yield return bot.WalkTo(StandT, 0.15f, 6f);
            yield return bot.LookAt(level.Door.Center);
            yield return Mark(bot);                          // 5: the door down in the hall
            yield return bot.Grab(level.Door);
            yield return bot.LookAt(player.Eye + Level14InfiniteHallway.View(0f, -60f) * 2f);
            yield return Mark(bot);                          // 6: in the hand, at the feet
            yield return bot.LookAt(player.Eye + Level14InfiniteHallway.View(0f, -15f) * 2f);
            yield return Mark(bot);                          // 7: in the hand, at the end wall
            yield return bot.Click();
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return bot.Wait(0.3f);
            yield return Mark(bot);                          // 8: against the end wall: the line
        }

        // A wrong try in the second hall: carried in looking small.
        static IEnumerator TourWrong(Bot bot, Level14InfiniteHallway level)
        {
            yield return level.Small(bot);
            yield return CarryThroughAndLetGo(bot, level, 1.2f, 60f);
            yield return bot.LookAt(level.Door.Position + Vector3.up * 0.4f);
            yield return Mark(bot);                          // 1: it stands there too small: the line
            yield return level.Through(bot);
            yield return bot.LookAt(new Vector3(0f, Ledge, Level14InfiniteHallway.TerraceZ));
            yield return Mark(bot);                          // 2: not tall enough: the line
        }

        [Test, Explicit]
        public void Tour_Pictures()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("no graphics device");
            Tour("start", TourStart);
            Tour("mouse", TourMouse);
            Tour("giant", TourGiant);
            Tour("wrong", TourWrong);
        }

        // ---- Probes (explicit; they write tables to tools/out/notes) ----------------------------------------

        [Test, Explicit]
        public void Probe_Solve()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var log = new StringBuilder();
            Player player = Game.Player;
            Prop door = level.Door;
            Game.Events.PropGrabbed += e => log.AppendLine(F(Game.Time) + " s  picked up: door " + F(e.OldScale) + " from " + F(e.GrabDistance) + " (k " + F(e.OldScale / e.GrabDistance) + "), player " + F(player.Scale) + " at " + F(player.Position));
            Game.Events.PropDropped += e => log.AppendLine(F(Game.Time) + " s  let go: door " + F(e.NewScale) + " at " + F(door.Position) + ", hold distance " + F(e.DropDistance) + ", pitch " + F(player.Pitch) + ", player at " + F(player.Position));
            level.Portal.OnSettled += () => log.AppendLine(F(Game.Time) + " s  settled: door " + F(door.Scale) + " at " + F(door.Position));
            level.Portal.Crossed += (from, to) => log.AppendLine(F(Game.Time) + " s  crossed " + F(from) + " -> " + F(to) + " (" + level.Portal.LastResult + "), player at " + F(player.Position));
            Game.Events.PlayerJumped += e => log.AppendLine(F(Game.Time) + " s  jumped at " + F(e.Position));
            Game.Events.PlayerLanded += e => log.AppendLine(F(Game.Time) + " s  landed at " + F(e.Position) + " speed " + F(e.ImpactSpeed));
            Game.Events.LevelCompleted += e => log.AppendLine(F(e.Time) + " s  COMPLETED, player " + F(player.Scale) + " at " + F(player.Position));
            string error = null;
            try
            {
                var bot = new Bot(Game);
                BotRunner.Run(Game, level.Solve(bot), 120f);
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }
            log.AppendLine("end: " + F(Game.Time) + " s, completed " + Game.LevelCompleted + ", player " + F(player.Scale) + " at " + F(player.Position) + ", door " + F(door.Scale) + " at " + F(door.Position));
            if (error != null) log.AppendLine("ERROR " + error);
            log.AppendLine("said: " + string.Join(" | ", said));
            Write("level14-probe-solve.txt", log);
        }

        [Test, Explicit]
        public void Probe_Gates()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            log.AppendLine("MOUSE-HOLE (0.8 high, 0.7 wide): walk at it from z = 13 for 4 s");
            foreach (float size in new[] { 0.3f, 0.4f, 0.44f, 0.45f, 0.46f, 0.47f, 0.48f, 0.5f, 0.6f, 1f })
            {
                Budget(clock, "level14-probe-gates.txt", log, "mouse-hole, size " + F(size));
                LoadScripted();
                Stand(new Vector3(0f, 0f, 13f), size);
                Walk(4f, 0f);
                log.AppendLine("  size " + F(size) + ": ends at " + F(Game.Player.Position) + (Game.Player.Position.z > 15.2f ? "  THROUGH" : "  stopped"));
                DisposeGame();
            }
            log.AppendLine("LEDGE (face " + F(Face) + " high, floor " + F(Ledge) + "): a running jump from z = 20 (jump at 22), then a standing jump at the face with a push");
            foreach (float size in new[] { 2f, 2.2f, 2.3f, 2.35f, 2.4f, 2.45f, 2.5f, 2.6f, 2.8f, 3.2f })
            {
                Budget(clock, "level14-probe-gates.txt", log, "ledge, size " + F(size));
                LoadScripted();
                Stand(new Vector3(0f, 0f, 20f), size);
                float top = 0f;
                Game.Player.Yaw = 0f;
                Input.Hold = new InputFrame { MoveZ = 1f };
                TestHelpers.RunUntil(Game, () => Game.Player.Position.z > 22f, 2f);
                Input.Once.Jump = true;
                for (int i = 0; i < 150; i++)
                {
                    Game.Tick();
                    top = Mathf.Max(top, Game.Player.Position.y);
                }
                bool running = Game.Player.Grounded && Game.Player.Position.y > Ledge - 0.1f;
                string first = "running: top " + F(top) + " ends " + F(Game.Player.Position) + (running ? " UP" : " down");
                Input.Hold = default;
                DisposeGame();

                LoadScripted();
                Stand(new Vector3(0f, 0f, 24.9f - 0.3f * size), size);
                Walk(0.5f, 0f);
                Input.Once.Jump = true;
                Input.Hold = new InputFrame { MoveZ = 1f };
                top = 0f;
                for (int i = 0; i < 150; i++)
                {
                    Game.Tick();
                    top = Mathf.Max(top, Game.Player.Position.y);
                }
                bool standing = Game.Player.Grounded && Game.Player.Position.y > Ledge - 0.1f;
                log.AppendLine("  size " + F(size) + ": " + first + " | at the face: top " + F(top) + " ends " + F(Game.Player.Position) + (standing ? " UP" : " down"));
                Input.Hold = default;
                DisposeGame();
            }
            log.AppendLine("WAY OUT (3.4 high, 1.6 wide): walk at it from z = 28 on the ledge for 6 s");
            foreach (float size in new[] { 0.3f, 1f, 1.35f, 1.8f, 1.9f, 1.95f, 1.98f, 2f, 2.02f, 2.05f, 2.2f, 2.6f, 3.2f })
            {
                Budget(clock, "level14-probe-gates.txt", log, "way out, size " + F(size));
                LoadScripted();
                Stand(new Vector3(0f, Ledge, 28f), size);
                Walk(6f, 0f);
                log.AppendLine("  size " + F(size) + ": ends at " + F(Game.Player.Position) + (Game.LevelCompleted ? "  OUT" : "  stopped"));
                // A jump at the opening does not help either.
                if (!Game.LevelCompleted)
                {
                    Walk(2f, 0f, false, true);
                    log.AppendLine("      after a jump: " + F(Game.Player.Position) + (Game.LevelCompleted ? "  OUT" : "  stopped"));
                }
                DisposeGame();
            }
            Write("level14-probe-gates.txt", log);
        }

        // The two gates of the second hall, finely: is there a size that gets up the ledge AND out?
        [Test, Explicit]
        public void Probe_GatesFine()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            log.AppendLine("LEDGE: walking jump (from z 20, jump at 22) | sprinting jump (from z 16, jump at 21.5) | standing at the face, jump, push | walking jump at 30 degrees");
            for (float size = 2.2f; size < 2.66f; size += 0.02f)
            {
                var row = new StringBuilder("  size " + F(size) + ":");
                for (int way = 0; way < 4; way++)
                {
                    Budget(clock, "level14-probe-gates-fine.txt", log, "ledge, size " + F(size) + ", way " + way);
                    LoadScripted();
                    float yaw = way == 3 ? 30f : 0f;
                    Stand(way == 2 ? new Vector3(0f, 0f, 24.9f - 0.3f * size) : new Vector3(way == 3 ? -2f : 0f, 0f, way == 1 ? 16.2f + 0.3f * size : 20f), size);
                    Game.Player.Yaw = yaw;
                    Input.Hold = new InputFrame { MoveZ = 1f, Sprint = way == 1 };
                    if (way == 2) RunSeconds(0.4f);
                    else TestHelpers.RunUntil(Game, () => Game.Player.Position.z > (way == 1 ? 21.5f : 22f), 3f);
                    Input.Once.Jump = true;
                    float top = 0f;
                    for (int i = 0; i < 150; i++)
                    {
                        Game.Tick();
                        top = Mathf.Max(top, Game.Player.Position.y);
                    }
                    bool up = Game.Player.Grounded && Game.Player.Position.y > Ledge - 0.1f;
                    row.Append("  " + (up ? "UP  " : "down") + " (top " + F(top) + ")");
                    Input.Hold = default;
                    DisposeGame();
                }
                log.AppendLine(row.ToString());
            }
            log.AppendLine("WAY OUT: walk at it from z = 28 on the ledge for 6 s, then a jump, then again from an angle");
            for (float size = 1.94f; size < 2.07f; size += 0.01f)
            {
                Budget(clock, "level14-probe-gates-fine.txt", log, "way out, size " + F(size));
                LoadScripted();
                Stand(new Vector3(0f, Ledge, 28f), size);
                Walk(6f, 0f);
                bool straight = Game.LevelCompleted;
                if (!straight) Walk(2f, 0f, false, true);
                bool jumped = Game.LevelCompleted;
                if (!jumped)
                {
                    Stand(new Vector3(-1.5f, Ledge, 28f), size);
                    Walk(4f, 25f, true);
                    Walk(3f, 0f, true, true);
                }
                log.AppendLine("  size " + F(size) + ": " + (straight ? "OUT" : jumped ? "out with a jump" : Game.LevelCompleted ? "out from an angle" : "stopped at " + F(Game.Player.Position)));
                DisposeGame();
            }
            Write("level14-probe-gates-fine.txt", log);
        }

        // What size the door has when it is let go at the feet in the first hall, by where it was picked up from.
        [Test, Explicit]
        public void Probe_Small()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            log.AppendLine("door 1.0 at (0,0,8); picked up from g away (the feet at z = 8 - g), let go on the spot at a pitch");
            float[] pitches = { -20f, -30f, -45f, -60f, -75f, -89f };
            log.Append("   g       k    ");
            foreach (float pitch in pitches) log.Append("  p" + pitch.ToString("0") + "   ");
            log.AppendLine();
            foreach (float g in new[] { 1f, 1.5f, 2f, 2.3f, 2.6f, 3f, 3.5f, 4f, 5f, 6f, 7.5f })
            {
                var row = new StringBuilder();
                float k = 0f;
                foreach (float pitch in pitches)
                {
                    Budget(clock, "level14-probe-small.txt", log, "from " + F(g) + ", pitch " + F(pitch));
                    Level14InfiniteHallway level = LoadScripted();
                    Stand(new Vector3(0f, 0f, 8f - g), 1f);
                    Assert.IsTrue(PickUp(level));
                    k = level.Ratio;
                    float scale = LetGoByHand(level, 0f, pitch);
                    row.Append("  " + F(scale));
                    DisposeGame();
                }
                log.AppendLine("  " + F(g) + "  " + F(k) + " " + row);
            }
            Write("level14-probe-small.txt", log);
        }

        // What size the door has when a small player lets go of it in the second hall, by how big it looked
        // when picked up and how high they look.
        [Test, Explicit]
        public void Probe_Big()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float[] pitches = { 0f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 89f };
            foreach (float size in new[] { 0.3f, 0.6f, 1f, 1.8f })
            {
                log.AppendLine("player " + F(size) + " at the shoe prints of hall B, looking down the hall (+Z)");
                log.Append("     k    ");
                foreach (float pitch in pitches) log.Append("   p" + pitch.ToString("00") + "  ");
                log.AppendLine();
                foreach (float k in new[] { 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.2f, 1.5f })
                {
                    var row = new StringBuilder();
                    foreach (float pitch in pitches)
                    {
                        Budget(clock, "level14-probe-big.txt", log, "size " + F(size) + " k " + F(k) + " pitch " + F(pitch));
                        Level14InfiniteHallway level = LoadScripted();
                        Vector3 stand = Level14InfiniteHallway.StandB;
                        Stand(stand, size);
                        // A door whose middle is at eye height, straight ahead: its distance is its size over k.
                        float doorScale = Game.Player.EyeHeight / 1.25f;
                        if (stand.z + doorScale / k + 0.2f * doorScale > Level14InfiniteHallway.TerraceZ)
                        {
                            // The hall is too short to see it that small.
                            row.Append("    -   ");
                            DisposeGame();
                            continue;
                        }
                        StandDoor(level, stand + Vector3.forward * (doorScale / k), doorScale);
                        Assert.IsTrue(PickUp(level), "picked up at k " + k);
                        float scale = LetGoByHand(level, 0f, pitch);
                        row.Append("  " + F(scale));
                        DisposeGame();
                    }
                    log.AppendLine("  " + F(k) + " " + row);
                }
            }
            Write("level14-probe-big.txt", log);
        }

        // What size the door has when a giant on the ledge lets go of it, by where the big door stood below
        // and where they look.
        [Test, Explicit]
        public void Probe_Medium()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float[] pitches = { 20f, 0f, -10f, -20f, -30f, -45f, -60f, -75f, -89f };
            foreach (float size in new[] { 3.2f, 2.5f })
                foreach (float doorZ in new[] { 16.5f, 18.5f, 20.5f, 22.5f })
                {
                    log.AppendLine("player " + F(size) + " on the ledge's shoe prints; the door (" + F(size) + ") stood at z = " + F(doorZ) + "; let go looking at the end wall (+Z) at a pitch");
                    var row = new StringBuilder();
                    float k = 0f;
                    foreach (float pitch in pitches)
                    {
                        Budget(clock, "level14-probe-medium.txt", log, "size " + F(size) + ", door at z " + F(doorZ) + ", pitch " + F(pitch));
                        Level14InfiniteHallway level = LoadScripted();
                        StandDoor(level, new Vector3(0f, 0f, doorZ), size);
                        Stand(Level14InfiniteHallway.StandT, size);
                        Assert.IsTrue(PickUp(level), "picked up from the ledge");
                        k = level.Ratio;
                        float scale = LetGoByHand(level, 0f, pitch);
                        row.Append("  p" + pitch.ToString("0") + " " + F(scale) + " at z " + level.Door.Position.z.ToString("0.0") + " y " + level.Door.Position.y.ToString("0.0") + ";");
                        DisposeGame();
                    }
                    log.AppendLine("  k " + F(k) + ":" + row);
                }
            Write("level14-probe-medium.txt", log);
        }

        // Walking into doors much smaller or bigger than the player: does the trip happen, and how long does it take?
        [Test, Explicit]
        public void Probe_Crossings()
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (float size in new[] { 3.2f, 1f, 0.3f })
            {
                log.AppendLine("player " + F(size) + " walks into a door standing 1.5 of their sizes ahead:");
                foreach (float doorScale in new[] { 0.1f, 0.15f, 0.2f, 0.3f, 0.4f, 0.6f, 1f, 1.35f, 1.9f, 2.5f, 3.2f })
                {
                    Budget(clock, "level14-probe-crossings.txt", log, "size " + F(size) + ", door " + F(doorScale));
                    Level14InfiniteHallway level = LoadScripted();
                    List<string> said = Listen();
                    bool ledge = size > 2f;
                    Vector3 stand = ledge ? new Vector3(0f, Ledge, 26.2f) : new Vector3(0f, 0f, 17f);
                    Stand(stand, size);
                    StandDoor(level, stand + Vector3.forward * (ledge ? 2.4f : 1.5f * size), doorScale);
                    int start = Game.LevelTicks;
                    Game.Player.Yaw = 0f;
                    Input.Hold = new InputFrame { MoveZ = 1f };
                    bool crossed = TestHelpers.RunUntil(Game, () => level.Portal.LastResult != PortalResult.None, 5f);
                    float seconds = (Game.LevelTicks - start) * Sim.Dt;
                    Input.Hold = default;
                    Run(30);
                    log.AppendLine("  door " + F(doorScale) + ": " + (crossed ? level.Portal.LastResult + " after " + F(seconds) + " s (walking there takes " + F(1.5f * size / (5f * size)) + ")" : "NOTHING in 5 s") +
                                   ", player " + F(Game.Player.Scale) + " at " + F(Game.Player.Position) + " grounded " + Game.Player.Grounded + (said.Count > 0 ? "  said: " + string.Join(" | ", said) : ""));
                    DisposeGame();
                }
            }
            Write("level14-probe-crossings.txt", log);
        }

        // ==== Review: what was tried against the level, each attack kept as a test =================================
        // (The probes these came from: tools/out/notes/level14-review-probes.cs.txt and level14-review-probe-*.txt.)

        // Hops along a heading for a while (scripted input).
        void Hop(float yaw, float seconds)
        {
            Game.Player.Yaw = yaw;
            Game.Player.Pitch = 0f;
            Input.Hold = new InputFrame { MoveZ = 1f };
            for (int i = 0; i < TestHelpers.Ticks(seconds); i++)
            {
                if (Game.Player.Grounded) Input.Once.Jump = true;
                Game.Tick();
            }
            Input.Hold = default;
        }

        // Small, then a big trip that is not big enough (let go too flat), then from the foot of the ledge the
        // door picked up, turned edge-on and let go looking up over the edge.
        static IEnumerator HangTheDoor(Bot bot, Level14InfiniteHallway level, float weakPitch, float standZ, float throwPitch, int turns)
        {
            yield return level.Small(bot);
            yield return CarryThroughAndLetGo(bot, level, 0.42f, weakPitch);
            yield return level.Through(bot);
            yield return bot.WalkTo(new Vector3(0f, 0f, standZ), 0.1f, 10f);
            yield return bot.Grab(level.Door);
            if (turns != 0) yield return bot.RotateHeld(turns);
            yield return LetGo(bot, level, 0f, throwPitch);
        }

        // Along the foot of the ledge from one side wall to the other, with a hop in the middle.
        static IEnumerator HopUnder(Bot bot, float z)
        {
            Player player = bot.Player;
            yield return bot.WalkTo(new Vector3(-2.2f, 0f, z), 0.1f, 8f);
            IEnumerator walk = bot.WalkTo(new Vector3(2.5f, 0f, z), 0.1f, 4f);
            bool jumped = false;
            while (player.Position.y < Ledge - 0.5f && walk.MoveNext())
            {
                yield return walk.Current;
                if (!jumped && player.Position.x > -1.2f)
                {
                    jumped = true;
                    yield return bot.Jump();
                }
            }
            yield return bot.Wait(0.5f);
        }

        // BYPASS, closed in the gadget (PortalDoorway.FootSlack). A trip used to count as soon as the door's
        // opening overlapped the player's height. A door standing on the ledge by its edge, edge-on, reaches
        // out over the hall: whoever was 1.5 or more and hopped along the face under it came out up on the
        // ledge at the door's size - and through the way out without ever having been big.
        [Test]
        public void ADoorOnTheEdgeOfTheLedge_TakesNobodyUpFromBelow()
        {
            foreach (float doorScale in new[] { 1.3f, 1.9f, 3.2f })
                foreach (float size in new[] { 1.5f, 1.9f, 2.3f, 2.45f })
                {
                    Level14InfiniteHallway level = LoadScripted();
                    StandDoor(level, new Vector3(0f, Face, 25.3f), doorScale, 90f);     // fixture: edge-on, its foot on the kerb
                    Run(30);
                    string what = "a door of " + doorScale + " on the edge, a player of " + size + " hopping along under it: " + Where(level);
                    Assert.AreEqual(Face, level.Door.Position.y, 0.01f, what);
                    Stand(new Vector3(-2.5f, 0f, 25f - 0.3f * size - 0.03f), size);     // fixture: at the foot of the ledge
                    Hop(90f, 2.5f);
                    TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
                    Assert.Greater(Game.Player.Position.x, 1f, what + " (they did pass under it)");
                    Assert.AreEqual(PortalResult.None, level.Portal.LastResult, what);
                    Assert.AreEqual(size, Game.Player.Scale, 1e-4f, what);
                    Assert.Less(Game.Player.Position.y, 0.5f, what);
                    DisposeGame();
                }
        }

        // The same by input only: these two scripts completed the level before the fix.
        [TestCase(40f, 24.3f, 80f)]
        [TestCase(40f, 24f, 75f)]
        public void TheDoorHungOverTheEdgeFromBelow_AndHoppedUnder_IsNoWayOut(float weakPitch, float standZ, float throwPitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(HangTheDoor(bot, level, weakPitch, standZ, throwPitch, 6), 60f), Where(level));
            TestHelpers.RunUntil(Game, () => level.Portal.Settled, 3f);
            float size = Game.Player.Scale;
            Assert.Less(size, Level14InfiniteHallway.LedgeLimit, "not big enough for the ledge");
            Assert.Greater(size, 1.2f, "but tall enough for a hop to reach the door's foot");
            Assert.Greater(level.Door.Position.y, Ledge - 0.01f, "the door stands up on the ledge: " + Where(level));
            Assert.Less(level.Door.Position.z - 0.6f * level.Door.Scale, Level14InfiniteHallway.TerraceZ, "and reaches out over the hall");
            Assert.Greater(Mathf.Abs(level.Portal.Normal.x), 0.99f, "edge-on");
            int trips = level.Trips;

            Runs(HopUnder(bot, Level14InfiniteHallway.TerraceZ - Game.Player.Radius - 0.05f), 20f);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Assert.AreEqual(trips, level.Trips, "no trip from below: " + Where(level));
            Assert.AreEqual(size, Game.Player.Scale);
            Assert.Less(Game.Player.Position.y, 0.5f, "still down in the hall");
            Assert.IsFalse(Attempt(level.GoOut(bot), 15f), "and not out");
            CollectionAssert.Contains(said, Level14InfiniteHallway.ReachLine, "told that the door cannot be got to up there");
        }

        // UNFAIR, fixed by moving the shoe prints. The level's own tip is to carry the door edge-on, and the
        // third hint says to look straight up: edge-on the door is 0.8 of its size long behind its middle, and
        // from the old prints (1.2 from the dividing wall) it met that wall at 1.3 to 1.8.
        [TestCase(80f)]
        [TestCase(89f)]
        public void CarriedEdgeOn_AndLetGoLookingStraightUpFromThePrints_ItStillGrowsBigEnough(float pitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Small(bot)));
            Assert.IsTrue(Runs(CarryThroughAndLetGo(bot, level, 0.42f, pitch, 6)), Where(level));
            Assert.Greater(Mathf.Abs(level.Portal.Normal.x), 0.99f, "it stands edge-on to the hall");
            Assert.AreEqual(3.2f, level.Door.Scale, 0.01f, "nothing stopped it but its own limit: " + Where(level));
            Assert.Greater(level.Door.Position.z - 0.8f * level.Door.Scale, Level14InfiniteHallway.HallBZ + 0.2f, "its near end is clear of the dividing wall");
            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine }, said);
        }

        // Elsewhere a wall still stops it: a step from the dividing wall edge-on, or face-on beside a side wall
        // (the floor plate lies in that corner). It looked right and its foot was off the floor - the level
        // used to say only "let go of it looking up" after the trip. Now it says what stopped it, and out in
        // the open the same door is big enough.
        [TestCase(0f, 15.6f, 6, 85f)]
        [TestCase(-3.6f, 18f, 0, 80f)]
        public void LetGoWhereAWallStopsIt_TheDoorStaysSmall_AndTheLevelSaysWhy(float x, float z, int turns, float pitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(level.Small(bot)));
            Assert.IsTrue(Runs(CarryToAndLetGo(bot, level, new Vector3(x, 0f, z), turns, pitch)), Where(level));
            Assert.Greater(level.Ratio, Level14InfiniteHallway.LooksTooSmall(0.3f), "it looked big enough");
            Assert.Less(level.Ratio, Level14InfiniteHallway.LooksTooBig(0.3f), "and not too big");
            Assert.Less(level.Door.Scale, Level14InfiniteHallway.LedgeLimit, "a wall stopped it: " + Where(level));
            Assert.Greater(level.Door.Scale, 0.6f);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine, Level14InfiniteHallway.CrampedLine }, said);

            // Let go looking straight up, the door came down round the bot: it stands in the opening, or a step
            // from it, and its first step is a trip through it. Bigger, not big enough - said. Then out in the
            // open: picked up again from one and a half of its sizes away, carried to the prints, let go
            // looking up.
            int trips = level.Trips;
            Assert.IsTrue(Runs(FromTheOpen(bot, level)), Where(level));
            Assert.AreEqual(trips + 1, level.Trips, "the first step away was a trip through the door that stood round the bot");
            Assert.GreaterOrEqual(level.Door.Scale, Level14InfiniteHallway.LedgeScale, Where(level));
            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine, Level14InfiniteHallway.CrampedLine, Level14InfiniteHallway.StillShortLine }, said,
                "nothing more was said: " + string.Join(" | ", said));
        }

        // The mouse-sized bot beside the little door picks it up, turns it if asked, carries it through the
        // mouse-hole to a spot in the second hall and lets go of it there looking up.
        static IEnumerator CarryToAndLetGo(Bot bot, Level14InfiniteHallway level, Vector3 spot, int turns, float pitch)
        {
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 6f);
            yield return bot.Grab(level.Door);
            if (turns != 0) yield return bot.RotateHeld(turns);
            yield return bot.LookAt(new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ));
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.5f), 0.05f, 15f, true);
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.HallBZ + 0.3f), 0.05f, 15f, true);
            yield return bot.WalkTo(spot, 0.05f, 15f, true);
            yield return LetGo(bot, level, 0f, pitch);
        }

        static IEnumerator FromTheOpen(Bot bot, Level14InfiniteHallway level)
        {
            Vector3 toMiddle = new Vector3(0f, 0f, 20f) - level.Door.Position;
            toMiddle.y = 0f;
            yield return bot.WalkTo(level.Door.Position + toMiddle.normalized * (1.5f * level.Door.Scale), 0.05f, 10f);
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(StandB, 0.05f, 15f);
            yield return LetGo(bot, level, 0f, 80f);
        }

        // UNFAIR, fixed three ways (the gadget's floor rule, the kerb, a line). On the ledge the door is picked
        // up looking back down the hall, and the natural thing is to let go of it at one's feet there and then.
        // Before: at 60 down the door hung on the edge and the trip put the player in the air beyond it; at 75
        // they came out a stride from the edge, walking. Either way: down in the hall at 1.3, the door up there.
        [TestCase(-45f, false)]
        [TestCase(-60f, true)]
        [TestCase(-75f, true)]
        [TestCase(-89f, true)]
        public void OnTheLedge_LetGoAtTheFeetStillFacingTheHall_NobodyGoesOverTheEdge(float pitch, bool onTheLedge)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(level.Small(bot), level.Big(bot), level.Climb(bot), bot.Grab(level.Door), LetGo(bot, level, 180f, pitch), bot.Wait(0.5f))), Where(level));
            Assert.AreEqual(3.2f, Game.Player.Scale);
            if (!onTheLedge)
            {
                // Over the kerb and down: it stands in the hall, and the level says what to do instead.
                Assert.Less(level.Door.Position.y, 0.05f, Where(level));
                Assert.AreEqual(Level14InfiniteHallway.EdgeLine, said[said.Count - 1]);
                Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f);
                Assert.IsTrue(Attempt(level.Medium(bot), 40f), Where(level));
                Assert.AreEqual(2, said.Count, string.Join(" | ", said));
                return;
            }
            Assert.Greater(level.Door.Position.y, Ledge - 0.01f, "it stands on the ledge (or on its kerb): " + Where(level));
            Assert.Less(level.Door.Scale, Level14InfiniteHallway.ExitScale);
            Assert.IsTrue(Runs(level.Through(bot)), Where(level));
            Assert.AreEqual(level.Door.Scale, Game.Player.Scale, 1e-4f, "the door's size, whichever side they came out of");
            // Whoever walked in is still walking: a second and a half more of it, straight at the hall.
            Runs(bot.WalkTo(new Vector3(0f, 0f, 20f), 0.3f, 1.5f), 5f);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Assert.AreEqual(Ledge, Game.Player.Position.y, 0.05f, "still up on the ledge: " + Where(level) + " (" + level.Portal.LastResult + ")");
            Assert.Greater(Game.Player.Position.z, Level14InfiniteHallway.TerraceZ + Level14InfiniteHallway.KerbDepth, "behind the kerb");
            CollectionAssert.DoesNotContain(said, Level14InfiniteHallway.EdgeLine);
            Assert.IsTrue(Attempt(level.GoOut(bot), 30f), Where(level));
        }

        // The kerb: nobody who fits the way out walks or sprints over it; a giant does not notice it.
        [TestCase(0.3f, true)]
        [TestCase(1f, true)]
        [TestCase(1.35f, true)]
        [TestCase(2f, true)]
        [TestCase(3.2f, false)]
        public void TheKerb_HoldsWhoeverIsSmallEnoughForTheWayOut_AndAGiantStepsOverIt(float size, bool held)
        {
            LoadScripted();
            Stand(new Vector3(0f, Ledge, 27f), size, 180f);                             // fixture: on the ledge, facing the hall
            Walk(2f, 180f);
            Walk(2f, 180f, true);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Player player = Game.Player;
            if (held)
            {
                Assert.AreEqual(Ledge, player.Position.y, 0.05f, "a player of " + size + " is still on the ledge");
                Assert.Greater(player.Position.z, Level14InfiniteHallway.TerraceZ + Level14InfiniteHallway.KerbDepth);
            }
            else Assert.Less(player.Position.y, 0.5f, "a player of " + size + " walked over the kerb and down into the hall");
            Assert.AreEqual(Face, Ledge + Level14InfiniteHallway.KerbHeight, 1e-5f, "the face is as high as before: the kerb is part of it");
        }

        // A giant on the plate with a wall on every side of them (fixture: a giant has no business down there
        // once the door is lost): the plate put the door where it lies itself, half in the side wall.
        [Test]
        public void AGiantOnThePlateWithWallsAllRound_GetsTheDoorInTheMiddleOfTheHall_NotInAWall()
        {
            Level14InfiniteHallway level = Load();
            StandDoor(level, new Vector3(2f, 0f, 6f), 0.2f);                            // fixture: the door in the first hall
            Stand(new Vector3(-3f, 0f, 17.3f), 3.2f);                                   // fixture: a giant within the plate's reach
            RunSeconds(1.5f);
            Assert.AreEqual(1, level.RecallB.Recalls);
            Prop door = level.Door;
            Assert.AreEqual(3.2f, door.Scale, "it comes at the size the player is");
            Assert.IsTrue(level.Portal.Settled);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 20f), door.Position), 0.05f, "in the middle of the second hall: " + Where(level));
            Assert.Greater(Mathf.Abs(level.Portal.Normal.z), 0.99f, "square to the hall");
            var hits = new Collider[4];
            int walls = Game.PhysicsScene.OverlapBox(door.Center + Vector3.up * 0.2f, door.LocalHalfExtents * (door.Scale * 0.98f), hits, door.Rotation, Layers.DefaultMask, QueryTriggerInteraction.Ignore);
            Assert.AreEqual(0, walls, "it stands in " + (walls > 0 ? hits[0].name : "nothing"));

            var bot = new Bot(Game);
            Assert.IsTrue(Attempt(Chain(level.Climb(bot), level.Medium(bot)), 60f), Where(level));
        }

        // What a first-time player does (probe "natural"). The door picked up from right behind it, where a
        // trip leaves them, only gets a quarter smaller each time - it gets there, and the line says what is
        // quicker.
        static IEnumerator CloseAgainAndAgain(Bot bot, Level14InfiniteHallway level, float from, float pitch, int times)
        {
            Player player = bot.Player;
            yield return bot.WalkTo(new Vector3(0f, 0f, 8f - from), 0.05f);
            for (int i = 0; i < times && player.Scale > Level14InfiniteHallway.MouseLimit; i++)
            {
                yield return bot.Grab(level.Door);
                yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(player.Yaw, pitch) * 2f);
                yield return bot.Until(() => level.Portal.Settled, 3f);
                yield return level.Through(bot);
            }
        }

        [Test]
        public void PickedUpFromRightBehindItAgainAndAgain_TheDoorGetsSmallEnoughInTheEnd_AndTheLineSaysWhatIsQuicker()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(CloseAgainAndAgain(bot, level, 1.2f, -60f, 8), 90f), Where(level));
            Assert.LessOrEqual(Game.Player.Scale, Level14InfiniteHallway.MouseLimit);
            Assert.AreEqual(3, level.Trips, "1 -> 0.66 -> 0.52 -> 0.40");
            Assert.Greater(said.Count, 0);
            foreach (string line in said) Assert.AreEqual(Level14InfiniteHallway.StillBigLine, line);
            Assert.IsTrue(Runs(ThroughTheHole(bot), 40f), Where(level));
        }

        // The mouse turns round the moment it has come out of the little door and picks it up from there: it
        // looks too big to rise off the floor. The level says so, and trip after trip - walked into, picked up
        // from right behind, let go looking up - it gets big enough all the same: that is the recursion.
        static IEnumerator TurnRoundAtOnce(Bot bot, Level14InfiniteHallway level, float bigPitch)
        {
            yield return level.Small(bot);
            yield return bot.Grab(level.Door);
            yield return bot.LookAt(new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ));
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.5f), 0.05f, 15f, true);
            yield return bot.WalkTo(StandB, 0.05f, 15f, true);
            yield return LetGo(bot, level, 0f, bigPitch);
        }

        static IEnumerator ThroughAndUpAgain(Bot bot, Level14InfiniteHallway level, float pitch, int times)
        {
            Player player = bot.Player;
            for (int i = 0; i < times && player.Scale < Level14InfiniteHallway.LedgeLimit; i++)
            {
                yield return level.Through(bot);
                if (player.Scale >= Level14InfiniteHallway.LedgeLimit) break;
                yield return bot.Grab(level.Door);
                yield return LetGo(bot, level, 0f, pitch);
            }
            if (player.Scale < Level14InfiniteHallway.LedgeLimit) yield return level.Through(bot);
        }

        [TestCase(60f)]
        [TestCase(85f)]
        public void TheMouseTurnsRoundAtOnce_TheDoorLooksTooBig_TheLevelSaysSo_AndTripAfterTripItGetsThere(float pitch)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(TurnRoundAtOnce(bot, level, pitch), 60f), Where(level));
            Assert.Greater(level.Ratio, Level14InfiniteHallway.LooksTooBig(0.3f), "from right behind it, it fills the picture");
            Assert.Less(level.Door.Scale, Level14InfiniteHallway.LedgeLimit);
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine, Level14InfiniteHallway.NearLine }, said);

            Assert.IsTrue(Runs(ThroughAndUpAgain(bot, level, pitch, 5), 90f), Where(level));
            Assert.GreaterOrEqual(Game.Player.Scale, Level14InfiniteHallway.LedgeLimit, Where(level));
            Assert.LessOrEqual(level.Trips, 4, "the small trip and three more at the most");
            CollectionAssert.Contains(said, Level14InfiniteHallway.StillShortLine);
            Assert.IsTrue(Attempt(Chain(level.Climb(bot), level.Medium(bot)), 60f), Where(level));
        }

        // The solution with every number a little off - where the bot stands, where it looks, from how far the
        // mouse picks the door up (within the rule: 0.34 to 0.68 away), yaw as well as pitch.
        static IEnumerator Sloppier(Bot bot, Level14InfiniteHallway level, Vector3 stand, float smallYaw, float smallPitch, float mouseFrom, Vector3 standB, float bigYaw, float bigPitch, Vector3 ledgeStand, float mediumYaw, float mediumPitch)
        {
            Player player = bot.Player;
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(stand, 0.1f);
            yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(smallYaw, smallPitch) * 2f);
            yield return bot.Until(() => level.Portal.Settled, 3f);
            yield return level.Through(bot);
            Vector3 away = player.Position - level.Door.Position;
            away.y = 0f;
            yield return bot.WalkTo(level.Door.Position + away.normalized * mouseFrom, 0.03f, 6f);
            yield return bot.Grab(level.Door);
            yield return bot.LookAt(new Vector3(0f, Level14InfiniteHallway.HoleHeight * 0.5f, Level14InfiniteHallway.WallZ));
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.5f), 0.05f, 15f, true);
            yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.HallBZ + 0.3f), 0.05f, 15f, true);
            yield return bot.WalkTo(standB, 0.05f, 15f, true);
            yield return LetGo(bot, level, bigYaw, bigPitch);
            yield return level.Through(bot);
            yield return level.Climb(bot);
            yield return bot.WalkTo(ledgeStand, 0.2f, 6f);
            yield return PickUpThenLetGo(bot, level, mediumYaw, mediumPitch);
            yield return level.Through(bot);
            yield return level.GoOut(bot);
        }

        // Plays so many jittered solutions (the same ones for the same seed); returns how many completed.
        int Jittered(int count, StringBuilder log)
        {
            var random = new System.Random(14);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                var stand = new Vector3(R(-0.8f, 0.8f), 0f, R(10.4f, 12f));
                float smallYaw = R(-12f, 12f), smallPitch = R(-65f, -32f);
                float mouseFrom = R(0.34f, 0.68f);
                var standB = new Vector3(R(-0.7f, 0.7f), 0f, StandB.z + R(-0.5f, 0.6f));
                float bigYaw = R(-12f, 12f), bigPitch = R(55f, 88f);
                var ledge = new Vector3(R(-1.2f, 1.2f), Ledge, R(26.3f, 27.5f));
                float mediumYaw = R(-12f, 12f), mediumPitch = R(-85f, -45f);
                Level14InfiniteHallway level = Load();
                List<string> said = Listen();
                var sizes = new List<string>();
                Game.Events.PropDropped += e => sizes.Add(F(e.NewScale));
                var bot = new Bot(Game);
                bool done = Attempt(Sloppier(bot, level, stand, smallYaw, smallPitch, mouseFrom, standB, bigYaw, bigPitch, ledge, mediumYaw, mediumPitch));
                if (done) ok++;
                log.AppendLine((done ? "ok   " : "FAIL ") + "A " + F(stand) + " yaw " + F(smallYaw) + " pitch " + F(smallPitch) + " | mouse from " + F(mouseFrom) + " | B " + F(standB) + " yaw " + F(bigYaw) + " pitch " + F(bigPitch) +
                               " | T " + F(ledge) + " yaw " + F(mediumYaw) + " pitch " + F(mediumPitch) + " | doors " + string.Join(" / ", sizes) + " | trips " + level.Trips + (done ? "" : " | " + Where(level)) +
                               (said.Count > 0 ? " | said: " + string.Join(" | ", said) : ""));
                DisposeGame();
            }
            return ok;
        }

        [Test]
        public void TheSolutionWithEveryNumberALittleOff_StillWorks()
        {
            var log = new StringBuilder();
            const int count = 8;
            Assert.AreEqual(count, Jittered(count, log), "\n" + log);
        }

        // The first thing a new player does: walk through the door as it stands. It is their own size, so
        // nothing happens - and until the review nothing was said either. Now: one line, once, and never
        // after the first trip (by then they know the rule).
        [Test]
        public void WalkingThroughTheDoorAsItStands_ChangesNothing_AndTheLevelSaysWhy_Once()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(bot.WalkTo(new Vector3(0f, 0f, 9.5f), 0.1f)));
            Assert.AreEqual(1f, Game.Player.Scale);
            Assert.AreEqual(0, level.Trips, "a door of the player's own size is just a door");
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.SameLine }, said);
            // Back through it, and round it instead of through it: nothing more.
            Assert.IsTrue(Runs(Chain(bot.WalkTo(new Vector3(0f, 0f, 6f), 0.1f), bot.Wait(7f), bot.WalkTo(new Vector3(0f, 0f, 9.5f), 0.1f), bot.WalkTo(new Vector3(2.5f, 0f, 8f), 0.1f), bot.WalkTo(new Vector3(0f, 0f, 5f), 0.1f))));
            Assert.AreEqual(1, said.Count, string.Join(" | ", said));
            // And the level is solved as ever (the solution's first pick-up is from the start).
            Assert.IsTrue(Attempt(Chain(bot.WalkTo(Level14InfiniteHallway.Spawn, 0.05f), level.Solve(bot))), Where(level));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.SameLine, Level14InfiniteHallway.TurnLine }, said);
        }

        // Past the door without going through its opening says nothing; nor does a door of their own size
        // once they have made a trip (the mouse and the 0.28 door: 0.3 is as small as they get).
        [Test]
        public void TheLineAboutADoorOfOnesOwnSize_IsNotSaidBesideTheDoor_NorAfterATrip()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(bot.WalkTo(new Vector3(1.6f, 0f, 6.5f), 0.1f), bot.WalkTo(new Vector3(1.6f, 0f, 9.5f), 0.1f), bot.WalkTo(new Vector3(-1.6f, 0f, 9.5f), 0.1f), bot.WalkTo(new Vector3(-1.6f, 0f, 6.5f), 0.1f))));
            Assert.AreEqual(0, said.Count, string.Join(" | ", said));
            Assert.IsTrue(Runs(Chain(bot.WalkTo(Level14InfiniteHallway.Spawn, 0.05f), level.Small(bot))));
            // The mouse walks back and forth through the little door.
            Vector3 at = level.Door.Position;
            Assert.IsTrue(Runs(Chain(bot.WalkTo(at + Vector3.back * 0.4f, 0.03f), bot.WalkTo(at + Vector3.forward * 0.4f, 0.03f), bot.WalkTo(at + Vector3.back * 0.4f, 0.03f))));
            Assert.AreEqual(1, level.Trips);
            Assert.AreEqual(0, said.Count, string.Join(" | ", said));
        }

        // Curiosity: the mouse makes the big door in the FIRST hall and walks in. A giant in a hall whose
        // only way on is a mouse-hole - nothing said that before. The way back is the same recursion: the
        // door from across the hall, at the feet, as often as it takes.
        static IEnumerator GrowInTheFirstHall(Bot bot, Level14InfiniteHallway level)
        {
            yield return level.Small(bot);
            yield return bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 4f);
            yield return bot.Grab(level.Door);
            yield return bot.WalkTo(new Vector3(0f, 0f, 5f), 0.05f, 15f, true);
            yield return LetGo(bot, level, 0f, 80f);
            yield return level.Through(bot);
        }

        static IEnumerator ShrinkAgain(Bot bot, Level14InfiniteHallway level, int times)
        {
            Player player = bot.Player;
            for (int i = 0; i < times && player.Scale > Level14InfiniteHallway.MouseLimit; i++)
            {
                // As far from the door as the hall lets them stand, then at the feet.
                float z = level.Door.Position.z > 7f ? 0.4f + player.Radius : Level14InfiniteHallway.WallZ - 0.4f - player.Radius;
                yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.2f * player.Scale, 10f);
                yield return bot.Grab(level.Door);
                yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(player.Yaw, -60f) * 2f);
                yield return bot.Until(() => level.Portal.Settled, 3f);
                yield return level.Through(bot);
            }
        }

        [Test]
        public void GrownInTheFirstHall_TheLevelSaysSo_AndTheSameRecursionLeadsBack()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(GrowInTheFirstHall(bot, level), 60f), Where(level));
            Assert.AreEqual(3.2f, Game.Player.Scale, 1e-3f, "a giant in the first hall: " + Where(level));
            Assert.AreEqual(Level14InfiniteHallway.Place.HallA, Level14InfiniteHallway.PlaceOf(Game.Player.Position));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.TurnLine, Level14InfiniteHallway.GrownLine }, said);
            Assert.IsFalse(Runs(ThroughTheHole(bot), 20f), "no way on at that size");

            Assert.IsTrue(Runs(ShrinkAgain(bot, level, 6), 90f), Where(level));
            Assert.LessOrEqual(Game.Player.Scale, Level14InfiniteHallway.MouseLimit, Where(level));
            Assert.LessOrEqual(level.Trips, 5, "the small trip, the big one and three more at the most: " + string.Join(" | ", said));
            Assert.IsTrue(Attempt(Chain(level.Big(bot), level.Climb(bot), level.Medium(bot)), 90f), Where(level));
        }

        // The other way up a ledge in this game is a step: a door half the player's size is as high as they
        // jump, and from its lintel the ledge would be one more hop for anybody from 1.4 up - who would then
        // walk straight out. The door is nothing to stand on (the player passes through all of it).
        [TestCase(1.4f)]
        [TestCase(1.7f)]
        [TestCase(2f)]
        [TestCase(2.3f)]
        public void TheDoorIsNoStepUpTheLedge(float size)
        {
            Level14InfiniteHallway level = LoadScripted();
            StandDoor(level, new Vector3(0f, 0f, 24.9f - 0.8f * 0.5f * size), 0.5f * size, 90f);    // fixture: edge-on, its end at the foot of the ledge
            Run(30);
            Assert.IsTrue(level.Portal.Settled);
            Assert.AreEqual(0f, level.Door.Position.y, 0.01f);
            Stand(new Vector3(0.25f * size, 0f, 20f), size);                                       // fixture: beside its plane, so that no hop is a trip
            Player player = Game.Player;
            player.Yaw = 0f;
            Input.Hold = new InputFrame { MoveZ = 1f };
            float highest = 0f;
            // Jump is mashed: whenever the feet are on anything, they jump.
            for (int i = 0; i < TestHelpers.Ticks(5f); i++)
            {
                if (player.Grounded) Input.Once.Jump = true;
                Game.Tick();
                highest = Mathf.Max(highest, player.Position.y);
            }
            Input.Hold = default;
            TestHelpers.RunUntil(Game, () => player.Grounded, 2f);
            Assert.Greater(player.Position.z, 24.9f - 0.3f * size - 0.2f, "they hopped all along the door, to the face of the ledge");
            // Its lintel is as high as they jump: a second jump from it would be twice that. (Measured without
            // any door: 1.28 of their size, 1.32 at 2.3 where the foot begins to roll on the kerb.)
            Assert.Less(highest, 1.36f * size, "never higher than their own jump");
            Assert.Less(highest, Face - 0.3f);
            Assert.Less(player.Position.y, 0.2f, "down in the hall");
            Assert.AreEqual(size, player.Scale, 1e-4f, "and no trip: they were beside the door's plane");
            Assert.AreEqual(0, level.Trips);
        }

        // Mashing jump (review, session 2). One jump gets a player of 2.48 up the ledge. Sprinting at it and
        // jumping whenever the feet touch anything, the round foot of the capsule now and then rolls on the
        // kerb's edge and somebody of 2.2 to 2.47 scrambles up as well (level14-review2-mash-straight.txt) -
        // too tall for the way out, all of them. What must never happen is that somebody who FITS the way out
        // gets up: under 2.16 the foot's round part does not reach the edge at all (1.28 + 0.3 sizes < 3.4).
        [TestCase(2f, 0f)]
        [TestCase(2f, 25f)]
        [TestCase(2.02f, 0f)]
        [TestCase(2.02f, 25f)]
        [TestCase(2.1f, 0f)]
        public void NobodyWhoFitsTheWayOut_ScramblesUpTheLedge_SprintingAndMashingJump(float size, float yaw)
        {
            foreach (float startZ in new[] { 16.5f, 17.3f, 18.1f, 18.9f, 19.7f, 20.5f, 21.3f, 22.1f })
            {
                LoadScripted();
                Stand(new Vector3(2.5f, 0f, startZ), size);                                          // fixture: in the second hall at that size
                Player player = Game.Player;
                player.Yaw = yaw;
                Input.Hold = new InputFrame { MoveZ = 1f, Sprint = true };
                float highest = 0f;
                for (int i = 0; i < TestHelpers.Ticks(3f); i++)
                {
                    if (player.Grounded) Input.Once.Jump = true;
                    Game.Tick();
                    highest = Mathf.Max(highest, player.Position.y);
                }
                Input.Hold = default;
                TestHelpers.RunUntil(Game, () => player.Grounded, 2f);
                string what = "a player of " + size + " from z " + startZ + " at " + yaw + " degrees: highest " + F(highest) + ", ends at " + F(player.Position);
                Assert.Less(player.Position.y, 0.2f, what);
                Assert.Less(highest + player.Radius, Face, what + " (the round of the foot stays under the edge)");
                DisposeGame();
            }
        }

        // The same, sixty times over (explicit; writes tools/out/notes/level14-probe-jitter.txt).
        [Test, Explicit]
        public void Probe_Jitter()
        {
            var log = new StringBuilder();
            const int count = 60;
            int ok = Jittered(count, log);
            log.Insert(0, ok + " of " + count + " completed\n");
            Write("level14-probe-jitter.txt", log);
        }

        // ==== Review, session 3 ==================================================================================
        // (Probes and their tables: tools/out/notes/level14-review3-probes.cs.txt, level14-review3-*.txt.)

        // BYPASS, closed in the gadget (PortalDoorway.Carried). A trip multiplied the player's velocity by the
        // ratio of the sizes, and the height of a jump goes with the square of its speed: the hop of a mouse
        // into a door of 1.1 to 2 came out as a leap of four to eight units - onto the ledge at a size that
        // fits the way out. The level was completed in two trips (level14-review3-jumpthrough-before.txt).
        // Now a jump into the door is the same jump at the new size, and nobody of 2 or less gets up.
        [TestCase(0.3f, 1.3f)]
        [TestCase(0.3f, 2f)]
        [TestCase(1f, 2f)]
        [TestCase(1.5f, 2f)]
        public void AJumpIntoTheDoor_ThrowsNobodyUpTheLedge(float from, float to)
        {
            foreach (float doorZ in new[] { 18.6f, 21f, 23f })
                foreach (bool sprint in new[] { false, true })
                    foreach (float lead in new[] { 0.05f, 0.15f, 0.3f, 0.5f })
                    {
                        Level14InfiniteHallway level = LoadScripted();
                        Stand(new Vector3(0f, 0f, Mathf.Max(doorZ - 2.5f * from - 0.3f, Level14InfiniteHallway.HallBZ + 0.4f * from + 0.05f)), from);   // fixture: in the second hall at that size
                        StandDoor(level, new Vector3(0f, 0f, doorZ), to);                                                                          // fixture
                        Player player = Game.Player;
                        player.Yaw = 0f;
                        Input.Hold = new InputFrame { MoveZ = 1f, Sprint = sprint };
                        bool jumped = false;
                        float highest = 0f;
                        for (int i = 0; i < TestHelpers.Ticks(3f); i++)
                        {
                            if (!jumped && player.Position.z >= doorZ - lead * from)
                            {
                                jumped = true;
                                Input.Once.Jump = true;
                            }
                            Game.Tick();
                            highest = Mathf.Max(highest, player.Position.y);
                        }
                        Input.Hold = default;
                        TestHelpers.RunUntil(Game, () => player.Grounded, 3f);
                        string what = "a player of " + from + (sprint ? " sprints" : " walks") + " at a door of " + to + " at z " + doorZ + " and jumps " + lead + " sizes before it: highest " + F(highest) + ", ends at " + F(player.Position);
                        Assert.AreEqual(1, level.Trips, what);
                        Assert.AreEqual(to, player.Scale, 1e-4f, what);
                        Assert.Less(highest, 1.33f * to, what + " (a jump at the new size, no more)");
                        Assert.Less(highest + player.Radius, Face, what + " (the round of the foot stays under the edge)");
                        Assert.Less(player.Position.y, 0.2f, what + " (down in the hall)");
                        Assert.IsFalse(Game.LevelCompleted);
                        DisposeGame();
                    }
        }

        // Sprints (or walks) at the door from the side the bot is on and jumps so far before its plane.
        static IEnumerator JumpInto(Bot bot, Level14InfiniteHallway level, float lead, bool sprint)
        {
            Player player = bot.Player;
            Vector3 at = level.Door.Position;
            float size = player.Scale;
            yield return bot.WalkTo(new Vector3(at.x, 0f, Mathf.Max(at.z - 3f * size, Level14InfiniteHallway.HallBZ + 0.5f * size)), 0.1f * size, 6f);
            IEnumerator walk = bot.WalkTo(new Vector3(at.x, 0f, Level14InfiniteHallway.TerraceZ + 3f), 0.3f, 4f, sprint);
            bool jumped = false;
            while (walk.MoveNext())
            {
                yield return walk.Current;
                if (!jumped && player.Position.z >= at.z - lead * size)
                {
                    jumped = true;
                    yield return bot.Jump();
                }
            }
        }

        // The same by input only. These scripts completed the level in two trips before the fix: small, the
        // door carried into the second hall and let go looking only half way up (a door of 1.1 or 1.5), and
        // a running jump into it.
        [TestCase(35f, 0.1f, false)]
        [TestCase(35f, 0.1f, true)]
        [TestCase(40f, 0.3f, false)]
        [TestCase(40f, 0.5f, true)]
        public void ARunningJumpIntoAHalfGrownDoor_IsNoWayUpTheLedge(float weakPitch, float lead, bool sprint)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(level.Small(bot), CarryThroughAndLetGo(bot, level, 0.42f, weakPitch)), 60f), Where(level));
            float doorScale = level.Door.Scale;
            Assert.Greater(doorScale, 1f, "bigger than the mouse: " + Where(level));
            Assert.Less(doorScale, Level14InfiniteHallway.ExitScale, "and small enough for the way out");
            float highest = 0f;
            Game.Events.PlayerLanded += e => highest = Mathf.Max(highest, e.Position.y);
            Runs(JumpInto(bot, level, lead, sprint), 20f);      // it ends walking at the face of the ledge until its command times out
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
            Assert.AreEqual(2, level.Trips, "the jump was a trip through the door: " + Where(level));
            Assert.AreEqual(doorScale, Game.Player.Scale, 1e-4f);
            Assert.Less(Game.Player.Position.y, 0.2f, "down in the hall, at the foot of the ledge: " + Where(level));
            Assert.Less(highest, 0.2f, "and never landed anywhere higher");
            Assert.IsFalse(Attempt(level.GoOut(bot), 15f), "no way out in two trips");
            CollectionAssert.Contains(said, Level14InfiniteHallway.StillShortLine, "told what the door still has to do");

            // From there the level is solved as it is meant to be: the door picked up again a step back from
            // it, let go looking straight up out in the open, walked into; the ledge; the last trip.
            Assert.IsTrue(Runs(FromTheOpen(bot, level), 40f), Where(level));
            Assert.GreaterOrEqual(level.Door.Scale, Level14InfiniteHallway.LedgeScale, Where(level));
            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            Assert.AreEqual(4, level.Trips);
        }

        // SOFT-LOCK tries that found nothing (kept: neither place was covered). A small door let go into the
        // way out while the giant is still a giant: they cannot follow it. From in front of the opening it is
        // in sight - told, picked up again, and the last trip is made. From the side of the ledge it is not -
        // told about the plates; down into the hall, called back, up again.
        [TestCase(0f, 0.9f, 31.7f, true)]
        [TestCase(0f, 0.3f, 33.5f, true)]
        [TestCase(-3.6f, 0.3f, 33.5f, false)]
        public void TheDoorInTheWayOut_WhereAGiantCannotFollow_IsPickedUpAgainOrCalledBack_AndTheLevelIsSolved(float standX, float doorScale, float doorZ, bool inSight)
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(level.Small(bot), level.Big(bot), level.Climb(bot), bot.WalkTo(new Vector3(standX, Ledge, 28f), 0.3f, 6f))), Where(level));
            said.Clear();
            StandDoor(level, new Vector3(0f, Ledge, doorZ), doorScale);                 // fixture: the door in the way out
            RunSeconds(1f);
            Assert.IsEmpty(said, "not before a second and a half");
            RunSeconds(1.2f);
            CollectionAssert.AreEqual(new[] { inSight ? Level14InfiniteHallway.ReachLine : Level14InfiniteHallway.LostLine }, said, Where(level));
            // Up to the end wall after it: from right under the lintel a door deep in the way out is hidden, and
            // then the level says where the plates are as well. Each line once, and nothing else.
            Assert.IsFalse(Attempt(level.GoOut(bot), 12f), "a giant does not get to it");
            CollectionAssert.IsSubsetOf(said, new[] { Level14InfiniteHallway.ReachLine, Level14InfiniteHallway.LostLine }, string.Join(" | ", said));
            CollectionAssert.AllItemsAreUnique(said);

            if (inSight)
            {
                Assert.IsTrue(Attempt(Chain(bot.WalkTo(StandT, 0.3f, 6f), level.Medium(bot)), 60f), Where(level));
                return;
            }
            // Down over the kerb, onto the plate: the door comes at a giant's size, in the middle of the hall.
            Assert.IsTrue(Runs(Chain(bot.WalkTo(new Vector3(-3f, 0f, 17.3f), 0.4f, 20f), bot.Until(() => level.RecallB.Recalls > 0, 3f), bot.Wait(0.3f), bot.Until(() => level.Portal.Settled, 2f)), 40f), Where(level));
            Assert.AreEqual(3.2f, level.Door.Scale, 1e-3f, "it comes at the size the player is");
            Assert.AreEqual(Level14InfiniteHallway.Place.HallB, Level14InfiniteHallway.PlaceOf(level.Door.Position));
            Assert.IsTrue(Attempt(Chain(level.Climb(bot), level.Medium(bot)), 60f), Where(level));
            CollectionAssert.AreEqual(new[] { Level14InfiniteHallway.LostLine }, said, "nothing more was said");
        }

        // The last trip made, and then - instead of walking out - a jump back down over the kerb. A player of
        // 1.3 in the second hall, the door up on the ledge. It is told (in sight: pick it up; out of sight: the
        // plate); the plate brings it at their size, and from there it is the second trip again.
        static IEnumerator JumpBackDown(Bot bot)
        {
            Player player = bot.Player;
            yield return bot.WalkTo(new Vector3(0f, Ledge, 26.6f), 0.2f, 6f);
            IEnumerator walk = bot.WalkTo(new Vector3(0f, 0f, 22.5f), 0.4f, 6f, true);
            bool jumped = false;
            while (walk.MoveNext())
            {
                yield return walk.Current;
                if (!jumped && player.Position.z < 26f)
                {
                    jumped = true;
                    yield return bot.Jump();
                }
            }
            yield return bot.Until(() => player.Grounded, 3f);
        }

        [Test]
        public void BackDownInTheHallAfterTheLastTrip_TheDoorLeftUpOnTheLedge_ComesBackFromThePlate_AndTheLevelIsSolved()
        {
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Assert.IsTrue(Runs(Chain(level.Small(bot), level.Big(bot), level.Climb(bot), PickUpThenLetGo(bot, level, 0f, -60f), level.Through(bot))), Where(level));
            float size = Game.Player.Scale;
            Assert.Less(size, Level14InfiniteHallway.ExitScale, "small enough for the way out");
            said.Clear();
            Assert.IsTrue(Runs(JumpBackDown(bot), 20f), Where(level));
            Assert.Less(Game.Player.Position.y, 0.2f, "down in the hall: " + Where(level));
            Assert.Greater(level.Door.Position.y, Ledge - 0.01f, "the door is up on the ledge");
            RunSeconds(2f);
            Assert.AreEqual(1, said.Count, string.Join(" | ", said));
            Assert.IsTrue(said[0] == Level14InfiniteHallway.ReachLine || said[0] == Level14InfiniteHallway.LostLine, said[0]);
            // At the foot of the ledge it is out of sight: the plates are named too. Each line once.
            Assert.IsFalse(Runs(level.Climb(bot), 20f), "and a player of " + size + " does not get back up");
            CollectionAssert.IsSubsetOf(said, new[] { Level14InfiniteHallway.ReachLine, Level14InfiniteHallway.LostLine }, string.Join(" | ", said));
            CollectionAssert.AllItemsAreUnique(said);

            Assert.IsTrue(Runs(OntoThePlate(bot, level.RecallB, level.Portal), 30f), Where(level));
            Assert.AreEqual(size, level.Door.Scale, 1e-3f, "it comes at the size the player is");
            Assert.IsTrue(Runs(FromTheOpen(bot, level), 40f), Where(level));
            Assert.GreaterOrEqual(level.Door.Scale, Level14InfiniteHallway.LedgeScale, Where(level));
            Assert.IsTrue(Attempt(Chain(level.Through(bot), level.Climb(bot), level.Medium(bot)), 60f), Where(level));
        }

        // ---- The monkey: a bot that does plausible things at random, watched tick by tick ------------------------

        // What must never happen, whatever is done: through the dividing wall at more than a mouse's size, up
        // on the ledge without having been big enough to jump it since they last stood up there (a giant who
        // jumps and passes through a door on the kerb in mid-air lands small, and rightly), out of the
        // hallway. Checked every tick (reads only).
        List<string> WatchTheGates()
        {
            var broken = new List<string>();
            Player player = Game.Player;
            float lastZ = player.Position.z;
            bool below = true;
            float biggest = player.Scale;
            const float middle = Level14InfiniteHallway.WallZ + 0.5f;
            Game.Context.OnUpdate(dt =>
            {
                Vector3 at = player.Position;
                float size = player.Scale;
                if ((lastZ < middle) != (at.z < middle) && size > 0.48f) broken.Add("through the dividing wall at " + F(size) + ", at " + F(at) + ", t " + F(Game.Time));
                lastZ = at.z;
                // (A landing presses the feet a few hundredths into the floor for a tick.)
                if (Mathf.Abs(at.x) > 5.01f || at.z < -0.01f || at.z > Level14InfiniteHallway.TunnelEnd + 0.01f || at.y < -0.1f - 0.1f * size || at.y > Level14InfiniteHallway.WallTop)
                    broken.Add("out of the hallway at " + F(at) + ", t " + F(Game.Time));
                if (!player.Grounded)
                {
                    if (below) biggest = Mathf.Max(biggest, size);
                    return;
                }
                // On the ledge (nothing else in the hallway is this high; a hard landing dips a tenth under its floor).
                if (at.y > Ledge - 0.9f)
                {
                    if (below && biggest < 2.15f) broken.Add("up on the ledge at " + F(size) + " (no bigger than " + F(biggest) + " since the hall's floor), at " + F(at) + ", t " + F(Game.Time));
                    below = false;
                    return;
                }
                // On a hall's floor: from here on it is this size, or whatever a door in the air makes of it.
                biggest = size;
                below = true;
            });
            return broken;
        }

        // Walks at a point, jumping whenever the feet are on anything.
        static IEnumerator MashAt(Bot bot, Vector3 target, float seconds, bool sprint)
        {
            Player player = bot.Player;
            IEnumerator walk = bot.WalkTo(target, 0.2f, seconds, sprint);
            while (walk.MoveNext())
            {
                yield return walk.Current;
                if (player.Grounded) yield return bot.Jump();
            }
        }

        // At the door from whichever side the bot is on, with a jump so far before its plane - and, once the
        // door has done what it does, on towards a point (the gate ahead) while still in the air.
        static IEnumerator JumpIntoFromHere(Bot bot, Level14InfiniteHallway level, float lead, bool sprint, Vector3 then)
        {
            Player player = bot.Player;
            Vector3 at = level.Door.Position;
            Vector3 normal = level.Portal.Normal;
            normal.y = 0f;
            normal.Normalize();
            if (Vector3.Dot(normal, at - player.Position) < 0f) normal = -normal;
            float size = player.Scale;
            yield return bot.WalkTo(at - normal * (2.5f * size), 0.15f * size, 4f);
            IEnumerator walk = bot.WalkTo(at + normal * (3f * level.Portal.TargetScale + 2f), 0.3f, 3f, sprint);
            bool jumped = false;
            int before = level.Trips;
            while (level.Trips == before && walk.MoveNext())
            {
                yield return walk.Current;
                if (!jumped && Vector3.Dot(at - player.Position, normal) <= lead * size)
                {
                    jumped = true;
                    yield return bot.Jump();
                }
            }
            yield return bot.WalkTo(then, 0.3f, 2.5f, sprint);
        }

        // One thing a player might do next, picked at random; `what` gets a letter for it.
        IEnumerator MonkeyAction(Bot bot, Level14InfiniteHallway level, System.Random random, StringBuilder what)
        {
            Player player = bot.Player;
            Game game = bot.Game;
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            float size = player.Scale;
            Level14InfiniteHallway.Place place = Level14InfiniteHallway.PlaceOf(player.Position);
            bool high = player.Position.y > Ledge - 0.5f;
            float floor = high ? Ledge : 0f;
            float zMin = high ? 25.5f : place == Level14InfiniteHallway.Place.HallA ? 0.3f : 15.3f;
            float zMax = high ? 30.7f : place == Level14InfiniteHallway.Place.HallA ? 13.7f : 24.7f;
            float margin = player.Radius + 0.1f;
            Vector3 Somewhere() => new Vector3(R(-5f + margin, 5f - margin), floor, R(Mathf.Min(zMin + margin, zMax), Mathf.Max(zMax - margin, zMin)));
            bool sprint = random.Next(2) == 0;

            if (game.Grabber.IsHolding)
            {
                int held = random.Next(5);
                if (held == 0)
                {
                    what.Append('q');
                    yield return bot.RotateHeld(random.Next(1, 12));
                }
                if (held <= 2)
                {
                    // Down at the feet, along the floor, half way up (where a door comes out between the sizes of
                    // the gates), up.
                    int way = random.Next(4);
                    float yaw = R(-180f, 180f), pitch = way == 0 ? R(-89f, -40f) : way == 1 ? R(-40f, 25f) : way == 2 ? R(25f, 50f) : R(50f, 89f);
                    what.Append("L(" + pitch.ToString("0", CultureInfo.InvariantCulture) + ")");
                    yield return bot.DropAt(player.Eye + Level14InfiniteHallway.View(yaw, pitch) * 5f, 3f);
                    yield return bot.Until(() => level.Portal.Settled, 4f);
                    // What one does with a door one has just put down: walk into it, or jump into it.
                    int next = random.Next(10);
                    if (next < 4)
                    {
                        what.Append('J');
                        yield return JumpIntoFromHere(bot, level, R(0.05f, 0.6f), sprint,
                            high ? new Vector3(R(-0.5f, 0.5f), Ledge, 33.5f) : place == Level14InfiniteHallway.Place.HallA ? new Vector3(0f, 0f, 16.5f) : new Vector3(R(-3f, 3f), 0f, 27f));
                    }
                    else if (next < 7)
                    {
                        what.Append('T');
                        yield return level.Through(bot);
                    }
                }
                else if (held == 3)
                {
                    what.Append('c');
                    yield return bot.WalkTo(Somewhere(), 0.2f * size, 4f, sprint);
                }
                else
                {
                    // On to the next hall with it, if the gate lets them.
                    what.Append('C');
                    if (high) yield return bot.WalkTo(new Vector3(0f, Ledge, 28f), 0.3f, 4f, sprint);
                    else if (place == Level14InfiniteHallway.Place.HallA)
                    {
                        yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.4f - 0.5f * size), 0.05f, 6f, sprint);
                        yield return bot.WalkTo(new Vector3(R(-1f, 1f), 0f, R(15.6f, 19f)), 0.1f, 4f, sprint);
                    }
                    else yield return MashAt(bot, new Vector3(0f, 0f, 27f), 3f, sprint);
                }
                yield break;
            }

            switch (random.Next(11))
            {
                case 0:
                case 1:
                case 2:
                    what.Append('P');
                    yield return bot.Grab(level.Door, 2f);
                    break;
                case 3:
                    what.Append('w');
                    yield return bot.WalkTo(Somewhere(), 0.2f * size, 4f, sprint);
                    break;
                case 4:
                case 5:
                    what.Append('T');
                    yield return level.Through(bot);
                    break;
                case 6:
                    what.Append('J');
                    yield return JumpIntoFromHere(bot, level, R(0.05f, 0.6f), sprint,
                        high ? new Vector3(R(-0.5f, 0.5f), Ledge, 33.5f) : place == Level14InfiniteHallway.Place.HallA ? new Vector3(0f, 0f, 16.5f) : new Vector3(R(-3f, 3f), 0f, 27f));
                    break;
                case 7:
                    // At the gate ahead.
                    what.Append('G');
                    if (high) yield return MashAt(bot, new Vector3(R(-0.5f, 0.5f), Ledge, 33.5f), 3f, sprint);
                    else if (place == Level14InfiniteHallway.Place.HallA)
                    {
                        yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.WallZ - 0.4f - 0.5f * size), 0.05f, 6f, sprint);
                        yield return MashAt(bot, new Vector3(0f, 0f, 16.5f), 3f, sprint);
                    }
                    else yield return MashAt(bot, new Vector3(R(-4f, 4f), 0f, 27f), 4f, sprint);
                    break;
                case 8:
                    // Back the way they came.
                    what.Append('B');
                    if (high) yield return MashAt(bot, new Vector3(R(-3f, 3f), 0f, 21f), 3f, sprint);
                    else if (place != Level14InfiniteHallway.Place.HallA)
                    {
                        yield return bot.WalkTo(new Vector3(0f, 0f, Level14InfiniteHallway.HallBZ + 0.4f + 0.5f * size), 0.05f, 6f, sprint);
                        yield return MashAt(bot, new Vector3(0f, 0f, 12f), 3f, sprint);
                    }
                    else yield return bot.WalkTo(new Vector3(R(-3f, 3f), 0f, 1.5f), 0.3f, 5f, sprint);
                    break;
                case 9:
                    what.Append('O');
                    RecallPad pad = place == Level14InfiniteHallway.Place.HallA ? level.RecallA : level.RecallB;
                    yield return bot.WalkTo(pad.Position, 0.15f * Mathf.Min(1f, size), 8f, sprint);
                    yield return bot.Wait(0.8f);
                    break;
                default:
                    what.Append('.');
                    yield return bot.Wait(1.7f);
                    break;
            }
        }

        // From wherever the monkey left things: is the level still there to be solved by the means it gives -
        // the plates and trip after trip? Round after round the bot looks at where it is and how big, and
        // does what that place asks for: in the first hall the plate, then the door from across the hall at
        // the feet until the mouse-hole lets it through, then the big door; in the second hall the plate,
        // then the door from a step back, looking up, until the ledge can be jumped; on the ledge the last
        // trip, or the way out. A step that fails is not the end: the next round looks again.
        bool Recover(Bot bot, Level14InfiniteHallway level, StringBuilder how)
        {
            Player player = Game.Player;
            if (Game.Grabber.IsHolding) Runs(Chain(bot.Drop(), bot.Until(() => level.Portal.Settled, 4f)), 10f);
            for (int round = 0; round < 14 && !Game.LevelCompleted; round++)
            {
                TestHelpers.RunUntil(Game, () => player.Grounded, 3f);
                float size = player.Scale;
                Level14InfiniteHallway.Place place = Level14InfiniteHallway.PlaceOf(player.Position);
                if (player.Position.y > Ledge - 0.5f)
                {
                    if (size <= Level14InfiniteHallway.ExitLimit)
                    {
                        how.Append("out ");
                        Attempt(level.GoOut(bot), 30f);
                    }
                    else if (Runs(Chain(bot.WalkTo(StandT, 0.3f, 6f), level.Medium(bot)), 40f)) how.Append("medium ");
                    else
                    {
                        // The door is not to be had from up here: down over the kerb, to the plate.
                        how.Append("down ");
                        Runs(MashAt(bot, new Vector3(0f, 0f, 21.5f), 5f, false), 10f);
                    }
                }
                else if (place == Level14InfiniteHallway.Place.Hole)
                {
                    how.Append("hole ");
                    Runs(bot.WalkTo(new Vector3(0f, 0f, 16.5f), 0.2f, 6f), 10f);
                }
                else if (place == Level14InfiniteHallway.Place.HallA)
                {
                    // (A walk to the plate that got nowhere: the bot stands where a jump took it and no walk
                    // leads out - in the corner behind the spool, say. Out as a player gets out: walking and jumping.)
                    if (!Runs(RecallHere(bot, level, level.RecallA, new Vector3(1.5f, 0f, 6f)), 40f)) Runs(MashAt(bot, new Vector3(0f, 0f, 7f), 6f, false), 10f);
                    if (size > Level14InfiniteHallway.MouseLimit)
                    {
                        how.Append("shrink ");
                        Runs(ShrinkAgain(bot, level, 8), 120f);
                    }
                    else
                    {
                        how.Append("big ");
                        Runs(level.Big(bot), 60f);
                    }
                }
                else
                {
                    if (!Runs(RecallHere(bot, level, level.RecallB, new Vector3(1.5f, 0f, 20f)), 40f)) Runs(MashAt(bot, new Vector3(0f, 0f, 20f), 6f, false), 10f);
                    if (size >= Level14InfiniteHallway.LedgeLimit)
                    {
                        how.Append("climb ");
                        Runs(Chain(level.Climb(bot), level.Medium(bot)), 60f);
                    }
                    else
                    {
                        how.Append("grow ");
                        Runs(Chain(FromTheOpen(bot, level), level.Through(bot)), 40f);
                    }
                }
            }
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            return Game.LevelCompleted;
        }

        // Onto the plate of the hall the bot is in (off it first, should the bot stand on it: one fetch per
        // visit), and there until the door has come and stands.
        static IEnumerator RecallHere(Bot bot, Level14InfiniteHallway level, RecallPad pad, Vector3 aside)
        {
            Player player = bot.Player;
            if (pad.PlayerOn) yield return bot.WalkTo(aside, 0.4f, 8f);
            int before = pad.Recalls;
            yield return bot.WalkTo(pad.Position, 0.15f * Mathf.Clamp(player.Scale, 0.3f, 2f), 20f);
            yield return bot.Until(() => pad.Recalls > before, 3f);
            yield return bot.Wait(0.3f);
            yield return bot.Until(() => level.Portal.Settled, 3f);
        }

        // An episode of the monkey: where it starts (0 the start, 1 a mouse, 2 a mouse in the second hall
        // with the door in hand, 3 a giant in the second hall, 4 a giant on the ledge, 5 a mouse in the
        // second hall beside a half-grown door), so many random actions, and then the recovery. Returns
        // what went wrong, if anything.
        string MonkeyEpisode(int seed, int actions, StringBuilder log)
        {
            var random = new System.Random(seed);
            int mode = seed % 6;
            Level14InfiniteHallway level = Load();
            List<string> said = Listen();
            List<string> broken = WatchTheGates();
            var bot = new Bot(Game);
            bool ready = true;
            if (mode >= 1) ready &= Runs(level.Small(bot));
            if (mode == 2) ready &= Runs(Chain(bot.WalkTo(level.Door.Position + Vector3.forward * 0.42f, 0.03f, 4f), bot.Grab(level.Door), bot.WalkTo(new Vector3(0f, 0f, 13.5f), 0.05f, 15f, true), bot.WalkTo(StandB, 0.05f, 15f, true)));
            if (mode == 3 || mode == 4) ready &= Runs(level.Big(bot));
            if (mode == 4) ready &= Runs(level.Climb(bot));
            if (mode == 5) ready &= Runs(CarryThroughAndLetGo(bot, level, 0.42f, 30f + 17f * (float)random.NextDouble()));
            var what = new StringBuilder();
            for (int i = 0; i < actions && !Game.LevelCompleted; i++)
            {
                bool ran = Runs(MonkeyAction(bot, level, random, what), 20f);
                what.Append(ran ? " " : "! ");
            }
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
            string state = "player " + F(Game.Player.Scale) + " at " + F(Game.Player.Position) + ", door " + F(level.Door.Scale) + " at " + F(level.Door.Position) + (Game.Grabber.IsHolding ? " (in the hand)" : "") +
                           ", trips " + level.Trips + ", crossings " + level.Portal.Crossings;
            string wrong = null;
            if (!ready) wrong = "the set-up did not run";
            else if (broken.Count > 0) wrong = "GATE BROKEN: " + string.Join("; ", broken);
            else if (Game.LevelCompleted && level.Portal.Crossings < 3) wrong = "COMPLETED after " + level.Portal.Crossings + " crossings";
            bool byItself = Game.LevelCompleted;
            var how = new StringBuilder();
            if (wrong == null && !byItself)
            {
                bool recovered = Recover(bot, level, how);
                if (broken.Count > 0) wrong = "GATE BROKEN in the recovery: " + string.Join("; ", broken);
                else if (!recovered) wrong = "NOT RECOVERED: " + Where(level);
            }
            log.AppendLine((wrong == null ? "ok   " : "FAIL ") + "seed " + seed + " mode " + mode + ": " + what + "| " + state + (byItself ? " | COMPLETED by the monkey" : " | then: " + how) + (wrong != null ? " | " + wrong : "") +
                           (said.Count > 0 ? " | said: " + string.Join(" / ", said) : ""));
            DisposeGame();
            return wrong;
        }

        // A dozen episodes, two from each start. A failure here is either a gate that gave way (GATE
        // BROKEN, COMPLETED after fewer than three crossings: a bypass) or a state the recovery did not solve
        // (NOT RECOVERED: a soft-lock, or the recovery script tripping over something new - Probe_Monkey
        // writes the whole table to tools/out/notes/level14-probe-monkey-<seed>.txt).
        [Test]
        public void ARandomPlayer_BreaksNoGate_AndTheLevelCanAlwaysStillBeSolved()
        {
            var log = new StringBuilder();
            int failed = 0;
            for (int seed = 0; seed < 12; seed++)
                if (MonkeyEpisode(seed, 14, log) != null) failed++;
            Assert.AreEqual(0, failed, "\n" + log);
        }

        // The same for as many episodes as fit in the sweep's time (explicit; about 140 a run).
        [Explicit]
        [TestCase(0, 14)]
        [TestCase(1000, 14)]
        [TestCase(2000, 24)]
        public void Probe_Monkey(int firstSeed, int actions)
        {
            var log = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int failed = 0, episodes = 0;
            for (int seed = firstSeed; seed < firstSeed + 400; seed++)
            {
                if (clock.Elapsed.TotalSeconds > ProbeBudget) break;
                if (MonkeyEpisode(seed, actions, log) != null) failed++;
                episodes++;
            }
            log.Insert(0, episodes + " episodes of " + actions + " actions from seed " + firstSeed + ", " + failed + " failed, " + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s\n");
            Write("level14-probe-monkey-" + firstSeed + ".txt", log);
            Assert.AreEqual(0, failed, "see tools/out/notes/level14-probe-monkey-" + firstSeed + ".txt");
        }
    }

    /// <summary>
    /// Level 14 on the pause card: every hint is set at the panel's full size, as in the levels before it
    /// (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize does the same for levels 5 to 9).
    /// </summary>
    public class Level14UiTests
    {
        GameObject host;
        GameRunner runner;

        [SetUp]
        public void NewStores()
        {
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
        }

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        [Test]
        public void EveryHint_FitsThePauseCardsPanel_AtFullSize()
        {
            host = new GameObject("Level 14 Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=14"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            devices.State.EscapePressed = false;
            PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            Assert.AreEqual(14, runner.Game.Level.Id);
            string[] hints = runner.Game.Level.Hints;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                runner.Frame(Sim.Dt);
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                Debug.Log("[Toybox] Level 14, hint " + (h + 1) + ": " + hints[h].Length + " characters, " + pause.HintText.textInfo.lineCount + " lines at size " + size.ToString("0.0"));
                Assert.Greater(pause.HintText.textInfo.lineCount, 0, "the text was laid out");
                Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }
}
