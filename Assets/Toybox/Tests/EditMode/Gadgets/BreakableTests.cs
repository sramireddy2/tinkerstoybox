using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>A breakable gives way to mass and speed along its direction, judged without collision callbacks.</summary>
    public class BreakableTests : SimTest
    {
        static BreakableOptions Barricade() => new BreakableOptions
        {
            Name = "barricade", Center = new Vector3(0f, 6f, 10.6f), Size = new Vector3(11f, 12f, 1.2f),
            AcceptTag = "smasher", MinMass = 14f, MinSpeed = 2.5f, Direction = Vector3.forward,
        };

        static Prop Launch(LevelContext ctx, float size, float speed, string tag = "smasher")
        {
            Prop block = ctx.AddProp(BasicToys.Block(size), new Vector3(0f, size * 0.5f + 0.01f, 4f),
                new PropOptions { Name = "Block " + size, Density = 1f, Friction = 0f, Tags = tag != null ? new[] { tag } : null });
            block.Body.linearVelocity = new Vector3(0f, 0f, speed);
            return block;
        }

        [Test]
        public void AHeavyFastHit_Breaks_ALightOrSlowOne_Bonks()
        {
            Breakable wall = null;
            var bonks = new List<float>();
            var broke = new List<float>();
            Build(ctx =>
            {
                // An ice floor: the blocks arrive at the speed they were launched with.
                var ice = new PhysicsMaterial("Ice") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                TestHelpers.Floor(ctx).GetComponent<Collider>().sharedMaterial = ice;
                ctx.OnDispose(() => Object.DestroyImmediate(ice));
                wall = new Breakable(ctx, Barricade());
                wall.Bonked += (p, share) => bonks.Add(share);
                wall.Broke += (p, speed) => broke.Add(speed);
                ctx.SetSpawn(new Vector3(8f, 0f, 0f), 0f);
            });
            Assert.AreEqual(1, wall.Body.GetComponentsInChildren<Collider>().Length);

            // Mass 8 (a 2-cube) at 8 units a second: fast enough, too light.
            Prop light = Launch(Game.Context, 2f, 8f);
            RunSeconds(1.5f);
            Assert.IsFalse(wall.Broken);
            Assert.AreEqual(1, bonks.Count, "one bonk per approach");
            Assert.AreEqual(8f / 14f, bonks[0], 0.01f, "it shudders in proportion to mass / MinMass");
            Assert.Less(light.Center.z, 10f, "and the block was stopped by it");
            Game.Context.RemoveProp(light);

            // Mass 27 (a 3-cube) at 1.5: heavy enough, too slow.
            Prop slow = Launch(Game.Context, 3f, 1.5f);
            RunSeconds(4f);
            Assert.IsFalse(wall.Broken);
            Assert.AreEqual(2, bonks.Count);
            Game.Context.RemoveProp(slow);

            // The wrong tag, heavy and fast.
            Prop stranger = Launch(Game.Context, 3f, 8f, null);
            RunSeconds(1.5f);
            Assert.IsFalse(wall.Broken, "only a smasher breaks it");
            Game.Context.RemoveProp(stranger);

            // Mass 27 at 8.
            Prop heavy = Launch(Game.Context, 3f, 8f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => wall.Broken, 2f));
            Assert.AreEqual(1, broke.Count);
            Assert.AreEqual(8f, broke[0], 0.5f, "the speed it arrived with, although the hit itself has already slowed it");
            Assert.AreSame(heavy, wall.BrokenBy);
            Assert.IsFalse(wall.Body.GetComponentInChildren<Collider>().enabled, "the collider is gone");
            RunSeconds(1f);
            Assert.Greater(heavy.Center.z, 11f, "and the way is open");

            wall.Reset();
            Assert.IsFalse(wall.Broken);
            Assert.IsTrue(wall.Body.GetComponentInChildren<Collider>().enabled);
        }

        [Test]
        public void ATopplingDomino_BreaksTheBarricade_WithItsTip()
        {
            // Level 4 in essence: a domino 8.2 tall and 33 heavy falls like a drawbridge. Its centre moves
            // at half the speed of its tip; the speed that counts is that of the point that touches.
            Breakable wall = null;
            Prop domino = null;
            float speed = 0f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                wall = new Breakable(ctx, Barricade());
                wall.Broke += (p, s) => speed = s;
                domino = ToyCatalog.Add(ctx, ToyId.Domino, new Vector3(0f, 4.1f + 0.01f, 4.6f), 4.1f, null, o => o.Tags = new[] { "smasher" });
                ctx.SetSpawn(new Vector3(8f, 0f, -4f), 0f);
            });
            Assert.AreEqual(33f, domino.Mass, 1f);
            RunSeconds(0.5f);
            Assert.IsFalse(wall.Broken, "standing, it does nothing");
            // A nudge over its balance point, toward the wall: half a radian a second about its front foot edge.
            domino.Body.angularVelocity = new Vector3(0.5f, 0f, 0f);
            domino.Body.linearVelocity = new Vector3(0f, 0.5f * 0.615f, 0.5f * 4.1f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => wall.Broken, 6f), "the falling domino breaks through (it is at " + domino.Center + ", turned " + domino.Rotation.eulerAngles + ")");
            Assert.GreaterOrEqual(speed, 5f, "LEVELS appendix B: Broke speed >= 5 (was " + speed + ")");
            RunSeconds(2f);
            Assert.Less(domino.Center.y, 1.5f, "it slams flat through the arch");
        }

        [Test]
        public void ALeaningWeight_DoesNotBreakIt()
        {
            Breakable wall = null;
            Prop slab = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                wall = new Breakable(ctx, Barricade());
                // A heavy slab put down against the face, with no speed into it.
                slab = ctx.AddProp(BasicToys.Block(new Vector3(4f, 6f, 1f)), new Vector3(0f, 3.01f, 9.45f), new PropOptions { Density = 2f, Tags = new[] { "smasher" } });
                ctx.SetSpawn(new Vector3(8f, 0f, 0f), 0f);
            });
            RunSeconds(3f);
            Assert.AreEqual(48f, slab.Mass, 0.1f);
            Assert.IsFalse(wall.Broken, "mass alone is not enough: it needs room to fall");
        }

        [Test]
        public void TheClock_IsFlattened_ByAWatchedDrivenBody()
        {
            Breakable clock = null;
            Prop baseball = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                clock = new Breakable(ctx, new BreakableOptions
                {
                    Name = "clock", Center = new Vector3(0f, 1.4f, 6f), Size = new Vector3(1f, 2.8f, 1f), MinMass = 8f, MinSpeed = 6f,
                    Direction = Vector3.down, OnBreak = BreakMode.SwapCollider, FlattenedHeight = 1f,
                });
                baseball = ToyCatalog.Add(ctx, ToyId.Baseball, new Vector3(0f, 8f, 6f));
                clock.Watch(baseball);
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.AreEqual(9.05f, baseball.Mass, 0.1f);
            // A gadget lowers it at 13 units a second, as the last path of the machine does, then lets go.
            Mover mover = baseball.BeginDrive();
            float y = 8f;
            Game.Context.OnUpdate(dt =>
            {
                if (!baseball.Driven) return;
                y -= 13f * dt;
                if (y < 3.6f)
                {
                    baseball.EndDrive(new Vector3(0f, -13f, 0f));
                    return;
                }
                mover.MoveTo(new Vector3(0f, y, 6f));
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => clock.Broken, 2f));
            Assert.AreEqual(13f, clock.BreakSpeed, 1f);
            Collider collider = clock.Body.GetComponentInChildren<Collider>();
            Assert.IsTrue(collider.enabled, "the clock is still there");
            Assert.AreEqual(1f, collider.bounds.max.y, 0.01f, "one unit high now");
            Assert.AreEqual(0f, collider.bounds.min.y, 0.01f, "standing on the same floor");
            RunSeconds(1.5f);
            Assert.Less(baseball.Center.y, 2f, "and the ball rests on what is left");
        }
    }
}
