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

        // The hall with a ledge across its far end: `height` high, from z = 19 to the end wall.
        void BuildHallWithALedge(float height)
        {
            Build(ctx =>
            {
                BuildHall(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, height * 0.5f, 22f), new Vector3(10f, height, 6f));
            });
        }

        // Level 14 (review): the trip used to count as soon as the opening overlapped the player's height. A
        // door standing up on a ledge, edge-on and reaching out over its edge, took whoever hopped along
        // under it up onto the ledge - a way round the level's second gate.
        [TestCase(1f)]
        [TestCase(1.5f)]
        [TestCase(1.9f)]
        [TestCase(2.3f)]
        public void ADoorThatStandsAFloorAbove_IsNotWalkedIntoFromBelow(float size)
        {
            BuildHallWithALedge(3.4f);
            Door.SetScale(1.3f);
            Door.SetPose(new Vector3(0f, 3.4f, 19.3f), Quaternion.Euler(0f, 90f, 0f));      // edge-on, a metre of it out over the hall
            Run(30);
            Assert.IsTrue(Portal.Settled);
            Assert.AreEqual(3.4f, Door.Position.y, 0.01f, "it stands on the ledge");
            Player player = Game.Player;
            player.Teleport(new Vector3(-2.5f, 0f, 19f - 0.3f * size - 0.03f), 90f);
            Assert.IsTrue(player.SetScale(size));
            Run(2);
            // Along the foot of the ledge, under the door, hopping all the way.
            Input.Hold = new InputFrame { MoveZ = 1f };
            float highest = 0f;
            for (int i = 0; i < 150; i++)
            {
                if (player.Grounded) Input.Once.Jump = true;
                Game.Tick();
                highest = Mathf.Max(highest, player.Position.y + player.Height);
            }
            Input.Hold = default;
            Assert.Greater(player.Position.x, 1f, "they passed under it");
            if (size >= 1.5f) Assert.Greater(highest, 3.4f, "and their head was as high as its foot");
            Assert.AreEqual(0, Portal.Crossings, "nobody walks into a door from the floor below it");
            Assert.AreEqual(PortalResult.None, Portal.LastResult);
            Assert.AreEqual(size, player.Scale, 1e-4f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => player.Grounded, 2f));
            Assert.Less(player.Position.y, 0.5f, "still down in the hall");
        }

        [Test]
        public void TheSameDoor_IsWalkedIntoByWhoeverStandsOnItsOwnFloor()
        {
            BuildHallWithALedge(3.4f);
            // The player first, then the door: it settles knowing which side of it they are on.
            Game.Player.Teleport(new Vector3(-2f, 3.4f, 19.8f), 90f);
            Run(5);
            Door.SetScale(1.3f);
            Door.SetPose(new Vector3(0f, 3.4f, 19.3f), Quaternion.Euler(0f, 90f, 0f));
            Run(30);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Assert.AreEqual(PortalResult.Through, Portal.LastResult);
            Assert.AreEqual(1.3f, Game.Player.Scale, 1e-4f);
            Assert.Greater(Game.Player.Position.x, 0f, "out of the far side");
            Assert.Greater(Game.Player.Position.z, 19f, "over the ledge, not over the hall");
        }

        // Level 14 (review): on the ledge the natural thing is to let go of the door at one's feet while still
        // facing the hall. Walking into it then came out in the air beyond the edge. Where there is no floor
        // to come out on - under the spot, or under the step after it - the player comes back out of the near
        // side, like where there is no room.
        [TestCase(19.25f)]
        [TestCase(19.4f)]
        public void TheFarSideOverADrop_TurnsThePlayerBack(float doorZ)
        {
            BuildHallWithALedge(3.4f);
            Player player = Game.Player;
            player.Teleport(new Vector3(0f, 3.4f, 21f), 180f);                              // on the ledge, facing the hall
            Run(5);
            Door.SetScale(0.5f);
            Door.SetPose(new Vector3(0f, 3.4f, doorZ), Quaternion.identity);
            Run(30);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Input.Hold = default;
            Assert.AreEqual(PortalResult.TurnedBack, Portal.LastResult, "the far side is the drop: back out of the near side");
            Assert.AreEqual(0.5f, player.Scale, 1e-4f, "the right size all the same");
            Assert.Greater(player.Position.z, doorZ, "on the side they came from");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, player.Yaw)), 1f, "turned round: the ledge ahead, the drop behind");
            RunSeconds(1f);
            Assert.IsTrue(player.Grounded);
            Assert.AreEqual(3.4f, player.Position.y, 0.05f, "standing on the ledge");
        }

        [Test]
        public void AFarSideWithFloorToSpare_AndADropThePlayerCanJumpBackUp_AreWalkedOutOnto()
        {
            // A step and a half from the edge: through.
            BuildHallWithALedge(3.4f);
            Game.Player.Teleport(new Vector3(0f, 3.4f, 22f), 180f);
            Run(5);
            Door.SetScale(0.5f);
            Door.SetPose(new Vector3(0f, 3.4f, 20f), Quaternion.identity);
            Run(30);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Input.Hold = default;
            Assert.AreEqual(PortalResult.Through, Portal.LastResult);
            Assert.Less(Game.Player.Position.z, 20f);
            Assert.AreEqual(3.4f, Game.Player.Position.y, 0.05f);
            Game.Dispose();
            Game = null;

            // A door of 2 on the edge of a step 1.5 high: whoever comes out of it jumps 2.5, so that is a floor.
            BuildHallWithALedge(1.5f);
            Game.Player.Teleport(new Vector3(0f, 1.5f, 22f), 180f);
            Run(5);
            Door.SetScale(2f);
            Door.SetPose(new Vector3(0f, 1.5f, 19.5f), Quaternion.identity);
            Run(30);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.LastResult != PortalResult.None, 3f));
            Input.Hold = default;
            Assert.AreEqual(PortalResult.Through, Portal.LastResult);
            Assert.AreEqual(2f, Game.Player.Scale, 1e-4f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded && Game.Player.Position.y < 0.2f, 2f), "down the step, on the floor of the hall");
        }

        // Level 14 (review): the trip multiplied the player's whole velocity by the ratio of the sizes. Gravity
        // is the same at every size, so the height of a jump went with the SQUARE of the ratio: the hop of a
        // mouse into a door of 1.5 came out as a leap of seven units - over the level's second gate at a size
        // that fits its third. Up and down the speed goes with the root of the ratio: the same jump at the
        // new size, and no more.
        [TestCase(0.3f, 1.3f)]
        [TestCase(0.3f, 2f)]
        [TestCase(0.3f, 3.2f)]
        [TestCase(1f, 2f)]
        [TestCase(1.5f, 2f)]
        [TestCase(3.2f, 1f)]
        public void AJumpIntoTheDoor_ComesOutAsAJumpOfTheNewSize_NotALeap(float from, float to)
        {
            foreach (bool sprint in new[] { false, true })
                foreach (float lead in new[] { 0.05f, 0.15f, 0.3f, 0.5f })
                {
                    Build(ctx => BuildHall(ctx, 14f, to));
                    Door.SetPose(new Vector3(0f, 0f, 12f), Quaternion.Euler(0f, 180f, 0f));
                    Player player = Game.Player;
                    if (from < 1f) player.SetScale(from);
                    player.Teleport(new Vector3(0f, 0f, 12f - 2.5f * from - 0.3f), 0f);
                    if (from > 1f) Assert.IsTrue(player.SetScale(from));
                    Run(5);
                    Input.Hold = new InputFrame { MoveZ = 1f, Sprint = sprint };
                    bool jumped = false;
                    float highest = 0f, highestAfter = 0f;
                    for (int i = 0; i < 180; i++)
                    {
                        if (!jumped && player.Position.z >= 12f - lead * from)
                        {
                            jumped = true;
                            Input.Once.Jump = true;
                        }
                        Game.Tick();
                        highest = Mathf.Max(highest, player.Position.y);
                        if (Portal.Crossings > 0) highestAfter = Mathf.Max(highestAfter, player.Position.y);
                    }
                    Input.Hold = default;
                    string what = from + " -> " + to + (sprint ? ", sprinting" : ", walking") + ", jumping " + lead + " sizes before the door: the feet got to " + highest.ToString("0.00");
                    Assert.IsTrue(jumped, what);
                    Assert.AreEqual(1, Portal.Crossings, what);
                    Assert.AreEqual(to, player.Scale, 1e-4f, what);
                    // A jump reaches 1.25 times the player's size (LEVELS 0.1; 1.31 by the numbers alone).
                    Assert.Less(highestAfter, 1.33f * to, what + " (after the trip: no higher than a jump at the new size)");
                    Assert.Less(highest, 1.33f * Mathf.Max(from, to), what);
                    Game.Dispose();
                    Game = null;
                }
        }

        [Test]
        public void WhatIsCarriedThroughTheDoor_AWalkWithTheSize_AJumpWithItsRoot()
        {
            // Walking at 5 sizes a second, on the way up at a jump's own speed: the same at four times the size.
            var walkingAndJumping = new Vector3(0f, Player.JumpSpeed, Player.WalkSpeed);
            Vector3 carried = PortalDoorway.Carried(walkingAndJumping, 4f, false);
            Assert.AreEqual(Player.WalkSpeed * 4f, carried.z, 1e-4f, "a walk goes with the size");
            Assert.AreEqual(Player.JumpSpeed * 2f, carried.y, 1e-4f, "a jump with its root");
            // Turned back: the way along the floor is reversed; up is still up, down is still down.
            Vector3 back = PortalDoorway.Carried(new Vector3(1f, 3f, -2f), 0.25f, true);
            Assert.Less(Vector3.Distance(new Vector3(-0.25f, 1.5f, 0.5f), back), 1e-5f);
            Assert.Less(PortalDoorway.Carried(new Vector3(0f, -8f, 0f), 0.25f, true).y, 0f, "a fall stays a fall");

            // In the hall: a mouse walks into a door of 3.2 and comes out walking, at the giant's pace, on the floor.
            Build(ctx => BuildHall(ctx, 11f, 3.2f));
            Player player = Game.Player;
            player.SetScale(0.3f);
            player.Teleport(new Vector3(0f, 0f, 7f), 0f);
            Run(5);
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.Crossings > 0, 3f));
            Assert.AreEqual(Player.WalkSpeed * 3.2f, player.Velocity.z, 0.5f, "walking on at the new size's pace");
            Assert.Less(Mathf.Abs(player.Velocity.y), 1f);
            Run(10);
            Assert.IsTrue(player.Grounded, "and never off the floor");
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
