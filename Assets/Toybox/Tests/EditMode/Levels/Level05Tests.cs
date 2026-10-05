using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using Toybox.UI;
using UnityEditor;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 5, The Fan and the Feather: the solver, the canyon that cannot be crossed without the raft, the
    /// scale window of the sail (LEVELS Appendix B: 6.5 to 12, intended 9.2), and what keeps it from being
    /// soft-locked.
    /// </summary>
    public class Level05Tests : SimTest
    {
        Level05FanFeather level;
        Bot bot;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(5);
            level = (Level05FanFeather)Game.Level;
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

        /// <summary>A spot on the balcony this far from the spool, on the line through the start.</summary>
        static Vector3 Beside(float distance) =>
            Level05FanFeather.SpoolBase + (Level05FanFeather.Spawn - Level05FanFeather.SpoolBase).normalized * distance;

        // The first half of the solution with two things changed: how far from the spool the feather is
        // picked up, and what the view is on when it is let go from the edge mark.
        IEnumerator PickUpAndPlace(float from, Vector3 aim)
        {
            yield return bot.WalkTo(Beside(from), 0.05f);
            yield return level.Take(bot);
            yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
            yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
            yield return bot.DropAt(aim);
        }

        IEnumerator DownAndAcross()
        {
            yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
            yield return level.Descend(bot);
            yield return level.Ride(bot);
        }

        // From the first box: up the ruler and back to the start.
        IEnumerator BackToTheStart()
        {
            yield return bot.WalkTo(new Vector3(9f, 0f, 14f), 0.3f, 15f);
            yield return bot.WalkTo(new Vector3(13.5f, 0f, 16.8f), 0.3f);
            yield return bot.WalkTo(new Vector3(13.5f, 3f, 9.4f), 0.3f);
            yield return bot.WalkTo(Level05FanFeather.Spawn, 0.1f);
        }

        bool OnTheSpool() => level.Feather.Scale == Level05FanFeather.StartScale && !level.Feather.Held &&
                             Vector3.Distance(level.Feather.Position, level.FeatherHome) < 0.05f;

        // The prop a ray from the eye to the point meets first, or null.
        Prop Seen(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude + 0.1f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? PropRef.Of(hit.collider) : null;
        }

        // What stands between the eye and a point, or null if the point is in view.
        string Blocker(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? hit.collider.name + " at " + hit.point : null;
        }

        // How far off the middle of the picture a point is, in degrees (yaw, pitch).
        Vector2 OffCentre(Vector3 point)
        {
            Vector3 to = point - Game.Player.Eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(Mathf.DeltaAngle(Game.Player.Yaw, yaw), pitch - Game.Player.Pitch);
        }

        [Test]
        public void TheLevelIsBuiltAsSpecified()
        {
            Load();
            Assert.AreEqual("fan-feather", level.Slug);
            Assert.AreEqual("The Fan and the Feather", level.Title);
            Assert.AreEqual(2, level.Phase);
            Assert.AreEqual("sunny-rug", level.Environment);
            Assert.AreEqual(-15f, level.GroundY);
            Assert.AreEqual(-13f, level.KillY);
            Assert.AreEqual("The wind only carries what it can catch.", level.Blurb);
            Assert.AreEqual(3, level.Hints.Length);

            Prop feather = level.Feather;
            Assert.AreEqual(1, Game.Props.Count, "the feather is the only prop: spool, blocks, ruler and fan are scenery");
            Assert.AreEqual(1f, feather.Scale, 1e-4f);
            Assert.AreEqual(0.5f, feather.MinScale, 1e-4f);
            Assert.AreEqual(12f, feather.MaxScale, 1e-4f);
            Assert.IsTrue(feather.HasTag(Level05FanFeather.SailTag));
            Assert.AreEqual(GrabPose.Upright, feather.GrabPose);
            Assert.IsFalse(feather.AllowPitch, "a sail lies flat");
            Assert.IsTrue(feather.Grabbable);
            Assert.IsTrue(Palette.Same(Palette.Mint.Hero, ToyInfo.Of(feather.GameObject).Candy), "Cherry: the colour that pops against the Mint room");
            Assert.AreEqual(0.0054f, ToyCatalog.Get(ToyId.Feather).Mass, 1e-4f, "LEVELS: 0.0054 s^3");
            Assert.AreEqual(Prop.MinMass, feather.Mass, 1e-5f, "at scale 1 that is under the least mass a body may have");

            // On its spool, a step from the start and under the crosshair: the first click is a close one.
            TestHelpers.RunSeconds(Game, 1f);
            Assert.Less(Vector3.Distance(feather.Position, level.FeatherHome), 0.03f, "it rests on the spool");
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.Spawn), 0.05f);
            Vector3 eye = Game.Player.Eye;
            Assert.AreSame(feather, Game.Grabber.FindTarget(), "a click at the start takes the feather");
            Assert.AreSame(feather, Seen(eye, feather.Center));
            float ratio = feather.Scale / Vector3.Distance(eye, feather.Center);
            Assert.AreEqual(0.73f, ratio, 0.01f, "LEVELS: k = 0.73");
            Assert.Greater(ratio, Level05FanFeather.FarRatio + 0.15f);

            // And from there the player sees where it goes, how to get there, and what it is all for.
            Vector3 outline = Level05FanFeather.Aim;
            foreach (Vector3 point in new[] { outline, outline + new Vector3(1.6f, 0f, 0f), outline + new Vector3(-1.6f, 0f, 0f), outline + new Vector3(0f, 0f, -3f), outline + new Vector3(0f, 0f, 4.2f) })
            {
                Assert.IsNull(Blocker(eye, point), "the painted outline is in view from the start (" + point + ")");
                Vector2 off = OffCentre(point);
                Assert.Less(Mathf.Abs(off.x), 45f, "and in the picture (" + point + " is " + off + " degrees off its middle)");
                Assert.Less(Mathf.Abs(off.y), 28f);
            }
            Assert.Greater(Mathf.Abs(OffCentre(outline).x), 15f, "beside the feather, not behind it");
            Assert.IsNull(Blocker(eye, Level05FanFeather.EdgeSpot + Vector3.up * 0.05f + Vector3.forward * 0.3f), "the mark at the edge is in view");
            Assert.IsNull(Blocker(eye, Level05FanFeather.ExitCentre), "the way out is in view");
            Assert.Less(Mathf.Abs(OffCentre(Level05FanFeather.ExitCentre).x), 45f);
            Assert.IsNull(Blocker(eye, new Vector3(-1f, 0.1f, 43f)), "and so is the far box's edge: the canyon shows");

            // The gale and the rule.
            Assert.IsTrue(level.Wind.Contains(new Vector3(-1f, 4f, 0f)));
            Assert.IsTrue(level.Wind.Contains(new Vector3(-5.9f, 8.9f, -5.9f)));
            Assert.IsTrue(level.Wind.Contains(new Vector3(3.9f, 1f, 43.9f)), "it blows across the canyon and two units onto the far box");
            Assert.IsFalse(level.Wind.Contains(new Vector3(4.2f, 1f, 5f)));
            Assert.IsFalse(level.Wind.Contains(Game.Player.Position + Vector3.up), "the balcony is out of the wind");
            Assert.Less((level.Wind.Direction - Vector3.forward).magnitude, 1e-5f);
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            Assert.IsFalse(level.Raft.Carries(6.4f), "LEVELS: it carries the player from 6.5");
            Assert.IsTrue(level.Raft.Carries(6.6f));
            Assert.IsTrue(level.Raft.Carries(12f), "up to the clamp");
            Assert.IsFalse(level.Raft.Carries(Level05FanFeather.MoorAbove));
            // capacity(s) = 0.106 s^2 - 0.0054 s^3
            Assert.AreEqual(0.106f * 9.2f * 9.2f - 0.0054f * 9.2f * 9.2f * 9.2f, level.Raft.Capacity(9.2f), 0.05f);
            // Just big enough to moor, the feather is too heavy for the wind; a hair smaller it is blown.
            float mass = ToyCatalog.Get(ToyId.Feather).Mass;
            Assert.AreEqual(0f, level.Wind.PropAcceleration(2.51f, mass * 2.51f * 2.51f * 2.51f));
            Assert.AreEqual(12f / 2.49f, level.Wind.PropAcceleration(2.49f, mass * 2.49f * 2.49f * 2.49f), 0.01f, "LEVELS: min(40, 12 / s)");

            // The machine.
            Assert.AreEqual(11f, level.Fan.transform.localScale.x, 1e-4f);
            Vector3 hub = level.Fan.transform.TransformPoint(ToyFactory.DeskFanHub);
            Assert.AreEqual(-1f, hub.x, 1e-3f);
            Assert.AreEqual(5f, hub.y, 1e-3f, "LEVELS: hub at (-1, 5, .)");
            Assert.Less(hub.z + ToyFactory.DeskFanGuardDepth * 0.5f * 11f, Level05FanFeather.BackZ, "behind the first box");
            Assert.Less((level.Fan.transform.forward - Vector3.forward).magnitude, 1e-4f, "it blows along the stream");

            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(-1f, 1.5f, 55f)), 1e-3f);

            // What is drawn of the level besides the feather and the fan: merged per material (ART_BIBLE 12:
            // level statics are at most 12 draws).
            int draws = 0;
            var materials = new List<string>();
            foreach (MeshRenderer renderer in Game.LevelRoot.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.transform.IsChildOf(feather.Transform) || renderer.transform.IsChildOf(level.Fan.transform)) continue;
                draws += renderer.sharedMaterials.Length;
                materials.Add(renderer.transform.parent.name + "/" + renderer.sharedMaterial.name);
            }
            Debug.Log("[Level05] statics: " + draws + " draws: " + string.Join(", ", materials));
            Assert.LessOrEqual(draws, 12);
        }

        [Test]
        public void TheLevelsTextsAreInTheCampaignsVoice()
        {
            Load();
            var texts = new List<string>(level.Hints)
            {
                level.Blurb, Level05FanFeather.FarLine, Level05FanFeather.StallLine, Level05FanFeather.BlownLine, Level05FanFeather.SpoolLine,
                Level05FanFeather.SpoolWayLine,
                Level05FanFeather.SmallLine, Level05FanFeather.CanyonLine, Level05FanFeather.FarBoxLine,
                Level05FanFeather.CalmLine, Level05FanFeather.BalconyLine, Level05FanFeather.FlatLine,
            };
            foreach (string text in texts)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "plain ASCII: '" + c + "' in \"" + text + "\"");
                string lower = text.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("grab") || lower.Contains("drop "), "toys are picked up and let go: \"" + text + "\"");
            }
            // A line is one glance long (the toast is two lines of the HUD's small type).
            for (int i = level.Hints.Length + 1; i < texts.Count; i++) Assert.LessOrEqual(texts[i].Length, 90, "\"" + texts[i] + "\"");
            Assert.AreEqual(texts.Count, new HashSet<string>(texts).Count, "no two lines say the same");
            Assert.AreEqual("It only ever gets as big as it looks. Pick it up from closer.", Level05FanFeather.FarLine, "the sentence of Levels 1, 2 and 4");
            // The first hint does not give the solution away, the second names the idea, the last one is the
            // recipe: where to pick it up, where to stand, what to cover, when to let go, and what then.
            foreach (string word in new[] { "balcony", "blotter", "mark", "edge", "painted", "spool", "big", "far" })
                Assert.IsFalse(level.Hints[0].ToLowerInvariant().Contains(word), "the first hint gives nothing away (\"" + word + "\")");
            StringAssert.Contains("far means big", level.Hints[1]);
            Assert.IsFalse(level.Hints[1].Contains("mark") || level.Hints[1].Contains("painted"), "the second hint is the idea, not the recipe");
            StringAssert.Contains("Pick the feather up from the mark beside its spool", level.Hints[2]);
            StringAssert.Contains("mark at the balcony edge", level.Hints[2]);
            StringAssert.Contains("cover the painted feather", level.Hints[2]);
            StringAssert.Contains("let go", level.Hints[2]);
            StringAssert.Contains("stand on the quill", level.Hints[2]);
            // What a line used to say and must not: the player who was told had done exactly that.
            Assert.IsFalse(Level05FanFeather.BalconyLine.Contains("Stand at the edge"), "said also to a player who stood on the edge mark and held it along the balcony");
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            float dropped = 0f, ratio = 0f;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                ratio = e.OldScale / e.GrabDistance;
            };
            List<string> said = Listen();
            var states = new List<string>();
            level.Raft.SailMoored += () => states.Add("moored");
            level.Raft.SailStalled += share => states.Add("stalled");
            level.Raft.SailLaunched += () => states.Add("launched");
            level.Raft.SailDocked += () => states.Add("docked");
            int gliding = 0, aboard = 0, respawns = 0;
            float lip = 0f, yaw = 0f;
            Game.Events.PlayerRespawned += e => respawns++;
            Game.Events.SailMoored += e => yaw = Mathf.DeltaAngle(0f, level.Feather.Rotation.eulerAngles.y);
            Game.Events.SailLaunched += e => lip = level.Feather.Center.y + 0.002f * level.Feather.Scale;
            // Every tick, with the level's own updates.
            Game.Context.OnUpdate(dt =>
            {
                if (level.Raft.State != SailState.Glide) return;
                gliding++;
                if (level.Raft.RiderAboard) aboard++;
            });
            int completed = 0;
            Game.Events.LevelCompleted += e => completed++;
            BotRunner.Run(Game, level.Solve(bot), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] solve: k " + ratio.ToString("0.000") + ", feather " + dropped.ToString("0.00") + ", " + (Game.LevelTicks * Sim.Dt).ToString("0.0") + " s, " +
                      gliding + " ticks of glide, yaw " + yaw.ToString("0.0") + ", outline " + lip.ToString("0.000") + " above the blotter");
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(0.73f, ratio, 0.02f, "picked up from the start, a step away (LEVELS: k = 0.73)");
            Assert.AreEqual(9.2f, dropped, 9.2f * 0.05f, "the solver's drop (LEVELS Appendix B: 9.2 +- 5 %)");
            CollectionAssert.AreEqual(new[] { "moored", "launched", "docked" }, states);
            Assert.Less(Mathf.Abs(yaw), 5f, "it lies along the wind, as the outline is painted");
            Assert.Less(lip, 0.1f, "pressed flat, its outline is no step: the solver walked onto it");
            Assert.Greater(gliding, 300, "about 37 units at 6 units a second");
            Assert.AreEqual(gliding, aboard, 2, "the rider stood on the feather for the whole glide");
            Assert.AreEqual(0, respawns);
            Assert.AreEqual(0, level.Leash.Returns);
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.AreEqual(Level05FanFeather.Landing.x, level.Feather.Center.x, 0.1f, "it came down on the far box");
            Assert.AreEqual(Level05FanFeather.Landing.z, level.Feather.Center.z, 0.1f);
            Assert.IsTrue(level.Feather.Grabbable, "and is a toy again");
            Assert.Less(Game.LevelTicks * Sim.Dt, 30f);
        }

        [Test]
        public void TheCanyonCannotBeWalkedOrJumped()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            float farthest = float.MinValue;
            void Watch()
            {
                Vector3 p = Game.Player.Position;
                if (p.y > -0.5f) farthest = Mathf.Max(farthest, p.z);
            }

            // Straight at the exit, sprinting: off the balcony, off the first box, into the canyon; again
            // and again from where the fall sends the player back to.
            IEnumerator Straight()
            {
                IEnumerator run = bot.WalkTo(Level05FanFeather.ExitCentre, 0.3f, 14f, true);
                while (run.MoveNext())
                {
                    Watch();
                    yield return run.Current;
                }
            }
            Assert.IsFalse(Drive(Straight(), 14f), "the walk never arrives");
            Assert.GreaterOrEqual(respawns, 2, "the canyon sent the player back");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthest, 22f, "running off the edge at 18 carries nobody to the far box at 42");

            // The best there is: a sprint down the middle of the stream and a jump off the very edge, the
            // wind behind (LEVELS: 5.5 and about 0.7; the gap is 24).
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded && Game.Player.Position.y > -0.1f, 4f), "back on the first box");
            respawns = 0;
            farthest = float.MinValue;
            bool jumped = false;
            IEnumerator Jumped()
            {
                yield return bot.WalkTo(new Vector3(-1f, 0f, 2f), 0.3f, 12f);
                IEnumerator run = bot.WalkTo(new Vector3(-1f, 0f, 45f), 0.3f, 6f, true);
                while (run.MoveNext())
                {
                    Watch();
                    if (!jumped && Game.Player.Grounded && Game.Player.Position.z > Level05FanFeather.CanyonNear - 0.2f)
                    {
                        jumped = true;
                        yield return bot.Jump();
                        continue;
                    }
                    yield return run.Current;
                }
            }
            Drive(Jumped(), 14f);
            Assert.IsTrue(jumped, "the bot reached the edge and jumped");
            Assert.GreaterOrEqual(respawns, 1, "the jump ended in the canyon");
            Assert.IsFalse(Game.LevelCompleted);
            Debug.Log("[Level05] the best jump came down at z = " + farthest.ToString("0.00"));
            Assert.Greater(farthest, 22.5f, "the jump did leave the edge");
            Assert.Less(farthest, 27f, "a sprint jump with the wind behind carries about 6 units; the gap is 24");
        }

        // The ends of the window (LEVELS Appendix B: works from 6.5 to the clamp at 12).
        [TestCase(1.9f, -1f, 11.6f, 6.5f, 7.1f, TestName = "TheLowEndOfTheWindow_CarriesThePlayerAcross")]
        [TestCase(1.3f, -3f, 17f, 11.99f, 12.01f, TestName = "TheHighEndOfTheWindow_TheClamp_CarriesThePlayerAcross")]
        public void TheWindow(float pickUpFrom, float aimX, float aimZ, float least, float most)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            int completed = 0, stalls = 0;
            Game.Events.LevelCompleted += e => completed++;
            level.Raft.SailStalled += share => stalls++;
            IEnumerator Script()
            {
                yield return PickUpAndPlace(pickUpFrom, new Vector3(aimX, 0.15f, aimZ));
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Script(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] picked up from " + pickUpFrom + " away, let go over (" + aimX + ", " + aimZ + "): feather " + dropped.ToString("0.00"));
            Assert.GreaterOrEqual(dropped, least);
            Assert.LessOrEqual(dropped, most);
            Assert.AreEqual(0, stalls);
            Assert.AreEqual(SailState.Docked, level.Raft.State);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void AFeatherThatIsTooSmall_Stalls_IsToldSo_AndCanBeMadeBiggerFromBesideIt()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            var shares = new List<float>();
            int launches = 0;
            level.Raft.SailStalled += share => shares.Add(share);
            level.Raft.SailLaunched += () => launches++;

            // Picked up from two and a half steps away it looks half as big; the outline makes a 5 of it.
            IEnumerator Small()
            {
                yield return PickUpAndPlace(2.6f, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Stall, 3f);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 3f);
                yield return bot.Wait(1.5f);
            }
            BotRunner.Run(Game, Small(), 60f);
            Debug.Log("[Level05] picked up from 2.6 away: feather " + dropped.ToString("0.00") + ", share " + (shares.Count > 0 ? shares[0].ToString("0.00") : "-"));
            Assert.That(dropped, Is.InRange(4f, 6f), "clearly too small, and big enough to lie still");
            Assert.AreEqual(1, shares.Count, "it tried once, and waits for the rider to step off before it tries again");
            Assert.Less(shares[0], 0.9f);
            Assert.AreEqual(0, launches);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(level.Feather.Center.z, Level05FanFeather.CanyonNear, "it went nowhere");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.FarLine, Level05FanFeather.StallLine }, said, "told at the pick-up, and told again when it could not lift");
            Assert.IsTrue(level.Feather.Grabbable);

            // Not a dead end: step off, pick it up from right beside it (now it looks big) and lay it down a
            // few steps farther along the blotter.
            IEnumerator Bigger()
            {
                yield return bot.WalkTo(new Vector3(4.5f, 0f, 12f), 0.3f);
                yield return bot.Grab(level.Feather);
                yield return bot.DropAt(new Vector3(-3f, 0.15f, 5f));
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Ride(bot);
            }
            BotRunner.Run(Game, Bigger(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] laid down again from beside it: feather " + dropped.ToString("0.00"));
            Assert.Greater(dropped, 6.6f);
            Assert.AreEqual(1, launches);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(2, said.Count, "and nothing more was said");
        }

        [Test]
        public void ACrumbInTheWind_IsBlownOffTheBox_AndComesBackToItsSpool()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f, farthest = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;

            // The first hint: carry the little feather down, let go of it in the wind and watch.
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(new Vector3(9.5f, 3f, 6f), 0.3f);
                yield return level.Descend(bot);
                yield return bot.WalkTo(new Vector3(3.5f, 0f, 10f), 0.2f);
                yield return bot.DropAt(new Vector3(1.5f, 0.05f, 10f));
                IEnumerator wait = bot.Until(() => respawned > 0, 8f);
                while (wait.MoveNext())
                {
                    if (!level.Feather.Held && level.Feather.Center.y > -1f) farthest = Mathf.Max(farthest, level.Feather.Center.z);
                    yield return wait.Current;
                }
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 60f);
            Debug.Log("[Level05] a crumb of " + dropped.ToString("0.00") + " in the wind: over the edge at z = " + farthest.ToString("0.0"));
            Assert.Less(dropped, Level05FanFeather.MoorAbove);
            Assert.Greater(farthest, Level05FanFeather.CanyonNear, "the wind slid it across the blotter and over the edge");
            Assert.AreEqual(SailState.Loose, level.Raft.State, "it was never moored");
            Assert.IsTrue(OnTheSpool(), "back on its spool at the size it started with (it is at " + level.Feather.Position + ", " + level.Feather.Scale + ")");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.BlownLine }, said);
            Assert.IsFalse(Game.LevelCompleted);

            // And from there the level is solved the ordinary way.
            IEnumerator Again()
            {
                yield return BackToTheStart();
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Load();
            // Restart with the feather in hand at the edge.
            IEnumerator Half()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.LookAt(Level05FanFeather.Aim);
            }
            BotRunner.Run(Game, Half(), 30f);
            Assert.AreSame(level.Feather, Game.Grabber.Held);
            Assert.Greater(level.Feather.Scale, 8f);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreSame(level, Game.Level, "the same level instance is built again");
            Assert.AreEqual(1f, level.Feather.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.Spawn), 0.1f);
            Assert.AreEqual(SailState.Loose, level.Raft.State);

            // Restart once more in the middle of the canyon, on the feather.
            IEnumerator MidAir()
            {
                yield return level.Take(bot);
                yield return level.Place(bot, Level05FanFeather.EdgeSpot, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Glide && level.Feather.Center.z > 28f, 12f);
            }
            BotRunner.Run(Game, MidAir(), 60f);
            Assert.IsFalse(level.Feather.Grabbable, "under way it cannot be pulled from under the rider");
            Assert.IsTrue(level.Feather.Driven);
            Game.RestartLevel();
            Game.Tick();
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            Assert.IsTrue(level.Feather.Grabbable);
            Assert.IsFalse(level.Feather.Driven);
            Assert.AreEqual(1f, level.Feather.Scale, 1e-4f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.Spawn), 0.1f);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.IsTrue(OnTheSpool());

            List<string> said = Listen();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(SailState.Docked, level.Raft.State);
            Assert.IsEmpty(said);
        }

        [Test]
        public void AFeatherThatLeavesTheWorld_ComesBack()
        {
            Load();
            List<string> said = Listen();
            Prop feather = level.Feather;
            TestHelpers.RunSeconds(Game, 0.5f);
            Vector3 home = feather.Position;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;

            // Let go over the canyon: it falls past the kill plane, and is back on its spool.
            IEnumerator IntoTheCanyon()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(new Vector3(11f, 3f, 9.4f), 0.2f);
                yield return bot.DropAt(new Vector3(11.5f, -4f, 34f));
                yield return bot.Until(() => respawned == 1, 8f);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, IntoTheCanyon(), 40f);
            Assert.Less(Vector3.Distance(feather.Position, home), 0.03f, "back on the spool");
            Assert.AreEqual(1f, feather.Scale, 1e-4f, "at the size it started with");
            Assert.AreEqual(0, level.Leash.Returns, "the kill plane brought it back");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CanyonLine }, said, "and the level says where it went");

            // Lying on the far box while the player is not there (it can be thrown there with a far-off
            // pick-up): out of reach, so it comes back too. (A fixture, not a bot move.)
            feather.SetScale(4f);
            feather.SetPose(new Vector3(-1f, 0.3f, 50f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 6f), "left on the far box at " + feather.Position);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CanyonLine, Level05FanFeather.FarBoxLine }, said);

            // The player who falls in is sent back, and can carry on.
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Game.Player.Teleport(new Vector3(-1f, 1f, 30f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 4f));
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.Spawn), 0.2f, "to the start: they had not left the balcony");
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void SteppingOffInMidFlight_LosesNothing()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0, respawned = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Game.Events.PropRespawned += e => respawned++;

            IEnumerator Fall()
            {
                yield return level.Take(bot);
                yield return level.Place(bot, Level05FanFeather.EdgeSpot, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Glide && level.Feather.Center.z > 24f, 12f);
                // Over the side.
                IEnumerator walk = bot.WalkTo(bot.Player.Position + new Vector3(8f, 0f, 0f), 0.3f, 4f);
                while (respawns == 0 && walk.MoveNext()) yield return walk.Current;
                yield return bot.Until(() => respawns == 1 && respawned == 1, 10f);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Fall(), 60f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.DeckRespawn), 0.5f, "sent back to the first box, where they came from");
            Assert.AreEqual(SailState.Loose, level.Raft.State, "the riderless feather was let go");
            Assert.IsTrue(OnTheSpool(), "and is back on its spool (it is at " + level.Feather.Position + ")");
            Assert.IsTrue(level.Feather.Grabbable);
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CanyonLine }, said);
            Assert.IsFalse(Game.LevelCompleted);

            IEnumerator Again()
            {
                yield return BackToTheStart();
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // The one way a try goes quietly wrong: the pick-up. Said at the pick-up, in the words of Levels 1, 2 and 4.
        [TestCase(1.3f, false, TestName = "PickedUpFromTheStart_NothingIsSaid")]
        [TestCase(1.8f, false, TestName = "PickedUpFromHalfAStepFartherBack_NothingIsSaid")]
        [TestCase(2.2f, true, TestName = "PickedUpFromTwoStepsAway_ItIsSaidAtOnce")]
        [TestCase(4f, true, TestName = "PickedUpFromAcrossTheBalcony_ItIsSaidAtOnce")]
        public void ThePickUp(float from, bool told)
        {
            Load();
            List<string> said = Listen();
            IEnumerator Script()
            {
                yield return bot.WalkTo(Beside(from), 0.05f);
                yield return level.Take(bot);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Script(), 30f);
            float ratio = Game.Grabber.Ratio;
            if (told)
            {
                Assert.Less(ratio, Level05FanFeather.FarRatio);
                CollectionAssert.AreEqual(new[] { Level05FanFeather.FarLine }, said);
            }
            else
            {
                Assert.Greater(ratio, Level05FanFeather.FarRatio);
                Assert.IsEmpty(said);
            }

            // What the line promises: held over the outline from the edge mark, a feather that was not told
            // off flies, and one that was does not.
            IEnumerator Place()
            {
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.DropAt(Level05FanFeather.Aim);
            }
            BotRunner.Run(Game, Place(), 30f);
            Debug.Log("[Level05] picked up from " + from + " away: k " + ratio.ToString("0.000") + ", on the outline " + level.Feather.Scale.ToString("0.00"));
            Assert.AreEqual(!told, level.Raft.Carries(level.Feather.Scale));
        }

        [Test]
        public void LetGoOnTheBalcony_TheLittleFeatherGoesBackToItsSpool()
        {
            Load();
            List<string> said = Listen();
            // Put down at the feet, a few steps from the spool: on a floor it could only be picked up from above.
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(new Vector3(11.5f, 3f, 5f), 0.3f);
                yield return bot.DropAt(new Vector3(12f, 3.02f, 6.5f));
                yield return bot.Wait(0.3f);
            }
            BotRunner.Run(Game, Script(), 30f);
            Assert.Less(level.Feather.Scale, Level05FanFeather.MoorAbove);
            Assert.Greater(Vector3.Distance(level.Feather.Position, level.FeatherHome), 2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 4f), "left on the floor at " + level.Feather.Position);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level05FanFeather.SmallLine }, said, "told why it went, and what to do instead");
            TestHelpers.RunSeconds(Game, 3f);
            Assert.AreEqual(1, level.Leash.Returns, "on its spool it is left alone");
            Assert.AreEqual(1, said.Count);
        }

        // Let go from well back on the balcony, the balcony catches it: it lies up there, a few units long.
        [Test]
        public void LetGoFromTooFarBack_ItCatchesOnTheBalcony_AndIsToldSo()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(new Vector3(9.5f, 3f, 6.5f), 0.3f);
                yield return bot.WalkTo(new Vector3(13.6f, 3f, 7.8f), 0.2f);
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return bot.Wait(3f);
            }
            BotRunner.Run(Game, Script(), 30f);
            Debug.Log("[Level05] let go from the back of the balcony: feather " + dropped.ToString("0.00") + " at " + level.Feather.Center);
            Assert.GreaterOrEqual(dropped, Level05FanFeather.MoorAbove);
            Assert.Less(dropped, Level05FanFeather.LeastScale);
            Assert.Greater(level.Feather.Center.y, Level05FanFeather.BalconyTop - 0.1f, "it lies on the balcony");
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            CollectionAssert.AreEqual(new[] { Level05FanFeather.BalconyLine }, said);
            Assert.AreEqual(0, level.Leash.Returns, "big enough to be picked up from where it lies");

            // Picked up again from beside it and let go from the edge mark, it flies.
            IEnumerator Again()
            {
                yield return bot.Grab(level.Feather);
                yield return bot.WalkTo(new Vector3(9.5f, 3f, 6.5f), 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // Big enough, but beside the stream: on the apron.
        [Test]
        public void LaidOutOfTheWind_ItIsToldSo_AndCarriedOntoTheBlotterItFlies()
        {
            Load();
            List<string> said = Listen();
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(new Vector3(9f, 3f, 9.4f), 0.2f);
                yield return bot.DropAt(new Vector3(9f, 0.1f, 16f));
                yield return bot.Wait(3f);
            }
            BotRunner.Run(Game, Script(), 30f);
            Debug.Log("[Level05] laid on the apron: feather " + level.Feather.Scale.ToString("0.00") + " at " + level.Feather.Center);
            Assert.GreaterOrEqual(level.Feather.Scale, Level05FanFeather.MoorAbove);
            Assert.Greater(level.Feather.Center.x, Level05FanFeather.StreamMaxX + Level05FanFeather.LaunchMargin);
            Assert.AreEqual(SailState.Loose, level.Raft.State, "out of the wind it is an ordinary toy");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CalmLine }, said);

            IEnumerator Again()
            {
                yield return bot.Grab(level.Feather);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f, 12f);
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Pressed flat by the gale, the moored feather is walked onto from wherever the player comes.
        [TestCase(-5f, 12f, TestName = "TheMooredFeather_IsWalkedOnto_FromTheFarSide")]
        [TestCase(-1f, 3f, TestName = "TheMooredFeather_IsWalkedOnto_FromTheFansEnd")]
        [TestCase(-1f, 17.3f, TestName = "TheMooredFeather_IsWalkedOnto_FromTheCanyonsEnd")]
        public void TheMooredFeather_IsWalkedOnto(float fromX, float fromZ)
        {
            Load();
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return level.Place(bot, Level05FanFeather.EdgeSpot, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                // Round it, not over it.
                float roundZ = fromZ < 10f ? 3f : 17.3f;
                yield return bot.WalkTo(new Vector3(3f, 0f, roundZ), 0.3f);
                yield return bot.WalkTo(new Vector3(fromX, 0f, roundZ), 0.3f);
                yield return bot.WalkTo(new Vector3(fromX, 0f, fromZ), 0.3f);
                Assert.IsFalse(level.Raft.RiderAboard, "not on it yet");
                yield return level.Ride(bot);
            }
            BotRunner.Run(Game, Script(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Robustness: the same run every time, and again after a restart.
        [Test]
        public void TheSolve_IsTheSameFiveTimesOver_AndAfterARestart()
        {
            float firstScale = 0f;
            int firstTicks = 0;
            for (int run = 0; run < 5; run++)
            {
                Game?.Dispose();
                Load();
                float dropped = 0f;
                Game.Events.PropDropped += e => dropped = e.NewScale;
                TestHelpers.PlayLevel(Game, 90f);
                if (run == 0)
                {
                    firstScale = dropped;
                    firstTicks = Game.LevelTicks;
                }
                Assert.AreEqual(firstScale, dropped, 0f, "run " + run + ": the feather is let go at the same size every time");
                Assert.AreEqual(firstTicks, Game.LevelTicks, "run " + run + ": and it takes the same number of ticks");
            }
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(Game.LevelCompleted);
            float again = 0f;
            Game.Events.PropDropped += e => again = e.NewScale;
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(firstScale, again, 0.02f, "after a restart");
        }

        // Robustness: where a player would stand and aim, give or take.
        [TestCase(7.7f, 3.5f, 0f, 0f)]
        [TestCase(7.7f, 1.5f, 0f, 0f)]
        [TestCase(7.7f, 6.5f, 0f, 0f)]
        [TestCase(8.4f, 4f, 0f, 0f)]
        [TestCase(9f, 6.5f, 0f, 0f)]
        [TestCase(7.7f, 3.5f, 1.5f, 1f)]
        [TestCase(7.7f, 3.5f, -2f, -3f)]
        [TestCase(7.7f, 3.5f, -3.5f, 3f)]
        [TestCase(7.7f, 3.5f, 0.5f, 5f)]
        [TestCase(8f, 8.5f, -1f, 2f)]
        // The ends of the blotter: at the canyon's edge (the clamp stops it with its middle a third of a unit
        // from the edge) and right in front of the fan.
        [TestCase(7.7f, 3.5f, 1.5f, 6.2f)]
        [TestCase(7.7f, 3.5f, 0f, -17.1f)]
        public void TheSolve_ToleratesWhereThePlayerStandsAndAims(float standX, float standZ, float aimDx, float aimDz)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Vector3 aim = Level05FanFeather.Aim + new Vector3(aimDx, 0f, aimDz);
            Vector3 moored = default;
            level.Raft.SailMoored += () => moored = level.Feather.Center;
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return level.Place(bot, new Vector3(standX, 3f, standZ), aim);
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Script(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] from (" + standX + ", " + standZ + ") at the outline + (" + aimDx + ", " + aimDz + "): feather " + dropped.ToString("0.00") + ", moored at " + moored.ToString("0.00"));
            Assert.IsTrue(Game.LevelCompleted);
            Assert.GreaterOrEqual(dropped, Level05FanFeather.LeastScale);
            Assert.IsEmpty(said);
        }

        // Robustness: a hand does not stand on the mark's middle or hold the feather on the outline's. Two
        // dozen tries scattered round the edge mark (up to a step back and a step either way along the edge),
        // round the outline (two units either way across, two and a half along) and round the start mark
        // (picked up from 1.2 to 1.7 away), drawn from a fixed seed.
        [Test]
        public void TheSolve_ToleratesAnUnsteadyHand()
        {
            var random = new System.Random(20261004);
            float least = float.MaxValue, most = 0f;
            for (int i = 0; i < 24; i++)
            {
                Vector3 stand, aim;
                do
                {
                    stand = Level05FanFeather.EdgeSpot + new Vector3((float)random.NextDouble() * 1.1f - 0.1f, 0f, (float)random.NextDouble() * 2.4f - 1.2f);
                }
                // Not in the spool.
                while ((stand - Level05FanFeather.SpoolBase).magnitude < Level05FanFeather.SpoolRadius + 0.6f);
                aim = Level05FanFeather.Aim + new Vector3((float)random.NextDouble() * 4f - 2f, 0f, (float)random.NextDouble() * 5f - 2.5f);
                float from = 1.2f + (float)random.NextDouble() * 0.5f;

                Game?.Dispose();
                Load();
                List<string> said = Listen();
                float dropped = 0f;
                Game.Events.PropDropped += e => dropped = e.NewScale;
                IEnumerator Script()
                {
                    yield return bot.WalkTo(Beside(from), 0.05f);
                    yield return level.Take(bot);
                    yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                    yield return bot.WalkTo(stand, 0.15f);
                    yield return bot.DropAt(aim);
                    yield return DownAndAcross();
                }
                string what = "try " + i + ": picked up from " + from.ToString("0.00") + " away, let go from " + stand + " over " + aim;
                Assert.IsTrue(Drive(Script(), 90f), what + ": the bot's script stopped (bot at " + Game.Player.Position + ", feather " + level.Feather.Scale + " at " + level.Feather.Center + ")");
                TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                Assert.IsTrue(Game.LevelCompleted, what + ": feather " + dropped);
                Assert.GreaterOrEqual(dropped, Level05FanFeather.LeastScale, what);
                Assert.IsEmpty(said, what);
                least = Mathf.Min(least, dropped);
                most = Mathf.Max(most, dropped);
            }
            // (The smallest of them, 6.8, is the try that adds up all three: picked up from 1.7 away, from the
            // corner of the range nearest the blotter, over the outline's near end.)
            Debug.Log("[Level05] an unsteady hand: 24 of 24, feathers from " + least.ToString("0.00") + " to " + most.ToString("0.00"));
            Assert.Less(most, Level05FanFeather.MaxScale + 0.01f);
        }

        // ---- The review's attacks (tools/out/notes/level05-review.md) --------------------------------------

        // The solver's first half: the feather on the outline, the bot down at the blotter's edge.
        IEnumerator LayAndDescend()
        {
            yield return level.Take(bot);
            yield return level.Place(bot, Level05FanFeather.EdgeSpot, Level05FanFeather.Aim);
            yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
            yield return level.Descend(bot);
        }

        IEnumerator ToTheExit()
        {
            yield return bot.WalkTo(new Vector3(Level05FanFeather.ExitCentre.x, 0f, Level05FanFeather.ExitCentre.z), 0.5f, 10f);
            yield return bot.Until(() => Game.LevelCompleted, 3f);
        }

        // A jump pressed just before landing goes off on the tick the feet touch down. Hop after hop the raft
        // never saw its rider standing; after a second it let the feather go under them and both went down
        // the canyon (the SailRaft's rule now: a rider in the air over the sail has not left it). And the
        // gale pushed a rider in the air forward along the feather, farther with every hop - four in a row
        // and they were off its tip (the level's rule now: the feather travels with the wind, its rider is
        // in still air).
        [Test]
        public void ARiderWhoHopsOnTheWayAcross_IsNotDropped_AndComesDownWhereTheyTookOff()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0, hops = 0, airborne = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Vector3 tookOff = default, cameDown = default;
            IEnumerator Script()
            {
                yield return LayAndDescend();
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Glide && level.Feather.Center.z > 20f, 8f);
                tookOff = bot.Player.Position - level.Feather.Center;
                // Four hops in a row over the canyon: 2.8 s in the air, with one tick of standing between them.
                while (hops < 4 && respawns == 0)
                {
                    if (bot.Player.Grounded)
                    {
                        hops++;
                        yield return bot.Jump(false);
                    }
                    else
                    {
                        airborne++;
                        yield return null;
                    }
                }
                IEnumerator land = bot.Until(() => bot.Player.Grounded || respawns > 0, 3f);
                while (land.MoveNext())
                {
                    airborne++;
                    yield return land.Current;
                }
                Assert.AreEqual(SailState.Glide, level.Raft.State, "the feather flies on under a rider who hops");
                Assert.IsTrue(level.Raft.RiderAboard, "and is there when they come down");
                cameDown = bot.Player.Position - level.Feather.Center;
                yield return bot.Until(() => level.Raft.State == SailState.Docked || respawns > 0, 20f);
                yield return ToTheExit();
            }
            BotRunner.Run(Game, Script(), 90f);
            Debug.Log("[Level05] four hops in a row: took off at " + tookOff.ToString("0.00") + " from the feather's middle, came down at " + cameDown.ToString("0.00"));
            Assert.AreEqual(4, hops);
            Assert.Greater(airborne * Sim.Dt, 2.4f, "far longer in the air than the raft waits for a rider");
            Assert.Less(Mathf.Abs(cameDown.z - tookOff.z), 0.4f, "the wind did not carry the rider along the feather");
            Assert.Less(Mathf.Abs(cameDown.x - tookOff.x), 0.4f);
            Assert.AreEqual(0, respawns, "nobody fell");
            Assert.AreEqual(0, level.Leash.Returns);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // The first thing many will try: hold the feather out toward the far box. It stops growing at its
        // biggest half way there, falls into the canyon and is back on its spool - and the level says where
        // it went (it used to say only that it was back).
        [TestCase(-1f, 1.5f, 55f, TestName = "HeldOutTowardTheWayOut_ItFallsIntoTheCanyon_AndIsToldSo")]
        [TestCase(-1f, 0.2f, 44f, TestName = "HeldOutTowardTheFarBoxsEdge_ItFallsIntoTheCanyon_AndIsToldSo")]
        public void HeldOutTowardTheFarBox(float x, float y, float z)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.DropAt(new Vector3(x, y, z));
                yield return bot.Until(OnTheSpool, 10f);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 40f);
            Assert.AreEqual(Level05FanFeather.MaxScale, dropped, 0.01f, "it stopped growing at its biggest, over the canyon");
            Assert.AreEqual(SailState.Loose, level.Raft.State, "it never lay on the far box");
            Assert.AreEqual(0, level.Leash.Returns, "the kill plane brought it back");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CanyonLine }, said);
            Assert.IsFalse(Game.LevelCompleted);

            IEnumerator Again()
            {
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.Spawn, 0.1f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // From the edge mark itself, held along the balcony (at the apron beyond its front edge), the balcony
        // catches it. The line used to say "Stand at the edge before you let go" - to a player on the edge mark.
        [Test]
        public void HeldAlongTheBalconyFromTheEdgeMark_ItLandsOnTheBalcony_AndTheLineSaysWhereToLetGo()
        {
            Load();
            List<string> said = Listen();
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.DropAt(new Vector3(10f, 0.1f, 14f));
                yield return bot.Wait(3f);
            }
            BotRunner.Run(Game, Script(), 40f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.EdgeSpot), 0.2f, "the player stands on the edge mark");
            Assert.GreaterOrEqual(level.Feather.Scale, Level05FanFeather.MoorAbove);
            Assert.Greater(level.Feather.Center.y, Level05FanFeather.BalconyTop - 0.1f, "it lies on the balcony (" + level.Feather.Center + ")");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.BalconyLine }, said);
            StringAssert.Contains("out over the blotter", Level05FanFeather.BalconyLine);

            // Doing what the line says, from where the player stands.
            IEnumerator Again()
            {
                yield return bot.Grab(level.Feather);
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // From the start mark the balcony's own edge is 2.4 away, and a view more than three degrees steeper
        // than the one to the outline's middle meets it: the feather stops there, small, and drops to the
        // deck under the edge (that is what the edge mark is for). It lies where the wind is not, out of
        // sight from the middle of the balcony; the line says where it has to go, and from beside it on
        // the deck it is big enough for that.
        [Test]
        public void StraightFromTheStart_HeldWellShortOfTheOutline_TheBalconysEdgeCatchesIt_AndItIsFetchedFromTheDeck()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            // On the outline's bearing, three units short of its middle.
            Vector3 aim = Level05FanFeather.Aim + new Vector3(2.3f, 0f, -2.07f);
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.DropAt(aim);
                yield return bot.Wait(4f);
            }
            BotRunner.Run(Game, Script(), 30f);
            Debug.Log("[Level05] straight from the start, three units short of the outline: feather " + dropped.ToString("0.00") + " at " + level.Feather.Center.ToString("0.00") + ", said: " + string.Join(" / ", said));
            Assert.That(dropped, Is.InRange(Level05FanFeather.MoorAbove, Level05FanFeather.LeastScale), "caught by the balcony's edge, a few units out");
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            Assert.AreEqual(0, level.Leash.Returns, "big enough to be picked up where it lies");
            Assert.Less(level.Feather.Center.y, 0.5f, "it went over the edge and lies on the deck (" + level.Feather.Center + ")");
            Assert.Greater(level.Feather.Center.x, Level05FanFeather.StreamMaxX + Level05FanFeather.LaunchMargin, "beside the blotter");
            CollectionAssert.AreEqual(new[] { Level05FanFeather.CalmLine }, said, "told where it has to go");

            // Down the ruler, to the feather, and from beside it across the blotter.
            IEnumerator Fetch()
            {
                yield return level.Descend(bot);
                yield return bot.WalkTo(new Vector3(5.8f, 0f, 7.5f), 0.3f);
                yield return bot.Grab(level.Feather);
                yield return bot.DropAt(new Vector3(-3f, 0.15f, 13f));
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Ride(bot);
            }
            BotRunner.Run(Game, Fetch(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] fetched from the deck and laid across the blotter: feather " + dropped.ToString("0.00"));
            Assert.Greater(dropped, Level05FanFeather.LeastScale + 0.5f);
            Assert.IsTrue(Game.LevelCompleted, "feather " + dropped + " at " + level.Feather.Center);
            Assert.AreEqual(1, said.Count);
        }

        // Let go from behind the spool, the spool catches the feather: it lies on it again, at another size
        // (0.7 to 1.2). At 0.7 it looks too small from the start mark ("Pick it up from closer" - to a player
        // on the mark). The spool puts it back as it was, and the level says what was in the way.
        [Test]
        public void LetGoOntoItsOwnSpoolAtAnotherSize_ItIsPutBackAsItWas()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(new Vector3(10f, 3f, 3.5f), 0.1f);
                yield return bot.DropAt(new Vector3(1f, 0.15f, 3f));
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 30f);
            Debug.Log("[Level05] let go from behind the spool: feather " + dropped.ToString("0.000") + " at " + level.Feather.Center.ToString("0.00"));
            Assert.Less(dropped, 1.5f, "the spool stopped it close by");
            Assert.Greater(Mathf.Abs(dropped - Level05FanFeather.StartScale), 0.02f, "at another size than it started with");
            Assert.Less(Vector3.Distance(level.Feather.Center, level.FeatherHome), Level05FanFeather.SpoolRadius + 0.1f, "it lies on the spool (" + level.Feather.Center + ")");
            Assert.AreEqual(level.FeatherHome.y, level.Feather.Center.y, 0.05f, "on its top");
            Assert.IsTrue(TestHelpers.RunUntil(Game, OnTheSpool, 4f), "and is put back as it was (" + level.Feather.Scale + " at " + level.Feather.Position + ")");
            Assert.AreEqual(1, level.Leash.Returns);
            CollectionAssert.AreEqual(new[] { Level05FanFeather.SpoolWayLine }, said, "it is where it was: the level says why");
            TestHelpers.RunSeconds(Game, 3f);
            Assert.AreEqual(1, level.Leash.Returns, "once");
            Assert.AreEqual(1, said.Count, "and said once");
            said.Clear();

            IEnumerator Again()
            {
                yield return bot.WalkTo(Level05FanFeather.Spawn, 0.1f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said, "picked up from the start mark again, it is not told off");
        }

        // The feather as a diving board. (Fixtures: the feather laid where a bot would take a minute to get
        // it.) Twelve long and loose beside the stream, its tip six out over the canyon: it tips under whoever
        // walks out. Moored at the edge and too small to fly: it holds, and a sprint and a jump off its tip
        // in the wind is the farthest anybody gets without flying - 28 of the 42 it takes.
        [TestCase(12f, 10f, 17f, 26f, TestName = "RunningOutAlongALooseFeatherOverTheEdge_AndJumping_FallsShort")]
        [TestCase(6.4f, -1f, 17.7f, 31f, TestName = "RunningOutAlongAMooredFeatherOverTheEdge_AndJumping_FallsShort")]
        public void TheFeatherAsADivingBoard(float scale, float x, float z, float atMost)
        {
            Load();
            level.Feather.SetScale(scale);
            level.Feather.SetPose(new Vector3(x, 0.25f, z), Quaternion.identity);
            Game.Player.Teleport(new Vector3(x, 0f, 6f), 0f);
            TestHelpers.RunSeconds(Game, 1.5f);
            Assert.Greater(level.Feather.Center.z + 0.49f * scale, Level05FanFeather.CanyonNear + 2.5f, "its tip is out over the canyon");
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            float farthest = 0f;
            bool jumped = false;
            IEnumerator Script()
            {
                IEnumerator run = bot.WalkTo(new Vector3(x, 0f, 60f), 0.3f, 8f, true);
                while (respawns == 0 && run.MoveNext())
                {
                    Player player = bot.Player;
                    if (player.Position.y > -0.6f) farthest = Mathf.Max(farthest, player.Position.z);
                    // Off the tip, or off whatever is left underfoot once the feather has tipped.
                    bool atTheEnd = player.GroundProp == level.Feather
                        ? player.Position.z > level.Feather.Center.z + 0.46f * scale
                        : player.Position.z > Level05FanFeather.CanyonNear - 0.2f;
                    if (!jumped && player.Grounded && atTheEnd)
                    {
                        jumped = true;
                        yield return bot.Jump();
                        continue;
                    }
                    yield return run.Current;
                }
            }
            Drive(Script(), 9f);
            Debug.Log("[Level05] diving board " + scale + ": the farthest anybody got above the boxes' tops was z = " + farthest.ToString("0.00"));
            Assert.IsTrue(jumped);
            Assert.AreEqual(1, respawns, "it ended in the canyon");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(farthest, Level05FanFeather.CanyonNear + 2f, "they did get out over the canyon");
            Assert.Less(farthest, atMost, "and nowhere near the far box at " + Level05FanFeather.CanyonFar);
        }

        // Dying with the feather in hand loses nothing: the fall sends the player back to the first box,
        // still holding it, and from there the blotter is far enough away.
        [Test]
        public void IntoTheCanyonWithTheFeatherInHand_ItIsKept_AndLaidFromTheFirstBoxItFlies()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            IEnumerator Fall()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(new Vector3(9f, 3f, 9.5f), 0.3f);
                IEnumerator run = bot.WalkTo(new Vector3(9f, 0f, 40f), 0.3f, 6f, true);
                while (respawns == 0 && run.MoveNext()) yield return run.Current;
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Fall(), 40f);
            Assert.AreEqual(1, respawns);
            Assert.AreSame(level.Feather, Game.Grabber.Held, "still in hand");
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.DeckRespawn), 0.5f, "on the first box: they had come down from the balcony on the way");
            Assert.IsEmpty(said);

            IEnumerator Lay()
            {
                yield return bot.DropAt(new Vector3(-2f, 0.15f, 8f));
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Ride(bot);
            }
            BotRunner.Run(Game, Lay(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] laid from the first box's respawn: feather " + dropped.ToString("0.00"));
            Assert.Greater(dropped, Level05FanFeather.LeastScale + 0.5f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        [Test]
        public void RestartingInAStall_StartsOver()
        {
            Load();
            IEnumerator Stall()
            {
                yield return PickUpAndPlace(2.6f, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Stall, 3f);
                yield return bot.Wait(0.3f);
            }
            BotRunner.Run(Game, Stall(), 60f);
            Assert.AreEqual(SailState.Stall, level.Raft.State);
            Assert.IsTrue(level.Feather.Driven);
            Assert.Greater(level.Feather.Center.y, 0.1f, "in mid-lift");
            Game.RestartLevel();
            Game.Tick();
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            Assert.IsFalse(level.Feather.Driven);
            Assert.IsTrue(level.Feather.Grabbable);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.IsTrue(OnTheSpool());
            List<string> said = Listen();
            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsEmpty(said, "the stall's line does not come back with the restart");
        }

        // Across, nothing can be lost any more: the feather thrown back into the canyon, the player after it.
        [Test]
        public void OnceAcross_TheFeatherThrownAway_AndAFallAfterIt_LeadBackToTheFarBox()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Script()
            {
                yield return LayAndDescend();
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Docked, 20f);
                yield return bot.WalkTo(new Vector3(5f, 0f, 46f), 0.3f, 6f);
                yield return bot.Grab(level.Feather);
                yield return bot.DropAt(new Vector3(-1f, -6f, 30f));
                yield return bot.Wait(4f);
                IEnumerator run = bot.WalkTo(new Vector3(0f, 0f, 30f), 0.3f, 6f, true);
                while (respawns == 0 && run.MoveNext()) yield return run.Current;
                yield return bot.Wait(1f);
            }
            BotRunner.Run(Game, Script(), 90f);
            Assert.AreEqual(1, respawns);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.FarRespawn), 0.5f, "sent back to the far box, not across the canyon");
            Assert.AreEqual(0, level.Leash.Returns, "wherever the feather lies now, it is left alone");
            Assert.IsFalse(Game.LevelCompleted);
            BotRunner.Run(Game, ToTheExit(), 20f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // What an eager player does first: a click on the feather, a turn to the painted outline, a click.
        // No step to the edge mark. On the way out the held feather passes over its own spool; as the level
        // was built it grazed the spool's rim by two hundredths, stopped there at 1.1, was sent back and the
        // player was told "Too small ... let go of it farther off" - holding it at the far end of the box.
        // The spool is a fifth lower now and the feather goes over it.
        [TestCase(0f, 0f, TestName = "StraightFromTheStart_HeldOverTheOutline_ItFlies")]
        [TestCase(-2.5f, -3f, TestName = "StraightFromTheStart_HeldOverTheOutlinesNearEnd_ItFlies")]
        [TestCase(-3f, 3f, TestName = "StraightFromTheStart_HeldOverTheBlottersFarSide_ItFlies")]
        [TestCase(0.5f, 4.5f, TestName = "StraightFromTheStart_HeldOverTheOutlinesFarEnd_ItFlies")]
        // Not by a hair: on the outline's own bearing a unit and a half nearer, the view is two degrees steeper
        // and passes spool and balcony edge that much lower. It still goes over both.
        [TestCase(1.1f, -1f, TestName = "StraightFromTheStart_HeldShortOfTheOutlinesMiddle_ItFlies")]
        public void StraightFromTheStart(float aimDx, float aimDz)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Vector3 stoodAt = default;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                stoodAt = Game.Player.Position;
            };
            Vector3 aim = Level05FanFeather.Aim + new Vector3(aimDx, 0f, aimDz);
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.LookAt(aim);
                yield return bot.Wait(0.2f);
                Assert.Greater(level.Feather.Scale, Level05FanFeather.LeastScale + 1f, "held over the blotter it is out there already (" + level.Feather.Center + "), not on the spool");
                yield return bot.DropAt(aim);
                yield return DownAndAcross();
            }
            BotRunner.Run(Game, Script(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level05] straight from the start over the outline + (" + aimDx + ", " + aimDz + "): feather " + dropped.ToString("0.00"));
            Assert.Less(Vector3.Distance(stoodAt, Level05FanFeather.Spawn), 0.05f, "let go from the start mark");
            Assert.Greater(dropped, Level05FanFeather.LeastScale + 1f, "big enough, and not by a hair");
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // Nothing moves by itself: two minutes of looking at the view.
        [Test]
        public void LeftAlone_NothingHappens()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            TestHelpers.RunSeconds(Game, 120f);
            Assert.IsTrue(OnTheSpool(), "the feather lies on its spool (" + level.Feather.Position + ")");
            Assert.AreEqual(0, respawned);
            Assert.AreEqual(0, level.Leash.Returns);
            Assert.AreEqual(SailState.Loose, level.Raft.State);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level05FanFeather.Spawn), 0.05f);
            Assert.IsEmpty(said);
            Assert.AreSame(level.Feather, Game.Grabber.FindTarget(), "and a click still takes it");
        }

        // Walks round the moored feather to beyond one of its ends and onto it there. along: +1 its tip, -1 its quill.
        IEnumerator BoardAtAnEnd(float along)
        {
            Prop feather = level.Feather;
            Vector3 axis = feather.Rotation * Vector3.forward * along;
            float clear = 0.5f * feather.Scale + 1.2f;
            Vector3 from = feather.Center + axis * clear, onto = feather.Center + axis * (0.41f * feather.Scale);
            from.x = Mathf.Clamp(from.x, -8.5f, 6.5f);
            from.z = Mathf.Clamp(from.z, -5.5f, 17.6f);
            from.y = onto.y = 0f;
            // Never across it: east of it, then level with the end, then over to the end.
            Vector3 east = new Vector3(Mathf.Min(6.5f, feather.Center.x + clear), 0f, bot.Player.Position.z);
            yield return bot.WalkTo(east, 0.3f, 12f);
            yield return bot.WalkTo(new Vector3(east.x, 0f, from.z > feather.Center.z ? Mathf.Min(17.6f, feather.Center.z + clear) : Mathf.Max(-5.5f, feather.Center.z - clear)), 0.3f, 12f);
            if (from.x < feather.Center.x - 1f) yield return bot.WalkTo(new Vector3(from.x, 0f, bot.Player.Position.z), 0.3f, 12f);
            yield return bot.WalkTo(from, 0.3f, 12f);
            Assert.IsFalse(level.Raft.RiderAboard, "not on it yet");
            // On, toward the end's own spot; stand still as soon as it is under way.
            IEnumerator walk = bot.WalkTo(onto, 0.25f, 12f);
            while (level.Raft.State == SailState.Moored && walk.MoveNext()) yield return walk.Current;
        }

        // A feather that lies askew is turned into the wind on its way (a quarter turn in a second and a
        // half); whoever stands near one of its ends is swung round with it, six units a second sideways.
        // They stay on.
        [TestCase(6, 1f, TestName = "AFeatherLaidAcrossTheWind_BoardedAtItsTip_CarriesItsRiderRoundAndAcross")]
        [TestCase(6, -1f, TestName = "AFeatherLaidAcrossTheWind_BoardedAtItsQuill_CarriesItsRiderRoundAndAcross")]
        [TestCase(-3, 1f, TestName = "AFeatherLaidAskew_BoardedAtItsTip_CarriesItsRiderRoundAndAcross")]
        [TestCase(12, -1f, TestName = "AFeatherLaidBackToFront_BoardedAtItsQuill_CarriesItsRiderAcross")]
        public void ATurnedFeather(int quarterHours, float end)
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            float laid = 0f, swung = 0f;
            Vector3 boarded = default;
            IEnumerator Script()
            {
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.LookAt(Level05FanFeather.Aim);
                yield return bot.RotateHeld(quarterHours);
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                laid = Mathf.DeltaAngle(0f, level.Feather.Rotation.eulerAngles.y);
                yield return level.Descend(bot);
                yield return BoardAtAnEnd(end);
                yield return bot.Until(() => level.Raft.State == SailState.Glide, 3f);
                boarded = level.Feather.Transform.InverseTransformPoint(bot.Player.Position);
                IEnumerator ride = bot.Until(() => level.Raft.State != SailState.Glide || respawns > 0, 20f);
                while (ride.MoveNext())
                {
                    swung = Mathf.Max(swung, Mathf.Abs(bot.Player.Velocity.x));
                    yield return ride.Current;
                }
                yield return ToTheExit();
            }
            BotRunner.Run(Game, Script(), 90f);
            Debug.Log("[Level05] laid at yaw " + laid.ToString("0") + ", boarded at " + boarded.ToString("0.00") + " of its length: swung round at up to " + swung.ToString("0.0") + " sideways");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(quarterHours * 15f, laid)), 4f, "it lies as it was turned in the hand (yaw " + laid + ")");
            Assert.Greater(Mathf.Abs(boarded.z), 0.3f, "the rider stood near its end");
            Assert.AreEqual(0, respawns, "and was not thrown off");
            Assert.AreEqual(SailState.Docked, level.Raft.State);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // The pause card shows a hint in a panel of fixed size; a hint that does not fit is cut off.
        [Test]
        public void TheHints_FitThePauseCardsPanel()
        {
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
            var host = new GameObject("Level 5 Hints") { hideFlags = HideFlags.DontSave };
            GameRunner runner = host.AddComponent<GameRunner>();
            try
            {
                var devices = new FakeDevices();
                runner.Begin(LaunchOptions.FromUrl("?level=5"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
                MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
                Assert.IsNotNull(menu);
                for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
                devices.State.EscapePressed = true;
                runner.Frame(Sim.Dt);
                PauseMenu pause = menu.Pause;
                Assert.AreSame(pause, menu.Current, "paused");
                string[] hints = runner.Game.Level.Hints;
                Assert.AreEqual(3, hints.Length);
                float room = pause.HintText.rectTransform.rect.height;
                for (int i = 0; i < hints.Length; i++)
                {
                    pause.NextHint();
                    runner.Frame(Sim.Dt);
                    Assert.AreEqual(hints[i], pause.HintText.text);
                    pause.HintText.ForceMeshUpdate();
                    float height = pause.HintText.preferredHeight;
                    Debug.Log("[Level05] hint " + (i + 1) + ": " + hints[i].Length + " characters, " + pause.HintText.textInfo.lineCount + " lines, " + height.ToString("0") + " of " + room.ToString("0") + " high");
                    Assert.Greater(height, 10f, "the text was laid out");
                    Assert.LessOrEqual(height, room, "hint " + (i + 1) + " fits its panel (" + pause.HintText.textInfo.lineCount + " lines): \"" + hints[i] + "\"");
                    Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (i + 1) + " is not cut off");
                }
                // (The check does tell: one more sentence on the third hint is a sixth line, and the panel holds five.)
                pause.HintText.text = hints[2] + " The wind does the rest of it for you, all the way across the canyon.";
                pause.HintText.ForceMeshUpdate();
                Assert.Greater(pause.HintText.preferredHeight, room, "a hint a line longer would not fit (" + pause.HintText.textInfo.lineCount + " lines)");
            }
            finally
            {
                runner.Shutdown();
                Object.DestroyImmediate(host);
                Game.Current?.Dispose();
                UiCapture.Reset();
                Settings.Use(null);
            }
        }
    }

    /// <summary>
    /// Pictures of what the solver does not show: the fan from the blotter, a feather that is too small
    /// under its rider, a crumb in the wind, the way back seen from the far box. For looking at
    /// (tools/out/shots/level05/tour-*.png); what it asserts is that every beat happened.
    /// </summary>
    public class Level05Tour : RoomFixture
    {
        const int Width = 1280, Height = 720;

        Level05FanFeather level;
        Bot bot;
        string pending;
        bool async;

        [SetUp]
        public void NoPlaceholders()
        {
            async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void Restore() => ShaderUtil.allowAsyncCompilation = async;

        void Shoot(string name)
        {
            Presentation.Frame(0f, 1f);
            Texture2D image = Shots.Photograph(Presentation.Camera, Width, Height, TierSpec.Of(Presentation.Context.Quality).Msaa);
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "out", "shots", "level05");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "tour-" + name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        // A tick is a frame; a script asks for a picture by naming it.
        void Play(IEnumerator script, float seconds)
        {
            var runner = new BotRunner(script, bot, Game);
            for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
            {
                bool more = runner.Advance();
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
                if (pending != null)
                {
                    Shoot(pending);
                    pending = null;
                }
                if (!more) return;
            }
            Assert.Fail("the tour's script did not finish in " + seconds + " s (bot at " + Game.Player.Position + ")");
        }

        [Test]
        public void Tour()
        {
            RequireGraphics();
            Game = Game.Create();
            Game.LoadLevel(5);
            level = (Level05FanFeather)Game.Level;
            bot = new Bot(Game);
            Presentation = Presentation.Create(Game, new PresentationOptions { Quality = QualityTier.Medium });
            Presentation.Frame(0f, 1f);
            Object.DestroyImmediate(Shots.Photograph(Presentation.Camera, Width, Height, 1));
            var said = new List<string>();
            Game.Events.Message += e => said.Add((Game.LevelTicks * Sim.Dt).ToString("0.0") + " s: " + e.Text);
            var states = new List<string>();
            level.Raft.SailStalled += share => states.Add("stalled " + share.ToString("0.00"));
            level.Raft.SailLaunched += () => states.Add("launched");
            level.Raft.SailDocked += () => states.Add("docked");
            Prop feather = level.Feather;
            Vector3 away = (Level05FanFeather.Spawn - Level05FanFeather.SpoolBase).normalized;

            IEnumerator Script()
            {
                // The start, looking round to the left: the blotter, the stream, the fan behind it.
                yield return bot.LookAt(new Vector3(-1f, 2f, 2f));
                pending = "left-from-start";
                yield return null;

                // Picked up from too far off, and held over the outline: smaller than the inner dashes.
                yield return bot.WalkTo(Level05FanFeather.SpoolBase + away * 2.6f, 0.05f);
                yield return level.Take(bot);
                yield return bot.WalkTo(Level05FanFeather.RoundTheSpool, 0.3f);
                yield return bot.WalkTo(Level05FanFeather.EdgeSpot, 0.15f);
                yield return bot.LookAt(Level05FanFeather.Aim);
                yield return bot.Wait(0.3f);
                pending = "held-too-small";
                yield return null;
                yield return bot.DropAt(Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                pending = "let-go-too-small";
                yield return null;

                // Down, and onto it: it cups, lifts and flops back.
                yield return level.Descend(bot);
                yield return bot.LookAt(new Vector3(-1f, 5f, -8f));
                pending = "fan-from-the-apron";
                yield return null;
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Stall, 3f);
                yield return bot.Wait(0.5f);
                pending = "stall";
                yield return null;
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 3f);

                // Off it, to the middle of the blotter's edge; the fan from there.
                yield return bot.WalkTo(new Vector3(2.8f, 0f, 3f), 0.3f);
                yield return bot.LookAt(new Vector3(-1f, 5f, -8f));
                pending = "fan-from-the-blotter";
                yield return null;

                // Taken from here and put down at the feet, it is a crumb: the wind takes it.
                yield return bot.Grab(feather);
                yield return bot.DropAt(new Vector3(1.6f, 0.05f, 4.2f));
                yield return bot.Wait(0.3f);
                yield return bot.LookAt(feather);
                pending = "blown";
                yield return null;
                yield return bot.Until(() => (feather.Position - level.FeatherHome).sqrMagnitude < 0.01f, 8f);
                yield return bot.Wait(0.5f);

                // Back up the ruler and the level as it is meant to be played.
                yield return bot.WalkTo(new Vector3(9f, 0f, 14f), 0.3f);
                yield return bot.WalkTo(new Vector3(13.5f, 0f, 16.6f), 0.3f);
                yield return bot.WalkTo(new Vector3(13.5f, 3f, 9.4f), 0.3f);
                pending = "up-the-ruler";
                yield return null;
                yield return bot.WalkTo(Level05FanFeather.Spawn, 0.1f);
                yield return level.Take(bot);
                // What an eager player sees: straight from the start mark, held over the outline.
                yield return bot.LookAt(Level05FanFeather.Aim);
                yield return bot.Wait(0.3f);
                pending = "held-from-the-start";
                yield return null;
                yield return level.Place(bot, Level05FanFeather.EdgeSpot, Level05FanFeather.Aim);
                yield return bot.Until(() => level.Raft.State == SailState.Moored, 6f);
                yield return level.Descend(bot);
                pending = "moored-from-the-apron";
                yield return null;
                yield return level.Board(bot);
                yield return bot.Until(() => level.Raft.State == SailState.Glide, 3f);
                yield return bot.Wait(1.2f);
                yield return bot.LookAt(new Vector3(-1f, 4f, -8f));
                pending = "looking-back";
                yield return null;
                yield return bot.LookAt(new Vector3(-1f, -6f, bot.Player.Position.z + 6f));
                pending = "looking-down";
                yield return null;
                yield return bot.Until(() => level.Raft.State == SailState.Docked, 16f);
                yield return bot.LookAt(new Vector3(3f, 2f, 4f));
                pending = "back-from-the-far-box";
                yield return null;
                yield return bot.WalkTo(new Vector3(Level05FanFeather.ExitCentre.x, 0f, Level05FanFeather.ExitCentre.z), 0.5f, 10f);
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
            Play(Script(), 150f);
            Debug.Log("[Level05] tour: " + string.Join(", ", states) + "\n" + string.Join("\n", said));
            Assert.IsTrue(Game.LevelCompleted);
        }
    }
}
