using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    public class PlayerTests : SimTest
    {
        void BuildFloor(System.Action<LevelContext> extra = null, float killY = -30f)
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
                extra?.Invoke(ctx);
            }, killY);
            Run(20);
        }

        float SpeedOverOneSecond()
        {
            Vector3 from = Game.Player.Position;
            RunSeconds(1f);
            return Vector3.Distance(from, Game.Player.Position);
        }

        [Test]
        public void MetricsMatchTheContract()
        {
            BuildFloor();
            Player player = Game.Player;
            Assert.AreEqual(0.3f, player.Collider.radius, 1e-6f);
            Assert.AreEqual(1.7f, player.Collider.height, 1e-6f);
            Assert.AreEqual(3f, player.Body.mass, 1e-6f);
            Assert.AreEqual(1.55f, player.Eye.y - player.Position.y, 1e-5f);
            Assert.AreEqual(new Vector3(0f, -22f, 0f), Physics.gravity);
            Assert.IsTrue(player.Grounded);
            Assert.AreEqual(0f, player.Position.y, 0.02f, "position is the feet");
        }

        [Test]
        public void WalksAtFiveUnitsPerSecond()
        {
            BuildFloor();
            Input.Hold.MoveZ = 1f;
            RunSeconds(0.5f);
            Assert.AreEqual(5f, SpeedOverOneSecond(), 0.1f);
            Assert.Greater(Game.Player.Position.z, 5f, "yaw 0 walks toward +Z");
        }

        [Test]
        public void SprintsAtEightUnitsPerSecond()
        {
            BuildFloor();
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            RunSeconds(0.5f);
            Assert.AreEqual(8f, SpeedOverOneSecond(), 0.15f);
        }

        [Test]
        public void StopsQuicklyWhenInputEnds()
        {
            BuildFloor();
            Input.Hold.MoveX = 1f;
            RunSeconds(1f);
            Assert.Greater(Game.Player.Position.x, 3f, "positive MoveX strafes toward +X at yaw 0");
            Input.Hold = default;
            RunSeconds(0.25f);
            Assert.Less(Game.Player.Velocity.magnitude, 0.05f);
        }

        [Test]
        public void JumpApexIsAboutOnePointThree()
        {
            BuildFloor();
            int jumped = 0;
            var landings = new List<PlayerLandEvent>();
            Game.Events.PlayerJumped += e => jumped++;
            Game.Events.PlayerLanded += landings.Add;

            float ground = Game.Player.Position.y;
            float highest = ground;
            Input.Once.Jump = true;
            for (int i = 0; i < TestHelpers.Ticks(1.5f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.AreEqual(1.3f, highest - ground, 0.13f);
            Assert.AreEqual(1, jumped);
            Assert.AreEqual(1, landings.Count);
            Assert.AreEqual(Player.JumpSpeed, landings[0].ImpactSpeed, 1f);
            Assert.IsNull(landings[0].Prop);
            Assert.IsNotNull(landings[0].Ground);
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void HoldingJumpDoesNotBunnyHop()
        {
            BuildFloor();
            int jumped = 0;
            Game.Events.PlayerJumped += e => jumped++;
            Input.Hold.Jump = true;
            RunSeconds(2f);
            Assert.AreEqual(1, jumped);
        }

        [Test]
        public void JumpBufferAndCoyoteTime()
        {
            // A ledge 3 units up; walking off it and pressing jump just after leaving still jumps.
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 1.5f, 0f), new Vector3(4f, 3f, 4f));
                ctx.SetSpawn(new Vector3(0f, 3.02f, 0f), 0f);
            });
            Run(20);
            int jumped = 0;
            Game.Events.PlayerJumped += e => jumped++;
            Input.Hold.MoveZ = 1f;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !Game.Player.Grounded, 2f), "never left the ledge");
            Run(3);
            Input.Once.Jump = true;
            Game.Tick();
            Assert.AreEqual(1, jumped, "coyote time should allow a jump a few ticks after leaving the ledge");

            // Pressing jump shortly before touching down jumps again on landing.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Velocity.y < 0f && Game.Player.Position.y < 0.6f, 3f));
            Input.Once.Jump = true;
            RunSeconds(0.3f);
            Assert.AreEqual(2, jumped, "a jump pressed just before landing should be buffered");
        }

        [Test]
        public void StandsStillOnAThirtyDegreeSlope()
        {
            float sin = Mathf.Sin(30f * Mathf.Deg2Rad), cos = Mathf.Cos(30f * Mathf.Deg2Rad);
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Ramp(ctx, 30f, 2f);
                ctx.SetSpawn(new Vector3(0f, 5f * sin + 0.05f, 2f + 5f * cos), 0f);
            });
            Run(30);
            Assert.IsTrue(Game.Player.Grounded, "should be standing on the slope");
            Assert.AreEqual(cos, Game.Player.GroundNormal.y, 0.01f);
            Vector3 start = Game.Player.Position;
            RunSeconds(3f);
            Assert.Less(Vector3.Distance(start, Game.Player.Position), 0.05f, "the player slid on a walkable slope");
        }

        [Test]
        public void ClimbsAFortyDegreeRamp()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Ramp(ctx, 40f, 3f);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Input.Hold.MoveZ = 1f;
            RunSeconds(3f);
            Assert.Greater(Game.Player.Position.y, 3f, "a 40 degree ramp is walkable");
        }

        [Test]
        public void CannotClimbASixtyDegreeRamp()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Ramp(ctx, 60f, 3f);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            float highest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(4f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.Less(highest, 1.2f, "a 60 degree ramp must not be climbable");
            Assert.Less(Game.Player.Position.y, 0.6f, "the player should end up back at the foot of the ramp");
        }

        [Test]
        public void SlidesAlongAWallWithoutSticking()
        {
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(3.5f, 2f, 0f), new Vector3(1f, 4f, 60f));
                ctx.SetSpawn(new Vector3(0f, 0f, -10f), 45f);
            });
            Run(20);
            Input.Hold.MoveZ = 1f;
            RunSeconds(1.5f);
            Assert.AreEqual(2.7f, Game.Player.Position.x, 0.05f, "should be pressed against the wall by now");
            float z = Game.Player.Position.z;
            RunSeconds(1f);
            // Walking at 45 degrees into the wall keeps the along-wall half of the motion: 5 * cos(45).
            Assert.AreEqual(5f * Mathf.Cos(45f * Mathf.Deg2Rad), Game.Player.Position.z - z, 0.25f);
            Assert.AreEqual(2.7f, Game.Player.Position.x, 0.05f);

            // And in the air: jumping against the wall must not hang on it.
            Input.Hold = default;
            Input.Hold.MoveX = 1f;
            Game.Player.Yaw = 0f;
            Input.Once.Jump = true;
            RunSeconds(1.5f);
            Assert.IsTrue(Game.Player.Grounded, "the player stuck to the wall instead of coming back down");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.05f);
        }

        [Test]
        public void RidesAKinematicPlatform()
        {
            Mover mover = null;
            var target = new Vector3(0f, 0.75f, 0f);
            Vector3 velocity = Vector3.zero;
            Build(ctx =>
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
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreSame(mover.GameObject.GetComponent<Collider>(), Game.Player.GroundCollider);

            void AssertRiding(string phase)
            {
                Vector3 p = Game.Player.Position;
                Assert.IsTrue(Game.Player.Grounded, phase + ": lost the platform");
                Assert.AreEqual(mover.Position.x, p.x, 0.15f, phase + ": slid off sideways");
                Assert.AreEqual(mover.Position.y + 0.25f, p.y, 0.05f, phase + ": not standing on the platform");
            }

            velocity = new Vector3(2f, 0f, 0f);
            RunSeconds(3f);
            Assert.AreEqual(6f, mover.Position.x, 0.05f);
            Assert.AreEqual(2f, mover.Velocity.x, 1e-3f);
            AssertRiding("sideways");

            velocity = new Vector3(0f, 1.5f, 0f);
            RunSeconds(1f);
            AssertRiding("up");

            velocity = new Vector3(0f, -1.5f, 0f);
            for (int i = 0; i < TestHelpers.Ticks(1f); i++)
            {
                Game.Tick();
                if (i > 6) AssertRiding("down");
            }

            velocity = Vector3.zero;
            RunSeconds(0.5f);
            AssertRiding("stopped");
            Assert.Less(Game.Player.Velocity.magnitude, 0.1f);
        }

        [Test]
        public void IsThrownUpwardByAFastRisingDynamicBody()
        {
            Prop launcher = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                launcher = ctx.AddProp(BasicToys.Block(new Vector3(3f, 0.5f, 3f)), new Vector3(0f, 0.25f, 0f),
                    new PropOptions { Density = 20f, Grabbable = false });
                // Two bars catch the rim of the block 1.25 units up; the middle, where the player stands, is open.
                TestHelpers.Box(ctx, new Vector3(1.75f, 2f, 0f), new Vector3(1f, 0.5f, 3f));
                TestHelpers.Box(ctx, new Vector3(-1.75f, 2f, 0f), new Vector3(1f, 0.5f, 3f));
                ctx.SetSpawn(new Vector3(0f, 0.55f, 0f), 0f);
            });
            Run(30);
            Assert.AreSame(launcher, Game.Player.GroundProp);
            float rest = Game.Player.Position.y;

            launcher.Body.AddForce(Vector3.up * 12f, ForceMode.VelocityChange);
            float highest = rest;
            for (int i = 0; i < TestHelpers.Ticks(2f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.Less(launcher.Position.y, 1.6f, "the bars should have stopped the block");
            Assert.Greater(highest - rest, 2.5f, "the player should have been flung well above where the block stopped");
        }

        [Test]
        public void BouncesOnABouncySurface()
        {
            // A trampoline: restitution has to survive the controller's ground handling.
            var bouncy = new PhysicsMaterial("Trampoline") { bounciness = 0.8f, bounceCombine = PhysicsMaterialCombine.Maximum };
            try
            {
                Build(ctx =>
                {
                    TestHelpers.Floor(ctx);
                    GameObject pad = TestHelpers.Box(ctx, new Vector3(0f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f));
                    pad.GetComponent<Collider>().sharedMaterial = bouncy;
                    ctx.SetSpawn(new Vector3(0f, 3.5f, 0f), 0f);
                });
                int landings = 0;
                Game.Events.PlayerLanded += e => landings++;
                Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Velocity.y > 1f, 2f), "never bounced");
                float highest = 0f;
                for (int i = 0; i < TestHelpers.Ticks(1f); i++)
                {
                    Game.Tick();
                    highest = Mathf.Max(highest, Game.Player.Position.y);
                }
                // Falling 3 units and keeping 0.8 of the speed gives 0.64 of the height back.
                Assert.Greater(highest, 0.5f + 1.4f, "the bounce was swallowed");
                Assert.Less(highest, 0.5f + 2.2f);
            }
            finally
            {
                Object.DestroyImmediate(bouncy);
            }
        }

        [Test]
        public void ASteadyPushCarriesThePlayer()
        {
            Vector3 wind = Vector3.zero;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
                ctx.OnUpdate(dt => ctx.Game.Player.AddPush(wind));
            });
            Run(20);

            // On the ground a sideways push of 20 becomes a drift of 5 that walking can just cancel.
            wind = new Vector3(20f, 0f, 0f);
            RunSeconds(0.5f);
            Assert.AreEqual(5f, Game.Player.Velocity.x, 0.1f);
            Assert.IsTrue(Game.Player.Grounded);
            Input.Hold.MoveX = -1f;
            RunSeconds(0.5f);
            Assert.AreEqual(0f, Game.Player.Velocity.x, 0.1f);
            Input.Hold = default;

            // Without the push the player stops again.
            wind = Vector3.zero;
            RunSeconds(0.5f);
            Assert.Less(Game.Player.Velocity.magnitude, 0.05f);

            // An updraft stronger than gravity lifts the player off, and in the air a push accelerates.
            wind = new Vector3(0f, 40f, 0f);
            float ground = Game.Player.Position.y;
            RunSeconds(1f);
            Assert.IsFalse(Game.Player.Grounded);
            Assert.AreEqual(0.5f * 18f, Game.Player.Position.y - ground, 1.5f, "40 up against 22 down for one second");
            wind = new Vector3(6f, 0f, 0f);
            float vx = Game.Player.Velocity.x;
            Run(30);
            Assert.AreEqual(vx + 3f, Game.Player.Velocity.x, 0.2f);
        }

        [Test]
        public void RespawnsAtTheCheckpointBelowKillY()
        {
            var respawns = new List<PlayerRespawnEvent>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 10f);
                ctx.SetSpawn(new Vector3(1f, 0f, 2f), 90f);
            }, killY: -5f);
            Game.Events.PlayerRespawned += respawns.Add;
            Run(10);

            Game.Player.Teleport(new Vector3(30f, 0f, 0f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns.Count > 0, 3f), "never respawned");
            Assert.AreEqual(1, respawns.Count);
            Assert.Less(respawns[0].From.y, -5f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 2f), Game.Player.Position), 0.05f);
            Assert.AreEqual(90f, Game.Player.Yaw, 1e-3f);
            Run(20);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(1, respawns.Count);
        }

        [Test]
        public void PropBelowKillYRespawnsAtItsOriginalPoseAndScale()
        {
            Prop block = null;
            var position = new Vector3(2f, 0.4f, 3f);
            Quaternion rotation = Quaternion.Euler(0f, 30f, 0f);
            var respawned = new List<Prop>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 10f);
                block = ctx.AddProp(BasicToys.Block(0.8f), position, rotation, new PropOptions { Scale = 1f });
            }, killY: -5f);
            Game.Events.PropRespawned += e => respawned.Add(e.Prop);
            Run(30);

            block.SetScale(2.5f);
            block.SetPose(new Vector3(30f, 2f, 0f), Quaternion.Euler(40f, 10f, 70f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned.Count > 0, 3f), "never respawned");
            Assert.AreSame(block, respawned[0]);
            Assert.AreEqual(1f, block.Scale, 1e-6f);
            Assert.AreEqual(block.Density * block.BaseVolume, block.Mass, 1e-4f);
            Assert.Less(Vector3.Distance(position, block.Position), 0.02f);
            Assert.Less(Quaternion.Angle(rotation, block.Rotation), 0.5f);
            Run(30);
            Assert.AreEqual(1, respawned.Count);
            Assert.Less(block.Body.linearVelocity.magnitude, 0.1f);
        }

        [Test]
        public void ScaleChangesBodyEyeAndSpeeds()
        {
            BuildFloor();
            Game.Player.SetScale(2f);
            Run(20);
            Assert.AreEqual(0.6f, Game.Player.Collider.radius, 1e-5f);
            Assert.AreEqual(3.4f, Game.Player.Collider.height, 1e-5f);
            Assert.AreEqual(3.1f, Game.Player.Eye.y - Game.Player.Position.y, 1e-4f);
            Assert.IsTrue(Game.Player.Grounded);

            Input.Hold.MoveZ = 1f;
            RunSeconds(0.5f);
            Assert.AreEqual(10f, SpeedOverOneSecond(), 0.3f);

            Input.Hold = default;
            RunSeconds(0.5f);
            float ground = Game.Player.Position.y, highest = ground;
            Input.Once.Jump = true;
            for (int i = 0; i < TestHelpers.Ticks(2f); i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.AreEqual(2.6f, highest - ground, 0.26f, "jump speed scales with sqrt(s), so the apex scales with s");
        }

        [Test]
        public void EyeInterpolatesBetweenTicks()
        {
            BuildFloor();
            Input.Hold.MoveZ = 1f;
            RunSeconds(1f);
            Vector3 before = Game.Player.Eye;
            Game.Tick();
            Vector3 after = Game.Player.Eye;
            Assert.Less(Vector3.Distance(before, Game.Player.EyeAt(0f)), 1e-5f);
            Assert.Less(Vector3.Distance(after, Game.Player.EyeAt(1f)), 1e-5f);
            Assert.Less(Vector3.Distance((before + after) * 0.5f, Game.Player.EyeAt(0.5f)), 1e-5f);
        }
    }
}
