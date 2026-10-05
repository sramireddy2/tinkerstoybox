using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Render;
using UnityEngine;
using L = Toybox.Levels.Level08FunnelPhysics;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 8, "Funnel Physics" (LEVELS.md): the bot's solution, the three scale windows, what happens to
    /// a marble of the wrong size, the shut gate, restarts, and the ways a marble comes back.
    /// </summary>
    public class Level08Tests : SimTest
    {
        L Level => (L)Game.Level;

        Bot Load()
        {
            Game?.Dispose();
            Game = Game.Create();
            Game.LoadLevel(8);
            return new Bot(Game);
        }

        static string F(float value, string format = "0.00") => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        static void WriteNote(string name, StringBuilder text)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/out/notes/" + name));
            File.WriteAllText(path, text.ToString());
            Debug.Log("[Toybox] probe: " + path);
        }

        // Where a marble is: IN (on a plate that is down), MOUTH (held up by a throat), BELOW (under a throat,
        // not latched), PORT (on its way out of the port), FROZEN (where it started), LID (anywhere else).
        string Where(Prop marble)
        {
            L level = Level;
            Vector3 c = marble.Center;
            for (int i = 0; i < L.Holes.Length; i++)
            {
                L.Hole hole = L.Holes[i];
                float off = new Vector2(c.x - hole.X, c.z - L.FunnelZ).magnitude;
                if (level.Plates[i].Pressed && level.Plates[i].PressedBy == marble) return "IN-" + hole.Name;
                if (off < hole.Mouth && c.y < hole.ThroatY - 0.05f) return "BELOW-" + hole.Name;
                if (off < hole.Mouth && c.y < marble.Radius + 0.3f) return "MOUTH-" + hole.Name;
            }
            if (level.Port.IsPending(marble)) return "PORT";
            if (marble.Frozen) return "FROZEN";
            return "LID";
        }

        // ---- Fixtures (not player input) ------------------------------------------------------------------

        static void Put(Prop marble, float scale, Vector3 centre)
        {
            marble.Unfreeze();
            marble.SetScale(scale);
            marble.SetPose(centre, Quaternion.identity);
        }

        // A marble of a scale just over a funnel's mouth, a little off its axis: it comes down into the cone.
        static void PutOver(int holeIndex, Prop marble, float scale)
        {
            L.Hole hole = L.Holes[holeIndex];
            Put(marble, scale, new Vector3(hole.X + hole.Throat * 0.25f, scale * 0.5f + 0.3f, L.FunnelZ + hole.Throat * 0.2f));
        }

        static bool OnTheLid(Prop marble)
        {
            Vector3 c = marble.Center;
            return Mathf.Abs(c.y - marble.Radius) < 0.06f && Mathf.Abs(c.x) < L.HalfX + 2.6f && c.z > L.NearZ && c.z < L.FarZ + 1f;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        static int Count(List<string> said, string line) => said.FindAll(text => text == line).Count;

        // ---- The solution -------------------------------------------------------------------------------

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            L level = Level;
            Assert.AreEqual("funnel-physics", level.Slug);
            Assert.AreEqual("Funnel Physics", level.Title);
            Assert.AreEqual(2, level.Phase);
            Assert.AreEqual("pegboard-workbench", level.Environment);
            Assert.AreEqual(-6.6f, level.GroundY, 1e-4f);
            Assert.AreEqual(-30f, level.KillY, 1e-4f);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.AreEqual(3, Game.Props.Count, "the three marbles are the only things that can be picked up");
            Assert.IsTrue(level.Peewee.Frozen, "the little one waits on its spool");
            Assert.AreEqual(L.PeeweeScale, level.Peewee.Scale, 1e-4f);
            Assert.IsFalse(level.Aggie.Frozen);
            Assert.AreEqual(L.AggieScale, level.Aggie.Scale, 1e-4f);
            Assert.IsTrue(level.Shooter.Frozen, "the boulder waits in its niche");
            Assert.AreEqual(L.ShooterScale, level.Shooter.Scale, 1e-4f);
            Assert.IsTrue(level.Exit.Locked, "the exit opens with the gate");
            Assert.IsTrue(level.Gate.IsClosed);
            foreach (Prop marble in level.Marbles)
            {
                Assert.IsTrue(marble.HasTag(L.MarbleTag));
                Assert.AreEqual(L.BiggestMarble, marble.MaxScale, 1e-4f);
                // "None of them the right size": not the middling one either (it is between the small hole and the middle one).
                foreach (L.Hole hole in L.Holes) Assert.IsFalse(hole.Takes(marble.Scale), marble.Name + " does not fit hole " + hole.Name + " as it lies");
            }

            var drops = new List<PropHoldEvent>();
            Game.Events.PropDropped += drops.Add;
            List<string> said = Listen();
            TestHelpers.PlayLevel(Game, 90f);

            Assert.AreEqual(3, drops.Count, "three pick-ups and three let-gos solve it");
            Assert.AreSame(level.Peewee, drops[0].Prop);
            Assert.AreSame(level.Aggie, drops[1].Prop);
            Assert.AreSame(level.Shooter, drops[2].Prop);
            // LEVELS.md Appendix B: 0.64 / 1.32 / 2.45, each within 5 %... of what was measured here.
            Assert.AreEqual(0.65f, level.Peewee.Scale, 0.03f, "the little one grew to 0.65");
            Assert.AreEqual(1.22f, level.Aggie.Scale, 0.06f, "the middling one grew by a third");
            Assert.AreEqual(2.41f, level.Shooter.Scale, 0.12f, "the boulder shrank to 2.4");
            Assert.AreEqual(1.64f, drops[0].GrabDistance, 0.1f, "the little one was taken from the start, a step away");
            Assert.AreEqual(3.7f, drops[1].GrabDistance, 0.2f, "the middling one from four steps away");
            Assert.AreEqual(23.1f, drops[2].GrabDistance, 0.5f, "the boulder from the back of the room");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(level.Plates[i].Pressed, "plate " + L.Holes[i].Name);
                Assert.AreSame(level.Marbles[i], level.Plates[i].PressedBy, "each marble in its own hole");
                Assert.IsTrue(L.Holes[i].Takes(level.Marbles[i].Scale));
                Assert.IsFalse(level.Marbles[i].Grabbable, "a marble on its plate stays there");
                Assert.AreSame(Palette.Go.Name, level.Studs[i].Signal.Name, "the studs round a hole that has its marble are green");
            }
            Assert.AreEqual(3, level.Latched);
            Assert.IsTrue(level.Gate.IsOpen);
            Assert.IsFalse(level.Exit.Locked);
            Assert.AreEqual(0, level.Port.Ejections, "nothing went through the wrong hole");
            Assert.IsEmpty(said, "the intended solution is not told off: " + string.Join(" | ", said));
            Assert.Less(Game.Time, 30f, "the bot needs about twenty seconds");
        }

        [Test]
        public void TheSolver_SolvesItTheSameWayEveryTime_AndAgainAfterARestart()
        {
            var first = new float[3];
            float firstTime = 0f;
            for (int run = 0; run < 5; run++)
            {
                Load();
                TestHelpers.PlayLevel(Game, 90f);
                for (int m = 0; m < 3; m++)
                {
                    if (run == 0) first[m] = Level.Marbles[m].Scale;
                    Assert.AreEqual(first[m], Level.Marbles[m].Scale, 1e-5f, "run " + run + ", marble " + m + ": the simulation is deterministic");
                }
                if (run == 0) firstTime = Game.Time;
                Assert.AreEqual(firstTime, Game.Time, 1e-4f, "run " + run);
            }
            // And in the same game, from the level's solved state.
            for (int run = 0; run < 5; run++)
            {
                Game.RestartLevel();
                Assert.IsFalse(Game.LevelCompleted);
                TestHelpers.PlayLevel(Game, 90f);
                for (int m = 0; m < 3; m++) Assert.AreEqual(first[m], Level.Marbles[m].Scale, 2e-3f, "restart " + run + ", marble " + m);
                for (int i = 0; i < 3; i++) Assert.AreSame(Level.Marbles[i], Level.Plates[i].PressedBy, "restart " + run + ", plate " + i);
                Assert.AreEqual(0, Level.Port.Ejections, "restart " + run);
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
        public void FromTheSpawn_TheThreeMarbles_TheThreeHoles_AndTheGate_AreInThePicture()
        {
            Load();
            L level = Level;
            Player player = Game.Player;
            Assert.Less(Vector3.Distance(player.Position, L.SpawnPoint), 0.05f);
            Assert.AreEqual(L.SpawnPitch, player.Pitch, 1e-3f);

            // The three marbles: each in plain view, inside the picture, left - right - up the middle.
            foreach (Prop marble in level.Marbles)
            {
                Assert.IsTrue(Sees(marble.Center, out RaycastHit hit), marble.Name);
                Assert.AreSame(marble, PropRef.Of(hit.collider), "the eye sees " + marble.Name + ", not " + hit.collider.name);
                Vector2 at = InThePicture(player, marble.Center);
                Assert.Less(Mathf.Abs(at.x), 0.85f, marble.Name + " is at " + at);
                Assert.Less(Mathf.Abs(at.y), 0.85f, marble.Name + " is at " + at);
            }
            Assert.Less(InThePicture(player, level.Peewee.Center).x, -0.3f, "the little one on its spool to the left");
            Assert.Greater(InThePicture(player, level.Aggie.Center).x, 0.3f, "the middling one on the lid to the right");
            Assert.Greater(InThePicture(player, level.Shooter.Center).y, 0.2f, "the boulder up in its niche");
            // The little one is a step away: taken from here it can grow into the small hole (k 0.15).
            Assert.Less(Vector3.Distance(player.Eye, level.Peewee.Center), 1.8f);
            Assert.Greater(level.Peewee.Scale / Vector3.Distance(player.Eye, level.Peewee.Center), 0.14f);

            // The three holes: the far side of each mouth is seen over its near rim (of the small one, twenty
            // steps away, only the top of its far side: the studs and the paint round it say where it is).
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                Vector3 farWall = Spot(hole, hole.Mouth * 0.93f);
                Vector2 at = InThePicture(player, farWall);
                Assert.Less(Mathf.Abs(at.x), 0.8f, "funnel " + hole.Name + " is at " + at);
                Assert.That(at.y, Is.InRange(-0.5f, 0.1f), "funnel " + hole.Name + " is at " + at);
                Assert.IsTrue(Sees(farWall, out RaycastHit hit), hole.Name);
                Assert.AreSame(level.Funnels[i].GameObject, hit.collider.gameObject, "the eye sees into funnel " + hole.Name + ", not " + hit.collider.name);
            }

            // The gate, straight ahead under the niche.
            var gate = new Vector3((L.GateX0 + L.GateX1) * 0.5f, L.GateHeight * 0.5f, L.GateZ - L.GateThickness * 0.5f);
            Vector2 gateAt = InThePicture(player, gate);
            Assert.Less(Mathf.Abs(gateAt.x), 0.2f, "the gate is at " + gateAt);
            Assert.Less(Mathf.Abs(gateAt.y), 0.3f, "the gate is at " + gateAt);
            Assert.IsTrue(Sees(gate, out RaycastHit gateHit));
            Assert.AreEqual("Gate", gateHit.collider.name, "nothing stands between the eye and the gate");

            // A click takes whichever marble the view is on.
            foreach (Prop marble in level.Marbles)
            {
                TestHelpers.LookAt(player, marble.Center);
                Assert.AreSame(marble, Game.Grabber.FindTarget(), "looking at " + marble.Name + ", a click takes it");
            }
        }

        // ---- The scale windows (LEVELS.md: 0.50-0.76, 1.00-1.52, 2.00-3.04; a throat passes what is smaller) ----

        // The low end, the intended size, the spec's high end, and a hair under the throat.
        [TestCase(0, 0.50f)]
        [TestCase(0, 0.64f)]
        [TestCase(0, 0.76f)]
        [TestCase(0, 0.79f)]
        [TestCase(1, 1.00f)]
        [TestCase(1, 1.32f)]
        [TestCase(1, 1.52f)]
        [TestCase(1, 1.58f)]
        [TestCase(2, 2.00f)]
        [TestCase(2, 2.45f)]
        [TestCase(2, 3.04f)]
        [TestCase(2, 3.16f)]
        public void AMarbleThatNearlyFillsTheHole_FallsThrough_AndPressesThePlate(int holeIndex, float scale)
        {
            Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            // Whichever marble: they differ in colour only.
            Prop marble = level.Marbles[(holeIndex + 1) % 3];
            List<string> said = Listen();
            Assert.IsTrue(hole.Takes(scale));
            Assert.AreEqual(FitState.Good, level.Gauges[holeIndex].Fit(scale), "the gauge calls " + scale + " good");
            PutOver(holeIndex, marble, scale);

            PressurePlate plate = level.Plates[holeIndex];
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => plate.Pressed, 6f),
                "a marble of " + scale + " (mass " + marble.Mass + " against " + hole.MinMass + ") presses plate " + hole.Name + "; it is " + Where(marble) + " at " + marble.Center);
            Assert.AreSame(marble, plate.PressedBy);
            RunSeconds(0.5f);
            Assert.IsFalse(marble.Grabbable, "the plate keeps it");
            Assert.Less(new Vector2(marble.Center.x - hole.X, marble.Center.z - L.FunnelZ).magnitude, 0.02f, "in the middle of the plate");
            Assert.AreEqual(hole.FloorY + marble.Radius, marble.Center.y, 0.25f, "at the bottom of the chamber");
            Assert.AreEqual(1, level.Latched);
            Assert.AreEqual(0, level.Port.Ejections);
            Assert.IsEmpty(said, string.Join(" | ", said));
            Assert.AreSame(Palette.Go.Name, level.Studs[holeIndex].Signal.Name);
            Assert.IsFalse(level.Gauges[holeIndex].Enabled, "nothing is left to judge at this hole");
            Assert.IsTrue(level.Gate.IsClosed, "one plate does not open the gate");
            Assert.IsTrue(level.Exit.Locked);
            Assert.IsFalse(Game.LevelCompleted);
        }

        [TestCase(0, 0.25f)]
        [TestCase(0, 0.40f)]
        [TestCase(0, 0.48f)]
        [TestCase(1, 0.60f)]
        [TestCase(1, 0.97f)]
        [TestCase(2, 1.20f)]
        [TestCase(2, 1.94f)]
        public void AMarbleThatIsTooSmall_FallsThrough_AndRollsBackOutOfThePort(int holeIndex, float scale)
        {
            Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Prop marble = level.Marbles[(holeIndex + 2) % 3];
            List<string> said = Listen();
            int rejected = 0;
            level.Plates[holeIndex].OnRejected += (prop, load) => rejected++;
            Assert.IsFalse(hole.Takes(scale));
            Assert.AreEqual(FitState.TooSmall, level.Gauges[holeIndex].Fit(scale));
            PutOver(holeIndex, marble, scale);

            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 1, 6f),
                "a marble of " + scale + " comes back out of the port; it is " + Where(marble) + " at " + marble.Center);
            Assert.IsFalse(level.Plates[holeIndex].Pressed, "too light: " + marble.Mass + " against " + hole.MinMass);
            Assert.AreEqual(1, rejected);
            Assert.AreEqual(1, Count(said, L.TooLightLine), string.Join(" | ", said));
            Assert.AreEqual(scale, marble.Scale, 1e-4f, "at the size it had");
            Assert.Less(marble.Center.x, -L.HalfX + 3f, "it is in the port, at " + marble.Center);

            // It rolls out onto the lid and lies there, in reach, a few steps from the port.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => marble.Velocity.magnitude < 0.1f && OnTheLid(marble), 10f), "it comes to rest: " + marble.Center + " at " + marble.Velocity.magnitude);
            Assert.That(marble.Center.x, Is.InRange(-L.HalfX + 1f, -4f), "a few steps from the port (it lies at " + marble.Center + ")");
            Assert.Less(Mathf.Abs(marble.Center.z - L.PortZ), 1.5f);
            Assert.IsTrue(marble.Grabbable);
            Assert.AreEqual(1, said.Count, "nothing more is said: " + string.Join(" | ", said));
            Assert.AreEqual(0, level.Latched);
        }

        [TestCase(0, 0.82f)]
        [TestCase(0, 1.5f)]
        [TestCase(0, 2.6f)]
        [TestCase(1, 1.65f)]
        [TestCase(1, 3.0f)]
        [TestCase(2, 3.3f)]
        [TestCase(2, 5.5f)]
        public void AMarbleThatIsTooBig_SitsInTheMouth_AndTheLevelSaysSo(int holeIndex, float scale)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Prop marble = level.Marbles[holeIndex];
            List<string> said = Listen();
            Assert.IsFalse(hole.Takes(scale));
            Assert.AreEqual(FitState.TooBig, level.Gauges[holeIndex].Fit(scale));
            PutOver(holeIndex, marble, scale);
            RunSeconds(4f);

            Assert.IsFalse(level.Plates[holeIndex].Pressed, "a marble of " + scale + " does not pass a throat of " + hole.PassBelow);
            Assert.AreEqual("MOUTH-" + hole.Name, Where(marble), "it is at " + marble.Center);
            Assert.Greater(marble.Center.y, hole.ThroatY, "held up by the throat");
            Assert.Less(new Vector2(marble.Center.x - hole.X, marble.Center.z - L.FunnelZ).magnitude, hole.Mouth * 0.5f, "over the hole");
            Vector3 before = marble.Center;
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(before, marble.Center), 0.1f, "it stays where it is (the jam rule leaves it alone)");
            Assert.AreEqual(1, Count(said, L.TooBigLine), "the level says what is wrong, once: " + string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
            Assert.AreEqual(0, level.Port.Ejections);
            // And it can be picked up again, from two steps before the funnel (one that only just does not pass
            // sits low in the mouth: from across the room the near rim hides it).
            Assert.IsTrue(marble.Grabbable);
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(hole, hole.Mouth + 2f), 0.3f, 20f), 30f);
            BotRunner.Run(Game, bot.Grab(marble), 10f);
            Assert.AreSame(marble, Game.Grabber.Held);
        }

        [Test]
        public void ASecondMarble_IntoAHoleThatHasItsMarble_RollsBackOutOfThePort()
        {
            Load();
            L level = Level;
            List<string> said = Listen();
            PutOver(1, level.Aggie, 1.3f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[1].Pressed, 6f));
            RunSeconds(0.5f);
            // A second one that would have done as well, and one that is too small.
            foreach (float scale in new[] { 1.2f, 0.5f })
            {
                int before = level.Port.Ejections;
                said.Clear();
                PutOver(1, level.Peewee, scale);
                Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == before + 1, 6f), "a second marble of " + scale + " is " + Where(level.Peewee) + " at " + level.Peewee.Center);
                Assert.AreEqual(1, Count(said, L.BusyLine), string.Join(" | ", said));
                Assert.AreEqual(scale, level.Peewee.Scale, 1e-4f);
                Assert.AreSame(level.Aggie, level.Plates[1].PressedBy, "the plate keeps the marble it has");
                Assert.IsTrue(level.Plates[1].Pressed);
                RunSeconds(1f);
            }
            Assert.AreEqual(1, level.Latched);
        }

        // Plates weigh the heaviest single marble: two that are each too light never add up.
        [Test]
        public void TwoLightMarblesOnOnePlate_DoNotAddUp()
        {
            Load();
            L level = Level;
            float scale = 1.75f;
            Assert.Greater(L.MarbleMass(scale) * 2f, L.Holes[2].MinMass, "together they would be heavy enough");
            Put(level.Peewee, scale, new Vector3(L.Holes[2].X, scale * 0.5f + 0.3f, L.FunnelZ));
            Put(level.Aggie, scale, new Vector3(L.Holes[2].X, scale * 1.5f + 0.5f, L.FunnelZ));
            RunSeconds(1.2f);
            Assert.IsFalse(level.Plates[2].Pressed, "load " + level.Plates[2].Load);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 2, 8f), "both come back: " + Where(level.Peewee) + ", " + Where(level.Aggie));
            Assert.IsFalse(level.Plates[2].Pressed);
        }

        static IEnumerator WalkOut(Bot bot)
        {
            yield return bot.WalkTo(L.BetweenMAndL, 0.3f, 20f);
            yield return bot.WalkTo(new Vector3(L.ExitCentre.x, 0f, L.ExitCentre.z), 0.3f, 12f);
            yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
        }

        [Test]
        public void TheGateOpens_WhenAllThreePlatesAreDown_AndNotBefore()
        {
            Bot bot = Load();
            L level = Level;
            // In any order, and with the marbles in each other's holes.
            PutOver(2, level.Peewee, 2.2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[2].Pressed, 6f));
            PutOver(0, level.Shooter, 0.7f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[0].Pressed, 6f));
            RunSeconds(1f);
            Assert.AreEqual(2, level.Latched);
            Assert.IsTrue(level.Gate.IsClosed, "two plates do not open the gate");
            Assert.IsTrue(level.Exit.Locked);

            PutOver(1, level.Aggie, 1.2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[1].Pressed, 6f));
            Assert.IsFalse(level.Exit.Locked, "the third plate unlocks the exit");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Gate.IsOpen, 1.5f), "and the gate rolls up within its 0.8 s");
            Assert.IsFalse(Game.LevelCompleted, "the way is open; the player still has to walk it");

            int completed = 0;
            Game.Events.LevelCompleted += e => completed++;
            BotRunner.Run(Game, WalkOut(bot), 40f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);
        }

        // ---- The bot at other distances: the size is how big it looked times how far away it is let go ----

        static IEnumerator Carry(Bot bot, Prop marble, Vector3[] wayToPick, Vector3 stand, Vector3 aim, float wait)
        {
            foreach (Vector3 point in wayToPick) yield return bot.WalkTo(point, 0.1f);
            yield return bot.Grab(marble);
            // (From the start the spool is in the way of a straight line to the small funnel.)
            if (bot.Player.Position.z < L.PastTheSpool.z - 0.5f) yield return bot.WalkTo(L.PastTheSpool, 0.2f);
            yield return bot.WalkTo(stand, 0.1f, 20f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(wait);
        }

        static readonly Vector3[] NoWay = new Vector3[0];
        static readonly Vector3[] WayToAggie = { L.PastTheSpool, L.PickAggie };
        static readonly Vector3[] WayToShooter = { new Vector3(5f, 0f, -8f), L.PickShooter };

        static Vector3[] WayTo(int marbleIndex) => marbleIndex == 0 ? NoWay : marbleIndex == 1 ? WayToAggie : WayToShooter;

        // The spot of a funnel's floor, cone or tube mouth that lies a distance beyond its axis (seen from -Z).
        static Vector3 Spot(L.Hole hole, float beyond)
        {
            float r = Mathf.Abs(beyond);
            float y = r >= hole.Mouth ? 0f : r <= hole.Throat ? hole.ThroatY : hole.ConeY(r);
            return new Vector3(hole.X, y, L.FunnelZ + beyond);
        }

        static Vector3 Back(L.Hole hole, float distance) => new Vector3(hole.X, 0f, L.FunnelZ - distance);

        // Each marble picked up as the solver picks it up, and let go over the far rim of its hole from three
        // distances: too near it is too small (through the hole and out of the port), in between it fits,
        // too far it is too big (it sits in the mouth). Measured: tools/out/notes/level08-probe-sweep-*.txt.
        [TestCase(0, 1.8f, "port", 0.46f)]
        [TestCase(0, 2.6f, "IN-S", 0.56f)]
        [TestCase(0, 3.4f, "IN-S", 0.66f)]
        [TestCase(0, 4.2f, "IN-S", 0.76f)]
        [TestCase(0, 5.5f, "MOUTH-S", 0.93f)]
        [TestCase(1, 2.8f, "IN-M", 1.10f)]
        [TestCase(1, 3.8f, "IN-M", 1.31f)]
        [TestCase(1, 4.8f, "IN-M", 1.51f)]
        [TestCase(1, 6.5f, "MOUTH-M", 1.86f)]
        [TestCase(2, 5f, "port", 1.64f)]
        [TestCase(2, 8f, "IN-L", 2.18f)]
        [TestCase(2, 9f, "IN-L", 2.36f)]
        [TestCase(2, 10f, "IN-L", 2.54f)]
        public void LetGoOverTheFarRim_FromNearerItIsSmaller_FromFartherBigger(int index, float back, string expected, float expectedScale)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[index];
            Prop marble = level.Marbles[index];
            BotRunner.Run(Game, Carry(bot, marble, WayTo(index), Back(hole, back), Spot(hole, hole.Mouth), 7f), 60f);
            string where = Where(marble);
            if (where == "LID" && level.Port.Ejections > 0) where = "port";
            Assert.AreEqual(expected, where, hole.Name + " from " + back + " back: a marble of " + marble.Scale + " at " + marble.Center);
            Assert.AreEqual(expectedScale, marble.Scale, expectedScale * 0.05f, "from " + back + " back");
            Assert.AreEqual(expected.StartsWith("IN"), hole.Takes(marble.Scale), "the rule and the outcome agree");
        }

        // The same from off the centre line, and looking a little to the side: the funnels are round.
        [TestCase(0, 3.4f, -30f)]
        [TestCase(0, 3.2f, 40f)]
        [TestCase(1, 3.8f, -25f)]
        [TestCase(1, 3.8f, 35f)]
        [TestCase(2, 8.8f, -20f)]
        [TestCase(2, 8.8f, 15f)]
        public void FromAnySideOfAFunnel_HeldOverItsFarSide_TheMarbleGoesIn(int index, float back, float bearing)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[index];
            Prop marble = level.Marbles[index];
            var from = new Vector3(Mathf.Sin(bearing * Mathf.Deg2Rad), 0f, -Mathf.Cos(bearing * Mathf.Deg2Rad));
            Vector3 stand = hole.Axis + from * back;
            // Over the far side, as seen from there: beyond the axis by the same share of the mouth as the solver's aim.
            float beyond = index == 2 ? hole.Mouth * 0.9f : (hole.Throat + hole.Mouth) * 0.5f;
            Vector3 aim = hole.Axis - from * beyond + Vector3.up * (index == 2 ? 0.4f : 0f);
            BotRunner.Run(Game, Carry(bot, marble, WayTo(index), stand, aim, 7f), 60f);
            Assert.AreEqual("IN-" + hole.Name, Where(marble), "from " + stand + ": a marble of " + marble.Scale + " at " + marble.Center);
        }

        // Any marble serves any hole (LEVELS.md): the boulder into the middle one, the middling one into the big one.
        [Test]
        public void TheMarblesAreInterchangeable_TheBoulderInTheMiddleHole_TheMiddlingOneInTheBigHole()
        {
            Bot bot = Load();
            L level = Level;
            BotRunner.Run(Game, Swapped(bot, level), 120f);
            Assert.AreSame(level.Shooter, level.Plates[1].PressedBy, "the boulder is " + Where(level.Shooter) + ", " + level.Shooter.Scale);
            Assert.AreSame(level.Aggie, level.Plates[2].PressedBy, "the middling one is " + Where(level.Aggie) + ", " + level.Aggie.Scale);
            Assert.AreSame(level.Peewee, level.Plates[0].PressedBy);
            Assert.IsTrue(Game.LevelCompleted);
        }

        static IEnumerator Swapped(Bot bot, L level)
        {
            yield return bot.Grab(level.Peewee);
            yield return bot.WalkTo(L.PastTheSpool);
            yield return bot.WalkTo(L.StandS, 0.15f);
            yield return bot.DropAt(L.AimS);
            yield return bot.Until(() => level.Plates[0].Pressed, 8f);
            // The middling one, picked up from five steps off, let go eight steps before the big hole: twice what it was.
            yield return bot.WalkTo(L.PickAggie, 0.15f);
            yield return bot.Grab(level.Aggie);
            yield return bot.WalkTo(Back(L.Holes[2], 8f), 0.15f, 20f);
            yield return bot.DropAt(Spot(L.Holes[2], L.Holes[2].Mouth));
            yield return bot.Until(() => level.Plates[2].Pressed, 10f);
            // The boulder from the back of the room, let go five steps before the middle hole: a quarter of what it was.
            yield return bot.WalkTo(L.PickShooter, 0.15f, 20f);
            yield return bot.Grab(level.Shooter);
            yield return bot.WalkTo(Back(L.Holes[1], 4.8f), 0.15f, 20f);
            yield return bot.DropAt(Spot(L.Holes[1], 1.35f));
            yield return bot.Until(() => level.Plates[1].Pressed, 10f);
            yield return bot.Until(() => level.Gate.IsOpen, 3f);
            yield return WalkOut(bot);
        }

        // ---- What the lamps say, and what the level says ------------------------------------------------

        static IEnumerator HoldOver(Bot bot, Prop marble, Vector3 stand, Vector3 aim)
        {
            yield return bot.Grab(marble);
            if (bot.Player.Position.z < L.PastTheSpool.z - 0.5f) yield return bot.WalkTo(L.PastTheSpool, 0.2f);
            yield return bot.WalkTo(stand, 0.1f, 20f);
            yield return bot.LookAt(aim);
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void TheLampsReadTheMarbleInTheHand_TooSmallNear_GoodAtTheRightDistance_TooBigFar()
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[0];
            FitGauge gauge = level.Gauges[0];
            Assert.AreEqual(FitState.Idle, gauge.State);
            Assert.AreSame(Palette.Amber.Name, level.Studs[0].Signal.Name, "amber while it waits");

            BotRunner.Run(Game, HoldOver(bot, level.Peewee, Back(hole, 1.8f), Spot(hole, hole.Mouth)), 30f);
            Assert.AreEqual(FitState.TooSmall, gauge.State, "from 1.8 back the little one is " + level.Peewee.Scale);
            Assert.AreSame(L.Wrong.Name, level.Studs[0].Signal.Name);
            Assert.AreSame(Palette.Amber.Name, level.Studs[1].Signal.Name, "the other holes' studs keep waiting");

            BotRunner.Run(Game, HoldOver(bot, level.Peewee, L.StandS, L.AimS), 30f);
            Assert.AreEqual(FitState.Good, gauge.State, "from the solver's stand it is " + level.Peewee.Scale);
            Assert.AreSame(Palette.Go.Name, level.Studs[0].Signal.Name);
            Assert.AreEqual(level.Peewee.Scale, gauge.Scale, 1e-4f);

            BotRunner.Run(Game, HoldOver(bot, level.Peewee, Back(hole, 5.5f), Spot(hole, hole.Mouth)), 30f);
            Assert.AreEqual(FitState.TooBig, gauge.State, "from 5.5 back it is " + level.Peewee.Scale);
            Assert.AreSame(L.Wrong.Name, level.Studs[0].Signal.Name);

            // Looking away from the funnel the gauge has nothing to read.
            BotRunner.Run(Game, bot.LookAt(new Vector3(0f, 3f, L.NearZ)), 10f);
            RunSeconds(0.1f);
            Assert.AreEqual(FitState.Idle, gauge.State);
            Assert.AreSame(Palette.Amber.Name, level.Studs[0].Signal.Name);
        }

        // The natural first try: the marble held over the middle of the hole. It grows as it goes, and its
        // underside meets the lid before the rim: it is let go in front of the hole. The level says so, and
        // picked up again from there and held over the far side it goes in.
        [Test]
        public void HeldOverTheMiddleOfTheHole_TheMarbleStopsShort_AndTheLevelSaysSo()
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[0];
            Prop marble = level.Peewee;
            List<string> said = Listen();
            BotRunner.Run(Game, Carry(bot, marble, NoWay, L.StandS, Spot(hole, 0f), 3f), 30f);
            Assert.AreEqual("LID", Where(marble), "it lies at " + marble.Center + ", " + marble.Scale);
            Assert.Less(marble.Center.z, L.FunnelZ - hole.Mouth + 0.05f, "in front of the near rim");
            Assert.AreEqual(1, Count(said, L.ShortLine), string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
            Assert.AreEqual(0, level.Port.Ejections);

            BotRunner.Run(Game, Again(bot, marble, L.AimS, 6f), 30f);
            Assert.AreEqual("IN-S", Where(marble), "picked up again and held over the far side: " + marble.Scale + " at " + marble.Center);
            Assert.AreEqual(1, said.Count, "nothing more is said: " + string.Join(" | ", said));
        }

        // The other way to miss: held too high, the marble passes low over the hole and comes down on the lid
        // behind it (measured: tools/out/notes/level08-probe-angles.txt).
        [TestCase(0, 3.4f, -12.5f, 8f)]
        [TestCase(2, 9.5f, -1f, -12f)]
        public void HeldTooHigh_TheMarbleGoesOverTheHole_AndTheLevelSaysSo(int index, float back, float pitch, float yaw)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[index];
            Prop marble = level.Marbles[index];
            List<string> said = Listen();
            Vector3 stand = Back(hole, back);
            Vector3 aim = stand + Vector3.up * Player.BaseEyeHeight + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * 30f;
            BotRunner.Run(Game, Carry(bot, marble, WayTo(index), stand, aim, 4f), 60f);
            Assert.AreEqual("LID", Where(marble), "it lies at " + marble.Center + ", " + marble.Scale);
            Assert.Greater(marble.Center.z, L.FunnelZ, "beyond the axis");
            Assert.Greater(new Vector2(marble.Center.x - hole.X, marble.Center.z - L.FunnelZ).magnitude, hole.Mouth, "behind the far rim");
            Assert.AreEqual(1, Count(said, L.OverLine), string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
        }

        // A marble put down on the lid away from the funnels, or carried past them, is nobody's failed attempt.
        [Test]
        public void AMarblePutDownOnTheLid_IsNotToldOff()
        {
            Bot bot = Load();
            L level = Level;
            List<string> said = Listen();
            // At the bot's own feet, looking toward the funnels; and on the lid three steps in front of the small one.
            BotRunner.Run(Game, Carry(bot, level.Peewee, NoWay, new Vector3(-4f, 0f, 0f), new Vector3(-4.5f, 0f, 1.5f), 2f), 30f);
            BotRunner.Run(Game, Carry(bot, level.Peewee, NoWay, new Vector3(-8f, 0f, 2f), new Vector3(-8f, 0f, 5f), 2f), 30f);
            // And against the far wall, well over the middle funnel.
            BotRunner.Run(Game, Carry(bot, level.Peewee, NoWay, L.StandM, new Vector3(-2f, 3f, L.FarZ), 3f), 30f);
            Assert.IsEmpty(said, string.Join(" | ", said));
        }

        static IEnumerator Again(Bot bot, Prop marble, Vector3 aim, float wait)
        {
            yield return bot.Grab(marble);
            yield return bot.DropAt(aim);
            yield return bot.Wait(wait);
        }

        // Too big: picked up again from where one stands it looks as big as it is; let go from closer it is smaller.
        [Test]
        public void TooBigForTheHole_PickedUpAgain_AndLetGoFromCloser_ItFits()
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[0];
            Prop marble = level.Peewee;
            List<string> said = Listen();
            BotRunner.Run(Game, Carry(bot, marble, NoWay, Back(hole, 5.5f), Spot(hole, hole.Mouth), 4f), 30f);
            Assert.AreEqual("MOUTH-S", Where(marble));
            Assert.AreEqual(1, Count(said, L.TooBigLine), string.Join(" | ", said));
            float tooBig = marble.Scale;

            BotRunner.Run(Game, Closer(bot, marble, Back(hole, 3.2f), L.AimS), 30f);
            Assert.AreEqual("IN-S", Where(marble), "from 3.2 back it is " + marble.Scale + " (it was " + tooBig + ")");
            Assert.Less(marble.Scale, tooBig);
        }

        static IEnumerator Closer(Bot bot, Prop marble, Vector3 stand, Vector3 aim)
        {
            yield return bot.Grab(marble);
            yield return bot.WalkTo(stand, 0.1f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(5f);
        }

        // Too small: it comes out of the port. Picked up from beside it there, it looks big; let go from the
        // same stand as before it is bigger than before.
        [Test]
        public void TooSmallForThePlate_FetchedFromThePort_AndLetGoFromFartherOff_ItFits()
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[0];
            Prop marble = level.Peewee;
            List<string> said = Listen();
            BotRunner.Run(Game, Carry(bot, marble, NoWay, Back(hole, 1.8f), Spot(hole, hole.Mouth), 9f), 40f);
            Assert.AreEqual(1, level.Port.Ejections, "it is " + Where(marble) + ", " + marble.Scale);
            Assert.AreEqual(1, Count(said, L.TooLightLine), string.Join(" | ", said));
            Assert.IsTrue(OnTheLid(marble), "it lies at " + marble.Center);
            float tooSmall = marble.Scale;
            Assert.Less(tooSmall, hole.MinScale);

            // Walk up to it, pick it up from two steps away, carry it back to the funnel.
            BotRunner.Run(Game, Fetch(bot, marble, 2.2f, L.StandS, L.AimS), 60f);
            Assert.AreEqual("IN-S", Where(marble), "let go from the solver's stand it is " + marble.Scale + " (it was " + tooSmall + ") at " + marble.Center);
        }

        static IEnumerator Fetch(Bot bot, Prop marble, float from, Vector3 stand, Vector3 aim)
        {
            Vector3 at = marble.Center;
            // Toward the middle of the lid from where it lies.
            Vector3 toward = new Vector3(-at.x, 0f, -2f - at.z).normalized;
            yield return bot.WalkTo(new Vector3(at.x, 0f, at.z) + toward * from, 0.1f, 20f);
            yield return bot.Grab(marble);
            yield return bot.WalkTo(stand, 0.1f, 20f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(5f);
        }

        [Test]
        public void PickedUpFromFarOff_TheLittleOneIsToldSo()
        {
            Bot bot = Load();
            L level = Level;
            List<string> said = Listen();
            // From the start it is a step away: nothing is said.
            BotRunner.Run(Game, bot.Grab(level.Peewee), 10f);
            BotRunner.Run(Game, bot.Drop(), 10f);
            RunSeconds(1f);
            Assert.IsEmpty(said, string.Join(" | ", said));

            Game.RestartLevel();
            bot = new Bot(Game);
            said = Listen();
            BotRunner.Run(Game, FromAfar(bot, level.Peewee), 20f);
            Assert.Less(Game.Grabber.Ratio, L.FarRatio, "from six steps off it looks " + Game.Grabber.Ratio + " of its distance across");
            Assert.AreEqual(1, Count(said, L.FarLine), string.Join(" | ", said));
        }

        static IEnumerator FromAfar(Bot bot, Prop marble)
        {
            yield return bot.WalkTo(new Vector3(3.5f, 0f, -5f + 3f), 0.2f);
            yield return bot.Grab(marble);
        }

        // ---- No way round the puzzle --------------------------------------------------------------------

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

        [Test]
        public void WalkingAndJumpingAtTheExit_DoesNotCompleteTheLevel()
        {
            Bot bot = Load();
            L level = Level;
            Player player = Game.Player;
            var exitFloor = new Vector3(L.ExitCentre.x, 0f, L.ExitCentre.z);

            // Straight at the exit, between the middle and the big funnel: the bot ends up against the gate.
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(L.BetweenMAndL, 0.3f, 20f), 30f);
            Assert.Throws<BotException>(() => BotRunner.Run(Game, bot.WalkTo(exitFloor, 0.5f, 5f), 20f));
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(player.Position.z, L.GateZ, "the shut gate stops the capsule (it is at " + player.Position + ")");

            // Sprinting and jumping at the gate, and along the far wall.
            float highest = 0f, farthest = 0f;
            Action track = () =>
            {
                highest = Mathf.Max(highest, player.Position.y);
                farthest = Mathf.Max(farthest, player.Position.z);
            };
            BotRunner.Run(Game, Push(bot, exitFloor, 3f, true, track), 10f);
            BotRunner.Run(Game, Push(bot, new Vector3(-1f, 0f, 30f), 2f, true, track), 10f);
            BotRunner.Run(Game, Push(bot, new Vector3(4f, 0f, 30f), 2f, true, track), 10f);
            Assert.Less(highest, 1.35f, "a jump from the lid rises 1.25: the niche's floor is at 6");
            Assert.Less(farthest, L.GateZ, "nobody got past the gate");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsTrue(level.Gate.IsClosed);
            Assert.IsTrue(level.Exit.Locked);
            Assert.AreEqual(0, level.Latched);
        }

        // Even inside the exit's own box nothing happens while the gate is shut: the exit is locked. And the
        // niche above the gate is a dead end.
        [Test]
        public void BehindTheShutGate_AndInTheNiche_TheLevelIsNotCompleted()
        {
            Load();
            L level = Level;
            Player player = Game.Player;
            // Fixture: inside the tunnel, in the exit's box.
            player.Teleport(new Vector3(L.ExitCentre.x, 0.02f, L.ExitCentre.z), 0f, 0f);
            RunSeconds(1f);
            Assert.IsFalse(Game.LevelCompleted, "the exit only opens with the gate");
            Assert.IsTrue(level.Exit.Locked);
            // Fixture: in the niche, beside the boulder.
            player.Teleport(new Vector3(L.NicheX0 + 0.4f, L.NicheY0 + 0.02f, L.FarZ + 0.5f), 0f, 0f);
            RunSeconds(1f);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(player.Position.y, L.NicheY0 - 0.1f, "the niche has a floor, and nothing under it but the tunnel's roof");
        }

        // All three throats are wider than the player. Whoever walks into a funnel slides down it, drops
        // into the chamber and steps out of the port; nothing is pressed by it.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void WhoeverFallsIntoAFunnel_StepsOutOfThePort(int holeIndex)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Player player = Game.Player;
            int respawns = 0;
            Vector3 put = default;
            Game.Events.PlayerRespawned += e =>
            {
                respawns++;
                put = e.To;
            };
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(hole, hole.Mouth + 1f), 0.3f, 20f), 30f);
            float lowest = 0f;
            BotRunner.Run(Game, Into(bot, hole.Axis, () => respawns > 0, () => lowest = Mathf.Min(lowest, player.Position.y)), 20f);
            Assert.AreEqual(1, respawns, "the chamber sends the player out of the port (the bot is at " + player.Position + ")");
            Assert.Less(lowest, hole.ThroatY - 0.2f, "after a drop through the throat");
            Assert.Greater(lowest, hole.FloorY - 0.1f);
            Assert.Less(Vector3.Distance(put, L.PortPlayer), 0.05f);
            RunSeconds(0.5f);
            Assert.Less(Vector3.Distance(player.Position, L.PortPlayer), 0.5f, "in front of the port, at " + player.Position);
            Assert.AreEqual(90f, player.Yaw, 1f, "facing the room");
            Assert.IsTrue(player.Grounded);
            Assert.AreEqual(1, level.Hazards[holeIndex].Catches);
            Assert.AreEqual(0, level.Latched, "the player is nothing a plate counts");
            Assert.IsFalse(Game.LevelCompleted);
        }

        static IEnumerator Into(Bot bot, Vector3 axis, Func<bool> done, Action eachTick)
        {
            // Keeps walking at the axis (and, once over it, just waits) until it is over.
            IEnumerator walk = bot.WalkTo(axis, 0.02f, 30f);
            bool walking = true;
            for (int tick = 0; tick < 600 && !done(); tick++)
            {
                if (walking) walking = walk.MoveNext();
                eachTick();
                yield return null;
            }
        }

        // With a marble in the hand it is the same, and the marble is still in the hand afterwards.
        [Test]
        public void FallingInWithAMarbleInHand_ThePlayerKeepsIt()
        {
            Bot bot = Load();
            L level = Level;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, bot.Grab(level.Peewee), 10f);
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(L.Holes[1], 3.5f), 0.3f, 20f), 30f);
            BotRunner.Run(Game, Into(bot, L.Holes[1].Axis, () => respawns > 0, () => { }), 20f);
            Assert.AreEqual(1, respawns);
            Assert.AreSame(level.Peewee, Game.Grabber.Held, "the marble is still in hand");
            Assert.AreEqual(0, level.Latched);
        }

        // ---- Restart and soft-locks ---------------------------------------------------------------------

        void AssertFresh(L level)
        {
            Assert.AreSame(level, Game.Level, "a restart builds the same level again");
            Assert.AreEqual(3, Game.Props.Count);
            Assert.IsTrue(level.Peewee.Frozen);
            Assert.AreEqual(L.PeeweeScale, level.Peewee.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Peewee.Center, L.PeeweeOrigin), 1e-3f);
            Assert.AreEqual(L.AggieScale, level.Aggie.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Aggie.Center, L.AggieOrigin), 0.02f);
            Assert.IsTrue(level.Shooter.Frozen);
            Assert.AreEqual(L.ShooterScale, level.Shooter.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Shooter.Center, L.ShooterOrigin), 1e-3f);
            foreach (Prop marble in level.Marbles) Assert.IsTrue(marble.Grabbable);
            Assert.AreEqual(0, level.Latched);
            Assert.AreEqual(0, level.Port.Ejections);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(level.Gauges[i].Enabled);
                Assert.AreSame(Palette.Amber.Name, level.Studs[i].Signal.Name);
            }
            Assert.IsTrue(level.Gate.IsClosed);
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

            // With the first marble on its plate and the second in hand.
            BotRunner.Run(Game, HalfWay(bot, level), 60f);
            Assert.AreEqual(1, level.Latched);
            Assert.AreSame(level.Aggie, Game.Grabber.Held);
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            // With a marble on its way down a funnel.
            bot = new Bot(Game);
            BotRunner.Run(Game, Carry(bot, level.Peewee, NoWay, L.StandS, L.AimS, 0.3f), 30f);
            Assert.AreEqual(0, level.Latched);
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            // With a marble in the port's queue.
            PutOver(1, level.Shooter, 0.6f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Waiting == 1, 4f));
            Game.RestartLevel();
            RunSeconds(1.5f);
            AssertFresh(level);

            // While the gate is on its way up.
            PutOver(0, level.Peewee, 0.6f);
            PutOver(1, level.Aggie, 1.3f);
            PutOver(2, level.Shooter, 2.5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Latched == 3, 8f));
            RunSeconds(0.3f);
            Assert.IsFalse(level.Gate.IsOpen, "the gate is on its way up");
            Assert.IsFalse(level.Gate.IsClosed);
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(0.65f, level.Peewee.Scale, 0.03f);

            // And once more after it has been solved.
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);
            TestHelpers.PlayLevel(Game, 90f);
        }

        static IEnumerator HalfWay(Bot bot, L level)
        {
            yield return bot.Grab(level.Peewee);
            yield return bot.WalkTo(L.PastTheSpool);
            yield return bot.WalkTo(L.StandS, 0.15f);
            yield return bot.DropAt(L.AimS);
            yield return bot.Until(() => level.Plates[0].Pressed, 8f);
            yield return bot.WalkTo(L.PickAggie, 0.15f);
            yield return bot.Grab(level.Aggie);
            yield return bot.Wait(0.2f);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AMarbleBelowTheKillPlane_ComesBackWhereItStarted_AsItStarted(int index)
        {
            Load();
            L level = Level;
            Prop marble = level.Marbles[index];
            Vector3 origin = index == 0 ? L.PeeweeOrigin : index == 1 ? L.AggieOrigin : L.ShooterOrigin;
            float scale = index == 0 ? L.PeeweeScale : index == 1 ? L.AggieScale : L.ShooterScale;
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            // Fixture: out of the world, at another size.
            Put(marble, 0.9f, new Vector3(40f, Game.KillY - 1f, 0f));
            Game.Tick();
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(scale, marble.Scale, 1e-4f, "at the size it started with");
            Assert.Less(Vector3.Distance(marble.Center, origin), 0.02f, "where it started (it is at " + marble.Center + ")");
            Assert.AreEqual(index != 1, marble.Frozen);
            TestHelpers.PlayLevel(Game, 90f);
        }

        // The niche is out of reach, and at the back of it a small marble is out of sight from the lid: one
        // left there goes back to where it started.
        [TestCase(0, 0.3f)]
        [TestCase(1, 0.3f)]
        [TestCase(1, 1.5f)]
        [TestCase(2, 0.8f)]
        public void AMarbleLeftInTheNiche_GoesBackToWhereItStarted(int index, float scale)
        {
            Load();
            L level = Level;
            Prop marble = level.Marbles[index];
            Vector3 origin = index == 0 ? L.PeeweeOrigin : index == 1 ? L.AggieOrigin : L.ShooterOrigin;
            float started = index == 0 ? L.PeeweeScale : index == 1 ? L.AggieScale : L.ShooterScale;
            if (index != 2) Assert.IsFalse(level.NicheLeash.OutOfBounds(marble));
            // Fixture: at the back of the niche (the boulder, if it is another marble, is in the way of nothing there).
            if (index != 2) Put(level.Shooter, 1f, new Vector3(-8f, 0.5f, -8f));
            Put(marble, scale, new Vector3(L.NicheX1 - 0.8f, L.NicheY0 + scale * 0.5f + 0.02f, L.NicheBack - 0.8f));
            RunSeconds(0.5f);
            Assert.IsTrue(level.NicheLeash.OutOfBounds(marble), "it lies in the niche, at " + marble.Center);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.NicheLeash.Returns == 1, 4f));
            Assert.AreEqual(started, marble.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(marble.Center, origin), 0.02f, "it is at " + marble.Center);
        }

        // Let go again the moment it was taken, the boulder is not where it was: the hold ends where it first
        // touches something, and that is the niche's sill. It comes down onto the lid, or stays in the niche
        // and is put back; either way it can be taken again.
        [Test]
        public void TheBoulderLetGoStraightAfterThePickUp_IsNotLost()
        {
            Bot bot = Load();
            L level = Level;
            Prop shooter = level.Shooter;
            BotRunner.Run(Game, GrabAndLetGo(bot, shooter), 30f);
            Assert.IsFalse(shooter.Frozen, "it was taken");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => shooter.Frozen || (shooter.Velocity.magnitude < 0.2f && shooter.Center.y < shooter.Radius + 0.4f), 12f),
                "it comes to rest on the lid or goes back to the niche (it is at " + shooter.Center + ", " + shooter.Scale + ")");
            Assert.IsFalse(level.NicheLeash.OutOfBounds(shooter));
            BotRunner.Run(Game, bot.Grab(shooter), 20f);
            Assert.AreSame(shooter, Game.Grabber.Held);
        }

        static IEnumerator GrabAndLetGo(Bot bot, Prop marble)
        {
            yield return bot.Grab(marble);
            yield return bot.Wait(0.2f);
            yield return bot.Drop();
        }

        // The jam rule (LEVELS.md): whatever lies still below a throat without a plate holding it is given
        // back. The plates do that themselves for everything they weigh; this is the net under them.
        [Test]
        public void AMarbleAtRestBelowAThroat_ThatNoPlateHolds_IsGivenBack()
        {
            Load();
            L level = Level;
            L.Hole hole = L.Holes[1];
            level.Plates[1].Enabled = false;
            PutOver(1, level.Peewee, 1.2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.JamLeash.Returns == 1, 6f), "the marble is " + Where(level.Peewee) + " at " + level.Peewee.Center);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 1, 2f));
            Assert.AreEqual(1.2f, level.Peewee.Scale, 1e-4f);
            Assert.IsFalse(level.Plates[1].Pressed);
            Assert.Greater(level.Peewee.Center.y, 0f, "it is out from under the lid, at " + level.Peewee.Center);
        }

        // Found while measuring (tools/out/notes/level08-probe-floor.txt, before the fix): the lid and the
        // funnels' floors are sheets, and a marble under 0.35 that came down a tube at 11 units a second or
        // more went through the chamber's floor, continuous collision detection or not, and lay on the bench
        // under the lid for good. Now there is solid ground under every sheet.
        [Test]
        public void ASmallMarbleDownATube_IsHeldByTheChambersFloor_AndComesBackOutOfThePort()
        {
            var failures = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                foreach (float scale in new[] { 0.2f, 0.25f, 0.3f, 0.34f })
                    foreach (float from in new[] { hole.ThroatY, 0.5f, 4f })
                    {
                        Load();
                        L level = Level;
                        Prop marble = level.Marbles[i];
                        Put(marble, scale, new Vector3(hole.X + 0.03f, from, L.FunnelZ));
                        float lowest = 99f;
                        for (int tick = 0; tick < 360 && level.Port.Ejections == 0; tick++)
                        {
                            Game.Tick();
                            lowest = Mathf.Min(lowest, marble.Center.y);
                        }
                        string what = hole.Name + ", " + scale + " from " + from + ": lowest " + lowest + ", " + Where(marble) + " at " + marble.Center;
                        // (At 16 units a second its middle dips a tenth into the block under the floor for a tick.)
                        if (lowest < hole.FloorY - 0.2f) failures.Add("through the floor - " + what);
                        else if (level.Port.Ejections != 1) failures.Add("not given back - " + what);
                        else if (level.Rescues != 0) failures.Add("by the net, not by the plate - " + what);
                    }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ASmallMarbleFromHighUp_DoesNotGoThroughTheLid()
        {
            var failures = new List<string>();
            foreach (float scale in new[] { 0.2f, 0.3f, 0.5f })
                foreach (Vector3 at in new[] { new Vector3(-3f, 13f, -3f), new Vector3(3.3f, 13f, 6.1f), new Vector3(1.4f, 13f, 9f), new Vector3(-12f, 13f, 14f), new Vector3(-5.5f, 9f, 9f) })
                {
                    Load();
                    L level = Level;
                    Prop marble = level.Aggie;
                    Put(marble, scale, at);
                    float lowest = 99f;
                    for (int tick = 0; tick < 240; tick++)
                    {
                        Game.Tick();
                        lowest = Mathf.Min(lowest, marble.Center.y);
                    }
                    if (lowest < 0f || !OnTheLid(marble) || level.Rescues != 0)
                        failures.Add(scale + " from " + at + ": lowest " + lowest + ", ends at " + marble.Center + ", rescues " + level.Rescues);
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // The net under everything: whatever does get under the lid outside a funnel is given back by the port.
        [TestCase(0f, -3f, 0f)]
        [TestCase(-7f, -1.5f, 8.4f)]
        [TestCase(4f, -4f, 10f)]
        [TestCase(-12f, -5f, -10f)]
        public void AMarbleUnderTheLid_IsGivenBackByThePort(float x, float y, float z)
        {
            Load();
            L level = Level;
            Prop marble = level.Peewee;
            var under = new Vector3(x, y, z);
            Assert.IsTrue(L.UnderTheLid(under), under + " is under the lid, outside the funnels");
            Put(marble, 0.4f, under);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 1, 2f), "it is at " + marble.Center);
            Assert.AreEqual(1, level.Rescues);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => marble.Velocity.magnitude < 0.1f && OnTheLid(marble), 10f), "it is at " + marble.Center);
            Assert.AreEqual(0.4f, marble.Scale, 1e-4f);
            Assert.AreEqual(1, level.Rescues, "once");
        }

        [Test]
        public void InsideAFunnel_IsNotUnderTheLid()
        {
            foreach (L.Hole hole in L.Holes)
            {
                Assert.IsFalse(L.UnderTheLid(new Vector3(hole.X, hole.FloorY + 0.1f, L.FunnelZ)), hole.Name + ": on the plate");
                Assert.IsFalse(L.UnderTheLid(new Vector3(hole.X + hole.Throat * 0.9f, hole.TubeY, L.FunnelZ)), hole.Name + ": in the tube");
                float wall = (hole.Throat + hole.Mouth) * 0.5f;
                Assert.IsFalse(L.UnderTheLid(new Vector3(hole.X, hole.ConeY(wall) + 0.1f, L.FunnelZ + wall)), hole.Name + ": on the cone");
                Assert.IsTrue(L.UnderTheLid(new Vector3(hole.X, hole.ConeY(wall) - 0.3f, L.FunnelZ + wall)), hole.Name + ": under the cone");
                Assert.IsTrue(L.UnderTheLid(new Vector3(hole.X + hole.Throat + 0.3f, hole.FloorY + 0.3f, L.FunnelZ)), hole.Name + ": beside the chamber");
                Assert.IsFalse(L.UnderTheLid(new Vector3(hole.X + hole.Mouth + 1f, 0.2f, L.FunnelZ)), "on the lid");
            }
        }

        // A marble parked in the port's alcove would be in the way of whatever comes out next (the port waits
        // while its mouth is taken): the port keeps nothing, it rolls out again.
        [Test]
        public void AMarbleLeftInThePortsAlcove_IsRolledOut_AndThePortGoesOnGivingBack()
        {
            Load();
            L level = Level;
            Put(level.Peewee, 0.9f, new Vector3(L.PortMouth.x, 0.46f, L.PortZ));
            RunSeconds(0.2f);
            Assert.IsTrue(L.InThePort(level.Peewee.Center), "fixture: it lies in the alcove, at " + level.Peewee.Center);
            // Another one falls through the middle hole meanwhile, too small for its plate.
            PutOver(1, level.Aggie, 0.6f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 1, 8f), "the port gave the second one back (waiting " + level.Port.Waiting + ", the first is at " + level.Peewee.Center + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => OnTheLid(level.Peewee) && OnTheLid(level.Aggie) && !L.InThePort(level.Peewee.Center) && !L.InThePort(level.Aggie.Center)
                                                           && level.Peewee.Velocity.magnitude < 0.1f && level.Aggie.Velocity.magnitude < 0.1f, 12f),
                "both lie on the lid in front of the port: " + level.Peewee.Center + ", " + level.Aggie.Center);
            Assert.AreEqual(0.9f, level.Peewee.Scale, 1e-4f);
            Assert.AreEqual(0.6f, level.Aggie.Scale, 1e-4f);
        }

        // What the port gives back does not start inside a wall: a marble too big for the alcove appears in front of it.
        [TestCase(0.3f)]
        [TestCase(1.9f)]
        [TestCase(3.1f)]
        public void WhatComesOutOfThePort_HasRoom(float scale)
        {
            Load();
            L level = Level;
            Prop marble = level.Aggie;
            Put(marble, scale, new Vector3(0f, scale * 0.5f, 0f));
            level.Port.Eject(marble);
            Game.Tick();
            Assert.AreEqual(1, level.Port.Ejections);
            Assert.Less(TestHelpers.DeepestOverlap(Game, marble), 0.02f, "a marble of " + scale + " at " + marble.Center + " is clear of the walls");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => marble.Velocity.magnitude < 0.1f && OnTheLid(marble), 10f), "it is at " + marble.Center);
            Assert.That(marble.Center.x, Is.InRange(-L.HalfX + scale * 0.5f, 0f), "on the lid, a few steps into the room");
            Assert.AreEqual(scale, marble.Scale, 1e-4f);
        }

        // A marble that is nearly as big as it can get is still not something to climb out on: the lid's walls
        // stand, unseen, up to the sky cap, and the niche is a dead end.
        [Test]
        public void TheBiggestMarble_StaysOnTheLid()
        {
            Load();
            L level = Level;
            Prop marble = level.Aggie;
            Put(marble, L.BiggestMarble, new Vector3(-6f, 4f, -4f));
            RunSeconds(3f);
            Assert.IsTrue(OnTheLid(marble), "a marble of 6 lies at " + marble.Center);
            Put(marble, 40f, new Vector3(0f, 4f, 0f));
            Assert.AreEqual(L.BiggestMarble, marble.Scale, 1e-4f, "the clamp");
        }

        // ---- The words ----------------------------------------------------------------------------------

        [Test]
        public void TheText_IsPlainAscii_PicksUpAndLetsGo_AndOnlyTheLastHintIsTheRecipe()
        {
            Load();
            L level = Level;
            var texts = new List<string> { level.Blurb, level.Title, L.TooBigLine, L.TooLightLine, L.BusyLine, L.TakenLine, L.ShortLine, L.CloserLine, L.OverLine, L.FarLine };
            texts.AddRange(level.Hints);
            foreach (string text in texts)
            {
                Assert.IsNotEmpty(text);
                foreach (char c in text) Assert.That((int)c, Is.InRange(32, 126), "'" + c + "' in: " + text);
                StringAssert.DoesNotContain("grab", text.ToLowerInvariant(), text);
                StringAssert.DoesNotContain("drop", text.ToLowerInvariant(), text);
            }
            Assert.AreEqual("Three holes. Three marbles. None of them the right size.", level.Blurb);
            Assert.AreEqual("It only ever gets as big as it looks. Pick it up from closer.", L.FarLine, "the campaign's sentence for a pick-up from too far away");
            // The first hint names the rule and nothing else; the second how a marble changes size; only the
            // third says which marble, picked up where, goes into which hole, and when to let go.
            foreach (string colour in new[] { "red", "yellow", "purple", "start" })
            {
                StringAssert.DoesNotContain(colour, level.Hints[0].ToLowerInvariant());
                StringAssert.DoesNotContain(colour, level.Hints[1].ToLowerInvariant());
                StringAssert.Contains(colour, level.Hints[2].ToLowerInvariant());
            }
            StringAssert.DoesNotContain("far side", level.Hints[0]);
            StringAssert.Contains("far side", level.Hints[1]);
            StringAssert.Contains("far side", level.Hints[2]);
            StringAssert.Contains("pick up", level.Hints[2].ToLowerInvariant());
            StringAssert.Contains("green", level.Hints[2]);
            StringAssert.Contains("let go", level.Hints[2]);
            // The hint panel holds about 210 letters at full size (the longest hint of levels 1 to 7 has 208);
            // more and the text is set smaller. The toast takes three lines.
            foreach (string hint in level.Hints) Assert.LessOrEqual(hint.Length, 210, hint);
            foreach (string line in new[] { L.TooBigLine, L.TooLightLine, L.BusyLine, L.TakenLine, L.ShortLine, L.CloserLine, L.OverLine, L.FarLine })
                Assert.LessOrEqual(line.Length, 125, line);
        }

        // ---- What it costs to draw ------------------------------------------------------------------------

        // The level with its room and every presenter, counted before culling (ART_BIBLE 12.1). The walls
        // are many boxes: the ones whose shadow nobody sees cast none, and paint, studs and trim never do.
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
                        Assert.Greater(census.Renderers, 40, "the level is drawn: " + census);
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

        // The middle of the far side of a funnel's cone, as seen from -Z: where the dashes are painted.
        static Vector3 FarSide(L.Hole hole) => Spot(hole, (hole.Throat + hole.Mouth) * 0.5f);

        // What the third hint says to do with a marble in the hand: hold it over the far side of its hole and
        // step forward (or back) until the lamps turn green; then let go. Nothing but the lamps is read.
        static IEnumerator ByTheLamps(Bot bot, L level, int holeIndex, float from, float to)
        {
            L.Hole hole = L.Holes[holeIndex];
            float step = to < from ? -0.25f : 0.25f;
            for (float back = from; step < 0f ? back >= to - 1e-3f : back <= to + 1e-3f; back += step)
            {
                yield return bot.WalkTo(Back(hole, back), 0.08f, 20f);
                // Over the far side: over the dashes on the cone, or a little higher, over the far rim.
                for (int aim = 0; aim < 2; aim++)
                {
                    yield return bot.LookAt(aim == 0 ? FarSide(hole) : Spot(hole, hole.Mouth));
                    yield return bot.Wait(0.05f);
                    if (level.Gauges[holeIndex].State != FitState.Good) continue;
                    Assert.AreSame(Palette.Go.Name, level.Studs[holeIndex].Signal.Name, "the studs say what the lamp says");
                    yield return bot.Drop();
                    yield return bot.Until(() => level.Plates[holeIndex].Pressed, 10f);
                    yield break;
                }
            }
            Assert.Fail("the lamps of funnel " + hole.Name + " never turned green between " + from + " and " + to + " back (the marble in the hand is " + bot.Game.Grabber.Held?.Scale + ")");
        }

        static IEnumerator Take(Bot bot, Prop marble, params Vector3[] wayToPick)
        {
            foreach (Vector3 point in wayToPick) yield return bot.WalkTo(point, 0.15f, 20f);
            yield return bot.Grab(marble);
            if (bot.Player.Position.z < L.PastTheSpool.z - 0.5f) yield return bot.WalkTo(L.PastTheSpool, 0.2f);
        }

        // The third hint, done to the letter: red and yellow picked up at the start, purple from the middle of
        // the room, each held over the far side of its hole from wherever the lamps are green.
        [TestCase(true)]
        [TestCase(false)]
        public void TheThirdHint_IsARecipeThatWorks(bool steppingForward)
        {
            Bot bot = Load();
            L level = Level;
            List<string> said = Listen();
            BotRunner.Run(Game, ByTheHint(bot, level, steppingForward), 240f);
            Assert.IsTrue(Game.LevelCompleted);
            for (int i = 0; i < 3; i++) Assert.AreSame(level.Marbles[i], level.Plates[i].PressedBy);
            Assert.AreEqual(0, level.Port.Ejections, "nothing went through a hole it did not fit");
            Assert.IsEmpty(said, "green lamps are never followed by a telling-off: " + string.Join(" | ", said));
        }

        static IEnumerator ByTheHint(Bot bot, L level, bool steppingForward)
        {
            float[] far = { 8f, 9f, 13f }, near = { 1.5f, 2.3f, 4.2f };
            // "Pick up red and yellow where you start..."
            yield return bot.Grab(level.Peewee);
            yield return bot.WalkTo(L.PastTheSpool, 0.2f);
            yield return ByTheLamps(bot, level, 0, steppingForward ? far[0] : near[0], steppingForward ? near[0] : far[0]);
            yield return bot.WalkTo(L.PastTheSpool, 0.3f, 20f);
            yield return bot.WalkTo(L.SpawnPoint, 0.2f);
            yield return bot.Grab(level.Aggie);
            yield return bot.WalkTo(L.PastTheSpool, 0.2f);
            yield return ByTheLamps(bot, level, 1, steppingForward ? far[1] : near[1], steppingForward ? near[1] : far[1]);
            // "...purple from the middle of the room."
            yield return bot.WalkTo(new Vector3(0f, 0f, (L.NearZ + L.FarZ) * 0.5f), 0.2f, 20f);
            yield return bot.Grab(level.Shooter);
            yield return ByTheLamps(bot, level, 2, steppingForward ? far[2] : near[2], steppingForward ? near[2] : far[2]);
            yield return bot.Until(() => level.Gate.IsOpen, 3f);
            yield return WalkOut(bot);
        }

        // The lamps are enough for other pick-ups too: wherever a marble was picked up, somewhere the lamps
        // turn green for it - or the level has said why not.
        [TestCase(0, 0.9f, 0f, 0)]
        [TestCase(0, 2.5f, 0f, 0)]
        [TestCase(0, 3.5f, 0f, 0)]
        [TestCase(1, -2.2f, 0f, 1)]
        [TestCase(1, -3f, 4f, 1)]
        [TestCase(1, -6f, 6f, 1)]
        [TestCase(2, 1.5f, 9f, 2)]
        [TestCase(2, 6.5f, -3f, 2)]
        [TestCase(2, 5f, -8f, 2)]
        [TestCase(2, 0f, -11f, 2)]
        public void PickedUpFromElsewhere_TheLampsStillFindTheSpot(int index, float pickX, float pickZ, int holeIndex)
        {
            Bot bot = Load();
            L level = Level;
            Prop marble = level.Marbles[index];
            // The little one and the middling one: from beside where they lie (an offset). The boulder: from a spot on the lid.
            Vector3 pick = index == 0 ? new Vector3(L.SpoolBase.x + pickX, 0f, L.SpoolBase.z)
                : index == 1 ? new Vector3(L.AggieOrigin.x + pickX, 0f, L.AggieOrigin.z + pickZ)
                : new Vector3(pickX, 0f, pickZ);
            float looked = 0f;
            Game.Events.PropGrabbed += e => looked = e.OldScale / e.GrabDistance;
            BotRunner.Run(Game, Take(bot, marble, L.PastTheSpool, pick), 60f);
            BotRunner.Run(Game, ByTheLamps(bot, level, holeIndex, holeIndex == 2 ? 13f : 9f, L.Holes[holeIndex].Mouth + 0.4f), 120f);
            Assert.AreSame(marble, level.Plates[holeIndex].PressedBy, "picked up from " + pick + " it looked " + looked + " of its distance across");
            Assert.AreEqual(0, level.Port.Ejections);
        }

        // ---- The review's attacks (2026-10-04; tools/out/notes/level08-review.md) -------------------------

        // Found by the review: the lamps used to read a marble within half a step of the mouth as well. One
        // that the lid had stopped in front of the hole, or that had gone over it, could be the right size
        // there - green lamps - and lay on the lid when it was let go (11 of 296 let-gos measured). Now they
        // read a marble whose middle is over the mouth, and say nothing about any other.
        [TestCase(0, 4.2f, 0.4f)]
        [TestCase(0, 5f, 0.7f)]
        [TestCase(0, 5.5f, 0.7f)]
        [TestCase(0, 2.6f, 2.2f)]
        [TestCase(0, 3f, 2.2f)]
        [TestCase(1, 5.8f, 1.35f)]
        [TestCase(1, 7.5f, 1.9f)]
        public void TheLampsStayDark_ForAMarbleOfTheRightSize_ThatIsNotOverTheHole(int index, float back, float spot)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[index];
            Prop marble = level.Marbles[index];
            List<string> said = Listen();
            BotRunner.Run(Game, Hold(bot, marble, WayTo(index), Back(hole, back), Spot(hole, spot)), 60f);
            float off = new Vector2(marble.Center.x - hole.X, marble.Center.z - L.FunnelZ).magnitude;
            Assert.Greater(off, hole.Mouth, "the marble in the hand is not over the mouth (it is at " + marble.Center + ")");
            Assert.AreEqual(FitState.Good, level.Gauges[index].Fit(marble.Scale), "though it is the right size there: " + marble.Scale);
            Assert.AreEqual(FitState.Idle, level.Gauges[index].State, "the lamp says nothing");
            Assert.AreSame(Palette.Amber.Name, level.Studs[index].Signal.Name, "and the studs keep waiting");
            BotRunner.Run(Game, LetGo(bot, 5f), 30f);
            Assert.AreEqual("LID", Where(marble), "let go, it lies on the lid, at " + marble.Center);
            Assert.AreEqual(1, said.Count, "and the level says why, once: " + string.Join(" | ", said));
            Assert.That(said[0], Is.EqualTo(L.ShortLine).Or.EqualTo(L.CloserLine).Or.EqualTo(L.OverLine));
        }

        // Green means: let go now and it goes in and presses the plate. Red or dark: it does not press it.
        // (A hair under the throat's size the lamp is red and the marble still goes in: it errs to that side.)
        [Test]
        public void WhatTheLampsSay_IsWhatHappens()
        {
            var wrong = new List<string>();
            int green = 0;
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                float[] backs = i == 0 ? new[] { 2.2f, 3f, 3.8f, 4.6f, 5.4f } : i == 1 ? new[] { 2.6f, 3.6f, 4.6f, 5.6f, 6.6f } : new[] { 6f, 7.5f, 9f, 10.5f, 12f };
                float[] spots = { 0f, hole.Throat, (hole.Throat + hole.Mouth) * 0.5f, hole.Mouth, hole.Mouth * 1.5f };
                foreach (float back in backs)
                    foreach (float spot in spots)
                    {
                        Bot bot = Load();
                        L level = Level;
                        Prop marble = level.Marbles[i];
                        BotRunner.Run(Game, Hold(bot, marble, WayTo(i), Back(hole, back), Spot(hole, spot)), 60f);
                        FitState lamp = level.Gauges[i].State;
                        float held = marble.Scale;
                        BotRunner.Run(Game, LetGo(bot, i == 2 ? 7f : 5f), 30f);
                        bool pressed = level.Plates[i].Pressed;
                        if (lamp == FitState.Good) green++;
                        if (lamp == FitState.Good != pressed && !(pressed && lamp == FitState.TooBig && held > hole.PassBelow * 0.99f))
                            wrong.Add(hole.Name + " from " + back + " back, held over " + spot + ": lamp " + lamp + ", scale " + held + ", it is " + Where(marble) + " at " + marble.Center);
                    }
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
            Assert.Greater(green, 20, "the grid has green cells at every funnel");
        }

        // Where the lamps have nothing to say - the marble's picture is over a funnel, the marble is not over
        // its mouth - the dashes on the funnel's far side blink: hold it over here.
        [Test]
        public void HeldWhereItWouldLieOnTheLid_TheFarSideOfTheFunnelBeckons()
        {
            Bot bot = Load();
            L level = Level;
            L.Hole s = L.Holes[0];
            for (int i = 0; i < 3; i++) Assert.IsFalse(level.Beckons(i), "nothing in the hand");

            // Over the throat from the solver's stand: the lid stops it in front of the hole.
            BotRunner.Run(Game, Hold(bot, level.Peewee, NoWay, L.StandS, Spot(s, 0f)), 30f);
            Assert.AreEqual(FitState.Idle, level.Gauges[0].State);
            Assert.IsTrue(level.Beckons(0), "the small funnel's far side beckons (the marble is at " + level.Peewee.Center + ")");
            Assert.IsFalse(level.Beckons(1));
            Assert.IsFalse(level.Beckons(2));
            Assert.AreSame(Palette.Amber.Name, level.Studs[0].Signal.Name);

            // Over the far side: the lamps take over.
            BotRunner.Run(Game, HoldOver(bot, level.Peewee, L.StandS, L.AimS), 30f);
            Assert.AreEqual(FitState.Good, level.Gauges[0].State);
            Assert.IsFalse(level.Beckons(0), "over the mouth the lamps speak");

            // Over the far side from too far off (the lid stops it before the rim again): it beckons again.
            BotRunner.Run(Game, HoldOver(bot, level.Peewee, Back(s, 5.5f), Spot(s, 0.7f)), 30f);
            Assert.AreEqual(FitState.Idle, level.Gauges[0].State);
            Assert.IsTrue(level.Beckons(0), "from too far off");

            // Looking somewhere else altogether: nothing.
            BotRunner.Run(Game, bot.LookAt(new Vector3(0f, 3f, L.NearZ)), 10f);
            RunSeconds(0.1f);
            for (int i = 0; i < 3; i++) Assert.IsFalse(level.Beckons(i), "looking away");
            BotRunner.Run(Game, bot.LookAt(Back(s, 3.5f)), 10f);
            RunSeconds(0.1f);
            Assert.IsFalse(level.Beckons(0), "looking at the lid well before the funnel");

            // A hole that has its marble does not ask for another.
            BotRunner.Run(Game, HoldOver(bot, level.Peewee, L.StandS, L.AimS), 30f);
            BotRunner.Run(Game, LetGo(bot, 3f), 30f);
            Assert.IsTrue(level.Plates[0].Pressed);
            BotRunner.Run(Game, Take(bot, level.Aggie, L.PickAggie), 30f);
            BotRunner.Run(Game, HoldOver(bot, level.Aggie, L.StandS, Spot(s, 0f)), 30f);
            Assert.IsFalse(level.Beckons(0), "the small hole is taken");
        }

        // Held over the far side from too far off, a marble's underside still meets the lid before the hole:
        // holding it higher does not help, going closer does.
        [TestCase(0, 5f, 0.7f)]
        [TestCase(1, 7.5f, 1.9f)]
        [TestCase(2, 12f, 3.6f)]
        [TestCase(2, 13f, 5f)]
        public void HeldOverTheFarSideFromTooFarOff_ItMeetsTheLidFirst_AndTheLevelSaysGoCloser(int index, float back, float spot)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[index];
            Prop marble = level.Marbles[index];
            List<string> said = Listen();
            BotRunner.Run(Game, Carry(bot, marble, WayTo(index), Back(hole, back), Spot(hole, spot), 4f), 60f);
            Assert.AreEqual("LID", Where(marble), "it lies at " + marble.Center + ", " + marble.Scale);
            Assert.Less(marble.Center.z, L.FunnelZ - hole.Mouth, "in front of the near rim");
            Assert.AreEqual(1, Count(said, L.CloserLine), string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
        }

        // The view over a funnel, as the level reads it when a marble is let go.
        [Test]
        public void SeenOver_IsTheFunnelTheMarblesPictureLiesOver()
        {
            L.Hole s = L.Holes[0], l = L.Holes[2];
            Vector3 eye = Back(s, 3.4f) + Vector3.up * Player.BaseEyeHeight;
            int Over(Vector3 from, Vector3 aim, float ratio, out bool beyond) => L.SeenOver(from, (aim - from).normalized, ratio, out beyond);
            Assert.AreEqual(0, Over(eye, Spot(s, 0f), 0.15f, out bool beyond), "into the throat");
            Assert.IsFalse(beyond, "the view meets the lid before the axis");
            Assert.AreEqual(0, Over(eye, Spot(s, 0.7f), 0.15f, out beyond), "at the far side of the cone");
            Assert.IsTrue(beyond);
            Assert.AreEqual(-1, Over(eye, Back(s, 3f), 0.15f, out beyond), "at the lid two steps before the rim");
            Assert.AreEqual(-1, Over(eye, eye + new Vector3(0f, 0.5f, 5f), 0.15f, out beyond), "upward");
            Assert.AreEqual(-1, Over(eye, new Vector3(s.X + 3f, 0f, L.FunnelZ), 0.15f, out beyond), "at the lid beside the funnel");
            // A big picture reaches the mouth from farther below it than a small one.
            Vector3 beforeTheRim = Back(l, l.Mouth + 0.8f);
            Vector3 far = Back(l, 10f) + Vector3.up * Player.BaseEyeHeight;
            Assert.AreEqual(-1, Over(far, beforeTheRim, 0.02f, out beyond));
            Assert.AreEqual(2, Over(far, beforeTheRim, 0.4f, out beyond));
            Assert.AreEqual(-1, L.SeenOver(new Vector3(l.X, -0.5f, L.FunnelZ), Vector3.forward, 0.2f, out beyond), "from inside a funnel");
        }

        // Found by the review: at 1.2 (LEVELS.md) the middling one fitted the middle hole as it lay, and it is
        // light enough to be shoved along: walked into for eleven seconds it rolled across the lid, into the
        // funnel and onto its plate - a third of the puzzle without the perspective. At 0.9 that teaches
        // instead: too light for the middle hole (out of the port), too big for the small one.
        [TestCase(1, "port")]
        [TestCase(0, "MOUTH-S")]
        public void TheMiddlingOne_RolledIntoAHoleAsItLies_DoesNotPressAPlate(int holeIndex, string expected)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Prop marble = level.Aggie;
            List<string> said = Listen();
            bool inTheHole() => level.Port.Ejections > 0 || level.Latched > 0 || (Where(marble).StartsWith("MOUTH") && marble.Velocity.magnitude < 0.3f);
            BotRunner.Run(Game, PushTo(bot, marble, hole.Axis, 60f, inTheHole), 90f);
            Assert.IsNull(Game.Grabber.Held, "nothing was picked up");
            RunSeconds(4f);
            string where = Where(marble);
            if (where == "LID" && level.Port.Ejections > 0) where = "port";
            Assert.AreEqual(expected, where, "shoved to hole " + hole.Name + " it is at " + marble.Center + " after " + Game.Time + " s");
            Assert.AreEqual(L.AggieScale, marble.Scale, 1e-4f, "as big as it was");
            Assert.AreEqual(0, level.Latched, "no plate is pressed by a marble nobody has sized");
            Assert.AreEqual(1, Count(said, holeIndex == 1 ? L.TooLightLine : L.TooBigLine), string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
        }

        // Found by the review: two marbles each a hair under the big throat's size - the second lies on the
        // first, its middle above the plate's sensor, and nothing was said. And a marble too big for a hole
        // that is taken was told to change its size. Both stay where they are and can be picked up.
        [TestCase(2, 0.985f, 0.985f)]
        [TestCase(0, 0.8f, 1.6f)]
        [TestCase(1, 0.8f, 3f)]
        public void AMarbleLeftInTheMouthOfAHoleThatIsTaken_IsToldSo(int holeIndex, float firstShare, float secondShare)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Prop first = level.Marbles[holeIndex], second = level.Marbles[(holeIndex + 1) % 3];
            PutOver(holeIndex, first, hole.PassBelow * firstShare);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[holeIndex].Pressed, 6f));
            RunSeconds(0.5f);
            List<string> said = Listen();
            PutOver(holeIndex, second, hole.PassBelow * secondShare);
            RunSeconds(6f);
            Assert.AreEqual("MOUTH-" + hole.Name, Where(second), "the second one is at " + second.Center);
            Assert.AreEqual(1, Count(said, L.TakenLine), string.Join(" | ", said));
            Assert.AreEqual(1, said.Count);
            Assert.AreEqual(0, level.Port.Ejections);
            Assert.AreSame(first, level.Plates[holeIndex].PressedBy);
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(hole, hole.Mouth + 1.5f), 0.3f, 20f), 30f);
            BotRunner.Run(Game, bot.Grab(second), 10f);
            Assert.AreSame(second, Game.Grabber.Held);
        }

        // Nothing but the three marbles answers a click: not the gate, not the lamps, not the things left lying about.
        [Test]
        public void OnlyTheMarblesCanBePickedUp()
        {
            Load();
            L level = Level;
            Player player = Game.Player;
            Assert.AreEqual(3, Game.Props.Count);
            var things = new List<Vector3>
            {
                new Vector3((L.GateX0 + L.GateX1) * 0.5f, 1.2f, L.GateZ - L.GateThickness * 0.5f),      // the gate
                new Vector3((L.GateX0 + L.GateX1) * 0.5f, 0.35f, L.GateZ - L.GateThickness * 0.5f),     // its amber bar
                L.SpoolBase + Vector3.up * 0.5f,                                                         // the little one's spool
                new Vector3(-11.8f, 0.55f, -11.1f), new Vector3(-9.6f, 0.65f, -10.9f),                   // the blocks
                new Vector3(10.6f, 0.45f, -11.1f),                                                       // the marker
                new Vector3(13.1f, 0.06f, -3f),                                                          // the ruler
                new Vector3(-12.9f, 1f, 14.6f), new Vector3(12.6f, 0.7f, 14.9f),                         // the dominoes, the spool on its side
                new Vector3(-L.HalfX, L.PortHeight + 0.1f, L.PortZ),                                     // the port's frame
            };
            foreach (L.Hole hole in L.Holes)
            {
                things.Add(hole.LampPosition);
                things.Add(new Vector3(hole.X, hole.PostHeight * 0.5f, L.FunnelZ + hole.Mouth + 0.45f));  // its post
                things.Add(new Vector3(hole.X, hole.ConeY((hole.Throat + hole.Mouth) * 0.5f), L.FunnelZ + (hole.Throat + hole.Mouth) * 0.5f)); // its cone
            }
            // (Fixture: the eye is put where each thing is in plain view.)
            foreach (Vector3 stand in new[] { L.SpawnPoint, new Vector3(0f, 0f, 2f), new Vector3(-6f, 0f, 3f), new Vector3(1.4f, 0f, 12f) })
            {
                player.Teleport(stand, 0f, 0f);
                Game.Tick();
                foreach (Vector3 thing in things)
                {
                    TestHelpers.LookAt(player, thing);
                    Prop target = Game.Grabber.FindTarget();
                    // (A marble may answer: the click is forgiving, and takes the little one from a hand's breadth beside it.)
                    Assert.IsTrue(target == null || target == level.Peewee || target == level.Aggie || target == level.Shooter, "from " + stand + " looking at " + thing + " a click would take " + (target != null ? target.Name : "nothing"));
                }
            }
        }

        // ---- Nothing is lost, nobody is stuck -------------------------------------------------------------

        // Is the marble in plain view from somewhere a player can stand on the lid?
        bool SeenFromTheLid(Prop marble)
        {
            Vector3 centre = marble.Center;
            float radius = marble.Radius;
            Vector3[] spots = { centre, centre + Vector3.up * radius * 0.9f, centre + Vector3.right * radius * 0.9f, centre - Vector3.right * radius * 0.9f, centre - Vector3.forward * radius * 0.9f };
            for (float x = -L.HalfX + 0.6f; x <= L.HalfX - 0.6f; x += 1.6f)
                for (float z = L.NearZ + 0.6f; z <= L.FarZ - 0.6f; z += 1.6f)
                {
                    bool standable = true;
                    foreach (L.Hole hole in L.Holes) standable &= new Vector2(x - hole.X, z - L.FunnelZ).magnitude > hole.Mouth + 0.4f;
                    if (!standable) continue;
                    var eye = new Vector3(x, Player.BaseEyeHeight, z);
                    foreach (Vector3 spot in spots)
                    {
                        Vector3 to = spot - eye;
                        if (Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude + 0.5f, Layers.SolidMask, QueryTriggerInteraction.Ignore) && PropRef.Of(hit.collider) == marble) return true;
                    }
                }
            return false;
        }

        // The places a marble could be let go in the hope of losing it: (where to stand, where to look).
        static readonly Vector3[][] OddPlaces =
        {
            new[] { new Vector3(0f, 0f, 4f), new Vector3(1.5f, 1.5f, 16.4f) },      // at the gate
            new[] { new Vector3(1.4f, 0f, 9f), new Vector3(1.5f, 0.3f, 16.4f) },    // at the foot of the gate, from between the funnels
            new[] { new Vector3(0f, 0f, 4f), new Vector3(1.5f, 9f, 20f) },          // into the niche
            new[] { new Vector3(3f, 0f, 2f), new Vector3(4f, 6.3f, 16.5f) },        // onto the niche's sill
            new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 13.9f, 5f) },          // at the sky
            new[] { new Vector3(4f, 0f, 4f), new Vector3(5.4f, 12.8f, 16f) },       // at the top corner of the tower
            new[] { new Vector3(-9f, 0f, 0f), new Vector3(-16.2f, 0.8f, 0f) },      // into the port
            new[] { new Vector3(-9f, 0f, 3f), new Vector3(-14f, 2.6f, 0f) },        // at the port's frame
            new[] { new Vector3(0f, 0f, 0f), new Vector3(-14f, 9f, 5f) },           // high up the left wall
            new[] { new Vector3(0f, 0f, 0f), new Vector3(14f, 5.2f, 12f) },         // at the top of the drawn part of the right wall
            new[] { new Vector3(0f, 0f, 0f), new Vector3(-12.9f, 1f, 14.6f) },      // behind the dominoes in the far left corner
            new[] { new Vector3(0f, 0f, 0f), new Vector3(12.9f, 0.4f, 15.6f) },     // behind the spool in the far right corner
            new[] { new Vector3(0f, 0f, -2f), new Vector3(-12.6f, 0.3f, -11.6f) },  // behind the blocks at the back
            new[] { new Vector3(0f, 0f, 0f), new Vector3(6.5f, 3.7f, 13.2f) },      // onto the big funnel's lamp
            new[] { new Vector3(-8f, 0f, 14.5f), new Vector3(-8f, 0.2f, 10.75f) },  // behind the small funnel's post, from behind
            new[] { new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5.3f) },           // at one's own feet
            new[] { new Vector3(-10.5f, 0f, 13f), new Vector3(-13.8f, 0f, 15.8f) }, // into the far left corner, past the dominoes
            new[] { new Vector3(11f, 0f, 13.5f), new Vector3(13.8f, 0f, 15.8f) },   // into the far right corner
        };

        // No strand, no restart (LEVELS.md 0.3): wherever a marble is let go, it ends up on a plate, back where
        // it started, or in plain view from the lid, to be picked up again.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void WhereverAMarbleIsLetGo_ItCanBeSeenAndPickedUpAgain(int index)
        {
            var failures = new List<string>();
            for (int place = 0; place < OddPlaces.Length; place++)
            {
                Bot bot = Load();
                L level = Level;
                Prop marble = level.Marbles[index];
                Vector3 stand = OddPlaces[place][0], aim = OddPlaces[place][1];
                string what = marble.Name + " let go from " + stand + " looking at " + aim;
                try
                {
                    BotRunner.Run(Game, Carry(bot, marble, WayTo(index), stand, aim, 9f), 90f);
                }
                catch (Exception e)
                {
                    failures.Add(what + ": " + e.Message);
                    continue;
                }
                if (level.Latched > 0) continue;
                Vector3 centre = marble.Center;
                if (marble.Frozen)
                {
                    // Back where it started (the niche gives a marble back to its first place).
                    continue;
                }
                // (Held up in a mouth - it had grown too big on the way - its middle is below the lid: that is in the room.)
                bool inAMouth = Where(marble).StartsWith("MOUTH");
                bool inTheRoom = Mathf.Abs(centre.x) < L.HalfX + 2.6f && centre.z > L.NearZ && centre.z < L.FarZ + 0.6f && (centre.y > -0.3f || inAMouth) && centre.y < L.WallTop;
                if (!inTheRoom || L.UnderTheLid(centre)) failures.Add(what + ": it is out of the room, at " + centre);
                else if (!marble.Grabbable) failures.Add(what + ": it cannot be picked up, at " + centre);
                else if (marble.Velocity.magnitude > 0.5f) failures.Add(what + ": it is still moving after nine seconds, at " + centre + " with " + marble.Velocity);
                else if (!SeenFromTheLid(marble)) failures.Add(what + ": nobody on the lid can see it, at " + centre + " (scale " + marble.Scale + ")");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // A player who walks into a funnel that has a marble in it - one held up by the throat, or one on the
        // plate - is not stuck there: they walk out over the cone, or drop in and step out of the port.
        [TestCase(0, 1.05f)]
        [TestCase(0, 2f)]
        [TestCase(1, 1.03f)]
        [TestCase(1, 1.6f)]
        [TestCase(2, 1.02f)]
        [TestCase(2, 1.5f)]
        [TestCase(0, 0.9f)]
        [TestCase(1, 0.9f)]
        [TestCase(2, 0.9f)]
        public void InAFunnelWithAMarbleInIt_ThePlayerIsNotStuck(int holeIndex, float share)
        {
            Bot bot = Load();
            L level = Level;
            L.Hole hole = L.Holes[holeIndex];
            Player player = Game.Player;
            Prop marble = level.Marbles[holeIndex];
            PutOver(holeIndex, marble, hole.PassBelow * share);
            RunSeconds(3f);
            Assert.AreEqual(share < 1f ? "IN-" + hole.Name : "MOUTH-" + hole.Name, Where(marble), "fixture");
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(hole, hole.Mouth + 1f), 0.3f, 20f), 30f);
            // Into the funnel, as far as it goes, for three seconds; then back out the way they came.
            float until = Game.Time + 3f;
            BotRunner.Run(Game, Into(bot, hole.Axis, () => respawns > 0 || Game.Time >= until, () => { }), 20f);
            if (respawns == 0)
            {
                Vector3 back = Back(hole, hole.Mouth + 1f);
                for (int tick = 0; tick < 300 && respawns == 0 && new Vector2(player.Position.x - back.x, player.Position.z - back.z).magnitude > 0.3f; tick++)
                {
                    StepToward(bot, back);
                    if (player.Grounded && tick % 20 == 10) bot.Jump().MoveNext();
                    Game.Tick();
                }
            }
            // (The last hop may still be in the air, and the tick it lands on it is a hair into the floor.)
            RunSeconds(0.2f);
            TestHelpers.RunUntil(Game, () => player.Grounded, 2f);
            RunSeconds(0.3f);
            Assert.Greater(player.Position.y, -0.05f, "the player is back on the lid, at " + player.Position + " (respawns " + respawns + ")");
            Assert.IsTrue(player.Grounded, "standing, at " + player.Position + " with " + player.Velocity + " (respawns " + respawns + ", the marble is at " + marble.Center + ", " + marble.Scale + ")");
            foreach (L.Hole other in L.Holes)
                Assert.Greater(new Vector2(player.Position.x - other.X, player.Position.z - L.FunnelZ).magnitude, other.Mouth, "and out of the funnels, at " + player.Position);
            Assert.AreEqual(share < 1f ? 1 : 0, level.Latched, "nothing was pressed by it");
            Assert.IsFalse(Game.LevelCompleted);
        }

        // More awkward moments for a restart: a marble held up in a mouth, the boulder in the hand, the
        // player on the way down a tube, a marble on its way out of the port, and the tick a plate goes down.
        [Test]
        public void RestartingAtMoreAwkwardMoments_PutsEverythingBack()
        {
            Bot bot = Load();
            L level = Level;

            PutOver(1, level.Peewee, 2.2f);
            RunSeconds(2f);
            Assert.AreEqual("MOUTH-M", Where(level.Peewee));
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            bot = new Bot(Game);
            BotRunner.Run(Game, Take(bot, level.Shooter, WayToShooter), 40f);
            Assert.AreSame(level.Shooter, Game.Grabber.Held);
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            bot = new Bot(Game);
            BotRunner.Run(Game, bot.WalkTo(L.PastTheSpool), 10f);
            BotRunner.Run(Game, bot.WalkTo(Back(L.Holes[2], L.Holes[2].Mouth + 1f), 0.3f, 20f), 30f);
            BotRunner.Run(Game, Into(bot, L.Holes[2].Axis, () => Game.Player.Position.y < L.Holes[2].ThroatY, () => { }), 20f);
            Assert.Less(Game.Player.Position.y, L.Holes[2].ThroatY, "the player is on the way down the big tube");
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);
            Assert.IsTrue(Game.Player.Grounded);

            PutOver(0, level.Aggie, 0.4f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Port.Ejections == 1, 6f));
            RunSeconds(0.3f);
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            PutOver(2, level.Shooter, 2.5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Plates[2].Pressed, 6f));
            Assert.IsTrue(level.Shooter.Driven, "the plate is drawing it in");
            Game.RestartLevel();
            RunSeconds(0.2f);
            AssertFresh(level);

            bot = new Bot(Game);
            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // The solver's let-gos from stands and at aims a hand's width off: all of them go in.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TheSolversLetGo_FromALittleOff_StillGoesIn(int index)
        {
            Vector3[] stands = { L.StandS, L.StandM, L.StandL };
            Vector3[] aims = { L.AimS, L.AimM, L.AimL };
            L.Hole hole = L.Holes[index];
            float aimStep = hole.Mouth * 0.2f;
            var misses = new List<string>();
            foreach (Vector3 standOff in new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) })
                foreach (Vector3 aimOff in new[] { new Vector3(aimStep, 0f, aimStep), new Vector3(-aimStep, 0f, -aimStep), new Vector3(0f, 0.3f, 0f), new Vector3(0f, -0.3f, 0f) })
                {
                    Bot bot = Load();
                    L level = Level;
                    Prop marble = level.Marbles[index];
                    BotRunner.Run(Game, Carry(bot, marble, WayTo(index), stands[index] + standOff, aims[index] + aimOff, index == 2 ? 7f : 5f), 60f);
                    if (level.Plates[index].PressedBy != marble) misses.Add("stand " + standOff + ", aim " + aimOff + ": " + Where(marble) + ", scale " + marble.Scale + " at " + marble.Center);
                }
            Assert.IsEmpty(misses, string.Join("\n", misses));
        }

        // ---- Probes (not checks: they write tables to tools/out/notes) ------------------------------------

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Port()
        {
            var text = new StringBuilder();
            text.AppendLine("scale  left the port at  at rest after  where            speed after 3 s / 6 s / 10 s");
            foreach (float scale in new[] { 0.2f, 0.3f, 0.48f, 0.7f, 0.97f, 1.4f, 1.9f, 2.4f, 3.1f })
            {
                Load();
                L level = Level;
                Prop marble = level.Aggie;
                Put(marble, scale, new Vector3(0f, scale * 0.5f, 0f));
                level.Port.Eject(marble);
                Game.Tick();
                Vector3 start = marble.Center;
                float restAt = -1f, v3 = 0f, v6 = 0f, v10 = 0f;
                for (int tick = 0; tick < 60 * 14; tick++)
                {
                    Game.Tick();
                    if (tick == 180) v3 = marble.Velocity.magnitude;
                    if (tick == 360) v6 = marble.Velocity.magnitude;
                    if (tick == 600) v10 = marble.Velocity.magnitude;
                    if (restAt < 0f && marble.Velocity.magnitude < 0.05f) restAt = tick / 60f;
                }
                text.AppendLine(F(scale).PadRight(6) + " " + F(start).PadRight(22) + F(restAt).PadRight(8) + " " + F(marble.Center).PadRight(30) + F(v3) + " / " + F(v6) + " / " + F(v10)
                    + "  overlap at start " + F(0f));
            }
            WriteNote("level08-probe-port.txt", text);
        }

        [Test, Explicit("writes what the solver does; not a check")]
        public void Probe_Solve()
        {
            var log = new StringBuilder();
            Bot bot = Load();
            L level = Level;
            Game.Events.PropGrabbed += e => log.AppendLine(F(Game.Time) + " grab " + e.Prop.Name + " scale " + F(e.OldScale, "0.000") + " from " + F(e.GrabDistance) + " k " + F(e.OldScale / e.GrabDistance, "0.000") + " bot " + F(Game.Player.Position));
            Game.Events.PropDropped += e => log.AppendLine(F(Game.Time) + " drop " + e.Prop.Name + " scale " + F(e.NewScale, "0.000") + " at " + F(e.DropDistance) + " centre " + F(e.Prop.Center) + " bot " + F(Game.Player.Position) + " pitch " + F(Game.Player.Pitch));
            Game.Events.Message += e => log.AppendLine(F(Game.Time) + " say: " + e.Text);
            Game.Events.PlatePressed += e => log.AppendLine(F(Game.Time) + " plate pressed by " + (e.Prop != null ? e.Prop.Name : "?") + " at " + F(e.Position));
            Game.Events.PlateRejected += e => log.AppendLine(F(Game.Time) + " plate rejected " + (e.Prop != null ? e.Prop.Name : "?") + " load " + F(e.Value));
            Game.Events.PortEjected += e => log.AppendLine(F(Game.Time) + " port ejected " + (e.Prop != null ? e.Prop.Name : "?"));
            Game.Events.LevelCompleted += e => log.AppendLine(F(Game.Time) + " COMPLETED");
            try
            {
                BotRunner.Run(Game, level.Solve(bot), 120f);
            }
            catch (Exception e)
            {
                log.AppendLine(F(Game.Time) + " EXCEPTION " + e.Message);
            }
            foreach (Prop marble in level.Marbles)
                log.AppendLine(marble.Name + ": " + Where(marble) + " scale " + F(marble.Scale, "0.000") + " at " + F(marble.Center));
            log.AppendLine("completed " + Game.LevelCompleted + " time " + F(Game.Time) + " bot " + F(Game.Player.Position));
            WriteNote("level08-probe-solve.txt", log);
        }

        // Stand on the funnel's centre line a distance back from its axis, look at a spot of it, let go.
        string Sweep(int holeIndex, int marbleIndex, Vector3[] wayToPick, float[] distances, float[] spots, float wait)
        {
            L.Hole hole = L.Holes[holeIndex];
            var table = new StringBuilder();
            table.AppendLine("funnel " + hole.Name + " (throat " + F(hole.Throat) + ", mouth " + F(hole.Mouth) + ", takes " + F(hole.MinScale) + " .. " + F(hole.PassBelow) + ")");
            table.Append("back\\spot");
            foreach (float spot in spots) table.Append((spot >= 0 ? "+" : "") + F(spot) + "            ");
            table.AppendLine();
            foreach (float distance in distances)
            {
                table.Append(F(distance).PadLeft(5) + "  ");
                foreach (float spot in spots)
                {
                    Bot bot = Load();
                    L level = Level;
                    Prop marble = level.Marbles[marbleIndex];
                    float dropped = 0f, k = 0f;
                    Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
                    Game.Events.PropDropped += e => dropped = e.NewScale;
                    string cell;
                    try
                    {
                        var way = new List<Vector3>(wayToPick);
                        BotRunner.Run(Game, Carry(bot, marble, way.ToArray(), new Vector3(hole.X, 0f, L.FunnelZ - distance), Spot(hole, spot), wait), 60f);
                        string where = Where(marble);
                        if (where == "LID" && level.Port.Ejections > 0) where = "port";
                        cell = F(dropped) + " " + where;
                    }
                    catch (Exception e)
                    {
                        cell = "ERR " + e.Message.Substring(0, Mathf.Min(30, e.Message.Length));
                    }
                    table.Append(cell.PadRight(16));
                    if (spot == spots[0]) table.Insert(table.Length - cell.PadRight(16).Length, "k" + F(k, "0.000") + " ");
                }
                table.AppendLine();
            }
            return table.ToString();
        }

        // Stand at a point, look along a yaw and a pitch (degrees), let go.
        string Angles(int holeIndex, int marbleIndex, Vector3[] wayToPick, Vector3 stand, float[] pitches, float[] yaws, float wait)
        {
            L.Hole hole = L.Holes[holeIndex];
            var table = new StringBuilder();
            float straight = Mathf.Atan2(hole.X - stand.x, L.FunnelZ - stand.z) * Mathf.Rad2Deg;
            table.AppendLine("funnel " + hole.Name + " from " + F(stand) + " (" + F(new Vector2(hole.X - stand.x, L.FunnelZ - stand.z).magnitude) + " back), yaw offsets across, pitch down the side");
            table.Append("pitch\\yaw ");
            foreach (float yaw in yaws) table.Append(F(yaw, "0.0").PadRight(16));
            table.AppendLine();
            foreach (float pitch in pitches)
            {
                table.Append(F(pitch, "0.0").PadLeft(6) + "    ");
                foreach (float yaw in yaws)
                {
                    Bot bot = Load();
                    L level = Level;
                    Prop marble = level.Marbles[marbleIndex];
                    float dropped = 0f;
                    Game.Events.PropDropped += e => dropped = e.NewScale;
                    string cell;
                    try
                    {
                        Vector3 eye = stand + Vector3.up * Player.BaseEyeHeight;
                        Vector3 aim = eye + Quaternion.Euler(-pitch, straight + yaw, 0f) * Vector3.forward * 30f;
                        BotRunner.Run(Game, Carry(bot, marble, wayToPick, stand, aim, wait), 60f);
                        string where = Where(marble);
                        if (where == "LID" && level.Port.Ejections > 0) where = "port";
                        cell = F(dropped) + " " + where;
                    }
                    catch (Exception e)
                    {
                        cell = "ERR " + e.Message.Substring(0, Mathf.Min(30, e.Message.Length));
                    }
                    table.Append(cell.PadRight(16));
                }
                table.AppendLine();
            }
            return table.ToString();
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Angles()
        {
            var text = new StringBuilder();
            float[] yaws = { -12f, -8f, -4f, 0f, 4f, 8f, 12f };
            text.AppendLine(Angles(0, 0, NoWay, new Vector3(-8f, 0f, 5.6f), new[] { -45f, -40f, -35f, -30f, -27.5f, -25f, -22.5f, -20f, -17.5f, -15f, -12.5f, -10f, -5f }, yaws, 5f));
            text.AppendLine(Angles(1, 1, WayToAggie, new Vector3(-2f, 0f, 5.2f), new[] { -35f, -30f, -25f, -22.5f, -20f, -17.5f, -15f, -12.5f, -10f, -7.5f, -5f, 0f }, yaws, 6f));
            text.AppendLine(Angles(2, 2, WayToShooter, new Vector3(6.5f, 0f, -0.5f), new[] { -14f, -12f, -10f, -9f, -8f, -7f, -6f, -5f, -4f, -3f, -2f, -1f, 0f, 2f }, yaws, 8f));
            text.AppendLine(Angles(2, 2, WayToShooter, new Vector3(6.5f, 0f, 0.5f), new[] { -14f, -12f, -10f, -9f, -8f, -7f, -6f, -5f, -4f, -3f, -2f, -1f, 0f, 2f }, new[] { -8f, 0f, 8f }, 8f));
            WriteNote("level08-probe-angles.txt", text);
        }

        // Does the lid hold a small marble that comes down fast?
        [Test, Explicit("writes a table; not a check")]
        public void Probe_Lid()
        {
            var text = new StringBuilder();
            text.AppendLine("scale  from   at            lowest   ends     rescues");
            foreach (float scale in new[] { 0.2f, 0.25f, 0.3f, 0.4f, 0.6f })
                foreach (float from in new[] { 4f, 8f, 13f })
                    foreach (Vector3 at in new[] { new Vector3(-3f, 0f, -3f), new Vector3(3.3f, 0f, 6.1f), new Vector3(-8.9f, 0f, 9.9f), new Vector3(-2f, 0f, 10.3f), new Vector3(6.5f, 0f, 11.4f), new Vector3(1.4f, 0f, 9f) })
                    {
                        Load();
                        L level = Level;
                        Prop marble = level.Aggie;
                        Put(marble, scale, new Vector3(at.x, from, at.z));
                        float lowest = 99f;
                        for (int tick = 0; tick < 300; tick++)
                        {
                            Game.Tick();
                            lowest = Mathf.Min(lowest, marble.Center.y);
                        }
                        text.AppendLine(F(scale).PadRight(6) + " " + F(from, "0").PadRight(6) + " " + F(at).PadRight(22) + " " + F(lowest).PadRight(8) + " " + Where(marble).PadRight(8) + " " + level.Rescues + " " + level.Port.Ejections);
                    }
            WriteNote("level08-probe-lid.txt", text);
        }

        // Does the chamber's floor hold a small marble that falls down the tube?
        [Test, Explicit("writes a table; not a check")]
        public void Probe_Floor()
        {
            var text = new StringBuilder();
            text.AppendLine("hole scale  from y   off   lowest y   ends (floor " + ")  mode at the floor");
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                foreach (float scale in new[] { 0.2f, 0.22f, 0.25f, 0.27f, 0.3f, 0.34f, 0.4f, 0.5f, 0.7f, 1f, 1.5f, 2.5f })
                {
                    if (scale >= hole.PassBelow) continue;
                    foreach (float from in new[] { hole.ThroatY, 0.5f, 3f })
                        foreach (float off in new[] { 0f, 0.6f })
                        {
                            Load();
                            L level = Level;
                            level.Plates[i].Enabled = false;
                            level.JamLeash.Enabled = false;
                            Prop marble = level.Marbles[i];
                            float side = (hole.Throat - scale * 0.5f) * off;
                            Put(marble, scale, new Vector3(hole.X + side, from, L.FunnelZ));
                            float lowest = 99f;
                            string mode = "";
                            for (int tick = 0; tick < 240; tick++)
                            {
                                Game.Tick();
                                if (marble.Center.y < lowest)
                                {
                                    lowest = marble.Center.y;
                                    if (marble.Center.y > hole.FloorY) mode = marble.Body.collisionDetectionMode.ToString() + " v " + F(marble.Velocity.magnitude);
                                }
                            }
                            bool through = marble.Center.y < hole.FloorY - 0.1f;
                            text.AppendLine(hole.Name + "    " + F(scale).PadRight(6) + " " + F(from).PadRight(7) + " " + F(off, "0.0").PadRight(5) + " " + F(lowest).PadRight(9) + " " + F(marble.Center.y).PadRight(8) + (through ? "THROUGH" : "held   ") + "  " + mode);
                        }
                }
            }
            WriteNote("level08-probe-floor.txt", text);
        }

        [Test, Explicit("writes a trace; not a check")]
        public void Probe_Trace()
        {
            var text = new StringBuilder();
            foreach (float back in new[] { 1.4f, 2.6f })
            {
                Bot bot = Load();
                L level = Level;
                L.Hole hole = L.Holes[0];
                Prop marble = level.Peewee;
                Game.Events.PropDropped += e => text.AppendLine(F(Game.Time) + " drop scale " + F(e.NewScale, "0.000") + " centre " + F(e.Prop.Center) + " valid " + Game.Grabber.PlacementValid + " pitch " + F(Game.Player.Pitch));
                Game.Events.PlateRejected += e => text.AppendLine(F(Game.Time) + " rejected");
                Game.Events.PortEjected += e => text.AppendLine(F(Game.Time) + " ejected");
                Game.Events.LeashReturned += e => text.AppendLine(F(Game.Time) + " leash");
                text.AppendLine("back " + F(back));
                BotRunner.Run(Game, Carry(bot, marble, new[] { new Vector3(L.SpoolBase.x + 3.5f, 0f, L.SpoolBase.z) }, Back(hole, back), Spot(hole, back > 2f ? 0.7f : 0f), 0f), 60f);
                for (int tick = 0; tick < 60 * 12; tick++)
                {
                    Game.Tick();
                    if (tick % 20 == 0) text.AppendLine("  " + F(Game.Time) + " " + Where(marble) + " at " + F(marble.Center) + " v " + F(marble.Velocity.magnitude) + " load " + F(level.Plates[0].Load, "0.0000") + " mass " + F(marble.Mass, "0.0000") + " pending " + level.Port.IsPending(marble) + " waiting " + level.Port.Waiting);
                }
            }
            WriteNote("level08-probe-trace.txt", text);
        }

        // How long a marble takes from the far side of a cone to its plate's verdict, also when it comes in sideways.
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Settle()
        {
            var text = new StringBuilder();
            text.AppendLine("hole scale  sideways  verdict after (s)  what");
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                foreach (float share in new[] { 0.42f, 0.55f, 0.7f, 0.85f, 0.97f })
                    foreach (float sideways in new[] { 0f, 2f, 5f })
                    {
                        Load();
                        L level = Level;
                        Prop marble = level.Marbles[i];
                        float scale = hole.PassBelow * share;
                        float off = (hole.Throat + hole.Mouth) * 0.5f;
                        Put(marble, scale, new Vector3(hole.X, hole.ConeY(off) + scale * 0.62f + 0.03f, L.FunnelZ + off));
                        marble.Body.linearVelocity = new Vector3(sideways, 0f, 0f);
                        float verdict = -1f;
                        string what = "none";
                        level.Plates[i].OnPressed += prop => what = "pressed";
                        level.Plates[i].OnRejected += (prop, load) => what = "rejected";
                        for (int tick = 0; tick < 60 * 15 && what == "none"; tick++)
                        {
                            Game.Tick();
                            verdict = (tick + 1) / 60f;
                        }
                        text.AppendLine(hole.Name + "    " + F(scale).PadRight(6) + " " + F(sideways, "0").PadRight(9) + " " + F(verdict).PadRight(18) + " " + what + (what == "none" ? " (" + Where(marble) + " at " + F(marble.Center) + ", speed " + F(marble.Velocity.magnitude) + ")" : ""));
                    }
            }
            WriteNote("level08-probe-settle.txt", text);
        }

        // The same marbles picked up from other distances: how big they look decides where one has to stand.
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_OtherPickUps()
        {
            var text = new StringBuilder();
            float[] spotsS = { 0f, 0.7f, 1f, 1.5f }, backS = { 1.4f, 1.8f, 2.2f, 2.6f, 3f, 3.5f, 4f, 4.5f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f };
            foreach (float from in new[] { 0.9f, 2.5f, 3.5f, 4.5f, 6f })
            {
                text.AppendLine("Peewee picked up from " + F(from) + " beside the spool");
                text.AppendLine(Sweep(0, 0, new[] { new Vector3(L.SpoolBase.x + from, 0f, L.SpoolBase.z) }, backS, spotsS, 5f));
            }
            text.AppendLine("Shooter picked up from (5, -8), 27.7 away");
            text.AppendLine(Sweep(2, 2, new[] { new Vector3(5f, 0f, -8f) }, new[] { 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f }, new[] { 1.6f, 2.6f, 3.6f, 5f }, 8f));
            text.AppendLine("Shooter picked up from (-12, -11), 33 away");
            text.AppendLine(Sweep(2, 2, new[] { L.PastTheSpool, new Vector3(-12f, 0f, -11f) }, new[] { 9f, 10f, 11f, 12f, 13f, 14f, 15f }, new[] { 2.6f, 3.6f, 5f }, 8f));
            text.AppendLine("Shooter picked up from under the niche (1.5, 12), 9.6 away");
            text.AppendLine(Sweep(2, 2, new[] { L.PastTheSpool, new Vector3(1.5f, 0f, 3f), new Vector3(1.5f, 0f, 12f) }, new[] { 4.2f, 4.6f, 5f, 5.5f, 6f, 7f }, new[] { -2f, 0f, 1.6f, 2.6f, 3.6f }, 8f));
            text.AppendLine("Aggie picked up from 1.6 beside it");
            text.AppendLine(Sweep(1, 1, new[] { L.PastTheSpool, new Vector3(L.AggieOrigin.x - 1.5f, 0f, L.AggieOrigin.z) }, new[] { 2.3f, 2.6f, 3f, 3.5f, 4f }, new[] { -1f, 0f, 0.8f, 1.35f, 1.9f }, 6f));
            WriteNote("level08-probe-other-pickups.txt", text);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_SweepS()
        {
            var text = new StringBuilder();
            text.Append(Sweep(0, 0, NoWay, new[] { 1.4f, 1.8f, 2.2f, 2.6f, 3f, 3.4f, 3.8f, 4.2f, 4.6f, 5f, 5.5f, 6f, 7f }, new[] { -1f, -0.5f, 0f, 0.4f, 0.7f, 1f, 1.5f }, 5f));
            WriteNote("level08-probe-sweep-s.txt", text);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_SweepM()
        {
            var text = new StringBuilder();
            text.Append(Sweep(1, 1, WayToAggie, new[] { 2.3f, 2.8f, 3.3f, 3.8f, 4.3f, 4.8f, 5.3f, 5.8f, 6.5f, 7.5f, 9f }, new[] { -1.9f, -1f, 0f, 0.8f, 1.35f, 1.9f, 2.6f }, 6f));
            WriteNote("level08-probe-sweep-m.txt", text);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_SweepL()
        {
            var text = new StringBuilder();
            text.Append(Sweep(2, 2, WayToShooter, new[] { 4.2f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 16f }, new[] { -3.6f, -2f, 0f, 1.6f, 2.6f, 3.6f, 5f }, 8f));
            WriteNote("level08-probe-sweep-l.txt", text);
        }

        // ---- The review's probes (tools/out/notes/level08-review-probe-*.txt) -------------------------------

        static string Letter(FitState state) => state == FitState.Good ? "G" : state == FitState.Idle ? "-" : state == FitState.TooSmall ? "s" : state == FitState.TooBig ? "B" : "?";

        // What the lamps said with the marble in the hand, against what happened when it was let go.
        // A cell: scale, lamp (G good, s too small, B too big, - idle), how far its middle was from the axis
        // as a share of the mouth's radius, and where it ended. "!!" marks green lamps and a marble that did not go in.
        string Truth(int holeIndex, int marbleIndex, Vector3[] wayToPick, float[] distances, float[] spots, float wait, ref int lies, ref int cells, ref int silent)
        {
            L.Hole hole = L.Holes[holeIndex];
            var table = new StringBuilder();
            table.AppendLine("funnel " + hole.Name + " (throat " + F(hole.Throat) + ", mouth " + F(hole.Mouth) + ", takes " + F(hole.MinScale) + " .. " + F(hole.PassBelow) + ")");
            table.Append("back\\spot ");
            foreach (float spot in spots) table.Append(((spot >= 0 ? "+" : "") + F(spot)).PadRight(30));
            table.AppendLine();
            foreach (float distance in distances)
            {
                table.Append(F(distance).PadLeft(5) + "     ");
                foreach (float spot in spots)
                {
                    Bot bot = Load();
                    L level = Level;
                    Prop marble = level.Marbles[marbleIndex];
                    string cell;
                    try
                    {
                        List<string> said = Listen();
                        BotRunner.Run(Game, Hold(bot, marble, wayToPick, new Vector3(hole.X, 0f, L.FunnelZ - distance), Spot(hole, spot)), 60f);
                        FitState lamp = level.Gauges[holeIndex].State;
                        float held = marble.Scale;
                        float off = new Vector2(marble.Center.x - hole.X, marble.Center.z - L.FunnelZ).magnitude / hole.Mouth;
                        BotRunner.Run(Game, LetGo(bot, wait), 30f);
                        string where = Where(marble);
                        if (where == "LID" && level.Port.Ejections > 0) where = "port";
                        bool lie = lamp == FitState.Good && !where.StartsWith("IN");
                        cells++;
                        if (lie) lies++;
                        string line = said.Count == 0 ? "" : said[0] == L.ShortLine ? " short" : said[0] == L.CloserLine ? " closer" : said[0] == L.OverLine ? " over" : said[0] == L.TooBigLine ? " big" : said[0] == L.TooLightLine ? " light" : " ?";
                        if (where == "LID" && said.Count == 0) silent++;
                        cell = F(held) + " " + Letter(lamp) + " " + F(off) + " " + where + line + (lie ? " !!" : where.StartsWith("IN") && lamp != FitState.Good ? " ~" : "");
                    }
                    catch (Exception e)
                    {
                        cell = "ERR " + e.Message.Substring(0, Mathf.Min(18, e.Message.Length));
                    }
                    table.Append(cell.PadRight(30));
                }
                table.AppendLine();
            }
            return table.ToString();
        }

        static IEnumerator Hold(Bot bot, Prop marble, Vector3[] wayToPick, Vector3 stand, Vector3 aim)
        {
            foreach (Vector3 point in wayToPick) yield return bot.WalkTo(point, 0.1f);
            yield return bot.Grab(marble);
            if (bot.Player.Position.z < L.PastTheSpool.z - 0.5f) yield return bot.WalkTo(L.PastTheSpool, 0.2f);
            yield return bot.WalkTo(stand, 0.1f, 20f);
            yield return bot.LookAt(aim);
            yield return bot.Wait(0.15f);
        }

        static IEnumerator LetGo(Bot bot, float wait)
        {
            yield return bot.Drop();
            yield return bot.Wait(wait);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_ReviewTruth()
        {
            var text = new StringBuilder();
            int lies = 0, cells = 0, silent = 0;
            text.AppendLine(Truth(0, 0, NoWay, new[] { 1.4f, 1.8f, 2.2f, 2.6f, 3f, 3.4f, 3.8f, 4.2f, 4.6f, 5f, 5.5f, 6f, 7f, 8f }, new[] { -1f, -0.5f, 0f, 0.4f, 0.7f, 1f, 1.5f, 2.2f }, 5f, ref lies, ref cells, ref silent));
            text.AppendLine(Truth(1, 1, WayToAggie, new[] { 2.3f, 2.8f, 3.3f, 3.8f, 4.3f, 4.8f, 5.3f, 5.8f, 6.5f, 7.5f, 9f }, new[] { -1.9f, -1f, 0f, 0.8f, 1.35f, 1.9f, 2.6f, 3.4f }, 6f, ref lies, ref cells, ref silent));
            text.AppendLine("the middling one picked up from the start");
            text.AppendLine(Truth(1, 1, NoWay, new[] { 2.3f, 2.8f, 3.3f, 3.8f, 4.3f, 4.8f, 5.3f, 5.8f, 6.5f, 7.5f, 9f }, new[] { -1.9f, -1f, 0f, 0.8f, 1.35f, 1.9f, 2.6f, 3.4f }, 6f, ref lies, ref cells, ref silent));
            text.AppendLine(Truth(2, 2, WayToShooter, new[] { 4.2f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 16f }, new[] { -3.6f, -2f, 0f, 1.6f, 2.6f, 3.6f, 5f, 6.5f }, 8f, ref lies, ref cells, ref silent));
            text.AppendLine("green lamps and no marble in the hole: " + lies + " of " + cells + " cells; on the lid and nothing said: " + silent);
            WriteNote("level08-review-probe-truth.txt", text);
        }

        // The solver's three let-gos, each from a stand and at an aim a little off: what a hand does.
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_ReviewJitter()
        {
            var text = new StringBuilder();
            Vector3[] stands = { L.StandS, L.StandM, L.StandL };
            Vector3[] aims = { L.AimS, L.AimM, L.AimL };
            float[] standOff = { -0.5f, 0f, 0.5f };
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                float aimStep = hole.Mouth * 0.2f;
                int good = 0, all = 0;
                text.AppendLine("funnel " + hole.Name + ": stand " + F(stands[i]) + " +- 0.5, aim " + F(aims[i]) + " +- " + F(aimStep) + " sideways and along, +- 0.3 up");
                foreach (float sx in standOff)
                    foreach (float sz in standOff)
                    {
                        text.Append("  stand " + F(sx, "+0.0;-0.0") + "," + F(sz, "+0.0;-0.0") + ": ");
                        foreach (Vector3 a in new[] { Vector3.zero, new Vector3(aimStep, 0f, 0f), new Vector3(-aimStep, 0f, 0f), new Vector3(0f, 0f, aimStep), new Vector3(0f, 0f, -aimStep), new Vector3(0f, 0.3f, 0f), new Vector3(0f, -0.3f, 0f) })
                        {
                            Bot bot = Load();
                            L level = Level;
                            Prop marble = level.Marbles[i];
                            string cell;
                            try
                            {
                                BotRunner.Run(Game, Hold(bot, marble, WayTo(i), stands[i] + new Vector3(sx, 0f, sz), aims[i] + a), 60f);
                                FitState lamp = level.Gauges[i].State;
                                float held = marble.Scale;
                                BotRunner.Run(Game, LetGo(bot, i == 2 ? 8f : 6f), 30f);
                                string where = Where(marble);
                                if (where == "LID" && level.Port.Ejections > 0) where = "port";
                                all++;
                                if (where.StartsWith("IN")) good++;
                                cell = F(held) + Letter(lamp) + " " + where;
                            }
                            catch (Exception e)
                            {
                                cell = "ERR " + e.Message.Substring(0, Mathf.Min(18, e.Message.Length));
                            }
                            text.Append(cell.PadRight(15));
                        }
                        text.AppendLine();
                    }
                text.AppendLine("  in the hole: " + good + " of " + all);
            }
            WriteNote("level08-review-probe-jitter.txt", text);
        }

        // One tick of walking at a point (the bot keeps whatever it holds).
        static bool StepToward(Bot bot, Vector3 point, bool sprint = false)
        {
            IEnumerator walk = bot.WalkTo(point, 0.02f, 5f, sprint);
            return walk.MoveNext();
        }

        // Rolls a marble across the lid by walking into it from behind, toward a point.
        static IEnumerator PushTo(Bot bot, Prop marble, Vector3 goal, float seconds, Func<bool> done)
        {
            for (int tick = TestHelpers.Ticks(seconds); tick > 0 && !done(); tick--)
            {
                Vector3 centre = marble.Center, feet = bot.Player.Position;
                var to = new Vector3(goal.x - centre.x, 0f, goal.z - centre.z);
                Vector3 dir = to.normalized;
                Vector3 behind = new Vector3(centre.x, 0f, centre.z) - dir * (marble.Radius + 0.45f);
                var off = new Vector3(feet.x - behind.x, 0f, feet.z - behind.z);
                // Behind it and in line: push. Otherwise get behind it first.
                bool inLine = Mathf.Abs(Vector3.Cross(dir, off).y) < 0.2f && Vector3.Dot(off, dir) > -0.4f;
                StepToward(bot, inLine ? new Vector3(goal.x, 0f, goal.z) : behind);
                yield return null;
            }
        }

        [Test, Explicit("writes what happens; not a check")]
        public void Probe_ReviewAttacks()
        {
            var text = new StringBuilder();

            // 1. The middling one fits the middle hole as it lies. Can it be rolled there by walking into it?
            {
                Bot bot = Load();
                L level = Level;
                List<string> said = Listen();
                L.Hole hole = L.Holes[1];
                Prop marble = level.Aggie;
                string error = "";
                try
                {
                    BotRunner.Run(Game, PushTo(bot, marble, hole.Axis, 60f, () => level.Plates[1].Pressed || level.Port.Ejections > 0), 90f);
                }
                catch (Exception e)
                {
                    error = " EXCEPTION " + e.Message;
                }
                RunSeconds(3f);
                text.AppendLine("push the middling one to the middle hole: after " + F(Game.Time) + " s it is " + Where(marble) + " at " + F(marble.Center) + ", scale " + F(marble.Scale)
                    + ", plate M pressed " + level.Plates[1].Pressed + ", bot at " + F(Game.Player.Position) + ", said: " + string.Join(" | ", said) + error);
            }

            // 2. A second marble that passes the throat, on top of one that is latched: where does it end up?
            for (int i = 0; i < 3; i++)
            {
                L.Hole hole = L.Holes[i];
                foreach (float share in new[] { 0.7f, 0.9f, 0.985f })
                    foreach (float second in new[] { 0.65f, 0.9f, 0.985f })
                    {
                        Load();
                        L level = Level;
                        List<string> said = Listen();
                        PutOver(i, level.Marbles[i], hole.PassBelow * share);
                        bool latched = TestHelpers.RunUntil(Game, () => level.Plates[i].Pressed, 6f);
                        RunSeconds(0.5f);
                        Prop other = level.Marbles[(i + 1) % 3];
                        PutOver(i, other, hole.PassBelow * second);
                        RunSeconds(8f);
                        text.AppendLine("second marble: hole " + hole.Name + " latched " + latched + " with " + F(hole.PassBelow * share) + ", then " + F(hole.PassBelow * second) + ": " + Where(other) + " at " + F(other.Center)
                            + " speed " + F(other.Velocity.magnitude) + ", port " + level.Port.Ejections + ", grabbable " + other.Grabbable + ", said: " + string.Join(" | ", said));
                    }
            }
            WriteNote("level08-review-probe-attacks.txt", text);
        }

        // ---- Pictures of moments the solver never reaches (tools/out/shots/level08/review/<name>) -----------

        // The level itself with another script in the bot's hands: the screenshot tool plays whatever Solve says.
        sealed class Scenario : LevelDefinition
        {
            public readonly L Inner = new L();
            readonly Func<L, Bot, IEnumerator> script;
            /// <summary>The one hint to show (the pause menu's panel shows a level's first), or -1 for all three.</summary>
            public int OnlyHint = -1;

            public Scenario(Func<L, Bot, IEnumerator> script)
            {
                this.script = script;
            }

            public override string Slug => "funnel-physics-scenario";
            public override string Title => "Funnel Physics";
            public override string Blurb => Inner.Blurb;
            public override string[] Hints => OnlyHint < 0 ? Inner.Hints : new[] { Inner.Hints[OnlyHint] };
            public override string Environment => Inner.Environment;
            public override int EnvironmentVisit => Inner.EnvironmentVisit;
            public override float GroundY => Inner.GroundY;
            public override float KillY => Inner.KillY;
            public override void Build(LevelContext ctx) => Inner.Build(ctx);
            public override IEnumerator Solve(Bot bot) => script(Inner, bot);
        }

        void Picture(string name, float[] times, Func<L, Bot, IEnumerator> script, bool overview = false, string ui = "hud", int onlyHint = -1)
        {
            Game?.Dispose();
            Game = null;
            Toybox.UI.UiCapture.Request = ui;
            try
            {
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 8, Definition = new Scenario(script) { OnlyHint = onlyHint }, Times = times, OutputDirectory = "tools/out/shots/level08/review/" + name, Overview = overview,
                });
            }
            finally
            {
                Toybox.UI.UiCapture.Reset();
            }
        }

        static IEnumerator At(Bot bot, float time) => bot.Until(() => bot.Game.Time >= time, 60f);

        static IEnumerator HoldThenLetGo(L level, Bot bot, int marbleIndex, Vector3 stand, Vector3 aim, float letGoAt, Vector3? thenLookAt = null)
        {
            yield return Hold(bot, level.Marbles[marbleIndex], WayTo(marbleIndex), stand, aim);
            yield return At(bot, letGoAt);
            yield return bot.Drop();
            if (thenLookAt.HasValue)
            {
                yield return bot.Wait(2.2f);
                yield return bot.LookAt(thenLookAt.Value);
            }
        }

        [Test, Explicit("writes pictures; not a check")]
        public void Probe_ReviewShots_Moments()
        {
            L.Hole s = L.Holes[0], l = L.Holes[2];
            // Held over the middle of the small hole from the solver's stand: it stops short.
            Picture("short", new[] { 5.9f, 7.2f }, (level, bot) => HoldThenLetGo(level, bot, 0, L.StandS, Spot(s, 0f), 6f));
            // From too near: too small; it goes through and comes out of the port.
            Picture("small", new[] { 5.9f, 7.6f, 10f }, (level, bot) => HoldThenLetGo(level, bot, 0, Back(s, 1.8f), Spot(s, s.Mouth), 6f, new Vector3(-L.HalfX, 1f, L.PortZ)));
            // From too far: too big; it sits in the mouth.
            Picture("big", new[] { 5.9f, 7.6f }, (level, bot) => HoldThenLetGo(level, bot, 0, Back(s, 5.5f), Spot(s, s.Mouth), 6f));
            // The boulder held over the middle of the big hole, and over its far rim, from nine steps back.
            Picture("big-hole-middle", new[] { 8.9f, 10.5f }, (level, bot) => HoldThenLetGo(level, bot, 2, Back(l, 9f), new Vector3(l.X, 0f, L.FunnelZ), 9f));
            Picture("big-hole-far", new[] { 8.9f, 10.5f }, (level, bot) => HoldThenLetGo(level, bot, 2, Back(l, 9f), Spot(l, l.Mouth), 9f));
            // Held over the far side from too far off: it meets the lid first.
            Picture("too-far", new[] { 5.9f, 7.4f }, (level, bot) => HoldThenLetGo(level, bot, 0, Back(s, 5f), Spot(s, 0.7f), 6f));
            Picture("too-far-big", new[] { 9.9f, 11.6f }, (level, bot) => HoldThenLetGo(level, bot, 2, Back(l, 12.5f), Spot(l, l.Mouth), 10f));
            // The three hints as the pause menu shows them.
            for (int hint = 0; hint < 3; hint++)
                Picture("hint" + (hint + 1), new[] { 1f }, (level, bot) => bot.Wait(0.1f), false, "hints", hint);
        }

        // The solver's own run, fresh: from the start, each marble in the hand over its hole, each latch, the way out.
        [Test, Explicit("writes pictures; not a check")]
        public void Probe_ReviewShots_Solve()
        {
            Game?.Dispose();
            Game = null;
            Toybox.UI.UiCapture.Request = "hud";
            try
            {
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 8, Times = new[] { 0f, 1.5f, 3.78f, 5.2f, 7.4f, 9.4f, 11f, 13.6f, 14.2f, 16.4f, 18.5f, 20.3f }, OutputDirectory = "tools/out/shots/level08/review/final", Overview = true,
                });
                Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                {
                    Level = 8, Times = new[] { 0f, 14.2f, 17.5f }, OutputDirectory = "tools/out/shots/level08/review/final-low", Quality = QualityTier.Low,
                });
            }
            finally
            {
                Toybox.UI.UiCapture.Reset();
            }
        }

        // The middling one shoved into the middle hole as it lies (what used to be a way round the puzzle).
        [Test, Explicit("writes pictures; not a check")]
        public void Probe_ReviewShots_Shove()
        {
            Picture("shove", new[] { 2.5f, 6f, 9f, 12f }, (level, bot) => PushTo(bot, level.Aggie, L.Holes[1].Axis, 20f, () => level.Port.Ejections > 0));
        }
    }
}
