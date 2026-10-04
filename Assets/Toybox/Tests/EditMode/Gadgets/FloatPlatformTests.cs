using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class FloatPlatformTests : FishbowlTest
    {
        [Test]
        public void TheCork_FollowsTheSurface_BetweenItsRestAndItsTop()
        {
            int left = 0, arrived = 0;
            Build(ctx => BuildBowl(ctx));
            Cork.FloatLeft += () => left++;
            Cork.FloatArrived += () => arrived++;
            RunSeconds(0.5f);
            Assert.IsTrue(Cork.AtRest, "no water in the tower: its top is flush with the gravel");
            Assert.AreEqual(0f, Cork.TopY, 1e-4f);
            Assert.AreEqual(-0.2f, Cork.Mover.Position.y, 1e-4f);

            // Half full: surface at 4, the cork rides a tenth above it. The leak lets it sink again.
            Tower.Add(36f);
            RunSeconds(2f);
            Assert.AreEqual(1, left);
            Assert.AreEqual(Tower.SurfaceY + 0.1f, Cork.TopY, 0.06f, "riding the surface down as the tower leaks");
            Assert.Less(Cork.TopY, 4.1f);
            Assert.Greater(Cork.TopY, 3f);
            Assert.AreEqual(0, arrived);

            // Poured in faster than it leaks, the water stands at the lip.
            bool pouring = true;
            Game.Context.OnUpdate(dt =>
            {
                if (pouring) Tower.Add(2f);
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Cork.AtTop, 3f), "the cork rises to the top");
            pouring = false;
            Assert.AreEqual(1, arrived);
            Assert.AreEqual(8f, Cork.TopY, 1e-3f, "the cork stops at its maximum, a tenth above the lip");
            Assert.LessOrEqual(Mathf.Abs(Cork.Speed), 3f + 1e-3f);

            // The tower drains: 24 s later the cork is back at rest.
            RunSeconds(26f);
            Assert.IsTrue(Cork.AtRest, "back at rest on the tower floor");
        }

        [Test]
        public void ItNeverGoesFasterThanItsMaxSpeed_NorChangesSpeedAbruptly()
        {
            Build(ctx => BuildBowl(ctx));
            Game.Context.OnUpdate(dt => Tower.Add(2f));
            float fastest = 0f, biggestChange = 0f, previous = 0f;
            for (int i = 0; i < 300 && !Cork.AtTop; i++)
            {
                Game.Tick();
                fastest = Mathf.Max(fastest, Cork.Speed);
                biggestChange = Mathf.Max(biggestChange, Mathf.Abs(Cork.Speed - previous));
                previous = Cork.Speed;
            }
            Assert.IsTrue(Cork.AtTop);
            Assert.AreEqual(3f, fastest, 1e-3f, "the water jumped to the lip at once; the cork rises at its own pace");
            Assert.LessOrEqual(biggestChange, 1f, "well inside the grip a rider has (6)");
        }
    }
}
