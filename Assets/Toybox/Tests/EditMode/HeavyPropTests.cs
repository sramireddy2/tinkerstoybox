using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Nothing throws the player: a dynamic prop that outweighs them and comes at them passes through
    /// instead of hitting them (PerspectiveGrabber.BeforePhysics), and is solid for them again once it is
    /// no longer coming and they are out of it. Everything else about props and the player's body stays as
    /// it was. The numbers the thresholds come from are in tools/out/notes/prelude-engine-fling.md.
    /// </summary>
    public class HeavyPropTests : SimTest
    {
        /// <summary>How a prop comes at a player who stands at the origin, looking down +Z.</summary>
        public enum Hit
        {
            /// <summary>A block sliding along the floor from the left.</summary>
            SlideSide,
            /// <summary>A block sliding along the floor from behind.</summary>
            SlideBehind,
            /// <summary>A plank a quarter of a unit thick sliding along the floor into the player's feet.</summary>
            SlideLow,
            /// <summary>A plank so thin (0.12) that the foot of the capsule steps onto it, sliding along the floor under the player.</summary>
            SlideThin,
            /// <summary>A block sliding toward the player while the player walks into it.</summary>
            WalkInto,
            /// <summary>A ball rolling along the floor from the left.</summary>
            Roll,
            /// <summary>A block let go above the player's head, squarely over them.</summary>
            FallAbove,
            /// <summary>A block let go above the player so that only its edge comes down on them.</summary>
            FallEdge,
            /// <summary>A domino-shaped slab that stands in front of the player and topples onto them.</summary>
            Topple,
        }

        public struct Outcome
        {
            /// <summary>Speed of the prop's nearest point toward the player in the tick before they first touched.</summary>
            public float Approach;
            /// <summary>The player's highest speed.</summary>
            public float Fastest;
            /// <summary>How far the player was moved: from where they stood, or (walking) sideways, up and back from the farthest they got.</summary>
            public float Moved;
            /// <summary>The prop ignored the player's capsule at some time; the tick at which it first did (-1: never) and the gap to the capsule then.</summary>
            public bool Passed;
            public int PassedAt;
            public float PassedGap;
            /// <summary>The smallest gap between the prop and the capsule (negative: they overlapped).</summary>
            public float Nearest;
            /// <summary>At the end: is the prop solid for the player, how fast is it, where is the player.</summary>
            public bool SolidAtEnd;
            public float PropSpeedAtEnd;
            public Vector3 PlayerAtEnd;
        }

        const float BlockSize = 2f;
        const float BallRadius = 1f;
        const float Friction = 0.6f;
        static readonly Vector3 SlabSize = new Vector3(1f, 2f, 0.3f);
        static readonly Vector3 PlankSize = new Vector3(3f, 0.25f, 3f);
        static readonly Vector3 ThinPlankSize = new Vector3(5f, 0.12f, 5f);

        Prop prop;

        // ---- The scenes ------------------------------------------------------------------------------------------

        static PropOptions Weighing(float mass, float volume) =>
            new PropOptions { Density = mass / volume, Friction = Friction, Grabbable = false };

        /// <summary>
        /// Builds the scene and sets the prop going. `speed` is the speed it should have when it reaches the
        /// player (for Topple it sets the size of the slab instead: 2 x speed / 3 high, and the taller it
        /// is, the faster the part that reaches the player's head).
        /// </summary>
        void Launch(Hit hit, float mass, float speed)
        {
            float slide = Friction * Game.Gravity;
            const float gap = 0.1f;
            Vector3 velocity = Vector3.zero, spin = Vector3.zero;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float half = BlockSize * 0.5f;
                float reach = half + Player.BaseRadius + gap;
                // Friction takes this much off on the way across the gap.
                float sliding = Mathf.Sqrt(speed * speed + 2f * slide * gap);
                PropOptions block = Weighing(mass, BlockSize * BlockSize * BlockSize);
                switch (hit)
                {
                    case Hit.SlideSide:
                        prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(-reach, half + 0.002f, 0f), block);
                        velocity = new Vector3(sliding, 0f, 0f);
                        break;
                    case Hit.SlideBehind:
                        prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(0f, half + 0.002f, -reach), block);
                        velocity = new Vector3(0f, 0f, sliding);
                        break;
                    case Hit.SlideLow:
                    case Hit.SlideThin:
                    {
                        Vector3 size = hit == Hit.SlideLow ? PlankSize : ThinPlankSize;
                        prop = ctx.AddProp(BasicToys.Block(size), new Vector3(-(size.x * 0.5f + Player.BaseRadius + gap), size.y * 0.5f + 0.002f, 0f),
                            Weighing(mass, size.x * size.y * size.z));
                        velocity = new Vector3(sliding, 0f, 0f);
                        break;
                    }
                    case Hit.WalkInto:
                    {
                        // They meet after about 1 / (5 + speed) seconds; the block has slid speed / (5 + speed) by then.
                        float start = Mathf.Sqrt(speed * speed + 2f * slide * speed / (Player.WalkSpeed + speed));
                        prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(0f, half + 0.002f, half + Player.BaseRadius + 1f), block);
                        velocity = new Vector3(0f, 0f, -start);
                        break;
                    }
                    case Hit.FallAbove:
                    case Hit.FallEdge:
                    {
                        // Let go from the height that makes it arrive at this speed.
                        float height = speed * speed / (2f * Game.Gravity);
                        float aside = hit == Hit.FallEdge ? half + 0.1f : 0f;
                        prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(aside, Player.BaseHeight + height + half + 0.002f, 0f), block);
                        break;
                    }
                    case Hit.Roll:
                        prop = ctx.AddProp(BasicToys.Ball(BallRadius), new Vector3(-(BallRadius + Player.BaseRadius + gap), BallRadius + 0.002f, 0f),
                            Weighing(mass, 4f / 3f * Mathf.PI * BallRadius * BallRadius * BallRadius));
                        velocity = new Vector3(speed, 0f, 0f);
                        spin = new Vector3(0f, 0f, -speed / BallRadius);
                        break;
                    case Hit.Topple:
                    {
                        // On its lower edge at z = foot, leaning 12 degrees toward the player (past its balance).
                        float scale = speed / 3f;
                        float height = SlabSize.y * scale;
                        float foot = 0.3f + 0.5f * height;
                        const float lean = 12f * Mathf.Deg2Rad;
                        float halfHeight = 0.5f * height, halfDepth = 0.5f * SlabSize.z * scale;
                        var centre = new Vector3(0f, halfHeight * Mathf.Cos(lean) + halfDepth * Mathf.Sin(lean) + 0.002f,
                            foot - halfHeight * Mathf.Sin(lean) + halfDepth * Mathf.Cos(lean));
                        PropOptions slab = Weighing(mass, SlabSize.x * SlabSize.y * SlabSize.z * scale * scale * scale);
                        slab.Scale = scale;
                        prop = ctx.AddProp(BasicToys.Block(SlabSize), centre, Quaternion.Euler(-12f, 0f, 0f), slab);
                        break;
                    }
                }
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.AreEqual(mass, prop.Mass, mass * 1e-3f, "test setup: the mass of the prop");
            // A test fixture's shortcut for "it is on its way".
            prop.Body.linearVelocity = velocity;
            prop.Body.angularVelocity = spin;
            if (hit == Hit.WalkInto) Input.Hold.MoveZ = 1f;
        }

        Outcome Play(Hit hit, float mass, float speed, float seconds = 4f)
        {
            Launch(hit, mass, speed);
            return Watch(seconds, hit == Hit.WalkInto);
        }

        /// <summary>Runs the scene and records what the prop does to the player.</summary>
        Outcome Watch(float seconds, bool walking = false)
        {
            var outcome = new Outcome { PassedAt = -1, Nearest = float.MaxValue };
            Vector3 stood = Game.Player.Position;
            float farthest = stood.z;
            bool touched = false;
            for (int i = 0; i < TestHelpers.Ticks(seconds); i++)
            {
                float gap = Gap(prop, out float approach);
                if (!touched)
                {
                    // As it was in the last tick before they touched.
                    if (i == 0 || gap >= 0.02f) outcome.Approach = approach;
                    touched = gap < 0.02f;
                }
                Game.Tick();
                Vector3 at = Game.Player.Position;
                outcome.Fastest = Mathf.Max(outcome.Fastest, Game.Player.Velocity.magnitude);
                farthest = Mathf.Max(farthest, at.z);
                float moved = walking
                    ? new Vector3(at.x - stood.x, at.y - stood.y, Mathf.Max(0f, farthest - at.z)).magnitude
                    : Vector3.Distance(stood, at);
                outcome.Moved = Mathf.Max(outcome.Moved, moved);
                outcome.Nearest = Mathf.Min(outcome.Nearest, Gap(prop, out _));
                if (!outcome.Passed && PassesThroughThePlayer(prop))
                {
                    outcome.Passed = true;
                    outcome.PassedAt = i;
                    outcome.PassedGap = gap;
                }
            }
            outcome.SolidAtEnd = !PassesThroughThePlayer(prop);
            outcome.PropSpeedAtEnd = prop.Velocity.magnitude;
            outcome.PlayerAtEnd = Game.Player.Position;
            return outcome;
        }

        bool PassesThroughThePlayer(Prop p)
        {
            foreach (Collider collider in p.Colliders)
                if (Physics.GetIgnoreCollision(collider, Game.Player.Collider)) return true;
            return false;
        }

        /// <summary>
        /// Distance between the prop and the player's capsule (negative: how deep they overlap), and how fast
        /// the prop's nearest point moves toward the player.
        /// </summary>
        float Gap(Prop p, out float approach)
        {
            Player player = Game.Player;
            float radius = player.Radius;
            Vector3 low = player.Position + Vector3.up * radius, high = player.Position + Vector3.up * (player.Height - radius);
            float best = float.MaxValue;
            approach = 0f;
            foreach (Collider collider in p.Colliders)
            {
                Transform t = collider.transform;
                if (Physics.ComputePenetration(collider, t.position, t.rotation, player.Collider, player.Position, Quaternion.identity, out Vector3 away, out float depth)
                    && depth > 0f)
                {
                    if (-depth >= best) continue;
                    best = -depth;
                    approach = Vector3.Dot(p.Body.GetPointVelocity(collider.ClosestPoint((low + high) * 0.5f)), -away);
                    continue;
                }
                // The nearest pair of points of the collider and the capsule's axis, by going back and forth.
                Vector3 onAxis = (low + high) * 0.5f;
                Vector3 onProp = collider.ClosestPoint(onAxis);
                for (int i = 0; i < 4; i++)
                {
                    onAxis = low + (high - low) * Mathf.Clamp01(Vector3.Dot(onProp - low, high - low) / (high - low).sqrMagnitude);
                    onProp = collider.ClosestPoint(onAxis);
                }
                Vector3 to = onAxis - onProp;
                float distance = to.magnitude - radius;
                if (distance >= best) continue;
                best = distance;
                approach = to.sqrMagnitude > 1e-10f ? Vector3.Dot(p.Body.GetPointVelocity(onProp), to.normalized) : 0f;
            }
            return best;
        }

        void End()
        {
            Game.Dispose();
            Game = null;
        }

        static string F(float value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);

        // ---- Every way a prop comes at the player: none of them throws them --------------------------------------

        // Times the player's mass (3): half as heavy, and from just heavier to a house.
        static readonly float[] Masses = { 0.5f, 1.2f, 2f, 5f, 20f, 200f };
        static readonly float[] Speeds = { 1f, 3f, 6f, 12f, 25f };

        /// <summary>
        /// After any single impact the player moves at less than 9 units a second and has been moved by less
        /// than 1.5 units. A prop that outweighs them and comes faster than PassSpeed does not touch them at
        /// all; a prop that does not outweigh them never passes through, and pushes them as it always did.
        /// (Before the rule: 12 to 76 units a second, up to 45 units away.)
        /// </summary>
        [TestCase(Hit.SlideSide)]
        [TestCase(Hit.SlideBehind)]
        [TestCase(Hit.SlideLow)]
        [TestCase(Hit.SlideThin)]
        [TestCase(Hit.WalkInto)]
        [TestCase(Hit.Roll)]
        [TestCase(Hit.FallAbove)]
        [TestCase(Hit.FallEdge)]
        [TestCase(Hit.Topple)]
        public void WhateverComesAtThePlayer_DoesNotThrowThem(Hit hit)
        {
            var failures = new List<string>();
            int passed = 0, pushed = 0;
            bool walking = hit == Hit.WalkInto;
            bool plank = hit == Hit.SlideLow || hit == Hit.SlideThin;
            foreach (float ratio in Masses)
                foreach (float speed in Speeds)
                {
                    // The tallest slab takes three seconds to come down.
                    Outcome o = Play(hit, ratio * Player.Mass, speed, hit == Hit.Topple ? 4f : 3f);
                    string what = hit + ", " + F(ratio, "0.#") + " x the player's mass, speed " + F(speed, "0") + " (at them with " + F(o.Approach)
                                  + "): fastest " + F(o.Fastest) + ", moved " + F(o.Moved) + (o.Passed ? ", passed at tick " + o.PassedAt + " from " + F(o.PassedGap) : ", solid");
                    bool heavy = ratio > 1f;
                    if (heavy)
                    {
                        if (o.Fastest >= 9f) failures.Add(what + ": thrown");
                        if (o.Moved > 1.5f) failures.Add(what + ": carried off");
                        if (o.Approach > PerspectiveGrabber.PassSpeed * 1.25f)
                        {
                            // It never touched them: they stand where they stood (or walk on as if it were not there).
                            if (!o.Passed) failures.Add(what + ": it should have passed through them");
                            else if (o.PassedGap <= 0f) failures.Add(what + ": it was only let through once it had touched them");
                            if (o.Moved > 0.05f || o.Fastest > (walking ? Player.WalkSpeed + 0.05f : 0.05f)) failures.Add(what + ": it touched them on its way through");
                            passed++;
                        }
                    }
                    else
                    {
                        if (o.Passed) failures.Add(what + ": a prop that does not outweigh the player went through them");
                        // A light plank that slides in under the feet becomes what the player stands on, and
                        // carries them: riding, not a hit, and as it always was (0.5 x at 25: 7 to 15 units a second).
                        if (!plank && o.Fastest >= 9f) failures.Add(what + ": thrown");
                        if (!plank && o.Moved > 1.5f) failures.Add(what + ": carried off");
                        if (o.Fastest > 0.2f && !walking) pushed++;
                    }
                    End();
                }
            Debug.Log("heavy props: " + hit + ": " + passed + " passed through untouched, " + pushed + " light ones pushed");
            Assert.IsEmpty(failures, failures.Count + " of " + Masses.Length * Speeds.Length + ":\n" + string.Join("\n", failures));
            Assert.GreaterOrEqual(passed, 20, "test setup: the heavy ones at speed came at the player and passed");
            if (!walking && hit != Hit.FallAbove) Assert.GreaterOrEqual(pushed, 3, "light props push the player, as before");
        }

        /// <summary>A prop lighter than the player hands them its momentum and no more, exactly as before the rule.</summary>
        [Test]
        public void ALightProp_PushesThePlayerByItsMomentum_AsBefore()
        {
            foreach (float speed in new[] { 6f, 12f, 25f })
            {
                const float mass = 0.5f * Player.Mass;
                Outcome o = Play(Hit.SlideSide, mass, speed);
                float expected = o.Approach * mass / (mass + Player.Mass);
                Assert.IsFalse(o.Passed, "a light block at " + speed);
                Assert.AreEqual(expected, o.Fastest, expected * 0.1f, "a block half the player's weight at " + speed + " gives them a third of its speed");
                Assert.IsTrue(o.SolidAtEnd);
                End();
            }
        }

        // ---- Slow and heavy: solid, shoves, and gives way before it becomes a bulldozer ---------------------------

        /// <summary>
        /// Slower than PassSpeed a heavy prop is solid. One that stops against the player stays solid; one
        /// that keeps coming (a boulder that weighs 600 rolling at 0.8: 2 units and more before the rule)
        /// shoves them for a quarter of a second and then passes through.
        /// </summary>
        [Test]
        public void ASlowBoulder_ShovesForAMoment_ThenPassesThrough()
        {
            Outcome roll = Play(Hit.Roll, 200f * Player.Mass, 0.8f, 6f);
            Debug.Log("heavy props: slow boulder: fastest " + F(roll.Fastest) + " moved " + F(roll.Moved) + " passed at " + roll.PassedAt + " prop speed at end " + F(roll.PropSpeedAtEnd));
            Assert.IsTrue(roll.Passed, "it kept shoving and was let through");
            int shoving = TestHelpers.Ticks(PerspectiveGrabber.PushSeconds);
            Assert.GreaterOrEqual(roll.PassedAt, shoving, "not before it had shoved for " + PerspectiveGrabber.PushSeconds + " s");
            Assert.Less(roll.PassedAt, shoving + 20);
            Assert.Greater(roll.Fastest, 0.5f, "until then it was solid and moved the player");
            Assert.Less(roll.Fastest, 1f);
            Assert.Less(roll.Moved, 0.4f, "a shove, not a ride");
            Assert.IsTrue(roll.SolidAtEnd, "it has rolled on through and is solid again");
            Assert.Greater(prop.Center.x, 0.5f, "it is past the player");
            End();

            // A block that slides up to the player and stops against them is simply solid.
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(-(BlockSize * 0.5f + Player.BaseRadius + 0.02f), BlockSize * 0.5f + 0.002f, 0f),
                    Weighing(200f * Player.Mass, BlockSize * BlockSize * BlockSize));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            prop.Body.linearVelocity = new Vector3(0.9f, 0f, 0f);
            Outcome slide = Watch(2f);
            Debug.Log("heavy props: slow block: at them with " + F(slide.Approach) + " fastest " + F(slide.Fastest) + " moved " + F(slide.Moved) + " nearest " + F(slide.Nearest));
            Assert.Less(slide.Nearest, 0.01f, "test setup: it reached the player");
            Assert.IsFalse(slide.Passed, "slower than " + PerspectiveGrabber.PassSpeed + " and soon at rest: solid");
            Assert.Less(slide.Moved, 0.1f);
            Assert.Less(slide.Fastest, 0.9f);
            Assert.Less(prop.Velocity.magnitude, 0.01f);
        }

        // ---- Afterwards -----------------------------------------------------------------------------------------

        /// <summary>
        /// A slab that topples onto the player comes to rest round them. They are not moved, walk out of it,
        /// and then it is solid: something to bump into and to stand on.
        /// </summary>
        [Test]
        public void TheToppledSlab_IsSolidAgain_OnceThePlayerHasWalkedOut_AndCanBeStoodOn()
        {
            // 4 high, 2 wide, 0.6 thick, 60: it ends up lying from z = -1.7 to 2.3 with the player inside it.
            Outcome fall = Play(Hit.Topple, 20f * Player.Mass, 6f);
            Assert.IsTrue(fall.Passed);
            Assert.Less(fall.Moved, 0.01f, "the player was not moved");
            Assert.Less(prop.Velocity.magnitude, 0.05f, "test setup: the slab has come to rest");
            Assert.Less(Mathf.Abs(prop.Transform.up.y), 0.1f, "test setup: it lies flat");
            Assert.Less(fall.Nearest, -0.25f, "test setup: it lies round the player");
            Assert.IsFalse(fall.SolidAtEnd, "while the player is inside it, it does not touch them");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.IsNull(Game.Player.GroundProp, "nor is it what they stand on: they stand on the floor");

            // Out through its side.
            Game.Player.Yaw = 90f;
            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !PassesThroughThePlayer(prop), 2f), "walking out makes it solid again");
            Input.Hold = default;
            RunSeconds(0.3f);
            float outside = Game.Player.Position.x;
            Assert.Greater(outside, 1.25f, "the player is beside the slab");
            Assert.Less(outside, 2.5f, "it became solid as soon as they were out");

            // Back into it: now it stops them.
            Game.Player.Yaw = -90f;
            Input.Hold.MoveZ = 1f;
            RunSeconds(1f);
            Input.Hold = default;
            Assert.Greater(Game.Player.Position.x, 1.25f, "the slab is solid from the side");
            Assert.IsTrue(!PassesThroughThePlayer(prop));

            // And on top of it (put there: a test fixture's shortcut for climbing).
            float top = SlabSize.z * 2f;
            Game.Player.Teleport(new Vector3(0f, top + 0.5f, 0f));
            RunSeconds(1f);
            Assert.AreSame(prop, Game.Player.GroundProp, "the player stands on the fallen slab");
            Assert.AreEqual(top, Game.Player.Position.y, 0.03f);
            Input.Once.Jump = true;
            RunSeconds(1.2f);
            Assert.AreSame(prop, Game.Player.GroundProp, "and lands on it again after a jump");
            Assert.IsTrue(!PassesThroughThePlayer(prop));
        }

        // ---- What works today keeps working ------------------------------------------------------------------------

        [Test]
        public void ThePlayerStandsAndWalksOnAHeavyProp_AtRest_AndWhileItMoves()
        {
            float drift = 0f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                var size = new Vector3(8f, 0.5f, 8f);
                prop = ctx.AddProp(BasicToys.Block(size), new Vector3(0f, 0.252f, 0f), Weighing(200f * Player.Mass, size.x * size.y * size.z));
                // Something drags the slab along the floor (a test fixture's shortcut for a raft, a sled).
                ctx.OnUpdate(dt =>
                {
                    if (drift > 0f) prop.Body.linearVelocity = new Vector3(drift, prop.Body.linearVelocity.y, 0f);
                });
                ctx.SetSpawn(new Vector3(0f, 0.55f, 0f), 0f);
            });
            bool passed = false;
            void Step(int ticks, string phase)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Game.Tick();
                    passed |= PassesThroughThePlayer(prop);
                }
                Assert.IsFalse(passed, phase + ": the slab under the player's feet gave way");
                Assert.AreSame(prop, Game.Player.GroundProp, phase + ": not standing on the slab (player at " + Game.Player.Position.ToString("F2") + " moving " + Game.Player.Velocity.ToString("F2")
                    + ", slab at " + prop.Center.ToString("F2") + " moving " + prop.Velocity.ToString("F2") + ")");
                Assert.AreEqual(prop.Center.y + 0.25f, Game.Player.Position.y, 0.03f, phase + ": sunk into it or lifted off it");
            }

            Step(30, "at rest");
            Input.Hold.MoveZ = 1f;
            Step(30, "walking on it");
            Input.Hold = default;

            // Slowly, and faster than a prop may come at the player: what carries them is not coming at them.
            foreach (float speed in new[] { 0.8f, 4f })
            {
                drift = speed;
                Step(60, "carried at " + speed);
                Assert.Greater(prop.Velocity.x, speed * 0.6f, "test setup: the slab is dragged along");
                Assert.AreEqual(prop.Velocity.x, Game.Player.Velocity.x, 0.2f, "carried along at " + speed);
                Input.Hold.MoveZ = -1f;
                Step(30, "walking on it at " + speed);
                Input.Hold.MoveZ = 1f;
                Step(30, "walking back on it at " + speed);
                Input.Hold = default;
                Step(10, "standing on it at " + speed);
                float before = Game.Player.Position.x - prop.Center.x;
                Input.Once.Jump = true;
                for (int i = 0; i < 60; i++)
                {
                    Game.Tick();
                    passed |= PassesThroughThePlayer(prop);
                }
                Step(1, "landed on it again at " + speed);
                Assert.AreEqual(before, Game.Player.Position.x - prop.Center.x, 0.3f, "the jump came down where it went up");
            }
        }

        [Test]
        public void PushingAProp_WorksAsBefore_LightOrHeavy()
        {
            // A crate (1), a block the player can still shove (4.5), and one they cannot (600), each at rest in their way.
            foreach (float mass in new[] { 1f, 1.5f * Player.Mass, 200f * Player.Mass })
            {
                Build(ctx =>
                {
                    TestHelpers.Floor(ctx, 200f);
                    prop = ctx.AddProp(BasicToys.Block(BlockSize), new Vector3(0f, BlockSize * 0.5f + 0.002f, BlockSize * 0.5f + 1f), Weighing(mass, BlockSize * BlockSize * BlockSize));
                    ctx.SetSpawn(Vector3.zero, 0f);
                });
                Input.Hold.MoveZ = 1f;
                float start = prop.Center.z;
                Outcome o = Watch(2f, true);
                string what = "a block of " + mass + " in the way";
                Assert.IsFalse(o.Passed, what + ": walking into a prop does not make it give way");
                Assert.IsTrue(o.SolidAtEnd);
                Assert.Greater(Game.Player.Position.z, 0.69f, what);
                Assert.AreEqual(prop.Center.z - BlockSize * 0.5f - Player.BaseRadius, Game.Player.Position.z, 0.05f, what + ": the player is up against it");
                float pushed = prop.Center.z - start;
                Debug.Log("heavy props: pushing " + mass + ": the block moved " + F(pushed) + ", the player is at " + F(Game.Player.Position.z));
                if (mass < 100f) Assert.Greater(pushed, mass < Player.Mass ? 6f : 4f, what + ": it is pushed along");
                else Assert.Less(pushed, 0.01f, what + ": it does not budge");
                End();
            }
        }

        // What moves with the player is not coming at them; what they are carried into is not coming at them either.
        [Test]
        public void OnAMovingPlatform_AHeavyPropThatRidesAlong_AndOneThePlatformCarriesThePlayerInto_StaySolid()
        {
            Mover platform = null;
            Prop rider = null, standing = null;
            var at = new Vector3(0f, 0.25f, 0f);
            float speed = 0f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                platform = ctx.AddKinematic(BasicToys.Slab(new Vector3(8f, 0.5f, 2f)), at);
                ctx.OnUpdate(dt =>
                {
                    at += Vector3.right * (speed * dt);
                    platform.MoveTo(at);
                });
                // Behind the player on the platform: in the world it moves toward them whenever the platform does.
                rider = ctx.AddProp(BasicToys.Block(1f), new Vector3(-2.5f, 1.002f, 0f), Weighing(20f * Player.Mass, 1f));
                // Across the platform's way, on two ledges beside it, clear of its top: it never moves.
                TestHelpers.Box(ctx, new Vector3(12f, 0.3f, 2f), new Vector3(2f, 0.6f, 1.5f));
                TestHelpers.Box(ctx, new Vector3(12f, 0.3f, -2f), new Vector3(2f, 0.6f, 1.5f));
                standing = ctx.AddProp(BasicToys.Block(new Vector3(2f, 2f, 5f)), new Vector3(12f, 1.602f, 0f), Weighing(200f * Player.Mass, 20f));
                ctx.SetSpawn(new Vector3(0f, 0.52f, 0f), 0f);
            });
            Run(30);
            Assert.AreSame(platform.Body, Game.Player.GroundCollider.attachedRigidbody, "test setup: the player rides the platform");
            speed = 3f;
            bool riderPassed = false, standingPassed = false;
            void Step(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Game.Tick();
                    riderPassed |= PassesThroughThePlayer(rider);
                    standingPassed |= PassesThroughThePlayer(standing);
                }
            }

            // Under way: the block has caught up with the platform. The player walks back into it.
            Step(60);
            Assert.AreEqual(3f, rider.Velocity.x, 0.1f, "test setup: the block rides along");
            Game.Player.Yaw = -90f;
            Input.Hold.MoveZ = 1f;
            Step(40);
            Input.Hold = default;
            Assert.IsFalse(riderPassed, "a heavy block that rides the same platform is solid for the player");
            Assert.AreEqual(rider.Center.x + 0.5f + Player.BaseRadius, Game.Player.Position.x, 0.05f, "the player is up against it");

            // On to the block that stands across the way: it stops them and the platform goes on without them.
            riderPassed = false;
            float fastest = 0f, sideways = 0f;
            for (int i = 0; i < 240; i++)
            {
                Step(1);
                if (Game.Player.Position.x < 10f) continue;
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                sideways = Mathf.Max(sideways, Mathf.Abs(Game.Player.Position.z));
            }
            Debug.Log("heavy props: platform: stopped at " + Game.Player.Position.ToString("F2") + " fastest " + F(fastest) + " sideways " + F(sideways) + " rider passed " + riderPassed
                      + " rider at " + rider.Center.ToString("F2") + " platform at " + platform.Position.x.ToString("F2"));
            Assert.IsFalse(standingPassed, "a heavy block the platform carries the player into is solid for them");
            Assert.Less(Game.Player.Position.x, 11f - Player.BaseRadius + 0.05f, "it stopped the player");
            Assert.Less(Vector3.Distance(standing.Center, new Vector3(12f, 1.6f, 0f)), 0.05f, "test setup: it never moved");
            // The platform then brings the block that rides it up against the stopped player. To the rule that
            // block is at rest (it moves with the player's ground), so here only the squeeze rule stands
            // between the two blocks and the player. It is enough: pressed, not thrown.
            Assert.Less(fastest, 3.5f, "the player never moved faster than the platform carried them");
            Assert.Less(sideways, 0.5f);
        }

        [Test]
        public void AHeavyPropLetGoAtThePlayersFeet_DoesNotTrapThem()
        {
            // Let go round the feet: it passes through them, and they walk out of it.
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                prop = ctx.AddProp(BasicToys.Block(1.5f), new Vector3(0f, 0.75f, 2.2f), new PropOptions { Density = 40f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            LookAt(prop.Center);
            Click();
            Assert.AreSame(prop, Game.Grabber.Held);
            Game.Player.Pitch = -89f;
            Run(5);
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.Greater(prop.Mass, Player.Mass * 2f, "test setup: a heavy block, of scale " + prop.Scale);
            Outcome settle = Watch(1.5f);
            Assert.Less(settle.Nearest, -0.05f, "test setup: it lies round the player's feet");
            Assert.Less(settle.Moved, 0.05f);
            Assert.Less(settle.Fastest, 0.5f);
            Game.Player.Pitch = 0f;
            Input.Hold.MoveZ = 1f;
            Outcome walk = Watch(1f, true);
            Input.Hold = default;
            Assert.Greater(Game.Player.Position.z, 4f, "the player walked out of it");
            Assert.IsTrue(walk.SolidAtEnd, "and then it is solid again");
            End();

            // Let go right in front of the feet, not touching: it is solid at once, and nobody is held by it.
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                prop = ctx.AddProp(BasicToys.Block(1.5f), new Vector3(0f, 0.75f, 2.2f), new PropOptions { Density = 40f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            LookAt(prop.Center);
            Click();
            Game.Player.Pitch = -35f;
            Run(5);
            Click();
            Outcome beside = Watch(1.5f);
            Assert.Greater(prop.Mass, Player.Mass, "test setup: heavier than the player, at scale " + prop.Scale);
            Assert.Greater(beside.Nearest, -0.01f, "test setup: it lies in front of the feet, not on them");
            Assert.Less(beside.Nearest, 0.6f, "test setup: right in front of them");
            Assert.IsFalse(beside.Passed, "a prop that is put down does not come at anybody");
            Assert.Less(beside.Moved, 0.05f);
            foreach (float yaw in new[] { 180f, 90f, -90f })
            {
                Vector3 from = Game.Player.Position;
                Game.Player.Pitch = 0f;
                Game.Player.Yaw = yaw;
                Input.Hold.MoveZ = 1f;
                RunSeconds(0.5f);
                Input.Hold = default;
                Assert.Greater(Vector3.Distance(from, Game.Player.Position), 2f, "walking away from it at yaw " + yaw);
                Game.Player.Teleport(from);
                Run(2);
            }
        }

        [Test]
        public void AHeavyPropThatComesAtAJumpingPlayer_PassesThroughThemToo()
        {
            Launch(Hit.SlideSide, 20f * Player.Mass, 12f);
            Input.Once.Jump = true;
            Outcome o = Watch(2f);
            Assert.IsTrue(o.Passed);
            Assert.Less(o.Fastest, Player.JumpSpeed + 0.01f, "nothing was added to the jump");
            Assert.Less(new Vector2(o.PlayerAtEnd.x, o.PlayerAtEnd.z).magnitude, 0.05f, "they came down where they went up");
        }

        /// <summary>The speeds go with the player's size, like everything else about them: to a player a third the size a prop at 0.6 is fast.</summary>
        [Test]
        public void TheThresholdsScaleWithThePlayer()
        {
            foreach (float scale in new[] { 1f, 0.3f })
            {
                Build(ctx =>
                {
                    TestHelpers.Floor(ctx, 200f);
                    prop = ctx.AddProp(BasicToys.Ball(BallRadius), new Vector3(-(BallRadius + Player.BaseRadius * scale + 0.05f), BallRadius + 0.002f, 0f),
                        Weighing(20f * Player.Mass, 4f / 3f * Mathf.PI));
                    ctx.SetSpawn(Vector3.zero, 0f);
                });
                Assert.IsTrue(Game.Player.SetScale(scale));
                prop.Body.linearVelocity = new Vector3(0.6f, 0f, 0f);
                prop.Body.angularVelocity = new Vector3(0f, 0f, -0.6f / BallRadius);
                Outcome o = Watch(1.5f);
                Debug.Log("heavy props: player scale " + scale + ": a ball at 0.6: fastest " + F(o.Fastest) + " moved " + F(o.Moved) + " passed at " + o.PassedAt + " from " + F(o.PassedGap));
                if (scale < 1f)
                {
                    Assert.IsTrue(o.Passed && o.PassedGap > 0f, "faster than " + PerspectiveGrabber.PassSpeed * scale + ": it passes before it touches");
                    Assert.Less(o.Fastest, 0.05f);
                }
                else
                {
                    Assert.Greater(o.Fastest, 0.3f, "slower than " + PerspectiveGrabber.PassSpeed + ": it is solid and shoves");
                }
                End();
            }
        }

        [Test]
        public void TheRuleIsDeterministic()
        {
            var runs = new List<string>();
            for (int run = 0; run < 2; run++)
            {
                Outcome o = Play(Hit.Topple, 20f * Player.Mass, 6f, 3f);
                runs.Add(o.PassedAt + " " + o.PassedGap.ToString("R") + " " + prop.Position.ToString("R") + " " + prop.Rotation.ToString("R") + " " + Game.Player.Position.ToString("R"));
                End();
            }
            Assert.AreEqual(runs[0], runs[1]);
        }

        // ---- The probe: what a prop does to the player, as a table -------------------------------------------------

        static readonly Hit[] Hits = { Hit.SlideSide, Hit.SlideBehind, Hit.SlideLow, Hit.SlideThin, Hit.WalkInto, Hit.Roll, Hit.FallAbove, Hit.FallEdge, Hit.Topple };
        static readonly float[] ProbeMasses = { 0.5f, 0.9f, 1.5f, 5f, 20f, 200f };
        static readonly float[] ProbeSpeeds = { 1f, 2f, 3f, 6f, 12f, 25f };

        /// <summary>
        /// Not a check: writes tools/out/notes/prelude-engine-fling-probe.txt, the measurements the rule's
        /// thresholds were decided from. Run it with -Filter "HeavyPropTests.Probe".
        /// </summary>
        [Test, Explicit("writes a table of measurements; not a check")]
        public void Probe_WhatAPropDoesToThePlayer()
        {
            var table = new StringBuilder();
            table.AppendLine("kind         mass(x3)  speed  approach  fastest  moved   passed(tick,gap)  nearest  solidAtEnd  propSpeedEnd  playerAtEnd");
            foreach (Hit hit in Hits)
            {
                foreach (float ratio in ProbeMasses)
                    foreach (float speed in ProbeSpeeds)
                    {
                        Outcome o = Play(hit, ratio * Player.Mass, speed, 3f);
                        table.AppendLine(hit.ToString().PadRight(12) + " " + F(ratio, "0.0").PadLeft(7) + "  " + F(speed, "0").PadLeft(5) + "  " + F(o.Approach).PadLeft(8)
                            + "  " + F(o.Fastest).PadLeft(7) + "  " + F(o.Moved).PadLeft(6) + "  "
                            + (o.Passed ? ("yes(" + o.PassedAt + "," + F(o.PassedGap) + ")") : "no").PadRight(16) + "  " + F(o.Nearest).PadLeft(7)
                            + "  " + (o.SolidAtEnd ? "solid" : "ghost").PadRight(10) + "  " + F(o.PropSpeedAtEnd).PadLeft(12) + "  " + o.PlayerAtEnd.ToString("F2"));
                        End();
                    }
                table.AppendLine();
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/out/notes/prelude-engine-fling-probe.txt"));
            File.WriteAllText(path, table.ToString());
            Debug.Log("[Toybox] probe: " + path);
        }
    }
}
