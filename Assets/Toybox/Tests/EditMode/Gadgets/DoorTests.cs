using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Doors are kinematic panels with two poses; they never move into the player.</summary>
    public class DoorTests : SimTest
    {
        [Test]
        public void ASlidingDoor_BlocksUntilOpened_AndTakesItsSeconds()
        {
            Door door = null;
            int opened = 0, closed = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A wall at z = 5 with a doorway x -1..1, y 0..3.
                TestHelpers.Box(ctx, new Vector3(-6f, 2f, 5f), new Vector3(10f, 4f, 0.6f));
                TestHelpers.Box(ctx, new Vector3(6f, 2f, 5f), new Vector3(10f, 4f, 0.6f));
                TestHelpers.Box(ctx, new Vector3(0f, 3.5f, 5f), new Vector3(2f, 1f, 0.6f));
                door = new Door(ctx, new DoorOptions
                {
                    Size = new Vector3(2f, 3f, 0.3f), ClosedPosition = new Vector3(0f, 1.5f, 5f), OpenPosition = new Vector3(0f, 4.5f, 5f), Seconds = 0.8f,
                });
                door.Opened += () => opened++;
                door.Closed += () => closed++;
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Input.Hold = new InputFrame { MoveZ = 1f };
            RunSeconds(2f);
            Assert.Less(Game.Player.Position.z, 4.6f, "a closed door is a wall");
            Assert.IsTrue(door.IsClosed);

            door.Open();
            Assert.IsTrue(door.Moving);
            int start = Game.LevelTicks;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => door.IsOpen, 2f));
            Assert.AreEqual(0.8f, (Game.LevelTicks - start) * Sim.Dt, 0.05f);
            Assert.AreEqual(1, opened);
            Assert.AreEqual(4.5f, door.Mover.Position.y, 1e-3f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Position.z > 7f, 3f), "and an open one is not");

            Input.Hold = default;
            door.Close();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => door.IsClosed, 2f));
            Assert.AreEqual(1, closed);
            door.Open();
            RunSeconds(0.2f);
            door.Reset();
            Assert.IsTrue(door.IsClosed, "Reset puts it back as Build left it");
            Assert.AreEqual(1.5f, door.Mover.Position.y, 1e-3f);
        }

        [Test]
        public void AFlap_FallsOpenOnItsHinge_AndIsFloorWhenItLiesFlat()
        {
            Door flap = null;
            Build(ctx =>
            {
                // A step down of 0.5 behind the flap: lying flat, the flap bridges it.
                TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 0f), new Vector3(12f, 1f, 10.08f));
                TestHelpers.Box(ctx, new Vector3(0f, -0.5f, 12f), new Vector3(12f, 1f, 8f));
                flap = new Door(ctx, new DoorOptions
                {
                    Size = new Vector3(2f, 3f, 0.08f), ClosedPosition = new Vector3(0f, 1.5f, 5f),
                    Motion = DoorMotion.Hinge, HingePivot = new Vector3(0f, 0f, 5.04f), HingeAxis = Vector3.right, HingeAngle = 90f, Seconds = 0.8f,
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 2f), 0f);
            }, -20f);
            flap.Open();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => flap.IsOpen, 2f));
            Assert.AreEqual(0.04f, flap.Mover.Position.y, 1e-3f, "lying flat, its top 0.08 above the floor");
            Assert.AreEqual(6.54f, flap.Mover.Position.z, 1e-3f);

            // Walk out over it: the flap covers the gap between the two floors (z 5.04 .. 8).
            Input.Hold = new InputFrame { MoveZ = 1f };
            float highest = 0f, lowest = 0f;
            bool onFlap = false;
            for (int i = 0; i < 180 && Game.Player.Position.z < 9f; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                lowest = Mathf.Min(lowest, Game.Player.Position.y);
                if (Game.Player.Grounded && Game.Player.GroundCollider != null && Game.Player.GroundCollider.attachedRigidbody == flap.Mover.Body) onFlap = true;
            }
            Assert.Greater(Game.Player.Position.z, 9f, "the player walked across");
            Assert.IsTrue(onFlap, "standing on the flap on the way");
            Assert.Less(highest, 0.2f, "without a hop");
            Assert.Greater(lowest, -0.05f, "and without falling into the gap");
        }

        [Test]
        public void TheCrushGuard_MakesTheDoorWait_WhileThePlayerIsInTheWay()
        {
            Door door = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                door = new Door(ctx, new DoorOptions
                {
                    Size = new Vector3(2f, 3f, 0.3f), ClosedPosition = new Vector3(0f, 1.5f, 5f), OpenPosition = new Vector3(0f, 4.6f, 5f),
                    Seconds = 0.5f, StartsOpen = true, Ease = DoorEase.Linear,
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 5f), 0f);
            });
            Assert.IsTrue(door.IsOpen);
            door.Close();
            RunSeconds(2f);
            Assert.IsTrue(door.Waiting, "the player stands in the doorway: the door waits above their head");
            Assert.IsFalse(door.IsClosed);
            Assert.GreaterOrEqual(door.Mover.Position.y - 1.5f, Game.Player.Height - 0.05f, "it stopped on top of the capsule, not in it");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.02f, "the player was not pressed into the floor");
            Assert.AreEqual(5f, Game.Player.Position.z, 0.05f, "nor squirted out");

            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => door.IsClosed, 3f), "once they step away it closes");
            Assert.Greater(Game.Player.Position.z, 5.4f);
        }
    }
}
