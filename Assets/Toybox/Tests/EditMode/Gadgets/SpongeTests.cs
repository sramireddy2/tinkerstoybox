using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class SpongeTests : FishbowlTest
    {
        [Test]
        public void ABigSponge_DrinksItsCapacity_AndTheBowlFallsToWadingDepth()
        {
            // The soak of Level 13: scale 9.5 in the middle of the bowl.
            Build(ctx => BuildBowl(ctx));
            var log = new List<string>();
            Sponge.Soaking += () => log.Add("soaking");
            Sponge.Full += () => log.Add("full");
            Sponge.Wringing += () => log.Add("wringing");
            RunSeconds(0.5f);
            Assert.AreEqual(0f, Sponge.Stored, 1e-4f, "on the dry island it drinks nothing");

            SpongeProp.SetScale(9.5f);
            SpongeProp.SetPose(new Vector3(0f, 2.5f, 1.2f), Quaternion.identity);
            Assert.AreEqual(0.315f * 9.5f * 9.5f * 9.5f, Sponge.Capacity, 0.01f);
            int start = Game.LevelTicks;
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Sponge.Activity == SpongeActivity.Idle && Sponge.Stored > 1f, 6f));
            float seconds = (Game.LevelTicks - start) * Sim.Dt;
            Assert.AreEqual(270.1f, Sponge.Stored, 0.2f, "0.315 x 9.5 cubed");
            Assert.AreEqual(1f, Sponge.Saturation01, 1e-3f);
            Assert.AreEqual(0.5f, Bowl.Depth, 0.01f, "LEVELS appendix B: bowl depth 0.5 after the soak");
            Assert.AreEqual(270f / 150f + 0.1f, seconds, 0.2f, "at 150 units a second: about two seconds");
            Assert.AreEqual(405f, Total, 1e-2f, "conserved");
            CollectionAssert.AreEqual(new[] { "soaking", "full" }, log);
        }

        [Test]
        public void MadeSmallInTheTower_ItWringsItselfOut_AndTheCorkLiftsThePlayerToTheRim()
        {
            Build(ctx => BuildBowl(ctx));
            // A full sponge of scale 9.5 that is now a brick of scale 2 on the cork, with the player beside it.
            SpongeProp.SetScale(9.5f);
            SpongeProp.SetPose(new Vector3(0f, 2.5f, 1.2f), Quaternion.identity);
            RunSeconds(3f);
            Assert.AreEqual(270.1f, Sponge.Stored, 0.2f);
            SpongeProp.SetScale(2f);
            SpongeProp.SetPose(new Vector3(-0.4f, 0.6f, 9f), Quaternion.identity);
            Game.Player.Teleport(new Vector3(1f, 0.05f, 7.6f), 0f);
            Assert.AreEqual(2.52f, Sponge.Capacity, 0.01f);

            int start = Game.LevelTicks;
            float worstDepth = 0f;
            bool grounded = true;
            float fastest = 0f;
            while (!Cork.AtTop && Game.LevelTicks - start < 8 * 60)
            {
                Game.Tick();
                worstDepth = Mathf.Max(worstDepth, Tower.SurfaceY - Game.Player.Position.y);
                grounded &= Game.Player.Grounded || Game.LevelTicks - start < 10;
                fastest = Mathf.Max(fastest, Mathf.Abs(Cork.Speed));
            }
            float seconds = (Game.LevelTicks - start) * Sim.Dt;
            Assert.IsTrue(Cork.AtTop, "the cork reaches the rim");
            Assert.LessOrEqual(seconds, 4f, "LEVELS appendix B: cork.AtTop within 4 s of the wring (took " + seconds + ")");
            Assert.AreEqual(7.9f / ((24f - 3f) / 9f), seconds, 0.3f, "24 in, 3 leaking out, over an area of 9: the surface rises at 2.3 and the cork keeps up");
            Assert.LessOrEqual(fastest, 3f + 1e-3f);
            Assert.IsTrue(grounded, "the player rode the cork all the way");
            Assert.Less(worstDepth, 0.3f, "and never stood deep in the water (worst " + worstDepth + ")");
            Assert.AreEqual(8f, Game.Player.Position.y, 0.05f, "at the rim");
            Assert.AreEqual(SpongeActivity.Wringing, Sponge.Activity, "it is still wringing: the surplus pours over the lip into the bowl");
            Assert.AreEqual(405f, Total, 0.05f, "conserved");

            // The player walks off over the rim onto the books.
            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Position.z > 11.5f, 3f));
            Assert.AreEqual(8f, Game.Player.Position.y, 0.1f);
        }

        [Test]
        public void AHeldSponge_NeitherSoaksNorLeaks()
        {
            Build(ctx => BuildBowl(ctx));
            // Full, then picked up and carried: it keeps what it holds whatever size the hold gives it.
            SpongeProp.SetScale(4f);
            SpongeProp.SetPose(new Vector3(0f, 1.2f, 0f), Quaternion.identity);
            RunSeconds(1.5f);
            float stored = Sponge.Stored;
            Assert.AreEqual(0.315f * 64f, stored, 0.05f);
            LookAt(SpongeProp.Center);
            Click();
            Assert.AreSame(SpongeProp, Game.Grabber.Held);
            // Look at the island at the feet: the sponge shrinks to a fraction over dry ground.
            LookAt(new Vector3(0f, 3f, -7.5f));
            RunSeconds(1f);
            Assert.Less(SpongeProp.Scale, 1f);
            Assert.AreEqual(stored, Sponge.Stored, 1e-4f, "held, it is not in the world");
            Assert.Greater(Sponge.Saturation, 1f, "it holds far more than it can at this size");
            Assert.AreEqual(1f, Sponge.Saturation01, 1e-4f);

            // Emptied back into the bowl by a leash or a reset.
            float before = Bowl.Volume;
            Sponge.EmptyInto(Bowl);
            Assert.AreEqual(0f, Sponge.Stored);
            Assert.AreEqual(before + stored, Bowl.Volume, 1e-3f);
        }
    }
}
