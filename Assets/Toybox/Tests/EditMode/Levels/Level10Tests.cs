using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Toys;
using UnityEngine;
using L = Toybox.Levels.Level10MatryoshkaBoxes;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 10, "Matryoshka Boxes": three gift boxes that come out of each other, none of which grows
    /// taller than 2, and a shelf 3.9 up. The solver, the shelf that cannot be reached without a staircase,
    /// the boxes' working sizes (LEVELS appendix B: big 1.6 to 2.0, step 0.75 to 1.15, top 0.85 to 1.15;
    /// measured, a hop gets up 1.30, so more works than that), and what keeps the level from being
    /// soft-locked.
    ///
    /// Measured tables: tools/out/notes/level10-probe-*.txt (the explicit Probe tests below write them).
    ///
    /// The review (tools/out/notes/level10-review.md) added, after "The review's attacks, kept as checks":
    /// the box under the feet that a jump and two clicks used to grow, two boxes and that trick, a standing
    /// place and an aim that are a little off, the red box held too low, restarts at awkward moments.
    ///
    /// The second review (same file, "Second review run") added: nothing is picked up from up on a box (the
    /// step pulled up after oneself, a box carried up and grown beside one's feet), and boxes picked up from
    /// anywhere on the mat's rim stand square.
    ///
    /// The third review (same file, "Third review run") added: the two inner boxes grow no taller than a hop
    /// (1.15), so two boxes are no way up and an inner box picked up from too near is a step that works or
    /// is told what to do; the third box put on by hand is told why it is small. Its probes are quoted in
    /// tools/out/notes/level10-review3-probes.cs.txt, their tables are level10-review3-probe-*.txt.
    /// </summary>
    public class Level10Tests : SimTest
    {
        L level;
        Bot bot;

        void Load()
        {
            Game?.Dispose();
            Game = Game.Create();
            Game.LoadLevel(10);
            level = (L)Game.Level;
            bot = new Bot(Game);
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        // Pumps a bot script for at most this long. True if it ran to its end; a command that fails (a hop
        // that does not get up) ends it too, and that is not an error here.
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

        static string F(float v) => v.ToString("0.00");
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        string Describe(Prop box)
        {
            Bounds b = L.BoundsOf(box);
            return box.Name + " " + box.Scale.ToString("0.000") + " at " + F(box.Center) + " y " + F(b.min.y) + ".." + F(b.max.y) + " z " + F(b.min.z) + ".." + F(b.max.z) +
                   " yaw " + Mathf.DeltaAngle(0f, box.Transform.eulerAngles.y).ToString("0.0");
        }

        // Picks a box up from one place, carries it to another, holds it at a point and lets go; then waits
        // for it to lie still.
        IEnumerator Place(Prop box, Vector3 pickFrom, Vector3 stand, Vector3 aim)
        {
            yield return level.Carry(bot, box, pickFrom, stand);
            yield return bot.DropAt(aim);
            yield return bot.Wait(0.2f);
            yield return bot.Until(() => L.AtRest(box), 5f);
            yield return bot.Wait(0.3f);
        }

        static Vector3 PickFrom(float z) => new Vector3(0f, 0f, z);
        static Vector3 StandAt(float z) => new Vector3(L.StairX, 0f, z);

        // The red box's own middle where it stands at the foot of the shelf: the windows below were measured
        // with it held there. (From the shoe prints that is the lowest aim that still brings it to the shelf;
        // the solver holds it a little higher, at L.BigAim, where a hand that wanders makes no difference.)
        static readonly Vector3 FootAim = new Vector3(L.StairX, 0.95f, 11.05f);

        // The solver's three placements, each with the pick-up or the standing place of the test's choosing.
        void Big(float standZ = 6.7f, Vector3? aim = null) => BotRunner.Run(Game, Place(level.Red, L.PickSpot, StandAt(standZ), aim ?? FootAim), 40f);
        void Step(float pickZ = -2.6f, Vector3? aim = null) => BotRunner.Run(Game, Place(level.Yellow, PickFrom(pickZ), L.StandSpot, aim ?? L.StepAim), 40f);
        void Top(float pickZ = -2.6f, Vector3? aim = null) => BotRunner.Run(Game, Place(level.Teal, PickFrom(pickZ), L.StandSpot, aim ?? L.TopAim), 40f);

        // Up the stairs and into the exit, if they can be climbed.
        bool Climb()
        {
            Drive(level.Climb(bot, level.Yellow, level.Red, level.Teal), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            return Game.LevelCompleted;
        }

        // What anybody does at a staircase, with no thought for where to take off: straight at the shelf
        // along the line the stairs stand on, forward held down, a hop whenever the feet are on something.
        // On the shelf it walks to the exit.
        IEnumerator Scramble(float seconds)
        {
            var goal = new Vector3(L.StairX, 0f, L.ShelfZ + 1.5f);
            int ticks = TestHelpers.Ticks(seconds);
            while (ticks > 0 && !(bot.Player.Grounded && bot.Player.Position.y > L.ShelfTop - 0.05f))
            {
                if (bot.Player.Grounded)
                {
                    yield return bot.Jump();
                    ticks--;
                }
                IEnumerator walk = bot.WalkTo(goal, 0.3f, 2f);
                int walked = 0;
                for (; walked < 10 && ticks > 0 && walk.MoveNext(); walked++, ticks--) yield return walk.Current;
                // In the air right over the goal there is nothing to walk and nothing to hop off: a tick
                // goes by all the same. (Without this the loop never yields again and the test run hangs.)
                if (walked == 0)
                {
                    yield return null;
                    ticks--;
                }
            }
            if (bot.Player.Grounded && bot.Player.Position.y > L.ShelfTop - 0.05f)
            {
                yield return bot.WalkTo(L.ExitCentre, 0.5f, 10f);
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
        }

        // Where a point is in the picture the level starts with: x and y from 0 to 1, from the top left.
        Vector2 OnScreen(Vector3 point)
        {
            Player player = Game.Player;
            Vector3 local = Quaternion.Inverse(player.LookRotation) * (point - player.Eye);
            float tan = Mathf.Tan(35f * Mathf.Deg2Rad);
            return new Vector2(0.5f + local.x / local.z / (2f * tan * 16f / 9f), 0.5f - local.y / local.z / (2f * tan));
        }

        // What stands between an eye and a point, or null if the point is in view.
        string Blocker(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? hit.collider.name + " at " + F(hit.point) : null;
        }

        // ---- The level as LEVELS.md describes it ----------------------------------------------------------

        [Test]
        public void TheLevelIsBuiltAsSpecified()
        {
            Load();
            Assert.AreEqual("matryoshka-boxes", level.Slug);
            Assert.AreEqual("Matryoshka Boxes", level.Title);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("cardboard-box", level.Environment);
            Assert.AreEqual(-L.RugThickness, level.GroundY, "the den's floor is under the rug (LEVELS: 0, the rug's top)");
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.AreSame(ToyCatalog.Get(ToyId.GiftBox), ToyCatalog.HeroOf(10));

            // Three gift boxes, one inside the next: 1.2 / 0.9 / 0.6, standing on the rug at the origin.
            Assert.AreEqual(3, Game.Props.Count, "the three boxes are the only props: blocks, marker and spool are scenery");
            Prop[] boxes = { level.Red, level.Yellow, level.Teal };
            float[] scales = { 1.2f, 0.9f, 0.6f };
            Color[] candies = { Palette.Cherry, Palette.Lemon, Palette.Lagoon };
            ToyDef def = ToyCatalog.Get(ToyId.GiftBox);
            for (int i = 0; i < 3; i++)
            {
                Prop box = boxes[i];
                Assert.AreSame(box, level.Boxes[i]);
                Assert.AreEqual(scales[i], box.Scale, 1e-4f);
                Assert.Less(Vector3.Distance(box.Center, new Vector3(0f, scales[i] * 0.5f, 0f)), 1e-3f, box.Name + " stands on the rug at the nest");
                Assert.AreEqual(0.3f, box.MinScale, 1e-4f);
                // The clamp is the puzzle: no box is taller than two marks. And the two that come out of the
                // red one are no taller than a hop (LEVELS' "largest rise", the top end of its windows for
                // them): the third review's doing, see TwoBoxes_AreNoWayUp.
                Assert.AreEqual(i == 0 ? 2f : 1.15f, box.MaxScale, 1e-4f);
                Assert.AreEqual(GrabPose.Upright, box.GrabPose);
                Assert.AreEqual(5.44f * scales[i] * scales[i] * scales[i], box.Mass, 0.01f);
                ToyInfo info = ToyInfo.Of(box.GameObject);
                Assert.AreEqual(def.Recipe, info.Recipe);
                Assert.IsTrue(Palette.Same(candies[i], info.Candy), box.Name + " wears " + Palette.CandyName(info.Candy));
                Assert.IsTrue(Palette.Peach.Allows(info.Candy));
                Assert.IsTrue(box.Frozen, "a box stays where it is until it is picked up (a shoved box would leave its inner ones behind)");
            }
            Assert.AreEqual("Gift Box", level.Red.Name);
            // Only the red one is there to begin with.
            Assert.IsTrue(level.Red.GameObject.activeSelf && level.Red.Grabbable);
            Assert.IsFalse(level.Yellow.GameObject.activeSelf || level.Yellow.Grabbable, "the yellow box is inside the red one");
            Assert.IsFalse(level.Teal.GameObject.activeSelf || level.Teal.Grabbable, "the blue box is inside the yellow one");
            Assert.AreEqual(1, level.Nested.RevealedCount);
            Assert.AreEqual(3, level.Nested.Count);

            Assert.AreEqual(1, Game.Exits.Count);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(4f, 5.15f, 14f)), 1e-3f);
            Assert.AreEqual(new Vector3(3f, 2.5f, 3f), level.Exit.Trigger.Volume.Size);

            // The geometry of LEVELS: rug top 0, the shelf's sheer face at z 12 and its top at 3.9, walls at
            // x -8 and 8 and z -6 and 16, closed at 12.
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 5f, 5f), Vector3.down, out RaycastHit hit, 30f, mask));
            Assert.AreEqual(0f, hit.point.y, 1e-3f, "the rug");
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 8f, 14f), Vector3.down, out hit, 30f, mask));
            Assert.AreEqual(3.9f, hit.point.y, 1e-3f, "the shelf's top");
            foreach (float y in new[] { 0.05f, 2f, 3.85f })
            {
                Assert.IsTrue(scene.Raycast(new Vector3(3f, y, 5f), Vector3.forward, out hit, 30f, mask));
                Assert.AreEqual(12f, hit.point.z, 1e-3f, "the shelf's face is sheer at y " + y);
            }
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 6f, 5f), Vector3.forward, out hit, 30f, mask));
            Assert.AreEqual(16f, hit.point.z, 1e-3f, "the wall behind the shelf");
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 6f, 5f), Vector3.back, out hit, 30f, mask));
            Assert.AreEqual(-6f, hit.point.z, 1e-3f);
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 6f, 5f), Vector3.left, out hit, 30f, mask));
            Assert.AreEqual(-8f, hit.point.x, 1e-3f);
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 6f, 5f), Vector3.right, out hit, 30f, mask));
            Assert.AreEqual(8f, hit.point.x, 1e-3f);
            Assert.IsTrue(scene.Raycast(new Vector3(3f, 6f, 5f), Vector3.up, out hit, 30f, mask));
            Assert.AreEqual(12f, hit.point.y, 1e-3f, "the sky cap");

            // The shelf leash of LEVELS: a box at rest above the shelf comes back after two seconds.
            Assert.IsFalse(level.ShelfLeash.OutOfBounds(level.Red));
            Assert.AreEqual(3, level.CrumbLeashes.Count);
        }

        [Test]
        public void TheStartShowsTheBoxTheShelfAndWhereTheBoxGoes()
        {
            Load();
            RunSeconds(0.2f);
            Player player = Game.Player;
            Vector3 eye = player.Eye;
            Assert.Less(Vector3.Distance(player.Position, L.PickSpot), 0.01f);

            // The red box under the crosshair, a step away: the first click is the right one.
            Assert.AreSame(level.Red, Game.Grabber.FindTarget(), "a click at the start picks the red box up");
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, player.Forward, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(level.Red, PropRef.Of(hit.collider), "the crosshair is on the red box");
            float k = level.Red.Scale / Vector3.Distance(eye, level.Red.Center);
            Assert.AreEqual(0.434f, k, 0.005f, "LEVELS: picked up from 2.6 in front of it, k = 0.434");

            // In the same picture, with nothing in front of them and not under the level card (the top left
            // corner: 37 % of the width, 24 % of the height): the chart with the red and the blue outline on
            // it, the shelf's top edge, and the way out.
            const float z = L.ShelfZ - 0.06f;
            float big = L.BigSize * 0.5f, top = L.TopSize * 0.5f;
            var points = new Dictionary<string, Vector3>
            {
                { "the red outline's left side", new Vector3(L.StairX - big, 1f, z) },
                { "the red outline's top", new Vector3(L.StairX, L.BigSize, z) },
                { "the red outline's right side", new Vector3(L.StairX + big, 1f, z) },
                { "the blue outline's middle", new Vector3(L.StairX, L.TopAim.y, z) },
                { "the blue outline's top", new Vector3(L.StairX + top, L.TopAim.y + top, z) },
                { "the chart's 2", new Vector3(L.NumeralX, 2f + L.NumeralRise, z) },
                { "the shelf's edge over the stairs", new Vector3(L.StairX, L.ShelfTop - 0.02f, z) },
                { "the way out", L.ExitCentre },
            };
            foreach (KeyValuePair<string, Vector3> point in points)
            {
                Assert.IsNull(Blocker(eye, point.Value), point.Key + " is hidden");
                Vector2 at = OnScreen(point.Value);
                Assert.IsTrue(at.x > 0.03f && at.x < 0.97f && at.y > 0.03f && at.y < 0.97f, point.Key + " is out of the picture at " + at);
                Assert.IsFalse(at.x < 0.37f && at.y < 0.24f, point.Key + " is under the level card at " + at);
            }

            // From the shoe prints: the three outlines and the shelf's edge, nothing in the way.
            Vector3 standEye = L.StandSpot + Vector3.up * Player.BaseEyeHeight;
            foreach (Vector3 point in new[]
                     {
                         new Vector3(L.StairX - big, 0.1f, z), new Vector3(L.StairX + big, L.BigSize, z), new Vector3(L.StairX, L.TopAim.y + top, z),
                         new Vector3(L.StairX, 0.06f, L.StepAim.z), new Vector3(L.StairX, L.ShelfTop - 0.02f, z),
                     })
                Assert.IsNull(Blocker(standEye, point), "from the shoe prints " + point + " is hidden");

            // What is painted, the marks and the mat are looks only; the dressing's looks are one set.
            foreach (string name in new[] { "Paint", "Top Outline", "Pick-up Mark", "Stand Mark", "Room", "Dressing", "Mat" })
            {
                Transform looks = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks, name);
                Assert.AreEqual(0, looks.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
            Assert.IsTrue(level.TopOutline.activeSelf);
        }

        [Test]
        public void TheBoxesComeOutOfEachOther()
        {
            Load();
            var revealed = new List<int>();
            level.Nested.Revealed += index => revealed.Add(index);
            int grabbable = 0;
            foreach (Prop prop in Game.Props)
                if (prop.Grabbable) grabbable++;
            Assert.AreEqual(1, grabbable, "one box to begin with");

            // The red box lifts away and a yellow one is simply there, where it stood.
            BotRunner.Run(Game, bot.Grab(level.Red), 5f);
            CollectionAssert.AreEqual(new[] { 1 }, revealed, "in the tick of the pick-up");
            Prop yellow = level.Yellow;
            Assert.IsTrue(yellow.GameObject.activeSelf && yellow.Grabbable);
            Assert.AreEqual(0.9f, yellow.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(yellow.Center, new Vector3(0f, 0.45f, 0f)), 1e-3f);
            Assert.IsFalse(level.Teal.GameObject.activeSelf, "the blue one is still inside");
            RunSeconds(0.5f);
            // The held box keeps its size in the picture; it stops in front of the yellow one now.
            Assert.IsTrue(level.Red.Held);
            Assert.AreEqual(0.434f, level.Red.Scale / Vector3.Distance(Game.Player.Eye, level.Red.Center), 0.005f);
            Assert.Less(L.BoundsOf(level.Red).max.z, L.BoundsOf(yellow).min.z + 0.01f, "the red box is held in front of the yellow one, not in it");
            Assert.Less(Vector3.Distance(yellow.Center, new Vector3(0f, 0.45f, 0f)), 1e-3f, "which has not moved");

            // Put down beside the mat; the yellow one comes off a blue one the same way.
            BotRunner.Run(Game, bot.DropAt(new Vector3(5f, 0.6f, 2f)), 5f);
            BotRunner.Run(Game, bot.Grab(yellow), 5f);
            CollectionAssert.AreEqual(new[] { 1, 2 }, revealed);
            Assert.IsTrue(level.Teal.GameObject.activeSelf && level.Teal.Grabbable);
            Assert.AreEqual(0.6f, level.Teal.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Teal.Center, new Vector3(0f, 0.3f, 0f)), 1e-3f);
            BotRunner.Run(Game, bot.DropAt(new Vector3(5f, 0.6f, -2f)), 5f);
            BotRunner.Run(Game, bot.Grab(level.Teal), 5f);
            Assert.AreEqual(3, level.Nested.RevealedCount);
            CollectionAssert.AreEqual(new[] { 1, 2 }, revealed, "and that is all of them");
        }

        // ---- The solution ------------------------------------------------------------------------------

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            List<string> said = Listen();
            var ratio = new Dictionary<Prop, float>();
            var dropped = new Dictionary<Prop, float>();
            Game.Events.PropDropped += e =>
            {
                ratio[e.Prop] = e.OldScale / e.GrabDistance;
                dropped[e.Prop] = e.NewScale;
            };
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            TestHelpers.PlayLevel(Game, 120f);
            Prop red = level.Red, yellow = level.Yellow, teal = level.Teal;
            Debug.Log("[Level10] solve: k " + ratio[red].ToString("0.000") + " / " + ratio[yellow].ToString("0.000") + " / " + ratio[teal].ToString("0.000") + "; " + Describe(red) + "; " +
                      Describe(yellow) + "; " + Describe(teal) + "; done in " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + " s; said [" + string.Join(" | ", said) + "]");

            // Each box picked up from the start, 2.6 in front of the nest.
            Assert.AreEqual(0.434f, ratio[red], 0.01f, "LEVELS: k = 0.434");
            Assert.AreEqual(0.323f, ratio[yellow], 0.01f);
            Assert.AreEqual(0.211f, ratio[teal], 0.01f);
            // What the solver makes of them (measured), each inside the window of LEVELS appendix B.
            Assert.AreEqual(1.91f, dropped[red], 0.04f, "LEVELS: 1.90");
            Assert.AreEqual(0.97f, dropped[yellow], 0.03f, "LEVELS: 1.01");
            Assert.AreEqual(1.07f, dropped[teal], 0.03f, "LEVELS: 1.00");
            Assert.IsTrue(dropped[red] >= 1.6f && dropped[red] <= 2f);
            Assert.IsTrue(dropped[yellow] >= 0.75f && dropped[yellow] <= 1.15f);
            Assert.IsTrue(dropped[teal] >= 0.85f && dropped[teal] <= 1.15f);
            Assert.AreEqual(dropped[red], red.Scale);
            Assert.AreEqual(dropped[yellow], yellow.Scale);
            Assert.AreEqual(dropped[teal], teal.Scale);

            // The staircase: the red box against the foot of the shelf, the yellow one on the rug before it,
            // the blue one on the red one at the back - each where its outline is painted.
            Bounds a = L.BoundsOf(yellow), b = L.BoundsOf(red), c = L.BoundsOf(teal);
            Assert.AreEqual(0f, b.min.y, 0.01f);
            Assert.Greater(b.max.z, L.ShelfZ - 0.08f, "the red box stands against the shelf");
            Assert.AreEqual(L.BigSize, b.max.y, 0.03f, "as tall as its outline");
            Assert.AreEqual(L.StairX, b.center.x, 0.02f);
            Assert.AreEqual(0f, a.min.y, 0.01f);
            Assert.Less(b.min.z - a.max.z, 0.2f, "the yellow box stands right in front of the red one (LEVELS: 0.2 short of it)");
            Assert.AreEqual(L.StepAim.z, a.center.z, 0.03f, "on its outline on the rug");
            Assert.AreEqual(L.StepSize, a.max.y, 0.03f);
            Assert.AreEqual(b.max.y, c.min.y, 0.01f, "the blue box stands on the red one");
            Assert.Greater(c.max.z, L.ShelfZ - 0.03f, "at the back, against the shelf");
            Assert.Greater(c.min.z - b.min.z, 0.7f, "leaving a tread to stand on");
            Assert.AreEqual(L.TopSize, c.size.y, 0.03f);
            foreach (Prop box in level.Boxes) Assert.Greater(box.Transform.up.y, 0.9999f, box.Name + " stands level");
            // Four hops, none more than a hop gets up.
            foreach (float rise in new[] { a.max.y, b.max.y - a.max.y, c.max.y - b.max.y, L.ShelfTop - c.max.y })
                Assert.Less(rise, 1.15f, "LEVELS' own limit for a hop");

            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.AreEqual(0, respawns);
            Assert.AreEqual(0, level.ShelfLeash.Returns);
            Assert.IsFalse(level.TopOutline.activeSelf, "the blue outline has done its work");
            Assert.Greater(Game.Player.Position.y, L.ShelfTop - 0.05f, "the player is on the shelf");
            Assert.Less(Game.LevelTicks * Sim.Dt, 30f);
        }

        // LEVELS.md's own solver: each box from its own distance, the blue one from a second place to stand.
        [Test]
        public void TheSolverOfLevelsMd_CompletesTheLevelToo()
        {
            Load();
            List<string> said = Listen();
            BotRunner.Run(Game, level.SolveAsSpecified(bot), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level10] LEVELS.md's solver: " + Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal) + "; done in " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + " s");
            Assert.IsTrue(Game.LevelCompleted);
            // Appendix B: 1.90 / 1.01 / 1.00, within 5 %.
            Assert.AreEqual(1.9f, level.Red.Scale, 1.9f * 0.05f);
            Assert.AreEqual(1.01f, level.Yellow.Scale, 1.01f * 0.05f);
            Assert.AreEqual(1f, level.Teal.Scale, 0.05f);
            Assert.IsEmpty(said);
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
                TestHelpers.PlayLevel(Game, 120f);
                ticks[run] = Game.LevelTicks;
                rest[run] = level.Teal.Center;
            }
            for (int run = 1; run < runs; run++)
            {
                Assert.AreEqual(ticks[0], ticks[run], "the same number of ticks in run " + run);
                Assert.AreEqual(rest[0], rest[run], "the blue box comes to rest in the same place in run " + run);
            }
        }

        // ---- No way up but the stairs --------------------------------------------------------------------

        [Test]
        public void WalkingAndJumpingAtTheShelf_DoesNotCompleteTheLevel()
        {
            Load();
            Input = new ScriptedInput();
            Game.Input = Input;
            float highest = 0f, farthest = float.MinValue;
            // Sprint and jump at the shelf's face: under the exit, at the chart, into both corners, and from
            // the top of the red box where it stands at the start.
            foreach (Vector3 from in new[] { new Vector3(4f, 0f, 6f), new Vector3(-2f, 0f, 6f), new Vector3(-7.4f, 0f, 6f), new Vector3(7.4f, 0f, 6f), new Vector3(0f, 1.25f, 0f) })
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
            Assert.IsFalse(Game.LevelCompleted, "the exit is on the shelf");
            Assert.Less(highest, 2.7f, "a jump, even off the red box, is nowhere near 3.9 (highest " + highest + ")");
            Assert.Less(farthest, L.ShelfZ, "nobody gets through the shelf's face");
            Assert.Greater(Game.Player.Position.y, -0.1f);
        }

        // One box cannot do it, however big: the clamp. Two cannot either, the way stairs are climbed.
        // (The reviews found two ways with two boxes. The step pulled up after oneself - picked up again
        // from the top of the red box and let go behind or under one's feet - is closed: see
        // FromUpOnTheBigBox_TheStepIsNotPulledUp. Left in, as a harder solution than the intended one: two
        // boxes of 1.25 to 1.35 each, one on the other and to one side - three hops at the very limit of a
        // hop, see Probe_TwoBoxes at the end.)
        [Test]
        public void ABoxAsBigAsItGets_IsNoWayUpByItself_AndNeitherAreTwo()
        {
            Load();
            List<string> said = Listen();
            // The red box at 2.0 against the shelf (held high against the chart from the shoe prints).
            Big(6.7f, new Vector3(L.StairX, 3.4f, L.ShelfZ));
            Assert.AreEqual(2f, level.Red.Scale, 1e-3f);
            Assert.Greater(L.BoundsOf(level.Red).max.z, L.ShelfZ - 0.05f);
            float highest = 0f;
            Game.Context.OnUpdate(dt =>
            {
                if (Game.Player.Grounded) highest = Mathf.Max(highest, Game.Player.Position.y);
            });
            IEnumerator HopAt(Vector3 from, Vector3 at)
            {
                yield return bot.WalkTo(from, 0.1f, 10f);
                for (int i = 0; i < 4; i++)
                {
                    yield return bot.Jump(false);
                    IEnumerator walk = bot.WalkTo(at, 0.2f, 1.2f);
                    for (int t = 0; t < 60 && walk.MoveNext(); t++) yield return walk.Current;
                }
            }
            Drive(HopAt(new Vector3(L.StairX, 0f, 9.4f), new Vector3(L.StairX, 2f, 11f)), 20f);
            Assert.AreEqual(0f, highest, 0.05f, "two marks are too tall to hop onto");
            Assert.IsFalse(Game.LevelCompleted);
            CollectionAssert.Contains(said, L.TallLine);

            // With the yellow box as a step: up to the top of the red one, and no further.
            Step();
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            IEnumerator UpAndAtTheShelf()
            {
                yield return bot.WalkTo(new Vector3(a.center.x, 0f, a.min.z - 0.55f), 0.1f, 10f);
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(a.center.x, 0f, a.center.z), 0.2f, 3f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(b.center.x, 0f, b.center.z), 0.2f, 3f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return HopAt(new Vector3(b.center.x, 0f, b.max.z - 0.4f), new Vector3(b.center.x, 0f, L.ShelfZ + 1f));
            }
            Drive(UpAndAtTheShelf(), 30f);
            Assert.AreEqual(2f, highest, 0.05f, "on top of the red box");
            Assert.IsFalse(Game.LevelCompleted, "1.9 more is no hop");
        }

        // ---- The windows (LEVELS appendix B), driven through the pick-up and the standing place -----------

        // The red box picked up from the start and let go from z: 7.6 gives 1.6 (the low end), the shoe
        // prints 1.9; held high against the chart from the shoe prints it is 2.0, the clamp.
        [TestCase(7.6f, false, 1.61f, TestName = "TheBigBox_AtTheLowEndOfItsWindow_Works")]
        [TestCase(6.7f, false, 1.91f, TestName = "TheBigBox_AsTheSolverMakesIt_Works")]
        [TestCase(6.7f, true, 2f, TestName = "TheBigBox_AtTheClamp_Works")]
        [TestCase(6.3f, false, 1.97f, TestName = "TheBigBox_FromAStepBehindTheShoePrints_Works")]
        public void TheBigBox_Works(float standZ, bool heldHigh, float scale)
        {
            Load();
            Big(standZ, heldHigh ? new Vector3(L.StairX, 3.4f, L.ShelfZ) : (Vector3?)null);
            Assert.AreEqual(scale, level.Red.Scale, 0.03f);
            Step();
            Top();
            Debug.Log("[Level10] big from z " + standZ + (heldHigh ? " held high" : "") + ": " + Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal));
            Assert.IsTrue(Climb(), "stairs of " + level.Yellow.Scale + " / " + level.Red.Scale + " / " + level.Teal.Scale + " were not climbed (player at " + Game.Player.Position + ")");
        }

        // The yellow box picked up from 3.7 before the nest comes out 0.75 (the low end), from 1.85 it is 1.15.
        [TestCase(-3.7f, 0.75f, TestName = "TheStep_AtTheLowEndOfItsWindow_Works")]
        [TestCase(-1.85f, 1.15f, TestName = "TheStep_AtTheHighEndOfItsWindow_Works")]
        public void TheStep_Works(float pickZ, float scale)
        {
            Load();
            Big();
            Step(pickZ);
            Assert.AreEqual(scale, level.Yellow.Scale, 0.03f);
            Top();
            Assert.IsTrue(Climb(), "stairs of " + level.Yellow.Scale + " / " + level.Red.Scale + " / " + level.Teal.Scale + " were not climbed (player at " + Game.Player.Position + ")");
        }

        // The blue box picked up from 3.5 before the nest comes out 0.85 (the low end), from 2.3 it is 1.15.
        [TestCase(-3.5f, 0.85f, TestName = "TheTopBox_AtTheLowEndOfItsWindow_Works")]
        [TestCase(-2.3f, 1.15f, TestName = "TheTopBox_AtTheHighEndOfItsWindow_Works")]
        public void TheTopBox_Works(float pickZ, float scale)
        {
            Load();
            Big();
            Step();
            Top(pickZ);
            Assert.AreEqual(scale, level.Teal.Scale, 0.03f);
            Assert.IsTrue(Climb(), "stairs of " + level.Yellow.Scale + " / " + level.Red.Scale + " / " + level.Teal.Scale + " were not climbed (player at " + Game.Player.Position + ")");
        }

        // A size that is clearly wrong: the stairs stand, and somewhere a step is a good deal more than a hop
        // (1.45 and up; the last hop that still gets up is about 1.35). The red box let go from too near
        // (1.3: the shelf is 1.5 above the blue one), the yellow one picked up from the back wall (0.5) with
        // the red one as big as it gets (1.45 above it). (An inner box cannot be too big since the third
        // review: it grows no taller than a hop. See the two tests after this one.)
        [TestCase(8.5f, false, -2.6f, -2.6f, 1.29f, 0.97f, 1.08f, TestName = "ABigBoxThatIsTooSmall_DoesNotWork_AndTheLevelSaysWhy")]
        [TestCase(6.7f, true, -5.5f, -2.6f, 2f, 0.54f, 1.08f, TestName = "AStepThatIsTooSmall_DoesNotWork_AndTheLevelSaysWhy")]
        public void AClearlyWrongSize_DoesNotWork(float standZ, bool heldHigh, float stepPickZ, float topPickZ, float big, float step, float top)
        {
            Load();
            Big(standZ, heldHigh ? new Vector3(L.StairX, 3.4f, L.ShelfZ) : (Vector3?)null);
            Step(stepPickZ);
            Top(topPickZ);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.AreEqual(big, level.Red.Scale, 0.04f, stairs);
            Assert.AreEqual(step, level.Yellow.Scale, 0.04f, stairs);
            Assert.AreEqual(top, level.Teal.Scale, 0.04f, stairs);
            List<string> said = Listen();
            // Not the solver's way up, and not anybody's: straight at it, hopping all the way.
            Assert.IsFalse(Climb(), "should not be climbable: " + stairs);
            Drive(Scramble(10f), 25f);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Assert.IsFalse(Game.LevelCompleted, "should not be climbable: " + stairs);
            Assert.Less(Game.Player.Position.y, L.ShelfTop - 0.5f, stairs);
            CollectionAssert.Contains(said, L.TallLine, "the hop that fails is told why");
            foreach (string line in said) Assert.AreEqual(L.TallLine, line, "and nothing else is said");
            Assert.LessOrEqual(said.Count, 3, "now and then, not at every hop: " + said.Count + " times in " + ((Game.LevelTicks) * Sim.Dt).ToString("0") + " s");
        }

        // The yellow box picked up from right beside it - what most people do with the second box - used to
        // come out 1.3 to 1.5 from the shoe prints: too tall to hop onto, and nothing said until the hop.
        // It stops growing at a hop's height now (1.15) and comes down a little before its footprint: a
        // step that works, with a gap to hop across. Nothing is said, and nothing needs saying.
        [TestCase(-0.8f)]
        [TestCase(-1.1f)]
        [TestCase(-1.4f)]
        [TestCase(-1.8f)]
        public void AStepPickedUpFromRightBesideIt_GrowsNoTallerThanAHop_AndTheStairsWork(float pickZ)
        {
            Load();
            List<string> said = Listen();
            Big();
            Step(pickZ);
            Assert.AreEqual(L.InnerMaxScale, level.Yellow.Scale, 1e-3f, Describe(level.Yellow));
            Assert.AreEqual(0f, L.BoundsOf(level.Yellow).min.y, 0.01f, "it stands on the rug: " + Describe(level.Yellow));
            Assert.Greater(level.Yellow.Transform.up.y, 0.999f, "level");
            Top();
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.IsEmpty(said, stairs);
            // Anybody's way up: straight at it, hopping.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(L.StairX, 0f, 6.2f), 0.2f), 20f);
            Drive(Scramble(15f), 30f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            Assert.IsTrue(Game.LevelCompleted, "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
            Assert.IsEmpty(said, stairs);
        }

        // The blue box picked up from too near (inside the mat's rim) used to come out 1.3 to 1.8 on the red
        // one: too tall to hop onto, nothing said until the hop. Now it is as big as it gets (1.15) well
        // before the shelf: it comes down short of it - on the front of the red box, on the step, or on
        // the rug - and the level says what to do about that. Picked up again from further back and held
        // in its outline from the shoe prints, it is the last step.
        [TestCase(-1.9f)]
        [TestCase(-1.3f)]
        [TestCase(-0.9f)]
        public void ATopBoxPickedUpFromTooNear_StopsGrowingShortOfTheShelf_AndTheLevelSaysWhatToDo(float pickZ)
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            Top(pickZ);
            RunSeconds(1f);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.AreEqual(L.InnerMaxScale, level.Teal.Scale, 1e-3f, stairs);
            Assert.Less(L.BoundsOf(level.Teal).max.z, L.ShelfZ - 0.25f, "short of the shelf: " + stairs);
            CollectionAssert.AreEqual(new[] { L.InnerClampLine }, said, stairs);
            // No stairs: it is not on the red box at all, or it is and leaves no tread before it. (Seen in
            // the probes: on the front of the red box from 1.9, tumbled back to the shoe prints from 1.3 -
            // the step knocked a step back on its way - and on top of the step from 0.9.)
            Bounds red = L.BoundsOf(level.Red), blue = L.BoundsOf(level.Teal);
            Assert.IsTrue(!L.StandsOn(level.Teal, level.Red) || blue.min.z - red.min.z < 0.25f, "there should be no stairs: " + stairs);
            Assert.IsTrue(level.TopOutline.activeSelf, "the blue outline still says where it goes: " + stairs);

            // From further back - as far from it as the shoe prints are from the chart - and again.
            said.Clear();
            Vector3 lies = level.Teal.Center;
            IEnumerator Again()
            {
                yield return bot.WalkTo(new Vector3(L.StairX, 0f, lies.z - 5.2f), 0.1f, 12f);
                yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y < 0.05f, 3f);
                yield return bot.Wait(0.1f);
                yield return bot.Grab(level.Teal);
                yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                yield return bot.DropAt(L.TopAim);
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => L.AtRest(level.Teal), 5f);
            }
            BotRunner.Run(Game, Again(), 40f);
            stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), stairs);
            Assert.Greater(L.BoundsOf(level.Teal).max.z, L.ShelfZ - 0.25f, "against the shelf: " + stairs);
            Assert.IsEmpty(said, stairs);
            Assert.IsTrue(Climb(), "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
        }

        // At its largest a little short of the shelf - picked up from just inside the mat's rim - the blue
        // box comes down on the red one with room to stand before it: the last step all the same, and
        // nothing is said.
        [Test]
        public void ATopBoxAtItsLargest_ALittleShortOfTheShelf_IsStillTheLastStep_AndNothingIsSaid()
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            Top(-2.1f);
            Bounds red = L.BoundsOf(level.Red), blue = L.BoundsOf(level.Teal);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.AreEqual(L.InnerMaxScale, level.Teal.Scale, 1e-3f, stairs);
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), stairs);
            Assert.Greater(L.ShelfZ - blue.max.z, 0.25f, "short of the shelf: " + stairs);
            Assert.Greater(blue.min.z - red.min.z, 0.3f, "with a tread before it: " + stairs);
            Assert.IsFalse(level.TopOutline.activeSelf, "the blue outline has done its work");
            Assert.IsEmpty(said, stairs);
            Assert.IsTrue(Climb(), "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
            Assert.IsEmpty(said, stairs);
        }

        // The stairs the solver builds need no care in the climbing: forward and hop, forward and hop.
        [Test]
        public void TheSolversStairs_AreClimbedByHoppingStraightAtThem()
        {
            Load();
            List<string> said = Listen();
            Big();
            Step();
            Top();
            BotRunner.Run(Game, bot.WalkTo(new Vector3(L.StairX, 0f, 7f), 0.2f), 20f);
            Drive(Scramble(15f), 30f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            Assert.IsTrue(Game.LevelCompleted, "player at " + F(Game.Player.Position));
            Assert.IsEmpty(said);
        }

        // Coming straight back from the shoe prints the boxes are picked up from the other side of the mat,
        // 17 degrees off the square of the nest. In the hand a box is square to the view (SquareOnGrab, since
        // the second review; before it the two stood 17 degrees askew at the shelf), so the stairs stand
        // as they do picked up from the start.
        [Test]
        public void PickedUpFromTheOtherSideOfTheMat_TheStairsStillWork()
        {
            Load();
            List<string> said = Listen();
            Vector3 away = (L.StandSpot - L.Nest).normalized;
            Vector3 rim = L.Nest + away * 2.7f;
            IEnumerator Natural()
            {
                yield return level.Carry(bot, level.Red, L.PickSpot, L.StandSpot);
                yield return bot.DropAt(L.BigAim);
                yield return bot.Until(() => L.AtRest(level.Red), 4f);
                foreach (Prop box in new[] { level.Yellow, level.Teal })
                {
                    yield return bot.WalkTo(rim, 0.08f, 12f);
                    yield return bot.Grab(box);
                    yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                    yield return bot.DropAt(box == level.Yellow ? L.StepAim : L.TopAim);
                    yield return bot.Wait(0.2f);
                    yield return bot.Until(() => L.AtRest(box), 5f);
                }
                yield return level.Climb(bot, level.Yellow, level.Red, level.Teal);
            }
            BotRunner.Run(Game, Natural(), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Debug.Log("[Level10] from the other side: " + Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal) + "; yaw " +
                      level.Yellow.Transform.eulerAngles.y.ToString("0.0") + " / " + level.Teal.Transform.eulerAngles.y.ToString("0.0"));
            Assert.IsTrue(Game.LevelCompleted);
            float askew = Mathf.Abs(Mathf.DeltaAngle(0f, level.Teal.Transform.eulerAngles.y * 4f)) / 4f;
            Assert.Less(askew, 1.5f, "the blue box stands square to the shelf");
            Assert.IsEmpty(said);
        }

        // ---- What the level says when a try fails ----------------------------------------------------------

        // The eye is lower than the red box is tall: a box held toward its top catches its front edge at
        // about half the size and falls in front of it.
        [TestCase(2.2f)]
        [TestCase(2.6f)]
        public void HeldTowardTheTopOfTheBigBox_TheBlueOneCatchesItsEdge_AndTheLevelSaysSo(float aimY)
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            Top(-2.6f, new Vector3(L.StairX, aimY, L.ShelfZ));
            RunSeconds(0.5f);
            Debug.Log("[Level10] held at " + aimY + ": " + Describe(level.Teal));
            CollectionAssert.AreEqual(new[] { L.EdgeLine }, said);
            Assert.Less(level.Teal.Scale, 0.75f);
            Assert.Less(level.Teal.Center.y, L.BoundsOf(level.Red).max.y, "it did not end up on the red box");
            Assert.IsTrue(level.TopOutline.activeSelf, "the outline still says where to hold it");

            // Picked up again from the rim of the mat... it lies by the stairs now: from as far away as it
            // was, and held against the chart from the shoe prints, it goes where it should.
            Vector3 lies = level.Teal.Center;
            IEnumerator Again()
            {
                yield return bot.WalkTo(new Vector3(lies.x, 0f, lies.z - 2.9f), 0.1f, 12f);
                yield return bot.Grab(level.Teal);
                yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                yield return bot.DropAt(L.TopAim);
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => L.AtRest(level.Teal), 5f);
            }
            BotRunner.Run(Game, Again(), 40f);
            Assert.AreEqual(L.BoundsOf(level.Red).max.y, L.BoundsOf(level.Teal).min.y, 0.01f, "on the red box this time (" + Describe(level.Teal) + ")");
            Assert.IsTrue(Climb(), "stairs of " + level.Yellow.Scale + " / " + level.Red.Scale + " / " + level.Teal.Scale);
        }

        // The yellow box held against the red one's face is not "caught": it stops there, as meant.
        [Test]
        public void TheStep_StoppedByTheBigBoxsFace_IsNotToldAnything()
        {
            Load();
            Big();
            List<string> said = Listen();
            Step(-2.6f, new Vector3(L.StairX, 0.9f, 11f));
            Assert.IsEmpty(said);
            Assert.AreEqual(0f, L.BoundsOf(level.Yellow).min.y, 0.01f);
            Assert.Greater(level.Yellow.Scale, 0.9f);
        }

        // From too far back the red box is as big as it gets before it reaches the shelf: it hangs in the
        // air short of it and comes down there.
        [TestCase(5f)]
        [TestCase(3f)]
        public void LetGoFromTooFarBack_TheBigBoxStopsShortOfTheShelf_AndTheLevelSaysSo(float standZ)
        {
            Load();
            List<string> said = Listen();
            Big(standZ);
            Assert.AreEqual(2f, level.Red.Scale, 1e-3f);
            Assert.Less(L.BoundsOf(level.Red).max.z, L.ShelfZ - 1f);
            CollectionAssert.AreEqual(new[] { L.ClampLine }, said);
        }

        // At its largest and against the shelf there is nothing to say.
        [Test]
        public void AtTheClampAndAgainstTheShelf_NothingIsSaid()
        {
            Load();
            List<string> said = Listen();
            Big(6.7f, new Vector3(L.StairX, 3.4f, L.ShelfZ));
            Assert.AreEqual(2f, level.Red.Scale, 1e-3f);
            Assert.IsEmpty(said);
        }

        // The blue box, 0.6 across: from 5 away it looks too small to become a step from the shoe prints.
        [TestCase(-2.6f, false)]
        [TestCase(-4f, false)]
        [TestCase(-5f, true)]
        public void APickUpFromTooFarAway_IsCalledOut(float pickZ, bool tooFar)
        {
            Load();
            IEnumerator Unwrap()
            {
                yield return bot.Grab(level.Red);
                yield return bot.DropAt(new Vector3(5f, 0.6f, 2f));
                yield return bot.Grab(level.Yellow);
                yield return bot.DropAt(new Vector3(5f, 0.6f, -2f));
                yield return bot.WalkTo(PickFrom(pickZ), 0.05f, 10f);
            }
            BotRunner.Run(Game, Unwrap(), 30f);
            List<string> said = Listen();
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            BotRunner.Run(Game, bot.Grab(level.Teal), 5f);
            RunSeconds(0.1f);
            Assert.AreEqual(tooFar, k < L.LooksTooSmall, "k " + k);
            if (tooFar) CollectionAssert.AreEqual(new[] { L.FarLine }, said);
            else Assert.IsEmpty(said);
        }

        // ---- No strand, no restart ------------------------------------------------------------------------

        [Test]
        public void ABoxThatGoesOverTheShelfsEdge_ComesBackToTheStart()
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            // Held too high the blue box clears the shelf's edge and grows on behind it. (Picked up from a
            // step behind the mat: from the start it is as big as it gets before it is over the edge, and
            // comes down on the red box.)
            Top(-3.5f, new Vector3(L.StairX, 5.4f, L.ShelfZ));
            Assert.Greater(level.Teal.Center.z, L.ShelfZ, "the blue box went over the edge (" + Describe(level.Teal) + ")");
            Assert.IsTrue(level.ShelfLeash.OutOfBounds(level.Teal));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.ShelfLeash.Returns == 1, 4f), "two seconds at rest up there and it is sent back");
            RunSeconds(0.2f);
            Assert.AreEqual(0.6f, level.Teal.Scale, 1e-4f, "at the size it started with");
            Assert.Less(Vector3.Distance(level.Teal.Center, new Vector3(0f, 0.3f, 0f)), 0.01f, "at the nest, which is free");
            CollectionAssert.AreEqual(new[] { L.ShelfLine }, said);
            // And the level goes on from there.
            Top();
            Assert.IsTrue(Climb());
        }

        [Test]
        public void ABoxOnTheShelf_Stays_WhileThePlayerIsUpThereToo()
        {
            Load();
            Big();
            Step();
            Top();
            // Up the stairs, short of the exit; then (a test fixture's shortcut) a box put on the shelf.
            IEnumerator Up()
            {
                yield return level.Climb(bot, level.Yellow, level.Red, level.Teal);
            }
            var runner = new BotRunner(Up(), bot, Game);
            for (int i = 0; i < TestHelpers.Ticks(30f) && !(Game.Player.Grounded && Game.Player.Position.y > L.ShelfTop - 0.05f); i++)
            {
                Assert.IsTrue(runner.Advance());
                Game.Tick();
            }
            Assert.IsFalse(Game.LevelCompleted);
            Game.Input = Input = new ScriptedInput();
            level.Yellow.SetPose(new Vector3(-5f, L.ShelfTop + 0.6f, 14f), Quaternion.identity);
            RunSeconds(4f);
            Assert.AreEqual(0, level.ShelfLeash.Returns, "with the player on the shelf the box is in plain view");
            Assert.Greater(level.Yellow.Center.y, L.ShelfTop);
            // Down again (the fixture once more): now it is out of sight, and comes back.
            Game.Player.Teleport(new Vector3(4f, 0f, 4f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.ShelfLeash.Returns == 1, 4f));
            Assert.AreEqual(0.9f, level.Yellow.Scale, 1e-4f);
        }

        [Test]
        public void ACrumb_GoesBackToTheStart()
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            // The blue box let go at one's own feet is as small as a box gets.
            IEnumerator AtTheFeet()
            {
                yield return level.Carry(bot, level.Teal, L.PickSpot, new Vector3(3f, 0f, 5f));
                yield return bot.DropAt(new Vector3(3f, 0f, 5.2f));
            }
            BotRunner.Run(Game, AtTheFeet(), 30f);
            Assert.AreEqual(0.3f, level.Teal.Scale, 0.01f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.CrumbLeashes[2].Returns == 1, 4f), "a second and a half and it is sent back");
            RunSeconds(0.2f);
            Assert.AreEqual(0.6f, level.Teal.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Teal.Center, new Vector3(0f, 0.3f, 0f)), 0.01f);
            CollectionAssert.AreEqual(new[] { L.CloseCrumbLine }, said, "and the line says why it was that small: it was let go that close");
            Assert.IsTrue(level.Teal.Frozen, "it waits there to be picked up");
            Top();
            Assert.IsTrue(Climb());
        }

        // A crumb of the other kind: picked up from too far away (which is said at the pick-up), it is small
        // wherever it is let go. Sent back, it is not told that it was let go too close.
        [Test]
        public void ACrumbFromAPickUpFromTooFarAway_GoesBackToo_AndIsNotToldItWasLetGoTooClose()
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            IEnumerator FromAfar()
            {
                // (Out of the yellow one first, and put down by the mat; then picked up from the back wall.)
                yield return level.Carry(bot, level.Teal, L.PickSpot, new Vector3(1.5f, 0f, -1.5f));
                yield return bot.DropAt(new Vector3(1.5f, 0.3f, 1.2f));
                yield return bot.Wait(0.5f);
                yield return bot.WalkTo(new Vector3(1.5f, 0f, -5.2f), 0.1f, 10f);
                yield return bot.Grab(level.Teal);
                yield return bot.DropAt(new Vector3(1.5f, 0f, -2.6f));
            }
            BotRunner.Run(Game, FromAfar(), 40f);
            Assert.Less(level.Teal.Scale, L.CrumbScale, Describe(level.Teal));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.CrumbLeashes[2].Returns == 1, 4f));
            RunSeconds(0.2f);
            Assert.AreEqual(0.6f, level.Teal.Scale, 1e-4f);
            CollectionAssert.AreEqual(new[] { L.FarLine, L.CrumbLine }, said);
        }

        // The room is closed, so nothing falls out of the world by itself. If a box ever does, the kill plane
        // sends it back to the start - and two solid boxes cannot stand in one place.
        [Test]
        public void ABoxThatFallsOutOfTheWorld_ComesBack_AndNeverIntoAnotherBox()
        {
            Load();
            int respawns = 0;
            Game.Events.PropRespawned += e => respawns++;
            // The red box has been picked up and put down; the yellow one stands in the nest. The red one
            // leaves the world (a test fixture's doing).
            BotRunner.Run(Game, bot.Grab(level.Red), 5f);
            BotRunner.Run(Game, bot.DropAt(new Vector3(-5f, 0.6f, 3f)), 5f);
            RunSeconds(0.5f);
            level.Red.SetPose(new Vector3(0f, -40f, 0f), Quaternion.identity);
            RunSeconds(0.1f);
            Assert.AreEqual(1, respawns);
            Assert.AreEqual(1.2f, level.Red.Scale, 1e-4f, "at the size it started with");
            Assert.Less(Vector3.Distance(level.Red.Center, new Vector3(2.4f, 0.6f, 0f)), 0.01f, "beside the nest: the yellow box stands in it (" + F(level.Red.Center) + ")");
            Assert.Less(TestHelpers.DeepestOverlap(Game, level.Red), 0.005f);
            Assert.Less(Vector3.Distance(level.Yellow.Center, new Vector3(0f, 0.45f, 0f)), 1e-3f, "the yellow box was not touched");

            // The yellow one goes the same way: its place in the nest is free, it comes back to it.
            BotRunner.Run(Game, bot.Grab(level.Yellow), 5f);
            BotRunner.Run(Game, bot.DropAt(new Vector3(-5f, 0.5f, 0f)), 5f);
            RunSeconds(0.5f);
            level.Yellow.SetPose(new Vector3(0f, -40f, 0f), Quaternion.identity);
            RunSeconds(0.1f);
            Assert.AreEqual(2, respawns);
            // (The blue box came out when the yellow one was picked up, and stands in the nest now.)
            Assert.Less(Vector3.Distance(level.Yellow.Center, new Vector3(2.4f, 0.45f, 2.5f)), 0.01f, "the next free place beside the nest (" + F(level.Yellow.Center) + ")");
            foreach (Prop box in level.Boxes) Assert.Less(TestHelpers.DeepestOverlap(Game, box), 0.005f, box.Name);
            RunSeconds(1f);
            foreach (Prop box in level.Boxes) Assert.Less(box.Velocity.magnitude, 1e-3f, box.Name + " stands still");

            // And the level can still be solved: each box picked up from 2.6 in front of where it stands.
            IEnumerator FromWhereTheyStand()
            {
                Vector3 before = new Vector3(0f, 0f, -2.6f);
                yield return Place(level.Red, level.Red.Center + before - Vector3.up * level.Red.Center.y, L.StandSpot, L.BigAim);
                yield return Place(level.Yellow, level.Yellow.Center + before - Vector3.up * level.Yellow.Center.y, L.StandSpot, L.StepAim);
                yield return Place(level.Teal, L.PickSpot, L.StandSpot, L.TopAim);
            }
            BotRunner.Run(Game, FromWhereTheyStand(), 120f);
            Assert.IsTrue(Climb(), "stairs of " + level.Yellow.Scale + " / " + level.Red.Scale + " / " + level.Teal.Scale);
        }

        [Test]
        public void RestartingMidSolve_ThenSolving_Works()
        {
            Load();
            TestHelpers.PlayLevel(Game, 120f);
            int ticks = Game.LevelTicks;

            // Two boxes placed, the blue one in the hand: restart.
            Load();
            Big();
            Step();
            BotRunner.Run(Game, level.Carry(bot, level.Teal, L.PickSpot, L.StandSpot), 30f);
            Assert.IsTrue(level.Teal.Held);
            Game.RestartLevel();
            Assert.AreSame(level, Game.Level, "the same level object builds itself again");
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(3, Game.Props.Count);
            Assert.AreEqual(1.2f, level.Red.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Red.Center, new Vector3(0f, 0.6f, 0f)), 1e-3f);
            Assert.IsFalse(level.Yellow.GameObject.activeSelf || level.Teal.GameObject.activeSelf, "one box again");
            Assert.AreEqual(1, level.Nested.RevealedCount);
            Assert.IsTrue(level.TopOutline.activeSelf);
            Assert.Less(Vector3.Distance(Game.Player.Position, L.PickSpot), 0.01f);
            bot = new Bot(Game);
            TestHelpers.PlayLevel(Game, 120f);
            Assert.AreEqual(ticks, Game.LevelTicks, "and the solution plays as it does from a fresh start");
        }

        // ---- The words, the walls, the toys -----------------------------------------------------------------

        [Test]
        public void TheWordsOfTheLevel_ArePlainAndTheHintsEscalate()
        {
            Load();
            var lines = new List<string>();
            foreach (FieldInfo field in typeof(L).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                string text = (string)field.GetRawConstantValue();
                if (text.Contains(" ")) lines.Add(text);
            }
            Assert.AreEqual(11, lines.Count, "the level's lines: " + string.Join(" | ", lines));
            var all = new List<string>(lines) { level.Blurb };
            all.AddRange(level.Hints);
            Assert.AreEqual(all.Count, new HashSet<string>(all).Count, "no two lines are the same");
            foreach (string text in all)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "plain characters only in: " + text);
                Assert.IsTrue(text.EndsWith(".") || text.EndsWith("?"), text);
                Assert.AreEqual(text.Trim(), text);
                StringAssert.DoesNotContain("  ", text);
                string lower = text.ToLowerInvariant();
                // One verb for one act, as in the levels before: toys are picked up and let go.
                foreach (string word in new[] { "grab", "take it", "taken", "drop", "release" })
                    Assert.IsFalse(lower.Contains(word), "\"" + text + "\" says \"" + word + "\"");
            }
            foreach (string line in lines) Assert.LessOrEqual(line.Length, 110, "too long for a toast: " + line);
            Assert.LessOrEqual(level.Blurb.Length, 60);
            Assert.AreEqual(Level01CheeseWedge.SmallLine, L.FarLine, "the campaign's one sentence for a pick-up from too far away");

            string first = level.Hints[0], second = level.Hints[1], third = level.Hints[2];
            // Five lines of the pause card at full size is about 200 characters.
            foreach (string hint in level.Hints) Assert.LessOrEqual(hint.Length, 200, hint);
            foreach (string word in new[] { "outline", "shoe prints", "bullseye", "edge", "rim", "shelf", "chart", "stairs" })
                StringAssert.DoesNotContain(word, first.ToLowerInvariant(), "the first hint does not give the solution away");
            StringAssert.Contains("another one inside", first);
            StringAssert.Contains("'2'", second, "the second hint is the rule: the clamp, and what a hop is good for");
            StringAssert.Contains("one mark", second);
            StringAssert.DoesNotContain("shoe prints", second, "where to stand is the third hint's");
            StringAssert.StartsWith("Pick", third, "the third hint is the whole solution, the pick-up included");
            foreach (string step in new[] { "rim of the mat", "shoe prints", "outline of its colour", "let go", "red", "against the shelf", "yellow", "on the rug", "blue", "top of the chart", "hop up" })
                StringAssert.Contains(step, third);
            // (Not "at the foot of the shelf": held at the very foot the red box comes down short of it.)
            StringAssert.DoesNotContain("foot of the shelf", third);
            Assert.Less(third.IndexOf("red"), third.IndexOf("yellow"));
            Assert.Less(third.IndexOf("yellow"), third.IndexOf("blue"));
        }

        [Test]
        public void TheRoomIsSealed()
        {
            Load();
            RunSeconds(0.1f);
            PhysicsScene scene = Game.PhysicsScene;
            int mask = Layers.DefaultMask;
            float half = L.HalfWidth, near = L.NearZ, top = L.WallTop;
            // The shelf's face from the rug to its top: no gap.
            for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.3f)
                for (float y = 0.05f; y <= L.ShelfTop - 0.04f; y += 0.3f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, L.ShelfZ - 0.1f), Vector3.forward, out _, 0.5f, mask, QueryTriggerInteraction.Ignore), "a way into the shelf at x " + x + ", y " + y);
            // The side walls and the near wall up to the sky cap, though only low cardboard is drawn on two
            // sides; the wall behind the shelf.
            for (float y = 0.2f; y <= top - 0.09f; y += 0.5f)
            {
                for (float z = near + 0.1f; z <= L.FarZ - 0.09f; z += 0.5f)
                {
                    if (z > L.ShelfZ - 0.1f && y < L.ShelfTop + 0.1f) continue;
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.left, out _, half + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at -X, z " + z + ", y " + y);
                    Assert.IsTrue(scene.Raycast(new Vector3(0f, y, z), Vector3.right, out _, half + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at +X, z " + z + ", y " + y);
                }
                for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.5f)
                {
                    Assert.IsTrue(scene.Raycast(new Vector3(x, y, 5f), Vector3.back, out _, 5f - near + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out at the near wall, x " + x + ", y " + y);
                    if (y > L.ShelfTop + 0.1f)
                        Assert.IsTrue(scene.Raycast(new Vector3(x, y, 12.5f), Vector3.forward, out _, L.FarZ - 12.5f + 0.05f, mask, QueryTriggerInteraction.Ignore), "a way out behind the shelf, x " + x + ", y " + y);
                }
            }
            for (float x = -half + 0.1f; x <= half - 0.09f; x += 0.5f)
                for (float z = near + 0.1f; z <= L.FarZ - 0.09f; z += 0.5f)
                    Assert.IsTrue(scene.Raycast(new Vector3(x, top - 0.5f, z), Vector3.up, out _, 0.55f, mask, QueryTriggerInteraction.Ignore), "a way out at the top, x " + x + ", z " + z);
        }

        static IEnumerator LookAndClick(Bot bot, Vector3 point)
        {
            yield return bot.LookAt(point);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
        }

        [Test]
        public void NothingButTheBoxes_CanBePickedUp()
        {
            Load();
            // Every body in the level is one of the three boxes; everything else is the world.
            Assert.AreEqual(3, Game.Props.Count);
            // (Asked of the body itself: the two boxes still in their wrapping are inactive, and an inactive
            // collider has no attached body to be found through.)
            foreach (Rigidbody body in Game.LevelRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                PropRef reference = body.GetComponent<PropRef>();
                Assert.IsTrue(reference != null && reference.Prop != null && level.Boxes.Contains(reference.Prop), body.name + " is a body that is not one of the boxes");
            }
            foreach (Collider collider in Game.LevelRoot.GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<PropRef>(true) == null) Assert.AreNotEqual(Layers.Prop, collider.gameObject.layer, collider.name);

            // Clicks at the scenery pick nothing up. From behind the start: what stands along the walls
            // (the red box is not in the way of any of it); from beyond the nest: the shelf's side.
            var near = new[]
            {
                new Vector3(-6.9f, 0.6f, -4.8f), new Vector3(-6.85f, 1.7f, -4.85f), new Vector3(-7.25f, 0.4f, 2.2f),     // blocks and the marker along the left wall
                new Vector3(6.9f, 0.5f, -4.9f), new Vector3(7f, 0.45f, -1.4f),                                           // a block and the spool on the right
            };
            var far = new[]
            {
                new Vector3(1.8f, 5f, 15f), new Vector3(4f, 7.2f, 15f), new Vector3(-6.6f, 4.5f, 14.9f),                 // the arch round the exit, a block on the shelf
                new Vector3(-3.5f, 2.4f, 12f), new Vector3(-2f, 0.02f, 9.4f), L.StandSpot, L.ExitCentre,                 // the chart, the outline on the rug, the shoe prints, the exit's mark
            };
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, -3.6f), 0.1f), 20f);
            foreach (Vector3 piece in near)
            {
                BotRunner.Run(Game, LookAndClick(bot, piece), 10f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            BotRunner.Run(Game, bot.WalkTo(new Vector3(-2f, 0f, -1.4f), 0.3f), 20f);
            BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 2.4f), 0.1f), 20f);
            foreach (Vector3 piece in far)
            {
                BotRunner.Run(Game, LookAndClick(bot, piece), 10f);
                Assert.IsNull(Game.Grabber.Held, "a click at " + piece + " picked something up");
            }
            Assert.AreEqual(1.2f, level.Red.Scale);
            Assert.IsTrue(level.Red.Frozen);
        }

        // ==== Pictures of other moments than the solver's (explicit: -Filter "Level10Tests.Tour") ================

        // The level with a script of the test's choosing in place of its Solve.
        sealed class TourLevel : LevelDefinition
        {
            public readonly L Inner = new L();
            readonly Func<Bot, L, IEnumerator> script;

            public TourLevel(Func<Bot, L, IEnumerator> script)
            {
                this.script = script;
            }

            public override string Slug => Inner.Slug;
            public override string Title => Inner.Title;
            public override string Blurb => Inner.Blurb;
            public override string[] Hints => Inner.Hints;
            public override string Environment => Inner.Environment;
            public override int EnvironmentVisit => 1;
            public override float GroundY => Inner.GroundY;
            public override float KillY => Inner.KillY;
            public override void Build(LevelContext ctx) => Inner.Build(ctx);
            public override IEnumerator Solve(Bot bot) => script(bot, Inner);
        }

        // (`ui`: a screen of the screenshot tool to draw over the picture - "hud" shows what the level says.)
        static void Tour(string folder, float[] times, Func<Bot, L, IEnumerator> script, string ui = null)
        {
            if (ui != null) Toybox.UI.UiCapture.Request = ui;
            try
            {
                Shots.Run(new ShotRequest
                {
                    Level = 10, Definition = new TourLevel(script), Times = times, Overview = true,
                    OutputDirectory = "tools/out/shots/level10/" + folder,
                });
            }
            finally
            {
                if (ui != null) Toybox.UI.UiCapture.Reset();
            }
        }

        // Looking about: the start, the chart from the shoe prints, the room behind, the stairs from the shelf.
        static IEnumerator LookAbout(Bot bot, L level)
        {
            yield return bot.Wait(0.5f);
            yield return bot.LookAt(new Vector3(-2f, 2f, 12f));
            yield return bot.Wait(1f);        // 1.6: the chart from the start
            yield return bot.Grab(level.Red);
            yield return bot.LookAt(new Vector3(0f, 1f, 4f));
            yield return bot.Wait(1f);        // 3: the red box in the hand, the yellow one behind it
            yield return bot.WalkTo(new Vector3(-2f, 0f, -1.4f), 0.4f, 12f);
            yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
            yield return bot.LookAt(new Vector3(-2f, 1.9f, 12f));
            yield return bot.Wait(1f);        // about 6: held against the chart from the shoe prints
            yield return bot.DropAt(L.BigAim);
            yield return bot.Wait(1f);
            yield return bot.LookAt(new Vector3(0f, 0.5f, 0f));
            yield return bot.Wait(1.5f);      // about 9: looking back at the nest
            yield return bot.LookAt(new Vector3(6f, 1.5f, 6f));
            yield return bot.Wait(1.5f);      // the sun's side
            yield return bot.LookAt(new Vector3(-8f, 3f, 4f));
            yield return bot.Wait(1.5f);      // the papered wall
            yield return bot.LookAt(new Vector3(-2f, 3f, 12f));
            yield return bot.Wait(3f);
        }

        // What a failed try looks like: the blue box caught on the red one's edge; the red one let go from too far back.
        static IEnumerator Misses(Bot bot, L level)
        {
            yield return level.Carry(bot, level.Red, L.PickSpot, new Vector3(-2f, 0f, 5f));
            yield return bot.DropAt(L.BigAim);
            yield return bot.Wait(1.5f);      // 3.7: as big as it gets, short of the shelf
            // Picked up again from where one stands (it looks as big as it did) and let go from the shoe prints.
            yield return bot.Grab(level.Red);
            yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
            yield return bot.DropAt(L.BigAim);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
            yield return level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.StepAim);
            yield return bot.Until(() => L.AtRest(level.Yellow), 4f);
            yield return level.Carry(bot, level.Teal, L.PickSpot, L.StandSpot);
            yield return bot.LookAt(new Vector3(-2f, 2.4f, 12f));
            yield return bot.Wait(1f);
            yield return bot.DropAt(new Vector3(-2f, 2.4f, 12f));
            yield return bot.Wait(3f);
        }

        // The finished stairs from the shelf, and from the side.
        static IEnumerator Finished(Bot bot, L level)
        {
            yield return level.Carry(bot, level.Red, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.BigAim);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
            yield return level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.StepAim);
            yield return bot.Until(() => L.AtRest(level.Yellow), 4f);
            yield return level.Carry(bot, level.Teal, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.TopAim);
            yield return bot.Until(() => L.AtRest(level.Teal) && level.Teal.Center.y < 3.6f, 4f);
            yield return bot.WalkTo(new Vector3(2.5f, 0f, 7f), 0.2f, 12f);
            yield return bot.LookAt(new Vector3(-2f, 1.6f, 10.6f));
            yield return bot.Wait(2f);        // about 14.5: the stairs from the side
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red), c = L.BoundsOf(level.Teal);
            yield return bot.WalkTo(new Vector3(-2f, 0f, 7.4f), 0.2f, 12f);
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.min.z - 0.55f), 0.1f, 12f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.center.z), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, b.min.z + 0.4f), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.WalkTo(new Vector3(-2f, 0f, c.min.z - 0.5f), 0.1f, 3f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, c.center.z), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, 13f), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.WalkTo(new Vector3(-4.5f, 0f, 13.2f), 0.2f, 5f);
            yield return bot.LookAt(new Vector3(-1.6f, 1.5f, 10.5f));
            yield return bot.Wait(2f);        // the stairs from the shelf
            yield return bot.LookAt(L.ExitCentre);
            yield return bot.Wait(3f);
        }

        // Up on the red box with only two boxes out: a click at the step below (at 14 s) picks nothing up,
        // and the level says why. Then down again, and from the rug it is picked up.
        static IEnumerator Refused(Bot bot, L level)
        {
            yield return level.Carry(bot, level.Red, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.BigAim);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
            yield return level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.StepAim);
            yield return bot.Until(() => L.AtRest(level.Yellow), 4f);
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.min.z - 0.55f), 0.1f, 12f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.center.z), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, b.min.z + 0.4f), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.WalkTo(new Vector3(-2f, 0f, b.min.z + 0.12f), 0.05f, 3f);
            yield return bot.LookAt(level.Yellow);
            yield return bot.Until(() => bot.Game.LevelTicks >= TestHelpers.Ticks(14f), 10f);
            yield return bot.Click();
            yield return bot.Wait(4f);        // 15 and 16.5: the step under the crosshair, nothing in the hand, the line
            yield return bot.WalkTo(new Vector3(0.5f, 0f, 9.4f), 0.2f, 8f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.Grab(level.Yellow);
            yield return bot.Wait(3f);
        }

        // The third box carried up the stairs and put on the red one by hand (let go at 18 s): it comes out
        // small, and the level says why. A second and a half later it is still there (0.57: no crumb).
        static IEnumerator ByHand(Bot bot, L level)
        {
            yield return level.Carry(bot, level.Red, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.BigAim);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
            yield return level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.StepAim);
            yield return bot.Until(() => L.AtRest(level.Yellow), 4f);
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            yield return level.Carry(bot, level.Teal, new Vector3(0f, 0f, -0.9f), new Vector3(-2f, 0f, a.min.z - 1.4f));
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.min.z - 0.55f), 0.1f, 12f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, a.center.z), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(new Vector3(-2f, 0f, b.min.z + 0.35f), 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.LookAt(new Vector3(-2f, b.max.y, L.ShelfZ - 0.5f));
            yield return bot.Until(() => bot.Game.LevelTicks >= TestHelpers.Ticks(18f), 10f);
            yield return bot.Click();
            yield return bot.Wait(4f);
        }

        // The blue box picked up from too near (1.9 before the nest) and held in its outline from the shoe
        // prints (let go at 14 s): as big as it gets short of the shelf, it comes down on the front of the
        // red box, and the level says what to do.
        static IEnumerator NearTop(Bot bot, L level)
        {
            yield return level.Carry(bot, level.Red, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.BigAim);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
            yield return level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot);
            yield return bot.DropAt(L.StepAim);
            yield return bot.Until(() => L.AtRest(level.Yellow), 4f);
            yield return level.Carry(bot, level.Teal, new Vector3(0f, 0f, -1.9f), L.StandSpot);
            yield return bot.LookAt(L.TopAim);
            yield return bot.Until(() => bot.Game.LevelTicks >= TestHelpers.Ticks(14f), 10f);
            yield return bot.Click();
            yield return bot.Wait(4f);
        }

        [Test, Explicit]
        public void Tour_Pictures()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("no graphics device");
            Tour("look", new[] { 1.4f, 2.9f, 6f, 8.9f, 10.4f, 11.9f }, LookAbout);
            Tour("misses", new[] { 3.7f, 16.3f, 18.3f }, Misses);
            Tour("finished", new[] { 15.4f, 21f, 23f }, Finished);
            Tour("refused", new[] { 13.5f, 15f, 16.5f, 20.5f }, Refused, "hud");
            Tour("byhand", new[] { 17.5f, 18.5f, 20f }, ByHand, "hud");
            Tour("neartop", new[] { 13.5f, 14.3f, 16f }, NearTop, "hud");
        }

        // ==== Measurements (explicit: run with -Filter "Level10Tests.Probe") ====================================
        // (A name filter runs explicit tests too. The checks alone: -Filter "Level10Tests\.(?!Probe_|Tour_)".)

        // What the two solvers make of the boxes, and when.
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Solve()
        {
            var text = new StringBuilder();
            for (int which = 0; which < 2; which++)
            {
                Load();
                List<string> heard = Listen();
                var ratio = new Dictionary<Prop, float>();
                var timeline = new StringBuilder();
                Game.Events.PropGrabbed += e => timeline.Append(" pick-up " + e.Prop.Name + " " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + ";");
                Game.Events.PlayerJumped += e => timeline.Append(" hop " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + ";");
                Game.Events.PropDropped += e =>
                {
                    ratio[e.Prop] = e.OldScale / e.GrabDistance;
                    timeline.Append(" let go " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + ";");
                };
                if (which == 0) TestHelpers.PlayLevel(Game, 120f);
                else
                {
                    BotRunner.Run(Game, level.SolveAsSpecified(bot), 120f);
                    TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
                }
                text.AppendLine((which == 0 ? "Solve" : "SolveAsSpecified") + ": completed " + Game.LevelCompleted + " in " + (Game.LevelTicks * Sim.Dt).ToString("0.00") + " s (" + Game.LevelTicks + " ticks)");
                foreach (Prop box in level.Boxes) text.AppendLine("  k " + ratio[box].ToString("0.000") + "  " + Describe(box));
                text.AppendLine("  said [" + string.Join(" | ", heard) + "]");
                text.AppendLine("  when:" + timeline);
            }
            WriteNote("level10-probe-solve.txt", text);
        }

        static void WriteNote(string name, StringBuilder text)
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../tools/out/notes/" + name));
            System.IO.File.WriteAllText(path, text.ToString());
            Debug.Log("[Level10] wrote " + path);
        }

        string Row(Prop box, float dropped, List<string> said)
        {
            Bounds b = L.BoundsOf(box);
            return "let go at " + dropped.ToString("0.000") + ", rests " + box.Scale.ToString("0.000") + " centre " + F(box.Center) + " y " + F(b.min.y) + ".." + F(b.max.y) +
                   " z " + F(b.min.z) + ".." + F(b.max.z) + " up " + box.Transform.up.y.ToString("0.000") + " yaw " + Mathf.DeltaAngle(0f, box.Transform.eulerAngles.y).ToString("0.0") +
                   (said.Count > 0 ? " SAID " + string.Join(" | ", said) : "");
        }

        // One placement of a box with what was placed before it; a row of the table.
        string Measure(Prop box, Vector3 pickFrom, Vector3 stand, Vector3 aim)
        {
            List<string> said = Listen();
            float dropped = 0f, ratio = 0f;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                ratio = e.OldScale / e.GrabDistance;
            };
            Drive(Place(box, pickFrom, stand, aim), 30f);
            TestHelpers.RunSeconds(Game, 2.5f);
            return "k " + ratio.ToString("0.000") + ", pitch " + Game.Player.Pitch.ToString("0.0") + ": " + Row(box, dropped, said);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Big()
        {
            var text = new StringBuilder();
            text.AppendLine("Red box picked up from the start, let go from (-2, 0, z) aimed at the foot of the shelf " + F(FootAim));
            foreach (float z in new[] { 3f, 4f, 5f, 5.5f, 6f, 6.3f, 6.7f, 7f, 7.3f, 7.6f, 8f, 8.5f, 9f })
            {
                Load();
                text.AppendLine("stand z " + z.ToString("0.0") + ": " + Measure(level.Red, L.PickSpot, StandAt(z), FootAim));
            }
            text.AppendLine();
            text.AppendLine("From the shoe prints, aimed at (-2, y, 12) on the shelf's face");
            foreach (float y in new[] { -1f, -0.5f, 0f, 0.3f, 0.6f, 0.95f, 1.3f, 1.55f, 1.9f, 2.3f, 2.8f, 3.4f, 4.2f })
            {
                Load();
                text.AppendLine("aim y " + y.ToString("0.00") + ": " + Measure(level.Red, L.PickSpot, L.StandSpot, new Vector3(L.StairX, y, L.ShelfZ)));
            }
            text.AppendLine();
            text.AppendLine("Picked up from (0, 0, z), let go from the shoe prints at the foot of the shelf");
            foreach (float z in new[] { -1.3f, -1.6f, -2f, -2.3f, -2.6f, -3f, -3.5f, -4f, -5f })
            {
                Load();
                text.AppendLine("pick-up z " + z.ToString("0.0") + ": " + Measure(level.Red, PickFrom(z), L.StandSpot, FootAim));
            }
            WriteNote("level10-probe-big.txt", text);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Step()
        {
            var text = new StringBuilder();
            text.AppendLine("Red box as the solver places it; yellow picked up from the start, let go from the shoe prints aimed through (-2, 0.5, z)");
            foreach (float z in new[] { 7.6f, 8f, 8.4f, 8.8f, 9.1f, 9.4f, 9.7f, 10f, 10.4f, 11f, 12f })
            {
                Load();
                Big();
                text.AppendLine("aim z " + z.ToString("0.0") + ": " + Measure(level.Yellow, L.PickSpot, L.StandSpot, new Vector3(L.StairX, 0.5f, z)));
            }
            text.AppendLine();
            text.AppendLine("Yellow picked up from (0, 0, z), let go from the shoe prints at the solver's aim " + F(L.StepAim));
            foreach (float z in new[] { -1.1f, -1.4f, -1.8f, -2.3f, -2.6f, -3f, -3.5f, -4f, -5f, -5.5f })
            {
                Load();
                Big();
                text.AppendLine("pick-up z " + z.ToString("0.0") + ": " + Measure(level.Yellow, PickFrom(z), L.StandSpot, L.StepAim));
            }
            WriteNote("level10-probe-step.txt", text);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Top()
        {
            var text = new StringBuilder();
            foreach (float standZ in new[] { 6.7f, 8f })
            {
                text.AppendLine("Red and yellow as the solver places them; blue picked up from the start, let go from (-2, 0, " + standZ.ToString("0.0") + ") aimed at (-2, y, 12) on the shelf's face");
                foreach (float y in new[] { 1.6f, 2f, 2.2f, 2.4f, 2.6f, 2.8f, 3f, 3.2f, 3.4f, 3.6f, 3.9f, 4.2f, 4.5f, 4.8f, 5.2f })
                {
                    Load();
                    Big();
                    Step();
                    text.AppendLine("aim y " + y.ToString("0.0") + ": " + Measure(level.Teal, L.PickSpot, StandAt(standZ), new Vector3(L.StairX, y, L.ShelfZ)));
                }
                text.AppendLine();
            }
            text.AppendLine("Blue picked up from (0, 0, z), let go from the shoe prints at the solver's aim " + F(L.TopAim));
            foreach (float z in new[] { -0.9f, -1.3f, -1.9f, -2.3f, -2.6f, -3f, -3.5f, -4f, -5f })
            {
                Load();
                Big();
                Step();
                text.AppendLine("pick-up z " + z.ToString("0.0") + ": " + Measure(level.Teal, PickFrom(z), L.StandSpot, L.TopAim));
            }
            WriteNote("level10-probe-top.txt", text);
        }

        // Takes the three boxes out of each other (three pick-ups at the nest), then stands them as a staircase
        // of the given sizes: the step on the rug, the big one against the shelf, the top one on it at the back.
        void Staircase(float step, float big, float top, float stepGap = 0.05f, float shelfGap = 0.01f)
        {
            IEnumerator Unwrap()
            {
                yield return bot.Grab(level.Red);
                yield return bot.DropAt(new Vector3(5f, 0.6f, 2f));
                yield return bot.Grab(level.Yellow);
                yield return bot.DropAt(new Vector3(5f, 0.6f, -2f));
                yield return bot.Grab(level.Teal);
                yield return bot.DropAt(new Vector3(-5f, 0.6f, -2f));
            }
            BotRunner.Run(Game, Unwrap(), 30f);
            const float x = L.StairX;
            float face = L.ShelfZ - shelfGap;
            level.Red.SetScale(big);
            level.Red.SetPose(new Vector3(x, big * 0.5f, face - big * 0.5f), Quaternion.identity);
            level.Yellow.SetScale(step);
            level.Yellow.SetPose(new Vector3(x, step * 0.5f, face - big - stepGap - step * 0.5f), Quaternion.identity);
            level.Teal.SetScale(top);
            level.Teal.SetPose(new Vector3(x, big + top * 0.5f + 0.002f, face - top * 0.5f), Quaternion.identity);
            TestHelpers.RunSeconds(Game, 0.5f);
        }

        // What a hop gets the player up: stairs of exact sizes (set up by the fixture), climbed by the solver's Climb.
        // (Since the third review the step and the top box are 1.15 at most, whatever a row asks for: the rows
        // "step 1.20" to "1.50" and "top 1.25" to "1.50" all measure 1.15 now.)
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_Climb()
        {
            var text = new StringBuilder();
            text.AppendLine("Staircases of exact sizes (step on the rug, big box against the shelf, top box on it at the back), climbed by the solver's Climb");
            var cases = new List<Vector3>();
            foreach (float a in new[] { 0.4f, 0.5f, 0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 1f, 1.15f, 1.2f, 1.25f, 1.3f, 1.35f, 1.4f, 1.45f, 1.5f }) cases.Add(new Vector3(a, 1.9f, 1f));
            foreach (float c in new[] { 0.5f, 0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.85f, 1.15f, 1.25f, 1.3f, 1.35f, 1.4f, 1.5f }) cases.Add(new Vector3(0.95f, 1.9f, c));
            foreach (float b in new[] { 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 2f }) cases.Add(new Vector3(0.9f, b, 1.1f));
            foreach (Vector3 sizes in cases)
            {
                Load();
                List<string> said = Listen();
                Staircase(sizes.x, sizes.y, sizes.z);
                float highest = 0f;
                Game.Context.OnUpdate(dt =>
                {
                    if (Game.Player.Grounded) highest = Mathf.Max(highest, Game.Player.Position.y);
                });
                bool completed = Climb();
                text.AppendLine("step " + F(sizes.x) + " big " + F(sizes.y) + " top " + F(sizes.z) + ": rises " + F(sizes.x) + " / " + F(sizes.y - sizes.x) + " / " + F(sizes.z) + " / " +
                                F(L.ShelfTop - sizes.y - sizes.z) + " -> " + (completed ? "COMPLETED" : "stuck, highest stand " + F(highest)) + (said.Count > 0 ? " SAID " + string.Join(" | ", said) : ""));
            }
            WriteNote("level10-probe-climb.txt", text);
        }

        // ==== The review's attacks, kept as checks ================================================================

        // Puts the red box on the rug at (x, z) the way a player does: picked up from the start, let go from
        // `from` before the spot, eyes on the rug there.
        IEnumerator RedOnTheRug(float x, float z, float from)
        {
            yield return bot.Grab(level.Red);
            yield return bot.WalkTo(new Vector3(x, 0f, -2.2f), 0.2f, 12f);
            yield return bot.WalkTo(new Vector3(x, 0f, z - from), 0.1f, 12f);
            yield return bot.DropAt(new Vector3(x, 0f, z));
            yield return bot.Wait(0.2f);
            yield return bot.Until(() => L.AtRest(level.Red), 4f);
        }

        // A hop from one place onto another, `height` up: to the take-off, eyes on the landing, hop, forward.
        IEnumerator HopTo(Vector3 from, Vector3 onto, float height)
        {
            yield return bot.WalkTo(from, 0.1f, 6f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            Vector3 along = onto - from;
            along.y = 0f;
            yield return bot.LookAt(new Vector3(onto.x, height, onto.z) + along.normalized * 0.6f, 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(onto, 0.15f, 3f);
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > height - 0.15f, 2f);
        }

        // One jump straight up with the eyes on the feet: a click some ticks after the feet leave the box
        // (in the air the engine no longer refuses the prop that was under them), a second click some ticks
        // after that (at 18 the jump is at its top).
        IEnumerator JumpClickClick(int grabTick, int dropTick)
        {
            Vector3 p = bot.Player.Position;
            yield return bot.LookAt(new Vector3(p.x, p.y - 4f, p.z + 0.02f), 2f);
            yield return bot.Jump(false);
            if (grabTick > 0) yield return bot.Wait(grabTick * Sim.Dt);
            yield return bot.Click();
            if (dropTick > 0) yield return bot.Wait(dropTick * Sim.Dt);
            yield return bot.Click();
            yield return bot.Wait(0.1f);
            yield return bot.Until(() => bot.Player.Grounded, 3f);
            yield return bot.Wait(0.3f);
        }

        // Jump, click, click. Before the review the box one had jumped off came back under the feet bigger
        // (the eye is higher when it is let go than when it was picked up), its owner riding it up: any box
        // was 2.0 after four jumps. The level keeps the box under the feet out of reach now.
        [TestCase(0, 18)]
        [TestCase(1, 12)]
        [TestCase(3, 10)]
        public void TheBoxUnderTheFeet_IsNotPickedUpInAJump(int grabTick, int dropTick)
        {
            Load();
            BotRunner.Run(Game, RedOnTheRug(3f, 7.6f, 3f), 30f);
            float size = level.Red.Scale;
            Assert.AreEqual(1f, size, 0.1f);
            Bounds b = L.BoundsOf(level.Red);
            IEnumerator Onto()
            {
                yield return HopTo(new Vector3(3f, 0f, b.min.z - 0.55f), new Vector3(3f, b.max.y, b.center.z), b.max.y);
                yield return bot.WalkTo(new Vector3(3f, b.max.y, b.center.z), 0.08f, 3f);
                yield return bot.Wait(0.2f);
            }
            BotRunner.Run(Game, Onto(), 20f);
            Assert.AreSame(level.Red, Game.Player.GroundProp, "the player stands on the red box");
            Assert.AreSame(level.Red, level.StoodOn);
            Assert.IsFalse(level.Red.Grabbable);

            List<string> said = Listen();
            int pickUps = 0;
            Game.Events.PropGrabbed += e => pickUps++;
            for (int i = 0; i < 4; i++)
            {
                BotRunner.Run(Game, JumpClickClick(grabTick, dropTick), 20f);
                Assert.IsNull(Game.Grabber.Held, "jump " + i);
                Assert.AreEqual(size, level.Red.Scale, "the box is as big as it was (jump " + i + ")");
                Assert.AreSame(level.Red, Game.Player.GroundProp, "and the player is back on it (jump " + i + ")");
                Assert.AreEqual(size, Game.Player.Position.y, 0.02f);
            }
            Assert.AreEqual(0, pickUps);
            CollectionAssert.Contains(said, L.FeetLine, "a click at the box under the feet is told why nothing happens");
            foreach (string line in said) Assert.AreEqual(L.FeetLine, line);
            Assert.LessOrEqual(said.Count, 2, "now and then, not at every click");

            // The jump and the click in one and the same tick, and a click in the tick of every landing: the
            // box is out of reach from the moment the feet come down on it. (Raw input: the bot has no
            // command that presses two buttons at once.)
            Game.Input = Input = new ScriptedInput();
            for (int i = 0; i < 150; i++)
            {
                if (Game.Player.Grounded)
                {
                    Input.Once.Jump = true;
                    Input.Once.GrabPressed = true;
                }
                else if (i % 7 == 0) Input.Once.GrabPressed = true;
                Game.Tick();
                Assert.IsNull(Game.Grabber.Held, "tick " + i);
            }
            Assert.AreEqual(size, level.Red.Scale);
            Assert.AreEqual(0, pickUps);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Assert.AreSame(level.Red, Game.Player.GroundProp);

            // Off it, it is a box like any other again - also when the click comes in mid-air on the way down.
            bot = new Bot(Game);
            IEnumerator Off()
            {
                yield return bot.WalkTo(new Vector3(3f, 0f, b.min.z - 1.6f), 0.2f, 6f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Off(), 15f);
            Assert.IsNull(level.StoodOn);
            Assert.IsTrue(level.Red.Grabbable);
            BotRunner.Run(Game, bot.Grab(level.Red), 5f);
            Assert.IsTrue(level.Red.Held);
        }

        // Jumping on the spot with the crosshair kept on a box and a click every other tick: at no moment
        // of a jump is anything picked up. (Raw input and the fixture's own aim: a hand on a mouse follows
        // the box as the eye goes up; the bot's aim takes a few ticks.)
        void JumpAndClickAt(Prop box, int ticks)
        {
            Game.Input = Input = new ScriptedInput();
            for (int i = 0; i < ticks; i++)
            {
                LookAt(box.Center);
                if (Game.Player.Grounded) Input.Once.Jump = true;
                if (i % 2 == 0) Input.Once.GrabPressed = true;
                Game.Tick();
                Assert.IsNull(Game.Grabber.Held, "tick " + i);
            }
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            bot = new Bot(Game);
        }

        // Two boxes, and the step pulled up after oneself. Measured before the second review's fix
        // (tools/out/notes/level10-review-probe-pullup.txt and level10-review2-probe-jumppull.txt): from the
        // red box's top the yellow one was picked up again and let go on the red box. With both feet down
        // it came out 0.56 to 0.66 behind a red box of 2.0; in a jump with the eyes on one's feet 0.72 to
        // 0.96, also with the stairs exactly as their outlines show them - and the shelf was a hop away:
        // done with two boxes, the blue one never touched. Up on a box nothing is picked up now.
        [TestCase(-2.6f, false, TestName = "FromUpOnTheBigBox_TheStepIsNotPulledUp_WithTheStairsAsOutlined")]
        [TestCase(-1.8f, true, TestName = "FromUpOnTheBigBox_TheStepIsNotPulledUp_WithTheBigBoxAtTheClampAndABiggerStep")]
        [TestCase(-1.4f, true, TestName = "FromUpOnTheBigBox_TheStepIsNotPulledUp_WithTheBigBoxAtTheClampAndTheBiggestStep")]
        public void FromUpOnTheBigBox_TheStepIsNotPulledUp(float stepPickZ, bool bigAtTheClamp)
        {
            Load();
            Big(6.7f, bigAtTheClamp ? new Vector3(L.StairX, 3.4f, L.ShelfZ) : L.BigAim);
            Step(stepPickZ);
            List<string> said = Listen();
            int pickUps = 0;
            Game.Events.PropGrabbed += e => pickUps++;
            float highest = 0f;
            Game.Context.OnUpdate(dt =>
            {
                if (Game.Player.Grounded) highest = Mathf.Max(highest, Game.Player.Position.y);
            });
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            float step = level.Yellow.Scale;
            Vector3 stepAt = level.Yellow.Center;
            IEnumerator Up()
            {
                yield return bot.WalkTo(new Vector3(a.center.x, 0f, a.min.z - 1.4f), 0.3f, 12f);
                yield return HopTo(new Vector3(a.center.x, 0f, a.min.z - 0.55f), new Vector3(a.center.x, a.max.y, a.center.z), a.max.y);
                yield return HopTo(new Vector3(a.center.x, a.max.y, Mathf.Min(b.min.z - 0.4f, a.max.z - 0.1f)), new Vector3(b.center.x, b.max.y, b.min.z + 0.25f), b.max.y);
                yield return bot.WalkTo(new Vector3(b.center.x, b.max.y, b.min.z + 0.12f), 0.05f, 3f);
                yield return bot.LookAt(level.Yellow);
                yield return bot.Click();
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Up(), 40f);
            string where = "player at " + F(Game.Player.Position) + "; " + Describe(level.Red) + "; " + Describe(level.Yellow);
            Assert.AreSame(level.Red, level.StoodOn, "the player is up on the red box: " + where);
            Assert.IsTrue(Game.PhysicsScene.Raycast(Game.Player.Eye, Game.Player.Forward, out RaycastHit seen, 50f, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                          PropRef.Of(seen.collider) == level.Yellow, "with the crosshair on the step: " + where);
            Assert.IsNull(Game.Grabber.Held, "from up there the step is not picked up");
            Assert.IsFalse(level.Yellow.Grabbable);
            Assert.IsFalse(level.Red.Grabbable);
            CollectionAssert.AreEqual(new[] { L.FeetLine }, said, "and the click is told why");

            // Not in a jump either, at any moment of it.
            JumpAndClickAt(level.Yellow, 180);
            Assert.AreEqual(0, pickUps);
            Assert.AreEqual(step, level.Yellow.Scale, "the step is as big as it was");
            Assert.Less((level.Yellow.Center - stepAt).magnitude, 0.01f, "and stands where it stood");
            Assert.AreSame(level.Red, Game.Player.GroundProp);
            foreach (string line in said) Assert.AreEqual(L.FeetLine, line);
            Assert.LessOrEqual(said.Count, 2, "now and then, not at every click");

            // So two boxes get nobody higher than the top of the big one.
            IEnumerator AtTheShelf()
            {
                yield return bot.WalkTo(new Vector3(b.center.x, b.max.y, b.max.z - 0.4f), 0.1f, 5f);
                for (int i = 0; i < 4; i++)
                {
                    yield return bot.Jump(false);
                    IEnumerator walk = bot.WalkTo(new Vector3(b.center.x, 0f, L.ShelfZ + 1f), 0.2f, 1.2f);
                    for (int t = 0; t < 60 && walk.MoveNext(); t++) yield return walk.Current;
                }
            }
            Drive(AtTheShelf(), 20f);
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            Assert.IsFalse(Game.LevelCompleted, "two boxes are no way up");
            Assert.AreEqual(b.max.y, highest, 0.05f, "nobody got higher than the top of the red box");
            Assert.IsTrue(level.Teal.Frozen, "the blue box was never touched");

            // Down on the rug a box is a box again.
            IEnumerator Down()
            {
                yield return bot.WalkTo(new Vector3(0.5f, 0f, 9.4f), 0.2f, 8f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Down(), 15f);
            Assert.IsNull(level.StoodOn);
            Assert.IsTrue(level.Yellow.Grabbable && level.Red.Grabbable);
            BotRunner.Run(Game, bot.Grab(level.Yellow), 5f);
            Assert.IsTrue(level.Yellow.Held);
        }

        // A box in the hand may be carried up the stairs and let go up there: a top box put on by hand.
        // From up there it is not picked up again - not at once, and not in a jump (jump, click, click
        // would grow it beside one's feet as it grew under them). From the rug it is.
        [Test]
        public void ABoxCarriedUpAndLetGoUpThere_IsNotPickedUpAgainFromUpThere()
        {
            Load();
            Big();
            Step();
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            IEnumerator CarryUp()
            {
                yield return level.Carry(bot, level.Teal, PickFrom(-1.3f), new Vector3(a.center.x, 0f, a.min.z - 1.4f));
                yield return HopTo(new Vector3(a.center.x, 0f, a.min.z - 0.55f), new Vector3(a.center.x, a.max.y, a.center.z), a.max.y);
                yield return HopTo(new Vector3(a.center.x, a.max.y, Mathf.Min(b.min.z - 0.4f, a.max.z - 0.1f)), new Vector3(b.center.x, b.max.y, b.min.z + 0.3f), b.max.y);
                yield return bot.LookAt(new Vector3(b.center.x, b.max.y + 0.3f, L.ShelfZ));
            }
            BotRunner.Run(Game, CarryUp(), 60f);
            Assert.IsTrue(level.Teal.Held, "the box is still in the hand up on the red one");
            Assert.AreSame(level.Red, level.StoodOn);
            // Let go, and a click again in the very next tick (raw input): the box stays where it was put.
            List<string> said = Listen();
            int pickUps = 0;
            Game.Events.PropGrabbed += e => pickUps++;
            Game.Input = Input = new ScriptedInput();
            Click();
            Assert.IsNull(Game.Grabber.Held, "let go against the shelf");
            Assert.IsFalse(level.Teal.Grabbable, "out of reach in the tick it is let go");
            // (Put on by hand from this close it is too small for the last hop: that is said, see
            // TheThirdBoxPutOnByHand_ComesOutSmall_AndTheLevelSaysWhy. Letting go is not refused.)
            CollectionAssert.AreEqual(new[] { L.CloseLine }, said);
            Click();
            Assert.IsNull(Game.Grabber.Held, "a click in the very next tick does not pick it up again");
            TestHelpers.RunUntil(Game, () => L.AtRest(level.Teal), 3f);
            RunSeconds(0.2f);
            CollectionAssert.AreEqual(new[] { L.CloseLine, L.FeetLine }, said, "and that click is told why");
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), Describe(level.Teal));
            float size = level.Teal.Scale;
            Assert.Greater(size, L.CrumbScale + 0.03f, "big enough to stay where it is");
            Assert.IsFalse(level.Teal.Grabbable, "let go up on a box, it is out of reach from up there");

            // Not in a jump either, at any moment of it.
            said.Clear();
            JumpAndClickAt(level.Teal, 150);
            Assert.AreEqual(0, pickUps);
            Assert.AreEqual(size, level.Teal.Scale, "the box is as big as it was");
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red));
            foreach (string line in said) Assert.AreEqual(L.FeetLine, line);
            Assert.LessOrEqual(said.Count, 2, "now and then, not at every click");

            // Down the stairs to the shoe prints: from the rug it is picked up like any other box.
            IEnumerator Down()
            {
                yield return bot.WalkTo(L.StandSpot, 0.2f, 8f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Down(), 15f);
            Assert.IsNull(level.StoodOn);
            Assert.IsTrue(level.Teal.Grabbable);
            BotRunner.Run(Game, bot.Grab(level.Teal), 5f);
            Assert.IsTrue(level.Teal.Held);
        }

        // The third box carried up the stairs and put on the big one by hand: what everybody tries first.
        // Let go from that close it is small - 0.30 to 0.57, picked up from anywhere between the nest and
        // the mat's rim, whatever it is held at (tools/out/notes/level10-review3-probe-byhand.txt, 24 of
        // 24) - and the shelf is more than a hop above it. Before the third review that got "too small to
        // use" a second and a half later, or nothing at all; now the level says why: at once if the box
        // stays up there, when it is sent back if it is a crumb.
        [TestCase(0.9f, 0, false, TestName = "TheThirdBoxPutOnByHand_ComesOutSmall_AndTheLevelSaysWhy")]
        [TestCase(2.6f, 0, true, TestName = "TheThirdBoxPutOnByHand_PickedUpFromTheMatsRim_IsACrumb_AndTheLevelSaysWhy")]
        [TestCase(1.3f, 2, true, TestName = "TheThirdBoxHeldInItsOutlineFromUpOnTheBigBox_IsACrumb_AndTheLevelSaysWhy")]
        public void TheThirdBoxPutOnByHand(float away, int aimAt, bool crumb)
        {
            Load();
            Big();
            Step();
            List<string> said = Listen();
            Bounds a = L.BoundsOf(level.Yellow), b = L.BoundsOf(level.Red);
            // The red box's top at the back, or the blue outline on the chart.
            Vector3 aim = aimAt == 0 ? new Vector3(L.StairX, b.max.y, L.ShelfZ - 0.5f) : new Vector3(L.StairX, L.TopAim.y, L.ShelfZ);
            IEnumerator ByHand()
            {
                yield return level.Carry(bot, level.Teal, PickFrom(-away), new Vector3(a.center.x, 0f, a.min.z - 1.4f));
                yield return HopTo(new Vector3(a.center.x, 0f, a.min.z - 0.55f), new Vector3(a.center.x, a.max.y, a.center.z), a.max.y);
                yield return HopTo(new Vector3(a.center.x, a.max.y, Mathf.Min(b.min.z - 0.4f, a.max.z - 0.1f)), new Vector3(b.center.x, b.max.y, b.min.z + 0.35f), b.max.y);
                yield return bot.DropAt(aim);
                yield return bot.Wait(0.2f);
            }
            BotRunner.Run(Game, ByHand(), 60f);
            Assert.AreSame(level.Red, level.StoodOn, "let go from up on the red box");
            float size = level.Teal.Scale;
            if (crumb)
            {
                Assert.Less(size, L.CrumbScale, Describe(level.Teal));
                Assert.IsEmpty(said, "a crumb is told when it is sent back");
                Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.CrumbLeashes[2].Returns == 1, 4f));
                RunSeconds(0.2f);
                CollectionAssert.AreEqual(new[] { L.CloseCrumbLine }, said);
                Assert.AreEqual(0.6f, level.Teal.Scale, 1e-4f, "back at the start, as big as it was");
                // From the start again, the solver's way.
                Top();
                Assert.IsEmpty(said.Skip(1), "and nothing more is said");
            }
            else
            {
                Assert.IsTrue(size >= L.CrumbScale && size < L.SmallTop, Describe(level.Teal));
                CollectionAssert.AreEqual(new[] { L.CloseLine }, said, "told at once");
                RunSeconds(2.5f);
                Assert.AreEqual(0, level.CrumbLeashes[2].Returns, "big enough to stay where it was put");
                Assert.IsTrue(L.StandsOn(level.Teal, level.Red), Describe(level.Teal));
                Assert.Greater(L.ShelfTop - L.BoundsOf(level.Teal).max.y, L.HopRise + 0.05f, "and the shelf is more than a hop above it");
                Assert.IsTrue(level.TopOutline.activeSelf, "the blue outline still says where a box is to be held");
                // Down on the rug it is picked up again from before the stairs (it shows over the red box's
                // edge from there) and held in its outline from the shoe prints.
                IEnumerator Again()
                {
                    yield return bot.WalkTo(new Vector3(L.StairX, 0f, a.min.z - 0.65f), 0.1f, 8f);
                    yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y < 0.05f, 3f);
                    yield return bot.Wait(0.1f);
                    yield return bot.Grab(level.Teal);
                    yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                    yield return bot.DropAt(L.TopAim);
                    yield return bot.Wait(0.2f);
                    yield return bot.Until(() => L.AtRest(level.Teal), 5f);
                }
                BotRunner.Run(Game, Again(), 40f);
                CollectionAssert.AreEqual(new[] { L.CloseLine }, said, "and nothing more is said");
            }
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), stairs);
            RunSeconds(0.2f);
            Assert.IsFalse(level.TopOutline.activeSelf, "a box that is the last step stands under the blue outline: it has done its work");
            Assert.IsTrue(Climb(), "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
        }

        // Wherever on the mat's rim a box is picked up from, and wherever on it the click lands, in the
        // hand it is square to the view: held toward the shelf from the shoe prints it stands square
        // against it, in its outline, at the size the solver makes it. (Before the second review a box kept
        // the turn it was picked up with: from a corner of the mat the stairs stood 45 degrees askew at
        // 1.79 / 0.79 / 1.03 - climbable, but nothing lined up with what is painted:
        // tools/out/notes/level10-review2-probe-turned-before.txt.)
        [TestCase(30f)]
        [TestCase(45f)]
        [TestCase(-45f)]
        [TestCase(135f)]
        public void PickedUpFromAnywhereOnTheRim_TheBoxesStandSquare_AndTheStairsWork(float angle)
        {
            Load();
            List<string> said = Listen();
            Vector3 Rim(float at) => new Vector3(Mathf.Sin(at * Mathf.Deg2Rad), 0f, -Mathf.Cos(at * Mathf.Deg2Rad)) * 2.6f;
            // (Round the mat's rim from where the way to the shelf meets it, and back the same way.)
            const float laneAngle = -55f;
            float turn = angle > laneAngle ? 25f : -25f;
            IEnumerator Fetch(Prop box, Vector3 aim)
            {
                if (bot.Player.Position.z > 1f) yield return bot.WalkTo(new Vector3(L.StairX, 0f, -1.4f), 0.4f, 12f);
                for (float at = laneAngle; turn > 0f ? at < angle : at > angle; at += turn) yield return bot.WalkTo(Rim(at), 0.3f, 12f);
                yield return bot.WalkTo(Rim(angle), 0.08f, 12f);
                yield return bot.Grab(box);
                for (float at = angle; turn > 0f ? at > laneAngle : at < laneAngle; at -= turn) yield return bot.WalkTo(Rim(at), 0.3f, 12f);
                yield return bot.WalkTo(new Vector3(L.StairX, 0f, -1.4f), 0.4f, 12f);
                yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                yield return bot.DropAt(aim);
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => L.AtRest(box), 5f);
                yield return bot.Wait(0.3f);
            }
            BotRunner.Run(Game, Fetch(level.Red, L.BigAim), 60f);
            BotRunner.Run(Game, Fetch(level.Yellow, L.StepAim), 60f);
            BotRunner.Run(Game, Fetch(level.Teal, L.TopAim), 60f);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            foreach (Prop box in level.Boxes)
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, box.Transform.eulerAngles.y * 4f)) / 4f, 1.5f, box.Name + " stands square to the shelf: " + stairs);
            Assert.AreEqual(1.91f, level.Red.Scale, 0.04f, stairs);
            Assert.AreEqual(0.97f, level.Yellow.Scale, 0.04f, stairs);
            Assert.AreEqual(1.07f, level.Teal.Scale, 0.04f, stairs);
            Assert.Greater(L.BoundsOf(level.Red).max.z, L.ShelfZ - 0.08f, "the red box stands against the shelf: " + stairs);
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), stairs);
            Assert.IsTrue(Climb(), "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
            Assert.IsEmpty(said, stairs);
        }

        // The solver's three placements with the standing place and the aim a little off, as a player's
        // are: (dx, dz) from the shoe prints, (ax, ay) from the aim (the yellow box's ay runs along the rug).
        [TestCase(0f, 0f, 0.3f, 0f)]
        [TestCase(0f, 0f, -0.3f, 0f)]
        [TestCase(0f, 0f, 0f, 0.3f)]
        [TestCase(0f, 0f, 0f, -0.3f)]
        [TestCase(0.4f, 0f, 0f, 0f)]
        [TestCase(-0.4f, 0f, 0f, 0f)]
        [TestCase(0f, 0.4f, 0f, 0f)]
        [TestCase(0f, -0.4f, 0f, 0f)]
        [TestCase(0.5f, 0.5f, 0.3f, 0f)]
        [TestCase(-0.5f, -0.5f, -0.3f, 0f)]
        [TestCase(0f, 0.4f, 0f, -0.3f)]
        [TestCase(1f, 0f, 0f, -0.3f)]
        [TestCase(-1f, 0f, 0f, 0.3f)]
        public void StoodAndAimedALittleOff_TheStairsStillWork(float dx, float dz, float ax, float ay)
        {
            Load();
            List<string> said = Listen();
            Vector3 standAt = L.StandSpot + new Vector3(dx, 0f, dz);
            BotRunner.Run(Game, Place(level.Red, L.PickSpot, standAt, L.BigAim + new Vector3(ax, ay, 0f)), 40f);
            BotRunner.Run(Game, Place(level.Yellow, L.PickSpot, standAt, L.StepAim + new Vector3(ax, 0f, ay)), 40f);
            BotRunner.Run(Game, Place(level.Teal, L.PickSpot, standAt, L.TopAim + new Vector3(ax, ay, 0f)), 40f);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; " + Describe(level.Teal);
            Assert.Greater(L.BoundsOf(level.Red).max.z, L.ShelfZ - L.ShortGap, "the red box stands against the shelf: " + stairs);
            Assert.IsTrue(L.StandsOn(level.Teal, level.Red), "the blue box stands on the red one: " + stairs);
            Assert.IsTrue(Climb(), "not climbed: " + stairs + " (player at " + F(Game.Player.Position) + ")");
            Assert.IsEmpty(said, stairs);
        }

        // Held toward the foot of the shelf too low, the red box comes down on the rug before it gets there:
        // smaller than its outline and short of the shelf. One line says so; held a little higher it is right.
        [TestCase(0.6f, 1.72f, 0.6f)]
        [TestCase(0.3f, 1.51f, 1.2f)]
        [TestCase(0f, 1.36f, 1.7f)]
        public void TheBigBoxHeldTooLow_ComesDownShortOfTheShelf_AndTheLevelSaysSo(float aimY, float scale, float gap)
        {
            Load();
            List<string> said = Listen();
            Big(6.7f, new Vector3(L.StairX, aimY, L.ShelfZ));
            Assert.AreEqual(scale, level.Red.Scale, 0.05f);
            Assert.AreEqual(gap, L.ShelfZ - L.BoundsOf(level.Red).max.z, 0.12f, Describe(level.Red));
            CollectionAssert.AreEqual(new[] { L.ShortLine }, said);

            // Picked up again from where it was picked up before (it looks as big as it did) and held in its outline.
            said.Clear();
            Bounds b = L.BoundsOf(level.Red);
            float away = level.Red.Scale / 0.434f;
            IEnumerator Again()
            {
                yield return bot.WalkTo(new Vector3(L.StairX, 0f, b.center.z - away), 0.1f, 12f);
                yield return bot.LookAt(level.Red);
                yield return bot.Grab(level.Red);
                yield return bot.WalkTo(L.StandSpot, 0.08f, 12f);
                yield return bot.DropAt(L.BigAim);
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => L.AtRest(level.Red), 5f);
            }
            BotRunner.Run(Game, Again(), 40f);
            Assert.Greater(L.BoundsOf(level.Red).max.z, L.ShelfZ - 0.1f, Describe(level.Red));
            Assert.AreEqual(1.91f, level.Red.Scale, 0.1f);
            Assert.IsEmpty(said);
        }

        // A box that stands short of the shelf on purpose is told nothing: the step on its footprint before
        // the big box is there, however big it is; and a box put down in the middle of the room.
        [TestCase(-2.6f)]
        [TestCase(-1.1f)]
        public void AStepOnItsFootprint_WithNoBigBoxYet_IsNotToldAnything(float pickZ)
        {
            Load();
            IEnumerator RedAside()
            {
                yield return bot.Grab(level.Red);
                yield return bot.DropAt(new Vector3(5f, 0.6f, 2f));
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, RedAside(), 20f);
            List<string> said = Listen();
            Step(pickZ);
            Assert.AreEqual(0f, L.BoundsOf(level.Yellow).min.y, 0.01f);
            Assert.Less(L.BoundsOf(level.Yellow).max.z, L.ShelfZ - 1f);
            Assert.IsEmpty(said, Describe(level.Yellow));
        }

        // Restarts at awkward moments: a box in the air, a box on the shelf in the two seconds before it is
        // sent back, the player on a box (which is out of reach just then), the player in mid-hop with a box
        // in the hand.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RestartingAtAnAwkwardMoment_ThenSolving_Works(int moment)
        {
            Load();
            TestHelpers.PlayLevel(Game, 120f);
            int fresh = Game.LevelTicks;

            Load();
            if (moment == 0)
            {
                BotRunner.Run(Game, level.Carry(bot, level.Red, L.PickSpot, L.StandSpot), 30f);
                BotRunner.Run(Game, bot.DropAt(new Vector3(L.StairX, 3.4f, L.ShelfZ)), 5f);
                RunSeconds(0.1f);
                Assert.Greater(level.Red.Velocity.magnitude, 0.5f, "the red box is falling");
            }
            else if (moment == 1)
            {
                // (Only a box that looks small gets over the shelf's edge before it is as big as it gets:
                // the blue one, picked up from a step behind the mat.)
                Big();
                Step();
                BotRunner.Run(Game, level.Carry(bot, level.Teal, PickFrom(-3.5f), L.StandSpot), 30f);
                BotRunner.Run(Game, bot.DropAt(new Vector3(L.StairX, 5.4f, L.ShelfZ)), 5f);
                RunSeconds(1.2f);
                Assert.IsTrue(level.ShelfLeash.OutOfBounds(level.Teal), "the blue box lies on the shelf (" + Describe(level.Teal) + ")");
                Assert.AreEqual(0, level.ShelfLeash.Returns);
            }
            else if (moment == 2)
            {
                BotRunner.Run(Game, RedOnTheRug(3f, 7.6f, 3f), 30f);
                Bounds b = L.BoundsOf(level.Red);
                BotRunner.Run(Game, HopTo(new Vector3(3f, 0f, b.min.z - 0.55f), new Vector3(3f, b.max.y, b.center.z), b.max.y), 20f);
                Assert.AreSame(level.Red, level.StoodOn);
                Assert.IsFalse(level.Red.Grabbable);
            }
            else
            {
                Big();
                BotRunner.Run(Game, level.Carry(bot, level.Yellow, L.PickSpot, L.StandSpot), 30f);
                BotRunner.Run(Game, bot.Jump(false), 2f);
                RunSeconds(0.15f);
                Assert.IsFalse(Game.Player.Grounded);
                Assert.IsTrue(level.Yellow.Held);
            }
            Game.RestartLevel();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsNull(level.StoodOn);
            Assert.IsTrue(level.Red.Grabbable && level.Red.Frozen);
            Assert.AreEqual(1, level.Nested.RevealedCount);
            bot = new Bot(Game);
            TestHelpers.PlayLevel(Game, 120f);
            Assert.AreEqual(fresh, Game.LevelTicks, "the solution plays as it does from a fresh start");
        }

        // ---- Two boxes are no way up (the third review) ------------------------------------------------------

        // With every box free to be 2.0, a red box of 1.25 to 1.35 against the shelf and the yellow one at
        // about 1.3 on it, to one side, were three hops to the shelf - each at the very limit of a hop, the
        // blue box never touched (4 of 13 size pairs: level10-review3-probe-twoboxes-before.txt). The two
        // boxes that come out of the red one grow no taller than a hop now: the best two boxes do is
        // 1.35 + 1.15, and the shelf is 1.40 above that.
        [TestCase(1.35f)]
        [TestCase(1.3f)]
        [TestCase(1.25f)]
        [TestCase(1.15f)]
        public void TwoBoxes_OneOnTheOther_AreNoWayUp(float big)
        {
            Load();
            // (The fixture stands them: the yellow one as big as it gets.)
            level.Yellow.SetScale(2f);
            Assert.AreEqual(L.InnerMaxScale, level.Yellow.MaxScale, 1e-4f);
            TwoBoxes(big, L.InnerMaxScale, 0.4f);
            Assert.AreEqual(L.InnerMaxScale, level.Yellow.Scale, 1e-3f, "the yellow box is as big as it gets");
            Assert.IsTrue(L.StandsOn(level.Yellow, level.Red), Describe(level.Yellow));
            bool completed = ClimbTwoBoxes(out _, out float highest);
            string stairs = Describe(level.Red) + "; " + Describe(level.Yellow) + "; highest stand " + F(highest);
            Assert.IsFalse(completed, "two boxes got the player to the exit: " + stairs);
            // (A box that is hopped at and about on rocks: a foot on its raised edge is a tenth or two above its top.)
            Assert.Less(highest, big + L.InnerMaxScale + 0.3f, "nobody got higher than the top of the second box: " + stairs);
            Assert.Greater(L.ShelfTop - (big + L.InnerMaxScale), 1.39f, "the shelf is more than any hop above it");
            Assert.IsTrue(level.Teal.Frozen, "the blue box was never touched");
        }

        // The other way with two: the yellow box carried up onto a red one of 1.35 and let go under the
        // feet at the top of a jump, where the eye is 1.3 higher than it will be. It came out 1.22 to
        // 1.23 and the shelf was a last hop of 1.32 away (3 of 72: level10-review3-probe-carryup-before.txt,
        // the first three here). It is 1.15 at most now.
        [TestCase(1f, 14)]
        [TestCase(1.1f, 18)]
        [TestCase(1.1f, 21)]
        [TestCase(0.9f, 18)]
        public void TheSecondBoxCarriedUpAndLetGoUnderTheFeetInAJump_IsNoWayUpEither(float away, int dropTick)
        {
            Load();
            var log = new StringBuilder();
            bool completed = CarryUpAndJump(1.35f, away, dropTick, log, out float highest, out string end);
            Assert.IsFalse(completed, "two boxes got the player to the exit: " + log);
            Assert.LessOrEqual(level.Yellow.Scale, L.InnerMaxScale + 1e-3f, log.ToString());
            Assert.Less(highest, 1.35f + L.InnerMaxScale + 0.3f, log + end);
            Assert.IsTrue(level.Teal.Frozen, "the blue box was never touched");
        }

        // ==== Measurements of the review (explicit, as the Probe tests above) ==================================
        // The other probes of the review are quoted in tools/out/notes/level10-review-probes.cs.txt; their
        // tables are tools/out/notes/level10-review-probe-*.txt.

        // Two boxes and no more: a big one against the shelf that a hop gets onto, another on it to one side.
        void TwoBoxes(float big, float top, float side)
        {
            IEnumerator Unwrap()
            {
                yield return bot.Grab(level.Red);
                yield return bot.DropAt(new Vector3(5f, 0.6f, 2f));
                yield return bot.Grab(level.Yellow);
                yield return bot.DropAt(new Vector3(5f, 0.6f, -2f));
            }
            BotRunner.Run(Game, Unwrap(), 30f);
            const float x = L.StairX;
            float face = L.ShelfZ - 0.01f;
            level.Red.SetScale(big);
            level.Red.SetPose(new Vector3(x, big * 0.5f, face - big * 0.5f), Quaternion.identity);
            level.Yellow.SetScale(top);
            level.Yellow.SetPose(new Vector3(x + side, big + top * 0.5f + 0.002f, face - top * 0.5f), Quaternion.identity);
            TestHelpers.RunSeconds(Game, 0.6f);
        }

        // The hops of two boxes, big against the shelf and the other on it to one side: rug -> big -> other ->
        // shelf with care, and if that does not do it, anybody's way (at each in turn, hopping all the time).
        bool ClimbTwoBoxes(out bool withCare, out float highest)
        {
            float top = 0f;
            Game.Context.OnUpdate(dt =>
            {
                if (Game.Player.Grounded) top = Mathf.Max(top, Game.Player.Position.y);
            });
            Bounds b = L.BoundsOf(level.Red), c = L.BoundsOf(level.Yellow);
            float freeX = c.min.x - 0.31f;
            IEnumerator Up()
            {
                yield return HopTo(new Vector3(freeX, 0f, b.min.z - 0.5f), new Vector3(freeX, b.max.y, b.center.z), b.max.y);
                yield return bot.Jump(false);
                yield return bot.WalkTo(new Vector3(c.center.x, c.max.y, c.center.z), 0.15f, 3f);
                yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > c.max.y - 0.15f, 2f);
                yield return HopTo(new Vector3(c.center.x, c.max.y, L.ShelfZ - 0.5f), new Vector3(c.center.x, L.ShelfTop, L.ShelfZ + 0.9f), L.ShelfTop);
                yield return bot.WalkTo(L.ExitCentre, 0.5f, 10f);
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
            Drive(Up(), 40f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            withCare = Game.LevelCompleted;
            if (!withCare)
            {
                IEnumerator Hops(Vector3 goal, float seconds)
                {
                    int ticks = TestHelpers.Ticks(seconds);
                    while (ticks > 0)
                    {
                        if (bot.Player.Grounded)
                        {
                            yield return bot.Jump();
                            ticks--;
                        }
                        IEnumerator walk = bot.WalkTo(goal, 0.2f, 2f);
                        int walked = 0;
                        for (; walked < 10 && ticks > 0 && walk.MoveNext(); walked++, ticks--) yield return walk.Current;
                        if (walked == 0)
                        {
                            yield return null;
                            ticks--;
                        }
                    }
                }
                IEnumerator Anyhow()
                {
                    yield return bot.WalkTo(new Vector3(freeX, 0f, b.min.z - 1.2f), 0.2f, 6f);
                    yield return Hops(new Vector3(freeX, 0f, b.center.z), 3f);
                    yield return Hops(new Vector3(c.center.x, 0f, c.center.z), 3f);
                    yield return Hops(new Vector3(c.center.x, 0f, L.ShelfZ + 1f), 4f);
                    yield return bot.WalkTo(L.ExitCentre, 0.5f, 10f);
                    yield return bot.Until(() => Game.LevelCompleted, 3f);
                }
                Drive(Anyhow(), 40f);
                TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            }
            TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f);
            highest = top;
            return Game.LevelCompleted;
        }

        // (Since the third review the yellow box is no bigger than 1.15, whatever is asked for here: the
        // table as it was before that is tools/out/notes/level10-review3-probe-twoboxes-before.txt.)
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_TwoBoxes()
        {
            var text = new StringBuilder();
            text.AppendLine("Two boxes only (the fixture stands them): big against the shelf, the other on it, shifted 0.4 to one side; hops rug -> big -> other -> shelf");
            foreach (Vector2 sizes in new[]
                     {
                         new Vector2(1.3f, 1.3f), new Vector2(1.35f, 1.25f), new Vector2(1.25f, 1.35f), new Vector2(1.3f, 1.25f), new Vector2(1.25f, 1.3f), new Vector2(1.35f, 1.35f),
                         new Vector2(1.2f, 1.35f), new Vector2(1.35f, 1.2f), new Vector2(1.25f, 1.25f), new Vector2(1.3f, 1.35f), new Vector2(1.35f, 1.3f), new Vector2(1.2f, 1.2f), new Vector2(1.15f, 1.15f),
                     })
            {
                Load();
                List<string> said = Listen();
                float big = sizes.x, top = sizes.y;
                TwoBoxes(big, top, 0.4f);
                top = level.Yellow.Scale;
                bool completed = ClimbTwoBoxes(out bool first, out float highest);
                text.AppendLine("big " + F(big) + " other " + F(top) + ": rises " + F(big) + " / " + F(top) + " / " + F(L.ShelfTop - big - top) + " -> " +
                                (completed ? (first ? "COMPLETED" : "COMPLETED (hopping about)") : "stuck, highest stand " + F(highest)) + "   " + Describe(level.Red) + "; " + Describe(level.Yellow) +
                                (said.Count > 0 ? " SAID " + string.Join(" | ", said.Distinct()) : ""));
            }
            WriteNote("level10-review-probe-twoboxes.txt", text);
        }

        // ==== The second review's measurements (explicit) ====================================================

        // Two boxes, nothing picked up from on a box: the red one against the shelf at a size a hop gets
        // onto (the fixture stands it there), the yellow one picked up `away` before the nest, carried up
        // onto it and let go under the feet at tick `dropTick` of a jump (18 is its top). True if that got
        // the player to the exit.
        bool CarryUpAndJump(float big, float away, int dropTick, StringBuilder log, out float highest, out string end)
        {
            float top = 0f;
            IEnumerator Unwrap()
            {
                yield return bot.Grab(level.Red);
                yield return bot.DropAt(new Vector3(5f, 0.6f, 2f));
            }
            BotRunner.Run(Game, Unwrap(), 30f);
            level.Red.SetScale(big);
            level.Red.SetPose(new Vector3(L.StairX, big * 0.5f, L.ShelfZ - 0.01f - big * 0.5f), Quaternion.identity);
            TestHelpers.RunSeconds(Game, 0.6f);
            Game.Context.OnUpdate(dt =>
            {
                if (Game.Player.Grounded) top = Mathf.Max(top, Game.Player.Position.y);
            });
            IEnumerator Route()
            {
                Bounds b = L.BoundsOf(level.Red);
                yield return bot.WalkTo(PickFrom(-away), 0.08f, 12f);
                yield return bot.LookAt(level.Yellow);
                yield return bot.Grab(level.Yellow);
                log.Append("k " + Game.Grabber.Ratio.ToString("0.000") + "; ");
                yield return bot.WalkTo(new Vector3(L.StairX, 0f, -1.4f), 0.4f, 12f);
                yield return bot.WalkTo(new Vector3(L.StairX, 0f, b.min.z - 1.4f), 0.3f, 12f);
                yield return HopTo(new Vector3(L.StairX, 0f, b.min.z - 0.55f), new Vector3(L.StairX, b.max.y, b.center.z), b.max.y);
                yield return bot.WalkTo(new Vector3(L.StairX, b.max.y, b.center.z), 0.08f, 3f);
                Vector3 p = bot.Player.Position;
                yield return bot.LookAt(new Vector3(p.x, p.y - 4f, p.z + 0.02f), 2f);
                yield return bot.Jump(false);
                yield return bot.Wait(dropTick * Sim.Dt);
                log.Append("feet " + F(bot.Player.Position.y) + " at the click; ");
                yield return bot.Click();
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => bot.Player.Grounded && L.AtRest(level.Yellow), 4f);
                yield return bot.Wait(0.3f);
                Bounds c = L.BoundsOf(level.Yellow);
                log.Append("yellow " + F(level.Yellow.Scale) + " y " + F(c.min.y) + ".." + F(c.max.y) + ", feet at " + F(bot.Player.Position.y) + "; ");
                Vector3 q = bot.Player.Position;
                yield return HopTo(new Vector3(q.x, q.y, Mathf.Max(c.min.z + 0.17f, L.ShelfZ - 0.5f)), new Vector3(q.x, L.ShelfTop, L.ShelfZ + 0.9f), L.ShelfTop);
                yield return bot.WalkTo(L.ExitCentre, 0.5f, 10f);
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
            end = "";
            try
            {
                BotRunner.Run(Game, Route(), 120f);
            }
            catch (Exception e)
            {
                end = " FAILED " + e.Message;
            }
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 1f);
            highest = top;
            return Game.LevelCompleted;
        }

        // (Since the third review the yellow box is no bigger than 1.15: the table as it was before that,
        // with three ways through, is tools/out/notes/level10-review3-probe-carryup-before.txt.)
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_CarryUp()
        {
            var text = new StringBuilder();
            text.AppendLine("Red against the shelf at `big` (fixture); yellow picked up from `away` in front of the nest, carried onto the red box, let go under the feet at tick `tick` of a jump (18 is its top)");
            foreach (float big in new[] { 1.25f, 1.3f, 1.35f })
                foreach (float away in new[] { 0.9f, 1f, 1.1f, 1.2f, 1.4f, 1.7f, 2f, 2.6f })
                    foreach (int dropTick in new[] { 14, 18, 21 })
                    {
                        Load();
                        List<string> said = Listen();
                        var log = new StringBuilder();
                        bool completed = CarryUpAndJump(big, away, dropTick, log, out float highest, out string end);
                        text.AppendLine("big " + F(big) + " away " + away.ToString("0.0") + " tick " + dropTick + ": " + log + (completed ? "-> COMPLETED with two boxes" : "-> not completed, highest stand " + F(highest)) + end +
                                        (said.Count > 0 ? " SAID " + string.Join(" | ", said.Distinct()) : ""));
                    }
            WriteNote("level10-review2-probe-carryup.txt", text);
        }
    }

    /// <summary>
    /// The pause card shows a hint in a panel of fixed size, and a label shrinks its type to fit. Level 10's
    /// three hints are set at the full body size and none is cut off - what Phase2CampaignTests asks of
    /// levels 5 to 9, with the real runner, the game's own menus and a store in memory.
    /// </summary>
    public class Level10HintTests
    {
        GameObject host;
        Toybox.Platform.GameRunner runner;
        FakeDevices devices;

        [SetUp]
        public void NewStores()
        {
            Toybox.Platform.Settings.Use(new Toybox.Platform.MemoryStore());
            Toybox.UI.UiCapture.Request = "";
        }

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
            Toybox.UI.UiCapture.Reset();
            Toybox.Platform.Settings.Use(null);
        }

        void Frames(int count = 1)
        {
            for (int i = 0; i < count; i++) runner.Frame(Sim.Dt);
        }

        [Test]
        public void EveryHint_FitsThePauseCardsPanel_AtFullSize()
        {
            host = new GameObject("Level 10 Hint Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<Toybox.Platform.GameRunner>();
            devices = new FakeDevices();
            runner.Begin(Toybox.Platform.LaunchOptions.FromUrl("?level=10"),
                new Toybox.Platform.RunnerOptions { Devices = devices, Store = new Toybox.Platform.MemoryStore(), Presenters = UiTestKit.Own() });
            Toybox.UI.MenuPresenter menu = runner.Presentation.Get<Toybox.UI.MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            Frames(30);
            devices.State.EscapePressed = true;
            Frames();
            devices.State.EscapePressed = false;
            Toybox.UI.PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            string[] hints = runner.Game.Level.Hints;
            Assert.AreEqual(10, runner.Game.Level.Id);
            float room = pause.HintText.rectTransform.rect.height;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                Frames();
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                int lineCount = pause.HintText.textInfo.lineCount;
                Debug.Log("[Level10] hint " + (h + 1) + ": " + hints[h].Length + " characters, " + lineCount + " lines at size " + size.ToString("0.0") +
                          " (" + pause.HintText.preferredHeight.ToString("0") + " of " + room.ToString("0") + " high)");
                Assert.Greater(lineCount, 0, "the text was laid out");
                Assert.LessOrEqual(lineCount, 5, "hint " + (h + 1) + " takes more than five lines");
                Assert.GreaterOrEqual(size, Toybox.UI.UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }
}
