using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    public class PerspectiveTests : SimTest
    {
        readonly List<PropHoldEvent> drops = new List<PropHoldEvent>();

        Prop BuildRoomWith(System.Func<LevelContext, Prop> addProp, float half = 20f)
        {
            Prop prop = null;
            drops.Clear();
            Build(ctx =>
            {
                TestHelpers.Room(ctx, half);
                prop = addProp(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Game.Events.PropDropped += drops.Add;
            Run(10);
            return prop;
        }

        void Grab(Prop prop)
        {
            LookAt(prop.Center);
            Click();
            Assert.AreSame(prop, Game.Grabber.Held, "the prop should be in hand after the click");
        }

        PropHoldEvent Drop()
        {
            int before = drops.Count;
            Click();
            Assert.IsNull(Game.Grabber.Held, "nothing should be held after the second click");
            Assert.AreEqual(before + 1, drops.Count, "PropDropped should fire once");
            return drops[drops.Count - 1];
        }

        [Test]
        public void NearToFarGrowsByTheDistanceRatio()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f)));
            Grab(block);
            // Toward the far wall, a little above the floor: the block comes down on the floor well out.
            LookAt(new Vector3(0f, 1f, 19.5f));
            Run(5);
            PropHoldEvent drop = Drop();

            Assert.Greater(drop.DropDistance, drop.GrabDistance * 3f, "the block should have travelled far out");
            Assert.Greater(drop.NewScale, drop.OldScale * 3f);
            float scaleRatio = drop.NewScale / drop.OldScale;
            float distanceRatio = drop.DropDistance / drop.GrabDistance;
            Assert.AreEqual(distanceRatio, scaleRatio, distanceRatio * 0.05f);
            Assert.AreEqual(drop.NewScale, block.Scale, 1e-5f);
        }

        [Test]
        public void FarToNearShrinks()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(2f), new Vector3(0f, 1f, 15f)));
            Grab(block);
            LookAt(new Vector3(0f, 0f, 2.5f));
            Run(5);
            PropHoldEvent drop = Drop();

            Assert.Less(drop.NewScale, drop.OldScale * 0.5f);
            float scaleRatio = drop.NewScale / drop.OldScale;
            float distanceRatio = drop.DropDistance / drop.GrabDistance;
            Assert.AreEqual(distanceRatio, scaleRatio, distanceRatio * 0.05f);
        }

        [Test]
        public void ApparentSizeStaysConstantWhileLookingAround()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(0.6f), new Vector3(0f, 0.3f, 4f)));
            Grab(block);
            float k = Game.Grabber.Ratio;
            Assert.Greater(k, 0f);

            var views = new[]
            {
                new Vector2(0f, -20f), new Vector2(40f, -10f), new Vector2(90f, 5f), new Vector2(150f, -35f),
                new Vector2(-120f, 20f), new Vector2(-60f, -60f), new Vector2(10f, 30f),
            };
            float smallest = float.MaxValue, largest = 0f;
            foreach (Vector2 view in views)
            {
                Game.Player.Yaw = view.x;
                Game.Player.Pitch = view.y;
                Run(2);
                Assert.IsTrue(Game.Grabber.PlacementValid, "there is room on every one of these view rays");
                float distance = Vector3.Distance(Game.Player.Eye, block.Center);
                Assert.AreEqual(k, block.Scale / distance, k * 1e-3f, "scale / distance changed at view " + view);
                smallest = Mathf.Min(smallest, block.Scale);
                largest = Mathf.Max(largest, block.Scale);
            }
            Assert.Greater(largest, smallest * 2f, "the views land at very different distances");
        }

        [Test]
        public void MaxScaleClampsTheHold()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 2f),
                new PropOptions { MaxScale = 2f }));
            Grab(block);
            LookAt(new Vector3(0f, 6f, 20f));
            Run(5);
            Assert.AreEqual(2f, block.Scale, 1e-3f);
            PropHoldEvent drop = Drop();
            Assert.AreEqual(2f, drop.NewScale, 1e-3f);
        }

        [Test]
        public void MinScaleClampsTheHold()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 12f),
                new PropOptions { MinScale = 0.5f }));
            Grab(block);
            // Straight down at the floor by the feet: without the clamp the block would end up tiny.
            LookAt(new Vector3(0f, 0f, 0.8f));
            Run(5);
            Assert.GreaterOrEqual(block.Scale, 0.5f - 1e-4f);
            PropHoldEvent drop = Drop();
            Assert.GreaterOrEqual(drop.NewScale, 0.5f - 1e-4f);
        }

        [Test]
        public void DroppedPropIsFreeOfTheWorldAndSettles()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 3f)));
            Grab(block);
            // Out to the far wall, a little above the floor: it ends up large, against the wall, and drops.
            LookAt(new Vector3(3f, 1.6f, 20f));
            Run(5);
            Drop();

            Assert.Less(TestHelpers.DeepestOverlap(Game, block), 1e-3f, "the block was released inside something");
            Vector3 released = block.Center;
            float fastest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(3f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, block.Body.linearVelocity.magnitude);
            }
            Assert.Less(block.Body.linearVelocity.magnitude, 0.5f, "the block has not settled after 3 s");
            Assert.Less(fastest, 10f, "the block was thrown around after the drop");
            Assert.Less(Vector3.Distance(released, block.Center), 1.5f, "the block ended up far from where it was put");
            Assert.Less(TestHelpers.DeepestOverlap(Game, block), 0.02f);
        }

        [Test]
        public void MassScalesWithTheCubeOfScale()
        {
            Prop block = null, ball = null, drum = null, wedge = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(new Vector3(1f, 2f, 0.5f)), new Vector3(0f, 1f, 5f), new PropOptions { Density = 3f });
                ball = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(3f, 0.5f, 5f));
                drum = ctx.AddProp(BasicToys.Cylinder(0.5f, 1f), new Vector3(6f, 0.5f, 5f));
                wedge = ctx.AddProp(BasicToys.Wedge(2f, 1f, 3f), new Vector3(-4f, 0.5f, 5f), new PropOptions { Scale = 2f });
            });

            Assert.AreEqual(1f, block.BaseVolume, 1e-4f);
            Assert.AreEqual(3f, block.Mass, 1e-3f);
            Assert.AreEqual(4f / 3f * Mathf.PI * 0.125f, ball.BaseVolume, 1e-4f);
            Assert.AreEqual(Mathf.PI * 0.25f, drum.BaseVolume, Mathf.PI * 0.25f * 0.02f, "a 24-sided prism is within 2% of a cylinder");
            Assert.AreEqual(3f, wedge.BaseVolume, 1e-3f, "half of a 2 x 1 x 3 box");
            Assert.AreEqual(3f * 8f, wedge.Mass, 1e-2f);

            block.SetScale(2f);
            Assert.AreEqual(3f * 8f, block.Mass, 1e-2f);
            block.SetScale(0.5f);
            Assert.AreEqual(3f / 8f, block.Mass, 1e-4f);

            // And through the mechanic itself.
            Game.Player.Teleport(Vector3.zero, 0f);
            Run(5);
            Game.Events.PropDropped += drops.Add;
            drops.Clear();
            Grab(ball);
            LookAt(new Vector3(12f, 0f, 30f));
            Run(3);
            PropHoldEvent drop = Drop();
            Assert.Greater(drop.NewScale, 1.5f);
            float expected = ball.Density * ball.BaseVolume * Mathf.Pow(drop.NewScale, 3f);
            Assert.AreEqual(expected, ball.Mass, expected * 1e-3f);
        }

        [Test]
        public void CannotGrabThePropUnderfoot()
        {
            Prop platform = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                platform = ctx.AddProp(BasicToys.Block(new Vector3(3f, 1f, 3f)), new Vector3(0f, 0.5f, 0f));
                ctx.SetSpawn(new Vector3(0f, 1.05f, 0f), 0f);
            });
            Run(30);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreSame(platform, Game.Player.GroundProp);

            Game.Player.Pitch = -80f;
            Click();
            Assert.IsNull(Game.Grabber.Held, "the block under the player's feet must not be grabbable");

            // From the floor next to it the same block can be taken.
            Game.Player.Teleport(new Vector3(0f, 0f, -6f), 0f);
            Run(10);
            Grab(platform);
        }

        [Test]
        public void DropOntoThePlayerDoesNotLaunchThePlayer()
        {
            Prop block = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Block(1.5f), new Vector3(0f, 0.75f, 6f)));
            Grab(block);
            // Straight down: the block comes to rest on the floor around the player's feet.
            Game.Player.Pitch = -89f;
            Run(5);
            Vector3 standing = Game.Player.Position;
            Collider propCollider = block.Colliders[0];
            Assert.IsTrue(Physics.ComputePenetration(propCollider, propCollider.transform.position, propCollider.transform.rotation,
                Game.Player.Collider, Game.Player.Position, Quaternion.identity, out _, out float depth) && depth > 0.05f,
                "the test needs the block to end up overlapping the player");
            Drop();
            Assert.IsTrue(Physics.GetIgnoreCollision(propCollider, Game.Player.Collider), "collision with the player should be suspended");

            float fastest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(2f); i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            }
            Assert.Less(fastest, 1f, "the player was pushed by the prop that was dropped on them");
            Assert.Less(Vector3.Distance(standing, Game.Player.Position), 0.1f);

            // Once the player has walked clear, the prop is solid for them again.
            Game.Player.Teleport(new Vector3(0f, 0f, -10f), 0f);
            Run(3);
            Assert.IsFalse(Physics.GetIgnoreCollision(propCollider, Game.Player.Collider));
        }

        [Test]
        public void ConvexWedgeIsPlacedFlushOnTheFloor()
        {
            Prop wedge = BuildRoomWith(ctx => ctx.AddProp(BasicToys.Wedge(1.5f, 0.8f, 1f), new Vector3(0f, 0.4f, 3f)));
            Grab(wedge);
            LookAt(new Vector3(-2f, 0.8f, 14f));
            Run(5);
            PropHoldEvent drop = Drop();
            Assert.Greater(drop.NewScale, 2f);

            // The lowest point of the hull is just above the floor (y = 0): not inside it, not floating.
            float lowest = LowestPoint(wedge);
            Assert.GreaterOrEqual(lowest, -1e-3f, "the wedge was placed inside the floor");
            Assert.Less(lowest, 0.06f, "the wedge was left hanging above the floor");
            Assert.Less(TestHelpers.DeepestOverlap(Game, wedge), 1e-3f);

            Quaternion placed = wedge.Rotation;
            RunSeconds(2f);
            Assert.Less(Quaternion.Angle(placed, wedge.Rotation), 1f, "the wedge tipped over after being put down flat");
            Assert.AreEqual(0f, LowestPoint(wedge), 0.02f);
            Assert.Less(wedge.Body.linearVelocity.magnitude, 0.5f);
        }

        static float LowestPoint(Prop prop)
        {
            float lowest = float.MaxValue;
            foreach (Collider collider in prop.Colliders)
            {
                var mesh = (MeshCollider)collider;
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    lowest = Mathf.Min(lowest, collider.transform.TransformPoint(vertex).y);
            }
            return lowest;
        }
    }
}
