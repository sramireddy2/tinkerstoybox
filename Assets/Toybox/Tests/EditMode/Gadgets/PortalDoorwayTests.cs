using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Level 14: a door is always the right size for whoever walks through it - so walking through makes you that size.</summary>
    public abstract class HallwayTest : SimTest
    {
        protected PortalDoorway Portal;
        protected Prop Door;
        protected readonly List<string> Log = new List<string>();

        /// <summary>Hall A of Level 14: x -5..5, z 0..14, 11 high, the doorway standing at (0, 0, 8) facing the spawn.</summary>
        protected void BuildHall(LevelContext ctx, float ceiling = 11f, float doorScale = 1f)
        {
            Log.Clear();
            TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 12f), new Vector3(10f, 1f, 26f));                        // floor z -1 .. 25
            TestHelpers.Box(ctx, new Vector3(-5.5f, ceiling * 0.5f, 12f), new Vector3(1f, ceiling, 26f));
            TestHelpers.Box(ctx, new Vector3(5.5f, ceiling * 0.5f, 12f), new Vector3(1f, ceiling, 26f));
            TestHelpers.Box(ctx, new Vector3(0f, ceiling * 0.5f, -1.5f), new Vector3(12f, ceiling, 1f));
            TestHelpers.Box(ctx, new Vector3(0f, ceiling * 0.5f, 25.5f), new Vector3(12f, ceiling, 1f));
            TestHelpers.Box(ctx, new Vector3(0f, ceiling + 0.5f, 12f), new Vector3(12f, 1f, 28f));               // ceiling
            Door = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(0f, 0f, 8f), Quaternion.Euler(0f, 180f, 0f), doorScale);
            Portal = new PortalDoorway(ctx, new PortalDoorwayOptions { Prop = Door });
            Portal.OnSettled += () => Log.Add("settled");
            Portal.Crossed += (from, to) => Log.Add("crossed " + from.ToString("0.00") + " -> " + to.ToString("0.00"));
            Portal.Blocked += () => Log.Add("blocked");
            ctx.SetSpawn(new Vector3(0f, 0f, 2f), 0f);
        }
    }

    public class PortalDoorwayTests : HallwayTest
    {
        [Test]
        public void TheFrame_SettlesStraightDown_AndNeverTurns()
        {
            Build(ctx => BuildHall(ctx));
            Run(2);
            Assert.IsTrue(Portal.Settled, "standing on the floor it is settled at once");
            Assert.IsTrue(Portal.Active);

            // Let go in mid-air, as the hold leaves it at the clamp: it comes down under gravity.
            Quaternion turned = Quaternion.Euler(0f, 37f, 0f);
            Door.SetPose(new Vector3(1f, 0.76f, 9f), turned);
            Run(1);
            Assert.IsFalse(Portal.Settled, "moved: it has to settle again");
            int start = Game.LevelTicks;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.Settled, 2f));
            Assert.AreEqual(Mathf.Sqrt(2f * 0.76f / 22f), (Game.LevelTicks - start) * Sim.Dt, 0.06f, "free fall over 0.76");
            Assert.AreEqual(0f, Door.Position.y, 0.01f, "its foot on the floor");
            Assert.AreEqual(1f, Door.Position.x, 1e-4f);
            Assert.Less(Quaternion.Angle(turned, Door.Rotation), 0.01f, "it never rotates");
            Assert.AreEqual(2, Log.Count, "settled twice: " + string.Join(", ", Log));
        }

        [Test]
        public void WhatARendererNeeds_IsThere()
        {
            Build(ctx => BuildHall(ctx, 11f, 0.28f));
            Run(2);
            Assert.AreEqual(0.3f, Portal.TargetScale, 1e-4f, "a door of 0.28 makes the player 0.3: the player's clamp");
            Assert.AreEqual(0.3f, Portal.ViewRatio, 1e-4f);
            Assert.AreEqual(new Vector2(1.2f, 2.2f) * 0.28f, Portal.OpeningSize);
            Assert.Less(Vector3.Distance(new Vector3(0f, 1.1f * 0.28f, 8f), Portal.OpeningCenter), 1e-4f);
            Assert.Less(Vector3.Distance(Vector3.back, Portal.Normal), 1e-4f, "it faces the spawn");
            // The camera for the view through the door: the eye scaled about the threshold.
            Vector3 eye = Game.Player.Eye;
            Vector3 view = Portal.ViewEye(eye);
            Assert.Less(Vector3.Distance(new Vector3(0f, 1.55f * 0.3f, 8f - 6f * 0.3f), view), 1e-3f, "at the height the small player's eye will have, 0.3 times as far from the door");
        }

        [Test]
        public void TheBot_ShrinksThroughATinyDoor_ThenGrowsThroughAHugeOne()
        {
            Build(ctx => BuildHall(ctx));
            var bot = new Bot(Game);
            float small = 0f, big = 0f;
            IEnumerator Script()
            {
                // Small: grabbed from 5 away, put down at the feet.
                yield return bot.WalkTo(new Vector3(0f, 0f, 3f), 0.1f);
                yield return bot.Grab(Door);
                yield return bot.DropAt(new Vector3(0f, 0.35f, 3.69f));
                yield return bot.Until(() => Portal.Settled, 2f);
                small = Door.Scale;
                yield return bot.WalkTo(new Vector3(0f, 0f, 4.15f), 0.05f);
                yield return bot.Until(() => bot.Player.Scale < 0.45f, 2f);
                Assert.AreEqual(0.3f, bot.Player.Scale, 1e-4f);
                Assert.Greater(bot.Player.Position.z, Door.Position.z, "on the far side of the door");
                Assert.AreEqual(0.3f * 1.55f, bot.Player.EyeHeight, 1e-4f);

                // Big: the mouse-sized player turns round, takes the door from close, looks almost straight up and lets it grow.
                yield return bot.Grab(Door);
                yield return bot.WalkTo(new Vector3(0f, 0f, 9f), 0.1f, 15f);
                yield return bot.DropAt(bot.Player.Eye + Quaternion.Euler(-60f, 0f, 0f) * Vector3.forward * 4.95f);
                yield return bot.Until(() => Portal.Settled, 3f);
                big = Door.Scale;
                yield return bot.WalkTo(Door.Position + Vector3.forward * 2f, 0.1f, 15f);
                yield return bot.Until(() => bot.Player.Scale > 2.5f, 2f);
                yield return bot.Wait(0.3f);
            }
            BotRunner.Run(Game, Script(), 60f);
            Assert.AreEqual(0.28f, small, 0.04f, "LEVELS: the door ends up 0.28 (anything up to 0.45 works)");
            Assert.AreEqual(3.2f, big, 0.01f, "against nothing but its clamp");
            Assert.AreEqual(3.2f, Game.Player.Scale, 1e-4f);
            Assert.AreEqual(3.2f * 1.7f, Game.Player.Height, 1e-3f, "5.4 tall");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(0f, Game.Player.Position.y, 0.05f);
            CollectionAssert.Contains(Log, "crossed 1.00 -> 0.30");
            CollectionAssert.Contains(Log, "crossed 0.30 -> 3.20");
            Assert.AreEqual(2, Portal.Crossings);
            Assert.AreEqual(PortalResult.Through, Portal.LastResult);
        }

        [Test]
        public void ADoorOfThePlayersOwnSize_IsJustADoor()
        {
            Build(ctx => BuildHall(ctx));
            Input.Hold = new InputFrame { MoveZ = 1f };
            float biggestStep = 0f;
            float previous = Game.Player.Position.z;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                biggestStep = Mathf.Max(biggestStep, Mathf.Abs(Game.Player.Position.z - previous));
                previous = Game.Player.Position.z;
            }
            Assert.Greater(Game.Player.Position.z, 10f, "walked through");
            Assert.AreEqual(1f, Game.Player.Scale, 1e-4f);
            Assert.AreEqual(0, Portal.Crossings, "no resize, no event");
            Assert.AreEqual(PortalResult.Through, Portal.LastResult);
            Assert.Less(biggestStep, 5f * Sim.Dt + 0.01f, "and no jump: the player just walks");
        }

        [Test]
        public void NoRoomBeyond_TurnsThePlayerBack_AndNoRoomAtAll_DoesNothing()
        {
            // A giant door close to the end wall: beyond it there is 1.2 of floor, and a size 3.2 player is 1.9 across.
            Build(ctx => BuildHall(ctx, 11f, 3.2f));
            Door.SetPose(new Vector3(0f, 0f, 23.3f), Quaternion.Euler(0f, 180f, 0f));
            Game.Player.Teleport(new Vector3(0f, 0f, 18f), 0f);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Assert.AreEqual(PortalResult.TurnedBack, Portal.LastResult);
            Assert.AreEqual(3.2f, Game.Player.Scale, 1e-4f, "the right size all the same");
            Assert.Less(Game.Player.Position.z, 23.3f, "back out of the side they went in");
            Assert.AreEqual(180f, Mathf.Abs(Game.Player.Yaw), 0.5f, "turned round");
            Input.Hold = default;
            Game.Dispose();
            Game = null;

            // A door of 2.6 under a ceiling of 4: a player 4.4 tall fits on neither side.
            Build(ctx => BuildHall(ctx, 4f, 2.6f));
            Run(3);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Assert.AreEqual(PortalResult.Blocked, Portal.LastResult);
            Assert.AreEqual(1f, Game.Player.Scale, 1e-4f, "nothing happened");
            CollectionAssert.Contains(Log, "blocked");
        }

        [Test]
        public void AHeldDoor_IsNotAPortal()
        {
            Build(ctx => BuildHall(ctx));
            // The middle of a doorway is a hole: take it by a post.
            LookAt(Door.Colliders[0].bounds.center);
            Click();
            Assert.AreSame(Door, Game.Grabber.Held);
            Run(2);
            Assert.IsFalse(Portal.Settled);
            Assert.IsFalse(Portal.Active);
            // Walk through where it hangs in the view: nothing.
            Input.Hold = new InputFrame { MoveZ = 1f };
            RunSeconds(1.5f);
            Assert.AreEqual(0, Portal.Crossings);
            Assert.AreEqual(1f, Game.Player.Scale, 1e-4f);
        }
    }
}
