using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class HazardZoneTests : SimTest
    {
        [Test]
        public void FeetInTheZone_SendThePlayerBack_AndPropsAreIgnored()
        {
            HazardZone hazard = null;
            Prop crate = null;
            int caught = 0, mirrored = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                hazard = new HazardZone(ctx, new HazardZoneOptions { Shape = Zone.MinMax(new Vector3(-3f, -1f, 6f), new Vector3(3f, 0.5f, 12f)) });
                hazard.PlayerCaught += () => caught++;
                crate = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(1.5f, 0.3f, 9f));
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Game.Events.HazardCaught += e => mirrored++;
            RunSeconds(1f);
            Assert.AreEqual(0, caught, "a prop in the zone is nobody's business");

            Input.Hold = new InputFrame { MoveZ = 1f };
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => caught > 0, 3f), "walking into the zone gets the player caught");
            Assert.Less(Game.Player.Position.z, 1f, "back at the checkpoint");
            Assert.AreEqual(1, hazard.Catches);
            Assert.AreEqual(1, mirrored);
            Assert.Greater(crate.Center.z, 8f, "the crate stayed where it was");
        }

        [Test]
        public void TheDelay_ForgivesABriefTouch_AndOnCaughtReplacesTheRespawn()
        {
            HazardZone hazard = null;
            int custom = 0;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                hazard = new HazardZone(ctx, new HazardZoneOptions
                {
                    Shape = Zone.Cylinder(0f, 0f, 2f, -1f, 1f), Delay = 0.5f, OnCaught = () => custom++,
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(0.4f);
            Assert.AreEqual(0, custom, "not yet");
            Assert.IsTrue(hazard.PlayerInside);
            RunSeconds(0.2f);
            Assert.AreEqual(1, custom, "caught after the delay; the level's own action ran");
            Assert.AreEqual(0f, Game.Player.Position.z, 0.01f);
        }
    }
}
