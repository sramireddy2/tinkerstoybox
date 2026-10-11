using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Jump, click, click. The engine has always refused a click at the toy under the feet ("lifting the
    /// thing you stand on would let you fly"), but only while the feet were on it: in the air the toy one
    /// had just jumped off could be picked up and let go again under oneself - from higher up, so bigger,
    /// and a toy that stays where it is put simply higher. A general way up (the final review, owner-3;
    /// Level 10 and Level 12 had each closed it for themselves).
    ///
    /// The rule now: the toy the feet were last on stays out of reach until they are on something else.
    /// The first half of this fixture is that rule on a bare floor; the second half tries the trick on
    /// every toy of every campaign level.
    ///
    /// Measured before the rule: tools/out/notes/final-fix-underfoot-probe-before.txt (and -after.txt with
    /// it), written by the explicit probe at the end.
    /// </summary>
    public class UnderfootTests : SimTest
    {
        // ==== The rule, on a bare floor =========================================================================

        Prop block, other;

        // A block to stand on (1.2 high, heavier than the player) with the player on top of it, and a second
        // one two steps to the side.
        void BuildOnABlock(PropBody body = PropBody.Dynamic)
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1.2f), new Vector3(0f, 0.6f, 0f), new PropOptions { Name = "Block", Body = body });
                other = ctx.AddProp(BasicToys.Block(1.2f), new Vector3(2.4f, 0.6f, 0f), new PropOptions { Name = "Other" });
                ctx.SetSpawn(new Vector3(0f, 1.22f, 0f), 0f);
            });
            Run(30);
            Assert.AreSame(block, Game.Player.GroundProp, "setup: the player stands on the block");
            Assert.AreSame(block, Game.Player.Underfoot);
        }

        // One tick with the eyes turned onto a point by input (a flick of the mouse), and whatever else.
        void TickLookingAt(Vector3 point, InputFrame frame = default)
        {
            Player player = Game.Player;
            Vector3 to = point - player.Eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -Player.MaxPitch, Player.MaxPitch);
            frame.LookYaw = Mathf.DeltaAngle(player.Yaw, yaw);
            frame.LookPitch = pitch - player.Pitch;
            Input.Once = frame;
            Game.Tick();
        }

        [Test]
        public void TheToyJustJumpedOff_IsOutOfReach_AtEveryMomentOfTheJump()
        {
            BuildOnABlock();
            Player player = Game.Player;
            int pickUps = 0;
            Game.Events.PropGrabbed += e => pickUps++;
            // A click in every single tick of three jumps, the first of them in the tick of the jump itself.
            for (int jump = 0; jump < 3; jump++)
            {
                int airborne = 0;
                for (int tick = 0; tick < 120; tick++)
                {
                    TickLookingAt(block.Center, new InputFrame { Jump = tick == 0, GrabPressed = true });
                    Assert.IsNull(Game.Grabber.Held, "jump " + jump + ", tick " + tick);
                    Assert.IsNull(Game.Grabber.Focus, "and it is not offered either (jump " + jump + ", tick " + tick + ")");
                    if (!player.Grounded)
                    {
                        airborne++;
                        Assert.AreSame(block, player.Underfoot, "in the air off it, it is still underfoot");
                        Assert.IsNull(player.GroundProp);
                    }
                    else if (airborne > 0) break;
                }
                Assert.Greater(airborne, 30, "a whole jump was made");
                Assert.AreSame(block, player.GroundProp, "and it came down on the block again");
            }
            Run(10);
            Assert.AreEqual(0, pickUps);
            Assert.AreEqual(1f, block.Scale);
            Assert.AreEqual(1.2f, player.Position.y, 0.02f);
        }

        // The trick itself. Before the rule a block of 1.2 grew under its owner jump by jump (and a block
        // that stays where it is put was simply a jump higher each time).
        [Test]
        public void JumpClickClick_LeavesTheToyAndThePlayerWhereTheyWere(
            [Values(PropBody.Dynamic, PropBody.Fixed)] PropBody body, [Values(0, 1, 2, 3)] int timing)
        {
            BuildOnABlock(body);
            Player player = Game.Player;
            Vector3 at = block.Center;
            int pickUps = 0;
            for (int jump = 0; jump < Jumps; jump++) pickUps += JumpClickClick(Timings[timing][0], Timings[timing][1]);
            Assert.AreEqual(0, pickUps, "nothing was picked up");
            Assert.AreEqual(1f, block.Scale, "the block is as big as it was");
            Assert.Less(Vector3.Distance(at, block.Center), 0.02f, "and where it was");
            Assert.AreSame(block, player.GroundProp);
            Assert.AreEqual(1.2f, player.Position.y, 0.02f, "and nobody is any higher up");
        }

        [Test]
        public void SteppedOffOntoTheFloor_ItIsPickedUpAtOnce()
        {
            BuildOnABlock();
            Player player = Game.Player;
            // Off its edge, backward. On the way down it is still the toy that was underfoot.
            int fell = 0;
            for (int tick = 0; tick < 240 && !(player.Grounded && player.GroundProp == null); tick++)
            {
                TickLookingAt(block.Center, new InputFrame { MoveZ = -1f, GrabPressed = !player.Grounded });
                if (!player.Grounded) fell++;
                Assert.IsNull(Game.Grabber.Held, "not on the way down (tick " + tick + ")");
            }
            Assert.IsTrue(player.Grounded && player.GroundProp == null, "down on the floor");
            Assert.That(fell, Is.InRange(5, 40), "a drop of 1.2: a third of a second");
            // The feet are on something else: the very next click takes it.
            Assert.IsNull(player.Underfoot);
            Assert.AreSame(block, Game.Grabber.Focus, "it is offered again in the tick of the landing");
            TickLookingAt(block.Center, new InputFrame { GrabPressed = true });
            Assert.AreSame(block, Game.Grabber.Held);
        }

        [Test]
        public void FromAnotherToy_ItIsPickedUp_OnItAndInTheAirOffIt()
        {
            BuildOnABlock();
            Player player = Game.Player;
            // A jump across onto the other block.
            for (int tick = 0; tick < 240 && player.GroundProp != other; tick++)
                TickLookingAt(other.Center + Vector3.up * 0.6f, new InputFrame { MoveZ = 1f, Jump = tick == 0 });
            Assert.AreSame(other, player.GroundProp, "over on the other block");
            Assert.AreSame(other, player.Underfoot);
            Run(10);

            // Standing on the other one, the first is a toy like any other.
            TickLookingAt(block.Center, new InputFrame { GrabPressed = true });
            Assert.AreSame(block, Game.Grabber.Held, "picked up from the other block");
            TickLookingAt(new Vector3(-2f, 0f, 0f));
            TickLookingAt(new Vector3(-2f, 0f, 0f), new InputFrame { GrabPressed = true });
            Assert.IsNull(Game.Grabber.Held);
            Run(30);

            // And so it is in a jump off the other one: a pick-up in mid-air is still a pick-up.
            TickLookingAt(block.Center, new InputFrame { Jump = true });
            for (int tick = 0; tick < 8; tick++) TickLookingAt(block.Center);
            Assert.IsFalse(player.Grounded);
            TickLookingAt(block.Center, new InputFrame { GrabPressed = true });
            Assert.AreSame(block, Game.Grabber.Held, "picked up in the air off the other block");
            // ... while the one that was jumped off is not.
            TickLookingAt(new Vector3(-2f, 0f, 0f), new InputFrame { GrabPressed = true });
            Assert.IsNull(Game.Grabber.Held);
            TickLookingAt(other.Center, new InputFrame { GrabPressed = true });
            Assert.IsNull(Game.Grabber.Held, "the block that was jumped off stays out of reach");
        }

        [Test]
        public void AToyThatWasNeverStoodOn_IsPickedUpInAJumpFromTheFloor()
        {
            BuildOnABlock();
            Player player = Game.Player;
            player.Teleport(new Vector3(0f, 0f, -4f), 0f);
            Run(5);
            TickLookingAt(block.Center, new InputFrame { Jump = true });
            for (int tick = 0; tick < 8; tick++) TickLookingAt(block.Center);
            Assert.IsFalse(player.Grounded);
            Assert.IsNull(player.Underfoot);
            TickLookingAt(block.Center, new InputFrame { GrabPressed = true });
            Assert.AreSame(block, Game.Grabber.Held);
        }

        // Whoever is put somewhere by the level (a respawn, a hazard, a doorway) has left the toy behind.
        [Test]
        public void PutSomewhereElse_NothingIsUnderfoot([Values] bool respawn)
        {
            BuildOnABlock();
            Player player = Game.Player;
            player.SetCheckpoint(new Vector3(0f, 0f, -5f), 0f);
            TickLookingAt(block.Center, new InputFrame { Jump = true });
            for (int tick = 0; tick < 8; tick++) TickLookingAt(block.Center);
            Assert.IsFalse(player.Grounded);
            Assert.AreSame(block, player.Underfoot);

            if (respawn) player.Respawn();
            else player.Teleport(new Vector3(0f, 3f, -5f));
            Assert.IsNull(player.Underfoot);
            if (!respawn) Assert.IsFalse(player.Grounded, "in the air over the floor");
            TickLookingAt(block.Center, new InputFrame { GrabPressed = true });
            Assert.AreSame(block, Game.Grabber.Held);
        }

        [Test]
        public void AToyThatIsGone_IsNotUnderfoot()
        {
            BuildOnABlock();
            Player player = Game.Player;
            TickLookingAt(block.Center, new InputFrame { Jump = true });
            Run(5);
            Assert.AreSame(block, player.Underfoot);
            Game.Context.RemoveProp(block);
            Assert.IsNull(player.Underfoot);
        }

        // ==== The trick, as raw input ============================================================================

        /// <summary>
        /// One jump straight up off whatever is under the feet, eyes on one's feet all the way: a click
        /// `grabTick` ticks after the jump is pressed (0: in the very same tick) and another `dropTick`
        /// ticks after that (the top of a jump is about 20 ticks in). Then down again, and whatever is
        /// still in the hand is let go. Returns how often anything was picked up.
        /// </summary>
        int JumpClickClick(int grabTick, int dropTick)
        {
            Player player = Game.Player;
            int pickUps = 0, landed = 0;
            System.Action<PropHoldEvent> count = e => pickUps++;
            Game.Events.PropGrabbed += count;
            try
            {
                for (int tick = 0; tick < 300; tick++)
                {
                    var frame = new InputFrame { LookPitch = -Player.MaxPitch - player.Pitch };
                    if (tick == 0) frame.Jump = true;
                    if (tick == grabTick || tick == grabTick + dropTick) frame.GrabPressed = true;
                    Input.Once = frame;
                    Game.Tick();
                    if (tick <= grabTick + dropTick || !player.Grounded) landed = 0;
                    else if (++landed >= 12) break;
                }
                if (Game.Grabber.Held != null)
                {
                    Input.Once = new InputFrame { GrabPressed = true };
                    Game.Tick();
                    Run(12);
                }
            }
            finally
            {
                Game.Events.PropGrabbed -= count;
            }
            return pickUps;
        }

        // ==== Every toy of every level ============================================================================

        struct Attempt
        {
            public string Prop;
            /// <summary>Could the player be stood on it at all?</summary>
            public bool Stood;
            public int Jumps, PickUps;
            public float ScaleBefore, ScaleAfter, FeetBefore, FeetAfter;
        }

        // The jumps of one attempt, and the moments of the two clicks that were tried: at once and at the
        // top; a tick late and on the way up; a little later; in the same tick as the jump.
        const int Jumps = 4;
        static readonly int[][] Timings = { new[] { 1, 18 }, new[] { 3, 10 }, new[] { 6, 14 }, new[] { 0, 20 } };

        // Stands the player on top of a toy (a fixture's shortcut: how one gets up there is each level's own
        // business). False if the toy is nothing to stand on - a marble, a crumb, something that tips.
        bool StandOn(Prop prop)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Collider collider in prop.Colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (any) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                any = true;
            }
            if (!any) return false;
            Player player = Game.Player;
            // Over its middle first, then a little to each side (an arch has no middle to stand on).
            var offsets = new[] { Vector2.zero, new Vector2(0.6f, 0f), new Vector2(-0.6f, 0f), new Vector2(0f, 0.6f), new Vector2(0f, -0.6f) };
            foreach (Vector2 offset in offsets)
            {
                var above = new Vector3(bounds.center.x + offset.x * bounds.extents.x, bounds.max.y + 1f, bounds.center.z + offset.y * bounds.extents.z);
                Vector3 top = default;
                bool hit = false;
                foreach (Collider collider in prop.Colliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    if (!collider.Raycast(new Ray(above, Vector3.down), out RaycastHit found, bounds.size.y + 2f)) continue;
                    if (hit && found.point.y <= top.y) continue;
                    top = found.point;
                    hit = true;
                }
                if (!hit) continue;
                player.Teleport(top + Vector3.up * 0.02f);
                int on = 0;
                for (int tick = 0; tick < 40 && on < 6; tick++)
                {
                    Game.Tick();
                    on = player.Grounded && player.GroundProp == prop ? on + 1 : 0;
                }
                if (on >= 6) return true;
            }
            return false;
        }

        // A toy that is nothing to stand on as the level has it (a card on its edge) is laid on its largest
        // face at the start, so many times bigger, as a player might put it down (a fixture's shortcut again).
        bool LayFlat(Prop prop, int times)
        {
            if (prop.Removed || prop.BodyKind == PropBody.Kinematic || prop.Driven) return false;
            if (times > 1) prop.SetScale(prop.Scale * 2f);
            Vector3 half = prop.LocalHalfExtents;
            Vector3 thin = half.x <= half.y && half.x <= half.z ? Vector3.right : half.y <= half.z ? Vector3.up : Vector3.forward;
            Quaternion rotation = Quaternion.FromToRotation(thin, Vector3.up);
            float thickness = Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * prop.Scale;
            Vector3 spot = Game.Context.SpawnPosition + Quaternion.Euler(0f, Game.Context.SpawnYaw, 0f) * Vector3.forward * (prop.Radius + 1f);
            Vector3 centre = spot + Vector3.up * (thickness + 0.05f);
            if (prop.Frozen) prop.Unfreeze();
            prop.SetPose(centre - rotation * (prop.LocalCenter * prop.Scale), rotation);
            Run(40);
            return !prop.Removed && prop.Grabbable;
        }

        /// <summary>
        /// The trick on every toy of a level that can be picked up: the level loaded afresh for each, the
        /// player stood on the toy, then `Jumps` jumps with two clicks each.
        /// </summary>
        List<Attempt> Sweep(int levelId, int grabTick, int dropTick)
        {
            var attempts = new List<Attempt>();
            Game?.Dispose();
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(levelId);
            int count = Game.Props.Count;
            for (int index = 0; index < count; index++)
            {
                if (index > 0) Game.LoadLevel(levelId);
                Run(30);
                if (index >= Game.Props.Count) break;
                Prop prop = Game.Props[index];
                if (prop.Removed || !prop.Grabbable || !prop.GameObject.activeInHierarchy) continue;
                var attempt = new Attempt { Prop = prop.Name, ScaleBefore = prop.Scale, ScaleAfter = prop.Scale };
                attempt.Stood = StandOn(prop);
                // Nothing to stand on as the level has it: on its largest face, then bigger and bigger (a
                // toy lighter than a third of the player is kicked away by the side of a foot).
                for (int grow = 0; grow < 4 && !attempt.Stood && !prop.Removed; grow++)
                    attempt.Stood = LayFlat(prop, 1 << grow) && StandOn(prop);
                if (attempt.Stood)
                {
                    attempt.ScaleBefore = prop.Scale;
                    attempt.FeetBefore = Game.Player.Position.y;
                    for (int jump = 0; jump < Jumps && !prop.Removed; jump++)
                    {
                        // Only a jump off the toy itself is the trick (the level may have sent it home meanwhile).
                        if (!(Game.Player.Grounded && Game.Player.GroundProp == prop)) break;
                        attempt.PickUps += JumpClickClick(grabTick, dropTick);
                        attempt.Jumps++;
                    }
                    attempt.ScaleAfter = prop.Removed ? 0f : prop.Scale;
                    attempt.FeetAfter = Game.Player.Position.y;
                }
                attempts.Add(attempt);
            }
            return attempts;
        }

        static string Describe(Attempt a)
        {
            if (!a.Stood) return a.Prop + ": nothing to stand on";
            return a.Prop + ": " + a.Jumps + " jumps, " + a.PickUps + " pick-ups, scale " + a.ScaleBefore.ToString("0.000") + " -> " + a.ScaleAfter.ToString("0.000") +
                   ", feet " + a.FeetBefore.ToString("0.00") + " -> " + a.FeetAfter.ToString("0.00");
        }

        static void WriteProbe(string name, StringBuilder text)
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../tools/out/notes/" + name));
            System.IO.File.WriteAllText(path, text.ToString());
        }

        // Levels whose toys are nothing to stand on, however they lie: 14's doorway (its frame is not solid
        // to the player). Everywhere else at least one toy was stood on and jumped off.
        static bool HasAToyToStandOn(int levelId) => levelId != 14;

        /// <summary>
        /// Every level, every toy that can be picked up, four timings of the two clicks: nothing is picked
        /// up from under one's own feet in a jump, no toy is any bigger for it and nobody any higher.
        /// (Before the rule: 14 of 15 levels had a toy that grew, by up to seven times in four jumps -
        /// tools/out/notes/final-fix-underfoot-probe-before.txt.)
        /// </summary>
        [Test]
        public void InEveryLevel_NoToyIsPickedUpFromUnderOnesFeetInAJump([NUnit.Framework.Range(1, 15)] int levelId)
        {
            int stood = 0, jumps = 0;
            var log = new StringBuilder();
            foreach (int[] timing in Timings)
            {
                foreach (Attempt attempt in Sweep(levelId, timing[0], timing[1]))
                {
                    string what = "level " + levelId + ", clicks at " + timing[0] + " and +" + timing[1] + ": " + Describe(attempt);
                    log.AppendLine(what);
                    if (!attempt.Stood) continue;
                    stood++;
                    jumps += attempt.Jumps;
                    Assert.AreEqual(0, attempt.PickUps, what);
                    Assert.LessOrEqual(attempt.ScaleAfter, attempt.ScaleBefore + 1e-4f, what);
                }
            }
            if (!HasAToyToStandOn(levelId)) return;
            Assert.Greater(stood, 0, "no toy of the level could be stood on:\n" + log);
            Assert.Greater(jumps, 0, "no jump off a toy was made:\n" + log);
        }

        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_TheTrickInEveryLevel()
        {
            var text = new StringBuilder();
            text.AppendLine("Jump, click, click on every toy that can be picked up, levels 1 to 15: " + Jumps + " jumps each, eyes on one's feet.");
            foreach (int[] timing in Timings)
            {
                text.AppendLine();
                text.AppendLine("first click " + timing[0] + " ticks after the jump, second " + timing[1] + " ticks later");
                for (int id = 1; id <= 15; id++)
                {
                    foreach (Attempt attempt in Sweep(id, timing[0], timing[1]))
                        text.AppendLine("  level " + id.ToString("00") + "  " + Describe(attempt));
                }
            }
            WriteProbe("final-fix-underfoot-probe-after.txt", text);
        }
    }
}
