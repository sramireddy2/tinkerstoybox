using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Toybox.Tests
{
    /// <summary>
    /// The forced-perspective mechanic under attack (M1 review): what the hold does around thin obstacles,
    /// where the held prop is while the player moves, what a click takes, what happens to a prop let go
    /// overhead, extreme scales, compound props, and what a hold costs. PerspectiveTests has the basic math.
    /// </summary>
    public class PerspectiveHoldTests : SimTest
    {
        // ---------------------------------------------------------------- helpers

        void Grab(Prop prop)
        {
            LookAt(prop.Center);
            Click();
            Assert.AreSame(prop, Game.Grabber.Held, "test setup: the prop should be in hand after the click");
        }

        void Drop()
        {
            Click();
            Assert.IsNull(Game.Grabber.Held, "test setup: nothing should be held after the second click");
        }

        static BoxCollider AddBox(GameObject root, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var child = new GameObject("Part");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = center;
            child.transform.localRotation = rotation ?? Quaternion.identity;
            BoxCollider box = child.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        // Seat, back and four legs; the pivot is on the floor under the seat (not at the center of the bounds).
        static GameObject Chair()
        {
            var root = new GameObject("Chair");
            AddBox(root, new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.1f, 1f));
            AddBox(root, new Vector3(0f, 1.05f, -0.45f), new Vector3(1f, 1f, 0.1f));
            for (int i = 0; i < 4; i++)
                AddBox(root, new Vector3(i % 2 == 0 ? -0.45f : 0.45f, 0.225f, i / 2 == 0 ? -0.45f : 0.45f), new Vector3(0.1f, 0.45f, 0.1f));
            return root;
        }

        static float LowestPoint(Prop prop)
        {
            Physics.SyncTransforms();
            float lowest = float.MaxValue;
            foreach (Collider collider in prop.Colliders) lowest = Mathf.Min(lowest, collider.bounds.min.y);
            return lowest;
        }

        // Vector3.Angle goes through acos and cannot resolve less than about 0.03 degrees; the cross product can.
        static float OffAxisDegrees(Game game, Prop prop)
        {
            Vector3 to = prop.Center - game.Player.Eye;
            float sine = Vector3.Cross(game.Player.Forward, to).magnitude / to.magnitude;
            return Mathf.Asin(Mathf.Clamp01(sine)) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- A: thin obstacles on the way out

        /// <summary>
        /// A plank that is 9 units wide by the time it reaches a partition cannot go through a doorway
        /// 1.2 units wide. The hold march steps 8 % of the distance at a time and only tests the pose at
        /// each step; with nothing else it would step right over a thin partition that the center ray does
        /// not hit. Where anything is near the prop's path the march takes finer steps.
        /// </summary>
        [TestCase(10f, 0.1f)]
        [TestCase(10f, 0.3f)]
        [TestCase(30f, 1f)]
        public void WidePlankIsNotCarriedThroughANarrowDoorway(float wallZ, float thickness)
        {
            Prop plank = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 40f);
                // A partition across the room at z = 10 with a doorway 1.2 wide and 2.2 high in the middle.
                TestHelpers.Box(ctx, new Vector3(-20.3f, 6f, wallZ), new Vector3(39.4f, 12f, thickness));
                TestHelpers.Box(ctx, new Vector3(20.3f, 6f, wallZ), new Vector3(39.4f, 12f, thickness));
                TestHelpers.Box(ctx, new Vector3(0f, 7.1f, wallZ), new Vector3(1.2f, 9.8f, thickness));
                plank = ctx.AddProp(BasicToys.Block(new Vector3(3f, 0.3f, 0.1f)), new Vector3(0f, 0.15f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(plank);
            float k = Game.Grabber.Ratio;

            int through = 0, samples = 0;
            string where = "";
            for (int i = 0; i <= 20; i++)
            {
                // Step back a little at a time; the view always goes through the doorway to the far wall.
                Game.Player.Teleport(new Vector3(0f, 0f, -0.1f * i));
                LookAt(new Vector3(0f, 1.9f, 40f));
                Run(2);
                Assert.IsTrue(Game.Grabber.PlacementValid);
                samples++;
                float widthAtWall = 3f * k * (wallZ - Game.Player.Eye.z);
                Assert.Greater(widthAtWall, 6f, "test setup: the plank is far wider than the doorway where it meets the wall");
                if (plank.Center.z > wallZ + thickness)
                {
                    through++;
                    where += " eyeZ=" + Game.Player.Eye.z.ToString("0.0") + "->z=" + plank.Center.z.ToString("0.0") + "(w=" + (3f * plank.Scale).ToString("0.0") + ")";
                }
            }
            Debug.Log("hold: A: wall at " + wallZ + ", " + thickness + " thick: plank beyond the partition in " + through + " of " + samples + " eye positions:" + where);
            Assert.AreEqual(0, through, "a plank several times wider than the doorway ended up on the far side of the partition in "
                + through + " of " + samples + " eye positions:" + where);
        }

        /// <summary>The same with an everyday prop: a half-unit crate and a row of bars it cannot fit between.</summary>
        [Test]
        public void CrateIsNotCarriedThroughBars()
        {
            Prop crate = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                // Bars 0.1 thick every 0.5 units (gaps of 0.4) across the room at z = 12.
                for (int i = -40; i <= 40; i++)
                    TestHelpers.Box(ctx, new Vector3(0.25f + i * 0.5f, 3f, 12f), new Vector3(0.1f, 6f, 0.1f));
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 9f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(crate);
            float k = Game.Grabber.Ratio;

            int through = 0, samples = 0;
            for (int i = 0; i <= 20; i++)
            {
                Game.Player.Teleport(new Vector3(0f, 0f, -0.1f * i));
                // Between two bars, at the far wall.
                LookAt(new Vector3(0f, 1.5f, 20f));
                Run(2);
                samples++;
                float sizeAtBars = 0.5f * k * (12f - Game.Player.Eye.z);
                Assert.Greater(sizeAtBars, 0.55f, "test setup: the crate is wider than the gap between the bars");
                if (crate.Center.z > 12f) through++;
            }
            Debug.Log("hold: A2: crate beyond the bars in " + through + " of " + samples + " eye positions");
            Assert.AreEqual(0, through, "a crate wider than the gap between the bars ended up behind them in " + through + " of " + samples + " eye positions");
        }

        // ---------------------------------------------------------------- B: aim assist

        [Test]
        public void AimAssistDoesNotGrabAPropTheCrosshairIsFarAwayFrom()
        {
            Prop plank = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                plank = ctx.AddProp(BasicToys.Block(new Vector3(8f, 0.2f, 0.2f)), new Vector3(0f, 0.1f, 6f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);

            // Looking 25 degrees up into the empty sky; the plank lies on the floor, 13 degrees below the horizon.
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = 25f;
            Assert.IsFalse(Game.PhysicsScene.Raycast(Game.Player.Eye, Game.Player.Forward, 500f, Layers.SolidMask), "test setup: the view ray hits nothing");
            Prop target = Game.Grabber.FindTarget();
            Assert.IsNull(target, "looking into the sky, 38 degrees above a plank on the floor, a click would still grab the plank");
        }

        [Test]
        public void AimAssistDoesNotGrabABigCrateFromWellBesideIt()
        {
            Prop crate = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(4f), new Vector3(0f, 2f, 10f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);

            // The crate's face spans +-14 degrees; the crosshair is at 18 degrees, on the floor beside it.
            Game.Player.Yaw = 18f;
            Game.Player.Pitch = -8f;
            bool hit = Game.PhysicsScene.Raycast(Game.Player.Eye, Game.Player.Forward, out RaycastHit seen, 500f, Layers.SolidMask);
            Assert.IsTrue(hit && PropRef.Of(seen.collider) == null, "test setup: the crosshair is on the floor, not on the crate");
            float missedBy = Mathf.Abs(seen.point.x) - 2f;
            Assert.Greater(missedBy, 1f, "test setup: the crosshair is more than a unit beside the crate");
            Assert.IsNull(Game.Grabber.FindTarget(), "a click on the floor " + missedBy.ToString("0.0") + " units beside the crate would grab the crate");
        }

        /// <summary>What the assist is for: the crosshair just beside a prop, or on a speck too small to hit.</summary>
        [Test]
        public void AimAssistTakesAPropTheCrosshairNarrowlyMisses()
        {
            Prop crate = null, speck = null, hidden = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 10f), new PropOptions { Name = "crate" });
                speck = ctx.AddProp(BasicToys.Block(0.05f), new Vector3(20f, 0.025f, 60f), new PropOptions { Name = "speck" });
                // Behind a wall: near the crosshair, but not in view.
                TestHelpers.Box(ctx, new Vector3(-20f, 2f, 20f), new Vector3(6f, 4f, 0.5f));
                hidden = ctx.AddProp(BasicToys.Block(1f), new Vector3(-20f, 0.5f, 22f), new PropOptions { Name = "hidden" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);

            // 0.15 units beside the crate's edge, 10 away: under one degree off.
            LookAt(new Vector3(0.65f, 0.5f, 10f));
            Assert.IsFalse(Game.PhysicsScene.Raycast(Game.Player.Eye, Game.Player.Forward, out RaycastHit seen, 30f, Layers.SolidMask) && PropRef.Of(seen.collider) == crate,
                "test setup: the view ray misses the crate");
            Assert.AreSame(crate, Game.Grabber.FindTarget());
            // Three degrees beside it is past the assist.
            LookAt(new Vector3(1.1f, 0.5f, 10f));
            Assert.IsNull(Game.Grabber.FindTarget());

            LookAt(speck.Center + new Vector3(0.3f, 0f, 0f));
            Assert.AreSame(speck, Game.Grabber.FindTarget(), "a speck 60 units away, a third of a degree off");

            LookAt(new Vector3(-20f, 4.2f, 20f));
            Assert.IsFalse(Game.PhysicsScene.Raycast(Game.Player.Eye, Game.Player.Forward, 60f, Layers.SolidMask), "test setup: the view passes just over the wall");
            Assert.IsNull(Game.Grabber.FindTarget(), "the assist reached behind a wall");
        }

        // ---------------------------------------------------------------- C: the held prop is placed after the step

        [Test]
        public void HeldPropStaysOnTheCrosshairWhileThePlayerMoves()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 30f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                ctx.SetSpawn(new Vector3(0f, 0f, -20f), 0f);
            });
            Run(10);
            Grab(block);
            float k = Game.Grabber.Ratio;
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = -40f;
            Run(5);
            Assert.Less(OffAxisDegrees(Game, block), 0.01f, "standing still the prop is on the view ray");

            float worstAngle = 0f, worstSize = 0f;
            Input.Hold.MoveX = 1f;
            Input.Hold.Sprint = true;
            for (int i = 0; i < 60; i++)
            {
                Game.Tick();
                if (i < 20) continue;
                Assert.IsTrue(Game.Grabber.PlacementValid);
                worstAngle = Mathf.Max(worstAngle, OffAxisDegrees(Game, block));
                float apparent = block.Scale / Vector3.Distance(Game.Player.Eye, block.Center);
                worstSize = Mathf.Max(worstSize, Mathf.Abs(apparent / k - 1f));
            }
            Input.Hold = default;
            Debug.Log("hold: C: strafing at sprint speed the held prop is " + worstAngle.ToString("0.00") + " deg off the crosshair, apparent size off by " + (worstSize * 100f).ToString("0.0") + " %, hold distance " + Game.Grabber.HoldDistance.ToString("0.00"));
            Assert.Less(worstAngle, 0.5f, "after a tick the held prop is not on the view ray of the eye the tick ended with");
        }

        [Test]
        public void ApparentSizeIsConstantWhileWalkingForward()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 30f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                ctx.SetSpawn(new Vector3(0f, 0f, -20f), 0f);
            });
            Run(10);
            Grab(block);
            float k = Game.Grabber.Ratio;
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = -60f;
            Run(5);

            float worstSize = 0f;
            Input.Hold.MoveZ = 1f;
            for (int i = 0; i < 60; i++)
            {
                Game.Tick();
                if (i < 20) continue;
                float apparent = block.Scale / Vector3.Distance(Game.Player.Eye, block.Center);
                worstSize = Mathf.Max(worstSize, Mathf.Abs(apparent / k - 1f));
            }
            Input.Hold = default;
            Debug.Log("hold: C2: walking forward, apparent size off by " + (worstSize * 100f).ToString("0.0") + " %, hold distance " + Game.Grabber.HoldDistance.ToString("0.00"));
            Assert.Less(worstSize, 0.01f, "scale / distance-from-the-eye is not constant while the player walks");
        }

        // ---------------------------------------------------------------- D: dropping a prop from above onto the player

        [TestCase(4f)]
        [TestCase(6f)]
        [TestCase(8f)]
        [TestCase(10f)]
        [TestCase(12f)]
        [TestCase(16f)]
        [TestCase(80f)]
        public void PropDroppedFromTheOpenSkyDoesNotShootThePlayerAway(float maxScale)
        {
            Prop block = null;
            int respawns = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 300f);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f), new PropOptions { MaxScale = maxScale });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.PlayerRespawned += e => respawns++;
            Run(10);
            Grab(block);
            Game.Player.Pitch = 89f;
            Run(3);
            float scale = block.Scale;
            float height = block.Center.y;
            Drop();
            Assert.IsFalse(Physics.GetIgnoreCollision(block.Colliders[0], Game.Player.Collider), "test setup: released well clear of the player");

            float lowest = 0f, fastest = 0f;
            int ticks = TestHelpers.Ticks(Mathf.Sqrt(2f * height / Game.Gravity) + 4f);
            for (int i = 0; i < ticks; i++)
            {
                Game.Tick();
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            }
            Debug.Log("hold: D: maxScale " + maxScale + ": block scale " + scale.ToString("0.0") + " mass " + block.Mass.ToString("0") + " dropped from y=" + height.ToString("0.0")
                + "; player lowest y " + lowest.ToString("0.00") + ", fastest " + fastest.ToString("0.0") + ", respawns " + respawns
                + ", ends at " + Game.Player.Position.ToString("F2") + " grounded " + Game.Player.Grounded + "; block center " + block.Center.ToString("F2"));
            Assert.AreEqual(0, respawns, "the player was pushed out of the world by a prop dropped on their head");
            Assert.Greater(lowest, -0.3f, "the player was pushed into the floor by a prop dropped on their head");
            Assert.Less(fastest, 1.5f * Player.SprintSpeed, "a prop dropped on the player's head shot them away at " + fastest.ToString("0.0")
                + " u/s (they ended " + new Vector2(Game.Player.Position.x, Game.Player.Position.z).magnitude.ToString("0.0") + " units from where they stood)");
        }

        // ---------------------------------------------------------------- E: grabbing the bottom of a stack

        [Test]
        public void GrabbingTheBottomOfAStackLetsTheRestFall()
        {
            Prop a = null, b = null, c = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                a = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f));
                b = ctx.AddProp(BasicToys.Block(0.8f), new Vector3(0f, 1.4f, 5f));
                c = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 2.1f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            RunSeconds(3f);
            Assert.IsTrue(b.Body.IsSleeping() && c.Body.IsSleeping(), "test setup: the stack is asleep");
            Grab(a);
            // Carry it off to the side.
            Game.Player.Yaw = 70f;
            RunSeconds(2f);

            Debug.Log("hold: E: after the grab b=" + b.Center.ToString("F3") + " c=" + c.Center.ToString("F3"));
            Assert.AreEqual(0.4f, b.Center.y, 0.03f, "the block that rested on the grabbed one should have fallen to the floor");
            Assert.AreEqual(1.1f, c.Center.y, 0.05f, "the top block should have come down with it");
            Assert.Less(new Vector2(b.Center.x, b.Center.z - 5f).magnitude, 0.2f, "the block was thrown sideways when its support was taken");
        }

        // ---------------------------------------------------------------- F: extreme scales

        [Test]
        public void SmallestPropsRestOnTheFloor()
        {
            Prop block = null, ball = null, wedge = null, drum = null, card = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 1f, 3f), new PropOptions { Scale = 0.02f });
                ball = ctx.AddProp(BasicToys.Ball(0.25f), new Vector3(1f, 1f, 3f), new PropOptions { Scale = 0.02f });
                wedge = ctx.AddProp(BasicToys.Wedge(1f, 0.5f, 0.5f), new Vector3(2f, 1f, 3f), new PropOptions { Scale = 0.02f });
                drum = ctx.AddProp(BasicToys.Cylinder(0.25f, 0.5f), new Vector3(3f, 1f, 3f), new PropOptions { Scale = 0.02f });
                card = ctx.AddProp(BasicToys.Block(new Vector3(0.6f, 0.9f, 0.02f)), new Vector3(4f, 6f, 3f), new PropOptions { Scale = 0.1f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            RunSeconds(4f);
            foreach (Prop prop in new[] { block, ball, wedge, drum, card })
            {
                Debug.Log("hold: F: " + prop.Name + " scale " + prop.Scale + " mass " + prop.Mass + " center y " + prop.Center.y.ToString("F4") + " v " + prop.Body.linearVelocity.magnitude.ToString("F3") + " mode " + prop.Body.collisionDetectionMode);
                Assert.Greater(prop.Center.y, -0.001f, prop.Name + " fell through the floor");
                Assert.Less(prop.Center.y, 0.06f, prop.Name + " is not lying on the floor");
                Assert.Less(prop.Body.linearVelocity.magnitude, 0.2f, prop.Name + " never came to rest");
            }
        }

        [Test]
        public void ShrunkToTheMinimumThroughTheMechanicAndStillThere()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 40f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 30f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(block);
            // Down at the floor just ahead: the block ends up at its minimum scale or close to it.
            LookAt(new Vector3(0f, 0f, 1f));
            Run(3);
            Drop();
            float scale = block.Scale;
            RunSeconds(3f);
            Debug.Log("hold: F2: dropped at scale " + scale + " mass " + block.Mass + " center " + block.Center.ToString("F4") + " valid " + Game.Grabber.PlacementValid);
            Assert.Greater(block.Center.y, 0f, "the shrunk block fell through the floor");
            Assert.Less(block.Center.y, 0.05f);
            Assert.Less(scale, 0.1f);
            Assert.GreaterOrEqual(scale, 0.02f - 1e-5f);

        }

        [Test]
        public void LargestPropSettlesWithoutExploding()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 400f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(block);
            // Level with the horizon, nothing in the way: out to the hold limit.
            Game.Player.Pitch = 12f;
            Run(3);
            Assert.IsTrue(Game.Grabber.PlacementValid);
            float k = Game.Grabber.Ratio;
            Assert.AreEqual(k, block.Scale / Vector3.Distance(Game.Player.Eye, block.Center), k * 1e-3f);
            Drop();
            float scale = block.Scale;
            float fastest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(8f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, block.Body.linearVelocity.magnitude);
            }
            Debug.Log("hold: F3: scale " + scale + " mass " + block.Mass + " center " + block.Center.ToString("F2") + " v " + block.Body.linearVelocity.magnitude + " fastest " + fastest);
            Assert.AreEqual(0.25f * scale, block.Center.y, 0.05f * scale, "the huge block is not resting on the floor");
            Assert.Less(block.Body.linearVelocity.magnitude, 0.5f);
            Assert.IsFalse(float.IsNaN(block.Center.x));
        }

        // ---------------------------------------------------------------- G: cost of a hold tick

        double[] MeasureHold(int ticks)
        {
            var watch = new Stopwatch();
            double total = 0, worst = 0;
            for (int i = 0; i < ticks; i++)
            {
                watch.Restart();
                Game.Tick();
                watch.Stop();
                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                worst = System.Math.Max(worst, ms);
            }
            return new[] { total / ticks, worst };
        }

        [Test]
        public void HoldCostInARoomStaysWithinBudget()
        {
            // 1. A crate against the floor of an empty room.
            Prop crate = null, chair = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 30f);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(-3f, 0.25f, 3f));
                chair = ctx.AddProp(Chair(), new Vector3(3f, 0f, 3f));
                // A heap of 24 small static bricks around (0, 0, 12).
                for (int i = 0; i < 24; i++)
                    TestHelpers.Box(ctx, new Vector3(-1.1f + (i % 6) * 0.45f, 0.1f + (i / 12) * 0.2f, 11.5f + (i / 6 % 2) * 0.45f), new Vector3(0.4f, 0.2f, 0.4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(30);
            double[] idle = MeasureHold(120);

            Grab(crate);
            LookAt(new Vector3(-6f, 0f, 8f));
            Run(30);
            double[] crateOnFloor = MeasureHold(120);
            LookAt(new Vector3(0f, 0.3f, 12f));
            Run(30);
            double[] crateOnHeap = MeasureHold(120);
            LookAt(new Vector3(8f, 0f, 3f));
            Run(2);
            Drop();
            Run(30);

            Grab(chair);
            LookAt(new Vector3(6f, 0f, 8f));
            Run(30);
            double[] chairOnFloor = MeasureHold(120);
            LookAt(new Vector3(0f, 0.3f, 12f));
            Run(30);
            double[] chairOnHeap = MeasureHold(120);
            // Sweeping the view across the heap.
            double sweepTotal = 0, sweepWorst = 0;
            var watch = new Stopwatch();
            for (int i = 0; i < 120; i++)
            {
                LookAt(new Vector3(-3f + i * 0.05f, 0.3f, 12f));
                watch.Restart();
                Game.Tick();
                watch.Stop();
                sweepTotal += watch.Elapsed.TotalMilliseconds;
                sweepWorst = System.Math.Max(sweepWorst, watch.Elapsed.TotalMilliseconds);
            }

            Debug.Log("hold: G (ms per Game.Tick, avg / worst): idle " + idle[0].ToString("0.00") + " / " + idle[1].ToString("0.00")
                + "; crate on floor " + crateOnFloor[0].ToString("0.00") + " / " + crateOnFloor[1].ToString("0.00")
                + "; crate on heap " + crateOnHeap[0].ToString("0.00") + " / " + crateOnHeap[1].ToString("0.00")
                + "; chair(6 boxes) on floor " + chairOnFloor[0].ToString("0.00") + " / " + chairOnFloor[1].ToString("0.00")
                + "; chair on heap of 24 " + chairOnHeap[0].ToString("0.00") + " / " + chairOnHeap[1].ToString("0.00")
                + "; chair swept over heap " + (sweepTotal / 120).ToString("0.00") + " / " + sweepWorst.ToString("0.00"));
            Assert.Less(chairOnHeap[0] - idle[0], 6.0, "holding a six-collider chair against a heap of bricks costs more than 6 ms per tick in the editor");
        }

        // ---------------------------------------------------------------- compound props, orientation, respawn, sweeps

        [Test]
        public void CompoundPropWithAnOffCenterPivotKeepsItsApparentSizeAndLandsFlush()
        {
            Prop chair = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                chair = ctx.AddProp(Chair(), new Vector3(0f, 0f, 4f), Quaternion.Euler(0f, 30f, 0f), new PropOptions { Density = 2f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Assert.AreEqual(0.1f + 0.1f + 4f * 0.0045f, chair.BaseVolume, 1e-4f);
            Assert.Greater(chair.LocalCenter.y, 0.5f, "test setup: the pivot is not the center");
            Grab(chair);
            float k = Game.Grabber.Ratio;

            foreach (Vector3 aim in new[] { new Vector3(0f, 0f, 9f), new Vector3(6f, 0f, 14f), new Vector3(-10f, 0.5f, 19.5f), new Vector3(2f, 0f, 2f) })
            {
                LookAt(aim);
                Run(2);
                Assert.IsTrue(Game.Grabber.PlacementValid, "there is room toward " + aim);
                Assert.AreEqual(k, chair.Scale / Vector3.Distance(Game.Player.Eye, chair.Center), k * 1e-3f, "apparent size changed toward " + aim);
                Assert.Less(OffAxisDegrees(Game, chair), 0.02f, "the chair's center left the view ray toward " + aim);
                Assert.Less(TestHelpers.DeepestOverlap(Game, chair), 1e-3f, "the held chair intersects the world toward " + aim);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 30f + Game.Player.Yaw, 0f), chair.Rotation), 0.05f, "orientation should follow the camera yaw only");
            }
            LookAt(new Vector3(3f, 0f, 9f));
            Run(2);
            float lowest = LowestPoint(chair);
            Assert.GreaterOrEqual(lowest, -1e-3f);
            Assert.Less(lowest, 0.05f, "the chair should have come down on the floor");
            Drop();
            float expected = 2f * chair.BaseVolume * Mathf.Pow(chair.Scale, 3f);
            Assert.AreEqual(expected, chair.Mass, expected * 1e-3f);
            Quaternion placed = chair.Rotation;
            RunSeconds(2f);
            Assert.Less(Quaternion.Angle(placed, chair.Rotation), 2f, "the chair fell over after being put down on its legs");
            Assert.Less(chair.Body.linearVelocity.magnitude, 0.3f);
        }

        [Test]
        public void RotateHeldTurnsAboutTheViewAxes()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(new Vector3(1f, 0.4f, 0.6f)), new Vector3(0f, 0.2f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Grab(block);
            Game.Player.Yaw = 90f;
            Game.Player.Pitch = 10f;
            Run(2);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), block.Rotation), 0.05f);

            Input.Once.RotateYaw = 1;
            Game.Tick();
            Input.Once.RotateYaw = 1;
            Game.Tick();
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 120f, 0f), block.Rotation), 0.05f, "two yaw steps are 30 degrees");

            Input.Once.RotatePitch = true;
            Game.Tick();
            // 90 degrees about the camera's right axis (which is world -Z... at yaw 90 right is (0,0,-1)).
            Quaternion expected = Quaternion.AngleAxis(90f, Quaternion.Euler(0f, 90f, 0f) * Vector3.right) * Quaternion.Euler(0f, 120f, 0f);
            Assert.Less(Quaternion.Angle(expected, block.Rotation), 0.05f, "a pitch step turns about the camera's right axis");
            Game.Tick();
            Assert.Less(Quaternion.Angle(expected, block.Rotation), 0.05f, "nothing turns without input");
        }

        [Test]
        public void RespawnWhileHeldRestoresScalePoseAndBody()
        {
            Prop block = null;
            var respawned = new List<PropEvent>();
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f), new PropOptions { Scale = 1.5f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            int grabbed = 0, dropped = 0;
            Game.Events.PropRespawned += respawned.Add;
            Game.Events.PropGrabbed += e => grabbed++;
            Game.Events.PropDropped += e => dropped++;
            Run(20);
            Vector3 home = block.Position;
            Grab(block);
            LookAt(new Vector3(0f, 1f, 19f));
            Run(3);
            Assert.Greater(block.Scale, 4f);
            block.Respawn();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(block.Held);
            Assert.AreEqual(1.5f, block.Scale, 1e-5f);
            Assert.AreEqual(Layers.Prop, block.GameObject.layer);
            Assert.IsFalse(block.Body.isKinematic);
            Assert.AreEqual(0.125f * 1.5f * 1.5f * 1.5f, block.Mass, 1e-4f);
            Assert.AreEqual(1, respawned.Count);
            Assert.AreEqual(1, grabbed);
            Assert.AreEqual(1, dropped, "a hold that ends by a respawn raises PropDropped too, so grabbed and dropped pair up");
            Run(30);
            Assert.Less(Vector3.Distance(home, block.Position), 0.2f);
            Assert.IsNull(Game.Grabber.Held);
        }

        [Test]
        public void SweepAcrossDepthDiscontinuitiesNeverIntersectsAndKeepsTheRatio()
        {
            Prop ball = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                // Pillars and a low table at various depths.
                TestHelpers.Box(ctx, new Vector3(-4f, 3f, 6f), new Vector3(1f, 6f, 1f));
                TestHelpers.Box(ctx, new Vector3(3f, 3f, 12f), new Vector3(2f, 6f, 0.3f));
                TestHelpers.Box(ctx, new Vector3(0f, 0.9f, 8f), new Vector3(3f, 0.2f, 2f));
                TestHelpers.Ramp(ctx, 25f, 14f, 6f, 6f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(6f, 0.5f, 7f));
                ball = ctx.AddProp(BasicToys.Ball(0.3f), new Vector3(0f, 0.3f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Grab(ball);
            float k = Game.Grabber.Ratio;
            int invalid = 0, jumps = 0;
            float previous = Game.Grabber.HoldDistance;
            for (int i = 0; i <= 480; i++)
            {
                Game.Player.Yaw = -60f + i * 0.25f;
                Game.Player.Pitch = -12f + 10f * Mathf.Sin(i * 0.05f);
                Game.Tick();
                if (!Game.Grabber.PlacementValid)
                {
                    invalid++;
                    continue;
                }
                float distance = Vector3.Distance(Game.Player.Eye, ball.Center);
                Assert.AreEqual(k, ball.Scale / distance, k * 1e-3f, "scale / distance changed at step " + i);
                Assert.Less(OffAxisDegrees(Game, ball), 0.02f, "off the view ray at step " + i);
                Assert.Less(TestHelpers.DeepestOverlap(Game, ball), 1e-3f, "the held ball intersects the world at step " + i);
                if (Mathf.Abs(distance - previous) > 2f) jumps++;
                previous = distance;
            }
            Debug.Log("hold: sweep: invalid placements " + invalid + ", depth jumps " + jumps);
            Assert.AreEqual(0, invalid);
            Assert.GreaterOrEqual(jumps, 2, "test setup: the sweep crosses depth discontinuities");
        }

        [Test]
        public void DropOntoASlopeAndOntoAnotherPropComesToRest()
        {
            Prop block = null, pedestal = null, pebble = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                TestHelpers.Ramp(ctx, 20f, 8f, 10f, 8f);
                pedestal = ctx.AddProp(BasicToys.Block(new Vector3(1.5f, 0.5f, 1.5f)), new Vector3(-3f, 0.25f, 2f));
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                pebble = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(6f, 0.25f, 7f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Grab(block);
            // Onto the ramp (20 degrees; friction 0.6 holds up to about 31).
            LookAt(new Vector3(0f, 1.2f, 12f));
            Run(3);
            Drop();
            Assert.Less(TestHelpers.DeepestOverlap(Game, block), 1e-3f);
            Vector3 released = block.Center;
            RunSeconds(3f);
            Debug.Log("hold: slope: released " + released.ToString("F2") + " now " + block.Center.ToString("F2") + " v " + block.Body.linearVelocity.magnitude.ToString("F3"));
            Assert.Less(block.Body.linearVelocity.magnitude, 0.2f, "the block did not come to rest on a 20 degree slope");
            Assert.Less(TestHelpers.DeepestOverlap(Game, block), 0.02f);

            // A small block onto the top of another prop.
            Grab(pebble);
            LookAt(new Vector3(-3f, 0.5f, 2f));
            Run(3);
            Drop();
            Assert.Less(TestHelpers.DeepestOverlap(Game, pebble), 1e-3f);
            RunSeconds(3f);
            Debug.Log("hold: on-prop: pebble " + pebble.Center.ToString("F3") + " scale " + pebble.Scale + " pedestal " + pedestal.Center.ToString("F3"));
            Assert.AreEqual(0.5f + 0.25f * pebble.Scale, pebble.Center.y, 0.02f, "the small block should be lying on the pedestal");
            Assert.Less(pebble.Body.linearVelocity.magnitude, 0.2f);
            Assert.Less(Vector3.Distance(pedestal.Center, new Vector3(-3f, 0.25f, 2f)), 0.05f, "the pedestal was shoved by the drop");
        }

        [Test]
        public void ViewRayIntoTheVoidHoldsAtTheLimit()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 20f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(20);
            Grab(block);
            float k = Game.Grabber.Ratio;
            Game.Player.Pitch = 45f;
            Run(2);
            Assert.IsTrue(Game.Grabber.PlacementValid);
            Assert.AreEqual(Mathf.Min(PerspectiveGrabber.MaxHoldDistance, 80f / k), Game.Grabber.HoldDistance, 1e-2f);
            Assert.AreEqual(k, block.Scale / Vector3.Distance(Game.Player.Eye, block.Center), k * 1e-3f);
        }

        // ---------------------------------------------------------------- D2: a prop that comes down over the player

        /// <summary>
        /// A prop that comes down on the player passes through them and comes to rest around them. They are
        /// not crushed and walk out through its side; once they are out it is solid for them again.
        /// </summary>
        [Test]
        public void PropThatLandsAroundThePlayerCanBeWalkedOutOf()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 300f);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(block);
            Game.Player.Pitch = 89f;
            Run(3);
            float height = block.Center.y;
            Drop();
            RunSeconds(Mathf.Sqrt(2f * height / Game.Gravity) + 3f);

            Collider own = block.Colliders[0];
            bool inside = Physics.ComputePenetration(own, own.transform.position, own.transform.rotation,
                Game.Player.Collider, Game.Player.Position, Quaternion.identity, out Vector3 direction, out float depth);
            Debug.Log("hold: D2: after landing player " + Game.Player.Position.ToString("F2") + " block center " + block.Center.ToString("F2") + " scale " + block.Scale.ToString("F1")
                + " overlapping " + inside + " depth " + depth.ToString("F2") + " dir " + direction.ToString("F2") + " ignore " + Physics.GetIgnoreCollision(own, Game.Player.Collider)
                + " target " + Game.Grabber.FindTarget());

            // Try to walk out toward the nearest side face (-Z).
            Vector3 start = Game.Player.Position;
            Game.Player.Yaw = 180f;
            Game.Player.Pitch = 0f;
            Input.Hold.MoveZ = 1f;
            float fastest = 0f, lowest = 0f, highest = 0f;
            string trace = "";
            for (int i = 0; i < TestHelpers.Ticks(8f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
                highest = Mathf.Max(highest, Game.Player.Position.y);
                if (i % 60 == 59) trace += " " + Game.Player.Position.ToString("F1");
            }
            Input.Hold = default;
            Debug.Log("hold: D2: walking 8 s: moved " + Vector3.Distance(start, Game.Player.Position).ToString("F2") + " fastest " + fastest.ToString("F1") + " y range " + lowest.ToString("F2") + ".." + highest.ToString("F2")
                + " block center " + block.Center.ToString("F2") + " v " + block.Body.linearVelocity.magnitude.ToString("F2") + " trace" + trace);
            Assert.IsTrue(inside && depth > 1f, "test setup: the block came down around the player");
            Assert.Less(Game.Player.Position.z, block.Center.z - 0.5f * block.Scale - 0.3f, "the player could not walk out of the block that landed around them");
            Assert.Less(fastest, 1.5f * Player.SprintSpeed);
            Assert.Greater(lowest, -0.05f);
            Assert.IsFalse(Physics.GetIgnoreCollision(own, Game.Player.Collider), "once the player is out, the block is solid for them again");
        }

        // ---------------------------------------------------------------- G2: worst case for the exact phase

        static GameObject Arch()
        {
            var root = new GameObject("Arch");
            AddBox(root, new Vector3(-1f, 0.75f, 0f), new Vector3(0.2f, 1.5f, 0.2f));
            AddBox(root, new Vector3(1f, 0.75f, 0f), new Vector3(0.2f, 1.5f, 0.2f));
            AddBox(root, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 0.2f, 0.2f));
            return root;
        }

        [Test]
        public void HoldCostForAnArchStraddlingARailStaysWithinBudget()
        {
            Prop arch = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 300f);
                // A low rail running away from the player; the arch is carried along it, legs either side.
                TestHelpers.Box(ctx, new Vector3(0f, 0.2f, 60f), new Vector3(0.2f, 0.4f, 116f));
                arch = ctx.AddProp(Arch(), new Vector3(4f, 0f, 6f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(30);
            double[] idle = MeasureHold(120);
            // The middle of an arch is empty; take it by a leg.
            LookAt(new Vector3(3f, 0.75f, 6f));
            Click();
            Assert.AreSame(arch, Game.Grabber.Held, "test setup: the arch should be in hand");
            // Along the rail, slightly above it, out toward the horizon.
            LookAt(new Vector3(0f, 1.2f, 100f));
            Run(30);
            float distance = Game.Grabber.HoldDistance;
            double[] held = MeasureHold(240);
            Debug.Log("hold: G2 (ms per Game.Tick, avg / worst): idle " + idle[0].ToString("0.00") + " / " + idle[1].ToString("0.00")
                + "; arch (3 boxes) straddling a rail " + held[0].ToString("0.00") + " / " + held[1].ToString("0.00") + " hold distance " + distance.ToString("0.0") + " valid " + Game.Grabber.PlacementValid);
            Assert.Less(held[0] - idle[0], 6.0, "a hold tick costs more than 6 ms in the editor");
        }

        // ---------------------------------------------------------------- measured bounds of awkward compound toys

        static GameObject Awkward()
        {
            var root = new GameObject("Awkward");
            AddBox(root, new Vector3(0.6f, 0.4f, 0.1f), new Vector3(1f, 0.2f, 0.4f), Quaternion.Euler(20f, 35f, 50f)).center = new Vector3(0.1f, 0f, -0.2f);

            var ball = new GameObject("Ball");
            ball.transform.SetParent(root.transform, false);
            ball.transform.localPosition = new Vector3(-0.7f, 0.3f, 0.2f);
            ball.transform.localScale = new Vector3(1f, 2f, 0.5f);
            ball.AddComponent<SphereCollider>().radius = 0.2f;

            var pill = new GameObject("Pill");
            pill.transform.SetParent(root.transform, false);
            pill.transform.localPosition = new Vector3(0f, 1f, -0.5f);
            pill.transform.localRotation = Quaternion.Euler(0f, 0f, 60f);
            pill.transform.localScale = new Vector3(0.5f, 1.5f, 1f);
            CapsuleCollider capsule = pill.AddComponent<CapsuleCollider>();
            capsule.radius = 0.15f;
            capsule.height = 1f;
            capsule.direction = 1;

            GameObject wedge = BasicToys.Wedge(0.8f, 0.5f, 0.4f);
            wedge.transform.SetParent(root.transform, false);
            wedge.transform.localPosition = new Vector3(0.2f, 0.25f, 0.9f);
            wedge.transform.localRotation = Quaternion.Euler(0f, 110f, 0f);
            return root;
        }

        [Test]
        public void MeasuredBoundsContainEveryColliderAtEveryScale()
        {
            Prop prop = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                prop = ctx.AddProp(Awkward(), new Vector3(0f, 3f, 5f), new PropOptions { Body = PropBody.Fixed });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            foreach (float scale in new[] { 1f, 0.05f, 7f })
            {
                prop.SetScale(scale);
                Physics.SyncTransforms();
                Bounds actual = prop.Colliders[0].bounds;
                foreach (Collider collider in prop.Colliders) actual.Encapsulate(collider.bounds);
                Vector3 min = prop.Center - prop.LocalHalfExtents * scale, max = prop.Center + prop.LocalHalfExtents * scale;
                float slack = 1e-3f * scale;
                Debug.Log("hold: bounds @" + scale + ": measured " + min.ToString("F4") + ".." + max.ToString("F4") + " actual " + actual.min.ToString("F4") + ".." + actual.max.ToString("F4"));
                Assert.IsTrue(actual.min.x >= min.x - slack && actual.min.y >= min.y - slack && actual.min.z >= min.z - slack
                    && actual.max.x <= max.x + slack && actual.max.y <= max.y + slack && actual.max.z <= max.z + slack,
                    "a collider sticks out of the box the hold search uses as its broad phase (scale " + scale + ")");
            }
        }

        [Test]
        public void AwkwardCompoundPropNeverIntersectsTheWorldWhileHeld()
        {
            Prop prop = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                TestHelpers.Box(ctx, new Vector3(-4f, 3f, 6f), new Vector3(1f, 6f, 1f));
                TestHelpers.Box(ctx, new Vector3(0f, 0.9f, 8f), new Vector3(3f, 0.2f, 2f));
                TestHelpers.Ramp(ctx, 25f, 14f, 6f, 6f);
                prop = ctx.AddProp(Awkward(), new Vector3(0f, 0.3f, 3f), Quaternion.Euler(0f, 40f, 0f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(40);
            Grab(prop);
            float k = Game.Grabber.Ratio;
            int invalid = 0;
            float deepest = 0f;
            for (int i = 0; i <= 240; i++)
            {
                Game.Player.Yaw = -60f + i * 0.5f;
                Game.Player.Pitch = -14f + 12f * Mathf.Sin(i * 0.1f);
                if (i % 40 == 20) Input.Once.RotateYaw = 1;
                if (i % 80 == 60) Input.Once.RotatePitch = true;
                Game.Tick();
                if (!Game.Grabber.PlacementValid)
                {
                    invalid++;
                    continue;
                }
                Assert.AreEqual(k, prop.Scale / Vector3.Distance(Game.Player.Eye, prop.Center), k * 1e-3f, "scale / distance changed at step " + i);
                Assert.Less(OffAxisDegrees(Game, prop), 0.01f, "off the view ray at step " + i);
                deepest = Mathf.Max(deepest, TestHelpers.DeepestOverlap(Game, prop));
            }
            Debug.Log("hold: awkward: invalid " + invalid + " deepest overlap " + deepest);
            Assert.Less(deepest, 1e-3f, "the held prop intersected the world");
        }

        // ---------------------------------------------------------------- kinematic props

        /// <summary>
        /// A prop that level code moves (PropBody.Kinematic) is not grabbable unless the level says so: a
        /// click must not take a platform off its path. If the level does allow it, the prop behaves like any
        /// held prop and its mover stands still until it is let go.
        /// </summary>
        [Test]
        public void KinematicPropsAreNotGrabbableUnlessAskedFor()
        {
            Prop platform = null, rider = null, toy = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A platform that level code slides from side to side, written the obvious way.
                platform = ctx.AddProp(BasicToys.Slab(new Vector3(3f, 0.4f, 3f)), new Vector3(0f, 1f, 8f), new PropOptions { Name = "platform", Body = PropBody.Kinematic });
                rider = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 1.5f, 8f), new PropOptions { Name = "rider" });
                toy = ctx.AddProp(BasicToys.Block(new Vector3(1f, 0.4f, 1f)), new Vector3(-6f, 1f, 6f), new PropOptions { Name = "toy", Body = PropBody.Kinematic, Grabbable = true });
                Prop moved = platform, turned = toy;
                ctx.OnUpdate(dt =>
                {
                    moved.Mover.MoveTo(new Vector3(2f * Mathf.Sin(ctx.Time), 1f, 8f));
                    turned.Mover.MoveTo(new Vector3(-6f + Mathf.Sin(ctx.Time), 1f, 6f));
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsFalse(platform.Grabbable, "a kinematic prop is not grabbable by default");
            Assert.IsTrue(rider.Grabbable);
            Assert.IsTrue(toy.Grabbable, "unless the level asks for it");
            Run(30);
            Assert.AreEqual(1.5f, rider.Center.y, 0.05f, "test setup: the rider rests on the platform");

            // A click on the platform, well clear of the crate riding it (the aim assist would take that).
            LookAt(platform.Center + new Vector3(1.3f, -0.1f, 0f));
            Assert.IsNull(Game.Grabber.FindTarget());
            Click();
            Assert.IsNull(Game.Grabber.Held, "a click took the level's platform");
            Run(30);
            Assert.AreEqual(Layers.Prop, platform.GameObject.layer);
            Assert.AreEqual(1f, platform.Scale);
            Assert.AreEqual(2f * Mathf.Sin(Game.Time - Sim.Dt), platform.Position.x, 0.05f, "the platform left its path");
            Assert.AreEqual(1.5f, rider.Center.y, 0.05f, "what stood on the platform fell off or through");

            // The one that may be taken: it follows the view, not its path, for as long as it is held.
            LookAt(toy.Center);
            Click();
            Assert.AreSame(toy, Game.Grabber.Held);
            Assert.IsTrue(toy.Mover.Suspended);
            LookAt(new Vector3(-10f, 3f, 10f));
            Run(30);
            Assert.IsTrue(Game.Grabber.PlacementValid);
            Assert.Less(OffAxisDegrees(Game, toy), 0.05f, "a held kinematic prop must be on the view ray, not on its mover's path");
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(toy.Mover.Suspended, "once it is let go the level moves it again");
            Assert.IsTrue(toy.Body.isKinematic);
        }

        // ---------------------------------------------------------------- what is drawn between ticks

        /// <summary>
        /// The render layer may put the held prop on the crosshair of an interpolated camera (Present). That
        /// must not leak into the simulation: the next tick starts from the simulation's own placement.
        /// </summary>
        [Test]
        public void PresentingTheHeldPropForAFrameDoesNotChangeTheSimulation()
        {
            Vector3 Play(bool present)
            {
                DisposeGame();
                Prop block = null;
                Build(ctx =>
                {
                    TestHelpers.Room(ctx, 20f);
                    block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                    ctx.SetSpawn(Vector3.zero, 0f);
                });
                Run(10);
                Grab(block);
                Input.Hold.MoveX = 1f;
                for (int i = 0; i < 40; i++)
                {
                    Input.Once.LookYaw = 1.5f;
                    Game.Tick();
                    Assert.Less(OffAxisDegrees(Game, block), 0.01f, "after a tick the prop is on the view ray");
                    if (!present) continue;
                    // A frame drawn from somewhere else, looking somewhere else.
                    Quaternion look = Quaternion.Euler(-20f, Game.Player.Yaw + 30f, 0f);
                    Vector3 eye = Game.Player.EyeAt(0.3f);
                    Game.Grabber.Present(eye, look);
                    Vector3 to = block.Center - eye;
                    Assert.Less(Vector3.Angle(look * Vector3.forward, to), 0.05f, "Present puts the prop on that camera's crosshair");
                    Assert.AreEqual(Game.Grabber.HoldDistance, to.magnitude, 1e-3f);
                }
                Input.Hold = default;
                Click();
                RunSeconds(2f);
                return block.Position;
            }

            Vector3 plain = Play(false);
            Vector3 presented = Play(true);
            Assert.IsTrue(plain.Equals(presented), "presenting frames changed where the prop ended up: " + plain.ToString("R") + " vs " + presented.ToString("R"));
        }

        // ---------------------------------------------------------------- nose against a wall

        /// <summary>Walking into a wall with a prop in hand: it shrinks down to the near limit, then stays put, free of the wall.</summary>
        [Test]
        public void JammedAgainstAWallThePropStaysAtItsLastFreePose()
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 10f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(block);
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = 0f;
            Input.Hold.MoveZ = 1f;
            string trace = "";
            for (int i = 0; i < 200; i++)
            {
                Game.Tick();
                Assert.Less(TestHelpers.DeepestOverlap(Game, block), 1e-3f, "the held block intersects the wall at tick " + i);
                if (i % 20 == 19 || (i > 100 && i < 130))
                    trace += "\n  t" + i + " eyeZ " + Game.Player.Eye.z.ToString("F3") + " valid " + Game.Grabber.PlacementValid + " hold " + Game.Grabber.HoldDistance.ToString("F3")
                        + " scale " + block.Scale.ToString("F3") + " center " + block.Center.ToString("F3") + " overlap " + TestHelpers.DeepestOverlap(Game, block).ToString("F4");
            }
            Assert.IsFalse(Game.Grabber.PlacementValid, "nose against the wall there is no room on the view ray");
            // Slide along the wall, still facing it.
            Input.Hold.MoveZ = 0f;
            Input.Hold.MoveX = 1f;
            Run(120);
            trace += "\n  after sliding 2 s: eye " + Game.Player.Eye.ToString("F2") + " valid " + Game.Grabber.PlacementValid + " center " + block.Center.ToString("F2") + " scale " + block.Scale.ToString("F3");
            Input.Hold = default;
            Click();
            trace += "\n  dropped: held " + (Game.Grabber.Held != null) + " center " + block.Center.ToString("F2") + " scale " + block.Scale.ToString("F3") + " mass " + block.Mass;
            Assert.IsNull(Game.Grabber.Held);
            Assert.Less(TestHelpers.DeepestOverlap(Game, block), 1e-3f, "released inside the wall");
            RunSeconds(2f);
            trace += "\n  2 s later center " + block.Center.ToString("F2");
            Debug.Log("hold: jam:" + trace);
            Assert.Less(block.Center.y, 0.1f, "the released block should have dropped to the floor");
            Assert.Less(block.Center.z, 10f, "the released block ended up behind the wall");
        }

        // ---------------------------------------------------------------- G3: many candidates

        static string Stats(List<double> samples)
        {
            samples.Sort();
            double total = 0;
            foreach (double sample in samples) total += sample;
            return (total / samples.Count).ToString("0.00") + " avg, " + samples[samples.Count / 2].ToString("0.00") + " median, "
                + samples[samples.Count * 95 / 100].ToString("0.00") + " p95, " + samples[samples.Count - 1].ToString("0.00") + " max";
        }

        List<double> TimeTicks(int ticks)
        {
            var samples = new List<double>(ticks);
            var watch = new Stopwatch();
            for (int i = 0; i < ticks; i++)
            {
                watch.Restart();
                Game.Tick();
                watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            return samples;
        }

        [Test]
        public void HoldCostAgainstAFenceStaysWithinBudget()
        {
            Prop plank = null, chair = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 300f);
                // A fence: 0.1 thick pickets every 0.3 units, 3 high, at z = 12.
                for (int i = -60; i <= 60; i++)
                    TestHelpers.Box(ctx, new Vector3(i * 0.3f, 1.5f, 12f), new Vector3(0.1f, 3f, 0.1f));
                plank = ctx.AddProp(BasicToys.Block(new Vector3(3f, 0.3f, 0.3f)), new Vector3(-3f, 0.15f, 3f));
                chair = ctx.AddProp(Chair(), new Vector3(3f, 0f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(60);
            int gcBefore = System.GC.CollectionCount(0);
            string idle = Stats(TimeTicks(300));

            Grab(plank);
            LookAt(new Vector3(0.15f, 1.5f, 12f));
            Run(30);
            float plankWidth = 3f * plank.Scale;
            string plankStats = Stats(TimeTicks(300));
            LookAt(new Vector3(-8f, 0f, 3f));
            Run(2);
            Drop();
            Run(30);

            Grab(chair);
            LookAt(new Vector3(0.15f, 1.5f, 12f));
            Run(30);
            float chairWidth = chair.Scale;
            List<double> chairSamples = TimeTicks(300);
            double chairMedian;
            {
                var sorted = new List<double>(chairSamples);
                sorted.Sort();
                chairMedian = sorted[sorted.Count / 2];
            }
            string chairStats = Stats(chairSamples);
            int gcAfter = System.GC.CollectionCount(0);

            Debug.Log("hold: G3 (ms per Game.Tick): idle " + idle + "; plank (1 box, " + plankWidth.ToString("0.0") + " wide) on a picket fence " + plankStats
                + "; chair (6 boxes, " + chairWidth.ToString("0.0") + " wide) on the fence " + chairStats + "; gen0 collections during the test " + (gcAfter - gcBefore));
            Assert.Less(chairMedian, 4.0, "a typical hold tick with a six-collider prop against a fence takes more than 4 ms in the editor");
        }

        /// <summary>The overhead drop as it happens in a level: a tall room, default MaxScale, look straight up, let go.</summary>
        [TestCase(30f, 89f)]
        [TestCase(30f, 86f)]
        [TestCase(12f, 89f)]
        public void PropReleasedAgainstAHighCeilingDoesNotShootThePlayerAway(float ceiling, float pitch)
        {
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 40f, ceiling);
                TestHelpers.Box(ctx, new Vector3(0f, ceiling + 0.5f, 0f), new Vector3(82f, 1f, 82f));
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(10);
            Grab(block);
            Game.Player.Pitch = pitch;
            Run(3);
            Assert.IsTrue(Game.Grabber.PlacementValid);
            float height = block.Center.y;
            Drop();
            Assert.IsFalse(Physics.GetIgnoreCollision(block.Colliders[0], Game.Player.Collider), "test setup: released well clear of the player");

            float fastest = 0f, highest = 0f, lowest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(Mathf.Sqrt(2f * height / Game.Gravity) + 4f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                highest = Mathf.Max(highest, Game.Player.Position.y);
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
            }
            float thrown = new Vector2(Game.Player.Position.x, Game.Player.Position.z).magnitude;
            Debug.Log("hold: D3: ceiling " + ceiling + " pitch " + pitch + ": block " + block.Scale.ToString("0.0") + " units, mass " + block.Mass.ToString("0") + ", released at y=" + height.ToString("0.0")
                + "; player fastest " + fastest.ToString("0.0") + " u/s, y " + lowest.ToString("0.00") + ".." + highest.ToString("0.00") + ", thrown " + thrown.ToString("0.0") + " units; block ends " + block.Center.ToString("F1"));
            Assert.Less(fastest, 1.5f * Player.SprintSpeed, "a " + block.Scale.ToString("0.0") + "-unit prop let go under a ceiling " + ceiling + " high shot the player away at "
                + fastest.ToString("0.0") + " u/s and threw them " + thrown.ToString("0.0") + " units");
        }

        /// <summary>The same drop with a wall behind the player: where does the shove put them?</summary>
        [TestCase(0.5f)]
        [TestCase(1.5f)]
        [TestCase(3f)]
        public void PropReleasedOverheadNextToAWallDoesNotPushThePlayerThroughIt(float clearance)
        {
            const float ceiling = 30f, half = 40f;
            Prop block = null;
            int respawns = 0;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, half, ceiling);
                TestHelpers.Box(ctx, new Vector3(0f, ceiling + 0.5f, 0f), new Vector3(82f, 1f, 82f));
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, -half + clearance + 4f));
                // Back to the wall at z = -40 (the wall is one unit thick).
                ctx.SetSpawn(new Vector3(0f, 0f, -half + clearance), 0f);
            });
            Game.Events.PlayerRespawned += e => respawns++;
            Run(10);
            Grab(block);
            Game.Player.Pitch = 89f;
            Run(3);
            float height = block.Center.y;
            Drop();

            float fastest = 0f, lowestZ = 0f, lowestY = 0f;
            for (int i = 0; i < TestHelpers.Ticks(Mathf.Sqrt(2f * height / Game.Gravity) + 4f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
                lowestZ = Mathf.Min(lowestZ, Game.Player.Position.z);
                lowestY = Mathf.Min(lowestY, Game.Player.Position.y);
            }
            Debug.Log("hold: D4: " + clearance + " from the wall: block " + block.Scale.ToString("0.0") + " units mass " + block.Mass.ToString("0") + "; player fastest " + fastest.ToString("0.0")
                + " u/s, ends " + Game.Player.Position.ToString("F2") + " (furthest back z " + lowestZ.ToString("F2") + ", lowest y " + lowestY.ToString("F2") + "), respawns " + respawns + "; block ends " + block.Center.ToString("F1"));
            Assert.AreEqual(0, respawns, "the player left the level");
            Assert.Greater(lowestZ, -half - 0.05f, "the player was pushed into or through the wall behind them (inner face at z = -40, they reached z = " + lowestZ.ToString("F2") + ")");
        }

        // ---------------------------------------------------------------- mass properties after a hold

        [Test]
        public void InertiaAndCenterOfMassFollowTheScaleThroughAHold()
        {
            Prop chair = null, block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 30f);
                chair = ctx.AddProp(Chair(), new Vector3(-2f, 0f, 4f));
                block = ctx.AddProp(BasicToys.Block(new Vector3(0.4f, 0.6f, 1f)), new Vector3(2f, 0.3f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Run(30);
            Vector3 comBefore = chair.Transform.InverseTransformPoint(chair.Body.worldCenterOfMass);
            Vector3 inertiaBefore = chair.Body.inertiaTensor;
            float massBefore = chair.Mass;

            Grab(chair);
            LookAt(new Vector3(-8f, 0f, 20f));
            Run(3);
            Drop();
            float s = chair.Scale;
            Assert.Greater(s, 1.2f, "test setup: the chair grew");
            Vector3 comAfter = chair.Transform.InverseTransformPoint(chair.Body.worldCenterOfMass);
            Assert.Less(Vector3.Distance(comBefore, comAfter), 1e-3f, "the center of mass moved within the toy when it was scaled");
            Assert.AreEqual(massBefore * s * s * s, chair.Mass, massBefore * s * s * s * 1e-3f);
            Vector3 inertiaAfter = chair.Body.inertiaTensor;
            float s5 = Mathf.Pow(s, 5f);
            Assert.AreEqual(inertiaBefore.x * s5, inertiaAfter.x, inertiaBefore.x * s5 * 0.01f, "inertia should grow with scale^5");
            Assert.AreEqual(inertiaBefore.y * s5, inertiaAfter.y, inertiaBefore.y * s5 * 0.01f);
            Assert.AreEqual(inertiaBefore.z * s5, inertiaAfter.z, inertiaBefore.z * s5 * 0.01f);

            RunSeconds(1f);
            Grab(block);
            LookAt(new Vector3(8f, 0f, 20f));
            Run(3);
            Drop();
            float b = block.Scale;
            float m = block.Mass;
            Vector3 size = new Vector3(0.4f, 0.6f, 1f) * b;
            Vector3 expected = new Vector3(size.y * size.y + size.z * size.z, size.x * size.x + size.z * size.z, size.x * size.x + size.y * size.y) * (m / 12f);
            Vector3 actual = block.Body.inertiaTensor;
            Assert.AreEqual(expected.x, actual.x, expected.x * 0.01f, "inertia of a box of the dropped size");
            Assert.AreEqual(expected.y, actual.y, expected.y * 0.01f);
            Assert.AreEqual(expected.z, actual.z, expected.z * 0.01f);
        }

    }
}
