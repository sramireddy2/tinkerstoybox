using NUnit.Framework;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Tests
{
    public class Level00Tests : SimTest
    {
        [Test]
        public void SolveCompletesTheSandbox()
        {
            Game = Game.Create();
            Game.LoadLevel(0);
            Assert.AreEqual("sandbox", Game.Level.Slug);
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void TheGapCannotBeJumped()
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(0);
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;

            // Sprint straight at the gap and jump at the last moment.
            Game.Player.Teleport(new Vector3(3f, 0f, 0f), 0f);
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            TestHelpers.RunUntil(Game, () => Game.Player.Position.z > 7.6f, 3f);
            Input.Once.Jump = true;
            float farthest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(4f) && respawns == 0; i++)
            {
                Game.Tick();
                if (Game.Player.Position.y > -0.5f) farthest = Mathf.Max(farthest, Game.Player.Position.z);
            }
            Assert.AreEqual(1, respawns, "the player should have fallen into the gap and respawned");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Less(farthest, 15f, "a sprint jump carries about 5.5 units, far short of the 10 unit gap");
            Assert.Greater(farthest, 12f, "the jump fell well short of the documented 5.5 units");
        }
    }
}
