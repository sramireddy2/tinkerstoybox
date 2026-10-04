using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The funnel is a size gate: a ball passes the throat when its radius is smaller, with real physics.</summary>
    public class FunnelTests : SimTest
    {
        // Funnel S of Level 8: throat 0.40, mouth 1.0, cone 0.42 deep (35 degrees), tube 1.0, chamber 1.2.
        static FunnelOptions Small(float rimY) => new FunnelOptions
        {
            Name = "S", Axis = new Vector2(0f, 9f), RimY = rimY, MouthRadius = 1f, ThroatRadius = 0.4f, ConeDepth = 0.42f, TubeLength = 1f,
            ChamberHeight = 1.2f, CollarHalfSize = new Vector2(3f, 3f),
        };

        Prop Marble(float scale, Vector3 position) =>
            ToyCatalog.Add(Game.Context, ToyId.Marble, position, scale, null, o => o.Name = "Marble " + scale);

        [Test]
        public void TheScaleWindow_OfLevel8_HoldsInPhysics()
        {
            FunnelResult funnel = null;
            Build(ctx =>
            {
                funnel = Funnel.Build(ctx, Small(3f));
                ctx.SetSpawn(new Vector3(0f, 3f, 7f), 0f);
            });
            Assert.AreEqual(3f - 0.42f, funnel.ThroatY, 1e-4f);
            Assert.AreEqual(3f - 2.62f, funnel.FloorY, 1e-4f, "the plate of funnel S is 2.62 under the rim");
            Assert.IsTrue(funnel.Footprint.Contains(new Vector3(0.5f, 1f, 9.5f)));
            Assert.IsTrue(funnel.ChamberBox.Contains(new Vector3(0f, 1f, 9f)));
            Assert.IsFalse(funnel.ChamberBox.Contains(new Vector3(0f, 2f, 9f)), "the tube is not the chamber");

            // 0.64 is the intended size, 0.76 the top of the window (5 % clearance), 0.50 the bottom.
            foreach (float scale in new[] { 0.64f, 0.76f, 0.5f })
            {
                Prop marble = Marble(scale, new Vector3(0.55f, 3.5f, 9.2f));
                bool through = TestHelpers.RunUntil(Game, () => marble.Center.y < funnel.ThroatY - 1f && marble.Velocity.sqrMagnitude < 0.05f, 6f);
                Assert.IsTrue(through, "a marble of scale " + scale + " (radius " + scale * 0.5f + ") passes a throat of 0.40; it stopped at " + marble.Center);
                Assert.AreEqual(funnel.FloorY + scale * 0.5f, marble.Center.y, 0.03f, "and comes to rest on the plate");
                Assert.IsTrue(funnel.ChamberBox.Contains(marble.Center));
                Game.Context.RemoveProp(marble);
            }

            // 0.84 (radius 0.42) and 1.2 are too big: they sit in the mouth.
            foreach (float scale in new[] { 0.84f, 1.2f })
            {
                Prop marble = Marble(scale, new Vector3(0.55f, 3.5f + scale * 0.5f, 9.2f));
                RunSeconds(4f);
                Assert.Greater(marble.Center.y, funnel.ThroatY, "a marble of scale " + scale + " does not pass; it is at " + marble.Center);
                Assert.Less(marble.Velocity.magnitude, 0.2f, "it sits in the cone");
                Assert.Less(new Vector2(marble.Center.x, marble.Center.z - 9f).magnitude, 0.1f, "on the axis");
                Game.Context.RemoveProp(marble);
            }
        }

        [Test]
        public void EveryRing_IsCircumscribed_AndFacesTheInside()
        {
            FunnelResult funnel = null;
            Build(ctx =>
            {
                funnel = Funnel.Build(ctx, Small(3f));
                ctx.SetSpawn(new Vector3(0f, 3f, 7f), 0f);
            });
            PhysicsScene physics = Game.PhysicsScene;
            var axis = new Vector3(0f, 0f, 9f);
            float nearest = float.MaxValue, farthest = 0f;
            for (int i = 0; i < 128; i++)
            {
                float angle = Mathf.PI * 2f * i / 128f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // In the tube, half way down.
                Assert.IsTrue(physics.Raycast(axis + Vector3.up * (funnel.ThroatY - 0.5f), direction, out RaycastHit hit, 5f, Layers.SolidMask), "the tube has a wall at " + angle);
                nearest = Mathf.Min(nearest, hit.distance);
                farthest = Mathf.Max(farthest, hit.distance);
                Assert.Less(Vector3.Dot(hit.normal, direction), -0.9f, "the tube wall faces the axis");
            }
            Assert.GreaterOrEqual(nearest, 0.4f - 1e-3f, "no clearance is smaller than the nominal radius");
            Assert.LessOrEqual(farthest, 0.4f / Mathf.Cos(Mathf.PI / 32f) + 1e-3f, "a 32-gon about the circle");

            // The cone, from above: a slope of 35 degrees facing up and inward.
            Assert.IsTrue(physics.Raycast(new Vector3(0.7f, 5f, 9f), Vector3.down, out RaycastHit cone, 5f, Layers.SolidMask));
            Assert.AreEqual(3f - 0.7f * 0.3f, cone.point.y, 0.02f, "0.70 down per unit of radius: 0.3 in from the mouth is 0.21 down");
            Assert.AreEqual(35f, Vector3.Angle(cone.normal, Vector3.up), 1.5f);
            Assert.Less(cone.normal.x, 0f, "leaning toward the axis");

            // The chamber: a floor under the tube and a ceiling round it.
            Assert.IsTrue(physics.Raycast(new Vector3(0f, 2f, 9f), Vector3.down, out RaycastHit floor, 5f, Layers.SolidMask));
            Assert.AreEqual(funnel.FloorY, floor.point.y, 1e-3f);
            Assert.IsTrue(physics.Raycast(new Vector3(0.7f, 1f, 9f), Vector3.up, out RaycastHit ceiling, 5f, Layers.SolidMask));
            Assert.AreEqual(funnel.FloorY + 1.2f, ceiling.point.y, 1e-3f);
        }

        [Test]
        public void TheCollar_IsFloorAroundTheMouth_AndThePlayerWalksOnIt()
        {
            FunnelResult funnel = null;
            Build(ctx =>
            {
                funnel = Funnel.Build(ctx, Small(0f));
                // The rest of the lid, laid from boxes around the collar (x -3..3, z 6..12).
                TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 12f));
                ctx.SetSpawn(new Vector3(-2.5f, 0f, 3f), 0f);
            });
            PhysicsScene physics = Game.PhysicsScene;
            foreach (Vector3 point in new[] { new Vector3(2.9f, 2f, 11.9f), new Vector3(-2.9f, 2f, 6.1f), new Vector3(1.2f, 2f, 9f), new Vector3(0f, 2f, 10.3f), new Vector3(-2.9f, 2f, 9f) })
            {
                Assert.IsTrue(physics.Raycast(point, Vector3.down, out RaycastHit hit, 5f, Layers.SolidMask), "the collar has floor at " + point);
                Assert.AreEqual(0f, hit.point.y, 1e-3f, "at rim height, at " + point);
            }

            // Along the collar's edge, past the mouth, without falling in or tripping on the seam.
            Input.Hold = new InputFrame { MoveZ = 1f };
            float lowest = 0f, highest = 0f;
            for (int i = 0; i < 150 && Game.Player.Position.z < 11.3f; i++)
            {
                Game.Tick();
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Assert.Greater(Game.Player.Position.z, 11f);
            Assert.Greater(lowest, -0.05f);
            Assert.Less(highest, 0.05f);
            Assert.IsTrue(Game.Player.Grounded);
        }
    }
}
