using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 13, "Escaping the Fishbowl": size is capacity. A sponge of scale s holds 0.48 s^3; the bowl
    /// holds 405 and can be waded once 108 of it are gone (1.1 deep), so the sponge has to be 6.09 or more
    /// when it lies in the water, and from 9.5 up it drinks the bowl dry (LEVELS appendix B has 8.4 to 12,
    /// the clamp, for a sponge that holds 0.315 s^3 and a bowl waded at 0.8: see Level13Fishbowl.WadeDepth
    /// and CapacityPerScale3), and anything that fits in the tower wrings enough out of it to lift the cork
    /// to the rim.
    ///
    /// Measured tables: tools/out/notes/level13-probe-*.txt (the explicit Probe tests below write them).
    /// </summary>
    public class Level13Tests : SimTest
    {
        const float Water = Level13Fishbowl.BowlWater;
        static readonly Vector3 Edge = Level13Fishbowl.EdgeSpot;
        static readonly Vector3 Pad = Level13Fishbowl.CorkSpot;
        /// <summary>The middle of the tower's doorway: "toward the glass tower".</summary>
        static readonly Vector3 Door = new Vector3(0f, 1.2f, Level13Fishbowl.TowerFront - Level13Fishbowl.GlassThickness);

        Level13Fishbowl Load()
        {
            Game?.Dispose();
            Game = Game.Create();
            Game.LoadLevel(13);
            return (Level13Fishbowl)Game.Level;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        // A point ten units along the view from an eye at this spot, at this pitch and yaw.
        static Vector3 Along(Vector3 feet, float pitch, float yaw = 0f) =>
            feet + Vector3.up * Player.BaseEyeHeight + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * 10f;

        // Runs a script that may give up half way; says whether it ran to its end.
        bool Attempt(IEnumerator script, float timeoutSeconds = 90f)
        {
            try
            {
                BotRunner.Run(Game, script, timeoutSeconds);
                return true;
            }
            catch (BotException e)
            {
                Debug.Log("[Toybox] the attempt ended: " + e.Message);
                return false;
            }
        }

        // Runs a script for as long as the condition holds (checked before every tick), or to its end.
        static IEnumerator While(Func<bool> condition, IEnumerator script)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(script);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                if (!condition()) yield break;
                yield return null;
            }
        }

        // The first half of the solution with the stand point and the aim of the test's choosing. The sponge
        // is picked up from the start, or from so much farther back.
        static IEnumerator SoakFrom(Bot bot, Level13Fishbowl level, Vector3 stand, Vector3 aim, float back = 0f)
        {
            if (back > 0f)
            {
                yield return bot.WalkTo(Level13Fishbowl.Spawn + Vector3.back * back, 0.03f, 10f);
                yield return bot.LookAt(level.Sponge);
            }
            yield return bot.Grab(level.Sponge);
            if ((stand - bot.Player.Position).magnitude > 0.1f) yield return bot.WalkTo(stand, 0.08f, 10f);
            yield return bot.DropAt(aim);
        }

        // The whole solution with the first let-go of the test's choosing.
        static IEnumerator SolveWith(Bot bot, Level13Fishbowl level, Vector3 stand, Vector3 aim)
        {
            yield return SoakFrom(bot, level, stand, aim);
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return level.Ride(bot);
        }

        // The solution up to the shoe prints on the cork, with the soaked sponge lying in the bowl.
        static IEnumerator ToThePad(Bot bot, Level13Fishbowl level)
        {
            yield return SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint);
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
        }

        // The first thing between two points that is not glass (the bowl's and the tower's glass is see-through), or null.
        string Opaque(Vector3 from, Vector3 to)
        {
            Vector3 direction = (to - from).normalized;
            float left = Vector3.Distance(from, to) - 0.05f;
            Vector3 origin = from;
            for (int i = 0; i < 32 && left > 0.02f; i++)
            {
                if (!Game.PhysicsScene.Raycast(origin, direction, out RaycastHit hit, left, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return null;
                string name = hit.collider.name;
                if (!name.StartsWith("Glass") && !name.StartsWith("Tower")) return name;
                origin = hit.point + direction * 0.02f;
                left -= hit.distance + 0.02f;
            }
            return null;
        }

        // Lets the sponge drink and come to rest (the level's own judgement of it follows within a second).
        void Settle(Level13Fishbowl level, float after = 1.2f)
        {
            TestHelpers.RunUntil(Game, () => !level.Sponge.Held && level.Soak.Activity != SpongeActivity.Soaking && level.Sponge.Velocity.sqrMagnitude < 0.01f, 8f);
            RunSeconds(after);
        }

        // Calls back whenever the sponge stops drinking: it is full, or there is nothing left to drink. (The
        // sponge of the solution holds more than the bowl, so it never reports itself full.)
        void WhenDrunk(Level13Fishbowl level, Action then)
        {
            bool was = false;
            Game.Context.OnUpdate(dt =>
            {
                bool now = level.Soak.Activity == SpongeActivity.Soaking;
                if (was && !now) then();
                was = now;
            });
        }

        // ---- What the level is ------------------------------------------------------------------------------------

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(13));
            Level13Fishbowl level = Load();
            Assert.AreEqual("fishbowl", level.Slug);
            Assert.AreEqual("Escaping the Fishbowl", level.Title);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("cardboard-box", level.Environment);
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(Level13Fishbowl.DenFloorY, level.GroundY, "the den's floor: below the tower's recessed floor (LEVELS has the gravel, y = 0)");
            Assert.Less(level.GroundY, Level13Fishbowl.BedY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.AreEqual("Wind-up toys don't swim. Sponges do the opposite.", level.Blurb, "LEVELS: the blurb");

            Prop sponge = level.Sponge;
            Assert.AreSame(ToyCatalog.HeroOf(13), ToyCatalog.Get(ToyId.Sponge));
            Assert.AreEqual("Sponge", sponge.Name);
            Assert.AreEqual(2f, sponge.Scale, "LEVELS: scale 2.0");
            Assert.AreEqual(0.5f, sponge.MinScale);
            Assert.AreEqual(12f, sponge.MaxScale);
            Assert.AreEqual(GrabPose.Snap90, sponge.GrabPose);
            Assert.IsTrue(sponge.HasTag(Level13Fishbowl.SpongeTag));
            Assert.Less(Vector3.Distance(sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.02f, "LEVELS: centre (0, 3.5, -6.9), on the castle's front edge");
            Assert.AreEqual(1f, Vector3.Dot(sponge.Transform.right, Vector3.right), 1e-4f, "long side toward the start");
            int toys = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) toys++;
            Assert.AreEqual(1, toys, "the sponge is the only thing that can be picked up");

            // The water: 405 in a bowl of area 270 stands 1.5 deep; the tower is empty and holds nothing while its flap is open.
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(270f, level.Bowl.Area);
            Assert.AreEqual(1.5f, level.Bowl.SurfaceY, 1e-4f);
            Assert.AreEqual(0f, level.Tower.Volume);
            Assert.AreEqual(9f, level.Tower.Area);
            Assert.AreEqual(405f, level.TotalWater, 1e-3f);
            Assert.AreEqual(0.48f * 9.5f * 9.5f * 9.5f, Level13Fishbowl.Capacity(9.5f), 1e-3f, "a sponge holds 0.48 per unit of scale cubed (LEVELS: 0.315)");
            Assert.Greater(Level13Fishbowl.Capacity(Level13Fishbowl.IntendedScale), Water, "the sponge the footprint is painted for holds the whole bowl");
            Assert.AreSame(sponge, level.Soak.Prop);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.IsTrue(level.Flap.IsOpen, "the flap starts open");
            Assert.IsTrue(level.Cork.AtRest);
            Assert.AreEqual(0f, level.Cork.TopY, 1e-4f, "the cork's top is level with the gravel");
            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(new Vector3(0f, 9.25f, 13f), Game.Exits[0].Position, "LEVELS: the exit on the books");

            // It lies on the castle and stays there; the player stands a step behind it.
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.02f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.05f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(405f, level.TotalWater, 1e-3f);
            Assert.AreEqual(0, level.Sweeps);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var grabs = new List<float>();
            var drops = new List<float>();
            Game.Events.PropGrabbed += e => grabs.Add(e.OldScale / e.GrabDistance);
            Game.Events.PropDropped += e => drops.Add(e.NewScale);
            float full = -1f, bowlAfter = -1f, wrung = -1f, top = -1f, shut = -1f;
            WhenDrunk(level, () =>
            {
                full = Game.Time;
                bowlAfter = level.Bowl.Depth;
            });
            level.Soak.Wringing += () => wrung = Game.Time;
            level.Flap.Closed += () => shut = Game.Time;
            level.Cork.FloatArrived += () => top = Game.Time;
            float total = 0f, worst = 0f;
            Game.Context.OnUpdate(dt =>
            {
                total = level.TotalWater;
                worst = Mathf.Max(worst, Mathf.Abs(total - Water));
            });

            TestHelpers.PlayLevel(Game, 120f);

            Assert.AreEqual(2, grabs.Count, "two pick-ups");
            Assert.AreEqual(1.2f, grabs[0], 0.02f, "LEVELS: picked up from 1.67 away it looks 1.20 big");
            Assert.AreEqual(Level13Fishbowl.IntendedScale, drops[0], Level13Fishbowl.IntendedScale * 0.05f, "LEVELS appendix B: the soaking sponge is 9.5 within 5 %");
            Assert.AreEqual(0f, bowlAfter, 1e-3f, "the sponge of the solution drinks the whole bowl (LEVELS appendix B has 0.5 left, with a sponge that holds 0.315 per unit cubed)");
            Assert.Greater(level.Soak.CapacityAt(drops[0]), Water);
            Assert.Greater(drops[1], 1.2f);
            Assert.Less(drops[1], 2.4f, "LEVELS appendix B: in the tower it is about 2 (nothing over 2.3 fits)");
            Assert.Greater(shut, wrung, "the flap shut after the wring began");
            Assert.LessOrEqual(shut - wrung, Level13Fishbowl.FlapSeconds + 0.1f);
            Assert.Less(top - wrung, 4f, "LEVELS appendix B: the cork is at the top within 4 s of the wring");
            Assert.Greater(Game.Player.Position.y, Level13Fishbowl.RimY - 0.05f, "the level ends on the books, at the height of the rim");
            Assert.Less(worst, 0.01f, "405 units of water, all the way through (off by " + worst + ")");
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.AreEqual(0, level.Sweeps);
            Assert.AreEqual(0, level.Resets);
            Assert.Less(Game.Time, 30f, "the solution takes well under a minute");
            Debug.Log("[Toybox] level 13 solved in " + F(Game.Time) + " s: k " + F(grabs[0]) + " / " + F(grabs[1]) + ", let go " + F(drops[0]) + " / " + F(drops[1]) +
                      ", full at " + F(full) + " (bowl " + F(bowlAfter) + "), wring " + F(wrung) + ", flap " + F(shut) + ", top " + F(top));
        }

        [Test]
        public void TheSolve_IsTheSameEveryTime()
        {
            Load();
            TestHelpers.PlayLevel(Game, 120f);
            float first = Game.Time;
            Load();
            TestHelpers.PlayLevel(Game, 120f);
            Assert.AreEqual(first, Game.Time, 1e-5f);
        }

        // (Review.) Five times in a row in one game - each after a restart at a different moment of the
        // solution before it - and to the tick the same every time.
        [Test]
        public void TheSolve_FiveTimesInARow_AcrossRestarts_IsTheSameToTheTick()
        {
            Level13Fishbowl level = Load();
            int ticks = -1;
            float[] stopAfter = { 0f, 1.5f, 6f, 10.5f, 13f };
            for (int run = 0; run < 5; run++)
            {
                // Part of the solution first, as far as this many seconds; then the restart key.
                if (stopAfter[run] > 0f)
                {
                    var partly = new Bot(Game);
                    float until = stopAfter[run];
                    BotRunner.Run(Game, While(() => Game.Time < until, level.Solve(partly)), 60f);
                    Assert.IsFalse(Game.LevelCompleted);
                    Game.RestartLevel();
                    level = (Level13Fishbowl)Game.Level;
                }
                List<string> said = Listen();
                int before = Game.LevelTicks;
                Assert.AreEqual(405f, level.TotalWater, 1e-3f);
                Assert.AreEqual(2f, level.Sponge.Scale);
                Assert.IsTrue(level.Flap.IsOpen && level.Cork.AtRest);
                var bot = new Bot(Game);
                BotRunner.Run(Game, level.Solve(bot), 120f);
                TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                Assert.IsTrue(Game.LevelCompleted, "run " + run + " (after a restart at " + stopAfter[run] + " s)");
                Assert.IsEmpty(said, "run " + run);
                int took = Game.LevelTicks - before;
                if (ticks < 0) ticks = took;
                Assert.AreEqual(ticks, took, "run " + run + " took a different number of ticks");
                Assert.AreEqual(405f, level.TotalWater, 0.01f);
                if (run < 4)
                {
                    Game.LoadLevel(13);
                    level = (Level13Fishbowl)Game.Level;
                }
            }
        }

        [Test]
        public void TheStartShowsTheSpongeTheWaterAndTheTower()
        {
            Level13Fishbowl level = Load();
            RunSeconds(0.2f);
            Player player = Game.Player;
            Vector3 eye = player.Eye;

            // The sponge under the crosshair, a step away: the first click is the right one.
            Assert.AreSame(level.Sponge, Game.Grabber.FindTarget(), "a click at the start picks the sponge up");
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, player.Forward, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(level.Sponge, PropRef.Of(hit.collider), "the crosshair is on the sponge");
            Assert.Greater(hit.normal.y, 0.9f, "on its top");
            // (Not on its very edge: a quarter of a unit and more of sponge beyond the crosshair, and to both sides.)
            foreach (Vector3 nudge in new[] { new Vector3(0f, 0f, 0.25f), new Vector3(0.8f, 0f, 0f), new Vector3(-0.8f, 0f, 0f), new Vector3(0f, 0f, -0.8f) })
            {
                Assert.IsTrue(Game.PhysicsScene.Raycast(eye, (hit.point + nudge - eye).normalized, out RaycastHit near, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
                Assert.AreSame(level.Sponge, PropRef.Of(near.collider), "the crosshair is well on the sponge (" + nudge + " from it is not)");
            }
            Assert.AreEqual(1.67f, Vector3.Distance(eye, level.Sponge.Center), 0.02f, "LEVELS: picked up from 1.67 away");

            // The way out is in the first picture: its mark behind the tower's glass, in the middle (clear of
            // the level card at the top left), with nothing but glass in front of its middle and its top.
            // (The picture is 70 degrees high; the books' own edge hides the lowest tenth of the mark.)
            float markHalf = ExitMarks.Fill * Mathf.Min(Level13Fishbowl.ExitSize.x, Mathf.Min(Level13Fishbowl.ExitSize.y, Level13Fishbowl.ExitSize.z)) * 0.5f;
            foreach (Vector3 corner in new[] { new Vector3(-markHalf, markHalf, 0f), new Vector3(markHalf, markHalf, 0f), Vector3.zero, new Vector3(-markHalf, -markHalf * 0.6f, 0f), new Vector3(markHalf, -markHalf * 0.6f, 0f) })
            {
                Vector3 to = Level13Fishbowl.ExitCentre + corner - eye;
                float yaw = Mathf.DeltaAngle(player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - player.Pitch;
                Assert.Less(pitch, 34f, "the way out's mark is cut off by the top of the picture");
                Assert.Less(Mathf.Abs(yaw), 8f, "the way out's mark is in the middle, clear of the level card");
                Assert.IsNull(Opaque(eye, Level13Fishbowl.ExitCentre + corner), "something stands in front of the way out's mark");
            }

            // Above the sponge: the tower's doorway with the flap's lamp over it, and the water in between.
            // (70 degrees up and down, 102 across.)
            float doorZ = Level13Fishbowl.TowerFront - Level13Fishbowl.GlassThickness;
            foreach (Vector3 point in new[]
                     {
                         new Vector3(0f, 1.2f, doorZ), new Vector3(0f, Level13Fishbowl.DoorHeight - 0.1f, doorZ), new Vector3(0f, 4f, doorZ - Level13Fishbowl.BayDepth),
                         new Vector3(-5f, 1.5f, 3f), new Vector3(5f, 1.5f, 3f),
                     })
            {
                Vector3 to = point - eye;
                float yaw = Mathf.DeltaAngle(player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - player.Pitch;
                Assert.Less(Mathf.Abs(yaw), 50f, point + " is off the side of the picture");
                Assert.Less(Mathf.Abs(pitch), 34f, point + " is off the top of the picture");
                bool blocked = Game.PhysicsScene.Raycast(eye, to.normalized, out hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(blocked, point + " is hidden by " + (blocked ? hit.collider.name : ""));
            }

            // From the castle's edge the whole footprint is in view, and so are the doorway and the way out.
            Vector3 edgeEye = Edge + Vector3.up * Player.BaseEyeHeight;
            Vector3 footprint = ToyFactory.SpongeSize * Level13Fishbowl.IntendedScale;
            Vector3 aim = Level13Fishbowl.AimPoint;
            foreach (Vector3 point in new[]
                     {
                         new Vector3(-footprint.x * 0.5f, 0f, aim.z - footprint.z * 0.5f), new Vector3(footprint.x * 0.5f, 0f, aim.z + footprint.z * 0.5f),
                         new Vector3(0f, 1.2f, doorZ), Level13Fishbowl.ExitCentre,
                     })
            {
                Vector3 to = point - edgeEye;
                bool blocked = Game.PhysicsScene.Raycast(edgeEye, to.normalized, out hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                // (Glass is in the way of the exit, and glass is see-through.)
                if (blocked && hit.collider.name.StartsWith("Glass") || blocked && hit.collider.name.StartsWith("Tower")) continue;
                Assert.IsFalse(blocked, "from the castle's edge " + point + " is hidden by " + (blocked ? hit.collider.name : ""));
            }

            // What is only drawn has no colliders, and what stops a toy is drawn.
            foreach (string name in new[] { "Paint", "Stand Mark", "Books", "Tower Glass", "Tower Frame", "Bowl", "Flakes" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
            Assert.Greater(Game.LevelRoot.Find("Solids").GetComponentsInChildren<Collider>(true).Length, 50);
            Assert.AreEqual(0, Game.LevelRoot.Find("Solids").GetComponentsInChildren<Renderer>(true).Length);
        }

        // ---- No way round ---------------------------------------------------------------------------------------

        [Test]
        public void WalkingAndJumpingAtTheWayOut_DoesNotCompleteTheLevel()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();

            // The bot, sent round the sponge and straight at the way out: into the water, and back on the castle.
            var bot = new Bot(Game);
            Assert.IsFalse(Attempt(StraightAtTheWayOut(bot), 30f), "nobody walks to the way out");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.GreaterOrEqual(level.Sweeps, 1);
            Assert.Less(Game.Player.Position.z, 0f);
            int swept = level.Sweeps;

            Input = new ScriptedInput();
            Game.Input = Input;
            float farthest = float.MinValue, highest = float.MinValue;

            // Off the castle toward the tower: walking, sprinting, and hopping all the way (a jump on every landing).
            foreach (int style in new[] { 0, 1, 2 })
            {
                TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
                // (A test fixture's shortcut: the player could have walked there.)
                Game.Player.Teleport(Level13Fishbowl.EdgeSpot, 0f);
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = style > 0;
                for (int i = 0; i < TestHelpers.Ticks(6f); i++)
                {
                    if (style == 2 && Game.Player.Grounded) Input.Once.Jump = true;
                    Game.Tick();
                    Vector3 feet = Game.Player.Position;
                    if (feet.y < 2.5f) farthest = Mathf.Max(farthest, feet.z);
                    highest = Mathf.Max(highest, feet.y);
                }
                Input.Hold = default;
            }
            Assert.IsFalse(Game.LevelCompleted);
            Assert.GreaterOrEqual(level.Sweeps, swept + 3, "every way into the water ends back on the castle");
            Debug.Log("[Toybox] level 13: walking, sprinting and hopping into the water got to z = " + F(farthest));
            Assert.Less(farthest, Level13Fishbowl.TowerFront - Level13Fishbowl.GlassThickness - Level13Fishbowl.BayDepth,
                "nobody wades or hops to the tower through 1.5 of water (got to z = " + farthest + ")");
            Assert.Contains(Level13Fishbowl.DeepLine, said);
            Assert.AreEqual(405f, level.TotalWater, 1e-3f);
            Assert.AreEqual(2f, level.Sponge.Scale);

            // And with the bowl shallow (a test fixture's shortcut: the water taken away), jumping on the cork
            // and against the rim gets nobody over a rim eight units up.
            level.Bowl.Set(100f);
            foreach (Vector3 from in new[] { Pad, new Vector3(0f, 0f, 8f), new Vector3(6f, 0f, 7f), new Vector3(-8f, 0f, 4f) })
            {
                TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
                Game.Player.Teleport(from, 0f);
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                highest = float.MinValue;
                for (int i = 0; i < TestHelpers.Ticks(4f); i++)
                {
                    if (i % 20 == 0) Input.Once.Jump = true;
                    Game.Tick();
                    highest = Mathf.Max(highest, Game.Player.Position.y);
                }
                Input.Hold = default;
                Assert.Less(highest, 2f, "a jump from " + from + " is nowhere near the rim");
                Assert.Less(new Vector2(Game.Player.Position.x, Game.Player.Position.z).magnitude, Level13Fishbowl.BowlRadius, "nobody gets through the glass");
            }
            Assert.IsFalse(Game.LevelCompleted, "the way out is over the rim, and only the cork goes there");
            Assert.IsTrue(level.Cork.AtRest);
        }

        static IEnumerator StraightAtTheWayOut(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(1.6f, 3f, -6.3f), 0.2f, 5f);
            yield return bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 6f, true);
        }

        // ---- The soak: 8.4 to 12 ----------------------------------------------------------------------------------

        // LEVELS appendix B: the soaking sponge works from 8.4 to 12 (the clamp). From the castle's edge that
        // is any pitch from -20 degrees upward; held toward the tower it stops on the tower's bay.
        // (Review, restart 3: with the bowl waded at 1.1 it works from 7.0, which is -28 degrees.)
        // (Restart 4: a sponge holds 0.48 per unit cubed, so it works from 6.1, which is -35 degrees, and from
        // 9.5 up the bowl is drunk dry.)
        [TestCase(-35f, 6.25f, 1.07f)]
        [TestCase(-28f, 7.1f, 0.86f)]
        [TestCase(-25f, 7.56f, 0.73f)]
        [TestCase(-20f, 8.5f, 0.41f)]
        [TestCase(-16f, 9.5f, 0f)]
        [TestCase(-12f, 10.0f, 0f)]
        [TestCase(0f, 9.8f, 0f)]
        [TestCase(15f, 9.8f, 0f)]
        public void FromTheLowEndOfTheWindowUp_TheBowlCanBeWaded_AndTheLevelSolved(float pitch, float scale, float depth)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            float soaked = 0f, bowlAfter = 0f;
            WhenDrunk(level, () =>
            {
                soaked = level.Sponge.Scale;
                bowlAfter = level.Bowl.Depth;
            });
            var bot = new Bot(Game);
            bool ran = Attempt(SolveWith(bot, level, Edge, Along(Edge, pitch)), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.AreEqual(scale, soaked, 0.4f, "the size at " + pitch + " degrees");
            Assert.GreaterOrEqual(soaked, Level13Fishbowl.LeastScale);
            Assert.AreEqual(depth, bowlAfter, 0.12f, "the bowl after the soak");
            Assert.LessOrEqual(bowlAfter, Level13Fishbowl.WadeDepth);
            Assert.IsTrue(ran && Game.LevelCompleted, "a sponge of " + soaked + " should solve the level (bot at " + Game.Player.Position + ")");
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
        }

        // The top of the window: 12, where the sponge stops growing. It takes a pick-up from a step closer
        // than the start and a let-go from the back of the castle, held out level.
        [Test]
        public void AtTheClamp_TwelveDrinksTheBowlDry_AndTheLevelIsSolved()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var grabs = new List<float>();
            Game.Events.PropGrabbed += e => grabs.Add(e.OldScale / e.GrabDistance);
            var bot = new Bot(Game);
            bool ran = Attempt(ClampScript(bot, level), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.Greater(grabs[0], 1.3f, "picked up from right beside it, it looks bigger than from the start");
            Assert.IsTrue(ran && Game.LevelCompleted, "bot at " + Game.Player.Position + ", sponge " + level.Sponge.Scale + " at " + level.Sponge.Center);
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
        }

        static IEnumerator ClampScript(Bot bot, Level13Fishbowl level)
        {
            // Right up to the sponge, then back to the battlements with it, and held out over the bowl.
            yield return bot.WalkTo(new Vector3(0f, 3f, -7.95f), 0.05f, 5f);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 3f, -8.5f), 0.08f, 5f);
            yield return bot.DropAt(Along(bot.Player.Position, 10f));
            Assert.AreEqual(Level13Fishbowl.MaxScale, level.Sponge.Scale, 0.01f, "LEVELS appendix B: 12, the clamp");
            yield return level.Drunk(bot);
            Assert.AreEqual(0f, level.Bowl.Depth, 0.02f, "a sponge of 12 holds more than the bowl");
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return level.Ride(bot);
        }

        // A clearly wrong size: looking down into the water at the castle's foot the sponge comes down at 6.8
        // and holds a quarter of the bowl.
        [TestCase(-40f, 5.8f)]
        [TestCase(-45f, 5.4f)]
        public void ASpongeThatIsTooSmall_LeavesTheBowlTooDeep_AndIsToldSo(float pitch, float scale)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Along(Edge, pitch)), 30f);
            Settle(level);
            Assert.AreEqual(scale, level.Sponge.Scale, 0.3f);
            Assert.Less(level.Sponge.Scale, Level13Fishbowl.LeastScale);
            Assert.AreEqual(Level13Fishbowl.Capacity(level.Sponge.Scale), level.Soak.Stored, 0.5f, "it is full");
            Assert.Greater(level.Bowl.Depth, Level13Fishbowl.WadeDepth, "and the bowl is still too deep");
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.ShortLine }, said);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);

            // Going in anyway: carried back to the castle, and everything is as it was at the start.
            BotRunner.Run(Game, While(() => level.Sweeps == 0, level.ToTheCork(bot)), 30f);
            RunSeconds(0.5f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(1, level.Sweeps, "nobody gets to the tower");
            Assert.AreEqual(1, level.Resets);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f, "back at the start");
            Assert.AreEqual(2f, level.Sponge.Scale, "the sponge is back, as it was");
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(Level13Fishbowl.DeepLine, said[said.Count - 1]);

            // A second try, from scratch, solves it.
            Assert.IsTrue(Attempt(level.Solve(bot), 120f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // "Pick it up again and let go farther out": a sponge that fell short keeps what it drank, and
        // picked up from the castle and held out farther it drinks the rest.
        [Test]
        public void ASpongeThatFellShort_PickedUpAgainAndHeldOutFarther_DrinksTheRest()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Along(Edge, -45f)), 30f);
            Settle(level);
            float first = level.Soak.Stored;
            Assert.Greater(first, 60f);
            BotRunner.Run(Game, Again(bot, level), 30f);
            Assert.Greater(level.Sponge.Scale, Level13Fishbowl.LeastScale);
            Assert.Greater(level.Soak.Stored, first + 80f, "it went on drinking");
            Assert.LessOrEqual(level.Bowl.Depth, Level13Fishbowl.WadeDepth);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.ShortLine }, said, "the second pick-up, from the castle's edge, is close enough");
        }

        // As the line says: picked up again and held out farther, toward the glass tower.
        static IEnumerator Again(Bot bot, Level13Fishbowl level)
        {
            yield return bot.Grab(level.Sponge);
            float held = level.Soak.Stored;
            yield return bot.Wait(0.5f);
            Assert.AreEqual(held, level.Soak.Stored, 1e-3f, "held, it keeps its water");
            yield return bot.DropAt(Door);
            yield return level.Drunk(bot);
        }

        // (Review.) Turned in the hand (the wheel, or Q) by 45 or 60 degrees, the sponge meets the tower's
        // bay with a corner sooner: 8.0 to 8.2. Flipped onto its side (F) it meets the gravel sooner: 8.1.
        // While the bowl had to be 0.8 deep to be waded that was a hair short of the 8.44 it took, and "hold
        // it out farther" was no help because there is no farther. Waded at 1.1 it takes 7.0: turned or
        // flipped and held toward the tower the sponge is big enough, with room to spare, and nothing is said.
        [TestCase(3, 0)]
        [TestCase(-3, 0)]
        [TestCase(-4, 0)]
        [TestCase(0, 1)]
        public void TurnedOrFlippedInTheHand_AndHeldTowardTheTower_ItIsBigEnough(int turns, int flips)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, TurnedLetGo(bot, level, turns, flips, Door), 30f);
            BotRunner.Run(Game, level.Drunk(bot), 30f);
            Settle(level);
            Assert.Less(level.Sponge.Scale, 8.44f, "turned " + turns * 15 + " degrees, flipped " + flips + " times, and held at the tower's door: smaller than it would be flat and square");
            Assert.Greater(level.Sponge.Scale, Level13Fishbowl.LeastScale + 0.5f, "but big enough, and not by a hair");
            Assert.Less(level.Bowl.Depth, Level13Fishbowl.WadeDepth - 0.15f);
            Assert.IsEmpty(said);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);

            // And it is picked up from the castle as easily as it was let go from there.
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            Assert.AreSame(level.Sponge, Game.Grabber.Held);
            Assert.IsEmpty(said);
        }

        // On its side AND held low - the crosshair on the middle of the dashed footprint, where it would be
        // 6.7 lying flat - it is too small (over the water in the middle of the bowl it is 6.4 and will do).
        // What is said is about the way it lies; laid flat again and held out toward the tower, it works.
        [Test]
        public void FlippedOntoItsSide_AndHeldOverTheMiddleOfTheBowl_ItStopsTooSoon_IsToldSo_AndLaidFlatItWorks()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            var middle = new Vector3(0f, 0f, Level13Fishbowl.AimPoint.z);
            BotRunner.Run(Game, TurnedLetGo(bot, level, 0, 1, middle), 30f);
            Settle(level);
            Assert.Less(level.Sponge.Scale, Level13Fishbowl.LeastScale, "on its side, held at the middle of the footprint");
            Assert.Greater(level.Bowl.Depth, Level13Fishbowl.WadeDepth);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.TurnedLine }, said);

            // Picked up again, a second flip lays it flat (the other side up), and held out as the line says.
            BotRunner.Run(Game, TurnedLetGo(bot, level, 0, 1, Door), 30f);
            BotRunner.Run(Game, level.Drunk(bot), 30f);
            Assert.GreaterOrEqual(level.Sponge.Scale, Level13Fishbowl.LeastScale);
            Assert.LessOrEqual(level.Bowl.Depth, Level13Fishbowl.WadeDepth);
            Assert.AreEqual(1, said.Count, "nothing more is said");
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
        }

        // (Review, restart 3.) Where a first-timer holds it: out over the water in the middle of the bowl, or
        // with the crosshair on the far row of the dashes. Waded at 0.8 both were a hair short (7.4 to 7.8 and
        // 7.9 to 8.2 where it took 8.44), the second of them right after being told to hold it out "until it
        // fills the dashes". Both solve the level now, from anywhere along the castle's edge, with nothing said.
        [Test]
        public void HeldOverTheMiddleOfTheBowl_OrAtTheFarDashes_FromAnywhereAlongTheEdge_TheLevelIsSolved()
        {
            var stands = new[] { Edge, new Vector3(0f, 3f, -6.2f), new Vector3(0f, 3f, -6.7f), new Vector3(1.6f, 3f, -6.35f), new Vector3(-1.6f, 3f, -6.35f) };
            // (Restart 4: and with the crosshair on the middle of the dashed footprint, on the gravel - 6.6 to 6.8.)
            var aims = new[]
            {
                new Vector3(0f, 1.5f, 0f), new Vector3(0f, 0f, Level13Fishbowl.AimPoint.z + ToyFactory.SpongeSize.z * Level13Fishbowl.IntendedScale * 0.5f),
                new Vector3(0f, 0f, Level13Fishbowl.AimPoint.z),
            };
            float least = float.MaxValue, deepest = 0f;
            foreach (Vector3 stand in stands)
                foreach (Vector3 aim in aims)
                {
                    Level13Fishbowl level = Load();
                    List<string> said = Listen();
                    float soaked = 0f, bowlAfter = 0f;
                    WhenDrunk(level, () =>
                    {
                        soaked = level.Sponge.Scale;
                        bowlAfter = level.Bowl.Depth;
                    });
                    var bot = new Bot(Game);
                    bool ran = Attempt(SolveWith(bot, level, stand, aim), 120f);
                    TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                    string what = "from " + stand + " held at " + aim + ": " + F(soaked) + ", bowl " + F(bowlAfter);
                    Assert.IsTrue(ran && Game.LevelCompleted, what + " (bot at " + Game.Player.Position + ")");
                    Assert.IsEmpty(said, what);
                    Assert.AreEqual(0, level.Sweeps, what);
                    least = Mathf.Min(least, soaked);
                    deepest = Mathf.Max(deepest, bowlAfter);
                }
            Debug.Log("[Toybox] level 13, the natural aims: least sponge " + F(least) + ", deepest bowl " + F(deepest));
            Assert.Greater(least, Level13Fishbowl.LeastScale + 0.2f, "not by a hair");
            Assert.Less(deepest, Level13Fishbowl.WadeDepth - 0.04f);
        }

        static IEnumerator TurnedLetGo(Bot bot, Level13Fishbowl level, int turns, int flips, Vector3 aim)
        {
            yield return bot.Grab(level.Sponge);
            if ((Edge - bot.Player.Position).magnitude > 0.1f) yield return bot.WalkTo(Edge, 0.08f, 10f);
            yield return bot.RotateHeld(turns, flips);
            yield return bot.DropAt(aim);
        }

        // A sixth of a turn or a quarter of one, and a sponge laid flat the other side up, are no trouble.
        [TestCase(2, 0)]
        [TestCase(6, 0)]
        [TestCase(0, 2)]
        public void TurnedALittleOrAQuarter_OrUpsideDown_ItStillWorks(int turns, int flips)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            Vector3 door = new Vector3(0f, 1.2f, Level13Fishbowl.TowerFront - Level13Fishbowl.GlassThickness);
            BotRunner.Run(Game, TurnedLetGo(bot, level, turns, flips, door), 30f);
            BotRunner.Run(Game, level.Drunk(bot), 30f);
            Assert.GreaterOrEqual(level.Sponge.Scale, Level13Fishbowl.LeastScale);
            Assert.IsEmpty(said);
        }

        // ---- The pick-up ------------------------------------------------------------------------------------------

        // (Restart 4.) From the back corner of the castle the sponge looks 0.83 big instead of 1.2. That was
        // "too far" while it took a sponge of 8.44; it takes 6.1 now, and held toward the tower from the edge
        // this one is 6.6 (far, and turned as well). Nothing is said, and the level is solved with it.
        [Test]
        public void PickedUpFromTheBackOfTheCastle_ItIsStillBigEnough_AndNothingIsSaid()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            float k = 0f, big = 0f;
            Game.Events.PropGrabbed += e =>
            {
                if (k == 0f) k = e.OldScale / e.GrabDistance;
            };
            Game.Events.PropDropped += e =>
            {
                if (big == 0f) big = e.NewScale;
            };
            var bot = new Bot(Game);
            Assert.IsTrue(Attempt(FromTheCornerAndOut(bot, level), 120f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.Less(k, 1.1f, "from the back corner it looks smaller than from the start");
            Assert.Greater(k, Level13Fishbowl.FarRatio + 0.08f, "but not too small (0.83 from the very corner)");
            // (6.6: picked up from the corner it is also turned 35 degrees to the bowl, and a corner of it meets the bay sooner.)
            Assert.Greater(big, Level13Fishbowl.LeastScale + 0.4f, "held toward the tower it is big enough, and not by a hair");
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level13Fishbowl.FarLine, "the campaign's sentence for a pick-up from too far away");

            // Once the bowl has been drunk, a pick-up from far off is no mistake either: nothing is said.
            level = Load();
            said = Listen();
            bot = new Bot(Game);
            BotRunner.Run(Game, ToThePad(bot, level), 60f);
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            Assert.IsEmpty(said);
        }

        static IEnumerator FromTheCornerAndOut(Bot bot, Level13Fishbowl level)
        {
            yield return FromTheCorner(bot, level);
            yield return bot.WalkTo(Edge, 0.08f, 10f);
            yield return bot.DropAt(Door);
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return level.Ride(bot);
        }

        static IEnumerator FromTheCorner(Bot bot, Level13Fishbowl level)
        {
            yield return bot.WalkTo(new Vector3(1.5f, 3f, -8.55f), 0.1f, 10f);
            yield return bot.LookAt(level.Sponge);
            yield return bot.Grab(level.Sponge);
        }

        // ---- What goes wrong, and how it is put right ---------------------------------------------------------------

        // Let go without a step toward the edge, the castle stops the sponge. If it stays on the castle it
        // can be picked up again from right beside it; if it topples into the water at the castle's foot it
        // looks too small from anywhere on the castle, and comes back.
        [TestCase(-25f, false)]
        [TestCase(-20f, true)]
        public void LetGoFromTheMiddleOfTheCastle_TheCastleStopsIt(float pitch, bool returned)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Level13Fishbowl.Spawn, Along(Level13Fishbowl.Spawn, pitch)), 30f);
            Assert.Less(level.Sponge.Scale, 3.2f, "LEVELS: aiming from the middle of the island stops the sponge on the island at about 3");
            RunSeconds(5f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.CastleLine }, said);
            Assert.AreEqual(returned ? 1 : 0, level.Returns);
            if (returned)
            {
                Assert.AreEqual(2f, level.Sponge.Scale, "back where it started");
                Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
                Assert.AreEqual(0f, level.Soak.Stored);
            }
            else
            {
                Assert.Greater(level.Sponge.Center.y, Level13Fishbowl.IslandTop, "it lies on the castle");
                Assert.Greater(level.BestRatio(Game.Player), Level13Fishbowl.UselessRatio);
            }
            Assert.AreEqual(405f, level.TotalWater, 0.01f, "whatever it drank is back in the bowl");
            Assert.AreEqual(0, level.Sweeps);

            // Either way the level is still to be had.
            Assert.IsTrue(Attempt(SolveWith(bot, level, Edge, Level13Fishbowl.AimPoint), 120f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Made small anywhere but in the tower, the full sponge lets its water go where it lies: the bowl
        // fills up again. That is said at once, and picking it up again stops it.
        [Test]
        public void LetGoSmallInTheBowl_TheFullSpongeRefillsIt_AndPickedUpAgainItStops()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheBowl(bot, level), 60f);
            Assert.Less(level.Sponge.Scale, 4f, "let go at the feet it is small");
            RunSeconds(0.3f);
            Assert.AreEqual(SpongeActivity.Wringing, level.Soak.Activity);
            Assert.AreSame(level.Bowl, level.Soak.Over);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.WringLine }, said);
            float before = level.Bowl.Depth;
            RunSeconds(1f);
            Assert.AreEqual(Level13Fishbowl.SqueezeRate / Level13Fishbowl.BowlArea, level.Bowl.Depth - before, 0.01f, "24 units a second into the bowl");
            Assert.IsTrue(level.Flap.IsOpen, "the flap has no business with this");

            // Picked up again in time, it keeps the rest.
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            float kept = level.Soak.Stored, depth = level.Bowl.Depth;
            RunSeconds(2f);
            Assert.AreEqual(kept, level.Soak.Stored, 1e-3f, "a held sponge neither soaks nor leaks");
            Assert.AreEqual(depth, level.Bowl.Depth, 1e-4f);
            Assert.LessOrEqual(depth, Level13Fishbowl.WadeDepth);
            Assert.AreEqual(0, level.Sweeps);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);

            // And the level is still to be had: into the tower with it.
            Assert.IsTrue(Attempt(Finish(bot, level), 60f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "what it kept is plenty for the tower");
        }

        // Out in the bowl beside the tower: the soaked sponge picked up and let go at the feet.
        static IEnumerator InTheBowl(Bot bot, Level13Fishbowl level)
        {
            yield return SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint);
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.WalkTo(new Vector3(0f, 0f, 5.75f), 0.2f, 10f);
            yield return bot.WalkTo(new Vector3(5.5f, 0f, 6.2f), 0.3f, 10f);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(new Vector3(6.2f, 0.3f, 7.4f));
        }

        static IEnumerator Finish(Bot bot, Level13Fishbowl level)
        {
            yield return level.ToTheCork(bot);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return level.Ride(bot);
        }

        [Test]
        public void LeftToItself_TheSpongeInTheBowl_BringsTheWaterBack_AndEverythingIsPutBack()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheBowl(bot, level), 60f);
            bool swept = TestHelpers.RunUntil(Game, () => level.Sweeps > 0, 25f);
            Assert.IsTrue(swept, "the bowl fills up again and the player is out of their depth");
            RunSeconds(0.2f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(1, level.Resets);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(0f, level.Tower.Volume);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.WringLine, Level13Fishbowl.RefillLine }, said);

            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // (Review.) The wind-up goldfish is the one dry perch in the bowl besides the castle and the sponge:
        // 0.85 high, never under water. Somebody who stands on it while the bowl fills up again is not swept
        // away - and is not stuck either: a step off it is a step into deep water, and that puts everything
        // back as always.
        [Test]
        public void StandingOnTheGoldfish_WhileTheBowlFillsUpAgain_IsNoTrap()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint), 30f);
            Settle(level);
            // (A test fixture's shortcut: the player could have waded there and hopped on.)
            Vector3 perch = Level13Fishbowl.FishAt + Quaternion.Euler(0f, Level13Fishbowl.FishYaw, 0f) * new Vector3(0f, 0.9f, -0.25f);
            Game.Player.Teleport(perch, 180f);
            RunSeconds(0.5f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Greater(Game.Player.Position.y, 0.8f, "on the fish");

            // The soaked sponge picked up from there and let go small beside the fish: it wrings itself out.
            BotRunner.Run(Game, Beside(bot, level), 30f);
            Assert.Less(level.Sponge.Scale, 5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Soak.Activity == SpongeActivity.Idle && level.Bowl.Depth > 1.3f, 30f), "the bowl fills up again");
            RunSeconds(3f);
            Assert.AreEqual(0, level.Sweeps, "nobody is swept off the fish");
            Assert.Greater(Game.Player.Position.y, 0.8f);
            // One line for one let-go: what is said when it wrings itself out, and nothing about its size
            // (or the way it lies) once it has come to rest.
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.WringLine }, said);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);

            Debug.Log("[Toybox] level 13, on the fish: sponge " + F(level.Sponge.Scale) + " at " + F(level.Sponge.Center) + ", bowl " + F(level.Bowl.Depth) +
                      ", looks " + F(level.BestRatio(Game.Player)) + ", returns " + level.Returns + ", said " + string.Join(" / ", said));

            // Off the fish: into deep water, back to the castle, everything as it was at the start - and solved from there.
            BotRunner.Run(Game, While(() => level.Sweeps == 0, bot.WalkTo(new Vector3(-6.5f, 0f, 3f), 0.3f, 8f)), 30f);
            RunSeconds(0.2f);
            Assert.AreEqual(1, level.Sweeps);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        static IEnumerator Beside(Bot bot, Level13Fishbowl level)
        {
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(new Vector3(-3.2f, 0.2f, 5.6f));
        }

        // Swept away with the sponge in the hand, the player keeps it - and what it holds.
        [Test]
        public void SweptAwayWithTheSpongeInTheHand_ThePlayerKeepsIt()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheBowl(bot, level), 60f);
            // Picked up again only when the bowl is all but too deep: the water that is out stays out.
            TestHelpers.RunUntil(Game, () => level.Bowl.Depth > Level13Fishbowl.WadeDepth - 0.02f, 25f);
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            // (A test fixture's shortcut: a little more water than there is, to put the player out of their depth.)
            level.Bowl.Add(10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Sweeps > 0, 3f));
            RunSeconds(0.2f);
            Assert.AreSame(level.Sponge, Game.Grabber.Held, "still in the hand");
            Assert.AreEqual(0, level.Resets, "nothing is put back while the sponge is held");
            Assert.Greater(level.Soak.Stored, 100f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);
            Assert.AreEqual(Level13Fishbowl.DeepLine, said[said.Count - 1]);
        }

        // Wrung out in the tower with nobody on the cork: the tower fills, runs over into the bowl, and the
        // player out in the bowl is out of their depth.
        [Test]
        public void WrungOutInTheTowerWithNobodyOnTheCork_TheSurplusRefillsTheBowl()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, Outside(bot, level), 60f);
            Assert.IsTrue(Level13Fishbowl.InTower(level.Sponge.Center), "the sponge went in through the doorway (it is at " + level.Sponge.Center + ", " + level.Sponge.Scale + ")");
            Assert.IsFalse(Level13Fishbowl.InTower(Game.Player.Position));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsClosed, 2f), "the flap shuts: nobody is in its way");
            Assert.IsEmpty(said);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Cork.AtTop, 6f), "the cork goes up by itself");
            // (Review, restart 3.) That is said while they watch it: the bowl is too deep again only eight
            // seconds later, and until then there is nothing they can do (the sponge lies behind glass).
            RunSeconds(0.1f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.TowerLine }, said, "what went wrong is said when the cork is up there without them");
            Assert.AreEqual(0, level.Sweeps);
            float alone = Game.Time;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Sweeps > 0, 14f), "and the surplus refills the bowl");
            Debug.Log("[Toybox] level 13: the bowl was too deep again " + F(Game.Time - alone) + " s after the cork got to the top by itself");
            // (Restart 4.) A sponge that drank the whole bowl would take a quarter of a minute over that at its
            // own pace, with nothing for anybody to do: once the line has been said it lets go four times as fast.
            Assert.Less(Game.Time - alone, 6f, "what comes of it comes while the line is still on the screen");
            Assert.Greater(Game.Time - alone, 2f, "but not before it has been read");
            RunSeconds(0.2f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(1, level.Resets);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.TowerLine, Level13Fishbowl.RefillLine }, said, "one line for what went wrong, one for what came of it");
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(0f, level.Tower.Volume);
            Assert.AreEqual(2f, level.Sponge.Scale);
            // The flap opens again and the cork comes down.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsOpen && level.Cork.AtRest, 6f));

            // And the sponge is its slow self again (at four times the pace the water would outrun the cork and
            // drown its rider): a clean retry rides up as always.
            float top = -1f, wrung = -1f;
            level.Soak.Wringing += () => wrung = Game.Time;
            level.Cork.FloatArrived += () => top = Game.Time;
            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, level.Sweeps);
            Assert.Greater(top - wrung, 3f, "the cork's own pace");
            Assert.AreEqual(2, said.Count, "nothing more is said");
        }

        // From under the bay, a step in front of the doorway: the sponge let go through it onto the cork.
        static IEnumerator Outside(Bot bot, Level13Fishbowl level)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 0f, 5.6f), 0.15f, 10f);
            yield return bot.DropAt(new Vector3(0f, 0.2f, 7.3f));
        }

        // The flap never closes on the player. With the player in the doorway what is wrung into the tower
        // runs straight out into the bowl - and that is said.
        [Test]
        public void StandingInTheDoorway_TheFlapWaits_AndTheWaterRunsOut()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheDoorway(bot, level), 60f);
            Assert.IsTrue(Level13Fishbowl.InTower(level.Sponge.Center), "the sponge is in the tower (at " + level.Sponge.Center + ")");
            RunSeconds(1.5f);
            Assert.IsFalse(level.Flap.IsClosed, "the crush guard: it waits");
            Assert.IsTrue(level.Flap.Waiting);
            Assert.AreEqual(0f, level.Tower.Depth, 0.06f, "nothing stays in the tower");
            Assert.IsTrue(level.Cork.TopY < 0.2f);
            Assert.Contains(Level13Fishbowl.DoorLine, said);
            Assert.Greater(level.Bowl.Depth, 0.1f, "it all runs out into the bowl (which was dry)");

            // Stepping onto the cork lets the flap shut, and what the sponge still holds is enough.
            Assert.IsTrue(Attempt(StepIn(bot, level), 30f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(0, level.Sweeps);
        }

        // (Review: the two lines no test had heard.) Staying in the doorway until the sponge has no more to
        // give: nobody is swept out from under a flap that is waiting for them, but when it gives up waiting
        // the bowl is too deep - swept, told why, and everything is put back.
        [Test]
        public void StayingInTheDoorway_UntilTheSpongeIsSpent_IsSweptAndToldWhy()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheDoorway(bot, level), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Bowl.Depth > Level13Fishbowl.WadeDepth + 0.1f, 25f), "the water runs out under the open flap");
            Assert.AreEqual(0, level.Sweeps, "the flap is waiting for them: they are not swept out from under it");
            Assert.IsTrue(level.Flap.Waiting);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Sweeps > 0, 25f), "until the sponge is spent and the flap gives up");
            RunSeconds(0.2f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.DoorLine, Level13Fishbowl.SpillLine }, said);
            Assert.AreEqual(1, level.Resets);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(0f, level.Tower.Volume);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);
            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // (Review.) The low end of the window (7.1: the bowl is left 1.08 deep) and the sponge let go from the
        // doorway: the bowl is too deep again within a tenth of a second. Nobody is swept out from under a
        // flap that is waiting for them: they are told to step onto the cork. But a sponge from the low end
        // has little to spare - it holds 113 units and the tower takes 91 - and after two seconds under
        // an open flap what is left does not lift the cork to the rim. It comes down again, and what
        // is said then is what happened.
        [Test]
        public void AtTheLowEnd_LetGoFromTheDoorway_TheyAreTold_AndWhatIsLeftDoesNotReachTheRim()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float soaked = 0f;
            WhenDrunk(level, () => soaked = level.Sponge.Scale);
            BotRunner.Run(Game, LowEndInTheDoorway(bot, level), 60f);
            Assert.AreEqual(6.25f, soaked, 0.15f, "the low end");
            RunSeconds(2f);
            Assert.Greater(level.Bowl.Depth, Level13Fishbowl.WadeDepth, "the bowl is too deep again");
            Assert.AreEqual(0, level.Sweeps);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.DoorLine }, said);

            // A step in: the flap shuts and the cork goes up - part of the way.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 7.1f), 0.12f, 4f), 10f);
            float highest = 0f;
            bool swept = false;
            for (int i = 0; i < TestHelpers.Ticks(30f) && !swept; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, level.Cork.TopY);
                swept = level.Sweeps > 0;
            }
            Debug.Log("[Toybox] level 13, the low end after a pause in the doorway: the cork got to " + F(highest));
            Assert.Greater(highest, 2f, "the flap shut and the cork went up");
            Assert.Less(highest, Level13Fishbowl.RimY - Level13Fishbowl.StepOff, "but not to where a hop reaches the rim");
            Assert.IsTrue(swept, "down again, and the flap opens on deep water");
            RunSeconds(0.2f);
            Assert.IsFalse(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.DoorLine, Level13Fishbowl.LowLine }, said);
            Assert.AreEqual(1, level.Resets);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(0f, level.Tower.Volume);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);

            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // A shorter pause, and the cork stops short of the rim but within a hop of it: it lies still there for
        // three seconds (the tower holds what it has), and a hop gets them out. No trap, and no reset either.
        [Test]
        public void AtTheLowEnd_AfterAShortPauseInTheDoorway_TheCorkStopsAHopBelowTheRim_AndAHopGetsThemOut()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, LowEndInTheDoorway(bot, level), 60f);
            RunSeconds(1.4f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 7.1f), 0.12f, 4f), 10f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsClosed && level.Soak.Activity == SpongeActivity.Idle, 10f), "the flap shut and the sponge ran dry");
            RunSeconds(0.5f);
            float stopped = level.Cork.TopY;
            Debug.Log("[Toybox] level 13, the low end after a short pause in the doorway: the cork stopped at " + F(stopped));
            Assert.IsFalse(level.Cork.AtTop);
            Assert.Greater(stopped, Level13Fishbowl.RimY - 1.1f, "within a hop of the rim");
            Assert.Less(stopped, Level13Fishbowl.RimY - 0.3f, "and not a step from it");
            RunSeconds(1.5f);
            Assert.AreEqual(stopped, level.Cork.TopY, 0.02f, "it lies still while the tower holds its water");
            Vector3 lay = level.Sponge.Center;
            float size = level.Sponge.Scale;
            Assert.IsTrue(Attempt(HopOut(bot), 20f), "bot at " + Game.Player.Position + ", cork " + level.Cork.TopY + ", sponge " + level.Sponge.Scale + " at " + level.Sponge.Center +
                                                    " (it lay at " + lay + ", " + size + "; said: " + string.Join(" / ", said) + ")");
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(0, level.Sweeps);
            Assert.AreEqual(0, level.Resets);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.DoorLine }, said);
        }

        // Toward the way out, hopping until the books are underfoot: onto the sponge that lies in the way, and
        // on over the rim - or down behind the sponge onto the cork again, and over the rim from there.
        static IEnumerator HopOut(Bot bot)
        {
            Game game = bot.Game;
            Player player = bot.Player;
            yield return bot.LookAt(Level13Fishbowl.ExitCentre);
            for (int hop = 0; hop < 6 && player.Position.z < Level13Fishbowl.LedgeNear; hop++)
            {
                yield return bot.Jump();
                bool left = false;
                int ticks = 0;
                yield return While(() =>
                {
                    if (!player.Grounded) left = true;
                    return !(left && player.Grounded) && ticks++ < 90;
                }, bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 8f));
            }
            yield return bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 8f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // The same sponge with no pause: on the cork, picked up through the doorway and let go at the feet. It
        // is enough, and the cork is at the rim.
        [Test]
        public void AtTheLowEnd_LetGoOnTheCork_ThereIsEnoughForTheTower()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float stored = 0f, left = -1f;
            level.Soak.Wringing += () => stored = level.Soak.Stored;
            level.Cork.FloatArrived += () => left = level.Soak.Stored;
            Assert.IsTrue(Attempt(SolveWith(bot, level, Edge, Along(Edge, LowEndPitch)), 120f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.Less(stored, 130f, "a sponge from the low end");
            Assert.Greater(left, 10f, "with something to spare when the cork is at the rim (it had " + stored + ")");
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
        }

        /// <summary>From the castle's edge, the pitch that gives the least sponge that will do: 6.25 (the bowl is left 1.07 deep).</summary>
        const float LowEndPitch = -35f;

        static IEnumerator LowEndInTheDoorway(Bot bot, Level13Fishbowl level)
        {
            yield return SoakFrom(bot, level, Edge, Along(Edge, LowEndPitch));
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 0f, Level13Fishbowl.TowerFront - 0.15f), 0.08f, 10f);
            yield return bot.DropAt(new Vector3(0f, 0.3f, 8.6f));
        }

        // Too far takes two steps now: let go at the feet on the castle it is a crumb (the castle stopped it:
        // told), and picked up again from the back corner that crumb looks a third as big as it did (told: the
        // campaign's sentence). Held out over the bowl all the same it lies there at 3 or so, and from the
        // castle it can never look bigger than it did. It comes back, and that is said.
        [Test]
        public void PickedUpFromFarOff_AndHeldOutAllTheSame_ItComesBack_AndThatIsSaid()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            var bot = new Bot(Game);
            BotRunner.Run(Game, FarAndOut(bot, level), 30f);
            Assert.Less(k, Level13Fishbowl.FarRatio, "the second pick-up, from the back corner");
            Assert.Less(level.Sponge.Scale, Level13Fishbowl.LeastScale - 1f);
            Assert.Less(level.Sponge.Center.y, Level13Fishbowl.IslandTop - 0.5f, "out in the bowl (it is " + level.Sponge.Scale + " at " + level.Sponge.Center + ")");
            RunSeconds(5f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.CastleLine, Level13Fishbowl.FarLine, Level13Fishbowl.CrumbLine }, said);
            Assert.AreEqual(1, level.Returns);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
            Assert.AreEqual(0, level.Sweeps);
        }

        static IEnumerator FarAndOut(Bot bot, Level13Fishbowl level)
        {
            var corner = new Vector3(-1.2f, 3f, -6.7f);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(corner, 0.08f, 10f);
            yield return bot.DropAt(corner + new Vector3(0f, 0.2f, 0.25f));
            yield return bot.Wait(1.2f);
            yield return FromTheCorner(bot, level);
            yield return bot.WalkTo(Edge, 0.08f, 10f);
            yield return bot.DropAt(Level13Fishbowl.AimPoint);
        }

        static IEnumerator InTheDoorway(Bot bot, Level13Fishbowl level)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 0f, Level13Fishbowl.TowerFront - 0.15f), 0.08f, 10f);
            yield return bot.DropAt(new Vector3(0f, 0.3f, 8.6f));
        }

        // A step in, onto what the sponge leaves of the cork behind the flap. At the top the sponge lies
        // between the player and the way out: a hop onto it, and over.
        static IEnumerator StepIn(Bot bot, Level13Fishbowl level)
        {
            Game game = bot.Game;
            yield return bot.WalkTo(new Vector3(0f, 0f, 7.1f), 0.12f, 4f);
            yield return bot.Until(() => level.Cork.AtTop, 12f);
            yield return bot.LookAt(Level13Fishbowl.ExitCentre);
            yield return bot.Jump(false);
            yield return bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 8f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // Up with the cork and no step off at the top: when the sponge has no more to give, the tower runs
        // empty again, the cork comes down, the flap opens on a bowl that is too deep - and everything goes back.
        [Test]
        public void StayingOnTheCorkAtTheTop_ThePlayerComesDownAgain_AndEverythingIsPutBack()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, HalfWayUp(bot, level), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Cork.AtTop, 6f));
            float arrived = Game.Time;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !level.Cork.AtTop, 40f), "the sponge runs out and the tower leaks");
            Assert.Greater(Game.Time - arrived, 5f, "there is time enough to step off");
            Assert.Greater(level.Bowl.Depth, Level13Fishbowl.WadeDepth, "the surplus has refilled the bowl meanwhile");
            Assert.IsEmpty(said, "nothing is said about a bowl the player is not standing in");
            Assert.AreEqual(0, level.Returns);
            // While a hop still reaches the rim the cork sinks at the leak's pace (a third of a unit a second):
            // nearly four seconds more to get off.
            float left = Game.Time;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Cork.TopY < Level13Fishbowl.RimY - Level13Fishbowl.StepOff, 10f));
            Assert.Greater(Game.Time - left, 3f, "the slow way down as far as a hop reaches");
            // (Review.) After that nobody waits 20 s in a glass tube: the tower runs empty as fast as it
            // filled, and the rider stays on the cork all the way down.
            float below = Game.Time, gap = 0f;
            bool swept = false;
            for (int i = 0; i < TestHelpers.Ticks(8f) && !swept; i++)
            {
                Game.Tick();
                swept = level.Sweeps > 0;
                if (!swept) gap = Mathf.Max(gap, Game.Player.Position.y - level.Cork.TopY);
            }
            Assert.IsTrue(swept, "down again, and the flap opens on deep water");
            Assert.Less(Game.Time - below, 6f, "the way down is short");
            Assert.Less(gap, 0.25f, "the rider stays on the cork on the way down (they were " + gap + " above it)");
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
            RunSeconds(0.2f);
            Assert.IsFalse(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.LateLine }, said);
            Assert.AreEqual(1, level.Resets);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.3f);

            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // ---- The wring ----------------------------------------------------------------------------------------------

        // (Review, restart 4.) The solution's own two let-gos, each a little off, as a hand on a mouse is: the
        // stand point a third of a unit beside the shoe prints or short of them, the first aim a unit and a
        // half to either side and half a unit up or down, the second a third of a unit off. Every one of them
        // drinks most or all of the bowl (8.1 to 9.9: 0.55 deep at most) and solves the level, with nothing said.
        [Test]
        public void TheSolutionsLetGos_ALittleOff_StillSolveIt()
        {
            var stands = new[] { Edge, Edge + new Vector3(0.35f, 0f, 0f), Edge + new Vector3(-0.35f, 0f, -0.25f) };
            var firsts = new[] { new Vector3(1.5f, 0f, 0f), new Vector3(-1.5f, 0.5f, 0.8f), new Vector3(0f, -0.6f, -0.8f) };
            var seconds = new[] { Vector3.zero, new Vector3(0.3f, 0f, 0.25f), new Vector3(-0.3f, 0.2f, -0.3f) };
            float least = float.MaxValue, deepest = 0f;
            for (int i = 0; i < stands.Length; i++)
                for (int j = 0; j < firsts.Length; j++)
                {
                    Level13Fishbowl level = Load();
                    List<string> said = Listen();
                    float soaked = 0f, bowlAfter = 0f;
                    WhenDrunk(level, () =>
                    {
                        soaked = level.Sponge.Scale;
                        bowlAfter = level.Bowl.Depth;
                    });
                    var bot = new Bot(Game);
                    Vector3 first = Level13Fishbowl.AimPoint + firsts[j], second = Level13Fishbowl.CorkAim + seconds[(i + j) % seconds.Length];
                    bool ran = Attempt(ALittleOff(bot, level, stands[i], first, second), 120f);
                    TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                    string what = "from " + stands[i] + " held at " + first + ", then let go at " + second + ": " + F(soaked) + ", bowl " + F(bowlAfter);
                    Assert.IsTrue(ran && Game.LevelCompleted, what + " (bot at " + Game.Player.Position + ", sponge " + level.Sponge.Scale + " at " + level.Sponge.Center + ")");
                    Assert.IsEmpty(said, what);
                    Assert.AreEqual(0, level.Sweeps, what);
                    Assert.AreEqual(405f, level.TotalWater, 0.01f, what);
                    least = Mathf.Min(least, soaked);
                    deepest = Mathf.Max(deepest, bowlAfter);
                }
            Debug.Log("[Toybox] level 13, the solution a little off: least sponge " + F(least) + ", deepest bowl " + F(deepest));
            // (The smallest is the aim half a unit low and short: 8.1, a third over what it takes.)
            Assert.Greater(least, Level13Fishbowl.LeastScale + 1.5f, "all of them far above the least sponge that will do");
            Assert.Less(deepest, Level13Fishbowl.WadeDepth - 0.4f, "and the bowl far from too deep");
        }

        static IEnumerator ALittleOff(Bot bot, Level13Fishbowl level, Vector3 stand, Vector3 first, Vector3 second)
        {
            yield return SoakFrom(bot, level, stand, first);
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(second);
            yield return level.Ride(bot);
        }

        // LEVELS appendix B: wringing works from 0.5 (the clamp) to about 2.3 (what fits in the tower). From
        // the shoe prints: looking straight down it is at its smallest, looking out level it is as big as
        // the tower lets it be.
        [TestCase(-80f)]
        [TestCase(-41f)]
        [TestCase(-15f)]
        [TestCase(5f)]
        public void WhateverFitsInTheTower_LiftsTheCorkToTheRim(float pitch)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float small = 0f;
            Game.Events.PropDropped += e => small = e.NewScale;
            Assert.IsTrue(Attempt(WringAt(bot, level, pitch), 120f), "bot at " + Game.Player.Position + ", sponge " + level.Sponge.Scale + " at " + level.Sponge.Center);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "a sponge of " + small + " in the tower");
            Assert.GreaterOrEqual(small, Level13Fishbowl.MinScale);
            Assert.Less(small, 2.6f, "nothing bigger fits in there");
            Assert.IsEmpty(said);
        }

        static IEnumerator WringAt(Bot bot, Level13Fishbowl level, float pitch)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Pad + Vector3.up * Player.BaseEyeHeight + Quaternion.Euler(-pitch, 180f, 0f) * Vector3.forward * 10f);
            yield return level.Ride(bot);
        }

        // (Review.) On the cork but not at its back, looking out through the doorway: the sponge used to come
        // down in the doorway or in front of it, where it counted as lying in the bowl - it wrung itself out
        // into the bowl with its holder on the cork, and everything was put back. From inside the tower it
        // can only be let go inside the tower now.
        [TestCase(7.6f, 1.2f, 5.0f)]
        [TestCase(7.6f, 0.3f, 5.9f)]
        [TestCase(7.3f, 1.2f, 5.0f)]
        [TestCase(7.1f, 0.3f, 6.2f)]
        [TestCase(7.1f, 1.2f, 6.5f)]
        [TestCase(8.4f, 2.0f, 4.0f)]
        public void LetGoFromAnywhereOnTheCork_LookingOutThroughTheDoorway_TheSpongeStaysInTheTower(float standZ, float aimY, float aimZ)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float small = 0f;
            Game.Events.PropDropped += e => small = e.NewScale;
            BotRunner.Run(Game, FromTheCork(bot, level, standZ, new Vector3(0f, aimY, aimZ)), 90f);
            Assert.IsTrue(Level13Fishbowl.InTower(level.Sponge.Center), "let go from z = " + standZ + " toward " + aimZ + " the sponge (" + small + ") is at " + level.Sponge.Center);
            float front = float.MaxValue;
            foreach (Collider collider in level.Sponge.Colliders) front = Mathf.Min(front, collider.bounds.min.z);
            Assert.GreaterOrEqual(front, Level13Fishbowl.TowerFront - 0.02f, "all of it is inside: nothing lies in the doorway, under the flap");
            RunSeconds(0.2f);
            Assert.AreSame(level.Tower, level.Soak.Over);
            Assert.AreEqual(SpongeActivity.Wringing, level.Soak.Activity);
            Assert.IsTrue(Attempt(level.Ride(bot), 30f), "bot at " + Game.Player.Position);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
        }

        static IEnumerator FromTheCork(Bot bot, Level13Fishbowl level, float standZ, Vector3 aim)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 0f, standZ), 0.08f, 10f);
            yield return bot.DropAt(aim);
        }

        // The guard is the player's own business only: it is there while the sponge is in the hand of
        // somebody in the tower, and never in their way.
        [Test]
        public void TheDoorwaysGuard_IsOnlyThereForASpongeHeldInTheTower_AndNeverStopsThePlayer()
        {
            Level13Fishbowl level = Load();
            Transform door = Game.LevelRoot.Find("Doorway Guard");
            Assert.IsNotNull(door);
            var box = door.GetComponent<BoxCollider>();
            Assert.IsFalse(box.enabled, "at the start it is not there");
            Assert.AreEqual(Layers.PlayerMask, box.excludeLayers.value, "the player's capsule never touches it");
            Assert.AreEqual(0, door.GetComponentsInChildren<Renderer>(true).Length);
            var bot = new Bot(Game);
            BotRunner.Run(Game, ToThePad(bot, level), 60f);
            Assert.IsFalse(box.enabled, "in the tower with empty hands: the sponge outside can be picked up through the doorway");
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            RunSeconds(0.1f);
            Assert.IsTrue(box.enabled, "holding it in the tower");
            // Out through the doorway with it, and in again.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 5.6f), 0.15f, 5f), 10f);
            Assert.IsFalse(box.enabled, "outside it is not there: the sponge can be let go anywhere, in through the doorway too");
            BotRunner.Run(Game, bot.WalkTo(Pad, 0.2f, 5f), 10f);
            Assert.IsTrue(box.enabled);
            BotRunner.Run(Game, bot.DropAt(Level13Fishbowl.CorkAim), 10f);
            RunSeconds(0.1f);
            Assert.IsFalse(box.enabled, "let go: gone again");
            Assert.IsTrue(Attempt(level.Ride(bot), 30f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // (Review.) Up at the rim the level is as good as done. With a sponge from the low end of the window
        // the bowl is too deep again before the player steps off the cork, and the rule that brings back a
        // sponge "that looks too small from the castle" used to put everything back behind them, with the
        // line about the tower running over without them.
        [Test]
        public void OnTheBooksBesideTheWayOut_NothingIsPutBack_AndNothingIsSaid()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, Dawdle(bot, level), 120f);
            Assert.Greater(level.Bowl.Depth, Level13Fishbowl.WadeDepth, "the bowl is too deep again behind them");
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Resets);
            Assert.AreEqual(0, level.Returns);
            Assert.AreEqual(0, level.Sweeps);
            Assert.Less(level.Sponge.Scale, 2.6f, "the sponge is where it was let go, not back on the castle");
            Assert.IsTrue(Level13Fishbowl.InTower(level.Sponge.Center));
            Assert.IsTrue(Attempt(ToTheWayOut(bot), 20f));
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
        }

        static IEnumerator Dawdle(Bot bot, Level13Fishbowl level)
        {
            // The low end: 8.5, and the bowl left 0.78 deep.
            yield return SoakFrom(bot, level, Edge, Along(Edge, -20f));
            yield return level.Drunk(bot);
            yield return level.ToTheCork(bot);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return bot.Until(() => level.Cork.AtTop, 10f);
            // Off the cork onto the books, beside the way out, and a good look round.
            yield return bot.WalkTo(new Vector3(0f, 8f, 10.9f), 0.2f, 6f);
            yield return bot.WalkTo(new Vector3(2.2f, 8f, 10.9f), 0.2f, 6f);
            yield return bot.Wait(6f);
        }

        static IEnumerator ToTheWayOut(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 6f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // (Review.) At the top with the sponge between the player and the way out (let go ahead of them on the
        // way in): walking into it shoves it out onto the books, a hop goes over it, and picked up it comes
        // along. The last used to fail: a held sponge feeds nothing, the cork sank at once, and a third of a
        // second later the rim was a kerb. The tower holds its water for three seconds now.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AtTheTopWithTheSpongeInTheWay_ShovedHoppedOverOrPickedUp_ThePlayerGetsOut(int how)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, InTheWay(bot, level), 90f);
            Assert.IsTrue(level.Cork.AtTop);
            Assert.Greater(level.Sponge.Center.z, Game.Player.Position.z + 0.5f, "the sponge lies between the player and the way out");
            float arrived = Game.Time;
            Assert.IsTrue(Attempt(Out(bot, level, how), 30f), "bot at " + Game.Player.Position + ", cork " + level.Cork.TopY + ", sponge " + level.Sponge.Scale + " at " + level.Sponge.Center);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
            Assert.AreEqual(0, level.Sweeps);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
            Debug.Log("[Toybox] level 13: out at the top (" + how + ") " + F(Game.Time - arrived) + " s after the cork arrived");
        }

        // Carried in and let go ahead, toward the back of the tower, from just inside the doorway.
        static IEnumerator InTheWay(Bot bot, Level13Fishbowl level)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.WalkTo(new Vector3(0f, 0f, 7.35f), 0.1f, 10f);
            yield return bot.DropAt(new Vector3(0f, 0.3f, 8.9f));
            yield return bot.Until(() => level.Cork.AtTop, 12f);
        }

        static IEnumerator Out(Bot bot, Level13Fishbowl level, int how)
        {
            Game game = bot.Game;
            if (how == 1)
            {
                yield return bot.LookAt(Level13Fishbowl.ExitCentre);
                yield return bot.Jump(false);
            }
            if (how == 2)
            {
                yield return bot.Grab(level.Sponge);
                // A look round with it in the hand: the cork stays where it is meanwhile.
                yield return bot.Wait(1.5f);
                Assert.IsTrue(level.Cork.AtTop, "the tower holds its water for a moment after the sponge stops");
            }
            yield return bot.WalkTo(Level13Fishbowl.ExitCentre, 0.5f, 8f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // The hold is three seconds and no more: after it the tower leaks as before.
        [Test]
        public void TheTowerHoldsItsWaterForThreeSeconds_AfterTheSpongeStops()
        {
            Level13Fishbowl level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, HalfWayUp(bot, level), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Cork.AtTop, 6f));
            int arrivals = 0;
            level.Cork.FloatArrived += () => arrivals++;
            BotRunner.Run(Game, bot.Grab(level.Sponge), 10f);
            float held = Game.Time, depth = level.Tower.Depth;
            RunSeconds(Level13Fishbowl.HoldSeconds - 0.2f);
            Assert.IsTrue(level.Cork.AtTop, "it holds");
            Assert.AreEqual(depth, level.Tower.Depth, 0.01f);
            Assert.AreEqual(0, arrivals, "and the cork lies still meanwhile (it did not leave and arrive again)");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !level.Cork.AtTop, 1f), "then it leaks");
            Assert.AreEqual(Level13Fishbowl.HoldSeconds, Game.Time - held, 0.3f);
            float before = level.Tower.Volume;
            RunSeconds(1f);
            Assert.AreEqual(Level13Fishbowl.LeakRate, before - level.Tower.Volume, 0.1f, "three units a second, as before");
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
            Assert.AreSame(level.Sponge, Game.Grabber.Held);
        }

        // The rule behind it, at both ends of the window and outside it: the tower holds 71 units up to its
        // lip and loses 3 a second, the flap takes 0.4 s to shut, and the sponge wrings 24 a second.
        [Test]
        public void TheWringWindow_IsWhatTheTowerNeeds()
        {
            float need = Level13Fishbowl.TowerLip * Level13Fishbowl.TowerArea;
            Assert.AreEqual(71.1f, need, 0.01f);
            float lost = Level13Fishbowl.FlapSeconds * Level13Fishbowl.SqueezeRate;
            float seconds = need / (Level13Fishbowl.SqueezeRate - Level13Fishbowl.LeakRate);
            Assert.Less(Level13Fishbowl.FlapSeconds + seconds, 4f, "LEVELS appendix B: at the top within 4 s");
            Assert.Less(Level13Fishbowl.SqueezeRate / Level13Fishbowl.TowerArea, Level13Fishbowl.CorkSpeed, "the water never outruns the cork (LEVELS: 40 a second drowned its rider)");
            float needed = lost + seconds * Level13Fishbowl.SqueezeRate;
            float soaked = Level13Fishbowl.Capacity(Level13Fishbowl.LeastScale);
            Assert.GreaterOrEqual(soaked, Water - Level13Fishbowl.WadeDepth * Level13Fishbowl.BowlArea, "6.09 is what makes the bowl wadeable");
            Assert.Less(Level13Fishbowl.Capacity(Level13Fishbowl.LeastScale - 0.01f), Water - Level13Fishbowl.WadeDepth * Level13Fishbowl.BowlArea, "and nothing less");
            Assert.AreEqual(90.9f, needed, 0.1f, "what the tower takes: 71 up to its lip, 10 that leak out meanwhile, 10 under the closing flap");
            Assert.Greater(soaked - needed, 15f, "the least sponge that drains the bowl has that to spare");
            foreach (float s in new[] { Level13Fishbowl.MinScale, 1f, 2f, 2.3f })
                Assert.Greater(soaked - Level13Fishbowl.Capacity(s), needed, "the smallest sponge that drains the bowl, let go at " + s);
            // A sponge that is not made smaller wrings nothing at all.
            Assert.LessOrEqual(soaked - Level13Fishbowl.Capacity(Level13Fishbowl.LeastScale), 0f);
        }

        // The low end by itself: a sponge at the clamp, 0.5, on the cork beside the player (set there by
        // the test: from inside the bowl nobody is far enough away to make it that small).
        [Test]
        public void AtTheLowEnd_HalfAUnitOfSponge_StillLiftsTheCork()
        {
            Level13Fishbowl level = Load();
            var bot = new Bot(Game);
            BotRunner.Run(Game, ToThePad(bot, level), 60f);
            float stored = level.Soak.Stored;
            level.Sponge.SetScale(Level13Fishbowl.MinScale);
            level.Sponge.SetPose(new Vector3(0f, 0.2f, 7.8f), Quaternion.identity);
            Assert.AreEqual(stored, level.Soak.Stored);
            Assert.IsTrue(Attempt(level.Ride(bot), 30f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(405f, level.TotalWater, 0.01f);
        }

        // ---- No strand, no restart ------------------------------------------------------------------------------------

        [Test]
        public void ASpongeThatFallsOutOfTheWorld_ComesBack()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint), 30f);
            Settle(level);
            Assert.Greater(level.Soak.Stored, 250f);

            // Below the kill plane (set there by the test: nothing in the level leads there).
            level.Sponge.SetPose(new Vector3(30f, level.KillY - 5f, 0f), Quaternion.identity);
            RunSeconds(0.1f);
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f, "back on the castle");
            Assert.AreEqual(0f, level.Soak.Stored, "and its water is back in the bowl");
            Assert.AreEqual(405f, level.Bowl.Volume, 0.01f);

            // And the level is solved from there.
            Assert.IsTrue(Attempt(level.Solve(bot), 120f));
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // The grab ray stops at glass: a sponge outside the bowl could never be picked up again.
        [TestCase(0f, 8.6f, 12.5f)]
        [TestCase(14f, 0.5f, 0f)]
        [TestCase(0.5f, 8.4f, 10.2f)]
        public void ASpongeBehindGlass_ComesBack_AndItsWaterWithIt(float x, float y, float z)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint), 30f);
            Settle(level);
            float stored = level.Soak.Stored;

            // On the books, on the den's floor, on the rim behind the tower (set there by the test).
            level.Sponge.SetScale(1.5f);
            level.Sponge.SetPose(new Vector3(x, y, z), Quaternion.identity);
            RunSeconds(2.5f);
            Assert.AreEqual(0, level.Leash.Returns, "it has three seconds");
            RunSeconds(1.5f);
            Assert.AreEqual(1, level.Leash.Returns);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(405f, level.TotalWater, 0.01f, "nothing is lost (it held " + stored + ")");
            Assert.Contains(Level13Fishbowl.BackLine, said);
        }

        // In the tower with the player high and dry on the castle: it wrings itself out in there, the
        // surplus refills the bowl, and nobody can wade to it any more. Everything goes back.
        [Test]
        public void ASmallSpongeInTheTower_WithThePlayerOnTheCastle_PutsEverythingBack()
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint), 30f);
            Settle(level);
            // (Set there by the test: from the castle it takes a sponge that looks tiny to get it through the doorway.)
            level.Sponge.SetScale(1.5f);
            level.Sponge.SetPose(new Vector3(0f, 0.5f, 8.2f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsClosed, 2f), "the flap shuts");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Cork.AtTop, 6f), "the cork goes up with the sponge on it");
            RunSeconds(0.1f);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.TowerLine }, said, "said while it is up there");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Resets > 0, 14f), "the bowl is too deep again, and the sponge is out of reach");
            RunSeconds(0.2f);
            Assert.AreEqual(0, level.Sweeps, "the player never left the castle");
            Assert.AreEqual(1, level.Returns);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.Less(Vector3.Distance(level.Sponge.Center, new Vector3(0f, 3.5f, -6.9f)), 0.05f);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(0f, level.Tower.Volume);
            CollectionAssert.AreEqual(new[] { Level13Fishbowl.TowerLine, Level13Fishbowl.BackLine }, said);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Flap.IsOpen && level.Cork.AtRest, 6f), "the flap opens again and the cork comes down");

            Assert.IsTrue(Attempt(level.Solve(bot), 120f), "a clean retry");
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Level13Fishbowl level = Load();
            var bot = new Bot(Game);

            // After the soak: restart.
            Assert.AreEqual(1.5f + Level13Fishbowl.FlakeLift, level.FlakesY, 1e-4f, "the fish food lies on the water");
            BotRunner.Run(Game, SoakFrom(bot, level, Edge, Level13Fishbowl.AimPoint), 30f);
            Settle(level);
            Assert.Less(level.Bowl.Depth, 0.6f);
            Assert.AreEqual(level.Bowl.SurfaceY + Level13Fishbowl.FlakeLift, level.FlakesY, 1e-4f, "and goes down with it");
            Game.RestartLevel();
            level = (Level13Fishbowl)Game.Level;
            Assert.AreEqual(1.5f + Level13Fishbowl.FlakeLift, level.FlakesY, 1e-4f);
            Assert.AreEqual(405f, level.Bowl.Volume, 1e-3f);
            Assert.AreEqual(2f, level.Sponge.Scale);
            Assert.AreEqual(0f, level.Soak.Stored);
            Assert.AreEqual(0, level.Sweeps);

            // Half way up the tower: restart.
            bot = new Bot(Game);
            BotRunner.Run(Game, HalfWayUp(bot, level), 60f);
            Assert.Greater(level.Cork.TopY, 3f);
            Assert.IsTrue(level.Flap.IsClosed);
            Game.RestartLevel();
            level = (Level13Fishbowl)Game.Level;
            Assert.IsTrue(level.Flap.IsOpen);
            Assert.IsTrue(level.Cork.AtRest);
            Assert.AreEqual(0f, level.Tower.Volume);
            Assert.AreEqual(405f, level.TotalWater, 1e-3f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level13Fishbowl.Spawn), 0.05f);

            List<string> said = Listen();
            TestHelpers.PlayLevel(Game, 120f);
            Assert.IsEmpty(said);
        }

        static IEnumerator HalfWayUp(Bot bot, Level13Fishbowl level)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Level13Fishbowl.CorkAim);
            yield return bot.Until(() => level.Cork.TopY > 4f, 6f);
        }

        // ---- One voice ------------------------------------------------------------------------------------------------

        [Test]
        public void TheLevel_SpeaksWithTheCampaignsVoice()
        {
            LevelDefinition level = LevelRegistry.Get(13);
            var all = new List<string> { level.Blurb };
            all.AddRange(level.Hints);
            var lines = new List<string>();
            foreach (FieldInfo field in level.GetType().GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text != null && text.Contains(" ")) lines.Add(text);
            }
            Assert.GreaterOrEqual(lines.Count, 9, "the level's lines were found");
            all.AddRange(lines);
            Assert.LessOrEqual(level.Blurb.Length, 60, "the blurb fits the level card");
            foreach (string text in all)
            {
                foreach (char c in text)
                    Assert.IsTrue(c >= ' ' && c <= '~', "\"" + text + "\" has a character the UI font may not have ('" + c + "')");
                Assert.IsTrue(text.EndsWith(".") || text.EndsWith("?") || text.EndsWith("!"), "\"" + text + "\" is a sentence");
                Assert.AreEqual(text.Trim(), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("grab"), "\"" + text + "\" - the levels say \"pick up\"");
                Assert.IsFalse(lower.Contains("take it") || lower.Contains("taken"), "\"" + text + "\" - the levels say \"pick up\"");
                Assert.IsFalse(lower.Contains("drop") || lower.Contains("release"), "\"" + text + "\" - the levels say \"let go\"");
            }
            foreach (string line in lines)
                Assert.LessOrEqual(line.Length, 110, "\"" + line + "\" is too long for a toast (" + line.Length + ")");
            string nudge = level.Hints[0].ToLowerInvariant();
            Assert.IsFalse(nudge.Contains("outline") || nudge.Contains("shoe prints") || nudge.Contains("edge") || nudge.Contains("tower") || nudge.Contains("cork"),
                "the first hint gives the place away: \"" + level.Hints[0] + "\"");
            StringAssert.StartsWith("Pick", level.Hints[2], "the last hint begins with the pick-up");
            StringAssert.Contains("let go", level.Hints[2], "and says when to let go");
            StringAssert.Contains("cork", level.Hints[2], "and where");
            foreach (string hint in level.Hints) Assert.LessOrEqual(hint.Length, 205, "\"" + hint + "\" is too long for the pause card");
        }

        // ---- What it costs to draw ------------------------------------------------------------------------------------

        // The level with its room and every presenter, counted before culling (ART_BIBLE 12.1).
        [Test]
        public void TheLevelWithItsRoom_IsWithinTheBudgetOfEveryTier()
        {
            Settings.Use(new MemoryStore());
            try
            {
                foreach (TierSpec tier in TierSpec.All)
                {
                    Load();
                    Presentation presentation = Presentation.Create(Game, new PresentationOptions { Quality = tier.Tier });
                    try
                    {
                        for (int i = 0; i < 4; i++) presentation.Frame(Sim.Dt, 1f);
                        SceneCensus census = SceneCensus.Take(Game);
                        Debug.Log("[Toybox] level 13, " + tier.Name + ": " + census);
                        Assert.Greater(census.Renderers, 20, "the level is drawn: " + census);
                        Assert.IsNull(census.Over(tier), tier.Name + ": " + census.Over(tier) + " (" + census + ")");
                    }
                    finally
                    {
                        presentation.Dispose();
                    }
                }
            }
            finally
            {
                Settings.Use(null);
            }
        }

        // ---- Probes (explicit: they measure, they do not judge) ---------------------------------------------------

        static void WriteProbe(string name, StringBuilder text)
        {
            string path = Path.Combine(Application.dataPath, "../tools/out/notes/" + name);
            File.WriteAllText(path, text.ToString());
            Debug.Log("[Toybox] probe written: " + path);
        }

        [Test, Explicit]
        public void Probe_Solve() => SolveProbe("level13-probe-solve.txt", null);

        // The same with the first let-go at the low end of the window (7.1: the bowl is left 1.08 deep).
        [Test, Explicit]
        public void Probe_LowEnd() => SolveProbe("level13-probe-lowend.txt", Along(Edge, LowEndPitch));

        void SolveProbe(string file, Vector3? aim)
        {
            var text = new StringBuilder();
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            Game.Events.PropGrabbed += e => text.AppendLine(F(Game.Time) + " s  pick-up: scale " + F(e.OldScale) + " from " + F(e.GrabDistance) + " (k " + F(e.OldScale / e.GrabDistance) + "), bowl " + F(level.Bowl.Depth));
            Game.Events.PropDropped += e => text.AppendLine(F(Game.Time) + " s  let go: scale " + F(e.NewScale) + " at " + F(e.DropDistance) + ", centre " + F(e.Prop.Center) + ", stored " + F(level.Soak.Stored));
            Game.Events.Message += e => text.AppendLine(F(Game.Time) + " s  SAID: " + e.Text);
            level.Flap.Closed += () => text.AppendLine(F(Game.Time) + " s  flap closed");
            level.Cork.FloatArrived += () => text.AppendLine(F(Game.Time) + " s  cork at the top, bowl " + F(level.Bowl.Depth) + ", player " + F(Game.Player.Position) + ", sponge " + F(level.Sponge.Center));
            level.Soak.Full += () => text.AppendLine(F(Game.Time) + " s  sponge full: " + F(level.Soak.Stored) + ", bowl " + F(level.Bowl.Depth) + " deep, sponge " + F(level.Sponge.Scale) + " at " + F(level.Sponge.Center));
            level.Soak.Wringing += () => text.AppendLine(F(Game.Time) + " s  wringing over " + (level.Soak.Over != null ? level.Soak.Over.Name : "nothing") + ", sponge " + F(level.Sponge.Scale) + " at " + F(level.Sponge.Center));
            int tick = 0;
            Game.Context.OnUpdate(dt =>
            {
                if (tick++ % 30 != 0) return;
                text.AppendLine(F(Game.Time) + " s  player " + F(Game.Player.Position) + (Game.Player.Grounded ? " g" : " a") + ", sponge " + F(level.Sponge.Scale) + " at " + F(level.Sponge.Center) +
                                (level.Sponge.Held ? " held" : "") + ", bowl " + F(level.Bowl.Depth) + ", tower " + F(level.Tower.Depth) + ", cork " + F(level.Cork.TopY) + ", stored " + F(level.Soak.Stored));
            });
            Game.Events.PlayerRespawned += e => text.AppendLine(F(Game.Time) + " s  PLAYER RESPAWNED from " + F(e.From) + ", sweeps " + level.Sweeps + ", flap " + F(level.Flap.Openness) + ", bowl " + F(level.Bowl.Depth) + ", tower " + F(level.Tower.Depth));
            var bot = new Bot(Game);
            bool ran = Attempt(aim.HasValue ? SolveWith(bot, level, Edge, aim.Value) : level.Solve(bot), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            text.AppendLine(F(Game.Time) + " s  script " + (ran ? "ran to its end" : "GAVE UP") + ", completed " + Game.LevelCompleted + ", total water " + F(level.TotalWater) + ", said " + said.Count);
            WriteProbe(file, text);
        }

        // What the first let-go gives, by where the player stands and how they look.
        [Test, Explicit]
        public void Probe_Soak()
        {
            var text = new StringBuilder();
            text.AppendLine("stand z | pitch | yaw | scale | centre | bowl after | stored | said");
            foreach (float z in new[] { -6.4f, -6.8f, -7.4f, -8.2f })
                foreach (float pitch in new[] { -45f, -35f, -30f, -25f, -22f, -20f, -18f, -16f, -12f, -8f, -4f, 0f, 10f, 20f, 35f })
                    text.AppendLine(SoakRow(new Vector3(0f, 3f, z), pitch, 0f, 0f));
            foreach (float yaw in new[] { -40f, -25f, -12f, 12f, 25f, 40f })
                foreach (float pitch in new[] { -25f, -16f, -8f })
                    text.AppendLine(SoakRow(Edge, pitch, yaw, 0f));
            WriteProbe("level13-probe-soak.txt", text);
        }

        // What the first let-go from the edge gives, by how far away the sponge was picked up.
        [Test, Explicit]
        public void Probe_PickUp()
        {
            var text = new StringBuilder();
            text.AppendLine("back | k | pitch | yaw | scale | centre | bowl after | stored | said");
            foreach (float back in new[] { 0.01f, 0.2f, 0.4f })
                foreach (float pitch in new[] { -25f, -20f, -16f, -10f, -5f, 0f, 10f })
                    text.AppendLine(SoakRow(Edge, pitch, 0f, back));
            WriteProbe("level13-probe-pickup.txt", text);
        }

        string SoakRow(Vector3 stand, float pitch, float yaw, float back)
        {
            Level13Fishbowl level = Load();
            List<string> said = Listen();
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            var bot = new Bot(Game);
            bool ran = Attempt(SoakFrom(bot, level, stand, Along(stand, pitch, yaw), back), 30f);
            RunSeconds(1.5f);
            Settle(level, 3f);
            return (back > 0f ? F(back) + " | " + F(k) : F(stand.z)) + " | " + pitch.ToString("0") + " | " + yaw.ToString("0") + " | " + F(level.Sponge.Scale) + " | " + F(level.Sponge.Center) + " | " +
                   F(level.Bowl.Depth) + " | " + F(level.Soak.Stored) + " | best k " + F(level.BestRatio(Game.Player)) + " | " + (ran ? "" : "GAVE UP ") + string.Join(" / ", said);
        }

        // What the second let-go gives, from the shoe prints on the cork, by how the player looks.
        [Test, Explicit]
        public void Probe_Wring()
        {
            var text = new StringBuilder();
            text.AppendLine("pitch | yaw | k | scale | centre | flap shut after | cork top after | completed | said");
            foreach (float yaw in new[] { 180f, 150f, 120f })
                foreach (float pitch in new[] { -85f, -70f, -55f, -41f, -30f, -20f, -10f, 0f, 10f })
                {
                    Level13Fishbowl level = Load();
                    List<string> said = Listen();
                    float k = 0f, wrung = -1f, shut = -1f, top = -1f, small = 0f;
                    Vector3 where = Vector3.zero;
                    Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
                    Game.Events.PropDropped += e =>
                    {
                        small = e.NewScale;
                        where = e.Prop.Center;
                    };
                    level.Soak.Wringing += () => wrung = Game.Time;
                    level.Flap.Closed += () => shut = Game.Time;
                    level.Cork.FloatArrived += () => top = Game.Time;
                    var bot = new Bot(Game);
                    bool ran = Attempt(WringProbe(bot, level, pitch, yaw), 120f);
                    TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                    text.AppendLine(pitch.ToString("0") + " | " + yaw.ToString("0") + " | " + F(k) + " | " + F(small) + " | " + F(where) + " | " + (shut >= 0f ? F(shut - wrung) : "-") + " | " +
                                    (top >= 0f ? F(top - wrung) : "-") + " | " + Game.LevelCompleted + " | " + (ran ? "" : "GAVE UP ") + string.Join(" / ", said));
                }
            WriteProbe("level13-probe-wring.txt", text);
        }

        static IEnumerator WringProbe(Bot bot, Level13Fishbowl level, float pitch, float yaw)
        {
            yield return ToThePad(bot, level);
            yield return bot.Grab(level.Sponge);
            yield return bot.DropAt(Pad + Vector3.up * Player.BaseEyeHeight + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * 10f);
            yield return level.Ride(bot);
        }
    }

    /// <summary>
    /// Level 13 on the pause card: every hint is set at the panel's full size, as in the levels before it
    /// (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize does the same for levels 5 to 9).
    /// </summary>
    public class Level13UiTests
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
            host = new GameObject("Level 13 Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=13"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            devices.State.EscapePressed = false;
            PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            Assert.AreEqual(13, runner.Game.Level.Id);
            string[] hints = runner.Game.Level.Hints;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                runner.Frame(Sim.Dt);
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                Debug.Log("[Toybox] Level 13, hint " + (h + 1) + ": " + hints[h].Length + " characters, " + pause.HintText.textInfo.lineCount + " lines at size " + size.ToString("0.0"));
                Assert.Greater(pause.HintText.textInfo.lineCount, 0, "the text was laid out");
                Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }
}
