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
using UnityEditor;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 2, The Thimble Chasm: the solver, the hole that cannot be crossed without the plug, the scale
    /// window of the well (LEVELS Appendix B: 9 to 13, intended 11.5), and what keeps it from being soft-locked.
    /// </summary>
    public class Level02Tests : SimTest
    {
        Level02ThimbleChasm level;
        Bot bot;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(2);
            level = (Level02ThimbleChasm)Game.Level;
            bot = new Bot(Game);
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

        // The whole solution with one thing changed: where the bot stands when it lets go.
        IEnumerator SolveFrom(Vector3 stand)
        {
            yield return bot.Grab(level.Thimble);
            yield return bot.WalkTo(stand, 0.15f);
            yield return bot.DropAt(Level02ThimbleChasm.OutlineCentre);
            yield return bot.Until(() => level.Well.Seated, 5f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 29f), 0.3f, 12f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 34f));
            yield return bot.Until(() => Game.LevelCompleted, 3f);
        }

        IEnumerator DropFrom(Vector3 stand)
        {
            yield return bot.Grab(level.Thimble);
            yield return bot.WalkTo(stand, 0.15f);
            yield return bot.DropAt(Level02ThimbleChasm.OutlineCentre);
        }

        [Test]
        public void TheLevelIsBuiltAsSpecified()
        {
            Load();
            Assert.AreEqual("thimble-chasm", level.Slug);
            Assert.AreEqual("The Thimble Chasm", level.Title);
            Assert.AreEqual(1, level.Phase);
            Assert.AreEqual("pegboard-workbench", level.Environment);
            Assert.AreEqual(-9f, level.GroundY);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.IsNotEmpty(level.Blurb);

            Prop thimble = level.Thimble;
            Assert.AreEqual(0.8f, thimble.Scale, 1e-4f);
            Assert.AreEqual(0.3f, thimble.MinScale, 1e-4f);
            Assert.AreEqual(13f, thimble.MaxScale, 1e-4f, "the clamp is the top of the well's window: too far back is harmless");
            Assert.IsTrue(thimble.HasTag(Level02ThimbleChasm.PlugTag));
            Assert.AreEqual(GrabPose.Upright, thimble.GrabPose);
            Assert.IsTrue(thimble.Grabbable);
            // The owner's brief: "a tiny silver thimble". Silver, and the candy that says "yours to lift" is
            // the band round its rim - the room's hero candy, Tangerine.
            Assert.AreSame(Materials.Toy(ToyFactory.Silver, Palette.Silver), thimble.GameObject.transform.Find("Visual").GetComponent<MeshRenderer>().sharedMaterial);
            Assert.AreSame(Materials.Toy(ToyRecipe.BrushedMetal, Palette.Tangerine), thimble.GameObject.transform.Find("Band").GetComponent<MeshRenderer>().sharedMaterial);
            Assert.IsTrue(Palette.Same(Palette.Pool.Hero, ToyInfo.Of(thimble.GameObject).Candy), "its candy pops against the Pool room");
            // On the spool, at eye height, a step from the spawn: the first pickup is a close one.
            TestHelpers.RunSeconds(Game, 1f);
            Assert.AreEqual(1.42f, thimble.Center.y, 0.02f);
            Assert.Less(Vector3.Distance(thimble.Position, Level02ThimbleChasm.SpoolBase + Vector3.up * 1.42f), 0.03f, "it rests on the spool");
            Assert.AreEqual(1.5f, Vector3.Distance(Game.Player.Eye, thimble.Center), 0.05f);

            Assert.AreEqual(FitState.TooSmall, level.Well.Fit(8.9f));
            Assert.AreEqual(FitState.Good, level.Well.Fit(9f));
            Assert.AreEqual(FitState.Good, level.Well.Fit(13f));
            Assert.IsTrue(level.Hazard.Enabled);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(0f, 1.3f, 34f)), 1e-3f);

            // From the spawn the player sees the toy, the hole (the far side of its striped lip) and the outline.
            Vector3 eye = Game.Player.Eye;
            Assert.AreSame(thimble, Seen(eye, thimble.Center), "the thimble is in plain view");
            Assert.IsNull(Blocker(eye, new Vector3(0f, -0.5f, 29.9f)), "the far side of the well's lip is in view: the hole shows");
            Assert.IsNull(Blocker(eye, Level02ThimbleChasm.OutlineCentre + Vector3.back * 0.1f), "the outline is in view");
            Assert.IsNull(Blocker(eye, new Vector3(0f, 1.3f, 31f)), "the way out is in view");
        }

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
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            TestHelpers.PlayLevel(Game, 60f);
            Debug.Log("[Level02] solve: k " + ratio.ToString("0.000") + ", thimble " + dropped.ToString("0.00") + ", " + (Game.LevelTicks * Sim.Dt).ToString("0.0") + " s");
            Assert.AreEqual(0.531f, ratio, 0.02f, "picked up from the spawn, 1.5 away");
            Assert.AreEqual(11.5f, dropped, 11.5f * 0.05f, "the solver's drop (LEVELS Appendix B: 11.5 +- 5 %)");
            Assert.IsTrue(level.Well.Seated);
            Assert.AreSame(level.Thimble, level.Well.SeatedProp);
            Assert.IsFalse(level.Thimble.Grabbable, "the seated thimble is the floor: it cannot be pulled from under the player");
            Assert.AreEqual(0, level.Hazard.Catches, "nobody fell in, and walking across the plug is safe");
            Assert.AreEqual(0, level.Leash.Returns);
            GadgetKit.VerticalExtent(level.Thimble, out _, out float top);
            Assert.AreEqual(0f, top, 0.01f, "its top is flush with the walkway");
            // And what is walked on is plain silver: the candy band is down the well with the rim.
            Assert.Less(level.Thimble.GameObject.transform.Find("Band").GetComponent<MeshRenderer>().bounds.max.y, -1f);
            Assert.AreEqual(0f, level.Thimble.Position.x, 1e-3f);
            Assert.AreEqual(Level02ThimbleChasm.AxisZ, level.Thimble.Position.z, 1e-3f, "on the axis of the hole");
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            // The gauge has nothing left to judge: its lamp has made way for the one that stays green.
            Assert.IsFalse(level.Gauge.Lamp.Renderer.gameObject.activeInHierarchy);
            Assert.AreSame(Materials.Gadget(Palette.Go), level.Outline.sharedMaterial);
            Assert.Less(Game.LevelTicks * Sim.Dt, 20f);
        }

        [Test]
        public void TheHoleCannotBeWalkedOrJumped()
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

            // Straight at the exit, sprinting: off the edge and into the well, again and again.
            IEnumerator Straight()
            {
                IEnumerator run = bot.WalkTo(Level02ThimbleChasm.ExitCentre, 0.3f, 12f, true);
                while (run.MoveNext())
                {
                    Watch();
                    yield return run.Current;
                }
            }
            Assert.IsFalse(Drive(Straight(), 12f), "the walk never arrives");
            Assert.GreaterOrEqual(respawns, 2, "the well sent the player back to the start");
            Assert.AreEqual(respawns, level.Hazard.Catches);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthest, 20f, "walking off the edge at 16 carries nobody to the door at 30");

            // The shortest crossing (LEVELS: 10.6): along a wall to the tip of the floor beside the hole, a
            // sprint at the nearer edge of the door, and a jump at the last moment.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded && Game.Player.Position.y > -0.1f, 4f), "back on the walkway");
            respawns = 0;
            farthest = float.MinValue;
            var door = new Vector3(1f, 0f, 30.2f);
            bool jumped = false;
            IEnumerator Jumped()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 0f), 0.3f, 12f);
                yield return bot.WalkTo(new Vector3(6.55f, 0f, 13f), 0.2f, 12f);
                IEnumerator run = bot.WalkTo(door, 0.3f, 4f, true);
                while (run.MoveNext())
                {
                    Watch();
                    Vector3 p = Game.Player.Position;
                    float fromAxis = new Vector2(p.x, p.z - Level02ThimbleChasm.AxisZ).magnitude;
                    if (!jumped && Game.Player.Grounded && fromAxis < Level02ThimbleChasm.SiloRadius + 0.32f)
                    {
                        jumped = true;
                        // The movement keys stay down: a running jump.
                        yield return bot.Jump();
                        continue;
                    }
                    yield return run.Current;
                }
            }
            Drive(Jumped(), 14f);
            Assert.IsTrue(jumped, "the bot reached the edge and jumped");
            Assert.GreaterOrEqual(respawns, 1, "the jump ended in the well");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(farthest, 20.5f, "the jump did leave the floor's tip");
            Assert.Less(farthest, 27f, "a sprint jump carries about 5.5 units; the door is 10.6 from the nearest floor");
        }

        // The ends of the window (LEVELS Appendix B: works from 9.0 to the clamp at 13).
        [TestCase(-4f, 12.99f, 13.01f, TestName = "TheHighEndOfTheWindow_TheClamp_PlugsTheHole")]
        [TestCase(8.5f, 9.0f, 9.6f, TestName = "TheLowEndOfTheWindow_PlugsTheHole")]
        public void TheWindow(float standZ, float least, float most)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            int completed = 0;
            Game.Events.LevelCompleted += e => completed++;
            BotRunner.Run(Game, SolveFrom(new Vector3(0f, 0f, standZ)), 60f);
            Debug.Log("[Level02] from z = " + standZ + ": thimble " + dropped.ToString("0.00"));
            Assert.GreaterOrEqual(dropped, least);
            Assert.LessOrEqual(dropped, most);
            Assert.IsTrue(level.Well.Seated);
            GadgetKit.VerticalExtent(level.Thimble, out _, out float top);
            Assert.AreEqual(0f, top, 0.01f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void AThimbleThatIsTooSmall_FallsIn_AndComesBackToTheSpool()
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            var rejected = new List<FitState>();
            level.Well.OnRejected += (prop, fit) => rejected.Add(fit);
            Prop thimble = level.Thimble;

            // Let go from the edge of the hole: there the wall stops it at about two thirds of the size.
            BotRunner.Run(Game, DropFrom(new Vector3(0f, 0f, 13f)), 30f);
            Debug.Log("[Level02] from z = 13: thimble " + dropped.ToString("0.00"));
            Assert.Less(dropped, 8.5f, "clearly too small");
            Assert.Greater(dropped, 5f);
            TestHelpers.RunSeconds(Game, 0.2f);
            CollectionAssert.AreEqual(new[] { FitState.TooSmall }, rejected);
            Assert.IsFalse(level.Well.Seating);
            Assert.AreEqual(1, said.Count, "told once what was wrong");

            // It falls to the pad, out of sight, and the pad gives it back.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 8f), "the thimble was not returned (at " + thimble.Position + ")");
            Assert.IsFalse(level.Well.Seated);
            Assert.IsFalse(Game.LevelCompleted);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.AreEqual(0.8f, thimble.Scale, 1e-4f, "at the size it started with");
            Assert.Less(Vector3.Distance(thimble.Position, Level02ThimbleChasm.SpoolBase + Vector3.up * 1.42f), 0.05f, "on the spool");
            Assert.IsTrue(thimble.Grabbable);

            // And from there the level is solved the ordinary way.
            IEnumerator Again()
            {
                yield return bot.WalkTo(Level02ThimbleChasm.Spawn, 0.3f, 12f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count, "and it is not said twice");
        }

        [Test]
        public void TheOutlineSaysWhetherTheHeldThimbleWouldFit()
        {
            Load();
            Material paint = level.Outline.sharedMaterial;
            Assert.AreEqual(FitState.Idle, level.Gauge.State);

            IEnumerator HoldAt(float z)
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.15f);
                yield return bot.LookAt(Level02ThimbleChasm.OutlineCentre);
                yield return bot.Wait(0.1f);
            }
            // At the edge of the hole the thimble covers about half of the outline: too small, the red of "wrong".
            Assert.IsTrue(Level02ThimbleChasm.Wrong.Equals(Toybox.Render.GadgetFx.Wrong), "the outline and the gauge's lamp show the same signal");
            BotRunner.Run(Game, HoldAt(13f), 30f);
            Assert.AreEqual(FitState.TooSmall, level.Gauge.State);
            Assert.Less(level.Thimble.Scale, 9f);
            Assert.AreSame(Materials.Gadget(Level02ThimbleChasm.Wrong), level.Outline.sharedMaterial);
            // A few steps back it fills it: green.
            BotRunner.Run(Game, HoldAt(3f), 30f);
            Assert.AreEqual(FitState.Good, level.Gauge.State);
            Assert.AreSame(Materials.Gadget(Palette.Go), level.Outline.sharedMaterial);
            // Looking away with it (the thimble lands on the floor at the player's feet): paint again.
            IEnumerator LookDown()
            {
                yield return bot.LookAt(new Vector3(0f, 0f, 5f));
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, LookDown(), 10f);
            Assert.AreEqual(FitState.Idle, level.Gauge.State);
            Assert.AreSame(paint, level.Outline.sharedMaterial);
        }

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Load();
            // Restart with the thimble in hand, half way down the walkway.
            IEnumerator Half()
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, 1f));
            }
            BotRunner.Run(Game, Half(), 30f);
            Assert.AreSame(level.Thimble, Game.Grabber.Held);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreSame(level, Game.Level, "the same level instance is built again");
            Assert.AreEqual(0.8f, level.Thimble.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level02ThimbleChasm.Spawn), 0.1f);

            // Restart once more with the hole plugged: the well is open again, and dangerous.
            BotRunner.Run(Game, DropFrom(Level02ThimbleChasm.StandPoint), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Well.Seated, 5f));
            Assert.IsFalse(level.Thimble.Grabbable);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(level.Well.Seated);
            Assert.IsNull(level.Well.SeatedProp);
            Assert.IsTrue(level.Thimble.Grabbable);
            Assert.AreEqual(FitState.Idle, level.Gauge.State);
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 60f);
            Assert.IsTrue(level.Well.Seated);
        }

        [Test]
        public void UnderThePlug_ThePlayerIsSentBack()
        {
            Load();
            BotRunner.Run(Game, DropFrom(Level02ThimbleChasm.StandPoint), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Well.Seated, 5f));
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;

            // Between the seated thimble and the wall of the well, under the grommet: a player who slipped in
            // beside the falling thimble. The well is still a hazard down there.
            float gap = (0.5f * level.Thimble.Scale + Level02ThimbleChasm.SiloRadius) * 0.5f;
            Game.Player.Teleport(new Vector3(gap, -5f, Level02ThimbleChasm.AxisZ), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 3f), "sealed in under the plug at " + Game.Player.Position);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level02ThimbleChasm.Spawn), 0.2f);

            // And from the start the way across is open.
            IEnumerator Across()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 29f), 0.3f, 15f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 34f));
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
            BotRunner.Run(Game, Across(), 30f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, respawns);
        }

        [Test]
        public void AThimbleThatLeavesTheWorld_ComesBack()
        {
            Load();
            Prop thimble = level.Thimble;
            Vector3 home = thimble.Position;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;

            // Below the kill plane (it cannot get there by itself: the level is closed all round and capped).
            thimble.SetScale(4f);
            thimble.SetPose(new Vector3(30f, level.KillY - 1f, 0f), Quaternion.identity);
            TestHelpers.Run(Game, 2);
            Assert.AreEqual(1, respawned);
            Assert.Less(Vector3.Distance(thimble.Position, home), 0.02f, "back on the spool");
            Assert.AreEqual(0.8f, thimble.Scale, 1e-4f, "at the size it started with");

            // On the pad at the bottom of the well, out of sight: the pad gives it back.
            thimble.SetScale(3f);
            thimble.SetPose(new Vector3(2f, Level02ThimbleChasm.PadTop + 1.25f, Level02ThimbleChasm.AxisZ - 1f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 6f));
            Assert.Less(Vector3.Distance(thimble.Position, home), 0.02f);
            Assert.AreEqual(0.8f, thimble.Scale, 1e-4f);

            // The player who falls in is sent back to the start, and can carry on.
            Game.Player.Teleport(new Vector3(0f, 1f, Level02ThimbleChasm.AxisZ), 0f);
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 4f));
            Assert.Less(Vector3.Distance(Game.Player.Position, Level02ThimbleChasm.Spawn), 0.2f);
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 60f);
        }

        // ================================================================================================
        // Review (tools/out/notes/level02-review.md): what a playtest found, kept as regressions.
        // ================================================================================================

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        IEnumerator GrabFromAndDrop(Vector3 grabFrom, Vector3 stand, Vector3 aim)
        {
            yield return bot.WalkTo(grabFrom, 0.1f);
            yield return bot.Grab(level.Thimble);
            yield return bot.WalkTo(stand, 0.15f, 20f);
            yield return bot.DropAt(aim);
        }

        IEnumerator WalkOut()
        {
            yield return bot.Until(() => level.Well.Seated, 5f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 29f), 0.3f, 15f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 34f));
            yield return bot.Until(() => Game.LevelCompleted, 3f);
        }

        bool OnTheSpool() => level.Thimble.Scale == Level02ThimbleChasm.StartScale &&
                             Vector3.Distance(level.Thimble.Position, Level02ThimbleChasm.SpoolBase + Vector3.up * 1.42f) < 0.05f;

        // Found: let go with the view a little low (about 6 degrees) the thimble stops on the walkway's edge,
        // topples into the well and lies on its side. Lying, it rolled to and fro for six to ten seconds
        // (once: for good) before the leash, which waited for it to lie still, sent it back - a hump in the
        // well with its top at -9 + s, above the hazard. Now: gone a second and a half after it went in.
        [TestCase(1.5f, 3f, 4.5f, TestName = "ToppledInFromTheEdge_ItIsBackOnTheSpoolInSeconds")]
        [TestCase(0.75f, 10f, 6f, TestName = "ToppledInFromTheEdge_TakenFromTouchingDistance_ItIsBackOnTheSpoolInSeconds")]
        public void ToppledIn(float grabDistance, float standZ, float aimY)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Vector3 grabFrom = grabDistance > 1f ? Level02ThimbleChasm.Spawn : new Vector3(0.16f, 0f, -4.8f);
            BotRunner.Run(Game, GrabFromAndDrop(grabFrom, new Vector3(0f, 0f, standZ), new Vector3(0f, aimY, 30f)), 30f);
            Assert.Greater(dropped, 7f);
            Assert.Less(dropped, 9f, "too small, and a size that lies in the well with its top near floor level");

            float tilt = 0f;
            int ticks = 0;
            while (level.Leash.Returns == 0 && ticks < TestHelpers.Ticks(6f))
            {
                Game.Tick();
                ticks++;
                if (level.Thimble.Center.y > Level02ThimbleChasm.LeashTop) continue;
                tilt = Mathf.Max(tilt, Vector3.Angle(level.Thimble.Rotation * Vector3.up, Vector3.up));
            }
            Debug.Log("[Level02] toppled in at " + dropped.ToString("0.00") + ", tilt " + tilt.ToString("0") + ", back after " + (ticks * Sim.Dt).ToString("0.0") + " s");
            Assert.AreEqual(1, level.Leash.Returns, "not returned within 6 s (thimble at " + level.Thimble.Center + ")");
            Assert.Greater(tilt, 45f, "it did go in on its side");
            Assert.Less(ticks * Sim.Dt, 4.5f, "from letting go to back on the spool");
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level02ThimbleChasm.EdgeLine }, said, "told what stopped it: the floor, not the place the player stood");
            Assert.IsFalse(Game.LevelCompleted);
        }

        // Found with it: a player on that hump stood above the hazard (its top was at -1.2). Anything under
        // the walkway's slab is the well now.
        [Test]
        public void TheWellIsAHazardRightUpToTheFloor()
        {
            Load();
            Zone well = level.Hazard.Shape;
            float z = Level02ThimbleChasm.AxisZ;
            Assert.IsTrue(well.Contains(new Vector3(0f, -0.7f, z)));
            Assert.IsTrue(well.Contains(new Vector3(6.5f, -0.7f, z)));
            Assert.IsTrue(well.Contains(new Vector3(0f, -8.9f, z + 6f)));
            Assert.IsFalse(well.Contains(new Vector3(0f, 0f, z)), "the plug's top is not in it");
            Assert.IsFalse(well.Contains(new Vector3(0f, -0.05f, 15.8f)), "nor the walkway's edge");
            Assert.IsFalse(well.Contains(new Vector3(0f, 0f, 30.3f)), "nor the tunnel's floor");

            // A foot of headroom is all there is: a player set down a unit under the floor is sent back.
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            // (A fixture, not a bot move: something to stand on, one unit down.)
            TestHelpers.Box(Game.Context, new Vector3(0f, -1.25f, z), new Vector3(4f, 0.5f, 4f));
            Game.Player.Teleport(new Vector3(0f, -1f, z), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 1f), "standing at " + Game.Player.Position);
        }

        // Found: the thimble that came back is on its spool twenty units from the hole, and taken from there
        // (k = 0.04) nothing makes it fit; the one line the level had said "step back". And held against the
        // outline that small it was not judged at all (its middle was more than 6 from the axis): no red.
        [Test]
        public void TakenFromFarOff_ItIsSaidAtOnce_TheOutlineIsRed_AndItComesBack()
        {
            Load();
            List<string> said = Listen();
            IEnumerator Far()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 10f), 0.2f);
                yield return bot.Grab(level.Thimble);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Far(), 30f);
            Assert.Less(Game.Grabber.Ratio, Level02ThimbleChasm.FarRatio);
            CollectionAssert.AreEqual(new[] { Level02ThimbleChasm.FarLine }, said);

            IEnumerator Hold()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 13f), 0.2f);
                yield return bot.LookAt(Level02ThimbleChasm.OutlineCentre);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Hold(), 30f);
            Assert.Less(level.Thimble.Scale, 2f);
            Assert.AreEqual(FitState.TooSmall, level.Gauge.State, "a small thimble against the far wall is judged too");
            Assert.AreSame(Materials.Gadget(Level02ThimbleChasm.Wrong), level.Outline.sharedMaterial);
            Assert.AreSame(Materials.Gadget(Level02ThimbleChasm.Wrong), level.HoleRim.sharedMaterial);

            IEnumerator LetGo()
            {
                yield return bot.Drop();
            }
            BotRunner.Run(Game, LetGo(), 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 4f));
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level02ThimbleChasm.FarLine }, said, "'step back' would be wrong advice: it is not said");

            // Taken from beside it, the ordinary way works; and that grab is not told off.
            IEnumerator Again()
            {
                yield return bot.WalkTo(Level02ThimbleChasm.Spawn, 0.3f, 12f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // Found (the builder's known issue): a small thimble thrown through the door stayed in the tunnel.
        [Test]
        public void ThrownThroughTheDoor_ItComesBack()
        {
            Load();
            BotRunner.Run(Game, GrabFromAndDrop(new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 15.5f), new Vector3(0f, 0.8f, 33f)), 30f);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.Greater(level.Thimble.Center.z, 30f, "it did land in the tunnel");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Leash.Returns == 1, 3f), "left in the tunnel at " + level.Thimble.Center);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
        }

        // Found: a thimble at the clamp that hangs short of the hole (let go from the back wall with the view
        // high) stands on the walkway, 13 across, and nothing said what was wrong with it.
        [Test]
        public void BigEnoughButShortOfTheHole_IsSaid_AndCarriedCloserItPlugs()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            BotRunner.Run(Game, GrabFromAndDrop(Level02ThimbleChasm.Spawn, new Vector3(0f, 0f, -9.5f), new Vector3(0f, 11f, 30f)), 30f);
            Assert.AreEqual(13f, dropped, 0.01f);
            TestHelpers.RunSeconds(Game, 2f);
            Assert.IsFalse(level.Well.Seating || level.Well.Seated);
            Assert.Greater(level.Thimble.Center.y, 0f, "it stands on the walkway");
            Assert.AreEqual(0, level.Leash.Returns);
            CollectionAssert.AreEqual(new[] { Level02ThimbleChasm.MissedLine }, said);
            Assert.IsTrue(level.Thimble.Grabbable);

            // Taken again from the same place (it looks as big as it did) and carried a few steps on.
            IEnumerator Closer()
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(Level02ThimbleChasm.StandPoint, 0.3f, 12f);
                yield return bot.DropAt(Level02ThimbleChasm.OutlineCentre);
                yield return WalkOut();
            }
            BotRunner.Run(Game, Closer(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // Found: taken again from close beside where it stands the thimble is very big on screen (k = 1.6);
        // held toward the outline from the middle of the walkway it stops on the floor every time. It is not
        // a dead end: carried to the hole it hangs over it at the clamp.
        [Test]
        public void TakenAgainFromBesideIt_BigOnScreen_ItPlugsFromTheEdge()
        {
            Load();
            IEnumerator Script()
            {
                yield return GrabFromAndDrop(Level02ThimbleChasm.Spawn, new Vector3(0f, 0f, -9.5f), new Vector3(0f, 11f, 30f));
                yield return bot.Wait(1.5f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 7.5f), 0.2f, 10f);
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, 15.6f), 0.15f, 10f);
                yield return bot.LookAt(Game.Player.Eye + new Vector3(0f, Mathf.Sin(20f * Mathf.Deg2Rad), Mathf.Cos(20f * Mathf.Deg2Rad)) * 10f);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Script(), 60f);
            Assert.Greater(Game.Grabber.Ratio, 1.4f);
            Assert.AreEqual(FitState.Good, level.Gauge.State);
            IEnumerator Finish()
            {
                yield return bot.Drop();
                yield return WalkOut();
            }
            BotRunner.Run(Game, Finish(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Found: from the start, with the view a little higher than the outline's middle, the thimble reaches
        // its clamp 6.1 to 6.8 from the hole's axis - half over the hole - and the well (which took a middle
        // within 6) left it standing on the edge. Its middle is over the hole: it is taken.
        [TestCase(-7f, 11f, TestName = "TheClampHangingHalfOverTheHole_FromBehindTheStart_IsTaken")]
        [TestCase(-6f, 15f, TestName = "TheClampHangingHalfOverTheHole_FromTheStart_IsTaken")]
        public void TheClampHalfOverTheHole(float standZ, float aimY)
        {
            Load();
            List<string> said = Listen();
            Vector3 centre = default;
            Game.Events.PropDropped += e => centre = e.Prop.Center;
            IEnumerator Script()
            {
                yield return GrabFromAndDrop(Level02ThimbleChasm.Spawn, new Vector3(0f, 0f, standZ), new Vector3(0f, aimY, 30f));
                yield return WalkOut();
            }
            BotRunner.Run(Game, Script(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            float fromAxis = new Vector2(centre.x, centre.z - Level02ThimbleChasm.AxisZ).magnitude;
            Debug.Log("[Level02] clamp let go " + fromAxis.ToString("0.00") + " from the axis");
            Assert.Greater(fromAxis, 6f, "the case this test is about");
            Assert.Less(fromAxis, Level02ThimbleChasm.CaptureRadius);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // The third hint as a procedure: hold it over the outline; green - let go; red - step back; on the
        // floor (the paint stays white) - walk closer. It has to work however the thimble was picked up.
        // (LEVELS.md's wording - "stand a few steps past the spool" - leaves a thimble taken from touching
        // distance on the walkway.)
        [TestCase(0.16f, -4.8f, 0.9f, 1.2f, TestName = "FollowingThePaint_TakenFromTouchingDistance_PlugsTheHole")]
        [TestCase(-0.1f, -4.8f, 0.7f, 0.9f, TestName = "FollowingThePaint_TakenFromOneStepAway_PlugsTheHole")]
        [TestCase(0f, -6f, 0.5f, 0.56f, TestName = "FollowingThePaint_TakenFromTheStart_PlugsTheHole")]
        [TestCase(-1.6f, -4.8f, 0.3f, 0.36f, TestName = "FollowingThePaint_TakenFromThreeStepsAway_PlugsTheHole")]
        public void FollowingThePaint(float grabX, float grabZ, float leastRatio, float mostRatio)
        {
            Load();
            List<string> said = Listen();
            int steps = 0;
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(grabX, 0f, grabZ), 0.1f);
                yield return bot.Grab(level.Thimble);
                float z = -2f;
                for (; steps < 40; steps++)
                {
                    yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.15f, 10f);
                    yield return bot.LookAt(Level02ThimbleChasm.OutlineCentre);
                    yield return bot.Wait(0.1f);
                    FitState paint = level.Gauge.State;
                    if (paint == FitState.Good) break;
                    z += paint == FitState.TooSmall ? -1f : 1f;
                    if (z < -9.5f || z > 15.5f) throw new BotException("ran out of walkway at z = " + z + " with the paint " + paint);
                }
                yield return bot.Drop();
                yield return WalkOut();
            }
            BotRunner.Run(Game, Script(), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            float ratio = Level02ThimbleChasm.StartScale / Vector3.Distance(new Vector3(grabX, 1.55f, grabZ), Level02ThimbleChasm.SpoolBase + Vector3.up * 1.42f);
            Debug.Log("[Level02] following the paint: k " + ratio.ToString("0.00") + ", " + steps + " steps, let go at " + level.Thimble.Scale.ToString("0.00"));
            Assert.GreaterOrEqual(ratio, leastRatio);
            Assert.LessOrEqual(ratio, mostRatio);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said, "green means it fits: nothing to tell off");
        }

        // What the paint promises, over a grid of pick-ups, standing places and views: green - the well takes
        // it; red - it does not, and the thimble comes back to the spool; white - it stays on the walkway (or
        // the well takes it after all). And whatever happens, the thimble is never left where it cannot be
        // reached.
        [Test]
        public void ThePaintKeepsItsWord()
        {
            var grabs = new[] { new Vector3(0.16f, 0f, -4.8f), Level02ThimbleChasm.Spawn, new Vector3(-1.6f, 0f, -4.8f) };
            float[] stands = { -6f, 0f, 6f, 10f, 14f };
            float[] aims = { 3f, 7.5f, 12f };
            int green = 0, red = 0, white = 0;
            foreach (Vector3 grab in grabs)
            {
                foreach (float z in stands)
                {
                    foreach (float aimY in aims)
                    {
                        Game?.Dispose();
                        Load();
                        string what = "grab from " + grab + ", stand z = " + z + ", aim y = " + aimY;
                        IEnumerator Hold()
                        {
                            yield return bot.WalkTo(grab, 0.1f);
                            yield return bot.Grab(level.Thimble);
                            yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.15f, 20f);
                            yield return bot.LookAt(new Vector3(0f, aimY, 30f));
                            yield return bot.Wait(0.05f);
                        }
                        BotRunner.Run(Game, Hold(), 60f);
                        FitState paint = level.Gauge.State;
                        IEnumerator LetGo()
                        {
                            yield return bot.Drop();
                        }
                        BotRunner.Run(Game, LetGo(), 5f);
                        TestHelpers.RunUntil(Game, () => level.Well.Seated || level.Leash.Returns > 0, 6f);
                        TestHelpers.RunSeconds(Game, 0.5f);
                        if (paint == FitState.Good)
                        {
                            green++;
                            Assert.IsTrue(level.Well.Seated, "green, and not taken: " + what);
                        }
                        else if (paint == FitState.TooSmall)
                        {
                            red++;
                            Assert.IsFalse(level.Well.Seated, "red, and taken: " + what);
                            // (Nearly always back on the spool. One that is a hair under 9 with its middle just
                            // past the edge stands there, held by the floor on either side of it: in reach.)
                            Assert.IsTrue(OnTheSpool() || level.Thimble.Center.y > 0f, "red, and lost: " + what + " (at " + level.Thimble.Center + ")");
                        }
                        else
                        {
                            white++;
                            Assert.IsTrue(level.Well.Seated || OnTheSpool() || level.Thimble.Center.y > 0f, "white, and lost: " + what + " (at " + level.Thimble.Center + ")");
                        }
                    }
                }
            }
            Debug.Log("[Level02] the paint: " + green + " green, " + red + " red, " + white + " white");
            Assert.Greater(green, 10);
            Assert.Greater(red, 3);
            Assert.Greater(white, 10);
        }

        [Test]
        public void ThePaintThatABigThimbleDoesNotCover_SaysItToo()
        {
            Load();
            Material paint = level.HoleRim.sharedMaterial;
            Assert.AreEqual(2, level.Tapes.Count);
            foreach (Renderer tape in level.Tapes) Assert.AreSame(paint, tape.sharedMaterial);

            IEnumerator HoldAt(float z)
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, z), 0.15f);
                yield return bot.LookAt(Level02ThimbleChasm.OutlineCentre);
                yield return bot.Wait(0.1f);
            }
            // Too small: the outline and the band round the hole blink red; the lines on the walls stay paint.
            BotRunner.Run(Game, HoldAt(13f), 30f);
            Assert.AreSame(Materials.Gadget(Level02ThimbleChasm.Wrong), level.HoleRim.sharedMaterial);
            foreach (Renderer tape in level.Tapes) Assert.AreSame(paint, tape.sharedMaterial, "the walls only ever say 'it fits'");
            // It fits (and at 11.6 it covers the outline and the lamp): band and walls are green.
            BotRunner.Run(Game, HoldAt(3f), 30f);
            Assert.AreEqual(FitState.Good, level.Gauge.State);
            Assert.AreSame(Materials.Gadget(Palette.Go), level.HoleRim.sharedMaterial);
            foreach (Renderer tape in level.Tapes) Assert.AreSame(Materials.Gadget(Palette.Go), tape.sharedMaterial);
            // The lines are in view beside the held thimble: nothing stands between the eye and the walls.
            Vector3 eye = Game.Player.Eye;
            foreach (float side in new[] { -1f, 1f })
                Assert.IsNull(Blocker(eye, new Vector3(side * (Level02ThimbleChasm.HalfWidth - 0.1f), Level02ThimbleChasm.TapeY, eye.z + 8f)));
            // Seated: green for good.
            IEnumerator LetGo()
            {
                yield return bot.Drop();
                yield return bot.Until(() => level.Well.Seated, 5f);
            }
            BotRunner.Run(Game, LetGo(), 10f);
            Assert.AreSame(Materials.Gadget(Palette.Go), level.HoleRim.sharedMaterial);
            foreach (Renderer tape in level.Tapes) Assert.AreSame(Materials.Gadget(Palette.Go), tape.sharedMaterial);
        }

        // Found in the pictures: after a quick aim the gauge's last word before the release can be "too small"
        // (it reads the hold one tick late), and the band round the hole stayed red while the well slid the
        // thimble in. Taken by the well means green, from that tick on.
        [Test]
        public void WhileTheWellTakesTheThimble_ThePaintIsGreen()
        {
            Load();
            BotRunner.Run(Game, DropFrom(Level02ThimbleChasm.StandPoint), 30f);
            for (int tick = 0; tick < 20; tick++)
            {
                Assert.IsTrue(level.Well.Seating, "tick " + tick);
                Assert.AreSame(Materials.Gadget(Palette.Go), level.HoleRim.sharedMaterial, "the band, tick " + tick);
                Assert.AreSame(Materials.Gadget(Palette.Go), level.Outline.sharedMaterial, "the outline, tick " + tick);
                foreach (Renderer tape in level.Tapes) Assert.AreSame(Materials.Gadget(Palette.Go), tape.sharedMaterial, "the walls, tick " + tick);
                Game.Tick();
            }
        }

        // Awkward moments.
        [TestCase(8, TestName = "RestartingWhileTheWellSlidesTheThimbleIn_Works")]
        [TestCase(40, TestName = "RestartingWhileTheThimbleFallsToItsSeat_Works")]
        public void RestartingWhileTheWellHasTheThimble(int ticksAfterLettingGo)
        {
            Load();
            BotRunner.Run(Game, DropFrom(Level02ThimbleChasm.StandPoint), 30f);
            TestHelpers.Run(Game, ticksAfterLettingGo);
            Assert.IsTrue(level.Well.Seating, "the well has it and it is not in its seat yet");
            Assert.IsFalse(level.Thimble.Grabbable);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(level.Well.Seating || level.Well.Seated);
            Assert.IsTrue(level.Thimble.Grabbable);
            Assert.AreEqual(0.8f, level.Thimble.Scale, 1e-4f);
            Assert.IsFalse(level.Thimble.Driven);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.IsTrue(OnTheSpool());
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void FallingInWithTheThimbleInHand_ThePlayerKeepsIt_AndCarriesOn()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Fall()
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, 22f), 0.3f, 6f, true);
            }
            Assert.IsFalse(Drive(Fall(), 4.5f), "the run never arrives");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns >= 1 && Game.Player.Grounded, 3f));
            Assert.AreSame(level.Thimble, Game.Grabber.Held, "still in hand");
            Assert.AreEqual(0, level.Leash.Returns);
            IEnumerator CarryOn()
            {
                yield return bot.WalkTo(Level02ThimbleChasm.StandPoint, 0.3f, 12f);
                yield return bot.DropAt(Level02ThimbleChasm.OutlineCentre);
                yield return WalkOut();
            }
            BotRunner.Run(Game, CarryOn(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Let go in mid-air over the hole: the thimble is judged like any other, the player goes back to the start.
        [Test]
        public void JumpingInAndLettingGoInTheAir_LosesNothing()
        {
            Load();
            IEnumerator JumpDrop()
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(0f, 0f, 15.6f), 0.2f, 8f, true);
                yield return bot.Jump();
                yield return bot.DropAt(Level02ThimbleChasm.OutlineCentre);
            }
            BotRunner.Run(Game, JumpDrop(), 30f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hazard.Catches == 1, 3f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Well.Seated || level.Leash.Returns == 1, 5f));
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(level.Well.Seated || OnTheSpool());
            if (!level.Well.Seated) TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void OnlyTheThimbleCanBeTaken_AndItStaysUpright()
        {
            Load();
            Assert.AreEqual(1, Game.Props.Count, "the thimble is the only prop: spool, blocks, marker and ruler are scenery");
            TestHelpers.RunSeconds(Game, 0.5f);
            // A click on each piece of scenery takes nothing.
            var scenery = new[]
            {
                new Vector3(0.9f, 0.5f, -4.8f),      // the spool, under the thimble
                new Vector3(-5.6f, 0.45f, -9.2f),    // the blocks
                new Vector3(-3.9f, 0.55f, -8.9f),
                new Vector3(5.2f, 0.4f, -9.1f),      // the marker
                new Vector3(-6.1f, 0.06f, 2f),       // the ruler
                Level02ThimbleChasm.GaugeLamp,       // the gauge's lamp
                new Vector3(0f, -8.8f, 26f),         // the pad
            };
            foreach (Vector3 point in scenery)
            {
                Vector3 target = point;
                IEnumerator Poke()
                {
                    if (target.y < -1f) yield return bot.WalkTo(new Vector3(0f, 0f, 15.5f), 0.2f, 10f);
                    yield return bot.LookAt(target);
                    yield return bot.Click();
                    yield return bot.Wait(0.05f);
                }
                BotRunner.Run(Game, Poke(), 30f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + point + " took " + (Game.Grabber.Held != null ? Game.Grabber.Held.Name : "nothing"));
            }
            // The thimble cannot be laid on its side by hand (a thimble on its side in the well is a hump).
            IEnumerator Turn()
            {
                yield return bot.WalkTo(Level02ThimbleChasm.Spawn, 0.3f, 12f);
                yield return bot.Grab(level.Thimble);
                yield return bot.RotateHeld(0, 1);
                yield return bot.RotateHeld(2, 1);
                yield return bot.Wait(0.3f);
                yield return bot.DropAt(new Vector3(0f, 0f, -2f));
                yield return bot.Wait(1f);
            }
            BotRunner.Run(Game, Turn(), 30f);
            Assert.Less(Vector3.Angle(level.Thimble.Rotation * Vector3.up, Vector3.up), 2f);
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
                TestHelpers.PlayLevel(Game, 60f);
                if (run == 0)
                {
                    firstScale = dropped;
                    firstTicks = Game.LevelTicks;
                }
                Assert.AreEqual(firstScale, dropped, 0f, "run " + run + ": the thimble is let go at the same size every time");
                Assert.AreEqual(firstTicks, Game.LevelTicks, "run " + run + ": and it takes the same number of ticks");
            }
            // The same game, started over twice after it was completed.
            for (int again = 0; again < 2; again++)
            {
                Game.RestartLevel();
                Game.Tick();
                Assert.IsFalse(Game.LevelCompleted);
                float dropped = 0f;
                Game.Events.PropDropped += e => dropped = e.NewScale;
                TestHelpers.PlayLevel(Game, 60f);
                Assert.AreEqual(firstScale, dropped, 0.02f, "restart " + again);
            }
        }

        // Robustness: where a player would stand and aim, give or take.
        [TestCase(0f, 3f, 0f, 0f)]
        [TestCase(-2f, 3f, 0f, 0f)]
        [TestCase(2f, 3f, 0f, 0f)]
        [TestCase(0f, 1f, 0f, -1.5f)]
        [TestCase(0f, 5f, 0f, 2f)]
        [TestCase(1.5f, 2f, 1.5f, 1f)]
        [TestCase(-1.5f, 4f, -1.5f, -1f)]
        [TestCase(3f, 0f, -1f, 2f)]
        [TestCase(-3f, 6f, 1f, 0f)]
        [TestCase(0f, -2f, 0f, 1.5f)]
        [TestCase(0f, 7f, 0f, 3f)]
        public void TheSolve_ToleratesWhereThePlayerStandsAndAims(float standX, float standZ, float aimDx, float aimDy)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            Vector3 aim = Level02ThimbleChasm.OutlineCentre + new Vector3(aimDx, aimDy, 0f);
            IEnumerator Script()
            {
                yield return bot.Grab(level.Thimble);
                yield return bot.WalkTo(new Vector3(standX, 0f, standZ), 0.15f);
                yield return bot.DropAt(aim);
                yield return WalkOut();
            }
            BotRunner.Run(Game, Script(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level02] from (" + standX + ", " + standZ + ") at the outline + (" + aimDx + ", " + aimDy + "): thimble " + dropped.ToString("0.00"));
            Assert.IsTrue(Game.LevelCompleted);
            Assert.GreaterOrEqual(dropped, Level02ThimbleChasm.MinPlug);
            Assert.IsEmpty(said);
        }
    }

    /// <summary>
    /// The owner's brief for Level 2: "Grab a tiny silver thimble". Whether it reads as one is a matter of
    /// pictures, so these look at pictures - the level as the game shows it, the solver playing, on every
    /// quality tier: on its spool from the spawn it is grey against the room's blue with its candy band
    /// under it; in the hand the die-cut border is a line round it and not a white that runs into its
    /// body; grown to plug the well, its top is turned metal and not one flat grey.
    /// </summary>
    public class Level02LookTests : RoomFixture
    {
        const int Width = 1280, Height = 720;

        Level02ThimbleChasm level;
        BotRunner script;
        bool async;

        [SetUp]
        public void NoPlaceholders()
        {
            // Without this the first render shows cyan placeholders while shader variants compile.
            async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void Restore() => ShaderUtil.allowAsyncCompilation = async;

        void Load(QualityTier tier)
        {
            RequireGraphics();
            Game = Game.Create();
            Game.LoadLevel(2);
            level = (Level02ThimbleChasm)Game.Level;
            Presentation = Presentation.Create(Game, new PresentationOptions { Quality = tier });
            Presentation.Frame(0f, 1f);
            // The first picture after a script reload is not to be trusted (Shots.Warm).
            Object.DestroyImmediate(Photograph());
            script = new BotRunner(level.Solve(new Bot(Game)));
        }

        Texture2D Photograph() => Shots.Photograph(Presentation.Camera, Width, Height, TierSpec.Of(Presentation.Context.Quality).Msaa);

        // The picture a failure is about, for looking at: Temp/ToyboxLevel02Look/<tier>-<moment>.png.
        Texture2D Photograph(string moment)
        {
            Texture2D image = Photograph();
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxLevel02Look");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, Presentation.Context.Quality + "-" + moment + ".png"), image.EncodeToPNG());
            return image;
        }

        // The solver plays as it does for Shots.Capture: a tick is a frame.
        bool PlayUntil(System.Func<bool> done, float seconds)
        {
            for (int i = TestHelpers.Ticks(seconds); i > 0 && !done(); i--)
            {
                if (script != null && !script.Advance()) script = null;
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
            }
            Presentation.Frame(0f, 1f);
            return done();
        }

        // Where a point of the world is in the picture, in pixels from its bottom left corner. (Not the
        // camera's own WorldToViewportPoint: outside a render its aspect is the editor window's.)
        Vector2 Pixel(Vector3 world)
        {
            Camera camera = Presentation.Camera;
            Vector3 view = camera.worldToCameraMatrix.MultiplyPoint3x4(world);
            Assert.Less(view.z, -camera.nearClipPlane, "behind the camera: " + world);
            float tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = view.x / (-view.z * tan * Width / Height), y = view.y / (-view.z * tan);
            return new Vector2((x * 0.5f + 0.5f) * Width, (y * 0.5f + 0.5f) * Height);
        }

        static bool Inside(Vector2 pixel, float margin) =>
            pixel.x >= margin && pixel.y >= margin && pixel.x < Width - margin && pixel.y < Height - margin;

        // What the picture shows round a pixel: the mean of a small square.
        static Color Mean(Texture2D image, Vector2 pixel, int reach)
        {
            Assert.IsTrue(Inside(pixel, reach + 1), "not in the picture: " + pixel);
            int cx = Mathf.RoundToInt(pixel.x), cy = Mathf.RoundToInt(pixel.y);
            Color sum = Color.clear;
            for (int y = cy - reach; y <= cy + reach; y++)
                for (int x = cx - reach; x <= cx + reach; x++) sum += image.GetPixel(x, y);
            return sum / ((2 * reach + 1) * (2 * reach + 1));
        }

        static float Value(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        static float Saturation(Color c)
        {
            float max = Value(c), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return max > 0f ? (max - min) / max : 0f;
        }

        static bool White(Color c) => Mathf.Min(c.r, Mathf.Min(c.g, c.b)) >= 0.96f;

        static string Hex(Color c) => Palette.ToHex(new Color(c.r, c.g, c.b, 1f));

        // A point on the thimble's wall where it faces the eye, at a height of the toy's own (scale 1):
        // -0.0375 is between two rows of dimples, -0.32 the middle of the band.
        Vector3 OnTheWall(float height, float radius)
        {
            Prop thimble = level.Thimble;
            Vector3 toEye = Game.Player.Eye - thimble.Position;
            toEye.y = 0f;
            return thimble.Position + (toEye.normalized * radius + Vector3.up * height) * thimble.Scale;
        }

        static void AssertSilver(Color body, string where)
        {
            Assert.Less(Saturation(body), 0.25f, where + ": grey, not a colour (" + Hex(body) + ")");
            Assert.That(Value(body), Is.InRange(0.45f, 0.88f), where + ": pale, but a good way under the border's white (" + Hex(body) + ")");
            Assert.GreaterOrEqual(body.b, body.r - 0.01f, where + ": cool (" + Hex(body) + ")");
        }

        [TestCase(QualityTier.Low)]
        [TestCase(QualityTier.Medium)]
        [TestCase(QualityTier.High)]
        public void TheThimble_ReadsSilver_OnItsSpool_InTheHand_AndPluggingTheWell(QualityTier tier)
        {
            Load(tier);
            Prop thimble = level.Thimble;
            Camera camera = Presentation.Camera;

            // ---- From the spawn: silver on its spool, its candy band under it, the room's blue behind it.
            Texture2D image = Photograph("spawn");
            try
            {
                Color body = Mean(image, Pixel(OnTheWall(-0.0375f, 0.48f)), 2);
                Color band = Mean(image, Pixel(OnTheWall(-0.32f, 0.4895f)), 2);
                Color room = Mean(image, Pixel(thimble.Position + camera.transform.right * thimble.Scale), 6);
                Debug.Log("[Level02] " + tier + ", from the spawn: body " + Hex(body) + ", band " + Hex(band) + ", the wall beside it " + Hex(room));
                AssertSilver(body, "on the spool");
                Assert.Greater(band.r, 0.8f, "the band is Tangerine (" + Hex(band) + ")");
                Assert.Greater(band.r - band.b, 0.5f, "the band is Tangerine (" + Hex(band) + ")");
                Assert.That(band.g, Is.InRange(0.25f, 0.75f), "the band is Tangerine (" + Hex(band) + ")");
                Assert.Greater(Saturation(room), Saturation(body) + 0.15f, "it stands out: grey metal against the dip (" + Hex(body) + " against " + Hex(room) + ")");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }

            // ---- In the hand: the die-cut border is a line round it.
            Assert.IsTrue(PlayUntil(() => Game.Grabber.Held == thimble, 5f), "the solver picks it up");
            PlayUntil(() => false, 0.6f);
            Assert.AreSame(thimble, Game.Grabber.Held);
            image = Photograph("held");
            try
            {
                AssertSilver(Mean(image, Pixel(OnTheWall(-0.0375f, 0.48f)), 2), "in the hand");
                Vector2 centre = Pixel(thimble.Position), top = Pixel(thimble.Position + Vector3.up * (0.4f * thimble.Scale));
                float halfWidth = Mathf.Abs(Pixel(thimble.Position + camera.transform.right * (0.5f * thimble.Scale)).x - centre.x);
                Assert.Greater(halfWidth, 60f, "big enough in the picture to look at");
                // Down five columns across its middle, from the wall behind it into its top: the border's
                // white, then the body. Where the white goes on for more than the border is wide, or the body
                // five pixels under it is all but white too, the body has melted into it. (The window's
                // glint may touch the border in one place: one column of the five, two if it falls between.)
                int melted = 0, columns = 0;
                var runs = new List<int>();
                for (int k = -2; k <= 2; k++)
                {
                    int x = Mathf.RoundToInt(centre.x + k * 0.3f * halfWidth);
                    int from = Mathf.Min(Height - 2, Mathf.RoundToInt(top.y) + 40), to = Mathf.RoundToInt(centre.y);
                    int y = from;
                    while (y > to && !White(image.GetPixel(x, y))) y--;
                    if (y <= to) continue;
                    int run = 0;
                    while (y > to && White(image.GetPixel(x, y)))
                    {
                        run++;
                        y--;
                    }
                    columns++;
                    runs.Add(run);
                    if (run > 10 || Value(image.GetPixel(x, y - 5)) >= 0.88f) melted++;
                }
                Debug.Log("[Level02] " + tier + ", in the hand: the border's white is " + string.Join(", ", runs) + " px deep over the top");
                Assert.AreEqual(5, columns, "the border runs over the whole top");
                Assert.LessOrEqual(melted, 2, "the body melts into the die-cut border");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }

            // ---- Plugging the well, the player on it: turned metal, ring after ring - not one flat grey.
            Assert.IsTrue(PlayUntil(() => level.Well.Seated && Game.Player.Position.z > Level02ThimbleChasm.AxisZ - 5.5f, 20f), "the solver walks onto the plug");
            Assert.AreEqual(11.5f, thimble.Scale, 11.5f * 0.05f);
            image = Photograph("plugged");
            try
            {
                // The middle of every ring of the turning along one line through the axis, 15 degrees off
                // the row of dimples the player walks along (which keeps clear of every dimple but those of
                // the inner ring: that one is left out).
                float s = thimble.Scale;
                var along = new Vector3(Mathf.Cos(105f * Mathf.Deg2Rad), 0f, Mathf.Sin(105f * Mathf.Deg2Rad));
                var shades = new List<float>();
                var seen = new List<string>();
                float jump = 0f, low = 1f, high = 0f, colour = 0f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float previous = -1f;
                    for (int ring = 1; ring < 8; ring++)
                    {
                        if (ring == 2)
                        {
                            previous = -1f;
                            continue;
                        }
                        Vector2 pixel = Pixel(thimble.Position + (along * (side * (0.025f + 0.05f * ring)) + Vector3.up * 0.4f) * s);
                        if (!Inside(pixel, 8f))
                        {
                            previous = -1f;
                            continue;
                        }
                        Color c = Mean(image, pixel, 1);
                        float shade = c.grayscale;
                        shades.Add(shade);
                        seen.Add(Hex(c));
                        colour += Saturation(c);
                        low = Mathf.Min(low, shade);
                        high = Mathf.Max(high, shade);
                        if (previous >= 0f) jump = Mathf.Max(jump, Mathf.Abs(shade - previous));
                        previous = shade;
                    }
                }
                Debug.Log("[Level02] " + tier + ", on the plug: rings " + string.Join(" ", seen) + "; the biggest step between neighbours " + jump.ToString("0.000"));
                Assert.GreaterOrEqual(shades.Count, 5, "rings of the top in the picture");
                // (The rings that mirror the floor take some of its blue; on the whole it is grey.)
                Assert.Less(colour / shades.Count, 0.22f, "grey metal");
                Assert.Greater(low, 0.4f, "pale metal");
                Assert.Greater(jump, 0.03f, "neighbouring rings mirror different parts of the room");
                Assert.Greater(high - low, 0.06f, "not one flat grey");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
