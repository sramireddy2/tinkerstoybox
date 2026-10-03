using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// The player controller against awkward ground: seams between colliders, crests, faces too steep to
    /// stand on, crevices, small steps and loose pebbles, moving and turning platforms, low ceilings.
    /// Each of these was a defect once (M1 review); the assertion messages carry the numbers measured then.
    /// </summary>
    public class PlayerTerrainTests : SimTest
    {
        const float FlatJumpApex = 1.25f;      // measured apex of a jump on level ground
        const float SprintJumpLength = 5.47f;  // measured, matches the contract's "about 5.5"

        static string F(float value) => value.ToString("F3");

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Disposes the current Game (if any) and builds a fresh ad-hoc level, so one test can try several setups.</summary>
        void Rebuild(Action<LevelContext> build, float killY = -30f)
        {
            if (Game != null)
            {
                Game.Dispose();
                Game = null;
            }
            Build(build, killY);
        }

        /// <summary>
        /// A 1 thick box whose top surface starts at `start` and runs `length` toward +Z, tilted by Unity's
        /// Euler X: pitchDegrees &gt; 0 descends toward +Z, &lt; 0 rises.
        /// </summary>
        static GameObject Slope(LevelContext ctx, Vector3 start, float pitchDegrees, float length, float width = 6f)
        {
            Quaternion rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 along = rotation * Vector3.forward;
            Vector3 normal = rotation * Vector3.up;
            Vector3 center = start + along * (length * 0.5f) - normal * 0.5f;
            return TestHelpers.Box(ctx, center, new Vector3(width, 1f, length), rotation);
        }

        /// <summary>One convex collider: a ramp rising toward +Z from (y 0, z 0) that continues as a flat platform.</summary>
        static GameObject RampAndPlatformMesh(LevelContext ctx, float angle, float rampLength, float platformLength, float width)
        {
            float cz = rampLength * Mathf.Cos(angle * Mathf.Deg2Rad), cy = rampLength * Mathf.Sin(angle * Mathf.Deg2Rad);
            var profile = new[]
            {
                new Vector2(0f, -1f), new Vector2(0f, 0f), new Vector2(cz, cy), new Vector2(cz + platformLength, cy), new Vector2(cz + platformLength, -1f),
            };
            int n = profile.Length;
            var vertices = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(-width * 0.5f, profile[i].y, profile[i].x);
                vertices[i + n] = new Vector3(width * 0.5f, profile[i].y, profile[i].x);
            }
            var triangles = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                triangles.AddRange(new[] { 0, i, i + 1 });
                triangles.AddRange(new[] { n, n + i + 1, n + i });
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                triangles.AddRange(new[] { i, j, n + j });
                triangles.AddRange(new[] { i, n + j, n + i });
            }
            var mesh = new Mesh { name = "RampAndPlatform", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            var go = new GameObject("RampAndPlatform");
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
            ctx.OnDispose(() => UnityEngine.Object.DestroyImmediate(mesh));
            return go;
        }

        // ==========================================================================================
        // Seams between flush colliders.
        //
        // PhysX generates a contact against the edge of the NEXT box while the capsule is still on the
        // current one. Its normal leans backward, so part of the walking speed would become upward speed.
        // PlayerContactScaler drops those contacts, and the controller takes back what is left of a bump.
        // ==========================================================================================

        struct Walk
        {
            public float MaxY, MaxVy;
            public int AirTicks, Landings;

            public override string ToString() => "maxY=" + MaxY.ToString("F4") + " maxVy=" + MaxVy.ToString("F2") + " airTicks=" + AirTicks + " PlayerLanded=" + Landings;
        }

        /// <summary>Walks +Z for 4 s over a 60 x 60 floor whose top is at y = 0, made of one slab or of sixty 1-deep boxes.</summary>
        Walk CrossFloor(bool tiled, bool sprint)
        {
            Rebuild(ctx =>
            {
                if (tiled)
                {
                    for (int i = 0; i < 60; i++)
                        TestHelpers.Box(ctx, new Vector3(0f, -0.5f, i + 0.5f), new Vector3(60f, 1f, 1f));
                }
                else
                {
                    TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 30f), new Vector3(60f, 1f, 60f));
                }
                ctx.SetSpawn(new Vector3(0f, 0f, 1.5f), 0f);
            });
            Run(30);
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = sprint;
            RunSeconds(0.5f);
            var walk = new Walk();
            int landings = 0;
            Game.Events.PlayerLanded += e => landings++;
            for (int i = 0; i < TestHelpers.Ticks(4f); i++)
            {
                Game.Tick();
                walk.MaxY = Mathf.Max(walk.MaxY, Game.Player.Position.y);
                walk.MaxVy = Mathf.Max(walk.MaxVy, Game.Player.Velocity.y);
                if (!Game.Player.Grounded) walk.AirTicks++;
            }
            walk.Landings = landings;
            return walk;
        }

        [Test]
        public void MovingAcrossFlushFloorBoxesStaysOnTheFloor()
        {
            Walk slab = CrossFloor(false, true);
            Assert.Less(slab.MaxY, 0.005f, "control: one slab must be perfectly smooth (" + slab + ")");
            Assert.AreEqual(0, slab.AirTicks, "control: one slab must keep the player grounded");

            Walk walking = CrossFloor(true, false);
            Walk sprinting = CrossFloor(true, true);
            string measured = "\n  one slab, sprint:      " + slab + "\n  1x1 tiles, walk:       " + walking + "\n  1x1 tiles, sprint:     " + sprinting;
            Assert.IsTrue(walking.MaxY < 0.01f && sprinting.MaxY < 0.01f && sprinting.AirTicks == 0 && sprinting.Landings == 0 && walking.Landings == 0,
                "A floor made of flush boxes (top faces at exactly the same height) bumps the player at every seam and, sprinting, throws them into the air:" + measured);
        }

        struct Crest
        {
            public float Above, Impact;
            public int AirTicks, Landings;

            public override string ToString() => "peak above the platform=" + Above.ToString("F3") + " airTicks=" + AirTicks + " PlayerLanded=" + Landings + " impact=" + Impact.ToString("F2");
        }

        /// <summary>Up a 6 long ramp and onto an 80 long platform flush with its top; either two boxes or one convex collider.</summary>
        Crest OverCrest(float angle, bool sprint, bool singleCollider)
        {
            float crestZ = 2f + 6f * Mathf.Cos(angle * Mathf.Deg2Rad);
            float crestY = 6f * Mathf.Sin(angle * Mathf.Deg2Rad);
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                if (singleCollider)
                {
                    ctx.AddStatic(RampAndPlatformMesh(ctx, angle, 6f, 80f, 6f), new Vector3(0f, 0f, 2f));
                }
                else
                {
                    TestHelpers.Ramp(ctx, angle, 2f, 6f, 6f);
                    TestHelpers.Box(ctx, new Vector3(0f, crestY - 0.5f, crestZ + 40f), new Vector3(6f, 1f, 80f));
                }
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            });
            Run(20);
            var crest = new Crest();
            int landings = 0;
            float impact = 0f;
            Game.Events.PlayerLanded += e =>
            {
                landings++;
                impact = Mathf.Max(impact, e.ImpactSpeed);
            };
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = sprint;
            for (int i = 0; i < TestHelpers.Ticks(4f); i++)
            {
                Game.Tick();
                Vector3 p = Game.Player.Position;
                if (p.z > 2.5f && !Game.Player.Grounded) crest.AirTicks++;
                if (p.z > crestZ) crest.Above = Mathf.Max(crest.Above, p.y - crestY);
            }
            Assert.Greater(Game.Player.Position.z, crestZ + 3f, "test setup: the player never got onto the platform");
            crest.Landings = landings;
            crest.Impact = impact;
            return crest;
        }

        static bool Smooth(Crest crest) => crest.Landings == 0 && crest.Above < 0.08f;

        [Test]
        public void RampCrestOnOneColliderIsSmooth()
        {
            // The reference: ramp and platform as one collider.
            Crest a = OverCrest(20f, true, true), b = OverCrest(30f, false, true), c = OverCrest(45f, false, true);
            Assert.IsTrue(Smooth(a) && Smooth(b) && Smooth(c), "20 deg sprint: " + a + "\n30 deg walk: " + b + "\n45 deg walk: " + c);
        }

        [Test]
        public void RampCrestOntoAPlatformDoesNotLaunchThePlayer()
        {
            // The ordinary way to build it: a ramp box and a platform box that share the crest edge.
            Crest a = OverCrest(20f, true, false), b = OverCrest(30f, false, false), c = OverCrest(45f, false, false);
            Assert.IsTrue(Smooth(a) && Smooth(b) && Smooth(c),
                "Going over the top of a ramp box onto a flush platform box throws the player into the air (the same geometry as ONE collider is smooth, see RampCrestOnOneColliderIsSmooth):"
                + "\n  20 deg, sprint: " + a + "\n  30 deg, walk:   " + b + "\n  45 deg, walk:   " + c);
        }

        /// <summary>
        /// Ground that drops away a little - a low step down, the far side of a crest, a slope that starts
        /// going down - is held on to: the feet are pulled down onto it and the player never counts as
        /// airborne. A real drop still is one.
        /// </summary>
        [Test]
        public void SmallDropsAreWalkedDownWithoutLeavingTheGround()
        {
            (int airTicks, int landings, float endY) Walk(Action<LevelContext> build, Vector3 spawn, bool sprint, float seconds)
            {
                Rebuild(ctx =>
                {
                    TestHelpers.Floor(ctx, 200f);
                    build(ctx);
                    ctx.SetSpawn(spawn, 0f);
                });
                Run(20);
                Assert.IsTrue(Game.Player.Grounded, "test setup: standing at the start");
                int landings = 0, airTicks = 0;
                Game.Events.PlayerLanded += e => landings++;
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = sprint;
                for (int i = 0; i < TestHelpers.Ticks(seconds); i++)
                {
                    Game.Tick();
                    if (!Game.Player.Grounded) airTicks++;
                }
                return (airTicks, landings, Game.Player.Position.y);
            }

            // A platform 0.15 high that ends at z = 2.
            var down = Walk(ctx => TestHelpers.Box(ctx, new Vector3(0f, 0.075f, -4f), new Vector3(6f, 0.15f, 12f)), new Vector3(0f, 0.15f, -2f), true, 1.5f);
            Assert.AreEqual(0f, down.endY, 0.01f, "test setup: walked off the low platform");
            Assert.IsTrue(down.airTicks == 0 && down.landings == 0, "stepping down 0.15: " + down.airTicks + " ticks in the air, " + down.landings + " landings");

            // A plateau 4 high that continues as a 30 degree slope going down toward +Z, sprinting.
            float run = 4f / Mathf.Tan(30f * Mathf.Deg2Rad);
            var slope = Walk(ctx =>
            {
                TestHelpers.Box(ctx, new Vector3(0f, 2f, -5f), new Vector3(8f, 4f, 10f));
                Slope(ctx, new Vector3(0f, 4f, 0f), 30f, 8f, 8f);
            }, new Vector3(0f, 4f, -3f), true, 0.5f + (3f + run) / Player.SprintSpeed);
            Assert.Less(slope.endY, 1f, "test setup: went down the slope");
            Assert.IsTrue(slope.airTicks == 0 && slope.landings == 0, "sprinting from a plateau onto a 30 degree down slope: " + slope.airTicks + " ticks in the air, " + slope.landings + " landings");

            // Half a unit is a drop: the player falls and lands.
            var drop = Walk(ctx => TestHelpers.Box(ctx, new Vector3(0f, 0.25f, -4f), new Vector3(6f, 0.5f, 12f)), new Vector3(0f, 0.5f, -2f), false, 2f);
            Assert.AreEqual(0f, drop.endY, 0.01f);
            Assert.IsTrue(drop.airTicks > 3 && drop.landings == 1, "walking off a ledge 0.5 high: " + drop.airTicks + " ticks in the air, " + drop.landings + " landings");
        }

        // ==========================================================================================
        // A face steeper than 50 degrees is not ground, and jumping at it must not climb it either.
        //
        // A frictionless capsule turns horizontal speed into speed up the face, and air steering toward
        // the face would keep feeding it. Such a face may stop or turn the player; it never adds height.
        // ==========================================================================================

        float JumpAtRamp(float angle, float jumpAtZ, bool sprint)
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Ramp(ctx, angle, 3f, 14f, 6f);
                ctx.SetSpawn(new Vector3(0f, 0f, -6f), 0f);
            });
            Run(20);
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = sprint;
            bool jumped = false;
            float highest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(6f); i++)
            {
                if (!jumped && Game.Player.Position.z >= jumpAtZ)
                {
                    Input.Once.Jump = true;
                    jumped = true;
                }
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            return highest;
        }

        [Test]
        public void JumpingAtASteepFaceDoesNotClimbIt()
        {
            // The ramp's foot is at z = 3; the jump is pressed 0.3 to 0.6 before it.
            float sprint60 = Mathf.Max(JumpAtRamp(60f, 2.4f, true), JumpAtRamp(60f, 2.7f, true));
            float walk60 = Mathf.Max(JumpAtRamp(60f, 2.4f, false), JumpAtRamp(60f, 2.7f, false));
            float sprint70 = Mathf.Max(JumpAtRamp(70f, 2.4f, true), JumpAtRamp(70f, 2.7f, true));
            string measured = "\n  60 deg, sprint + jump: feet reach y=" + F(sprint60) + "\n  60 deg, walk + jump:   y=" + F(walk60) + "\n  70 deg, sprint + jump: y=" + F(sprint70)
                + "\n  (apex of the same jump on level ground: " + F(FlatJumpApex) + ")";
            float limit = FlatJumpApex * 1.5f;
            Assert.IsTrue(sprint60 < limit && walk60 < limit && sprint70 < limit,
                "A face too steep to walk on can be climbed to two or three times the jump height by jumping at it:" + measured);
        }

        // ==========================================================================================
        // Walking (no jump) into a steep face must not bob up and down and spam PlayerLanded.
        // ==========================================================================================

        [Test]
        public void WalkingIntoASteepFaceDoesNotBounce()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Ramp(ctx, 60f, 3f, 14f, 6f);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            int landings = 0, toggles = 0, airTicks = 0;
            float hardest = 0f, highest = 0f;
            Game.Events.PlayerLanded += e =>
            {
                landings++;
                hardest = Mathf.Max(hardest, e.ImpactSpeed);
            };
            Input.Hold.MoveZ = 1f;
            bool was = Game.Player.Grounded;
            for (int i = 0; i < TestHelpers.Ticks(5f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                if (Game.Player.Grounded != was) toggles++;
                was = Game.Player.Grounded;
                if (!was) airTicks++;
            }
            Assert.IsTrue(landings <= 1 && toggles <= 2,
                "Holding forward against a 60 degree face for 5 s: PlayerLanded fired " + landings + " times, Grounded flipped " + toggles + " times, "
                + airTicks + " of 300 ticks in the air, feet up to y=" + F(highest) + ", hardest impact " + F(hardest));
        }

        // ==========================================================================================
        // Air steering turns the velocity; it must not add speed beyond walk / sprint speed. Otherwise
        // steering sideways lengthens a jump and chained jumps build up speed without limit.
        // Contract: "Longest jump (sprint, level ground) ~ 5.5 units - design gaps >= 9 to be un-jumpable".
        // ==========================================================================================

        [Test]
        public void SteeringInTheAirDoesNotLengthenTheSprintJump()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                ctx.SetSpawn(new Vector3(0f, 0f, -20f), 0f);
            });
            Run(20);
            Player player = Game.Player;
            Vector3 takeoff = Vector3.zero, landing = Vector3.zero;
            bool landed = false;
            Game.Events.PlayerJumped += e => takeoff = e.Position;
            Game.Events.PlayerLanded += e =>
            {
                if (landed) return;
                landed = true;
                landing = e.Position;
            };
            // Sprint forward, jump, then let go of W and hold D (still sprinting) for the whole flight.
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            RunSeconds(1f);
            Input.Once.Jump = true;
            Game.Tick();
            Input.Hold.MoveZ = 0f;
            Input.Hold.MoveX = 1f;
            float topSpeed = 0f;
            for (int i = 0; i < 120 && !landed; i++)
            {
                Game.Tick();
                topSpeed = Mathf.Max(topSpeed, Flat(player.Velocity).magnitude);
            }
            Assert.IsTrue(landed, "test setup: never landed");
            float length = Flat(landing - takeoff).magnitude;
            Assert.IsTrue(length < SprintJumpLength * 1.05f && topSpeed < Player.SprintSpeed * 1.05f,
                "Sprint jump with W released and D held in the air: length " + F(length) + " (straight sprint jump: " + F(SprintJumpLength) + "), horizontal speed reached "
                + F(topSpeed) + " (sprint speed: " + F(Player.SprintSpeed) + ")");
        }

        static readonly Vector2[] Keys =
        {
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(1f, -1f),
            new Vector2(0f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 0f), new Vector2(-1f, 1f),
        };

        [Test]
        public void ChainedJumpsCannotCrossANineUnitGap()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx, 600f);
                ctx.SetSpawn(new Vector3(0f, 0f, -250f), 0f);
            });
            Run(20);
            Player player = Game.Player;
            Vector3 takeoff = player.Position;
            float longest = 0f, topSpeed = 0f;
            string lengths = "";
            Game.Events.PlayerJumped += e => takeoff = e.Position;
            Game.Events.PlayerLanded += e =>
            {
                float d = Flat(e.Position - takeoff).magnitude;
                longest = Mathf.Max(longest, d);
                lengths += d.ToString("F1") + " ";
            };

            // Keyboard only, the view never turns: sprint for a second, then jump on every landing and each
            // tick hold whichever of the eight WASD combinations gains the most speed.
            bool lastJump = false;
            Input.Script = tick =>
            {
                var frame = new InputFrame { Sprint = true };
                if (tick < 60)
                {
                    frame.MoveZ = 1f;
                    return frame;
                }
                Vector3 v = Flat(player.Velocity);
                float best = -1f;
                Vector2 bestKey = Keys[0];
                foreach (Vector2 key in Keys)
                {
                    Vector3 d = new Vector3(key.x, 0f, key.y).normalized;
                    float result = player.Grounded
                        ? Vector3.MoveTowards(v, d * Player.SprintSpeed, 70f * Sim.Dt).magnitude
                        : (v + d * Mathf.Clamp(Player.SprintSpeed - Vector3.Dot(v, d), 0f, 14f * Sim.Dt)).magnitude;
                    if (result > best)
                    {
                        best = result;
                        bestKey = key;
                    }
                }
                frame.MoveX = bestKey.x;
                frame.MoveZ = bestKey.y;
                frame.Jump = player.Grounded && !lastJump;
                lastJump = frame.Jump;
                return frame;
            };
            for (int i = 0; i < TestHelpers.Ticks(8f); i++)
            {
                Game.Tick();
                topSpeed = Mathf.Max(topSpeed, Flat(player.Velocity).magnitude);
            }
            Assert.IsTrue(longest < 9f,
                "Chained jumps with air strafing (keys only, fixed view) outrun the contract's un-jumpable gap of 9: jump lengths " + lengths
                + "- longest " + F(longest) + ", top horizontal speed " + F(topSpeed) + " (sprint speed " + F(Player.SprintSpeed) + ")");
        }

        // ==========================================================================================
        // Resting between two faces that are each too steep to be ground: the pair carries the player, so
        // they count as standing and can jump. (Otherwise only a restart would get them out.)
        // ==========================================================================================

        [Test]
        public void CanJumpOutOfASteepCrevice()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                // Two 60 degree faces forming a V whose bottom line is at z = 0, y = 0, closed at both ends.
                Slope(ctx, Vector3.zero, -60f, 8f, 4f);
                Quaternion mirrored = Quaternion.Euler(60f, 0f, 0f);
                TestHelpers.Box(ctx, mirrored * new Vector3(0f, -0.5f, -4f), new Vector3(4f, 1f, 8f), mirrored);
                TestHelpers.Box(ctx, new Vector3(2.5f, 4f, 0f), new Vector3(1f, 8f, 12f));
                TestHelpers.Box(ctx, new Vector3(-2.5f, 4f, 0f), new Vector3(1f, 8f, 12f));
                ctx.SetSpawn(new Vector3(0f, 1.5f, 0f), 0f);
            });
            RunSeconds(1.5f);
            Vector3 rest = Game.Player.Position;
            bool groundedAtRest = Game.Player.Grounded;
            Assert.Less(Game.Player.Velocity.magnitude, 0.05f, "test setup: the player should be at rest in the crevice");

            int jumps = 0;
            Game.Events.PlayerJumped += e => jumps++;
            float highest = rest.y;
            Input.Hold.MoveZ = 1f;
            for (int i = 0; i < TestHelpers.Ticks(4f); i++)
            {
                if (i % 20 == 0) Input.Once.Jump = true;
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.IsTrue(jumps > 0,
                "At rest in a V of two 60 degree faces (feet at " + rest.ToString("F2") + ", Grounded=" + groundedAtRest + "): 12 jump presses over 4 s produced " + jumps
                + " jumps, the feet never rose above y=" + F(highest) + ". The player is stuck until the level is restarted.");
        }

        // ==========================================================================================
        // Riding a rotating Mover: with the tangent velocity of the contact point every tick would end
        // slightly outside the circle and the rider would spiral outward. Mover.PointVelocity gives the
        // chord of the arc instead.
        // ==========================================================================================

        [Test]
        public void RiderStaysPutOnATurntable()
        {
            const float degreesPerSecond = 90f;
            Mover mover = null;
            float angle = 0f;
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(16f, 0.5f, 16f)), new Vector3(0f, 0.75f, 0f));
                ctx.OnUpdate(dt =>
                {
                    angle += degreesPerSecond * dt;
                    mover.MoveTo(new Vector3(0f, 0.75f, 0f), Quaternion.Euler(0f, angle, 0f));
                });
                ctx.SetSpawn(new Vector3(2f, 1.02f, 0f), 0f);
            });
            RunSeconds(10f);
            Assert.IsTrue(Game.Player.Grounded, "test setup: the player should be standing on the turntable");
            float radius = Flat(Game.Player.Position).magnitude;
            Vector3 glued = Quaternion.Euler(0f, angle, 0f) * new Vector3(2f, 0f, 0f);
            float offset = Flat(Game.Player.Position - glued).magnitude;
            Assert.IsTrue(Mathf.Abs(radius - 2f) < 0.1f,
                "Standing still for 10 s at radius 2 on a platform turning at 90 deg/s: radius is now " + F(radius) + ", " + F(offset)
                + " away from the spot on the platform the player started on (measured in an earlier run: 2.105 at 45 deg/s, 4.53 at 180 deg/s)");
        }

        // ==========================================================================================
        // Vertical Movers: an elevator that starts, stops or reverses keeps its rider (up to
        // Player.MoverGripSpeed of sudden change); a piston that stops after rising fast throws them.
        // ==========================================================================================

        struct Ride
        {
            public int AirTicks;
            public float Gap;
        }

        [Test]
        public void RiderStaysOnAnElevatorThatChangesSpeed()
        {
            Mover mover = null;
            // High above the floor, so the platform never reaches it while descending.
            var target = new Vector3(0f, 10.75f, 0f);
            Vector3 velocity = Vector3.zero;
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(4f, 0.5f, 4f)), target);
                ctx.OnUpdate(dt =>
                {
                    target += velocity * dt;
                    mover.MoveTo(target);
                });
                ctx.SetSpawn(new Vector3(0f, 11.02f, 0f), 0f);
            });
            Run(30);
            Assert.IsTrue(Game.Player.Grounded);
            int landings = 0;
            Game.Events.PlayerLanded += e => landings++;

            Ride Phase(Vector3 v, float seconds)
            {
                velocity = v;
                var ride = new Ride();
                for (int i = 0; i < TestHelpers.Ticks(seconds); i++)
                {
                    Game.Tick();
                    if (!Game.Player.Grounded) ride.AirTicks++;
                    ride.Gap = Mathf.Max(ride.Gap, Game.Player.Position.y - (mover.Position.y + 0.25f));
                }
                return ride;
            }

            Ride up = Phase(Vector3.up * 3f, 1f);
            Ride stop = Phase(Vector3.zero, 1f);
            Ride down = Phase(Vector3.down * 5f, 1f);
            Phase(Vector3.zero, 1f);
            Assert.AreEqual(0, up.AirTicks, "riding up at a steady 3 u/s should work");
            Assert.IsTrue(down.AirTicks <= 2 && stop.AirTicks <= 2,
                "An elevator that starts descending at 5 u/s leaves the rider hanging for " + down.AirTicks + " ticks (gap up to " + F(down.Gap)
                + "); one that stops after rising at 3 u/s tosses the rider " + F(stop.Gap) + " into the air for " + stop.AirTicks + " ticks. PlayerLanded fired "
                + landings + " times during the ride.");
        }

        // ==========================================================================================
        // SetScale checks for room. Growing where the new capsule does not fit would leave it overlapping
        // floor and ceiling, and the solver would settle it THROUGH the floor.
        // ==========================================================================================

        [Test]
        public void GrowingUnderALowCeilingDoesNotPushThePlayerThroughTheFloor()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx, 100f);
                // Underside of the ceiling at y = 4; the player at scale 4 is 6.8 tall.
                TestHelpers.Box(ctx, new Vector3(0f, 4.5f, 0f), new Vector3(20f, 1f, 20f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }, -50f);
            Run(20);
            Assert.IsTrue(Game.Player.Grounded);
            bool grown = Game.Player.SetScale(4f);
            Assert.IsFalse(grown, "SetScale must refuse a size there is no room for");
            Assert.AreEqual(1f, Game.Player.Scale, "a refused SetScale leaves the player as they were");
            Assert.AreEqual(Player.BaseHeight, Game.Player.Collider.height, 1e-6f);
            Assert.IsTrue(Game.Player.Grounded);
            // Half that size fits under the ceiling (3.4 tall), and growing next to a wall just moves over.
            Assert.IsTrue(Game.Player.SetScale(2f), "there is room for scale 2");
            Assert.AreEqual(2f, Game.Player.Scale);
            Assert.IsTrue(Game.Player.SetScale(1f));
            Assert.IsFalse(Game.Player.SetScale(4f));
            float lowest = 0f;
            for (int i = 0; i < 300; i++)
            {
                Game.Tick();
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
            }
            Assert.IsTrue(lowest > -0.1f,
                "SetScale(4) under a ceiling 4 units up (the capsule becomes 6.8 tall): the feet were pushed to y=" + F(lowest) + ", through the 1 unit thick floor (top at y=0). After 5 s the feet are at y="
                + F(Game.Player.Position.y) + ", Scale=" + Game.Player.Scale + ", Grounded=" + Game.Player.Grounded);
        }

        // ==========================================================================================
        // Small things underfoot.
        // ==========================================================================================

        /// <summary>
        /// Props a few centimetres across are what the mechanic leaves behind whenever something grabbed from
        /// afar is put down near the feet. They are too light to carry the player: the foot kicks them along
        /// instead of climbing them, and walking into one never throws the player into the air.
        /// </summary>
        [TestCase(0.02f)]
        [TestCase(0.06f)]
        [TestCase(0.12f)]
        [TestCase(0.16f)]
        [TestCase(0.2f)]
        [TestCase(0.3f)]
        [TestCase(0.5f)]
        public void WalkingOverATinyPropDoesNotThrowThePlayerUp(float scale)
        {
            Prop pebble = null;
            var landings = new List<PlayerLandEvent>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                pebble = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f * scale, 3f), new PropOptions { Scale = scale });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.PlayerLanded += landings.Add;
            Run(30);
            float size = 0.5f * scale;
            Input.Hold.MoveZ = 1f;
            float highest = 0f, fastestUp = 0f;
            int airborne = 0;
            for (int i = 0; i < 90; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                fastestUp = Mathf.Max(fastestUp, Game.Player.Velocity.y);
                if (!Game.Player.Grounded) airborne++;
            }
            Input.Hold = default;
            string impacts = "";
            foreach (PlayerLandEvent landing in landings) impacts += " " + landing.ImpactSpeed.ToString("0.00");
            Debug.Log("pebble " + size.ToString("0.000") + " (mass " + pebble.Mass + "): player rose to " + highest.ToString("0.000") + ", up to " + fastestUp.ToString("0.00") + " u/s upward, "
                + airborne + " ticks off the ground, landings:" + impacts + "; player z " + Game.Player.Position.z.ToString("0.0") + " pebble " + pebble.Center.ToString("F2"));
            Assert.Less(highest, size + 0.04f, "walking over a prop " + size.ToString("0.000") + " high lifted the player " + highest.ToString("0.000"));
            Assert.AreEqual(0, airborne, "the player left the ground");
            Assert.IsEmpty(landings);
            Assert.Greater(Game.Player.Position.z, 6.5f, "the pebble stopped the player");
        }

        /// <summary>
        /// A static step: up to about a tenth of a unit (a third of the capsule's radius) the foot rolls over
        /// its lip and comes down on the other side without ever leaving the ground; higher than that it is
        /// a wall, and nothing lifts the player over it.
        /// </summary>
        [TestCase(0.03f, true)]
        [TestCase(0.06f, true)]
        [TestCase(0.1f, true)]
        [TestCase(0.15f, false)]
        [TestCase(0.25f, false)]
        public void LowStaticStepsAreWalkedOverAndHigherOnesBlock(float height, bool passable)
        {
            var landings = new List<PlayerLandEvent>();
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, height * 0.5f, 3f), new Vector3(height, height, height));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.PlayerLanded += landings.Add;
            Run(30);
            Input.Hold.MoveZ = 1f;
            float highest = 0f;
            int airborne = 0;
            for (int i = 0; i < 90; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                if (!Game.Player.Grounded) airborne++;
            }
            Assert.AreEqual(0, airborne, "a step " + height + " high made the player leave the ground");
            Assert.IsEmpty(landings, "a step " + height + " high caused a landing");
            if (passable)
            {
                Assert.Greater(Game.Player.Position.z, 6.5f, "a step " + height + " high stopped the player");
                Assert.Less(highest, height + 0.04f, "going over a step " + height + " high lifted the feet to " + F(highest));
                Assert.AreEqual(0f, Game.Player.Position.y, 0.01f, "back on the floor behind the step");
            }
            else
            {
                Assert.Less(Game.Player.Position.z, 3f, "a step " + height + " high is too high to walk over");
                Assert.Less(highest, 0.03f, "the step lifted the player although it is too high to climb");
            }
        }

        // ==========================================================================================
        // Things that held up under the same review and must keep doing so.
        // ==========================================================================================

        [Test]
        public void IdleOnWalkableSlopesAtEveryScale()
        {
            foreach (float scale in new[] { 0.25f, 1f, 4f })
            {
                foreach (float angle in new[] { 45f, 49f })
                {
                    float sin = Mathf.Sin(angle * Mathf.Deg2Rad), cos = Mathf.Cos(angle * Mathf.Deg2Rad);
                    Rebuild(ctx =>
                    {
                        TestHelpers.Floor(ctx);
                        TestHelpers.Ramp(ctx, angle, 2f, 30f, 20f);
                        ctx.SetSpawn(new Vector3(0f, 12f * sin + 0.02f, 2f + 12f * cos), 30f);
                    });
                    Game.Player.SetScale(scale);
                    Run(40);
                    Assert.IsTrue(Game.Player.Grounded, "scale " + scale + " on " + angle + " degrees: not grounded");
                    Vector3 start = Game.Player.Position;
                    for (int i = 0; i < TestHelpers.Ticks(5f); i++)
                    {
                        Game.Tick();
                        Assert.IsTrue(Game.Player.Grounded, "scale " + scale + " on " + angle + " degrees: lost the ground");
                    }
                    Assert.Less(Vector3.Distance(start, Game.Player.Position), 0.01f, "scale " + scale + " on " + angle + " degrees: slid");
                }
            }
        }

        [Test]
        public void ScaleKeepsTheFeetOnTheFloor()
        {
            foreach (float scale in new[] { 0.25f, 4f })
            {
                Rebuild(ctx =>
                {
                    TestHelpers.Floor(ctx, 400f);
                    ctx.SetSpawn(new Vector3(0f, 0f, -150f), 0f);
                });
                Run(20);
                Game.Player.SetScale(scale);
                Assert.IsTrue(Game.Player.Grounded, "grounded right after SetScale(" + scale + ")");
                for (int i = 0; i < 60; i++)
                {
                    Game.Tick();
                    Assert.AreEqual(0f, Game.Player.Position.y, 0.005f, "the feet moved after SetScale(" + scale + ")");
                }
                Assert.AreEqual(Player.BaseEyeHeight * scale, Game.Player.Eye.y - Game.Player.Position.y, 1e-4f);

                Input.Hold.MoveZ = 1f;
                RunSeconds(0.5f);
                Vector3 from = Game.Player.Position;
                RunSeconds(1f);
                Assert.AreEqual(Player.WalkSpeed * scale, Vector3.Distance(from, Game.Player.Position), 0.05f * Player.WalkSpeed * scale);
                Input.Hold = default;
                RunSeconds(0.5f);

                float highest = 0f;
                Input.Once.Jump = true;
                for (int i = 0; i < TestHelpers.Ticks(3f); i++)
                {
                    Game.Tick();
                    highest = Mathf.Max(highest, Game.Player.Position.y);
                }
                Assert.AreEqual(FlatJumpApex * scale, highest, 0.1f * FlatJumpApex * scale, "apex at scale " + scale);
            }
        }

        [Test]
        public void TeleportAndRespawnStopThePlayer()
        {
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            }, -10f);
            Run(20);
            Player player = Game.Player;
            int jumps = 0, respawns = 0;
            Game.Events.PlayerJumped += e => jumps++;
            Game.Events.PlayerRespawned += e => respawns++;

            // Mid-jump, at sprint speed, with another jump press buffered.
            Input.Hold.MoveX = 1f;
            Input.Hold.Sprint = true;
            Input.Once.Jump = true;
            Run(10);
            Input.Hold = default;
            Input.Once.Jump = true;
            Game.Tick();
            player.Teleport(new Vector3(-10f, 0f, 5f));
            Assert.AreEqual(Vector3.zero, player.Velocity);
            Assert.IsTrue(player.Grounded, "Teleport onto the floor should be grounded at once");
            Run(10);
            Assert.Less(Vector3.Distance(new Vector3(-10f, 0f, 5f), player.Position), 0.01f);
            Assert.AreEqual(1, jumps, "the buffered jump must not fire after a teleport");

            player.Teleport(new Vector3(60f, 5f, 0f));
            player.SetVelocity(new Vector3(9f, 0f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns > 0, 5f), "never respawned");
            Assert.AreEqual(Vector3.zero, player.Velocity);
            Run(30);
            Assert.Less(Vector3.Distance(Vector3.zero, player.Position), 0.01f);

            player.AddLook(0f, 1000f);
            Assert.AreEqual(Player.MaxPitch, player.Pitch);
            player.AddLook(123456f, -5000f);
            Assert.AreEqual(-Player.MaxPitch, player.Pitch);
            Assert.IsTrue(player.Yaw >= -180f && player.Yaw <= 180f);
            Assert.AreEqual(1f, player.Forward.magnitude, 1e-4f);
        }

        [Test]
        public void LandingImpactAndThinFloors()
        {
            // A 5 cm thick floor, fallen onto from 10 and from 80 units.
            foreach (float height in new[] { 10f, 80f })
            {
                Rebuild(ctx =>
                {
                    TestHelpers.Box(ctx, new Vector3(0f, -0.025f, 0f), new Vector3(20f, 0.05f, 20f));
                    ctx.SetSpawn(new Vector3(0f, height, 0f), 0f);
                }, -200f);
                var impacts = new List<float>();
                Game.Events.PlayerLanded += e => impacts.Add(e.ImpactSpeed);
                float lowest = height;
                for (int i = 0; i < TestHelpers.Ticks(6f); i++)
                {
                    Game.Tick();
                    lowest = Mathf.Min(lowest, Game.Player.Position.y);
                }
                Assert.AreEqual(1, impacts.Count, "exactly one PlayerLanded after a fall of " + height);
                Assert.AreEqual(Mathf.Sqrt(2f * Toybox.Engine.Game.Gravity * height), impacts[0], 0.6f, "impact speed after a fall of " + height);
                Assert.Greater(lowest, -0.15f, "fell into the floor");
                Assert.AreEqual(0f, Game.Player.Position.y, 0.01f);
            }
        }

        [Test]
        public void AKinematicPistonLaunchesThePlayer()
        {
            Mover mover = null;
            var target = new Vector3(0f, 0.75f, 0f);
            Vector3 velocity = Vector3.zero;
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                mover = ctx.AddKinematic(BasicToys.Slab(new Vector3(4f, 0.5f, 4f)), target);
                ctx.OnUpdate(dt =>
                {
                    target += velocity * dt;
                    mover.MoveTo(target);
                });
                ctx.SetSpawn(new Vector3(0f, 1.02f, 0f), 0f);
            });
            Run(30);
            velocity = Vector3.up * 12f;
            Run(6);
            velocity = Vector3.zero;
            float highest = 0f;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y - (mover.Position.y + 0.25f));
            }
            // 12 u/s against gravity 22 is 3.27 units.
            Assert.AreEqual(3.27f, highest, 0.3f, "the launch speed should persist once airborne");
        }

        [Test]
        public void WalkingOffALedgeWhileHoldingAProp()
        {
            Prop block = null;
            Rebuild(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 1.5f, -5f), new Vector3(6f, 3f, 10f));
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 10f));
                ctx.SetSpawn(new Vector3(0f, 3f, -3f), 0f);
            });
            Run(30);
            LookAt(block.Center);
            Click();
            Assert.AreSame(block, Game.Grabber.Held);
            Input.Hold.MoveZ = 1f;
            float highest = 0f;
            int landings = 0;
            Game.Events.PlayerLanded += e => landings++;
            for (int i = 0; i < TestHelpers.Ticks(2f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                Assert.AreNotSame(block, Game.Player.GroundProp, "the held prop must never be ground");
            }
            Assert.AreSame(block, Game.Grabber.Held);
            Assert.Less(highest, 3.02f, "the held prop pushed the player up");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.02f, "should be standing on the lower floor");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(1, landings);
        }
    }
}
