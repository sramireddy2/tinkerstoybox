using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The fishbowl of Level 13 as bookkeeping: bowl (area 270, 405 units of water) and tower (area 9).</summary>
    public abstract class FishbowlTest : SimTest
    {
        protected WaterVolume Bowl, Tower;
        protected Sponge Sponge;
        protected FloatPlatform Cork;
        protected Prop SpongeProp;

        protected void BuildBowl(LevelContext ctx, float spongeScale = 2f)
        {
            // Gravel floor at 0 (the tower floor is recessed by the cork's thickness), an island with the spawn on it.
            TestHelpers.Box(ctx, new Vector3(0f, -0.5f, -2f), new Vector3(24f, 1f, 17.4f));              // z -10.7 .. 6.7
            TestHelpers.Box(ctx, new Vector3(-6.75f, -0.5f, 8.35f), new Vector3(10.5f, 1f, 3.3f));       // beside the tower
            TestHelpers.Box(ctx, new Vector3(6.75f, -0.5f, 8.35f), new Vector3(10.5f, 1f, 3.3f));
            TestHelpers.Box(ctx, new Vector3(0f, -0.9f, 8.35f), new Vector3(3f, 1f, 3.3f));              // tower floor at -0.4
            TestHelpers.Box(ctx, new Vector3(0f, 1.5f, -7.6f), new Vector3(4f, 3f, 3.2f));               // castle island, top 3
            TestHelpers.Box(ctx, new Vector3(0f, 4f, 10.15f), new Vector3(3f, 8f, 0.3f));                // the bowl's glass behind the tower: rim at 8
            TestHelpers.Box(ctx, new Vector3(0f, 4f, 12.65f), new Vector3(6f, 8f, 4.7f));                // the books outside, top 8, from z 10.3
            Bowl = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "bowl", Footprint = Zone.Cylinder(0f, 0f, 10f, 0f, 8f), FloorY = 0f, Area = 270f, Volume = 405f, WadeDepth = 0.8f, SweepDelay = 0.3f,
            });
            Tower = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "tower", Footprint = Zone.MinMax(new Vector3(-1.5f, 0f, 6.7f), new Vector3(1.5f, 10.5f, 10f)), FloorY = 0f, Area = 9f, Volume = 0f,
                MaxSurfaceY = 7.9f, OverflowTo = Bowl, LeakTo = Bowl, LeakRate = 3f,
            });
            SpongeProp = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(0f, 3f + 0.25f * spongeScale, -6.9f), spongeScale, null, o => o.Tags = new[] { "sponge" });
            Sponge = new Sponge(ctx, new SpongeOptions { Prop = SpongeProp, Volumes = new[] { Tower, Bowl } });
            Cork = new FloatPlatform(ctx, new FloatPlatformOptions
            {
                Size = new Vector3(2.8f, 0.4f, 3f), Position = new Vector2(0f, 8.35f), Volume = Tower, RestY = 0f, MaxY = 8f, MaxSpeed = 3f, Freeboard = 0.1f,
            });
            ctx.SetSpawn(new Vector3(0f, 3f, -8.5f), 0f);
        }

        protected float Total => Bowl.Volume + Tower.Volume + Sponge.Stored;
    }

    public class WaterVolumeTests : FishbowlTest
    {
        [Test]
        public void TheSurface_IsFloorPlusVolumeOverArea_AndWaterIsConserved()
        {
            Build(ctx => BuildBowl(ctx));
            Assert.AreEqual(1.5f, Bowl.SurfaceY, 1e-4f, "405 / 270");
            Assert.AreEqual(1.5f, Bowl.Depth, 1e-4f);
            Assert.AreEqual(0f, Tower.SurfaceY, 1e-4f);
            Assert.IsTrue(Bowl.Over(new Vector3(3f, 50f, 3f)));
            Assert.IsFalse(Bowl.Over(new Vector3(9f, 0f, 9f)));

            Assert.AreEqual(100f, Bowl.Take(100f), 1e-4f);
            Assert.AreEqual(305f, Bowl.Take(1000f), 1e-3f, "only what is there can be taken");
            Assert.AreEqual(0f, Bowl.SurfaceY, 1e-4f);
            Bowl.Add(405f);
            Assert.AreEqual(1.5f, Bowl.SurfaceY, 1e-4f);
        }

        [Test]
        public void TheTower_OverflowsIntoTheBowl_AndLeaksBackEmpty()
        {
            var levels = new List<float>();
            float overflowed = 0f;
            Build(ctx => BuildBowl(ctx));
            Tower.LevelChanged += levels.Add;
            Tower.Overflowing += amount => overflowed += amount;
            // 100 units into an area of 9 would stand 11.1 high; the lip is at 7.9.
            Tower.Add(100f);
            Run(1);
            Assert.AreEqual(7.9f, Tower.SurfaceY, 1e-3f);
            Assert.AreEqual(100f - 7.9f * 9f - 3f * Sim.Dt, overflowed, 0.01f, "what does not fit pours over the lip");
            Assert.AreEqual(405f + 100f, Bowl.Volume + Tower.Volume, 1e-2f, "into the bowl: nothing is lost");
            // Leaking at 3 a second, 71 units are gone in 24 s.
            RunSeconds(24f);
            Assert.AreEqual(0f, Tower.Volume, 0.2f);
            Assert.AreEqual(505f, Bowl.Volume + Tower.Volume, 1e-2f);
            Assert.Greater(levels.Count, 100, "the level change is reported as it falls");

            // A level opens the flap: the tower holds nothing.
            Tower.MaxSurfaceY = 0f;
            Tower.Add(50f);
            Run(1);
            Assert.AreEqual(0f, Tower.Volume, 1e-4f);
        }

        [Test]
        public void ThePlayer_WadesUpToTheirDepth_AndIsSweptBackFromDeeperWater()
        {
            int swept = 0;
            Build(ctx => BuildBowl(ctx));
            Bowl.PlayerSwept += () => swept++;
            // Knee deep: surface at 0.7.
            Bowl.Set(0.7f * 270f);
            Game.Player.Teleport(new Vector3(5f, 0f, 0f), 0f);
            RunSeconds(1f);
            Assert.AreEqual(0, swept, "0.7 deep is wading");
            Assert.AreEqual(5f, Game.Player.Position.x, 0.01f);

            Bowl.Set(405f);
            RunSeconds(0.2f);
            Assert.AreEqual(0, swept, "1.5 deep, but not yet for the sweep delay");
            RunSeconds(0.2f);
            Assert.AreEqual(1, swept);
            Assert.AreEqual(-8.5f, Game.Player.Position.z, 0.05f, "back on the island");
            Assert.AreEqual(3f, Game.Player.Position.y, 0.05f);
            RunSeconds(1f);
            Assert.AreEqual(1, swept, "the island is dry");
        }
    }
}
