using System.Collections;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Level 11: beams from the lid, blocked by what is in the world - and by nothing that is in the hand.</summary>
    public class LaserRainTests : SimTest
    {
        LaserRain rain;
        Prop card;

        void BuildCorridor(LevelContext ctx)
        {
            TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 12.5f), new Vector3(12f, 1f, 29f));          // floor: z -2 .. 27
            TestHelpers.Box(ctx, new Vector3(-6.5f, 4f, 11f), new Vector3(1f, 8f, 26f));             // side walls at x = +-6
            TestHelpers.Box(ctx, new Vector3(6.5f, 4f, 11f), new Vector3(1f, 8f, 26f));
            TestHelpers.Box(ctx, new Vector3(0f, 4f, -2.5f), new Vector3(14f, 8f, 1f));              // back wall
            TestHelpers.Box(ctx, new Vector3(-3.5f, 4f, 23.5f), new Vector3(5f, 8f, 1f));            // far wall with a door x -1..1, y 0..2.6
            TestHelpers.Box(ctx, new Vector3(3.5f, 4f, 23.5f), new Vector3(5f, 8f, 1f));
            TestHelpers.Box(ctx, new Vector3(0f, 5.3f, 23.5f), new Vector3(2f, 5.4f, 1f));
            TestHelpers.Box(ctx, new Vector3(0f, 8.5f, 11f), new Vector3(14f, 1f, 26f));             // the lid: underside 8
            TestHelpers.Box(ctx, new Vector3(-3.5f, 1.5f, 18f), new Vector3(1f, 3f, 10f));           // low walls: x -4..-3 and 3..4, z 13..23, top 3
            TestHelpers.Box(ctx, new Vector3(3.5f, 1.5f, 18f), new Vector3(1f, 3f, 10f));
            ctx.AddStatic(BasicToys.Cylinder(0.6f, 0.8f), new Vector3(0f, 0.4f, 3f));                // the card table
            rain = new LaserRain(ctx, new LaserRainOptions
            {
                Center = new Vector3(0f, 8f, 18f), Size = new Vector2(12f, 8f), Range = 8f, LatticePitch = 0.45f, ZapDelay = 0.1f,
                Lane = Zone.MinMax(new Vector3(-3f, 0f, 14f), new Vector3(3f, 8f, 22f)),
            });
            card = ToyCatalog.Add(ctx, ToyId.PlayingCard, new Vector3(0f, 0.81f, 3f), 0.5f);
            ctx.SetSpawn(new Vector3(0f, 0f, -1f), 0f);
        }

        [Test]
        public void TheLattice_MeasuresHowFarEveryBeamGets()
        {
            Build(BuildCorridor);
            Run(1);
            Assert.AreEqual(550, rain.BeamCount, 40, "LEVELS: about 550 beams at a pitch of 0.45");
            int onFloor = 0, onWalls = 0;
            for (int i = 0; i < rain.BeamCount; i++)
            {
                Vector3 origin = rain.BeamOrigin(i);
                Assert.AreEqual(8f, origin.y, 1e-4f);
                Assert.IsTrue(origin.x >= -6.001f && origin.x <= 6.001f && origin.z >= 13.999f && origin.z <= 22.001f, "inside the emitter: " + origin);
                if (Mathf.Abs(rain.BeamLength(i) - 8f) < 1e-3f) onFloor++;
                else if (Mathf.Abs(rain.BeamLength(i) - 5f) < 1e-3f) onWalls++;
                else Assert.Fail("beam " + i + " at " + origin + " ends after " + rain.BeamLength(i));
            }
            Assert.Greater(onWalls, 60, "the beams over the low walls end on their tops (3 high)");
            Assert.Greater(onFloor, 400);
            Assert.AreEqual(0f, rain.Coverage01, 1e-4f, "nothing roofs the lane");
            Assert.AreEqual(Vector3.down, rain.Direction);
        }

        [Test]
        public void WithoutARoof_TheRainZapsWhoeverWalksIn()
        {
            Build(BuildCorridor);
            int zapped = 0;
            rain.Zapped += () => zapped++;
            Game.Player.Teleport(new Vector3(1.5f, 0f, 11f), 0f);
            Input.Hold = new InputFrame { MoveZ = 1f, Sprint = true };
            float deepest = 0f;
            for (int i = 0; i < 180 && zapped == 0; i++)
            {
                Game.Tick();
                deepest = Mathf.Max(deepest, Game.Player.Position.z);
            }
            Assert.AreEqual(1, zapped);
            Assert.AreEqual(1, rain.Zaps);
            Assert.Less(deepest, 14f + 0.1f * 8f + 0.5f, "sprinting buys 0.8 of a unit, not eight (got to z = " + deepest + ")");
            Assert.Less(Game.Player.Position.z, 0f, "back at the spawn");
        }

        [Test]
        public void TheBot_RoofsTheLaneWithTheCard_AndWalksThroughDry()
        {
            Build(BuildCorridor);
            int zapped = 0, clear = 0;
            rain.Zapped += () => zapped++;
            rain.AllClear += () => clear++;
            var bot = new Bot(Game);
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 1.9f), 0.1f);
                yield return bot.Grab(card);
                yield return bot.DropAt(new Vector3(0f, 6.4f, 23f));
                yield return bot.Until(() => card.Center.y < 3.4f && card.Velocity.sqrMagnitude < 0.01f, 4f);
                yield return bot.Wait(0.3f);
                Assert.AreEqual(1f, rain.Coverage01, 1e-4f, "every beam over the lane ends on the card");
                yield return bot.WalkTo(new Vector3(1.8f, 0f, 3f));
                yield return bot.WalkTo(new Vector3(0f, 0f, 12f));
                yield return bot.WalkTo(new Vector3(0f, 0f, 22.5f));
                yield return bot.WalkTo(new Vector3(0f, 0f, 25.5f));
            }
            BotRunner.Run(Game, Script(), 60f);
            Assert.AreEqual(5.9f, card.Scale, 0.5f, "LEVELS: 5.9 (window 4.7 .. 7.0)");
            Assert.AreEqual(3.12f, card.Center.y, 0.05f, "lying on both walls");
            Assert.AreEqual(0, zapped, "dry all the way");
            Assert.AreEqual(1, clear);
            Assert.Greater(Game.Player.Position.z, 25f);
            int onCard = 0;
            for (int i = 0; i < rain.BeamCount; i++)
                if (Mathf.Abs(rain.BeamLength(i) - (8f - 3.24f)) < 0.03f) onCard++;
            Assert.Greater(onCard, 250, "hundreds of dots wink out in a card-shaped rectangle (" + onCard + ")");
        }

        [Test]
        public void ACardInTheHand_IsNoUmbrella()
        {
            Build(BuildCorridor);
            int zapped = 0;
            rain.Zapped += () => zapped++;
            // Grabbed from close, held overhead, big enough to cover the player: and still not in the world.
            Game.Player.Teleport(new Vector3(0f, 0f, 1.9f), 0f);
            LookAt(card.Center);
            Click();
            Assert.AreSame(card, Game.Grabber.Held);
            Game.Player.Teleport(new Vector3(0f, 0f, 12f), 0f);
            Game.Player.Pitch = 80f;
            Run(3);
            Assert.Greater(card.Center.y, Game.Player.Position.y + 2f, "the card is over the player's head");
            Assert.Greater(card.Scale, 1.5f);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => zapped > 0, 3f), "the beams pass straight through a held toy");
            Assert.AreEqual(0f, rain.Coverage01, 1e-4f);
        }

        [Test]
        public void ASingleLaser_IsBlockedByAProp()
        {
            LaserRain laser = null;
            Prop crate = null;
            int zapped = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // One beam across the path at chest height, from x = -5 toward +x.
                laser = new LaserRain(ctx, new LaserRainOptions { Center = new Vector3(-5f, 1f, 6f), Size = Vector2.zero, Direction = Vector3.right, Up = Vector3.up, Range = 10f, ZapDelay = 0.05f });
                laser.Zapped += () => zapped++;
                crate = ctx.AddProp(BasicToys.Block(1.5f), new Vector3(-3f, 0.75f, 6f), new PropOptions { Name = "Crate" });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.AreEqual(1, laser.BeamCount);
            RunSeconds(0.5f);
            Assert.AreEqual(1.25f, laser.BeamLength(0), 0.02f, "the beam ends on the crate");
            Input.Hold = new InputFrame { MoveZ = 1f };
            RunSeconds(2f);
            Assert.AreEqual(0, zapped, "behind the crate the path is safe");
            Assert.Greater(Game.Player.Position.z, 8f);

            // Take the crate away and walk back.
            Game.Context.RemoveProp(crate);
            Input.Hold = new InputFrame { MoveZ = -1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => zapped > 0, 3f), "now the beam reaches across the path");
            Assert.AreEqual(10f, laser.BeamLength(0), 0.02f);
        }
    }
}
