using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using L = Toybox.Levels.Level12Keyhole;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 12, "The Keyhole": the key is the bridge, and nobody can pick up what they stand on. The key
    /// has to be picked up from the chest (and put down small), the spool has to fill the drawer, and the
    /// key is carried across the spool to the lock.
    ///
    /// The sizes (LEVELS appendix B): the lock takes a key of 0.06 to 0.5; the spool carries the player to
    /// the door from 8.35 across to its largest size, 9.9 (intended 9.1). Measured: tools/out/notes/level12-build.md.
    ///
    /// Since the review (tools/out/notes/level12-review.md): the paint is the spool's gauge (green: it would
    /// fill the drawer from where it is held), the drawer draws a spool that is big enough to its middle
    /// against the door wall, the spool cannot be flipped, the key gets no smaller than its lock takes, and
    /// the lock takes a key let go anywhere in front of the door's face. Since the second review: a spool
    /// picked up in mid-air gets no bigger until the player has landed (no riding it up into a tower), and
    /// whoever is high over the drawer is sent back to the start (no leap from a tower through the open
    /// door). Since the third review: the door stops things at the wall's own face and nothing but the key
    /// holds a spool up over the drawer (it hung on the door's sill, 0.015 deep, and was a place to unlock
    /// from), the two shoe-print marks are where the paint is green, and a spool that comes back to where
    /// the key lies by then puts the key on top of itself. Pictures of other moments than the solver's:
    /// Level12Stills, at the end of this file.
    /// </summary>
    public class Level12Tests : SimTest
    {
        Level12Keyhole level;
        Bot bot;

        void Load()
        {
            // (A test that goes through several cases loads the level afresh for each.)
            Game?.Dispose();
            Game = Game.Create();
            Game.LoadLevel(12);
            level = (Level12Keyhole)Game.Level;
            bot = new Bot(Game);
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        // Pumps a bot script for at most this long. True if it ran to its end; a command that fails (a walk
        // that never arrives) ends it too, and that is not an error here.
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

        static string F(Vector3 v) => "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";

        string State() =>
            "bot at " + F(Game.Player.Position) + ", key " + level.Key.Scale.ToString("0.000") + " at " + F(level.Key.Center) + ", spool " + level.Spool.Scale.ToString("0.00") + " at " +
            F(level.Spool.Center) + ", door " + (level.Door.IsOpen ? "open" : "shut") + ", t " + Game.Time.ToString("0.0");

        static string Told(List<string> said) => said.Count == 0 ? "nothing said" : "said: " + string.Join(" | ", said);

        bool KeyIsTheBridge => level.Key.Frozen && Vector3.Distance(level.Key.Center, L.KeyCentre) < 0.02f && Mathf.Abs(level.Key.Scale - L.KeyStart) < 1e-3f;

        bool KeyIsOnTheShelf
        {
            get
            {
                Prop key = level.Key;
                Vector3 at = key.Center;
                return !key.Held && !key.Frozen && at.y > L.ShelfTop - 0.02f && at.y < L.ShelfTop + 0.3f && at.x > L.ShelfMinX && at.x < L.ShelfMaxX && at.z < L.ShelfFront + 0.3f;
            }
        }

        bool SpoolIsHome => level.Spool.Frozen && Vector3.Distance(level.Spool.Center, L.SpoolCentre) < 0.02f && Mathf.Abs(level.Spool.Scale - L.SpoolStart) < 1e-3f;

        float SpoolTop => level.Spool.Center.y + 0.5f * level.Spool.Scale;

        void AssertCompleted(string what)
        {
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, what + " (" + State() + ")");
        }

        // ---- Pieces of the solution ----------------------------------------------------------------------------

        // The first half: the key off the drawer and onto the little shelf.
        IEnumerator ShelveTheKey()
        {
            yield return bot.Grab(level.Key);
            yield return level.PutKeyOnShelf(bot);
        }

        // What the paint said about the spool in the hand, a moment before it was let go (HoldSpool notes it).
        FitState paintAtLetGo;

        // Picks the spool up from one place, holds it toward a point from another, lets go.
        IEnumerator HoldSpool(Vector3 pickUpFrom, Vector3 letGoFrom, Vector3 aim)
        {
            paintAtLetGo = FitState.Idle;
            yield return bot.WalkTo(pickUpFrom, 0.05f, 12f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(letGoFrom, 0.1f, 12f);
            yield return bot.LookAt(aim);
            yield return bot.Wait(0.05f);
            paintAtLetGo = level.SpoolTint;
            yield return bot.DropAt(aim);
        }

        // The spool again from wherever it stands and wherever the bot is: picked up, and let go from the mark.
        IEnumerator SpoolAgain()
        {
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(L.ThrowStand, 0.1f, 12f);
            yield return bot.DropAt(L.OutlineCentre);
            yield return bot.Until(() => level.SpoolFills, 6f);
        }

        // The key from the shelf (or from wherever it lies in view of the shelf's mark), over the spool, to the
        // lock, and out. fromDoor: how far from the door to let go (0: where the crossing ends, a step from it).
        IEnumerator KeyToTheLock(float fromDoor = 0f)
        {
            yield return bot.WalkTo(L.ShelfStand, 0.15f);
            yield return bot.Grab(level.Key);
            yield return level.Cross(bot);
            if (fromDoor > 0f) yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - fromDoor), 0.05f, 6f);
            yield return bot.DropAt(L.Keyhole);
            yield return level.Leave(bot);
        }

        // With the key on the shelf and the spool where it started: the rest of the solution.
        IEnumerator TheRest()
        {
            yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
            yield return KeyToTheLock();
        }

        // The spool put in the drawer at a size (a test fixture's shortcut for a perfect let-go): against the
        // door wall, or against the chest.
        void SpoolInTheDrawer(float scale, bool againstTheDoor = true, float x = 0f, float lift = 0.02f)
        {
            Prop spool = level.Spool;
            spool.Unfreeze();
            spool.SetScale(scale);
            float radius = ToyFactory.ThreadSpoolRadius * scale;
            float z = againstTheDoor ? L.DoorZ - radius - 0.05f : L.EdgeZ + radius + 0.05f;
            spool.SetPose(new Vector3(x, L.PitFloor + 0.5f * scale + lift, z), Quaternion.identity);
        }

        // The key in the air at the keyhole at a size, lying across (a fixture's shortcut for a let-go there).
        void KeyAtTheKeyhole(float scale)
        {
            Prop key = level.Key;
            key.Unfreeze();
            key.SetScale(scale);
            key.SetPose(new Vector3(0f, L.Keyhole.y, L.DoorZ - 0.45f), Quaternion.Euler(0f, 90f, 0f));
        }

        // Runs at a point with the sprint key down and jumps where the chest ends. Returns how far the feet got
        // before they were below the chest's top.
        float RunAndJumpAt(Vector3 target, float seconds)
        {
            var input = new ScriptedInput();
            Game.Input = input;
            Player player = Game.Player;
            float farthest = player.Position.z;
            bool jumped = false;
            for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
            {
                TestHelpers.LookAt(player, new Vector3(target.x, player.Eye.y, target.z));
                input.Hold = new InputFrame { MoveZ = 1f, Sprint = true };
                if (!jumped && player.Grounded && player.Position.z > L.EdgeZ - 0.35f)
                {
                    input.Once = new InputFrame { Jump = true };
                    jumped = true;
                }
                if (player.Position.z < 0f) jumped = false;
                Game.Tick();
                if (player.Position.y > -0.5f) farthest = Mathf.Max(farthest, player.Position.z);
            }
            bot = new Bot(Game);
            return farthest;
        }

        // A flick of the mouse, as input: the look that puts the crosshair on a point this tick.
        InputFrame Aim(Vector3 target, InputFrame frame = default)
        {
            Player player = Game.Player;
            Vector3 to = target - player.Eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -Player.MaxPitch, Player.MaxPitch);
            frame.LookYaw = Mathf.DeltaAngle(player.Yaw, yaw);
            frame.LookPitch = pitch - player.Pitch;
            return frame;
        }

        // The door open and the key in it (a fixture's shortcut for the key let go at the lock from afar).
        void OpenTheDoor()
        {
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            KeyAtTheKeyhole(0.3f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Door.IsOpen, 4f), State());
        }

        // The size the spool had when the jumps of SpoolJump began.
        float spoolJumpStart;

        // Spool-jumping: the spool put down small at a place on the chest, hopped onto, and then - jump, pick
        // it up from under one's own feet in the air, let go of it at the top of the jump, land on it - again
        // and again (the look and the clicks are input, a tick apart, as a quick hand makes them). Returns the
        // log; the player ends on top of whatever the spool has become.
        string SpoolJump(float towerZ, int cycles)
        {
            var log = new System.Text.StringBuilder();
            Prop spool = level.Spool;
            Player player = Game.Player;
            IEnumerator Start()
            {
                yield return bot.WalkTo(L.SpoolStand, 0.05f);
                yield return bot.Grab(spool);
                // Put down a good two steps ahead: a little bigger than it started, as high as a hop goes, and
                // heavy enough to be stood on (a lighter toy is kicked away by the side of a foot).
                yield return bot.WalkTo(new Vector3(0f, 0f, towerZ - 2.3f), 0.05f);
                yield return bot.DropAt(new Vector3(0f, 0f, towerZ));
                yield return bot.Wait(0.6f);
                Vector3 c = spool.Center;
                float r = ToyFactory.ThreadSpoolRadius * spool.Scale;
                yield return bot.WalkTo(new Vector3(c.x, 0f, c.z - r - 0.45f), 0.05f, 4f);
                yield return bot.LookAt(new Vector3(c.x, bot.Player.Eye.y, c.z + 5f));
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(c.x, 0f, c.z), 0.12f, 2f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                // (To the middle of its top, and still: a jump with the hop's last speed lands beside a small spool.)
                yield return bot.WalkTo(new Vector3(c.x, 0f, c.z), 0.06f, 2f);
                yield return bot.Wait(0.3f);
            }
            if (!Drive(Start(), 30f)) log.AppendLine("  start script did not finish");
            log.AppendLine("  start: spool " + spool.Scale.ToString("0.00") + " at " + F(spool.Center) + ", player " + F(player.Position) + " on " + (player.GroundProp != null ? player.GroundProp.Name : "-"));
            spoolJumpStart = spool.Scale;
            if (player.GroundProp != spool) return log.ToString();

            var input = new ScriptedInput();
            Game.Input = input;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                float before = spool.Scale;
                Vector3 below() => new Vector3(player.Eye.x, spool.Held ? 0f : spool.Center.y, player.Eye.z + 0.02f);
                // Jump, looking straight down.
                input.Once = Aim(below(), new InputFrame { Jump = true });
                Game.Tick();
                int waited = 0;
                while (player.Grounded && waited++ < 6)
                {
                    input.Once = Aim(below());
                    Game.Tick();
                }
                // In the air: the click.
                input.Once = Aim(below(), new InputFrame { GrabPressed = true });
                Game.Tick();
                if (Game.Grabber.Held != spool)
                {
                    log.AppendLine("  cycle " + cycle + ": the click in the air picked up " + (Game.Grabber.Held != null ? Game.Grabber.Held.Name : "nothing") + " (grounded " + player.Grounded + ", feet " + F(player.Position) + ")");
                    break;
                }
                float ratio = Game.Grabber.Ratio;
                // To the top of the jump, looking straight down.
                int guard = 0;
                while (player.Velocity.y > 0.3f && guard++ < 60)
                {
                    input.Once = Aim(below());
                    Game.Tick();
                }
                float held = spool.Scale, feetAtLetGo = player.Position.y;
                input.Once = Aim(below(), new InputFrame { GrabPressed = true });
                Game.Tick();
                float letGo = spool.Scale;
                guard = 0;
                while (!player.Grounded && guard++ < 240) Game.Tick();
                for (int i = 0; i < 12; i++) Game.Tick();
                log.AppendLine("  cycle " + cycle + ": " + before.ToString("0.00") + " -> looks " + ratio.ToString("0.000") + ", held " + held.ToString("0.00") + ", let go " + letGo.ToString("0.00") +
                               " (feet " + feetAtLetGo.ToString("0.00") + ") -> spool " + spool.Scale.ToString("0.00") + " at " + F(spool.Center) + ", feet " + F(player.Position) + " on " +
                               (player.GroundProp != null ? player.GroundProp.Name : player.Grounded ? "floor" : "air") + ", max " + spool.MaxScale.ToString("0.00"));
                if (player.GroundProp != spool) break;
                if (spool.Scale >= L.SpoolMax - 0.01f) break;
            }
            input.Hold = default;
            return log.ToString();
        }

        // ---- What the level is ---------------------------------------------------------------------------------

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(12));
            Load();
            Assert.AreEqual("the-keyhole", level.Slug);
            Assert.AreEqual("The Keyhole", level.Title);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("high-shelf", level.Environment);
            Assert.AreEqual(-9f, L.PitFloor, "LEVELS: the socks are nine units under the chest's top");
            Assert.AreEqual(-9.5f, level.GroundY, "the shelf the chest stands on: half a unit of drawer and socks under the pit's floor");
            Assert.AreEqual(-30f, level.KillY);
            // (LEVELS: "... You are standing on the key." - which nobody is at the start. Said at the door instead.)
            Assert.AreEqual("The door is locked. The only key is the bridge.", level.Blurb);
            StringAssert.Contains("under your feet", L.LockedLine);
            Assert.AreEqual(3, level.Hints.Length);

            Prop key = level.Key, spool = level.Spool;
            Assert.AreEqual(8f, key.Scale, 1e-4f, "LEVELS: the key is sixteen units long");
            Assert.AreEqual(0.06f, key.MinScale, 1e-5f, "(LEVELS: 0.05.) No smaller than its lock takes");
            Assert.AreEqual(8f, key.MaxScale, 1e-4f);
            Assert.AreEqual(GrabPose.Snap90, key.GrabPose);
            Assert.IsTrue(key.HasTag(L.KeyTag));
            Assert.IsTrue(key.Frozen, "the bridge lies where it lies until it is picked up");
            Assert.AreEqual(28.7f, key.Mass, 0.3f, "LEVELS: 0.056 x 8^3");
            Assert.Less(Vector3.Distance(key.Center, new Vector3(0f, 0.16f, 13f)), 0.01f, "level, across the drawer");
            Assert.Less(Quaternion.Angle(key.Rotation, Quaternion.identity), 0.01f);

            Assert.AreEqual(1f, spool.Scale, 1e-4f);
            Assert.AreEqual(0.5f, spool.MinScale, 1e-4f);
            Assert.AreEqual(9.9f, spool.MaxScale, 1e-4f, "LEVELS: its top is never more than a hop above the chest");
            Assert.AreEqual(GrabPose.Upright, spool.GrabPose);
            Assert.IsTrue(spool.KeepUpright, "it never ends up tumbled");
            Assert.IsTrue(spool.HasTag(L.SpoolTag));
            Assert.AreEqual(0.38f, spool.Mass, 0.01f, "LEVELS: 0.38 x s^3");
            Assert.Less(Vector3.Distance(spool.Center, L.SpoolCentre), 0.01f);
            int toys = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) toys++;
            Assert.AreEqual(2, toys, "the key and the spool are the only toys");

            // The lock (LEVELS appendix B: 0.06 to 0.5) and the door behind it.
            Socket hole = level.Lock;
            Assert.AreEqual(0.06f, hole.MinScale, 1e-5f);
            Assert.AreEqual(0.5f, hole.MaxScale, 1e-5f);
            Assert.AreEqual(FitState.Good, hole.Fit(0.06f));
            Assert.AreEqual(FitState.Good, hole.Fit(0.5f));
            Assert.AreEqual(FitState.TooSmall, hole.Fit(0.059f));
            Assert.AreEqual(FitState.TooBig, hole.Fit(0.501f));
            // It takes a key let go in front of the door's face (LEVELS: a box 1.2 x 1.2 round the keyhole).
            Assert.IsTrue(hole.Capture.Contains(L.Keyhole + Vector3.back * 0.3f));
            Assert.IsTrue(hole.Capture.Contains(new Vector3(0.8f, L.DoorTop - 0.2f, L.DoorZ - 0.8f)));
            Assert.IsTrue(hole.Capture.Contains(new Vector3(-0.8f, L.SillY + 0.2f, L.DoorZ - 0.1f)));
            Assert.IsFalse(hole.Capture.Contains(new Vector3(L.DoorHalfWidth + 0.05f, L.Keyhole.y, L.DoorZ - 0.3f)), "beside the door");
            Assert.IsFalse(hole.Capture.Contains(new Vector3(0f, L.DoorTop + 0.05f, L.DoorZ - 0.3f)), "over the door");
            Assert.IsFalse(hole.Capture.Contains(new Vector3(0f, L.SillY - 0.05f, L.DoorZ - 0.3f)), "under the sill");
            Assert.IsFalse(hole.Capture.Contains(new Vector3(0f, L.Keyhole.y, L.DoorZ - 0.95f)), "a step in front of it");
            Assert.IsFalse(hole.Seated);
            Assert.IsFalse(level.Door.IsOpen);
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.IsTrue(level.Exit.Locked, "locked until the door has opened");
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(0f, 1.75f, 22.5f)), 1e-3f, "LEVELS: the exit is in the hall behind the door");

            // The spool's window (LEVELS appendix B: 8.35 to 9.9, intended 9.1) is what a hop allows: from its
            // top up to the sill, and from the chest up to its top.
            Assert.AreEqual(8.35f, L.FillMin, 1e-4f);
            Assert.AreEqual(1.1f, L.SillY - (L.PitFloor + L.FillMin), 1e-3f, "from the smallest spool the sill is 1.1 up");
            Assert.AreEqual(0.9f, L.PitFloor + L.SpoolMax, 1e-3f, "the biggest stands 0.9 over the chest");
            Assert.That(L.OutlineScale, Is.InRange(L.FillMin + 0.5f, L.SpoolMax - 0.5f), "the outline is a size from the middle of the window");
            Assert.AreEqual(9.1f, L.OutlineScale, 1e-4f);
            Assert.Greater(L.OutlineCentre.y - 0.5f * L.OutlineScale, L.DoorTop + 0.2f, "the outline is over the door");
            Assert.Less(L.OutlineCentre.y + 0.5f * L.OutlineScale, L.WallTop - 0.3f, "and under the eaves");
            Assert.IsTrue(level.Hazard.Shape.Contains(new Vector3(0f, L.HazardTop - 0.1f, 14f)), "LEVELS: the drawer below -1.5 sends the player back");
            Assert.IsFalse(level.Hazard.Shape.Contains(new Vector3(0f, L.HazardTop + 0.1f, 14f)));
            Assert.Less(L.HazardTop, L.PitFloor + L.FillMin - 0.5f, "nobody standing on a spool that fills the drawer is sent back");
            // And high over it too (the second review): higher than a jump from the biggest spool nobody gets by
            // walking and jumping, and from a tower on the chest a running jump reached the open door.
            Assert.Greater(L.HighY, L.PitFloor + L.SpoolMax + 1.25f + 0.3f, "a jump from the biggest spool is not sent back");
            Assert.IsTrue(level.HighHazard.Shape.Contains(new Vector3(0f, L.HighY + 0.1f, 14f)));
            Assert.IsTrue(level.HighHazard.Shape.Contains(new Vector3(5.5f, L.WallTop, L.DoorZ - 0.1f)));
            Assert.IsFalse(level.HighHazard.Shape.Contains(new Vector3(0f, L.HighY - 0.1f, 14f)));
            Assert.IsFalse(level.HighHazard.Shape.Contains(new Vector3(0f, 6f, L.EdgeZ + 0.3f)), "on something that stands by the chest's edge");
            Assert.IsFalse(level.HighHazard.Shape.Contains(new Vector3(0f, 6f, 2f)), "over the chest");
            Assert.IsFalse(level.HighHazard.Shape.Contains(new Vector3(0f, L.HighY + 0.1f, L.DoorZ + 0.3f)), "in the doorway");

            // Nothing moves by itself.
            RunSeconds(2f);
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsTrue(SpoolIsHome);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.03f, "LEVELS: the start is (0, 0, -2)");
            Assert.AreEqual(0f, Game.Player.Yaw, 0.01f);
            Assert.IsFalse(Game.LevelCompleted);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            List<string> said = Listen();
            var ratios = new List<float>();
            var sizes = new List<float>();
            var order = new List<Prop>();
            int respawns = 0;
            Game.Events.PropGrabbed += e => ratios.Add(e.OldScale / e.GrabDistance);
            Game.Events.PropDropped += e =>
            {
                order.Add(e.Prop);
                sizes.Add(e.NewScale);
            };
            Game.Events.PlayerRespawned += e => respawns++;
            float spoolTop = 0f;
            level.Lock.OnSeated += prop => spoolTop = SpoolTop;
            TestHelpers.PlayLevel(Game, 60f);

            CollectionAssert.AreEqual(new[] { level.Key, level.Spool, level.Key }, order, "the key is put down, the spool fills the drawer, the key goes into the lock");
            Assert.That(ratios[0], Is.InRange(0.5f, 0.56f), "the key from the start: eight across from fifteen away");
            Assert.That(sizes[0], Is.InRange(0.3f, 0.7f), "on the little shelf it is a trinket (LEVELS: 0.31; from the start, 0.58)");
            Assert.That(ratios[1], Is.InRange(0.58f, 0.65f), "the spool from its mark (LEVELS: 0.627)");
            Assert.AreEqual(L.OutlineScale, sizes[1], L.OutlineScale * 0.05f, "LEVELS appendix B: the solver's spool is 9.1 within 5 %");
            Assert.That(sizes[2], Is.InRange(0.2f, 0.42f), "LEVELS appendix B: the key at the lock is about 0.3, well inside 0.06 to 0.5");
            Assert.That(spoolTop, Is.InRange(-0.2f, 0.45f), "the spool's top is about level with the chest (LEVELS: +0.12)");
            Assert.IsTrue(level.Lock.Seated);
            Assert.IsTrue(level.Door.IsOpen);
            Assert.IsFalse(level.Exit.Locked);
            Assert.IsFalse(level.Key.Grabbable, "the key stays in the lock");
            Assert.AreEqual(L.SeatScale, level.Key.Scale, 1e-3f);
            Assert.AreEqual(0, respawns, "nobody fell");
            Assert.AreEqual(0, level.Hazard.Catches + level.HighHazard.Catches);
            Assert.AreEqual(0, level.KeyLeash.Returns + level.SpoolLeash.Returns);
            Assert.Less(Game.Time, 20f, "the solution takes a quarter of a minute (measured 14.3 s)");
            CollectionAssert.IsEmpty(said, "the solution needs no telling off");
        }

        // What the first picture has in it, from where the player stands at the start.
        [Test]
        public void TheStartShowsTheKeyTheSpoolTheDoorAndTheOutline()
        {
            Load();
            Game.Tick();
            Player player = Game.Player;
            Vector3 eye = player.Eye;

            // The key is under the crosshair: a click picks it up, and with that the bridge is gone.
            Assert.AreSame(level.Key, Game.Grabber.FindTarget(), "a click at the start picks the key up");
            Assert.Less(Vector3.Angle(player.Forward, level.Key.Center - eye), 3f);
            Assert.That(L.KeyStart / Vector3.Distance(eye, level.Key.Center), Is.InRange(0.5f, 0.56f));

            var sights = new Dictionary<string, Vector3>
            {
                { "the keyhole", L.Keyhole + Vector3.back * 0.03f },
                { "the door", new Vector3(0f, 2.4f, L.DoorZ - 0.03f) },
                { "the key's bow", new Vector3(0f, 0.34f, 7f) },
                { "the spool", L.SpoolCentre },
                { "the spool's mark", L.SpoolMark + new Vector3(0f, 0.02f, -0.3f) },
                { "the mark to let go from", L.ThrowMark + Vector3.up * 0.02f },
                { "the outline", L.OutlineCentre + Vector3.back * 0.03f },
                { "the outline's lower left corner", new Vector3(-4.8f, L.OutlineCentre.y - 0.5f * L.OutlineScale + 0.2f, L.DoorZ - 0.03f) },
                { "the outline's lower right corner", new Vector3(4.8f, L.OutlineCentre.y - 0.5f * L.OutlineScale + 0.2f, L.DoorZ - 0.03f) },
                { "the outline's upper left corner", new Vector3(-4.8f, L.OutlineCentre.y + 0.5f * L.OutlineScale - 0.2f, L.DoorZ - 0.03f) },
                { "the outline's upper right corner", new Vector3(4.8f, L.OutlineCentre.y + 0.5f * L.OutlineScale - 0.2f, L.DoorZ - 0.03f) },
            };
            Quaternion toView = Quaternion.Inverse(player.LookRotation);
            float tanV = Mathf.Tan(35f * Mathf.Deg2Rad), tanH = tanV * 16f / 9f;
            foreach (KeyValuePair<string, Vector3> sight in sights)
            {
                Vector3 local = toView * (sight.Value - eye);
                Assert.Greater(local.z, 0f, sight.Key + " is in front of the player");
                float across = local.x / local.z / tanH, up = local.y / local.z / tanV;
                Assert.Less(Mathf.Abs(across), 0.97f, sight.Key + " is in the picture (across)");
                Assert.Less(Mathf.Abs(up), 0.97f, sight.Key + " is in the picture (up and down)");
                // The level card lies over the top left of the picture for the first seconds.
                bool underTheCard = across > -0.97f && across < -0.225f && up > 0.5f && up < 0.94f;
                Assert.IsFalse(underTheCard, sight.Key + " is under the level card (" + across.ToString("0.00") + ", " + up.ToString("0.00") + ")");
                // The first thing along the line of sight is the thing itself.
                Vector3 to = sight.Value - eye;
                // (The spool's own side is half a unit in front of its middle.)
                bool blocked = Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                               Vector3.Distance(hit.point, sight.Value) > 0.7f;
                Assert.IsFalse(blocked, sight.Key + " is hidden by " + (blocked ? hit.collider.name + " at " + hit.point : ""));
            }

            // Paint and looks have no colliders: what stops a toy is what the level put there to stop it.
            foreach (string name in new[] { "Chest And Walls", "Socks", "Dollhouse", "Little Shelf Looks", "Paint", "Outline", "Tape", "Edge Line", "Stand Mark Shelf", "Stand Mark Spool", "Stand Mark Door", "Clutter" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        [Test]
        public void EverythingTheLevelSays_IsShortPlainAndInTheCampaignsWords()
        {
            var level12 = (Level12Keyhole)LevelRegistry.Get(12);
            var lines = new List<string>();
            foreach (FieldInfo field in typeof(Level12Keyhole).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text != null && text.Contains(" ")) lines.Add(text);
            }
            Assert.GreaterOrEqual(lines.Count, 15, "the level's lines were found");
            var all = new List<string>(lines) { level12.Blurb };
            all.AddRange(level12.Hints);
            foreach (string text in all)
            {
                foreach (char c in text)
                    Assert.IsTrue(c >= ' ' && c <= '~', "\"" + text + "\" has a character the UI font may not have ('" + c + "')");
                Assert.IsTrue(text.EndsWith(".") || text.EndsWith("?") || text.EndsWith("!"), "\"" + text + "\" is a sentence");
                Assert.AreEqual(text.Trim(), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("grab") || lower.Contains("take it") || lower.Contains("taken"), "\"" + text + "\" - the levels say \"pick up\"");
                Assert.IsFalse(lower.Contains("drop") || lower.Contains("release"), "\"" + text + "\" - the levels say \"let go\"");
            }
            foreach (string line in lines)
                Assert.LessOrEqual(line.Length, 110, "\"" + line + "\" is too long for a toast");
            Assert.LessOrEqual(level12.Blurb.Length, 60, "the blurb fits the level card");
            foreach (string hint in level12.Hints)
                Assert.LessOrEqual(hint.Length, 200, "\"" + hint + "\" is too long for the pause card");
            string nudge = level12.Hints[0].ToLowerInvariant();
            Assert.IsFalse(nudge.Contains("outline") || nudge.Contains("shelf") || nudge.Contains("spool") || nudge.Contains("mark"), "the first hint gives the solution away");
            StringAssert.StartsWith("Pick", level12.Hints[2], "the last hint is the recipe, from the first pick-up");
            StringAssert.Contains("let go", level12.Hints[2]);
            StringAssert.Contains("shelf", level12.Hints[2]);
            StringAssert.Contains("spool", level12.Hints[2]);
            StringAssert.Contains("outline", level12.Hints[2]);
            StringAssert.Contains("lock", level12.Hints[2]);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, L.FarLine, "the sentence for a pick-up from too far away is Level 1's, word for word");
        }

        // ---- No way round --------------------------------------------------------------------------------------

        // LEVELS: "At the door the player is standing on the key and nothing else."
        [TestCase(0f)]
        [TestCase(-0.6f)]
        [TestCase(0.6f)]
        public void AcrossTheKey_TheDoorIsShut_AndTheKeyIsUnderfoot(float x)
        {
            Load();
            List<string> said = Listen();
            Player player = Game.Player;
            Drive(bot.WalkTo(new Vector3(x * 0.5f, 0f, 12f), 0.1f, 12f), 14f);
            Drive(bot.WalkTo(new Vector3(x, 0f, 19.6f), 0.05f, 6f), 8f);
            Assert.Greater(player.Position.z, 19.2f, "the key carries the player to the door (" + State() + ")");
            Assert.AreSame(level.Key, player.GroundProp, "on the key");
            CollectionAssert.AreEqual(new[] { L.LockedLine }, said);

            // Jumping at the door, five times: there is no sill to stand on, no way in.
            var input = new ScriptedInput();
            Game.Input = input;
            float highest = -100f, deepest = 0f;
            for (int hop = 0; hop < 5; hop++)
            {
                input.Hold = new InputFrame { MoveZ = 1f };
                input.Once = new InputFrame { Jump = true };
                for (int tick = 0; tick < 70; tick++)
                {
                    TestHelpers.LookAt(player, player.Eye + Vector3.forward * 10f);
                    Game.Tick();
                    deepest = Mathf.Max(deepest, player.Position.z);
                    if (!player.Grounded) continue;
                    highest = Mathf.Max(highest, player.Position.y);
                    Assert.AreSame(level.Key, player.GroundProp, "the only thing to stand on over there is the key (feet " + F(player.Position) + ")");
                }
            }
            Assert.Greater(player.Position.z, 19.2f, "still at the door (" + State() + ")");
            Assert.Less(highest, L.SillY - 0.04f, "nobody stands on the sill");
            Assert.Less(deepest, L.DoorZ - 0.2f, "nobody gets into the door");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(level.Exit.Locked);
            Assert.IsFalse(level.Door.IsOpen);

            // A click at the key underfoot picks nothing up, and the level says why.
            bot = new Bot(Game);
            Assert.IsTrue(Drive(bot.LookAt(new Vector3(x, 0.3f, 17f)), 3f));
            Assert.IsTrue(Drive(bot.Click(), 1f));
            Assert.IsNull(Game.Grabber.Held, "nobody picks up what they stand on");
            RunSeconds(0.2f);
            CollectionAssert.AreEqual(new[] { L.LockedLine, L.StandingLine }, said);
            Assert.IsTrue(KeyIsTheBridge, "and the bridge is still the bridge");
        }

        [Test]
        public void WithoutABridge_ARunningJumpEndsAmongTheSocks_EvenWithTheDoorOpen()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            float farthest = RunAndJumpAt(L.ExitCentre, 6f);
            Assert.GreaterOrEqual(respawns, 1, "whoever jumps into the drawer is back at the start");
            Assert.AreEqual(respawns, level.Hazard.Catches);
            Assert.Less(farthest, L.EdgeZ + 7f, "a running jump is five and a half units (got to z " + farthest + "), the drawer is twelve");
            Assert.IsFalse(Game.LevelCompleted);

            // The door open and nothing in the drawer (LEVELS: a key that looks tiny enough can be let go at
            // the lock from the chest - "the door opens, and the spool is still needed to reach it").
            KeyAtTheKeyhole(0.3f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Door.IsOpen, 4f), "the lock takes the key and the door opens (" + State() + ")");
            Assert.IsFalse(level.Exit.Locked);
            int before = respawns;
            farthest = RunAndJumpAt(L.ExitCentre, 6f);
            Assert.Greater(respawns, before);
            Assert.Less(farthest, L.EdgeZ + 7f);
            Assert.IsFalse(Game.LevelCompleted, "an open door twelve units away is not the way out yet");
            Assert.IsFalse(level.Key.Grabbable, "LEVELS: the key in the lock cannot be used as a bridge again");

            // And then the spool, as intended.
            RunSeconds(0.5f);
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            BotRunner.Run(Game, level.Cross(bot), 30f);
            BotRunner.Run(Game, level.Leave(bot), 30f);
            AssertCompleted("the spool carries the player to the open door");
        }

        [Test]
        public void TheChestIsSealed_AndThereIsNothingToStandOnAtTheDoor()
        {
            Load();
            // Without the bridge (the key is on the little shelf).
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            RunSeconds(0.2f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            const float w = L.HalfWidth, top = L.CapY;
            // The four sides from the socks to the sky cap - the ones that are drawn and the one that is open to
            // the room - and the chest's own front under its top.
            for (float y = L.PitFloor + 0.4f; y <= top - 0.3f; y += 0.7f)
            {
                bool overTheChest = y > 0.2f;
                for (float z = overTheChest ? L.BackZ + 0.2f : L.EdgeZ + 0.2f; z <= L.DoorZ - 0.2f; z += 0.7f)
                {
                    Assert.IsTrue(scene.Raycast(new Vector3(-w + 0.5f, y, z), Vector3.left, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at -X, y " + y + ", z " + z);
                    Assert.IsTrue(scene.Raycast(new Vector3(w - 0.5f, y, z), Vector3.right, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at +X, y " + y + ", z " + z);
                }
                for (float x = -w + 0.2f; x <= w - 0.2f; x += 0.7f)
                {
                    // (The slot under the door is a hole in the wall: 0.4 high, closed 1.2 in.)
                    bool slot = Mathf.Abs(x) < L.SlotHalfWidth + 0.05f && y > -0.05f && y < L.SlotTop + 0.05f;
                    float reach = slot ? L.SlotDepth + 0.6f : 0.6f;
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, L.DoorZ - 0.5f), Vector3.forward, out _, reach, mask, QueryTriggerInteraction.Ignore), "a way out at the door wall, x " + x + ", y " + y);
                    if (overTheChest)
                        Assert.IsTrue(scene.Raycast(new Vector3(x, y, L.BackZ + 0.5f), Vector3.back, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at the back, x " + x + ", y " + y);
                    else
                        Assert.IsTrue(scene.Raycast(new Vector3(x, y, L.EdgeZ + 0.5f), Vector3.back, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way under the chest, x " + x + ", y " + y);
                }
            }
            for (float x = -w + 0.2f; x <= w - 0.2f; x += 0.7f)
                for (float z = L.BackZ + 0.2f; z <= L.DoorZ - 0.2f; z += 0.7f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, top - 0.5f, z), Vector3.up, out _, 0.6f, mask, QueryTriggerInteraction.Ignore), "a way out at the top, x " + x + ", z " + z);

            // Over the whole drawer, right up to the door wall, a body the player's width comes down on the socks
            // and on nothing else: the door wall is sheer, the door is flush in it, and there is no far side.
            var rows = new List<float>();
            for (float z = L.EdgeZ + 0.31f; z < L.DoorZ - 0.31f; z += 0.4f) rows.Add(z);
            rows.Add(L.DoorZ - 0.31f);
            foreach (float z in rows)
                for (float x = -w + 0.31f; x <= w - 0.31f; x += 0.33f)
                {
                    Assert.IsTrue(scene.SphereCast(new Vector3(x, top - 0.5f, z), 0.29f, Vector3.down, out RaycastHit hit, 40f, mask, QueryTriggerInteraction.Ignore), "no floor at x " + x + ", z " + z);
                    Assert.Less(hit.point.y, L.PitFloor + 0.02f, "something to stand on at x " + x + ", z " + z + ": " + hit.collider.name + " at " + hit.point);
                }
            // The slot the key's tip lies in is too low to get into, and the sill is no step: 0.45 up, 0.015 deep.
            Assert.Less(L.SlotTop, 0.5f);
            Assert.Less(L.DoorRecess, 0.02f);
        }

        static IEnumerator LookAndClick(Bot bot, Vector3 point)
        {
            yield return bot.LookAt(point);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void NothingButTheKeyAndTheSpool_CanBePickedUp()
        {
            Load();
            var setPieces = new[]
            {
                new Vector3(0f, 2.4f, L.DoorZ),                                // the door
                L.OutlineCentre,                                               // the outline over it
                new Vector3(-4f, 6f, L.DoorZ),                                 // the dollhouse
                new Vector3(L.HalfWidth, 4f, 10f),                             // the atlas
                new Vector3(4.9f, 0.7f, -4.5f), new Vector3(3.1f, 0.5f, -4.9f), // the blocks in the corner
                new Vector3(-4f, L.ShelfTop - 0.1f, L.ShelfFront),              // the little shelf
                new Vector3(-4f, 2.3f, L.BackZ),                               // the rack over it
                L.ThrowMark, L.ShelfStand,                                     // the marks
                new Vector3(-3f, 0f, 4f),                                      // the chest's top
            };
            foreach (Vector3 piece in setPieces)
            {
                Assert.IsTrue(Drive(LookAndClick(bot, piece), 10f));
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsTrue(SpoolIsHome);
        }

        // No strand: wherever a small flat thing can come to lie on the chest - on its top, on the little shelf,
        // on the blocks in the corner - somebody standing (or hopping) somewhere on the chest can see it, and what
        // can be seen can be picked up. (The blocks used to stand askew with a third on top: 29 of these points,
        // in three pockets behind and beside them, were out of sight from everywhere.)
        [Test]
        public void NowhereOnTheChestIsOutOfSight()
        {
            Load();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            RunSeconds(0.5f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.SolidMask;
            var eyes = new List<Vector3>();
            for (float x = -5.69f; x <= 5.7f; x += 0.3f)
                for (float z = -5.69f; z <= 7.7f; z += 0.3f)
                {
                    var feet = new Vector3(x, 0.02f, z);
                    if (!GadgetKit.CapsuleFree(Game, feet, Player.BaseRadius, Player.BaseHeight)) continue;
                    eyes.Add(feet + Vector3.up * (Player.BaseEyeHeight - 0.02f));
                    eyes.Add(feet + Vector3.up * (Player.BaseEyeHeight + 1.05f));   // at the top of a hop
                }
            Assert.Greater(eyes.Count, 2000, "places to look from");
            int tested = 0;
            var hidden = new List<string>();
            // From above (what lies on top of things), and from under the little shelf's board (the floor under it).
            foreach (float from in new[] { 3f, L.ShelfTop - L.ShelfThickness - 0.05f })
            for (float x = -5.95f; x <= 5.96f; x += 0.1f)
                for (float z = -5.95f; z <= 7.96f; z += 0.1f)
                {
                    if (from < 3f && (x < L.ShelfMinX || x > L.ShelfMaxX || z > L.ShelfFront)) continue;
                    if (!scene.Raycast(new Vector3(x, from, z), Vector3.down, out RaycastHit floor, 4f, mask, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 point = floor.point + Vector3.up * 0.02f;
                    tested++;
                    bool seen = false;
                    for (int i = 0; i < eyes.Count && !seen; i++)
                    {
                        Vector3 to = point - eyes[i];
                        float distance = to.magnitude;
                        seen = !scene.Raycast(eyes[i], to / distance, out _, distance - 0.03f, mask, QueryTriggerInteraction.Ignore);
                    }
                    if (!seen) hidden.Add(F(point) + " on " + floor.collider.name);
                }
            Assert.Greater(tested, 15000);
            CollectionAssert.IsEmpty(hidden, hidden.Count + " places where a key would be out of sight");
        }

        // ---- The key and the lock (LEVELS appendix B: 0.06 to 0.5) ------------------------------------------------

        [TestCase(0.06f)]
        [TestCase(0.3f)]
        [TestCase(0.5f)]
        public void TheLockTakesAKeyOfTheRightSize_AndTheDoorOpens(float scale)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            RunSeconds(2f);
            Assert.IsTrue(level.SpoolFills, State());
            Prop taken = null;
            level.Lock.OnSeated += prop => taken = prop;
            KeyAtTheKeyhole(scale);
            Game.Tick();
            Assert.IsTrue(level.Lock.Seating, "a key of " + scale + " is drawn in at once (" + State() + ")");
            Assert.IsFalse(level.Key.Grabbable, "and it is the lock's from then on");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Lock.Seated, 1.5f), "home after the ease and the turn");
            Assert.AreSame(level.Key, taken);
            Assert.AreEqual(L.SeatScale, level.Key.Scale, 1e-3f, "the lock draws it down to the keyhole's size");
            Assert.Less(Vector3.Distance(level.Key.Center, L.SeatPosition(L.SeatScale)), 0.15f, "in the keyhole (" + State() + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Door.IsOpen, 2f), "the door slides up");
            Assert.IsFalse(level.Exit.Locked);
            Assert.Greater(level.Key.Center.y, L.DoorTop, "the key goes up with the door");

            BotRunner.Run(Game, level.Cross(bot), 30f);
            BotRunner.Run(Game, level.Leave(bot), 30f);
            AssertCompleted("through the open door");
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.KeyLeash.Returns);
        }

        [TestCase(0.56f)]
        [TestCase(1.2f)]
        public void AKeyThatIsTooBig_IsNot_AndTheLevelSaysSo(float scale)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            RunSeconds(2f);
            KeyAtTheKeyhole(scale);
            RunSeconds(3f);
            Assert.IsFalse(level.Lock.Seated || level.Lock.Seating);
            Assert.IsFalse(level.Door.IsOpen);
            Assert.IsTrue(level.Exit.Locked);
            Assert.AreEqual(scale, level.Key.Scale, 1e-3f, "it keeps its size");
            Assert.IsTrue(level.Key.Grabbable);
            // (The key was last picked up at the start, looking half a unit big: carried closer it would fit.)
            CollectionAssert.AreEqual(new[] { L.KeyCloserLine }, said);
            Assert.Greater(level.Key.Center.y, -0.5f, "it lies on the spool, at the door (" + State() + ")");
            Assert.AreEqual(0, level.KeyLeash.Returns, "in plain view: it stays where it fell");
        }

        // LEVELS: the lock takes 0.06 to 0.5, and the key shrinks to 0.05. Measured: a key that looked under
        // 0.17 in the hand (the one the level puts on the shelf, picked up from a few steps away) came out
        // under 0.06 when it was carried right up to the door - which is what one does with a key - and was
        // sent back to the shelf as too small. Now the key gets no smaller than its lock takes.
        [TestCase(0.32f)]
        [TestCase(0.9f)]
        public void AKeyThatLooksTiny_CarriedRightUpToTheDoor_IsNotTooSmallForItsLock(float fromDoor)
        {
            Load();
            List<string> said = Listen();
            Prop key = level.Key;
            float ratio = 0f, atTheLock = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            // The key as the level puts it back (a fixture's shortcut), picked up from the middle of the chest.
            key.SetScale(L.ShelfScale);
            key.SetPose(L.ShelfSpot + Vector3.up * 0.05f, Quaternion.Euler(0f, 180f, 0f));
            RunSeconds(2f);
            Assert.IsTrue(level.SpoolFills && KeyIsOnTheShelf, State());
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => atTheLock = e.NewScale;
            IEnumerator FromAfar()
            {
                yield return bot.WalkTo(new Vector3(1f, 0f, 1f), 0.2f);
                yield return bot.Grab(key);
                yield return level.Cross(bot);
                // Looking at the keyhole all the way, as one does.
                yield return bot.LookAt(L.Keyhole);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - fromDoor), 0.05f, 4f);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            // (Right at the door the walk ends against it: that is as far as anybody gets.)
            Drive(FromAfar(), 40f);
            Assert.Less(ratio, 0.1f, "from eight units away the key on the shelf looks tiny (" + ratio + ")");
            Assert.That(atTheLock, Is.InRange(L.LockMin, 0.2f), "at the door it is as small as the lock takes, and no smaller");
            AssertCompleted("let go " + fromDoor + " from the door (key " + atTheLock + ", " + Told(said) + ")");
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.KeyLeash.Returns);
            Assert.AreEqual(L.LockMin, L.KeyMin, 0f, "the key's smallest size is the lock's");
            Assert.AreEqual(FitState.Good, level.Lock.Fit(level.Key.MinScale));
        }

        // The same with the bot's hands: the key from the shelf, let go at the keyhole from a distance to the
        // door. LEVELS: any standing distance from 0.4 to 1.6 (measured with the solver's key: 0.3 to 1.35).
        [TestCase(0.45f)]
        [TestCase(1.25f)]
        public void LetGoAtTheKeyhole_FromRightAtTheDoorOrAStepBack_TheKeyGoesIn(float fromDoor)
        {
            Load();
            List<string> said = Listen();
            float atTheLock = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            Game.Events.PropDropped += e => atTheLock = e.NewScale;
            BotRunner.Run(Game, KeyToTheLock(fromDoor), 60f);
            AssertCompleted("let go " + fromDoor + " from the door (key " + atTheLock + ")");
            Assert.That(atTheLock, Is.InRange(L.LockMin, L.LockMax), "inside the lock's window");
            CollectionAssert.IsEmpty(said);
        }

        [Test]
        public void LetGoFromTooFarBack_TheKeyIsTooBig_AndPickedUpFromThereItFits()
        {
            Load();
            List<string> said = Listen();
            float atTheLock = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            Game.Events.PropDropped += e => atTheLock = e.NewScale;
            Assert.IsFalse(Drive(KeyToTheLock(2.6f), 30f), "the door stays shut");
            Assert.Greater(atTheLock, L.LockMax + 0.1f, "from 2.6 away the key is " + atTheLock);
            Assert.IsFalse(level.Door.IsOpen);
            CollectionAssert.AreEqual(new[] { L.KeyCloserLine }, said, "it looks small enough (half a unit): it only has to be carried closer");
            Assert.AreEqual(0, level.KeyLeash.Returns, "the key lies on the spool, in view (" + State() + ")");

            // What the line says: picked up again (from here it lies a couple of units away) and carried right
            // up to the door.
            bot = new Bot(Game);
            IEnumerator Again()
            {
                yield return bot.Grab(level.Key);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 1f), 0.1f, 6f);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Again(), 40f);
            AssertCompleted("picked up from where it was let go and held up close (key " + atTheLock + ")");
            Assert.That(atTheLock, Is.InRange(L.LockMin, L.LockMax));
            CollectionAssert.AreEqual(new[] { L.KeyCloserLine }, said);
        }

        // ---- The spool and the drawer (LEVELS appendix B: 8.35 to 9.9, intended 9.1) ------------------------------

        [TestCase(8.35f)]
        [TestCase(9.1f)]
        [TestCase(9.9f)]
        public void ASpoolInTheWindow_CarriesThePlayerToTheDoor(float scale)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(scale);
            RunSeconds(2.5f);
            Assert.IsTrue(level.SpoolFills, "a spool of " + scale + " stays in the drawer (" + State() + ")");
            Assert.AreEqual(L.PitFloor + scale, SpoolTop, 0.03f, "upright on the socks");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + scale);
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.Hazard.Catches);
            Assert.AreEqual(0, level.SpoolLeash.Returns);
        }

        [TestCase(5f)]
        [TestCase(7.9f)]
        public void ASpoolThatIsTooSmall_GoesBackToWhereItStood_AndTheLevelSaysWhy(float scale)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(scale);
            RunSeconds(1.2f);
            Assert.IsFalse(level.SpoolFills);
            Assert.IsFalse(SpoolIsHome, "not before its grace time is over");
            RunSeconds(0.6f);
            Assert.IsTrue(SpoolIsHome, "too short to stand on: back on the chest at the size it started with (" + State() + ")");
            Assert.AreEqual(1, level.SpoolLeash.Returns);
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
            Assert.IsFalse(Game.LevelCompleted);

            BotRunner.Run(Game, TheRest(), 90f);
            AssertCompleted("solved after a spool that was too small");
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
        }

        // The pick-up decides how big the spool looks. Measured, let go from the mark: from as close as anyone
        // gets (0.9 behind its middle) it reaches its largest size a little short of the door; from 1.3 it is
        // just big enough.
        [TestCase(0.9f)]
        [TestCase(1.15f)]
        [TestCase(1.3f)]
        public void PickedUpFromCloseBy_AndLetGoFromTheMark_TheSpoolFillsTheDrawer(float behind)
        {
            Load();
            List<string> said = Listen();
            float ratio = 0f, dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropGrabbed += e =>
            {
                if (e.Prop == level.Spool) ratio = e.OldScale / e.GrabDistance;
            };
            Game.Events.PropDropped += e =>
            {
                if (e.Prop == level.Spool) dropped = e.NewScale;
            };
            BotRunner.Run(Game, HoldSpool(L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -behind), L.ThrowStand, L.OutlineCentre), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "picked up from " + behind + " behind it (looks " + ratio + ") it is " + dropped + " (" + State() + ")");
            Assert.That(dropped, Is.InRange(L.FillMin, L.SpoolMax + 1e-3f));
            Assert.Less(level.DoorGap(), 0.6f, "it stands against the door wall, or within a short step of it");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
        }

        [Test]
        public void PickedUpFromTooFarAway_ItIsCalledOut_ComesOutTooSmall_AndGoesBack()
        {
            Load();
            List<string> said = Listen();
            float ratio = 0f, dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -2.5f), L.ThrowStand, L.OutlineCentre), 30f);
            Assert.That(ratio, Is.InRange(0.3f, L.SpoolFarRatio), "from two and a half units away it looks small");
            Assert.That(dropped, Is.InRange(4.5f, 7f), "and against the door wall it is no bridge");
            CollectionAssert.AreEqual(new[] { L.FarLine }, said, "said at the pick-up, in the campaign's words");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), "too small: back where it stood (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.FarLine, L.SpoolFarSmallLine }, said);

            BotRunner.Run(Game, TheRest(), 90f);
            AssertCompleted("picked up from the mark it works");
            CollectionAssert.AreEqual(new[] { L.FarLine, L.SpoolFarSmallLine }, said, "no further complaint");
        }

        [Test]
        public void PickedUpFromALittleTooFar_ItComesOutTooSmall_AndHeldFromFurtherBackItFills()
        {
            Load();
            List<string> said = Listen();
            float ratio = 0f, dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Vector3 from = L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -1.7f);
            BotRunner.Run(Game, HoldSpool(from, L.ThrowStand, L.OutlineCentre), 30f);
            Assert.That(ratio, Is.InRange(L.SpoolFarRatio, 0.55f));
            Assert.Less(dropped, L.FillMin, "from the mark it is " + dropped);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), State());
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said, "the line says what helps: hold it and step back");

            // The same pick-up, and let go from the back of the chest.
            BotRunner.Run(Game, HoldSpool(from, new Vector3(0f, 0f, -4.5f), L.OutlineCentre), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "held from further back it is " + dropped + " (" + State() + ")");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
        }

        // Where the spool is let go from (picked up from its mark). LEVELS: a standing band of z -0.9 to 2.4.
        [TestCase(-0.5f)]
        [TestCase(2.5f)]
        public void LetGoFromAStepOrTwoOffTheMark_TheSpoolStillFillsTheDrawer(float z)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, z), L.OutlineCentre), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "let go from z " + z + " it is " + dropped + " (" + State() + ")");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
        }

        [Test]
        public void LetGoFromTooNearTheEdge_TheSpoolIsTooSmall_AndGoesBack()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, 4.5f), L.OutlineCentre), 30f);
            Assert.That(dropped, Is.InRange(6.5f, L.FillMin - 0.2f), "the nearer the wall, the smaller");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), State());
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
            BotRunner.Run(Game, TheRest(), 90f);
            AssertCompleted("from the mark");
        }

        // LEVELS: "Spool clamped and short of the wall: stand closer and re-place." Measured before the drawer
        // drew the spool in: from z -2 and further back the spool reached its full size in the air with its
        // near side still over the chest, and came down balanced on the chest's edge. Now its middle is over
        // the drawer, so the drawer takes it: stepping back too far is harmless.
        [TestCase(-2f)]
        [TestCase(-4.5f)]
        [TestCase(-5.5f)]
        public void LetGoFromTheBackOfTheChest_ItIsAsBigAsItGets_AndTheDrawerTakesIt(float z)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Vector3 letGoAt = Vector3.zero;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                letGoAt = e.Prop.Center;
            };
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, z), L.OutlineCentre), 30f);
            Assert.AreEqual(L.SpoolMax, dropped, 1e-3f, "LEVELS: the clamp");
            Assert.Less(letGoAt.z - ToyFactory.ThreadSpoolRadius * dropped, L.EdgeZ, "let go with its near side still over the chest (" + F(letGoAt) + ")");
            Assert.Greater(letGoAt.z, L.EdgeZ, "but its middle over the drawer");
            Assert.AreEqual(FitState.Good, paintAtLetGo, "and the paint said so");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "the drawer takes it (" + State() + ")");
            Assert.Less(level.DoorGap(), 0.2f, "against the door wall");
            Assert.AreEqual(0f, level.Spool.Center.x, 0.05f, "in the middle");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.SpoolLeash.Returns);
        }

        // What is left of the clamp: picked up from touching distance the spool looks so big that from the
        // very back of the chest, held high, it is full size with its middle still over the chest.
        [Test]
        public void AsBigAsItGetsWithItsMiddleOverTheChest_ItStaysThere_AndTheLevelSaysSo()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Vector3 letGoAt = Vector3.zero;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                letGoAt = e.Prop.Center;
            };
            Vector3 close = L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -0.9f);
            BotRunner.Run(Game, HoldSpool(close, new Vector3(0f, 0f, -5.6f), new Vector3(0f, 14f, L.DoorZ)), 30f);
            Assert.AreEqual(L.SpoolMax, dropped, 1e-3f, "LEVELS: the clamp");
            Assert.Less(letGoAt.z, L.EdgeZ, "its middle is over the chest (" + F(letGoAt) + ")");
            Assert.AreEqual(FitState.TooSmall, paintAtLetGo, "the paint said: not like this");
            RunSeconds(3f);
            Assert.IsFalse(level.SpoolFills);
            Assert.IsFalse(level.Guiding);
            Assert.Greater(level.Spool.Center.y, 4f, "it stands on the chest (" + State() + ")");
            Assert.AreEqual(0, level.SpoolLeash.Returns, "in plain view: it stays");
            CollectionAssert.AreEqual(new[] { L.SpoolClampLine }, said);

            // LEVELS: "stand closer and re-place".
            BotRunner.Run(Game, SpoolAgain(), 30f);
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("picked up again and let go from the mark");
            CollectionAssert.AreEqual(new[] { L.SpoolClampLine }, said);
        }

        // Where on the door wall the spool is held (from the marks). The outline reaches from 3.4 to 12.5 and
        // five to either side. Measured: with the crosshair from 5 up to a unit over the outline's top, and up
        // to 1.5 to either side, the spool fills the drawer and stands within a step of the door.
        [TestCase(0f, 5.2f)]
        [TestCase(0f, 10.5f)]
        [TestCase(0f, 12.5f)]
        [TestCase(0f, 13.5f)]
        [TestCase(-1.5f, 7.95f)]
        [TestCase(1.5f, 7.95f)]
        public void HeldAnywhereAboutTheMiddleOfTheOutline_TheSpoolFillsTheDrawer(float x, float y)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, L.ThrowStand, new Vector3(x, y, L.DoorZ)), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "held at (" + x + ", " + y + ") it is " + dropped + " (" + State() + ")");
            Assert.Less(level.DoorGap(), L.GapMax - 0.1f, "within a short step of the door wall");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
        }

        // Further to the side it catches on the side wall earlier: big enough, but (measured before the drawer
        // drew the spool in) it came down 1.5 to 1.8 short of the door and had to be picked up again. Now the
        // drawer draws it to its middle, against the door wall.
        [TestCase(2f)]
        [TestCase(-2f)]
        public void HeldALittleTooFarToTheSide_TheDrawerDrawsItToTheMiddle(float x)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Vector3 letGoAt = Vector3.zero;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                letGoAt = e.Prop.Center;
            };
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, L.ThrowStand, new Vector3(x, L.OutlineCentre.y, L.DoorZ)), 30f);
            Assert.That(dropped, Is.InRange(L.FillMin, L.SpoolMax), "big enough");
            Assert.Greater(Mathf.Abs(letGoAt.x), 1f, "let go to one side (" + F(letGoAt) + ")");
            Assert.Greater(L.DoorZ - (letGoAt.z + ToyFactory.ThreadSpoolRadius * dropped), L.GapMax, "and a long step short of the door");
            Assert.AreEqual(FitState.Good, paintAtLetGo, "the paint said it would do");
            Assert.IsTrue(level.Guiding, "the drawer draws it in");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "it is " + dropped + " (" + State() + ")");
            Assert.IsFalse(level.Guiding);
            Assert.AreEqual(dropped, level.Spool.Scale, 1e-3f, "at the size it was let go at");
            Assert.AreEqual(0f, level.Spool.Center.x, 0.05f, "in the middle of the drawer");
            Assert.That(level.DoorGap(), Is.InRange(-0.01f, 0.2f), "against the door wall (" + State() + ")");
            Assert.Less(Vector3.Angle(level.Spool.Rotation * Vector3.up, Vector3.up), 0.5f, "upright");
            RunSeconds(1.5f);
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.SpoolLeash.Returns);
        }

        // ---- The paint is the gauge (the campaign's language since Level 2) -----------------------------------------

        // A spool that fills the drawer is ten units deep: held against the door wall its near side is ten units
        // nearer than its outline, and on screen it is a third wider and twice as tall as the outline. One that
        // just covers the outline measures 7.0 - too small. So the outline cannot be the measure; its paint is.
        [Test]
        public void ThePaintSaysWhetherTheSpoolInTheHandWouldFillTheDrawer()
        {
            Load();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Renderer[] paint = { level.OutlinePaint, level.TapePaint, level.EdgePaint };
            foreach (Renderer renderer in paint) Assert.IsNotNull(renderer);
            Material line = level.OutlinePaint.sharedMaterial, edge = level.EdgePaint.sharedMaterial;
            Assert.AreSame(line, level.TapePaint.sharedMaterial, "the line along the atlas is the outline's paint");
            Assert.AreNotSame(line, edge);
            Material green = Materials.Gadget(Palette.Go), red = Materials.Gadget(Level02ThimbleChasm.Wrong);
            Assert.AreEqual(FitState.Idle, level.SpoolTint);

            void AssertPaint(FitState state, string when)
            {
                Assert.AreEqual(state, level.SpoolTint, when + " (" + State() + ")");
                Material wanted = state == FitState.Good ? green : state == FitState.TooSmall ? red : null;
                Assert.AreSame(wanted ?? line, level.OutlinePaint.sharedMaterial, when + ": the outline");
                Assert.AreSame(wanted ?? line, level.TapePaint.sharedMaterial, when + ": the line along the atlas");
                Assert.AreSame(wanted ?? edge, level.EdgePaint.sharedMaterial, when + ": the line along the chest's edge");
            }
            IEnumerator HoldFrom(float z)
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.1f, 12f);
                yield return bot.LookAt(L.OutlineCentre);
                yield return bot.Wait(0.1f);
            }

            // Picked up and looked at where it stood: nothing is judged.
            BotRunner.Run(Game, bot.WalkTo(L.SpoolStand, 0.05f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Spool), 10f);
            RunSeconds(0.1f);
            AssertPaint(FitState.Idle, "just picked up");

            // Held against the outline from too near the edge: too small. Red (and it blinks).
            BotRunner.Run(Game, HoldFrom(5f), 20f);
            Assert.That(level.Spool.Scale, Is.InRange(6f, L.FillMin - 0.3f));
            AssertPaint(FitState.TooSmall, "held from near the edge");
            Assert.Greater(level.Spool.Scale, 7.05f, "(and yet it covers its outline: what just covers it measures 7.0)");
            // A step back and it is bigger; from the mark it would fill the drawer. Green.
            BotRunner.Run(Game, HoldFrom(L.ThrowStand.z), 20f);
            Assert.That(level.Spool.Scale, Is.InRange(L.FillMin + 0.3f, L.SpoolMax));
            AssertPaint(FitState.Good, "held from the mark");
            // Further back it is as big as it gets, and still over the drawer: green.
            BotRunner.Run(Game, HoldFrom(-4f), 20f);
            Assert.AreEqual(L.SpoolMax, level.Spool.Scale, 1e-3f);
            AssertPaint(FitState.Good, "held from the back of the chest");
            // Looked at the floor at one's feet it is a small thing again: nothing is judged.
            BotRunner.Run(Game, bot.LookAt(new Vector3(0f, 0f, -1.5f)), 10f);
            RunSeconds(0.1f);
            Assert.Less(level.Spool.Scale, 4f);
            AssertPaint(FitState.Idle, "held over the floor");

            // Let go where the paint is green: it fills the drawer, and the paint stays green for as long as it does.
            BotRunner.Run(Game, HoldFrom(L.ThrowStand.z), 20f);
            AssertPaint(FitState.Good, "held from the mark again");
            BotRunner.Run(Game, bot.Click(), 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), State());
            RunSeconds(1f);
            AssertPaint(FitState.Good, "standing in the drawer");

            // With the key across the drawer nothing fills it, whatever its size: red.
            Load();
            BotRunner.Run(Game, bot.WalkTo(L.SpoolStand, 0.05f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Spool), 10f);
            BotRunner.Run(Game, HoldFrom(L.ThrowStand.z), 20f);
            Assert.Greater(level.Spool.Scale, L.FillMin);
            Assert.AreEqual(FitState.TooSmall, level.SpoolTint, "the key is in the way");
            Assert.AreSame(Materials.Gadget(Level02ThimbleChasm.Wrong), level.OutlinePaint.sharedMaterial);
        }

        // The paint never lies: wherever the spool is picked up from, let go from and held toward - green a
        // moment before it is let go means it fills the drawer (in the middle, against the door wall), and
        // anything else means it does not.
        [TestCase(0.9f)]
        [TestCase(1.3f)]
        [TestCase(1.7f)]
        public void LetGoOnGreen_TheSpoolFillsTheDrawer_AndOnNothingElse(float behind)
        {
            // The middle of the outline; to one side; over its top; under it (where the chest stops the spool).
            var aims = new[] { L.OutlineCentre, new Vector3(2.5f, L.OutlineCentre.y, L.DoorZ), new Vector3(0f, 15.5f, L.DoorZ), new Vector3(0f, 4.4f, L.DoorZ) };
            int green = 0, other = 0;
            foreach (float z in new[] { -5.5f, 1f, 3.5f, 5f })
                foreach (Vector3 aim in aims)
                {
                    Load();
                    float dropped = 0f;
                    BotRunner.Run(Game, ShelveTheKey(), 30f);
                    Game.Events.PropDropped += e => dropped = e.NewScale;
                    BotRunner.Run(Game, HoldSpool(L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -behind), new Vector3(0f, 0f, z), aim), 30f);
                    FitState paint = paintAtLetGo;
                    TestHelpers.RunUntil(Game, () => level.SpoolFills || SpoolIsHome, 6f);
                    RunSeconds(2f);
                    string what = "picked up " + behind + " behind it, let go from z " + z + " toward " + aim + ": " + dropped.ToString("0.00") + ", paint " + paint + " (" + State() + ")";
                    if (paint == FitState.Good)
                    {
                        green++;
                        Assert.IsTrue(level.SpoolFills, "green, and it does not fill the drawer - " + what);
                        Assert.AreEqual(0f, level.Spool.Center.x, 0.05f, what);
                        Assert.That(level.DoorGap(), Is.InRange(-0.01f, 0.2f), what);
                        Assert.AreEqual(FitState.Good, level.SpoolTint, "and the paint stays green - " + what);
                    }
                    else
                    {
                        other++;
                        Assert.IsFalse(level.SpoolFills, "not green, and it fills the drawer - " + what);
                    }
                }
            Assert.GreaterOrEqual(green, 2, "green in several places");
            Assert.GreaterOrEqual(other, 3, "and not in several others");
        }

        [Test]
        public void HeldTooLow_ItStopsOnTheChest_AndTheLevelSaysSo()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, L.ThrowStand, new Vector3(0f, 2.6f, L.DoorZ)), 30f);
            Assert.That(dropped, Is.InRange(2.5f, 6f), "it touched the chest's top long before the door wall");
            RunSeconds(2.5f);
            Assert.IsFalse(level.SpoolFills);
            Assert.Greater(level.Spool.Center.y, 0.5f, "it stands on the chest (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolFloorLine }, said);
            Assert.AreEqual(0, level.SpoolLeash.Returns);

            BotRunner.Run(Game, SpoolAgain(), 30f);
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("picked up again and held against the outline");
            CollectionAssert.AreEqual(new[] { L.SpoolFloorLine }, said);
        }

        [Test]
        public void HeldTowardTheSide_ItCatchesOnTheSideWall_AndTheLevelSaysSo()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, L.ThrowStand, new Vector3(-4.5f, L.OutlineCentre.y, L.DoorZ)), 30f);
            Assert.Less(dropped, L.FillMin, "the side wall stopped it at " + dropped);
            RunSeconds(4f);
            Assert.IsFalse(level.SpoolFills);
            CollectionAssert.AreEqual(new[] { L.SpoolSideLine }, said, State());

            // Wherever that left it (on the chest's edge, or back where it stood): again, in the middle.
            BotRunner.Run(Game, level.Spool.Frozen ? level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre) : SpoolAgain(), 30f);
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("held in the middle");
            CollectionAssert.AreEqual(new[] { L.SpoolSideLine }, said);
        }

        // LEVELS: "Spool placed while the key still bridges the pit: it stops against the key." And then the
        // other order works too: the key pulled out from under it is no longer the bridge, and the spool is.
        [Test]
        public void TheSpoolFirst_StandsOnTheKey_AndFillsTheDrawerWhenTheKeyIsPickedUp()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            Assert.AreEqual(FitState.TooSmall, paintAtLetGo, "the paint said: not like this (the key is in the way)");
            Assert.IsFalse(level.Guiding, "the drawer does not draw the spool through the key");
            RunSeconds(3f);
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsFalse(level.SpoolFills);
            Assert.AreEqual(FitState.Idle, level.SpoolTint);
            Assert.Greater(level.Spool.Scale, L.FillMin);
            Assert.AreEqual(0.32f, level.Spool.Center.y - 0.5f * level.Spool.Scale, 0.05f, "the spool stands on the key (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolKeyLine }, said);
            Assert.AreEqual(0, level.SpoolLeash.Returns);

            IEnumerator Pull()
            {
                yield return bot.Grab(level.Key);
                yield return bot.Until(() => level.SpoolFills, 6f);
                yield return level.Cross(bot);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 0.8f), 0.05f, 6f);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Pull(), 60f);
            AssertCompleted("the key pulled out from under the spool and carried over it");
            CollectionAssert.AreEqual(new[] { L.SpoolKeyLine }, said);
        }

        // LEVELS: "Jump off the key and grab it in mid-air (legal): the player falls and respawns holding the
        // key - the same as step 2."
        [Test]
        public void PickedUpInMidAirAtTheDoor_TheKeyComesAlong_AndTheDoorIsAsFarAwayAsBefore()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Leap()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 12f), 0.1f, 12f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 19.2f), 0.05f, 12f);
                yield return bot.LookAt(new Vector3(0f, 0.3f, 17.5f));
                yield return bot.Jump(false);
                yield return bot.Wait(0.05f);
                yield return bot.Click();
            }
            BotRunner.Run(Game, Leap(), 40f);
            Assert.AreSame(level.Key, Game.Grabber.Held, "in the air nobody stands on anything");
            Assert.Greater(Game.Grabber.Ratio, 1f, "from this close the key looks huge");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns > 0, 4f), "and without it there is nothing to land on");
            Assert.AreSame(level.Key, Game.Grabber.Held, "LEVELS: back at the start, still holding what was held");
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.1f);
            Assert.IsFalse(level.Door.IsOpen);
            Assert.IsFalse(Game.LevelCompleted);

            // The same as step 2 from here: the key down by the shelf, the spool, and the key picked up again from
            // a few steps away (which makes it small enough for the lock).
            IEnumerator Rest()
            {
                yield return level.PutKeyOnShelf(bot);
                yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
                yield return bot.WalkTo(new Vector3(-1.5f, 0f, -1.5f), 0.2f);
                yield return bot.Grab(level.Key);
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Rest(), 90f);
            AssertCompleted("solved after the leap (" + Told(said) + ")");
            CollectionAssert.DoesNotContain(said, L.TooBigLine);
        }

        // The same from the middle of the bridge: from there the key fills the view, and no step toward the lock
        // would make it small enough. Said at the pick-up.
        [Test]
        public void PickedUpInMidAirOverTheMiddleOfTheBridge_TheKeyLooksTooBigForAnyLock_AndTheLevelSaysSo()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Leap()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 13f), 0.1f, 12f);
                yield return bot.LookAt(new Vector3(0f, 0.3f, 14.5f));
                yield return bot.Jump(false);
                yield return bot.Wait(0.05f);
                yield return bot.Click();
            }
            BotRunner.Run(Game, Leap(), 40f);
            Assert.AreSame(level.Key, Game.Grabber.Held);
            Assert.Greater(Game.Grabber.Ratio, L.KeyNearRatio, "eight units of key from under two away");
            RunSeconds(0.1f);
            CollectionAssert.AreEqual(new[] { L.KeyNearLine }, said);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns > 0, 4f), State());
            Assert.AreSame(level.Key, Game.Grabber.Held, "back at the start with the key in the hand");
            Assert.IsFalse(Game.LevelCompleted);
        }

        // ---- Ways round that were tried ------------------------------------------------------------------------------

        // LEVELS: "Posting the key from the landing, 12 away: works if the key looks tiny enough. The door opens,
        // and the spool is still needed to reach it. Tolerated." Measured: the key on the shelf, picked up from
        // the far corner of the chest's edge (it looks 0.04) and held toward the keyhole from the edge.
        [Test]
        public void TheKeyLetGoAtTheLockFromAcrossTheDrawer_OpensTheDoor_AndTheSpoolIsStillNeeded()
        {
            Load();
            List<string> said = Listen();
            float ratio = 0f, atTheLock = 0f;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e => atTheLock = e.NewScale;
            IEnumerator Post()
            {
                yield return bot.WalkTo(new Vector3(5.5f, 0f, 7.6f), 0.1f);
                yield return bot.Grab(level.Key);
                yield return bot.WalkTo(new Vector3(0f, 0f, 7.6f), 0.1f);
                yield return bot.DropAt(L.Keyhole);
            }
            BotRunner.Run(Game, Post(), 30f);
            Assert.Less(ratio, 0.045f, "from seventeen units away the key on the shelf looks tiny (" + ratio + ")");
            Assert.That(atTheLock, Is.InRange(L.LockMin, L.LockMax), "twelve units away it is the lock's size");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Door.IsOpen, 4f), "the lock takes it and the door opens (" + State() + ")");
            Assert.IsFalse(level.Exit.Locked);
            Assert.IsFalse(level.Key.Grabbable, "the key is the lock's: no bridge is to be had from it again");
            Assert.IsFalse(Game.LevelCompleted);
            // The way out is open and twelve units of socks away.
            float farthest = RunAndJumpAt(L.ExitCentre, 6f);
            Assert.GreaterOrEqual(respawns, 1);
            Assert.Less(farthest, L.EdgeZ + 7f);
            Assert.IsFalse(Game.LevelCompleted, "an open door is not a way across");
            RunSeconds(0.5f);
            // The other way across is still the spool.
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            BotRunner.Run(Game, level.Cross(bot), 30f);
            BotRunner.Run(Game, level.Leave(bot), 30f);
            AssertCompleted("across the spool to the open door");
            CollectionAssert.IsEmpty(said);
        }

        // The spool carried across the key and stood on it at the door is something else to stand on there. From
        // it the key can be picked up - and with the key goes what the spool stood on.
        [Test]
        public void TheSpoolStoodOnTheKeyAtTheDoor_IsNoPlaceToUnlockFrom()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Prop key = level.Key, spool = level.Spool;
            IEnumerator Carry()
            {
                yield return bot.WalkTo(L.SpoolStand, 0.05f);
                yield return bot.Grab(spool);
                yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.1f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 17f), 0.05f, 12f);
                yield return bot.DropAt(new Vector3(0f, 0.34f, 18.9f));
                yield return bot.Wait(1.5f);
            }
            BotRunner.Run(Game, Carry(), 40f);
            Assert.That(spool.Scale, Is.InRange(0.8f, 1.3f), "a spool of about its own size, on the key (" + State() + ")");
            Assert.Greater(spool.Center.z, 17.5f);
            Assert.AreEqual(0, level.SpoolLeash.Returns, "on the key, in plain view: it stays");
            IEnumerator Hop()
            {
                Vector3 top = spool.Center;
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z - ToyFactory.ThreadSpoolRadius * spool.Scale - 0.35f), 0.05f, 4f);
                yield return bot.LookAt(new Vector3(top.x, bot.Player.Eye.y, top.z + 5f));
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z), 0.15f, 2f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
            }
            BotRunner.Run(Game, Hop(), 20f);
            Assert.AreSame(spool, Game.Player.GroundProp, "standing on the spool, at the door (" + State() + ")");
            // The key from up here (the blade shows beside the spool), and at once the keyhole.
            IEnumerator Take()
            {
                yield return bot.LookAt(new Vector3(0.68f, 0.33f, Game.Player.Position.z - 1.2f));
                yield return bot.Click();
                yield return bot.DropAt(L.Keyhole, 1.5f);
            }
            Drive(Take(), 4f);
            RunSeconds(4f);
            Assert.IsFalse(Game.LevelCompleted, "nobody gets out this way (" + State() + ", " + Told(said) + ")");
            Assert.GreaterOrEqual(respawns, 1, "with the key goes the floor: the spool and whoever stood on it fall");
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.5f, "back at the start");

            // Whatever became of the two toys, both can be seen and picked up, and the level is solved the ordinary way.
            RunSeconds(2f);
            bot = new Bot(Game);
            IEnumerator Again()
            {
                // (Off the key first, if that is what the fall left underfoot: nobody picks up what they stand on.)
                yield return bot.WalkTo(new Vector3(4.3f, 0f, -3.5f), 0.3f);
                if (Game.Grabber.Held == null && !level.Lock.Seated) yield return bot.Grab(key);
                if (Game.Grabber.Held == key) yield return level.PutKeyOnShelf(bot);
                if (!level.SpoolFills)
                {
                    if (spool.Frozen) yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
                    else yield return SpoolAgain();
                }
                if (!level.Lock.Seated)
                {
                    yield return bot.WalkTo(L.ShelfStand, 0.15f);
                    yield return bot.Grab(key);
                }
                yield return level.Cross(bot);
                if (!level.Lock.Seated) yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            AssertCompleted("solved the ordinary way afterwards (" + Told(said) + ")");
        }

        // The spool stands on its rim, always (as Level 2's thimble does). Flipped in the hand it used to come
        // down on its side: a drawer "filled" by a round wall, its top 1.35 over the chest - too high to get onto.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TheSpoolCannotBeFlipped_ItAlwaysStandsOnItsRim(int flips)
        {
            Load();
            List<string> said = Listen();
            Assert.IsFalse(level.Spool.AllowPitch);
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            IEnumerator Flip()
            {
                yield return bot.WalkTo(L.SpoolStand, 0.05f);
                yield return bot.Grab(level.Spool);
                // The flip key, and a turn for good measure.
                yield return bot.RotateHeld(3, flips);
                yield return bot.WalkTo(L.ThrowStand, 0.1f);
                yield return bot.DropAt(L.OutlineCentre);
                yield return bot.Until(() => level.SpoolFills, 6f);
            }
            BotRunner.Run(Game, Flip(), 40f);
            // (It comes down at twenty units a second and is a moment in settling.)
            RunSeconds(1f);
            Assert.Less(Vector3.Angle(level.Spool.Rotation * Vector3.up, Vector3.up), 0.5f, "upright (" + level.Spool.Rotation.eulerAngles + ")");
            Assert.AreEqual(L.PitFloor + level.Spool.Scale, SpoolTop, 0.03f, "on its rim, on the socks");
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across the spool");
            CollectionAssert.IsEmpty(said);
        }

        // The marks are where the solver stands; nobody else stands exactly there. A step off each mark and the
        // crosshair anywhere about the middle of the outline: the same result, and nothing to be told off for.
        [TestCase(0.3f, 0f, 0.3f, 0.6f, 0f, 0f)]
        [TestCase(-0.3f, 0f, -0.3f, -0.6f, 1f, 1.5f)]
        [TestCase(0f, 0.25f, 0.4f, 0.4f, -1f, -1.5f)]
        [TestCase(0f, -0.25f, -0.4f, -0.4f, 0.7f, 2.5f)]
        [TestCase(0.2f, 0.2f, 0f, 1f, -0.7f, 3.5f)]
        public void AStepOffTheMarks_AndTheCrosshairAboutTheMiddle_TheSolutionStillWorks(float pickX, float pickZ, float letGoX, float letGoZ, float aimX, float aimY)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, HoldSpool(L.SpoolStand + new Vector3(pickX, 0f, pickZ), L.ThrowStand + new Vector3(letGoX, 0f, letGoZ), L.OutlineCentre + new Vector3(aimX, aimY, 0f)), 30f);
            Assert.AreEqual(FitState.Good, paintAtLetGo, "green (" + dropped + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), "it is " + dropped + " (" + State() + ")");
            Assert.That(level.DoorGap(), Is.InRange(-0.01f, 0.2f));
            Assert.AreEqual(0f, level.Spool.Center.x, 0.05f);
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("across a spool of " + dropped);
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(0, level.Hazard.Catches + level.KeyLeash.Returns + level.SpoolLeash.Returns);
        }

        // ---- Nobody gets to the door from above (the second review) ------------------------------------------------

        // Spool-jumping. The engine refuses a pick-up of the toy underfoot, but not in mid-air: jump off the
        // spool, pick it up from under one's own feet, let go of it at the top of the jump (from higher up, so
        // it comes out bigger), land on it. Measured before the level's rule: 0.85 to 9.9 in thirteen jumps - a
        // tower on the chest. Now a spool picked up in mid-air gets no bigger until the player has landed.
        [Test]
        public void JumpingOffTheSpool_AndPickingItUpInMidAir_BuildsNoTower()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Prop spool = level.Spool;
            Player player = Game.Player;
            string log = SpoolJump(5.5f, 8);
            Assert.AreSame(spool, player.GroundProp, "the eight jumps were made, each from the spool and back onto it:\n" + log);
            StringAssert.Contains("cycle 7", log, log);
            Assert.That(spoolJumpStart, Is.InRange(L.SpoolStart, 1.2f), "it was put down about the size it started with:\n" + log);
            Assert.LessOrEqual(spool.Scale, spoolJumpStart + 1e-3f, "the spool is no bigger than it was before the jumps:\n" + log);
            Assert.Less(player.Position.y, spoolJumpStart + 0.05f, "and nobody is higher up than a hop:\n" + log);
            CollectionAssert.Contains(said, L.AirLine, "the level says why (" + Told(said) + ")");
            CollectionAssert.DoesNotContain(said, L.FarLine, "and nothing about how far away it was picked up");
            RunSeconds(0.2f);
            Assert.AreEqual(L.SpoolMax, spool.MaxScale, 1e-4f, "with the feet on something again it is an ordinary spool");

            // And an ordinary spool it is: picked up from the chest, a step away, it fills the drawer.
            bot = new Bot(Game);
            IEnumerator Ordinary()
            {
                Vector3 c = spool.Center;
                yield return bot.WalkTo(new Vector3(c.x, 0f, c.z - 1.15f), 0.1f, 6f);
                yield return bot.Until(() => bot.Player.Grounded && bot.Player.GroundProp == null, 3f);
                yield return SpoolAgain();
                yield return KeyToTheLock();
            }
            BotRunner.Run(Game, Ordinary(), 90f);
            AssertCompleted("solved the ordinary way afterwards (" + Told(said) + ")");
            Assert.AreEqual(0, level.HighHazard.Catches);
        }

        // A pick-up in mid-air is still a pick-up (LEVELS: legal). Once the feet are down again the spool grows
        // as it always does.
        [Test]
        public void PickedUpInMidAir_AndLandedWithIt_TheSpoolIsAnOrdinarySpool()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, bot.WalkTo(L.SpoolStand, 0.05f), 10f);
            Prop spool = level.Spool;
            Player player = Game.Player;
            var input = new ScriptedInput();
            Game.Input = input;
            input.Once = Aim(spool.Center, new InputFrame { Jump = true });
            Game.Tick();
            for (int i = 0; i < 6 && player.Grounded; i++)
            {
                input.Once = Aim(spool.Center);
                Game.Tick();
            }
            Assert.IsFalse(player.Grounded, "in the air");
            input.Once = Aim(spool.Center, new InputFrame { GrabPressed = true });
            Game.Tick();
            Assert.AreSame(spool, Game.Grabber.Held, "picked up in mid-air");
            Assert.AreEqual(L.SpoolStart, spool.MaxScale, 1e-4f, "in the air it gets no bigger than it is");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => player.Grounded, 2f));
            Game.Tick();
            Assert.AreSame(spool, Game.Grabber.Held);
            Assert.AreEqual(L.SpoolMax, spool.MaxScale, 1e-4f, "landed: it grows as it always does");

            bot = new Bot(Game);
            IEnumerator Rest()
            {
                yield return bot.WalkTo(L.ThrowStand, 0.1f);
                yield return bot.LookAt(L.OutlineCentre);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Rest(), 20f);
            Assert.AreEqual(FitState.Good, level.SpoolTint, "from the mark the paint is green (" + State() + ")");
            BotRunner.Run(Game, bot.Click(), 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), State());
            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("solved after a pick-up in mid-air");
            CollectionAssert.IsEmpty(said);
        }

        // The way round that the tower was for. With the door open (the key let go at the lock from afar: LEVELS'
        // tolerated case) and a spool 9.9 high on the chest's edge, its rim five units out over the drawer, a
        // sprint and a jump from 3.35 before the rim came down through the doorway: out, and the drawer was never
        // filled. Now whoever is high over the drawer is sent back to the start. (The tower is a fixture's
        // shortcut here; the spool cannot be ridden up any more, see above.) The other two are lower spools
        // as far out as a spool stands since the third review - their middles on the chest's edge. (They
        // used to hang out from the edge by nearly their whole width, held by their rims, and from one four
        // units high the jump was just long enough: see ASpoolWhoseMiddleIsOverTheDrawer_HangsOnNothing.)
        [TestCase(9.9f, 7.27f, 2.6f, 4.1f)]
        [TestCase(6f, 7.9f, 0.35f, 3.1f)]
        [TestCase(4f, 7.9f, 0.35f, 1.85f)]
        public void FromATowerOnTheChestsEdge_NobodyLeapsThroughTheOpenDoor(float scale, float middle, float nearest, float farthest)
        {
            int tries = 0;
            var through = new List<string>();
            var notSentBack = new List<string>();
            for (float before = nearest; before <= farthest + 0.01f; before += 0.125f)
            {
                Load();
                List<string> said = Listen();
                int respawns = 0;
                Game.Events.PlayerRespawned += e => respawns++;
                OpenTheDoor();
                Prop spool = level.Spool;
                Player player = Game.Player;
                float radius = ToyFactory.ThreadSpoolRadius * scale;
                spool.Unfreeze();
                spool.SetScale(scale);
                spool.SetPose(new Vector3(0f, 0.5f * scale + 0.01f, middle), Quaternion.identity);
                // On its top, at the end that is over the chest.
                player.Teleport(new Vector3(0f, scale + 0.03f, Mathf.Min(middle - radius + 0.45f, L.EdgeZ + 0.2f)), 0f);
                RunSeconds(0.5f);
                Assert.AreSame(spool, player.GroundProp, "on the tower (" + State() + ")");
                Assert.AreEqual(scale, spool.Center.y + 0.5f * scale, 0.05f, "which stands on the chest (" + State() + ")");
                Assert.IsFalse(level.Guiding || level.SpoolFills);
                Assert.AreEqual(0, level.HighHazard.Catches, "standing on something by the chest's edge is nobody's business");
                float rim = spool.Center.z + radius;
                Assert.Greater(rim, L.EdgeZ + 2f, "and stands out over the drawer");

                var input = new ScriptedInput();
                Game.Input = input;
                bool jumped = false;
                for (int i = 0; i < 400 && respawns == 0 && !Game.LevelCompleted; i++)
                {
                    var frame = new InputFrame { MoveZ = 1f, Sprint = true };
                    if (!jumped && player.Grounded && player.Position.z > rim - before)
                    {
                        frame.Jump = true;
                        jumped = true;
                    }
                    input.Once = Aim(new Vector3(0f, player.Eye.y, 40f), frame);
                    Game.Tick();
                }
                RunSeconds(0.5f);
                string what = "a sprint and a jump from " + before.ToString("0.000") + " before the rim (" + State() + ", " + Told(said) + ")";
                // (Sent back from the tower's top, out over the drawer, before the jump was made - or in the air after it.)
                Assert.IsTrue(jumped || level.HighHazard.Catches == 1, what);
                tries++;
                if (Game.LevelCompleted) through.Add(what);
                else if (respawns != 1 || level.HighHazard.Catches != 1 || level.Hazard.Catches != 0 || said.Count != 1 || said[0] != L.HighLine ||
                         Vector3.Distance(player.Position, L.Spawn) > 0.5f)
                    notSentBack.Add(what + " respawns " + respawns + ", sent back from above " + level.HighHazard.Catches + ", from the socks " + level.Hazard.Catches);
            }
            Assert.GreaterOrEqual(tries, 12);
            CollectionAssert.IsEmpty(through, "out through the door without filling the drawer");
            CollectionAssert.IsEmpty(notSentBack, "everybody is sent back to the start from high over the drawer, before they are anywhere near the socks, and told why");
        }

        // What that rule must never touch: the highest anybody gets over the drawer by walking and jumping - on
        // the biggest spool there is, and on the key.
        [Test]
        public void JumpingAboutOnTheBiggestSpool_AndOnTheKey_NobodyIsSentBack()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Player player = Game.Player;
            float highest = -100f;
            void Hop(Vector3 toward, int hops, bool sprint)
            {
                var input = new ScriptedInput();
                Game.Input = input;
                for (int hop = 0; hop < hops; hop++)
                {
                    input.Once = Aim(new Vector3(toward.x, player.Eye.y, toward.z), new InputFrame { Jump = true, MoveZ = 1f, Sprint = sprint });
                    Game.Tick();
                    for (int tick = 0; tick < 55; tick++)
                    {
                        input.Once = Aim(new Vector3(toward.x, player.Eye.y, toward.z), new InputFrame { MoveZ = 1f, Sprint = sprint });
                        Game.Tick();
                        if (player.Position.z > L.EdgeZ + 0.4f) highest = Mathf.Max(highest, player.Position.y);
                    }
                }
                bot = new Bot(Game);
            }

            // Along the key and at the door (the key is 0.32 thick).
            Drive(bot.WalkTo(new Vector3(0f, 0f, 9f), 0.1f, 12f), 14f);
            Hop(new Vector3(0f, 0f, 30f), 6, true);
            Assert.AreSame(level.Key, player.GroundProp, State());
            Assert.That(highest, Is.InRange(1.2f, 1.75f), "a jump from the key");
            Assert.AreEqual(0, respawns, State());
            Assert.AreEqual(0, level.HighHazard.Catches + level.Hazard.Catches);

            // On the biggest spool: its top is 0.9 over the chest. Across from the chest (a running jump), jumps
            // on the spot here and there on its top and at the door, and a running jump along it.
            Load();
            respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            player = Game.Player;
            highest = -100f;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.SpoolMax);
            RunSeconds(2f);
            void Watch()
            {
                if (player.Position.z > L.EdgeZ + 0.4f) highest = Mathf.Max(highest, player.Position.y);
            }
            IEnumerator About()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, L.EdgeZ - 2f), 0.3f);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.EdgeZ - 0.35f), 0.2f, 10f, true);
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(0f, 0f, 12f), 0.2f, 8f);
                foreach (Vector3 spot in new[] { new Vector3(-4f, 0f, 13f), new Vector3(4f, 0f, 16f), new Vector3(0f, 0f, 10.5f), new Vector3(0f, 0f, L.DoorZ - 0.5f) })
                {
                    yield return bot.WalkTo(spot, 0.2f, 8f);
                    yield return bot.Until(() => bot.Player.Grounded, 2f);
                    for (int hop = 0; hop < 2; hop++)
                    {
                        yield return bot.Jump(false);
                        yield return bot.Wait(0.2f);
                        yield return bot.Until(() => bot.Player.Grounded, 2f);
                    }
                }
                // A running jump along the top, toward the door.
                yield return bot.WalkTo(new Vector3(0f, 0f, 11f), 0.2f, 8f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 13f), 0.3f, 8f, true);
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 0.6f), 0.2f, 8f, true);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
            }
            var runner = new BotRunner(About(), bot, Game);
            for (int i = TestHelpers.Ticks(60f); i > 0 && runner.Advance(); i--)
            {
                Game.Tick();
                Watch();
            }
            Assert.IsTrue(runner.Finished && !runner.Failed, "the walk about the spool's top ran to its end (" + State() + ")");
            Assert.AreSame(level.Spool, player.GroundProp, State());
            Assert.That(highest, Is.InRange(1.9f, 2.25f), "a jump from the biggest spool: 0.9 + 1.25");
            Assert.Less(highest, L.HighY - 0.3f, "well under what is sent back");
            Assert.AreEqual(0, level.HighHazard.Catches, "nobody is sent back from above (" + State() + ")");
            Assert.AreEqual(0, respawns, State());
            Assert.AreEqual(0, level.Hazard.Catches);

            // And higher still: a key as big as will lie on that spool (eight units long, where the lock refused
            // it), and a jump from the key.
            Prop key = level.Key;
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 10.2f), 0.2f, 8f), 12f);
            key.SetScale(4f);
            key.SetPose(new Vector3(0f, L.PitFloor + L.SpoolMax + 0.15f, 15.8f), Quaternion.identity);
            RunSeconds(1.5f);
            Assert.Greater(key.Center.y, 0.9f, "the key lies on the spool (" + State() + ")");
            highest = -100f;
            IEnumerator OnTheKey()
            {
                yield return bot.LookAt(new Vector3(0f, bot.Player.Eye.y, 30f));
                yield return bot.WalkTo(new Vector3(0f, 0f, 11.2f), 0.2f, 4f);
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(0f, 0f, 13.4f), 0.25f, 4f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                for (int hop = 0; hop < 3; hop++)
                {
                    yield return bot.Jump(false);
                    yield return bot.Wait(0.2f);
                    yield return bot.Until(() => bot.Player.Grounded, 2f);
                }
            }
            bool onTheKey = false;
            runner = new BotRunner(OnTheKey(), bot, Game);
            for (int i = TestHelpers.Ticks(30f); i > 0 && runner.Advance(); i--)
            {
                Game.Tick();
                Watch();
                onTheKey |= player.GroundProp == key;
            }
            Assert.IsTrue(runner.Finished && !runner.Failed, State());
            Assert.IsTrue(onTheKey, "stood on the key (" + State() + ")");
            Assert.That(highest, Is.InRange(2.15f, 2.5f), "a jump from the key on the biggest spool: 0.9 + 0.16 + 1.25");
            Assert.Less(highest, L.HighY - 0.5f);
            Assert.AreEqual(0, level.HighHazard.Catches + level.Hazard.Catches, State());
            Assert.AreEqual(0, respawns);
        }

        // LEVELS' own case for the key ("jump off and pick it up in mid-air: the player falls and is back at the
        // start holding it"), with the spool: off its top, picked up from under one's feet, down among the socks.
        // Back at the start it is an ordinary spool again - from this close it looks huge, and it is as big as it
        // gets a few steps from the edge.
        [Test]
        public void TheSpoolPickedUpInMidAirOverTheDrawer_ComesBackWithThePlayer_AndFillsTheDrawerAgain()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            RunSeconds(2f);
            BotRunner.Run(Game, level.Cross(bot), 30f);
            Prop spool = level.Spool;
            Player player = Game.Player;
            Assert.AreSame(spool, player.GroundProp, State());
            var input = new ScriptedInput();
            Game.Input = input;
            Vector3 down = new Vector3(player.Eye.x, spool.Center.y, player.Eye.z + 0.5f);
            input.Once = Aim(down, new InputFrame { Jump = true });
            Game.Tick();
            for (int i = 0; i < 6 && player.Grounded; i++)
            {
                input.Once = Aim(down);
                Game.Tick();
            }
            input.Once = Aim(down, new InputFrame { GrabPressed = true });
            Game.Tick();
            Assert.AreSame(spool, Game.Grabber.Held, "picked up from under one's own feet, in the air");
            Assert.AreEqual(L.OutlineScale, spool.MaxScale, 1e-3f, "in the air it gets no bigger than it is");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns > 0, 4f), "and without it there is nothing to land on (" + State() + ")");
            RunSeconds(0.2f);
            Assert.AreSame(spool, Game.Grabber.Held, "back at the start with the spool in the hand");
            Assert.AreEqual(L.SpoolMax, spool.MaxScale, 1e-4f, "where it is an ordinary spool again");
            Assert.Greater(Game.Grabber.Ratio, 1f, "that looks huge");

            bot = new Bot(Game);
            IEnumerator Again()
            {
                for (float z = 0f; z <= 6.6f; z += 0.5f)
                {
                    yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.1f);
                    yield return bot.LookAt(L.OutlineCentre);
                    yield return bot.Wait(0.1f);
                    if (level.SpoolTint == FitState.Good) break;
                }
                yield return bot.DropAt(L.OutlineCentre);
                yield return bot.Until(() => level.SpoolFills, 6f);
                yield return KeyToTheLock();
            }
            BotRunner.Run(Game, Again(), 90f);
            AssertCompleted("solved after that (" + Told(said) + ")");
            CollectionAssert.DoesNotContain(said, L.AirLine);
            Assert.AreEqual(0, level.HighHazard.Catches);
        }

        // ---- Restart, respawn, nothing lost ----------------------------------------------------------------------

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Load();
            // The key on the shelf, the spool in the drawer - and the restart key.
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            Assert.IsFalse(KeyIsTheBridge);
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Once.RestartPressed = true;
            Game.Tick();
            Assert.AreSame(level, Game.Level, "the same level, built again");
            Assert.IsTrue(KeyIsTheBridge, "the key lies across the drawer again");
            Assert.IsTrue(SpoolIsHome);
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(level.Door.IsOpen);
            Assert.IsTrue(level.Exit.Locked);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.05f);
            Assert.AreEqual(0, level.KeyLeash.Returns + level.SpoolLeash.Returns, "new leashes");

            // Again, up to the moment the door goes up with the key in it - and restart.
            bot = new Bot(Game);
            IEnumerator UpToTheLock()
            {
                yield return ShelveTheKey();
                yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
                yield return bot.WalkTo(L.ShelfStand, 0.15f);
                yield return bot.Grab(level.Key);
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
            }
            BotRunner.Run(Game, UpToTheLock(), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Door.Openness > 0.3f, 3f), State());
            Socket old = level.Lock;
            Game.RestartLevel();
            Assert.AreNotSame(old, level.Lock);
            Assert.IsFalse(level.Lock.Seated || level.Lock.Seating);
            Assert.IsFalse(level.Door.IsOpen);
            Assert.AreEqual(0f, level.Door.Openness, 1e-4f);
            Assert.IsTrue(level.Exit.Locked);
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsTrue(level.Key.Grabbable, "a fresh key, not the lock's");
            Assert.IsTrue(SpoolIsHome);
            RunSeconds(1f);
            Assert.IsTrue(KeyIsTheBridge);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.05f, "nothing of the old try reaches the new start");

            // With the spool in the hand.
            bot = new Bot(Game);
            BotRunner.Run(Game, bot.WalkTo(L.SpoolStand, 0.1f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Spool), 10f);
            Game.RestartLevel();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsTrue(SpoolIsHome);

            // And the whole solution, twice (the second time from inside the hall).
            TestHelpers.PlayLevel(Game, 60f);
            Game.RestartLevel();
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(KeyIsTheBridge);
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void PickedUpAgainOrRestarted_WhileTheDrawerDrawsTheSpoolIn_NothingIsLeftBehind()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            // From the back of the chest: full size with its near side over the chest, a third of a second's slide.
            var back = new Vector3(0f, 0f, -4.5f);
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, back, L.OutlineCentre), 30f);
            Assert.IsTrue(level.Guiding, State());
            RunSeconds(0.1f);
            Assert.IsTrue(level.Guiding, "still on its way");
            Assert.IsTrue(level.Spool.Driven);
            Vector3 onTheWay = level.Spool.Center;

            // Picked up again in mid-slide: it is in the hand, at the size it looks from here, and nothing drags on it.
            BotRunner.Run(Game, bot.Grab(level.Spool), 5f);
            Assert.AreSame(level.Spool, Game.Grabber.Held);
            RunSeconds(0.5f);
            Assert.IsFalse(level.Guiding);
            Assert.IsFalse(level.Spool.Driven);
            Assert.AreSame(level.Spool, Game.Grabber.Held);
            // Let go again from the mark: the ordinary way.
            BotRunner.Run(Game, bot.WalkTo(L.ThrowStand, 0.1f), 10f);
            BotRunner.Run(Game, bot.DropAt(L.OutlineCentre), 10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), State());
            Assert.Less(level.DoorGap(), 0.2f);

            // The restart key in mid-slide.
            Load();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, back, L.OutlineCentre), 30f);
            RunSeconds(0.1f);
            Assert.IsTrue(level.Guiding);
            Game.RestartLevel();
            Assert.IsFalse(level.Guiding);
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsTrue(SpoolIsHome, State());
            Assert.AreEqual(FitState.Idle, level.SpoolTint);
            RunSeconds(1f);
            Assert.IsTrue(SpoolIsHome, "nothing of the old try moves the new spool (" + State() + ")");
            Assert.AreEqual(FitState.Idle, level.SpoolTint);
            TestHelpers.PlayLevel(Game, 60f);
            CollectionAssert.IsEmpty(said);
            Assert.Greater(onTheWay.z, L.EdgeZ);
        }

        // The restart key while both toys are a second from being taken back: the spool too small in the
        // drawer, the key among the socks beside it.
        [Test]
        public void RestartedWhileBothToysAreAboutToBeTakenBack_NothingOfItReachesTheNewStart()
        {
            Load();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            // Too small: held against the outline from near the edge.
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, 5f), L.OutlineCentre), 30f);
            Assert.AreEqual(FitState.TooSmall, paintAtLetGo);
            // And the key after it: picked up from here (eleven units from the shelf, it looks tiny) and let go
            // from the edge, onto the spool or the socks.
            IEnumerator KeyAfterIt()
            {
                yield return bot.Grab(level.Key);
                yield return bot.WalkTo(new Vector3(0f, 0f, 7.3f), 0.2f);
                yield return bot.DropAt(new Vector3(0f, -1.4f, 15f));
            }
            BotRunner.Run(Game, KeyAfterIt(), 30f);
            RunSeconds(0.3f);
            Assert.IsTrue(level.KeyLost() || level.SpoolUseless(), "something is about to be taken back (" + State() + ")");
            int before = level.KeyLeash.Returns;
            Game.RestartLevel();
            List<string> said = Listen();
            Assert.AreEqual(0, before, "the key had not been taken back yet");
            Assert.IsTrue(KeyIsTheBridge);
            Assert.IsTrue(SpoolIsHome);
            RunSeconds(4f);
            Assert.IsTrue(KeyIsTheBridge, "the old try's key does not come back on the new try's shelf (" + State() + ")");
            Assert.IsTrue(SpoolIsHome);
            Assert.AreEqual(0, level.KeyLeash.Returns + level.SpoolLeash.Returns);
            Assert.AreEqual(FitState.Idle, level.SpoolTint);
            CollectionAssert.IsEmpty(said);
            TestHelpers.PlayLevel(Game, 60f);
            CollectionAssert.IsEmpty(said);
        }

        // Falling in with the spool in the hand, and later off the spool with the key in the hand: back at the
        // start each time with what was held, at the size it looked, and on with it.
        [Test]
        public void FallingInWithTheSpoolInTheHand_AndLaterWithTheKey_LosesNothing()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            BotRunner.Run(Game, bot.WalkTo(L.SpoolStand, 0.05f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Spool), 10f);
            float looks = Game.Grabber.Ratio;
            // Off the edge of the chest with it.
            Input = new ScriptedInput();
            Game.Input = Input;
            for (int i = TestHelpers.Ticks(8f); i > 0 && respawns == 0; i--)
            {
                TestHelpers.LookAt(Game.Player, new Vector3(2f, Game.Player.Eye.y, 12f));
                Input.Hold = new InputFrame { MoveZ = 1f };
                Game.Tick();
            }
            Input.Hold = default;
            Assert.AreEqual(1, respawns, State());
            Assert.AreSame(level.Spool, Game.Grabber.Held, "still in the hand");
            Assert.AreEqual(looks, Game.Grabber.Ratio, 1e-4f, "looking as big as it did");
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.1f);
            bot = new Bot(Game);
            IEnumerator OnWithIt()
            {
                yield return bot.WalkTo(L.ThrowStand, 0.1f);
                yield return bot.LookAt(L.OutlineCentre);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, OnWithIt(), 20f);
            Assert.AreEqual(FitState.Good, level.SpoolTint, State());
            BotRunner.Run(Game, bot.Click(), 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f), State());

            // The key from the shelf, onto the spool - and off its side.
            IEnumerator OntoTheSpool()
            {
                yield return bot.WalkTo(L.ShelfStand, 0.15f);
                yield return bot.Grab(level.Key);
                yield return level.Cross(bot);
            }
            BotRunner.Run(Game, OntoTheSpool(), 40f);
            looks = Game.Grabber.Ratio;
            Input = new ScriptedInput();
            Game.Input = Input;
            for (int i = TestHelpers.Ticks(8f); i > 0 && respawns == 1; i--)
            {
                TestHelpers.LookAt(Game.Player, new Vector3(5.9f, Game.Player.Eye.y, 19.5f));
                Input.Hold = new InputFrame { MoveZ = 1f };
                Game.Tick();
            }
            Input.Hold = default;
            Assert.AreEqual(2, respawns, "off the spool, between it and the atlas (" + State() + ")");
            Assert.AreSame(level.Key, Game.Grabber.Held, "the key is still in the hand");
            Assert.AreEqual(looks, Game.Grabber.Ratio, 1e-4f);
            Assert.IsTrue(level.SpoolFills, "and the spool stands where it stood");
            bot = new Bot(Game);
            IEnumerator Finish()
            {
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Finish(), 60f);
            AssertCompleted("solved after two falls (" + Told(said) + ")");
            CollectionAssert.IsEmpty(said);
            Assert.AreEqual(2, level.Hazard.Catches);
            Assert.AreEqual(0, level.KeyLeash.Returns + level.SpoolLeash.Returns);
        }

        [Test]
        public void AToyThatFallsOutOfTheWorld_ComesBack()
        {
            Load();
            List<string> said = Listen();
            Prop key = level.Key, spool = level.Spool;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;

            // Below the kill plane (a test fixture's shortcut: the drawer has a bottom). With nothing in the
            // drawer the key comes back as it began: the bridge.
            key.Unfreeze();
            key.SetScale(2f);
            key.SetPose(new Vector3(0f, level.KillY - 5f, 14f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns);
            Assert.IsTrue(KeyIsTheBridge, State());

            spool.Unfreeze();
            spool.SetScale(6f);
            spool.SetPose(new Vector3(0f, level.KillY - 5f, 14f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.AreEqual(2, respawns);
            Assert.IsTrue(SpoolIsHome, State());
            RunSeconds(1f);
            Assert.IsTrue(KeyIsTheBridge && SpoolIsHome, "where they stay");

            // With the spool standing in the drawer the bridge would lie through it: the key comes back on the
            // little shelf instead, at pocket size.
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            RunSeconds(2f);
            Assert.IsTrue(level.SpoolFills);
            key.SetPose(new Vector3(0f, level.KillY - 5f, 14f), Quaternion.identity);
            RunSeconds(1.5f);
            Assert.AreEqual(3, respawns);
            Assert.IsFalse(KeyIsTheBridge);
            Assert.IsTrue(KeyIsOnTheShelf, State());
            Assert.AreEqual(L.ShelfScale, key.Scale, 1e-3f);
            Assert.IsTrue(level.SpoolFills, "and the spool stands as it stood");

            BotRunner.Run(Game, KeyToTheLock(), 60f);
            AssertCompleted("solved with the key that came back (" + Told(said) + ")");
        }

        [Test]
        public void AKeyThatIsLostOrACrumb_IsPutBackOnTheShelf()
        {
            Load();
            List<string> said = Listen();
            Prop key = level.Key;
            BotRunner.Run(Game, ShelveTheKey(), 30f);

            // Small, among the socks: out of sight from the chest.
            key.SetScale(0.5f);
            key.SetPose(new Vector3(2f, L.PitFloor + 0.5f, 14f), Quaternion.identity);
            RunSeconds(1.2f);
            Assert.IsFalse(KeyIsOnTheShelf, "not before its grace time is over");
            RunSeconds(1.5f);
            Assert.IsTrue(KeyIsOnTheShelf, State());
            Assert.AreEqual(L.ShelfScale, key.Scale, 1e-3f, "at pocket size");
            Assert.Less(Quaternion.Angle(key.Rotation, Quaternion.Euler(0f, 180f, 0f)), 10f, "lying as it is painted on the shelf");
            Assert.Less(Vector3.Distance(key.Center, L.ShelfSpot), 0.1f, "on its paint (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.KeyLostLine }, said);
            Assert.AreEqual(1, level.KeyLeash.Returns);

            // Big, among the socks: in plain view from the edge of the chest. It stays (LEVELS: "a key on the
            // socks is 9 or more away, which makes it smaller at the door").
            said.Clear();
            key.SetScale(2f);
            key.SetPose(new Vector3(2f, L.PitFloor + 0.5f, 14f), Quaternion.identity);
            RunSeconds(4f);
            Assert.AreEqual(1, level.KeyLeash.Returns);
            Assert.Less(key.Center.y, -8f);
            CollectionAssert.IsEmpty(said);
            // Until the spool comes down over it.
            SpoolInTheDrawer(L.OutlineScale, lift: 1f);
            RunSeconds(4f);
            Assert.IsTrue(KeyIsOnTheShelf, State());
            Assert.IsTrue(level.SpoolFills, "the spool settles on the socks (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.KeyLostLine }, said);
            Assert.AreEqual(2, level.KeyLeash.Returns);

            // In the slot under the door.
            said.Clear();
            key.SetScale(0.3f);
            key.SetPose(new Vector3(0f, 0.2f, L.DoorZ + 0.6f), Quaternion.identity);
            RunSeconds(2.5f);
            Assert.IsTrue(KeyIsOnTheShelf, State());
            Assert.AreEqual(3, level.KeyLeash.Returns);

            // A crumb, wherever it lies.
            said.Clear();
            key.SetScale(0.07f);
            key.SetPose(new Vector3(1f, 0.3f, 2f), Quaternion.identity);
            RunSeconds(2.5f);
            Assert.IsTrue(KeyIsOnTheShelf, State());
            Assert.AreEqual(L.ShelfScale, key.Scale, 1e-3f);
            CollectionAssert.AreEqual(new[] { L.KeyCrumbLine }, said);
            Assert.AreEqual(4, level.KeyLeash.Returns);

            // A key of an ordinary size on the chest is nobody's business.
            said.Clear();
            key.SetScale(0.4f);
            key.SetPose(new Vector3(1f, 0.3f, 2f), Quaternion.identity);
            RunSeconds(4f);
            Assert.AreEqual(4, level.KeyLeash.Returns);
            CollectionAssert.IsEmpty(said);

            // And the level is still there to be solved: the key from where it lies on the chest, picked up from
            // a step away, over the spool and into the lock.
            IEnumerator End()
            {
                yield return bot.WalkTo(new Vector3(1f, 0f, 0.6f), 0.1f);
                yield return bot.Grab(key);
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, End(), 60f);
            AssertCompleted("solved after all that (" + Told(said) + ")");
        }

        // The little shelf is a place to put things, and the spool is a thing. The key that comes back while
        // the spool stands where it is to appear is not kept waiting out of sight (the level says "it is back"):
        // it appears on the chest in front of the shelf.
        [TestCase(1f)]
        [TestCase(2.2f)]
        public void AKeyThatComesBack_WhileTheSpoolStandsOnTheLittleShelf_AppearsBesideIt(float spoolScale)
        {
            Load();
            List<string> said = Listen();
            Prop key = level.Key, spool = level.Spool;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            // The spool on the middle of the shelf (a fixture's shortcut), and the key lost among the socks.
            float radius = ToyFactory.ThreadSpoolRadius * spoolScale;
            spool.Unfreeze();
            spool.SetScale(spoolScale);
            spool.SetPose(new Vector3(L.ShelfSpot.x, L.ShelfTop + 0.5f * spoolScale + 0.01f, Mathf.Max(L.ShelfSpot.z, L.BackZ + radius + 0.02f)), Quaternion.identity);
            key.SetScale(0.5f);
            key.SetPose(new Vector3(2f, L.PitFloor + 0.5f, 14f), Quaternion.identity);
            RunSeconds(3f);
            Assert.AreEqual(1, level.KeyLeash.Returns);
            CollectionAssert.AreEqual(new[] { L.KeyLostLine }, said);
            Assert.AreEqual(L.ShelfScale, key.Scale, 1e-3f, "at pocket size");
            Vector3 at = key.Center, stands = spool.Center;
            Assert.Greater(at.y, -0.1f, "the key is back on the chest's side of things (" + State() + ")");
            Assert.Less(at.z, 0f, "by the little shelf (" + State() + ")");
            Assert.Greater(new Vector2(at.x - stands.x, at.z - stands.z).magnitude, radius, "beside the spool, not under or in it (" + State() + ")");
            Assert.Less(key.Velocity.magnitude, 0.1f, "lying still");
            Assert.Less(at.y, 0.3f, "on the chest in front of the shelf (" + State() + ")");
            // In plain view: from the middle of the chest a click at it would pick it up.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-1f, 0f, -1f), 0.2f), 10f);
            BotRunner.Run(Game, bot.LookAt(key), 10f);
            Assert.AreSame(key, Game.Grabber.FindTarget(), "the key is in view (" + State() + ")");

            // And the level is still there to be solved: the spool from the shelf (picked up from a step in front
            // of it, held high - from this close it looks big - and let go when the paint is green), then the key.
            var high = new Vector3(0f, 11f, L.DoorZ);
            IEnumerator End()
            {
                yield return bot.WalkTo(new Vector3(stands.x, 0f, stands.z + radius + 0.9f), 0.1f);
                yield return bot.Grab(spool);
                for (float z = -2f; z <= 5f; z += 0.5f)
                {
                    yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.1f);
                    yield return bot.LookAt(high);
                    yield return bot.Wait(0.1f);
                    if (level.SpoolTint == FitState.Good) break;
                }
                yield return bot.DropAt(high);
                yield return bot.Until(() => level.SpoolFills, 6f);
                Vector3 lies = key.Center;
                var from = new Vector3(lies.x + 0.9f, 0f, lies.z + 0.9f);
                yield return bot.WalkTo(from, 0.15f);
                yield return bot.Grab(key);
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, End(), 90f);
            AssertCompleted("solved after all that (" + Told(said) + ")");
        }

        [Test]
        public void ASpoolThatIsACrumb_GoesBackToWhereItStood()
        {
            Load();
            List<string> said = Listen();
            Prop spool = level.Spool;
            // A spool of an ordinary size on the chest stays where it is put.
            spool.Unfreeze();
            spool.SetScale(2f);
            spool.SetPose(new Vector3(-2f, 1.1f, 4f), Quaternion.identity);
            RunSeconds(4f);
            Assert.IsFalse(SpoolIsHome);
            Assert.AreEqual(0, level.SpoolLeash.Returns);
            CollectionAssert.IsEmpty(said);

            // A crumb does not: nobody could pick it up from close enough to make it grow back.
            spool.SetScale(0.6f);
            spool.SetPose(new Vector3(-2f, 0.4f, 4f), Quaternion.identity);
            RunSeconds(1.2f);
            Assert.IsFalse(SpoolIsHome, "not before its grace time is over");
            RunSeconds(0.6f);
            Assert.IsTrue(SpoolIsHome, State());
            Assert.AreEqual(1, level.SpoolLeash.Returns);
            CollectionAssert.AreEqual(new[] { L.SpoolCrumbLine }, said);

            TestHelpers.PlayLevel(Game, 90f);
        }

        // LEVELS: "Fell in: HazardZone returns the player to the spawn, still holding whatever was held."
        [Test]
        public void FallingAmongTheSocks_PutsThePlayerBackAtTheStart_WithWhatTheyHold()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, bot.Grab(level.Key), 10f);
            // Off the edge of the chest, beside where the bridge was.
            Input = new ScriptedInput();
            Game.Input = Input;
            for (int i = TestHelpers.Ticks(8f); i > 0 && respawns == 0; i--)
            {
                TestHelpers.LookAt(Game.Player, new Vector3(3f, Game.Player.Eye.y, 12f));
                Input.Hold = new InputFrame { MoveZ = 1f };
                Game.Tick();
            }
            Input.Hold = default;
            Assert.AreEqual(1, respawns, State());
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(1, level.Hazard.Catches);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.1f);
            Assert.AreSame(level.Key, Game.Grabber.Held);

            // From there, the solution (the key is in the hand already).
            bot = new Bot(Game);
            BotRunner.Run(Game, level.PutKeyOnShelf(bot), 30f);
            BotRunner.Run(Game, TheRest(), 90f);
            AssertCompleted("solved after a fall");
        }

        [Test]
        public void TheSolution_PlaysTheSameEveryTime()
        {
            const int runs = 5;
            var ticks = new int[runs];
            var rest = new Vector3[runs];
            for (int run = 0; run < runs; run++)
            {
                Load();
                TestHelpers.PlayLevel(Game, 60f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Spool.Center;
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the spool comes to rest in the same place in run " + run);
            }

            // And once more in a game that has already been through a wrong try and a restart.
            Load();
            Drive(HoldSpool(L.SpoolStand, L.ThrowStand, new Vector3(0f, 2.6f, L.DoorZ)), 20f);
            Game.RestartLevel();
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(ticks[0], Game.LevelTicks, "the same solve after a restart");
        }

        // ---- The third review -------------------------------------------------------------------------------------

        // The two shoe-print marks keep what they promise. They used to be the solver's own two spots: a spool
        // picked up from a quarter of a unit behind the first prints (still on the mark, which reached 0.6
        // behind them) came out 8.1 to 8.3 from the second mark - red paint, too small by a hair. Now the
        // first prints are right behind the spool and the second mark is a third of a unit further back: from
        // the prints, from a step short of them and from right up against the spool, let go from the middle
        // or from any rim of the second mark, with the crosshair about the middle of the outline: green.
        [Test]
        public void FromAnywhereOnTheTwoMarks_ThePaintIsGreen_AndTheSpoolFillsTheDrawer()
        {
            Assert.Less(Vector3.Distance(L.SpoolMark, L.SpoolCentre - Vector3.up * L.SpoolCentre.y), 0.95f, "the prints are right behind the spool");
            Assert.Less(L.SpoolMark.z, L.SpoolCentre.z, "on the side one comes from");
            Assert.Less(Vector3.Distance(L.SpoolStand, L.SpoolMark), 0.3f, "the solver stops a short step short of them");
            Assert.That(L.ThrowStand.z - L.ThrowMark.z, Is.InRange(0.2f, 0.45f), "and lets go from the front half of the second mark");
            Load();
            foreach (string name in new[] { "Stand Mark Spool", "Stand Mark Door" })
            {
                Transform mark = Game.LevelRoot.Find(name);
                Assert.IsNotNull(mark, name);
                Vector3 wanted = name == "Stand Mark Spool" ? L.SpoolMark : L.ThrowMark;
                Assert.Less(Vector3.Distance(mark.position, wanted + Vector3.up * 0.012f), 1e-3f, name + " is painted where the level says it is");
            }

            // Pick-up places about the first prints (the last one: walked straight up to the spool from the start).
            var picks = new[]
            {
                L.SpoolMark, L.SpoolMark + new Vector3(0f, 0f, -0.35f), L.SpoolMark + new Vector3(0.3f, 0f, -0.3f), L.SpoolMark + new Vector3(-0.3f, 0f, -0.3f),
                L.SpoolMark + new Vector3(0.35f, 0f, 0.05f), new Vector3(2.45f, 0f, 2.3f),
            };
            // Let-go places on the second mark: its middle, its front and back rims, its sides.
            var stands = new[]
            {
                L.ThrowMark, L.ThrowMark + new Vector3(0f, 0f, 0.55f), L.ThrowMark + new Vector3(0f, 0f, -0.55f), L.ThrowMark + new Vector3(0.55f, 0f, 0.1f), L.ThrowMark + new Vector3(-0.5f, 0f, 0.25f),
            };
            // The crosshair about the middle of the outline.
            var aims = new[] { L.OutlineCentre, L.OutlineCentre + new Vector3(-1f, -1.4f, 0f), L.OutlineCentre + new Vector3(1.2f, 2f, 0f) };
            int tries = 0;
            var wrong = new List<string>();
            for (int p = 0; p < picks.Length; p++)
                for (int t = 0; t < stands.Length; t++)
                {
                    // (Every pick-up with every let-go place; the crosshair goes round. The worst pair - the rear of
                    // the first mark, the front rim of the second - with the crosshair low as well.)
                    Vector3 aim = p == 1 && t == 1 ? aims[1] : aims[(p + t) % aims.Length];
                    Load();
                    List<string> said = Listen();
                    float ratio = 0f, dropped = 0f;
                    BotRunner.Run(Game, ShelveTheKey(), 30f);
                    Game.Events.PropGrabbed += e => ratio = e.OldScale / e.GrabDistance;
                    Game.Events.PropDropped += e => dropped = e.NewScale;
                    bool ran = Drive(HoldSpool(picks[p], stands[t], aim), 30f);
                    bool fills = ran && TestHelpers.RunUntil(Game, () => level.SpoolFills, 6f);
                    tries++;
                    if (!ran || !fills || paintAtLetGo != FitState.Good || said.Count > 0 || level.DoorGap() > 0.2f)
                        wrong.Add("picked up from " + F(picks[p]) + " (looks " + ratio.ToString("0.000") + "), let go from " + F(stands[t]) + " toward " + F(aim) + ": " + dropped.ToString("0.00") + ", paint " + paintAtLetGo +
                                  (ran ? "" : ", the script failed") + " (" + State() + ", " + Told(said) + ")");
                }
            Assert.AreEqual(30, tries);
            CollectionAssert.IsEmpty(wrong, "on the marks, and not green or not filling the drawer");
        }

        // The spool that is too small goes back to where it stood. If the key lies there by then (put down
        // on the spool's place while the spool was away), the spool came back INTO it: a pocket key stayed
        // where it lay, inside the spool and out of sight; a bigger one lay through it (one of eight, sixteen
        // units long, was pressed into the chest's top). Now the key is put on top of the spool, in plain view.
        [TestCase(0.5f)]
        [TestCase(1.5f)]
        [TestCase(4f)]
        public void TheSpoolComesBackToWhereTheKeyLiesByThen_AndTheKeyIsOnTopOfIt_NotInside(float keyScale)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Prop spool = level.Spool, key = level.Key;
            // Too small: held against the outline from near the edge.
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, 5.5f), L.OutlineCentre), 30f);
            Assert.AreEqual(FitState.TooSmall, paintAtLetGo);
            // (A fixture's shortcut: the key lying where the spool stood, as if it had been put down there.)
            key.SetScale(keyScale);
            key.SetPose(new Vector3(L.SpoolCentre.x, 0.02f * keyScale + 0.01f, L.SpoolCentre.z), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), "the spool goes back to where it stood (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);

            float deepest = float.MaxValue;
            for (int i = 0; i < 180; i++)
            {
                Game.Tick();
                // How far the key's nearest point is from the spool's axis while it is lower than the spool's top:
                // inside the spool if that is less than its radius.
                Vector3 nearest = GadgetKit.ClosestPoint(key, spool.Center);
                if (nearest.y < spool.Center.y + 0.5f * spool.Scale - 0.03f)
                    deepest = Mathf.Min(deepest, new Vector2(nearest.x - spool.Center.x, nearest.z - spool.Center.z).magnitude);
            }
            Assert.IsTrue(SpoolIsHome, "the spool stands where it stood (" + State() + ")");
            Assert.Greater(deepest, ToyFactory.ThreadSpoolRadius * spool.Scale - 0.06f, "the key is never inside the spool (" + State() + ")");
            Assert.AreEqual(keyScale, key.Scale, 1e-3f, "it keeps its size");
            Assert.IsFalse(level.KeyLost(), "it is not lost");
            Assert.AreEqual(0, level.KeyLeash.Returns);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => key.Velocity.sqrMagnitude < 0.01f, 4f), "and it comes to rest (" + State() + ")");
            GadgetKit.VerticalExtent(key, out float lowest, out _);
            Assert.Greater(lowest, -0.03f, "on the spool or on the chest beside it, not pressed into the chest's top (" + State() + ")");

            // In plain view: picked up from a few steps away, put on the little shelf, and on with the solution
            // (the key picked up from the middle of the chest, which makes even a big one small enough).
            bot = new Bot(Game);
            IEnumerator Rest()
            {
                yield return bot.WalkTo(new Vector3(0.5f, 0f, -1f), 0.2f);
                yield return bot.Grab(key);
                yield return level.PutKeyOnShelf(bot);
                yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
                yield return bot.WalkTo(new Vector3(1f, 0f, 1f), 0.2f);
                yield return bot.Grab(key);
                yield return level.Cross(bot);
                yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Rest(), 120f);
            AssertCompleted("solved with the key from on top of the spool (" + Told(said) + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
        }

        // The rule high over the drawer is for a drawer that has not been filled. With the biggest spool
        // standing in it, whoever is up there (off a key stood on its edge on the spool, say) comes down on
        // the spool and is where they may be - and is not told to fill a drawer that is full.
        [Test]
        public void HighOverADrawerThatIsFilled_NobodyIsSentBack()
        {
            var high = new Vector3(0f, 6.5f, 14f);
            // Nothing in the drawer: sent back from up there, and told why.
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            // (A fixture's shortcut for having climbed something.)
            Game.Player.Teleport(high, 0f);
            RunSeconds(1f);
            Assert.AreEqual(1, level.HighHazard.Catches, State());
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(0, level.Hazard.Catches, "before they are anywhere near the socks");
            CollectionAssert.AreEqual(new[] { L.HighLine }, said);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.5f);

            // The drawer filled: down onto the spool, and nothing is said.
            foreach (float scale in new[] { L.FillMin, L.SpoolMax })
            {
                Load();
                said = Listen();
                respawns = 0;
                Game.Events.PlayerRespawned += e => respawns++;
                BotRunner.Run(Game, ShelveTheKey(), 30f);
                SpoolInTheDrawer(scale);
                RunSeconds(2f);
                Assert.IsTrue(level.SpoolFills && level.DrawerFilled, State());
                Game.Player.Teleport(high, 0f);
                RunSeconds(2f);
                Assert.AreEqual(0, level.HighHazard.Catches, "over a spool of " + scale + " (" + State() + ")");
                Assert.AreEqual(0, respawns);
                Assert.AreSame(level.Spool, Game.Player.GroundProp, "down on the spool (" + State() + ")");
                CollectionAssert.IsEmpty(said);
                // And on with it from there: the key is fetched and carried across.
                bot = new Bot(Game);
                IEnumerator Rest()
                {
                    // (Back to the chest: a running jump from the spool's near rim.)
                    yield return bot.WalkTo(new Vector3(0f, 0f, level.Spool.Center.z - ToyFactory.ThreadSpoolRadius * scale + 1.2f), 0.2f, 8f);
                    yield return bot.WalkTo(new Vector3(0f, 0f, level.Spool.Center.z - ToyFactory.ThreadSpoolRadius * scale + 0.4f), 0.2f, 8f, true);
                    yield return bot.Jump();
                    yield return bot.WalkTo(new Vector3(0f, 0f, 5f), 0.3f, 8f, true);
                    yield return KeyToTheLock();
                }
                BotRunner.Run(Game, Rest(), 120f);
                AssertCompleted("solved from there, over a spool of " + scale + " (" + Told(said) + ")");
            }

            // The spool picked up again: the rule is back.
            Load();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            SpoolInTheDrawer(L.OutlineScale);
            RunSeconds(2f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 5f), 0.2f), 10f);
            BotRunner.Run(Game, bot.Grab(level.Spool), 10f);
            Assert.IsFalse(level.DrawerFilled, "in the hand it fills nothing");
            Game.Player.Teleport(high, 0f);
            RunSeconds(1f);
            Assert.AreEqual(1, level.HighHazard.Catches, "with the drawer empty again the rule is back (" + State() + ")");
        }

        // The way round that the third review found. The door's face stood 0.015 behind the wall's: a sill 0.015
        // deep, 0.13 over the key - and the spool cannot tip. Carried across the key and let go against the shut
        // door it hung there by its rim (42 of 100 tries). Hopped onto from the key it was something else to
        // stand on at the door: the key was picked up from it (the spool stayed where it hung), let go at the
        // keyhole, and the way in was a step down from the spool - out, and the drawer was never filled.
        // Now the door stops things at the wall's own face (there is no sill to hang on), and nothing but the
        // key holds a spool up whose middle is over the drawer: it slips off whatever its rim rests on.
        [TestCase(1.15f, 1.7f, 0.9f, 0f)]
        [TestCase(1.15f, 1.7f, 1.1f, 0.45f)]
        [TestCase(0.9f, 1.7f, 0.7f, 0f)]
        [TestCase(1.15f, 2f, 0.7f, 0f)]
        [TestCase(1.15f, 1.7f, 1.8f, 0f)]
        public void TheSpoolLetGoAgainstTheShutDoor_HangsOnNoSill_AndIsNoPlaceToUnlockFrom(float pickBehind, float from, float aimY, float x)
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Prop key = level.Key, spool = level.Spool;
            Player player = Game.Player;
            IEnumerator Carry()
            {
                yield return bot.WalkTo(L.SpoolCentre + new Vector3(0f, -L.SpoolCentre.y, -pickBehind), 0.05f);
                yield return bot.Grab(spool);
                yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.1f);
                yield return bot.WalkTo(new Vector3(x, 0f, L.DoorZ - from), 0.05f, 12f);
                yield return bot.DropAt(new Vector3(x, aimY, L.DoorZ));
            }
            BotRunner.Run(Game, Carry(), 40f);
            RunSeconds(1.2f);
            float bottom = spool.Center.y - 0.5f * spool.Scale, size = spool.Scale;
            string stood = "the spool, " + size.ToString("0.000") + " at " + F(spool.Center) + ", its foot at " + bottom.ToString("0.000");
            Assert.That(size, Is.InRange(0.8f, 1.2f), "small enough to hop onto from the key (" + State() + ")");
            Assert.Less(L.DoorZ - (spool.Center.z + ToyFactory.ThreadSpoolRadius * size), 0.3f, "and at the door (" + State() + ")");

            // Onto it from the key.
            IEnumerator Hop()
            {
                Vector3 top = spool.Center;
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z - ToyFactory.ThreadSpoolRadius * spool.Scale - 0.35f), 0.05f, 4f);
                yield return bot.LookAt(new Vector3(top.x, bot.Player.Eye.y, top.z + 5f));
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z), 0.15f, 2f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
            }
            bool hopped = Drive(Hop(), 20f) && player.GroundProp == spool;
            // The key from up there (its blade shows beside the spool), and at once the keyhole.
            IEnumerator Take()
            {
                yield return bot.LookAt(new Vector3(x > 0.2f ? -0.55f : 0.68f, 0.33f, bot.Player.Position.z - 1.2f));
                yield return bot.Click();
                yield return bot.DropAt(L.Keyhole, 1.5f);
                yield return bot.Until(() => level.Door.IsOpen, 4f);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.ExitCentre.z), 0.4f, 6f);
            }
            if (hopped)
            {
                bot = new Bot(Game);
                Drive(Take(), 12f);
            }
            RunSeconds(4f);
            string what = stood + "; hopped onto it: " + hopped + "; " + State() + ", " + Told(said);
            Assert.IsFalse(Game.LevelCompleted, "out through the door, and the drawer was never filled - " + what);
            Assert.AreEqual(0.32f, bottom, 0.04f, "let go against the door the spool stands on the key - it hangs on no sill - " + what);
            Assert.IsFalse(level.SpoolFills);
            if (hopped) Assert.GreaterOrEqual(respawns, 1, "with the key goes the floor: the spool and whoever stood on it fall - " + what);
            RunSeconds(3f);
            Assert.IsTrue(spool.Frozen || spool.Held || spool.Center.z < L.EdgeZ, "nothing is left at the door: the spool is back where it stood (" + State() + ")");

            // Whatever became of the two toys, both can be seen and picked up, and the level is solved the ordinary way.
            bot = new Bot(Game);
            IEnumerator Again()
            {
                // (Off the key first, if that is what the fall left underfoot: nobody picks up what they stand on.)
                yield return bot.WalkTo(new Vector3(4.3f, 0f, -3.5f), 0.3f, 14f);
                if (Game.Grabber.Held == null && !level.Lock.Seated) yield return bot.Grab(key);
                if (Game.Grabber.Held == key) yield return level.PutKeyOnShelf(bot);
                if (!level.SpoolFills)
                {
                    if (spool.Frozen) yield return level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre);
                    else yield return SpoolAgain();
                }
                if (!level.Lock.Seated)
                {
                    yield return bot.WalkTo(L.ShelfStand, 0.15f);
                    yield return bot.Grab(key);
                }
                yield return level.Cross(bot);
                if (!level.Lock.Seated) yield return bot.DropAt(L.Keyhole);
                yield return level.Leave(bot);
            }
            BotRunner.Run(Game, Again(), 120f);
            AssertCompleted("solved the ordinary way afterwards (" + Told(said) + ")");
        }

        // What is left of that way round is a race against the door, and the door wins it: from on top of the
        // spool that stands on the key at the door - jump, pick the key up in the same instant (the spool falls
        // away), a flick to the keyhole, let go. The lock takes the key; the door starts to rise 0.85 s later
        // and a body's height is free 0.3 s after that; the jump is over the sill for 0.8 s. (LEVELS: "letting
        // go of it at the lock during that jump opens the door but leaves no bridge".) If the lock or the door
        // is ever made quicker, this is the test that says so.
        [Test]
        public void FromTheSpoolOnTheKeyAtTheDoor_AJumpWithTheKeyLetGoAtTheKeyhole_IsOverBeforeTheDoorIsOpen()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Prop key = level.Key, spool = level.Spool;
            Player player = Game.Player;
            IEnumerator Carry()
            {
                yield return bot.WalkTo(L.SpoolStand, 0.05f);
                yield return bot.Grab(spool);
                yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.1f);
                yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 1.7f), 0.05f, 12f);
                yield return bot.DropAt(new Vector3(0f, 0.9f, L.DoorZ));
                yield return bot.Wait(1.2f);
                Vector3 top = spool.Center;
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z - ToyFactory.ThreadSpoolRadius * spool.Scale - 0.35f), 0.05f, 4f);
                yield return bot.LookAt(new Vector3(top.x, bot.Player.Eye.y, top.z + 5f));
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(top.x, 0f, top.z + 0.15f), 0.1f, 2f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return bot.Wait(0.3f);
            }
            BotRunner.Run(Game, Carry(), 60f);
            Assert.AreSame(spool, player.GroundProp, "on the spool, on the key, at the door (" + State() + ")");
            float from = player.Position.y;

            var input = new ScriptedInput();
            Game.Input = input;
            var blade = new Vector3(0.68f, 0.33f, player.Position.z - 1.2f);
            // The jump and the pick-up in one instant; the flick to the keyhole; the let-go.
            input.Once = Aim(blade, new InputFrame { Jump = true, GrabPressed = true });
            Game.Tick();
            Assert.AreSame(key, Game.Grabber.Held, "the key is picked up from on top of the spool (" + State() + ")");
            input.Once = Aim(L.Keyhole, new InputFrame { MoveZ = 1f });
            Game.Tick();
            input.Once = Aim(L.Keyhole, new InputFrame { MoveZ = 1f, GrabPressed = true });
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            int letGo = Game.LevelTicks, taken = -1, underTheSill = -1, doorMoves = -1;
            float highest = from;
            for (int i = 0; i < 240 && respawns == 0 && !Game.LevelCompleted; i++)
            {
                input.Once = Aim(new Vector3(0f, player.Eye.y, 40f), new InputFrame { MoveZ = 1f });
                Game.Tick();
                highest = Mathf.Max(highest, player.Position.y);
                if (taken < 0 && (level.Lock.Seating || level.Lock.Seated)) taken = Game.LevelTicks;
                if (underTheSill < 0 && player.Position.y < L.SillY) underTheSill = Game.LevelTicks;
                if (doorMoves < 0 && level.Door.Openness > 0.001f) doorMoves = Game.LevelTicks;
            }
            RunSeconds(2.5f);
            string what = "jumped from " + from.ToString("0.00") + " to " + highest.ToString("0.00") + "; the lock took the key " + (taken - letGo) + " ticks after it was let go, the feet were under the sill after " +
                          (underTheSill - letGo) + ", the door began to move after " + (doorMoves < 0 ? "-" : (doorMoves - letGo).ToString()) + " (" + State() + ")";
            Assert.GreaterOrEqual(taken, 0, "the lock takes the key that is let go at it in mid-air - " + what);
            Assert.Greater(highest, from + 1f, "a full jump - " + what);
            Assert.IsFalse(Game.LevelCompleted, "nobody gets in during that jump - " + what);
            Assert.AreEqual(1, respawns, "down among the socks, and back at the start - " + what);
            Assert.IsTrue(level.Door.IsOpen, "the door is open by now, twelve units of socks away - " + what);
            Assert.Greater(underTheSill, 0);
            Assert.Less(underTheSill - letGo, TestHelpers.Ticks(0.85f), "the jump is over the sill for less time than the door takes to begin - " + what);
            Assert.GreaterOrEqual(doorMoves - underTheSill, 2, "the door has not begun to move when the feet are under the sill - " + what);

            // And then the spool, as intended.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 4f), "the spool that fell is back where it stood (" + State() + ")");
            bot = new Bot(Game);
            BotRunner.Run(Game, level.FillDrawer(bot, L.SpoolStand, L.ThrowStand, L.OutlineCentre), 30f);
            BotRunner.Run(Game, level.Cross(bot), 30f);
            BotRunner.Run(Game, level.Leave(bot), 30f);
            AssertCompleted("across the spool to the open door");
        }

        // Nothing but the key holds a spool up whose middle is over the drawer. It cannot tip, so it used to hang
        // wherever its rim found a hold: on the chest's edge (a spool six units high, 6.3 of its 6.6 out over
        // the drawer), and on the sill of the open doorway. Now it slips off, the way it would have tipped.
        [TestCase(1f, 8.3f)]
        [TestCase(4f, 9.95f)]
        [TestCase(6f, 11f)]
        public void ASpoolWhoseMiddleIsOverTheDrawer_HangsOnNothing(float scale, float middle)
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Prop spool = level.Spool;
            float radius = ToyFactory.ThreadSpoolRadius * scale;
            Assert.Less(middle - radius, L.EdgeZ - 0.2f, "its rim is on the chest");
            spool.Unfreeze();
            spool.SetScale(scale);
            spool.SetPose(new Vector3(0f, 0.5f * scale + 0.01f, middle), Quaternion.identity);
            bool slipped = false;
            for (int i = 0; i < 60; i++)
            {
                Game.Tick();
                slipped |= level.Slipping;
            }
            Assert.IsTrue(slipped, "it slips off the chest's edge (" + State() + ")");
            Assert.Less(spool.Center.y - 0.5f * scale, -1f, "and falls into the drawer (" + State() + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 4f), "too small to stand on: back where it stood (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);

            // With its middle over the chest it stands as it always did, however far its rim reaches out.
            spool.Unfreeze();
            spool.SetScale(scale);
            spool.SetPose(new Vector3(0f, 0.5f * scale + 0.01f, L.EdgeZ - 0.1f), Quaternion.identity);
            RunSeconds(2f);
            Assert.IsFalse(level.Slipping);
            Assert.AreEqual(0.5f * scale, spool.Center.y, 0.03f, "on the chest (" + State() + ")");
            Assert.AreEqual(L.EdgeZ - 0.1f, spool.Center.z, 0.03f, "where it was put");
            Assert.AreEqual(1, level.SpoolLeash.Returns);

            // With somebody on it (and the door open: the leap from a tower that hung out was the way round).
            Load();
            said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            OpenTheDoor();
            spool = level.Spool;
            spool.Unfreeze();
            spool.SetScale(scale);
            spool.SetPose(new Vector3(0f, 0.5f * scale + 0.01f, middle), Quaternion.identity);
            Game.Player.Teleport(new Vector3(0f, scale + 0.03f, middle), 0f);
            var input = new ScriptedInput();
            Game.Input = input;
            for (int i = 0; i < 300 && respawns == 0 && !Game.LevelCompleted; i++)
            {
                // Off toward the door at a run, with a jump, the moment the feet are down.
                input.Once = Aim(new Vector3(0f, Game.Player.Eye.y, 40f), new InputFrame { MoveZ = 1f, Sprint = true, Jump = Game.Player.Grounded && i > 2 });
                Game.Tick();
            }
            RunSeconds(0.5f);
            Assert.IsFalse(Game.LevelCompleted, "nobody gets out over a spool that hangs (" + State() + ", " + Told(said) + ")");
            Assert.AreEqual(1, respawns, "down among the socks, or sent back from high over them (" + State() + ")");
            Assert.Less(Vector3.Distance(Game.Player.Position, L.Spawn), 0.5f);
        }

        // Held too low from near the edge the spool is stopped by the chest's top with its middle already past
        // the edge. It used to hang there by its rim (and be told off for touching the floor); now it slips
        // into the drawer, comes back, and is told the same thing.
        [Test]
        public void StoppedByTheChestsTopWithItsMiddlePastTheEdge_TheSpoolSlipsIn_ComesBack_AndTheLevelSaysWhy()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Vector3 letGoAt = Vector3.zero;
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                letGoAt = e.Prop.Center;
            };
            BotRunner.Run(Game, HoldSpool(L.SpoolStand, new Vector3(0f, 0f, 4f), new Vector3(0f, 1.75f, L.DoorZ)), 30f);
            Assert.That(dropped, Is.InRange(2.5f, 6f), "it touched the chest's top long before the door wall");
            Assert.Greater(letGoAt.z, L.EdgeZ, "with its middle past the edge (" + F(letGoAt) + ")");
            Assert.Less(letGoAt.z - ToyFactory.ThreadSpoolRadius * dropped, L.EdgeZ, "and its rim on the chest");
            Assert.AreEqual(FitState.TooSmall, paintAtLetGo, "the paint said: not like this");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Slipping, 1f), "it does not hang there (" + State() + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), "into the drawer, too small, and back to where it stood (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolFloorLine }, said);
            Assert.AreEqual(1, level.SpoolLeash.Returns);

            BotRunner.Run(Game, TheRest(), 90f);
            AssertCompleted("held against the outline it works");
            CollectionAssert.AreEqual(new[] { L.SpoolFloorLine }, said);
        }

        // The same at the other end of the drawer: the sill of the open doorway is two units deep, and a spool
        // small enough for the doorway stood on its edge with its middle outside. And one that is big enough
        // to fill the drawer does not slip into it where it hangs: the drawer draws it to the door, as ever.
        [Test]
        public void OnTheSillOfTheOpenDoorway_AndBigEnoughOnTheChestsEdge_TheSpoolDoesNotHangEither()
        {
            Load();
            List<string> said = Listen();
            OpenTheDoor();
            Prop spool = level.Spool;
            spool.Unfreeze();
            spool.SetScale(1.2f);
            spool.SetPose(new Vector3(0f, L.SillY + 0.61f, L.DoorZ - 0.4f), Quaternion.identity);
            Assert.Greater(spool.Center.z + ToyFactory.ThreadSpoolRadius * 1.2f, L.DoorZ + 0.2f, "its rim is on the sill");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Slipping, 1f), "it slips off the sill (" + State() + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => SpoolIsHome, 5f), "into the drawer, and back to where it stood (" + State() + ")");
            CollectionAssert.AreEqual(new[] { L.SpoolSmallLine }, said);
            // Inside the doorway, its middle over the sill, it stands (and can be seen and picked up from the chest).
            spool.Unfreeze();
            spool.SetScale(1.2f);
            spool.SetPose(new Vector3(0f, L.SillY + 0.61f, L.DoorZ + 0.3f), Quaternion.identity);
            RunSeconds(2f);
            Assert.IsFalse(level.Slipping);
            Assert.AreEqual(L.DoorZ + 0.3f, spool.Center.z, 0.03f, State());
            Assert.AreEqual(L.SillY + 0.6f, spool.Center.y, 0.03f, State());
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 6f), 0.2f), 10f);
            BotRunner.Run(Game, bot.Grab(spool), 10f);
            Assert.AreSame(spool, Game.Grabber.Held, "in view through the doorway");

            // Big enough, with its rim on the chest's edge: drawn to the middle of the drawer against the door wall.
            Load();
            said = Listen();
            BotRunner.Run(Game, ShelveTheKey(), 30f);
            spool = level.Spool;
            spool.Unfreeze();
            spool.SetScale(9f);
            spool.SetPose(new Vector3(0.5f, 4.51f, L.EdgeZ + 0.6f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.SpoolFills, 5f), "the drawer takes it (" + State() + ")");
            Assert.That(level.DoorGap(), Is.InRange(-0.01f, 0.2f), State());
            Assert.AreEqual(0f, spool.Center.x, 0.05f);
            CollectionAssert.IsEmpty(said);
        }
    }

    /// <summary>Level 12's hints on the pause card (the game's own runner and menus, fake devices).</summary>
    public class Level12UiTests
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
            host = new GameObject("Level 12 Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=12"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            devices.State.EscapePressed = false;
            PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            Assert.AreEqual(12, runner.Game.Level.Id);
            string[] hints = runner.Game.Level.Hints;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                runner.Frame(Sim.Dt);
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                int lines = pause.HintText.textInfo.lineCount;
                Debug.Log("[Toybox] Level 12, hint " + (h + 1) + ": " + hints[h].Length + " characters, " + lines + " lines at size " + size.ToString("0.0"));
                Assert.Greater(lines, 0, "the text was laid out");
                Assert.LessOrEqual(lines, 5, "hint " + (h + 1) + " is more than five lines on the card");
                Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }

    /// <summary>
    /// Pictures of Level 12 at other moments than its solver's - the paint as a gauge (red, green), the spool
    /// on its way into the drawer, the key in the way, the door seen from the key:
    ///
    ///   tools/unity.ps1 exec -Method Toybox.Tests.Level12Stills.Capture -UnityArgs '-toyboxUi','hud'
    ///   (add '-toyboxTour','door' for the spool at the shut door and the spool held too low, 'side' for the spool held to the side)
    ///
    /// Written to -toyboxOut (tools/out/shots/level12/tour), in a folder per script. Not a test.
    /// </summary>
    public static class Level12Stills
    {
        // The level with a script of somebody else's choosing in place of its Solve.
        sealed class Tour : LevelDefinition
        {
            readonly Level12Keyhole level = new Level12Keyhole();
            readonly System.Func<Bot, Level12Keyhole, IEnumerator> script;

            public Tour(System.Func<Bot, Level12Keyhole, IEnumerator> script)
            {
                this.script = script;
            }

            public override string Slug => level.Slug;
            public override string Title => level.Title;
            public override string Blurb => level.Blurb;
            public override string[] Hints => level.Hints;
            public override string Environment => level.Environment;
            public override int EnvironmentVisit => level.EnvironmentVisit;
            public override float GroundY => level.GroundY;
            public override float KillY => level.KillY;
            public override void Build(LevelContext ctx) => level.Build(ctx);
            public override IEnumerator Solve(Bot bot) => script(bot, level);
        }

        static IEnumerator At(Bot bot, float seconds) => bot.Until(() => bot.Game.Time >= seconds, 30f);

        // The key shelved, then the spool held against the outline from three places, let go from the last.
        static IEnumerator Gauge(Bot bot, Level12Keyhole level)
        {
            yield return bot.Grab(level.Key);
            yield return level.PutKeyOnShelf(bot);
            yield return bot.WalkTo(L.SpoolStand, 0.1f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(new Vector3(0f, 0f, 5f), 0.1f);
            yield return bot.LookAt(L.OutlineCentre);
            yield return At(bot, 6f);       // 6: from near the edge - it covers its outline and is too small: red
            yield return bot.WalkTo(L.ThrowStand, 0.1f);
            yield return bot.LookAt(L.OutlineCentre);
            yield return At(bot, 9f);       // 9: from the mark: green
            yield return bot.WalkTo(new Vector3(0f, 0f, -4.5f), 0.1f);
            yield return bot.LookAt(L.OutlineCentre);
            yield return At(bot, 13f);      // 13: from the back of the chest: as big as it gets, green
            yield return bot.Click();       // 13.2: the drawer draws it in
            yield return At(bot, 16f);      // 16: in the drawer; the paint stays green
            yield return bot.LookAt(new Vector3(4f, 2f, 12f));
            yield return At(bot, 17.5f);    // 17.5: the line along the atlas
            yield return bot.LookAt(new Vector3(4.6f, 0.6f, -5.2f));
            yield return At(bot, 19f);      // 19: the blocks in the corner
            yield return bot.WalkTo(new Vector3(-1f, 0f, -2f), 0.2f);
            yield return bot.LookAt(L.ShelfSpot);
            yield return At(bot, 21.5f);    // 21.5: the key on the little shelf
            yield return bot.Wait(30f);
        }

        // The spool held up while the key still lies across the drawer, and let go.
        static IEnumerator KeyInTheWay(Bot bot, Level12Keyhole level)
        {
            yield return bot.WalkTo(L.SpoolStand, 0.1f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(L.ThrowStand, 0.1f);
            yield return bot.LookAt(L.OutlineCentre);
            yield return At(bot, 4f);       // 4: big enough, and red: the key is in the way
            yield return bot.Click();
            yield return At(bot, 7f);       // 7: it stands on the key, and the level says why
            yield return bot.Wait(30f);
        }

        // Across the key to the door, and a click at the key underfoot.
        static IEnumerator AtTheDoor(Bot bot, Level12Keyhole level)
        {
            yield return bot.WalkTo(new Vector3(0f, 0f, 12f), 0.1f, 12f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 18.6f), 0.05f, 6f);
            yield return bot.LookAt(new Vector3(0f, 1.6f, L.DoorZ));
            yield return At(bot, 7f);       // 7: the door from the key: locked
            yield return bot.LookAt(new Vector3(0f, 0.3f, 16.5f));
            yield return bot.Click();
            yield return At(bot, 8.5f);     // 8.5: nobody picks up what they stand on
            yield return bot.Wait(30f);
        }

        // The spool held far to one side (it catches on the side wall and stays too small), let go, and looked
        // at from the start: where does it end up, and what does that look like?
        static IEnumerator HeldToTheSide(Bot bot, Level12Keyhole level)
        {
            yield return bot.Grab(level.Key);
            yield return level.PutKeyOnShelf(bot);
            yield return bot.WalkTo(L.SpoolStand, 0.1f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(L.ThrowStand, 0.1f);
            yield return bot.LookAt(new Vector3(-4.5f, L.OutlineCentre.y, L.DoorZ));
            yield return At(bot, 5.5f);     // 5.5: held to the side: red
            yield return bot.Click();
            yield return At(bot, 6.5f);     // 6.5: a second after it was let go
            yield return bot.WalkTo(new Vector3(2f, 0f, -3f), 0.2f);
            yield return bot.LookAt(new Vector3(-2f, 2.5f, 9f));
            yield return At(bot, 9.5f);     // 9.5: where it stands (or stood)
            yield return bot.Wait(30f);
        }

        // The spool carried across the key and let go against the shut door (it used to hang on the door's sill),
        // hopped onto, and the key picked up from it.
        static IEnumerator SpoolAtTheDoor(Bot bot, Level12Keyhole level)
        {
            yield return bot.WalkTo(L.SpoolStand, 0.05f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(new Vector3(0f, 0f, 6f), 0.1f);
            yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 1.7f), 0.05f, 12f);
            yield return bot.LookAt(new Vector3(0f, 0.9f, L.DoorZ));
            yield return At(bot, 6f);       // 6: the spool held against the door, from the key
            yield return bot.Click();
            yield return bot.WalkTo(new Vector3(0f, 0f, L.DoorZ - 3.2f), 0.1f, 6f);
            yield return bot.LookAt(new Vector3(0f, 0.6f, L.DoorZ - 0.5f));
            yield return At(bot, 8f);       // 8: it stands on the key at the door
            Vector3 top = level.Spool.Center;
            yield return bot.WalkTo(new Vector3(top.x, 0f, top.z - ToyFactory.ThreadSpoolRadius * level.Spool.Scale - 0.35f), 0.05f, 4f);
            yield return bot.LookAt(new Vector3(top.x, bot.Player.Eye.y, top.z + 5f));
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(top.x, 0f, top.z), 0.15f, 2f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.LookAt(L.Keyhole);
            yield return At(bot, 10.5f);    // 10.5: the door from on top of the spool
            yield return bot.LookAt(new Vector3(0.68f, 0.33f, bot.Player.Position.z - 1.2f));
            yield return bot.Click();
            yield return bot.Wait(30f);
        }

        // The spool held too low from near the edge: stopped by the chest's top with its middle past the edge.
        static IEnumerator HeldTooLow(Bot bot, Level12Keyhole level)
        {
            yield return bot.Grab(level.Key);
            yield return level.PutKeyOnShelf(bot);
            yield return bot.WalkTo(L.SpoolStand, 0.1f);
            yield return bot.Grab(level.Spool);
            yield return bot.WalkTo(new Vector3(0f, 0f, 4f), 0.1f);
            yield return bot.LookAt(new Vector3(0f, 1.75f, L.DoorZ));
            yield return At(bot, 5.5f);     // 5.5: held too low: red
            yield return bot.Click();
            yield return At(bot, 5.75f);    // 5.75: it slips off the edge
            yield return At(bot, 6.2f);     // 6.2: and falls
            yield return At(bot, 8.5f);     // 8.5: back where it stood, and why
            yield return bot.Wait(30f);
        }

        public static void Capture()
        {
            string directory = Toybox.EditorTools.ToyboxArgs.Get("-toyboxOut", "tools/out/shots/level12/tour");
            string tour = Toybox.EditorTools.ToyboxArgs.Get("-toyboxTour", "");
            // -toyboxTour door: only the spool let go against the shut door. low: only the spool held too low.
            if (tour == "door" || tour == "low")
            {
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 12, Definition = new Tour(SpoolAtTheDoor), Times = new[] { 6f, 8f, 10.5f, 11.5f }, Overview = true,
                    OutputDirectory = directory + "/door",
                });
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 12, Definition = new Tour(HeldTooLow), Times = new[] { 5.5f, 5.75f, 6.2f, 8.5f }, Overview = true,
                    OutputDirectory = directory + "/low",
                });
                return;
            }
            // -toyboxTour side: only the spool held to the side.
            if (tour == "side")
            {
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 12, Definition = new Tour(HeldToTheSide), Times = new[] { 5.5f, 6.5f, 9.5f }, Overview = true,
                    OutputDirectory = directory + "/side",
                });
                return;
            }
            Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
            {
                Level = 12, Definition = new Tour(Gauge), Times = new[] { 6f, 9f, 13f, 13.2f, 16f, 17.5f, 19f, 21.5f }, Overview = true,
                OutputDirectory = directory + "/gauge",
            });
            Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
            {
                Level = 12, Definition = new Tour(KeyInTheWay), Times = new[] { 4f, 7f }, Overview = true,
                OutputDirectory = directory + "/key-in-the-way",
            });
            Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
            {
                Level = 12, Definition = new Tour(AtTheDoor), Times = new[] { 7f, 8.5f }, Overview = false,
                OutputDirectory = directory + "/at-the-door",
            });
        }
    }
}
