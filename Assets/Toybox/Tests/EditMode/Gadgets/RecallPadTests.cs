using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The floor plate that brings the doorway back, at the player's own size.</summary>
    public class RecallPadTests : HallwayTest
    {
        RecallPad pad;

        void BuildWithPad(LevelContext ctx)
        {
            BuildHall(ctx);
            pad = new RecallPad(ctx, new RecallPadOptions { Position = new Vector3(-3.5f, 0f, 1.5f), Radius = 0.6f, Prop = Door, HoldSeconds = 0.5f });
        }

        [Test]
        public void StandingOnThePad_FetchesTheDoor_AtThePlayersSize_FacingThem()
        {
            Build(BuildWithPad);
            int recalled = 0;
            pad.Recalled += () => recalled++;
            // The door is far away and big; the player is small.
            Door.SetScale(3f);
            Door.SetPose(new Vector3(2f, 0f, 20f), Quaternion.identity);
            Game.Player.SetScale(0.5f);
            Game.Player.Teleport(new Vector3(-3.5f, 0f, 1.5f), 90f);
            RunSeconds(0.4f);
            Assert.AreEqual(0, recalled, "not yet: half a second");
            Assert.IsTrue(pad.PlayerOn);
            Assert.Greater(pad.Progress01, 0.5f);
            RunSeconds(0.2f);
            Assert.AreEqual(1, recalled);
            Assert.AreEqual(0.5f, Door.Scale, 1e-4f, "at the player's scale");
            Vector3 ahead = Vector3.right;
            Vector3 offset = Door.Position - Game.Player.Position;
            Assert.Greater(Vector3.Dot(offset, ahead), 0.25f, "in front of the player (they look toward +X)");
            Assert.Less(offset.magnitude, 1f, "within a step");
            Assert.Greater(Vector3.Dot(Door.Rotation * Vector3.forward, -ahead), 0.99f, "facing them");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Portal.Settled, 1f), "and it settles: a portal again");
            Assert.AreEqual(0f, Door.Position.y, 0.01f);

            RunSeconds(2f);
            Assert.AreEqual(1, recalled, "one fetch per visit");
            // Step off and on again.
            Game.Player.Teleport(new Vector3(0f, 0f, 5f), 0f);
            Run(2);
            Game.Player.Teleport(new Vector3(-3.4f, 0f, 1.6f), 0f);
            RunSeconds(0.6f);
            Assert.AreEqual(2, recalled);
        }

        [Test]
        public void AWallAhead_PutsTheDoorSomewhereItFits_AndAHeldDoorStaysInTheHand()
        {
            Build(BuildWithPad);
            // A player of twice the size looks at the side wall (x = -5) from the pad: no room ahead for a door of
            // their size, nor to either side (it is 3.2 wide); behind them there is.
            Game.Player.SetScale(2f);
            Game.Player.Teleport(new Vector3(-3.9f, 0f, 1.5f), -90f);
            RunSeconds(0.6f);
            Assert.AreEqual(1, pad.Recalls);
            Assert.AreEqual(2f, Door.Scale, 1e-4f);
            Assert.Greater(Door.Position.x, -3.9f, "behind the player, not inside the wall: it is at " + Door.Position);
            Collider wall = null;
            foreach (Collider c in Door.Colliders)
            {
                Collider[] hits = new Collider[8];
                int count = Game.PhysicsScene.OverlapBox(c.bounds.center, c.bounds.extents * 0.95f, hits, Quaternion.identity, Layers.DefaultMask);
                if (count > 0) wall = hits[0];
            }
            Assert.IsNull(wall, "the fetched door stands clear of the walls");

            // In the hand, the pad leaves it alone.
            Game.Player.Teleport(new Vector3(0f, 0f, 6f), 0f);
            Run(2);
            LookAt(Door.Colliders[0].bounds.center);
            Click();
            Assert.AreSame(Door, Game.Grabber.Held);
            Game.Player.Teleport(new Vector3(-3.5f, 0f, 1.5f), 0f);
            RunSeconds(1f);
            Assert.AreEqual(1, pad.Recalls);
            Assert.AreSame(Door, Game.Grabber.Held);
        }
    }
}
