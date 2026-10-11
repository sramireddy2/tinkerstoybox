using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// The budget check (ART_BIBLE 12.1) counts what the frame really draws: a level is measured at the
    /// tier it was built for - materials are chosen while a level builds - and a level with a portal is
    /// measured with the portal's camera, which draws it a second time on Medium and High.
    /// </summary>
    public class BudgetCensusTests : SimTest
    {
        QualityTier tierBefore;

        [SetUp]
        public void RememberTheTier() => tierBefore = Materials.Tier;

        [TearDown]
        public void PutTheTierBack()
        {
            Materials.Tier = tierBefore;
            Settings.Use(null);
        }

        [Test]
        public void ASecondView_CountsDrawsShadowCastersAndTrianglesTwice_AndNothingElse()
        {
            var once = new SceneCensus { Renderers = 70, Draws = 90, ShadowCasterDraws = 30, Triangles = 20000, Materials = 31, Particles = 100 };
            SceneCensus twice = once.WithSecondView();
            Assert.AreEqual(180, twice.Draws);
            Assert.AreEqual(60, twice.ShadowCasterDraws);
            Assert.AreEqual(40000, twice.Triangles);
            Assert.AreEqual(70, twice.Renderers);
            Assert.AreEqual(31, twice.Materials);
            Assert.AreEqual(100, twice.Particles);
            Assert.AreEqual(90, once.Draws, "the census itself is not changed");

            // What one camera draws within the budget, two may not.
            TierSpec medium = TierSpec.Of(QualityTier.Medium);
            Assert.Less(90, medium.MaxDrawsMain);
            Assert.Greater(180, medium.MaxDrawsMain);
            Assert.IsNull(once.Over(medium), once.Over(medium));
            StringAssert.Contains("draws 180/" + medium.MaxDrawsMain, twice.Over(medium));
        }

        [Test]
        public void ALevelWithAPortal_IsCheckedWithItsSecondCamera_AndIsWithinTheBudgetOfEveryTier()
        {
            Settings.Use(new MemoryStore());
            foreach (TierSpec tier in TierSpec.All)
            {
                // As in the game: the tier is in force before the level is built.
                Materials.Tier = tier.Tier;
                Game?.Dispose();
                Game = Game.Create();
                Game.LoadLevel(14);
                bool second = SceneCensus.HasSecondView(Game, tier.Tier);
                Assert.AreEqual(tier.Tier != QualityTier.Low, second, tier.Name + ": the portal's view is a camera on Medium and High, a card on Low");

                Presentation presentation = Presentation.Create(Game, new PresentationOptions { Quality = tier.Tier });
                try
                {
                    for (int i = 0; i < 4; i++) presentation.Frame(Sim.Dt, 1f);
                    SceneCensus census = SceneCensus.Take(Game);
                    Assert.Greater(census.Renderers, 20, "the level is drawn: " + census);
                    SceneCensus counted = second ? census.WithSecondView() : census;
                    Debug.Log("[Toybox] level 14, " + tier.Name + ": " + census + (second ? " - twice with the portal's camera" : ""));
                    Assert.IsNull(counted.Over(tier), tier.Name + ": " + counted.Over(tier) + " (" + census + (second ? ", counted twice" : "") + ")");
                }
                finally
                {
                    presentation.Dispose();
                }
            }

            // A level without a portal has one view on every tier.
            Game.Dispose();
            Game = Game.Create();
            Game.LoadLevel(1);
            foreach (TierSpec tier in TierSpec.All) Assert.IsFalse(SceneCensus.HasSecondView(Game, tier.Tier), tier.Name);
            Assert.IsFalse(SceneCensus.HasSecondView(null, QualityTier.High));
        }
    }
}
