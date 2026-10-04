using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Size is area: the wind takes a small light toy and leaves a big one; it takes the player only in the air.</summary>
    public class WindStreamTests : SimTest
    {
        WindStream wind;

        void BuildGale(LevelContext ctx)
        {
            TestHelpers.Floor(ctx, 120f);
            wind = new WindStream(ctx, new WindStreamOptions
            {
                Center = new Vector3(0f, 4.5f, 19f), Size = new Vector3(10f, 9f, 50f), Direction = Vector3.forward, PlayerAirPush = 3f, PropDrag = 12f, MaxPropMass = 1f,
            });
            ctx.SetSpawn(new Vector3(8f, 0f, 0f), 0f);
        }

        [Test]
        public void LooseProps_AreBlown_ByDragOverScale_AndHeavyOnesStay()
        {
            Prop small = null, medium = null, heavy = null, outside = null;
            Build(ctx =>
            {
                BuildGale(ctx);
                // In the air, so that friction does not blur the numbers.
                small = ctx.AddProp(BasicToys.Block(1f), new Vector3(-3f, 6f, 0f), new PropOptions { Name = "Small", Scale = 0.2f });       // mass 0.008 -> 0.01
                medium = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 6f, 0f), new PropOptions { Name = "Medium", Scale = 0.8f });     // mass 0.51
                heavy = ctx.AddProp(BasicToys.Block(1f), new Vector3(3f, 6f, 0f), new PropOptions { Name = "Heavy", Scale = 1.5f });       // mass 3.4
                outside = ctx.AddProp(BasicToys.Block(1f), new Vector3(-9f, 6f, 0f), new PropOptions { Name = "Outside", Scale = 0.2f });
            });
            Assert.AreEqual(40f, wind.PropAcceleration(0.2f, 0.01f), 1e-4f, "12 / 0.2 = 60, capped at 40");
            Assert.AreEqual(15f, wind.PropAcceleration(0.8f, 0.51f), 1e-4f, "12 / 0.8");
            Assert.AreEqual(0f, wind.PropAcceleration(1.5f, 3.4f), 1e-4f, "heavier than MaxPropMass: unmoved");

            RunSeconds(0.4f);
            Assert.AreEqual(-22f * 0.1f * 0.4f, medium.Velocity.y, 0.05f, "and the wind carries nine tenths of what it blows");
            Assert.AreEqual(-22f * 0.4f, heavy.Velocity.y, 0.05f, "what it does not move falls as ever");
            Assert.AreEqual(40f * 0.4f, small.Velocity.z, 0.8f, "acceleration 40");
            Assert.AreEqual(15f * 0.4f, medium.Velocity.z, 0.3f, "acceleration 15");
            Assert.AreEqual(0f, heavy.Velocity.z, 1e-3f);
            Assert.AreEqual(0f, outside.Velocity.z, 1e-3f, "outside the stream nothing blows");
            Assert.IsTrue(wind.Contains(medium.Center));
            Assert.IsFalse(wind.Contains(outside.Center));
        }

        [Test]
        public void ThePlayer_IsCarriedInTheAir_AndStandsFirmOnTheGround()
        {
            Build(ctx =>
            {
                BuildGale(ctx);
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(1f);
            Assert.IsTrue(wind.PlayerInside);
            Assert.AreEqual(0f, Game.Player.Position.z, 0.01f, "on the ground the wind does not move the player");

            // One jump: 0.69 s in the air at 3 units per second squared.
            Input.Once.Jump = true;
            Run(2);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 2f));
            Assert.AreEqual(0.5f * 3f * 0.69f * 0.69f, Game.Player.Position.z, 0.15f, "blown downwind while airborne (LEVELS: about 0.7)");
        }

        [Test]
        public void TheStrength_ScalesEverything_AndSaysWhenItChanges()
        {
            Prop leaf = null;
            var changes = new List<float>();
            Build(ctx =>
            {
                BuildGale(ctx);
                leaf = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 8f, 0f), new PropOptions { Name = "Leaf", Scale = 0.8f });
            });
            wind.WindChanged += changes.Add;
            int mirrored = 0;
            Game.Events.WindChanged += e => mirrored++;
            wind.Strength = 0f;
            RunSeconds(0.3f);
            Assert.AreEqual(0f, leaf.Velocity.z, 1e-3f, "calm");
            // The fan of Level 15: strength = (scale / 0.6) squared.
            wind.Strength = (0.94f / 0.6f) * (0.94f / 0.6f);
            wind.Strength = wind.Strength;
            RunSeconds(0.2f);
            Assert.AreEqual(15f * 2.454f * 0.2f, leaf.Velocity.z, 0.5f);
            Assert.AreEqual(2, changes.Count, "one event per real change");
            Assert.AreEqual(0f, changes[0]);
            Assert.AreEqual(2.4544f, changes[1], 1e-3f);
            Assert.AreEqual(2, mirrored);
            wind.Reset();
            Assert.AreEqual(1f, wind.Strength, 1e-5f);
        }
    }
}
