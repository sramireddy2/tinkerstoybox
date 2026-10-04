using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Progress = Toybox.Platform.Progress;
using Object = UnityEngine.Object;
using Volume = UnityEngine.Rendering.Volume;

namespace Toybox.Tests
{
    /// <summary>
    /// The rules of ART_BIBLE 7.3 / 7.4 / 12.1 as data and functions: the tier table, the starting tier,
    /// the render scale, the render-target estimate and the governor. No Unity objects involved.
    /// </summary>
    public sealed class QualityRuleTests
    {
        const float Fast = 1f / 60f, Slow = 0.025f;

        [Test]
        public void TheTierTable_IsTheArtBibles()
        {
            Assert.AreEqual(3, TierSpec.All.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual((QualityTier)i, TierSpec.All[i].Tier);
                Assert.AreSame(TierSpec.All[i], TierSpec.Of((QualityTier)i));
            }
            TierSpec low = TierSpec.Low, medium = TierSpec.Medium, high = TierSpec.High;

            // 7.3
            Assert.AreEqual(1536 * 864, low.PixelBudget);
            Assert.AreEqual(1920 * 1080, medium.PixelBudget);
            Assert.AreEqual(2560 * 1440, high.PixelBudget);
            CollectionAssert.AreEqual(new[] { 1, 2, 4 }, new[] { low.Msaa, medium.Msaa, high.Msaa });
            CollectionAssert.AreEqual(new[] { true, false, false }, new[] { low.Fxaa, medium.Fxaa, high.Fxaa });
            CollectionAssert.AreEqual(new[] { false, true, true }, new[] { low.Bloom, medium.Bloom, high.Bloom });
            CollectionAssert.AreEqual(new[] { false, false, true }, new[] { low.BloomHighQuality, medium.BloomHighQuality, high.BloomHighQuality });
            Assert.AreEqual(4, medium.BloomIterations);
            Assert.AreEqual(5, high.BloomIterations);
            CollectionAssert.AreEqual(new[] { false, true, true }, new[] { low.MacroBand, medium.MacroBand, high.MacroBand });
            Assert.AreEqual(8, medium.MacroTaps);
            Assert.AreEqual(3.5f, medium.MacroRadiusPx);
            Assert.AreEqual(12, high.MacroTaps);
            Assert.AreEqual(5f, high.MacroRadiusPx);
            CollectionAssert.AreEqual(new[] { 6, 10, 16 }, new[] { low.PoolSpheres, medium.PoolSpheres, high.PoolSpheres });
            CollectionAssert.AreEqual(new[] { 128, 256, 256 }, new[] { low.DetailSize, medium.DetailSize, high.DetailSize });
            CollectionAssert.AreEqual(new[] { 1, 4, 4 }, new[] { low.DetailAniso, medium.DetailAniso, high.DetailAniso });
            CollectionAssert.AreEqual(new[] { false, false, true }, new[] { low.DetailBump, medium.DetailBump, high.DetailBump });
            CollectionAssert.AreEqual(new[] { false, true, true }, new[] { low.GlassBackShell, medium.GlassBackShell, high.GlassBackShell });
            CollectionAssert.AreEqual(new[] { 0, 120, 300 }, new[] { low.DustMotes, medium.DustMotes, high.DustMotes });
            CollectionAssert.AreEqual(new[] { 60, 150, 300 }, new[] { low.Confetti, medium.Confetti, high.Confetti });
            CollectionAssert.AreEqual(new[] { 0.6f, 0.8f, 0.8f }, new[] { low.ScaleFloor, medium.ScaleFloor, high.ScaleFloor });

            // 5.3: the first cascade ends 20 units from the camera on every tier.
            CollectionAssert.AreEqual(new[] { 2048, 4096, 4096 }, new[] { low.ShadowResolution, medium.ShadowResolution, high.ShadowResolution });
            CollectionAssert.AreEqual(new[] { 110f, 170f, 240f }, new[] { low.ShadowDistance, medium.ShadowDistance, high.ShadowDistance });
            CollectionAssert.AreEqual(new[] { 2, 2, 3 }, new[] { low.Cascades, medium.Cascades, high.Cascades });
            Assert.AreEqual(TierSpec.FirstCascade, low.Cascade2Split * low.ShadowDistance, 0.25f);
            Assert.AreEqual(TierSpec.FirstCascade, medium.Cascade2Split * medium.ShadowDistance, 0.25f);
            Assert.AreEqual(TierSpec.FirstCascade, high.Cascade3Split.x * high.ShadowDistance, 0.25f);
            Assert.AreEqual(80f, high.Cascade3Split.y * high.ShadowDistance, 1f);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, new[] { low.SoftShadowQuality, medium.SoftShadowQuality, high.SoftShadowQuality });
            Assert.AreEqual(new Vector2Int(2048, 1024), new Vector2Int(low.ShadowAtlasWidth, low.ShadowAtlasHeight));
            Assert.AreEqual(new Vector2Int(4096, 2048), new Vector2Int(medium.ShadowAtlasWidth, medium.ShadowAtlasHeight));
            Assert.AreEqual(new Vector2Int(4096, 4096), new Vector2Int(high.ShadowAtlasWidth, high.ShadowAtlasHeight));

            // 12.1
            CollectionAssert.AreEqual(new[] { 130, 160, 180 }, new[] { low.MaxDrawsMain, medium.MaxDrawsMain, high.MaxDrawsMain });
            CollectionAssert.AreEqual(new[] { 120, 140, 210 }, new[] { low.MaxDrawsShadow, medium.MaxDrawsShadow, high.MaxDrawsShadow });
            CollectionAssert.AreEqual(new[] { 300, 350, 450 }, new[] { low.MaxDrawsFrame, medium.MaxDrawsFrame, high.MaxDrawsFrame });
            CollectionAssert.AreEqual(new[] { 150000, 200000, 250000 }, new[] { low.MaxTriangles, medium.MaxTriangles, high.MaxTriangles });
            Assert.AreEqual(40, TierSpec.MaxMaterials);
            Assert.AreEqual(40, TierSpec.MaxShaderPrograms);
        }

        [Test]
        public void TheStartingTier_FollowsTheDeviceTable()
        {
            QualityTier Start(string name, int maxTexture = 16384, bool handheld = false, bool hdr = true) =>
                Quality.StartingTier(new DeviceInfo(name, maxTexture, handheld, hdr));

            Assert.AreEqual(QualityTier.Medium, Start("NVIDIA GeForce RTX 3060"));
            Assert.AreEqual(QualityTier.Medium, Start("ANGLE (Intel, Intel(R) Iris(R) Xe Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)"), "Iris Xe is the Medium reference");
            Assert.AreEqual(QualityTier.Medium, Start(""), "a masked name");
            Assert.AreEqual(QualityTier.Medium, Start(null));
            Assert.AreEqual(QualityTier.Medium, Start("Apple M2"));

            Assert.AreEqual(QualityTier.Low, Start("ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)"));
            Assert.AreEqual(QualityTier.Low, Start("Intel(R) HD Graphics 4000"));
            Assert.AreEqual(QualityTier.Low, Start("Google SwiftShader"));
            Assert.AreEqual(QualityTier.Low, Start("llvmpipe (LLVM 15.0.7, 256 bits)"));
            Assert.AreEqual(QualityTier.Low, Start("Microsoft Basic Render Driver"));
            Assert.AreEqual(QualityTier.Low, Start("NVIDIA GeForce RTX 3060", maxTexture: 2048));
            Assert.AreEqual(QualityTier.Low, Start("Adreno (TM) 740", handheld: true));
            Assert.AreEqual(QualityTier.Low, Start("NVIDIA GeForce RTX 3060", hdr: false), "no HDR target");
            // High is never chosen automatically.
            foreach (string name in new[] { "NVIDIA GeForce RTX 4090", "AMD Radeon RX 7900 XTX" }) Assert.AreNotEqual(QualityTier.High, Start(name));
        }

        [Test]
        public void TheRenderScale_KeepsThePictureWithinTheTiersPixelBudget()
        {
            Assert.AreEqual(1f, Quality.BaseScale(TierSpec.Medium, 1920, 1080));
            Assert.AreEqual(1f, Quality.BaseScale(TierSpec.Medium, 1280, 720), "a smaller screen is not scaled up");
            Assert.AreEqual(0.75f, Quality.BaseScale(TierSpec.Medium, 2560, 1440), 1e-4f);
            Assert.AreEqual(0.5f, Quality.BaseScale(TierSpec.Medium, 3840, 2160), 1e-4f);
            Assert.AreEqual(0.5f, Quality.BaseScale(TierSpec.Low, 3840, 2160), "never below half for the screen's sake");
            Assert.AreEqual(0.8f, Quality.BaseScale(TierSpec.Low, 1920, 1080), 1e-4f);
            Assert.AreEqual(1f, Quality.BaseScale(TierSpec.High, 2560, 1440));
            Assert.AreEqual(1f, Quality.BaseScale(TierSpec.Medium, 0, 0), "no screen, no scaling");

            // The governor's share multiplies the screen's.
            Assert.AreEqual(0.9f, Quality.RenderScale(TierSpec.Medium, 1920, 1080, 0.9f), 1e-4f);
            Assert.AreEqual(0.6f, Quality.RenderScale(TierSpec.Medium, 2560, 1440, 0.8f), 1e-4f);
            foreach (TierSpec tier in TierSpec.All)
            {
                float scale = Quality.RenderScale(tier, 2560, 1440, 1f);
                Assert.LessOrEqual(2560 * scale * 1440 * scale, tier.PixelBudget * 1.03f, tier.Name);
            }
        }

        [Test]
        public void RenderTargetMemory_IsAboutWhatTheBudgetSays()
        {
            foreach (TierSpec tier in TierSpec.All)
            {
                float mb = Quality.RenderTargetBytes(tier, tier.PixelWidth, tier.PixelHeight) / (1024f * 1024f);
                Assert.Greater(mb, tier.MaxRenderTargetMb * 0.6f, tier.Name);
                // "About 20 / 75 / 180 MB": the estimate may be a few megabytes over on High.
                Assert.Less(mb, tier.MaxRenderTargetMb * 1.05f, tier.Name);
            }
            Assert.Less(Quality.RenderTargetBytes(TierSpec.Medium, 1920, 1080, 0.8f), Quality.RenderTargetBytes(TierSpec.Medium, 1920, 1080));
        }

        static int Feed(QualityGovernor governor, float dt, int frames)
        {
            int changes = 0;
            for (int i = 0; i < frames; i++)
                if (governor.Frame(dt)) changes++;
            return changes;
        }

        static int FeedSeconds(QualityGovernor governor, float dt, float seconds) => Feed(governor, dt, Mathf.CeilToInt(seconds / dt));

        [Test]
        public void TwelveSlowFramesIn120_StepTheScaleDown_ThenTheTier()
        {
            var governor = new QualityGovernor(QualityTier.Medium);
            Assert.AreEqual(0, Feed(governor, Fast, 108));
            Assert.AreEqual(0, Feed(governor, Slow, 11), "eleven slow frames are tolerated");
            Assert.IsTrue(governor.Frame(Slow), "the twelfth is a step down");
            Assert.AreEqual(QualityTier.Medium, governor.Tier);
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
            Assert.IsTrue(governor.Waiting);

            // Three seconds to settle: the reallocation itself hitches.
            Assert.AreEqual(0, FeedSeconds(governor, Slow, QualityGovernor.SettleTime - 0.05f));
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
            Assert.AreEqual(1, Feed(governor, Slow, 20));
            Assert.AreEqual(0.8f, governor.Scale, 1e-4f, "Medium's floor");

            // At the floor the next step is a tier, with the scale back at 1.
            Assert.AreEqual(1, FeedSeconds(governor, Slow, 4f));
            Assert.AreEqual(QualityTier.Low, governor.Tier);
            Assert.AreEqual(1f, governor.Scale);

            // Low goes down to 0.6 and then has nothing left to give.
            Assert.AreEqual(4, FeedSeconds(governor, Slow, 30f));
            Assert.AreEqual(QualityTier.Low, governor.Tier);
            Assert.AreEqual(0.6f, governor.Scale, 1e-4f);
            Assert.AreEqual(0, FeedSeconds(governor, Slow, 30f));
            Assert.AreEqual(7, governor.StepsDown);
        }

        [Test]
        public void ElevenSlowFramesInEvery120_AreTolerated()
        {
            var governor = new QualityGovernor(QualityTier.Medium);
            for (int round = 0; round < 40; round++)
            {
                Assert.AreEqual(0, Feed(governor, Fast, 109));
                Assert.AreEqual(0, Feed(governor, Slow, 11));
            }
            Assert.AreEqual(QualityTier.Medium, governor.Tier);
            Assert.AreEqual(1f, governor.Scale);
            Assert.IsFalse(governor.SteppedUp, "and with a slow frame every two seconds it never steps up either");
        }

        [Test]
        public void FramesOver100ms_AreIgnored_UnlessTheyComeInARow()
        {
            var governor = new QualityGovernor(QualityTier.Low);
            // A tab switch, a load, a GC now and then: not the picture's fault.
            for (int i = 0; i < 60; i++)
            {
                Assert.IsFalse(governor.Frame(0.4f));
                Assert.AreEqual(0, Feed(governor, Fast, 8));
            }
            Assert.AreEqual(0, governor.SlowInWindow);
            Assert.AreEqual(1f, governor.Scale);

            // A picture that takes 150 ms every frame is slow, whatever the reason.
            int frames = 0;
            while (!governor.Frame(0.15f) && frames < 100) frames++;
            Assert.AreEqual(QualityGovernor.IgnoredRun - 1 + QualityGovernor.SlowLimit - 1, frames);
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
        }

        [Test]
        public void TheFiveSecondsAfterALevelLoad_AreNotJudged()
        {
            var governor = new QualityGovernor(QualityTier.Medium);
            governor.LevelLoaded();
            Assert.IsTrue(governor.Waiting);
            Assert.AreEqual(0, FeedSeconds(governor, Slow, QualityGovernor.LoadGrace - 0.05f));
            Assert.AreEqual(0, governor.SlowInWindow);
            Assert.AreEqual(1, Feed(governor, Slow, 20));
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);

            // A load in the middle of a bad stretch forgets it.
            var second = new QualityGovernor(QualityTier.Medium);
            Feed(second, Slow, 11);
            second.LevelLoaded();
            Assert.AreEqual(0, second.SlowInWindow);
        }

        [Test]
        public void AfterTwentyCalmSeconds_ItStepsUpOncePerSession()
        {
            // From Medium, with the scale where it belongs: try High.
            var governor = new QualityGovernor(QualityTier.Medium);
            Assert.AreEqual(0, FeedSeconds(governor, Fast, QualityGovernor.CalmTime - 0.5f));
            Assert.AreEqual(1, FeedSeconds(governor, Fast, 1f));
            Assert.AreEqual(QualityTier.High, governor.Tier);
            Assert.IsTrue(governor.SteppedUp);
            // It holds: High stays, and later trouble is an ordinary step down.
            Assert.AreEqual(0, FeedSeconds(governor, Fast, 60f));
            Assert.AreEqual(1, Feed(governor, Slow, 12));
            Assert.AreEqual(QualityTier.High, governor.Tier);
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
            Assert.IsFalse(governor.Locked);
            // Once per session.
            Assert.AreEqual(0, FeedSeconds(governor, Fast, 120f));
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
            Assert.AreEqual(1, governor.StepsUp);

            // With the scale lowered, the step up raises the scale and leaves the tier alone.
            var lowered = new QualityGovernor(QualityTier.Medium);
            Feed(lowered, Slow, 12);
            Assert.AreEqual(0.9f, lowered.Scale, 1e-4f);
            Assert.AreEqual(1, FeedSeconds(lowered, Fast, QualityGovernor.SettleTime + QualityGovernor.CalmTime + 0.5f));
            Assert.AreEqual(QualityTier.Medium, lowered.Tier);
            Assert.AreEqual(1f, lowered.Scale, 1e-4f);

            // A session that started on Low never tries a higher tier, and High has nowhere to go.
            var low = new QualityGovernor(QualityTier.Low);
            Assert.AreEqual(0, FeedSeconds(low, Fast, 120f));
            Assert.AreEqual(QualityTier.Low, low.Tier);
            var high = new QualityGovernor(QualityTier.High);
            Assert.AreEqual(0, FeedSeconds(high, Fast, 120f));
            Assert.AreEqual(QualityTier.High, high.Tier);
        }

        [Test]
        public void AStepUpThatDoesNotHold_IsTakenBack_AndNothingIsRaisedAgain()
        {
            var governor = new QualityGovernor(QualityTier.Medium);
            Assert.AreEqual(1, FeedSeconds(governor, Fast, QualityGovernor.CalmTime + 0.5f));
            Assert.AreEqual(QualityTier.High, governor.Tier);

            // Slow right away: judged once the three seconds are over, well within the ten.
            Assert.AreEqual(1, FeedSeconds(governor, Slow, QualityGovernor.SettleTime + 1f));
            Assert.AreEqual(QualityTier.Medium, governor.Tier, "back where the picture ran smoothly");
            Assert.AreEqual(1f, governor.Scale);
            Assert.IsTrue(governor.Locked);

            Assert.AreEqual(0, FeedSeconds(governor, Fast, 120f), "never up again");
            Assert.AreEqual(QualityTier.Medium, governor.Tier);
            // Down is still possible: a heavier level must not leave the player with a slow picture.
            Assert.AreEqual(1, Feed(governor, Slow, 12));
            Assert.AreEqual(0.9f, governor.Scale, 1e-4f);
        }
    }

    /// <summary>What the pipeline's setup steps leave in the project (ART_BIBLE 3.8, 5.3, 7.2, 7.3, 7.5).</summary>
    public sealed class PipelineSetupTests
    {
        static UniversalRendererData Renderer => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CoreSetup.RendererPath);

        static T Field<T>(Object target, string name, Func<SerializedProperty, T> read)
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(name);
            Assert.IsNotNull(property, target.name + " has no field " + name);
            return read(property);
        }

        static int Int(Object target, string name) => Field(target, name, p => p.intValue);
        static bool Bool(Object target, string name) => Field(target, name, p => p.boolValue);
        static float Float(Object target, string name) => Field(target, name, p => p.floatValue);

        [Test]
        public void ThreeUrpAssets_CarryTheTiersNumbers_AndShareOneRenderer()
        {
            UniversalRendererData renderer = Renderer;
            Assert.IsNotNull(renderer);
            foreach (TierSpec tier in TierSpec.All)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineSetup.TierAssetPath(tier.Tier));
                Assert.IsNotNull(asset, PipelineSetup.TierAssetPath(tier.Tier));
                string name = tier.Name;
                Assert.AreEqual("ToyboxURP_" + name, asset.name);
                Assert.AreEqual(1, asset.rendererDataList.Length, name);
                Assert.AreSame(renderer, asset.rendererDataList[0], name + ": the shared renderer");

                Assert.IsTrue(asset.supportsHDR, name);
                Assert.AreEqual(HDRColorBufferPrecision._32Bits, asset.hdrColorBufferPrecision, name);
                Assert.AreEqual(tier.Msaa, asset.msaaSampleCount, name);
                Assert.AreEqual(1f, asset.renderScale, name + ": the authored scale");
                Assert.IsFalse(asset.supportsCameraDepthTexture, name + ": no depth texture");
                Assert.IsFalse(asset.supportsCameraOpaqueTexture, name + ": no opaque texture");
                Assert.AreEqual(1, Int(asset, "m_UpscalingFilter"), name + ": linear upscaling");

                // 5.3 / 3.8: main-light shadows on, cascades (never one), soft shadows on - on every tier.
                Assert.AreEqual(LightRenderingMode.PerPixel, asset.mainLightRenderingMode, name);
                Assert.IsTrue(asset.supportsMainLightShadows, name);
                Assert.IsTrue(asset.supportsSoftShadows, name);
                Assert.GreaterOrEqual(asset.shadowCascadeCount, 2, name);
                Assert.AreEqual(tier.Cascades, asset.shadowCascadeCount, name);
                Assert.AreEqual(tier.ShadowResolution, asset.mainLightShadowmapResolution, name);
                Assert.AreEqual(tier.ShadowDistance, asset.shadowDistance, name);
                if (tier.Cascades == 2) Assert.AreEqual(tier.Cascade2Split, asset.cascade2Split, 1e-5f, name);
                else Assert.AreEqual(tier.Cascade3Split, asset.cascade3Split, name);
                Assert.AreEqual(0.1f, asset.cascadeBorder, 1e-5f, name);
                Assert.AreEqual(1f, asset.shadowDepthBias, name);
                Assert.AreEqual(1f, asset.shadowNormalBias, name);
                Assert.IsTrue(asset.conservativeEnclosingSphere, name);
                Assert.AreEqual(tier.SoftShadowQuality, Int(asset, "m_SoftShadowQuality"), name);

                Assert.AreEqual(LightRenderingMode.Disabled, asset.additionalLightsRenderingMode, name);
                Assert.IsFalse(asset.supportsMixedLighting, name);
                Assert.IsFalse(asset.supportsLightCookies, name);
                Assert.IsFalse(asset.useRenderingLayers, name);
                Assert.IsFalse(Bool(asset, "m_ReflectionProbeBlending"), name);
                Assert.IsFalse(Bool(asset, "m_ReflectionProbeBoxProjection"), name);
                Assert.IsTrue(asset.useSRPBatcher, name);
                Assert.IsFalse(Bool(asset, "m_SupportsDynamicBatching"), name + ": the editor logs an error while this is on");
                Assert.AreEqual(ColorGradingMode.LowDynamicRange, asset.colorGradingMode, name);
                Assert.AreEqual(32, asset.colorGradingLutSize, name);
            }
        }

        [Test]
        public void ThreeQualityLevels_AllInTheWebBuild_MediumTheDefault()
        {
            CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, QualitySettings.names);
            foreach (TierSpec tier in TierSpec.All)
            {
                int index = Quality.LevelIndex(tier.Tier);
                Assert.AreEqual((int)tier.Tier, index);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineSetup.TierAssetPath(tier.Tier));
                Assert.AreSame(asset, QualitySettings.GetRenderPipelineAssetAt(index), tier.Name);
                Assert.AreSame(asset, Quality.PipelineOf(tier.Tier));
                // URP strips shader variants by the union of the features of the assets in the build.
                Assert.IsTrue(QualitySettings.IsPlatformIncluded("WebGL", index), tier.Name + " is enabled for WebGL");
                Assert.IsTrue(QualitySettings.IsPlatformIncluded("Standalone", index), tier.Name);
            }

            Object settings = SetupUtil.ProjectSettingsAsset("ProjectSettings/QualitySettings.asset");
            var serialized = new SerializedObject(settings);
            bool sawWebGl = false;
            foreach (SerializedProperty platform in serialized.FindProperty("m_PerPlatformDefaultQuality"))
            {
                string name = platform.FindPropertyRelative("first").stringValue;
                Assert.AreEqual((int)QualityTier.Medium, platform.FindPropertyRelative("second").intValue, name + " starts on Medium");
                sawWebGl |= name == "WebGL";
            }
            Assert.IsTrue(sawWebGl);
            SerializedProperty levels = serialized.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
                Assert.AreEqual(0, levels.GetArrayElementAtIndex(i).FindPropertyRelative("globalTextureMipmapLimit").intValue, "no level halves the textures");

            // Outside a running game the editor sits on Medium, with Medium's asset as the fallback pipeline.
            Assert.AreEqual(QualityTier.Medium, Quality.ActiveTier);
            Assert.AreSame(Quality.PipelineOf(QualityTier.Medium), GraphicsSettings.defaultRenderPipeline);
            Assert.AreSame(Quality.PipelineOf(QualityTier.Medium), GraphicsSettings.currentRenderPipeline);
        }

        [Test]
        public void TheRenderer_IsForwardWithStencil_AndBlursBeforeTheSticker()
        {
            UniversalRendererData renderer = Renderer;
            Assert.AreEqual(RenderingMode.Forward, renderer.renderingMode);
            Assert.AreEqual(DepthPrimingMode.Disabled, renderer.depthPrimingMode);
            Assert.AreEqual(IntermediateTextureMode.Always, renderer.intermediateTextureMode);
            Assert.AreEqual((int)DepthFormat.Depth_24_Stencil_8, Int(renderer, "m_DepthAttachmentFormat"), "stencil in the depth attachment");
            Assert.IsNotNull(renderer.postProcessData, "URP's post shaders and textures (7.5)");

            FullScreenPassRendererFeature blur = null;
            int blurIndex = -1, stickerIndex = -1;
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feature = renderer.rendererFeatures[i];
                Assert.IsNotNull(feature, "feature " + i);
                if (feature.name == PostLook.MacroBandFeature)
                {
                    blur = feature as FullScreenPassRendererFeature;
                    blurIndex = i;
                }
                if (feature.name == "Sticker") stickerIndex = i;
            }
            Assert.IsNotNull(blur, "the stock full-screen feature named MacroBand");
            Assert.AreSame(blur, Quality.RendererFeature<FullScreenPassRendererFeature>(PostLook.MacroBandFeature));
            if (stickerIndex >= 0) Assert.Less(blurIndex, stickerIndex, "the sticker is on the lens: the lens blur runs before it");

            var material = AssetDatabase.LoadAssetAtPath<Material>(PipelineSetup.MacroBandPath);
            Assert.IsNotNull(material);
            Assert.AreEqual(PipelineSetup.MacroBandShader, material.shader.name);
            Assert.AreSame(material, Resources.Load<Material>("Materials/MacroBand"), "under Resources, so the shader ships");
            Assert.AreSame(material, blur.passMaterial);
            Assert.AreEqual(0, blur.passIndex);
            Assert.AreEqual(FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing, blur.injectionPoint);
            Assert.IsTrue(blur.fetchColorBuffer);
            Assert.AreEqual(ScriptableRenderPassInput.None, blur.requirements, "no depth, no normals, no opaque copy");
            Assert.IsFalse(blur.bindDepthStencilAttachment);
            Assert.IsTrue(blur.isActive, "on, while no game is running");
        }

        [Test]
        public void TheVolumeProfileAsset_HoldsTheFourOverrides()
        {
            var profile = Resources.Load<VolumeProfile>(PostLook.ProfileResource);
            Assert.IsNotNull(profile, "Resources/" + PostLook.ProfileResource);
            Assert.AreEqual(PipelineSetup.VolumeProfilePath, AssetDatabase.GetAssetPath(profile));
            Assert.AreEqual(4, profile.components.Count);

            Assert.IsTrue(profile.TryGet(out Bloom bloom));
            Assert.IsTrue(bloom.active);
            Overridden(bloom.threshold, 1.1f);
            Overridden(bloom.intensity, 0.35f);
            Overridden(bloom.scatter, 0.6f);
            Overridden(bloom.clamp, 8f);
            Overridden(bloom.tint, Color.white);
            Overridden(bloom.highQualityFiltering, false);
            Overridden(bloom.downscale, BloomDownscaleMode.Half);
            Overridden(bloom.maxIterations, 4);

            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping));
            Assert.IsTrue(tonemapping.active);
            Overridden(tonemapping.mode, PostLook.Tonemap);
            Assert.AreNotEqual(TonemappingMode.ACES, tonemapping.mode.value, "ACES is banned");

            Assert.IsTrue(profile.TryGet(out ColorAdjustments adjustments));
            Assert.IsTrue(adjustments.active);
            Overridden(adjustments.postExposure, 0.2f);
            Overridden(adjustments.contrast, 8f);
            Overridden(adjustments.saturation, 6f);

            Assert.IsTrue(profile.TryGet(out Vignette vignette));
            Assert.IsTrue(vignette.active);
            Overridden(vignette.color, Palette.Lin(Palette.Ink));
            Overridden(vignette.intensity, 0.18f);
            Overridden(vignette.smoothness, 0.45f);
            Overridden(vignette.center, new Vector2(0.5f, 0.5f));
            Overridden(vignette.rounded, false);
        }

        static void Overridden<T>(VolumeParameter<T> parameter, T value)
        {
            Assert.IsTrue(parameter.overrideState, "overridden: " + value);
            Assert.AreEqual(value, parameter.value);
        }

        [Test]
        public void PostVariantStripping_IsOff_AndTheGlobalSettingsAreRegistered()
        {
            Assert.IsNotNull(GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>(), "the URP global settings");
            var stripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            Assert.IsNotNull(stripping);
            Assert.IsFalse(stripping.stripUnusedPostProcessingVariants, "the game changes its volume profile at runtime");
        }

        [Test]
        public void TheVariantCollection_ListsTheGamesShaders_WithinTheProgramBudget()
        {
            var collection = Resources.Load<ShaderVariantCollection>(ShaderWarmup.Resource);
            Assert.IsNotNull(collection, "Resources/" + ShaderWarmup.Resource);
            Assert.AreEqual(PipelineSetup.VariantsPath, AssetDatabase.GetAssetPath(collection));
            Assert.LessOrEqual(collection.variantCount, TierSpec.MaxShaderPrograms);

            Shader blur = Shader.Find(PipelineSetup.MacroBandShader);
            Assert.IsNotNull(blur);
            Assert.IsFalse(ShaderUtil.ShaderHasError(blur), "MacroBand compiles");
            Assert.IsTrue(collection.Contains(new ShaderVariantCollection.ShaderVariant { shader = blur, passType = PassType.Normal, keywords = new string[0] }));

            // The lit shaders, once they exist, with the keywords this pipeline really draws them with:
            // cascades and soft shadows on every tier, and the empty-shadow-map state.
            int shaders = 0;
            foreach (string name in PipelineSetup.GameShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) continue;
                shaders++;
                if (!shader.keywordSpace.FindKeyword("_MAIN_LIGHT_SHADOWS_CASCADE").isValid) continue;
                Assert.IsTrue(collection.Contains(new ShaderVariantCollection.ShaderVariant
                {
                    shader = shader, passType = PassType.ScriptableRenderPipeline, keywords = new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT" },
                }), name + ": cascades + soft shadows");
                Assert.IsTrue(collection.Contains(new ShaderVariantCollection.ShaderVariant
                {
                    shader = shader, passType = PassType.ShadowCaster, keywords = new string[0],
                }), name + ": shadow caster");
            }
            Assert.AreEqual(shaders, collection.shaderCount, "one entry per shader of the game that exists");
        }

        [Test]
        public void RunningTheSteps_ASecondTime_ChangesNothing()
        {
            List<SetupStep> steps = ProjectSetup.Filter(ProjectSetup.Discover(), nameof(PipelineSetup));
            Assert.GreaterOrEqual(steps.Count, 7);
            foreach (SetupStep step in steps)
            {
                Assert.GreaterOrEqual(step.Order, 100, step.Name);
                Assert.Less(step.Order, 200, step.Name + ": the pipeline's range");
            }

            // Once, in case something was left changed in memory; then the run that is measured.
            Assert.AreEqual(0, ProjectSetup.RunSteps(steps));
            var objects = new List<Object>
            {
                Renderer,
                Quality.RendererFeature<FullScreenPassRendererFeature>(PostLook.MacroBandFeature),
                AssetDatabase.LoadAssetAtPath<Material>(PipelineSetup.MacroBandPath),
                Resources.Load<ShaderVariantCollection>(ShaderWarmup.Resource),
                SetupUtil.ProjectSettingsAsset("ProjectSettings/QualitySettings.asset"),
                GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>(),
            };
            foreach (TierSpec tier in TierSpec.All) objects.Add(Quality.PipelineOf(tier.Tier));
            var profile = Resources.Load<VolumeProfile>(PostLook.ProfileResource);
            objects.Add(profile);
            objects.AddRange(profile.components);

            var before = new List<int>();
            foreach (Object item in objects)
            {
                Assert.IsNotNull(item);
                before.Add(EditorUtility.GetDirtyCount(item));
            }
            int featureCount = Renderer.rendererFeatures.Count;

            Assert.AreEqual(0, ProjectSetup.RunSteps(steps));

            for (int i = 0; i < objects.Count; i++)
                Assert.AreEqual(before[i], EditorUtility.GetDirtyCount(objects[i]), objects[i].name + " (" + objects[i].GetType().Name + ") was written again");
            Assert.AreEqual(featureCount, Renderer.rendererFeatures.Count, "no second MacroBand feature");
        }
    }

    /// <summary>The two presenters at work: tiers, the render scale, the governor, the post volume, the lens blur and the flashes.</summary>
    public sealed class PipelinePresenterTests : SimTest
    {
        // A room after dark, for the preset-dependent numbers.
        sealed class NightLevel : LevelDefinition
        {
            public override string Environment => "night-light";

            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 40f);
                ctx.SetSpawn(Vector3.zero, 0f);
            }
        }

        Presentation presentation;
        GameFlow flow;
        int levelBefore;

        QualityPresenter Tiers => presentation.Get<QualityPresenter>();
        PostLook Post => presentation.Get<PostLook>();
        static FullScreenPassRendererFeature BlurFeature => Quality.RendererFeature<FullScreenPassRendererFeature>(PostLook.MacroBandFeature);

        [SetUp]
        public void OwnSettings()
        {
            Settings.Use(new MemoryStore());
            levelBefore = QualitySettings.GetQualityLevel();
        }

        [TearDown]
        public void DisposePresentation()
        {
            presentation?.Dispose();
            presentation = null;
            flow?.Dispose();
            flow = null;
            Settings.Use(null);
            Assert.AreEqual(levelBefore, QualitySettings.GetQualityLevel(), "the Quality level is put back");
            foreach (TierSpec tier in TierSpec.All) Assert.AreEqual(1f, Quality.PipelineOf(tier.Tier).renderScale, tier.Name + ": the render scale is put back");
            Assert.IsTrue(BlurFeature.isActive, "the MacroBand feature is left on");
        }

        Presentation Present(QualityTier? tier = null, LevelDefinition level = null, bool withFlow = false)
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            if (withFlow)
            {
                flow = new GameFlow(Game, new LevelList(new[] { 0 }, id => level ?? new AdHocLevel(ctx => TestHelpers.Floor(ctx))), new Progress(new MemoryStore()));
                flow.StartLevel(0);
            }
            else
            {
                Game.LoadLevel(level ?? new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            }
            presentation = Presentation.Create(Game, new PresentationOptions { Plain = true, Presenters = PipelineSetup.PlainLitPresenters(), Quality = tier, Flow = flow });
            return presentation;
        }

        void Frames(float dt, int count)
        {
            for (int i = 0; i < count; i++) presentation.Frame(dt, 1f);
        }

        [Test]
        public void BothPresenters_AreFound_QualityFirst()
        {
            PresenterRegistry.Entry quality = null, post = null;
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
            {
                if (entry.Type == typeof(QualityPresenter)) quality = entry;
                if (entry.Type == typeof(PostLook)) post = entry;
            }
            Assert.IsNotNull(quality);
            Assert.IsNotNull(post);
            Assert.IsFalse(quality.ProvidesLook || quality.ProvidesHud || quality.Fallback, "the tier is decided with the plain look too");
            Assert.IsTrue(post.ProvidesLook);
            Assert.Less(quality.Order, post.Order, "the tier is decided before anyone reads it");
            Assert.Less(post.Order, 100, "the pipeline's range");

            List<PresenterRegistry.Entry> plain = PresenterRegistry.Select(PresenterRegistry.All, plain: true);
            Assert.IsTrue(plain.Contains(quality));
            Assert.IsFalse(plain.Contains(post), "no post-processing on the plain look");
        }

        [Test]
        public void ATier_BringsItsQualityLevelAndUrpAsset_AndDisposePutsThemBack()
        {
            foreach (TierSpec tier in TierSpec.All)
            {
                Present(tier.Tier);
                Assert.AreEqual(tier.Tier, presentation.Context.Quality);
                Assert.AreEqual(tier.Tier, Tiers.Tier);
                Assert.AreEqual(Quality.LevelIndex(tier.Tier), QualitySettings.GetQualityLevel(), tier.Name);
                Assert.AreSame(Quality.PipelineOf(tier.Tier), GraphicsSettings.currentRenderPipeline, tier.Name);
                Assert.AreEqual(tier.Tier, Materials.Tier, "materials follow");
                Assert.IsNull(Tiers.Governor, "tools and tests render what they asked for");
                Assert.IsFalse(Tiers.Governing);

                presentation.Dispose();
                presentation = null;
                Assert.AreEqual(levelBefore, QualitySettings.GetQualityLevel());
                DisposeGame();
            }
        }

        [Test]
        public void WithoutATier_ItIsMedium_AndTheLevelStaysWhereItWas()
        {
            Present();
            Assert.AreEqual(QualityTier.Medium, presentation.Context.Quality);
            Assert.AreEqual(Quality.LevelIndex(QualityTier.Medium), QualitySettings.GetQualityLevel());
            Assert.IsTrue(presentation.Camera.allowHDR);
            Assert.IsTrue(Tiers.Hdr);
        }

        [Test]
        public void TheSettingsMenusChoice_SwitchesTheTier()
        {
            Present();
            var seen = new List<QualityTier>();
            presentation.Context.QualityChanged += seen.Add;

            Settings.Quality = QualitySetting.High;
            Assert.AreEqual(QualityTier.High, presentation.Context.Quality);
            Assert.AreEqual(Quality.LevelIndex(QualityTier.High), QualitySettings.GetQualityLevel());
            Assert.IsTrue(Tiers.Manual);
            Settings.Quality = QualitySetting.Low;
            Assert.AreEqual(QualityTier.Low, presentation.Context.Quality);
            Assert.AreSame(Quality.PipelineOf(QualityTier.Low), GraphicsSettings.currentRenderPipeline);
            CollectionAssert.AreEqual(new[] { QualityTier.High, QualityTier.Low }, seen);

            // A choice made before the game starts is where it starts.
            presentation.Dispose();
            DisposeGame();
            Settings.Quality = QualitySetting.High;
            Present();
            Assert.AreEqual(QualityTier.High, presentation.Context.Quality);
        }

        [Test]
        public void SomebodyElseSettingTheTier_IsFollowed()
        {
            Present();
            presentation.Context.Quality = QualityTier.Low;
            Assert.AreEqual(QualityTier.Low, Tiers.Tier);
            Assert.AreEqual(Quality.LevelIndex(QualityTier.Low), QualitySettings.GetQualityLevel());
        }

        [Test]
        public void TheRenderScale_FollowsTheScreen()
        {
            Present(QualityTier.Medium);
            UniversalRenderPipelineAsset medium = Quality.PipelineOf(QualityTier.Medium);
            Assert.AreEqual(1f, medium.renderScale, "a small picture is not scaled");

            Tiers.SizeOverride = new Vector2Int(2560, 1440);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0.75f, medium.renderScale, 1e-4f);
            Assert.AreEqual(0.75f, Tiers.RenderScale, 1e-4f);
            Assert.AreEqual(new Vector2Int(2560, 1440), Tiers.Size);
            Assert.Less(Tiers.RenderTargetBytes, Quality.RenderTargetBytes(TierSpec.Medium, 2560, 1440));

            Tiers.SizeOverride = new Vector2Int(3840, 2160);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0.5f, medium.renderScale, 1e-4f);

            // High may use more pixels on the same screen.
            Settings.Quality = QualitySetting.High;
            Assert.AreEqual(0.67f, Quality.PipelineOf(QualityTier.High).renderScale, 1e-4f);
        }

        [Test]
        public void InAutomaticMode_TheDeviceDecidesTheStart_AndTheGovernorStepsDown()
        {
            Present();
            Tiers.BeginAutomatic(new DeviceInfo("ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)", 16384));
            Assert.AreEqual(QualityTier.Low, presentation.Context.Quality, "a weak device starts on Low");
            Assert.IsTrue(Tiers.Governing);

            Tiers.BeginAutomatic(new DeviceInfo("NVIDIA GeForce RTX 3060", 16384));
            Assert.AreEqual(QualityTier.Medium, presentation.Context.Quality);
            UniversalRenderPipelineAsset medium = Quality.PipelineOf(QualityTier.Medium);

            // The level was just loaded: five seconds of grace. Then twelve slow frames are a step.
            const float slow = 0.03f;
            Frames(slow, Mathf.CeilToInt(QualityGovernor.LoadGrace / slow));
            Assert.AreEqual(1f, medium.renderScale);
            Frames(slow, 13);
            Assert.AreEqual(0.9f, medium.renderScale, 1e-4f);
            Assert.AreEqual(QualityTier.Medium, presentation.Context.Quality);

            Frames(slow, Mathf.CeilToInt(QualityGovernor.SettleTime / slow) + 13);
            Assert.AreEqual(0.8f, medium.renderScale, 1e-4f);
            Frames(slow, Mathf.CeilToInt(QualityGovernor.SettleTime / slow) + 13);
            Assert.AreEqual(QualityTier.Low, presentation.Context.Quality, "below Medium's floor comes Low");
            Assert.AreEqual(Quality.LevelIndex(QualityTier.Low), QualitySettings.GetQualityLevel());
            Assert.AreEqual(1f, Quality.PipelineOf(QualityTier.Low).renderScale);
            Assert.Greater(Tiers.SlowFrames, 300);
            Assert.AreEqual(Tiers.Frames, Tiers.SlowFrames);
            Assert.AreEqual(slow, Tiers.WorstFrame, 1e-5f);

            // A manual choice switches the governor off...
            Settings.Quality = QualitySetting.Medium;
            Assert.IsFalse(Tiers.Governing);
            Frames(slow, 400);
            Assert.AreEqual(QualityTier.Medium, presentation.Context.Quality);
            Assert.AreEqual(1f, medium.renderScale);
            // ... and Auto brings it back, from the device's tier.
            Settings.Quality = QualitySetting.Auto;
            Assert.IsTrue(Tiers.Governing);
            Assert.AreEqual(QualityTier.Medium, presentation.Context.Quality);
        }

        [Test]
        public void TheGovernor_OnlyJudgesFramesOfPlay()
        {
            Present(withFlow: true);
            Tiers.BeginAutomatic(new DeviceInfo("NVIDIA GeForce RTX 3060", 16384));
            Assert.IsTrue(flow.Pause());
            Frames(0.03f, 600);
            Assert.AreEqual(1f, Quality.PipelineOf(QualityTier.Medium).renderScale, "a menu's frames say nothing about the level");
            Assert.AreEqual(0, Tiers.Frames);
        }

        [Test]
        public void TheCensus_CountsWhatTheLevelPutsInFrontOfTheCamera()
        {
            Present(level: new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                for (int i = 0; i < 5; i++) ctx.AddProp(BasicToys.Block(1f), new Vector3(i * 2f, 0.5f, 5f));
            }));
            Frames(Sim.Dt, 4);
            SceneCensus census = Tiers.Census;
            Assert.GreaterOrEqual(census.Renderers, 6);
            Assert.GreaterOrEqual(census.Draws, 6);
            Assert.GreaterOrEqual(census.ShadowCasterDraws, 5);
            Assert.Greater(census.Triangles, 5 * 12);
            Assert.GreaterOrEqual(census.Materials, 2);
            Assert.IsNull(census.Over(TierSpec.Low), "six draws are within every budget");

            var heavy = new SceneCensus { Draws = 190, ShadowCasterDraws = 80, Triangles = 100, Materials = 44 };
            Assert.AreEqual("draws 190/160, shadow draws 160/140, materials 44/40", heavy.Over(TierSpec.Medium));
            Assert.AreEqual("draws 190/180, shadow draws 240/210, materials 44/40", heavy.Over(TierSpec.High));
        }

        [Test]
        public void ThePostVolume_IsAnInstanceOfTheProfileAsset_AndFollowsTheRoom()
        {
            Present();
            PostLook post = Post;
            Assert.IsNotNull(post.Volume);
            Assert.IsTrue(post.Volume.isGlobal);
            Assert.AreSame(presentation.Context.Root, post.Volume.transform.parent, "under the presentation's root");
            var shared = Resources.Load<VolumeProfile>(PostLook.ProfileResource);
            Assert.AreSame(shared, post.Volume.sharedProfile);
            Assert.AreNotSame(shared, post.Profile, "an instance: the asset is never edited");
            Assert.IsTrue(shared.TryGet(out Bloom sharedBloom));
            Assert.AreNotSame(sharedBloom, post.Bloom);

            // What the camera's volume stack ends up with. (The volume manager comes with the pipeline, which
            // the first render creates - and a change of tier takes down.)
            if (!VolumeManager.instance.isInitialized && presentation.Context.HasGraphics)
                Object.DestroyImmediate(PipelineSetup.Photograph(presentation.Camera, 32, 32));
            VolumeStack stack = VolumeManager.instance.isInitialized ? VolumeManager.instance.CreateStack() : null;
            if (stack != null) try
            {
                VolumeManager.instance.Update(stack, presentation.Camera.transform, 1);
                Assert.AreEqual(0.35f, stack.GetComponent<Bloom>().intensity.value, 1e-5f);
                Assert.AreEqual(1.1f, stack.GetComponent<Bloom>().threshold.value, 1e-5f);
                Assert.AreEqual(PostLook.Tonemap, stack.GetComponent<Tonemapping>().mode.value);
                Assert.AreEqual(0.2f + PostLook.ExposureTrim, stack.GetComponent<ColorAdjustments>().postExposure.value, 1e-5f);
                Assert.AreEqual(8f, stack.GetComponent<ColorAdjustments>().contrast.value, 1e-5f);
                Assert.AreEqual(6f, stack.GetComponent<ColorAdjustments>().saturation.value, 1e-5f);
                Assert.AreEqual(0.18f, stack.GetComponent<Vignette>().intensity.value, 1e-5f);
                Assert.AreEqual(0.45f, stack.GetComponent<Vignette>().smoothness.value, 1e-5f);
            }
            finally
            {
                stack.Dispose();
            }

            // Night: more bloom (6.4).
            Game.LoadLevel(new NightLevel());
            Assert.AreSame(EnvironmentPreset.NightLight, Game.Environment.Preset);
            Assert.AreEqual(EnvironmentPreset.NightLight.Bloom, post.Bloom.intensity.value, 1e-5f);
            Assert.AreEqual(0.55f, post.Bloom.intensity.value, 1e-5f);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(EnvironmentPreset.NightLight.Exposure + PostLook.ExposureTrim, post.Exposure, 1e-5f);

            // The asset is as it was.
            Assert.AreEqual(0.35f, sharedBloom.intensity.value, 1e-5f);
        }

        [Test]
        public void TheCamera_RendersPost_WithTheTiersAntiAliasing()
        {
            Present(QualityTier.Medium);
            Camera camera = presentation.Camera;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            Assert.AreEqual(presentation.Context.HasGraphics, data.renderPostProcessing);
            Assert.IsFalse(data.stopNaN);
            Assert.IsFalse(data.dithering, "outside Play Mode a picture must be reproducible; the game dithers");
            Assert.AreEqual(AntialiasingMode.None, data.antialiasing);
            Assert.IsTrue(camera.allowMSAA);
            Assert.IsTrue(Post.Bloom.active);
            Assert.IsFalse(Post.Bloom.highQualityFiltering.value);
            Assert.AreEqual(4, Post.Bloom.maxIterations.value);

            Settings.Quality = QualitySetting.Low;
            Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, data.antialiasing, "Low: FXAA instead of MSAA");
            Assert.IsFalse(camera.allowMSAA);
            Assert.IsFalse(Post.Bloom.active, "Low: no bloom");

            Settings.Quality = QualitySetting.High;
            Assert.AreEqual(AntialiasingMode.None, data.antialiasing);
            Assert.IsTrue(camera.allowMSAA);
            Assert.IsTrue(Post.Bloom.active);
            Assert.IsTrue(Post.Bloom.highQualityFiltering.value, "High: high-quality bloom filtering");
            Assert.AreEqual(5, Post.Bloom.maxIterations.value);
        }

        [Test]
        public void TheLensBlur_FollowsTheTierAndTheStrengthSetting()
        {
            Present(QualityTier.Medium);
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(Post.MacroBandActive);
            Assert.IsTrue(BlurFeature.isActive);
            Assert.AreEqual(3.5f * Settings.DefaultLensBlur, Post.RadiusAt1080, 1e-4f, "3.5 px at the default strength of 60%");
            Assert.AreEqual(8, Post.Taps);
            Assert.AreEqual(0f, Post.FullBlur);

            Settings.LensBlur = 1f;
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(3.5f, Post.RadiusAt1080, 1e-4f);

            Settings.Quality = QualitySetting.High;
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(5f, Post.RadiusAt1080, 1e-4f);
            Assert.AreEqual(12, Post.Taps);

            // No strength, no pass (and no copy of the colour buffer).
            Settings.LensBlur = 0f;
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsFalse(Post.MacroBandActive);
            Assert.IsFalse(BlurFeature.isActive);
            Settings.LensBlur = 0.6f;
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(BlurFeature.isActive);

            // Low: the feature is switched off.
            Settings.Quality = QualitySetting.Low;
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsFalse(Post.MacroBandActive);
            Assert.IsFalse(BlurFeature.isActive);
            Assert.AreEqual(0f, Post.RadiusAt1080);
        }

        [Test]
        public void Pausing_BlursTheWholePicture_In180ms()
        {
            Present(QualityTier.Medium, withFlow: true);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0f, Post.FullBlur);
            Assert.IsTrue(Post.FullBlurReached);

            Assert.IsTrue(flow.Pause());
            presentation.Frame(0.09f, 1f);
            Assert.AreEqual(0.5f, Post.FullBlur, 1e-3f);
            Assert.IsFalse(Post.FullBlurReached);
            Assert.AreEqual(PostLook.MaxTaps, Post.Taps);
            presentation.Frame(0.09f, 1f);
            presentation.Frame(0.01f, 1f);
            Assert.AreEqual(1f, Post.FullBlur);
            Assert.AreEqual(PostLook.PauseBlurPx, Post.RadiusAt1080, 1e-4f, "14 px, whatever the strength setting");
            Assert.IsTrue(Post.FullBlurReached, "the pause card may capture the frame now");

            Assert.IsTrue(flow.Resume());
            Frames(0.05f, 5);
            Assert.AreEqual(0f, Post.FullBlur);
            Assert.AreEqual(8, Post.Taps);

            // A menu can ask for the blur itself.
            Post.FullBlurTarget = 1f;
            Frames(0.05f, 5);
            Assert.AreEqual(1f, Post.FullBlur);
            Post.FullBlurTarget = null;
            Frames(0.05f, 5);
            Assert.AreEqual(0f, Post.FullBlur);

            // Low skips it.
            Settings.Quality = QualitySetting.Low;
            flow.Pause();
            Frames(0.05f, 5);
            Assert.AreEqual(0f, Post.FullBlur);
            Assert.IsTrue(Post.FullBlurReached);
        }

        Prop BuildWithBlock()
        {
            Prop block = null;
            Present(level: new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }));
            return block;
        }

        [Test]
        public void LettingGoOfAToy_IsAShutterFlash_Of90ms()
        {
            Prop block = BuildWithBlock();
            presentation.Frame(Sim.Dt, 1f);
            float normal = Post.Exposure;
            Assert.AreEqual(0.2f + PostLook.ExposureTrim, normal, 1e-5f, "the room's +0.2 EV and the trim that goes with tonemapping None");

            // Really grab it and let it go again.
            LookAt(block.Center);
            Click();
            Assert.IsTrue(block.Held);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-5f, "nothing on the grab");
            Click();
            Assert.IsFalse(block.Held);

            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal + PostLook.ReleaseFlashEv, Post.Exposure, 1e-5f, "the frame of the release: +0.12 EV");
            Assert.AreEqual(Post.Exposure, Post.ColorAdjustments.postExposure.value, 1e-6f);
            presentation.Frame(0.045f, 1f);
            Assert.AreEqual(normal + PostLook.ReleaseFlashEv * 0.5f, Post.Exposure, 1e-4f, "half way down after 45 ms");
            presentation.Frame(0.045f, 1f);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f, "gone after 90 ms");
        }

        [Test]
        public void CompletingALevel_IsAWhiteFlash_Of300ms()
        {
            Present();
            presentation.Frame(Sim.Dt, 1f);
            float normal = Post.Exposure;
            Game.CompleteLevel();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal + PostLook.CompleteFlashEv, Post.Exposure, 1e-5f, "+1.5 EV for one frame");
            presentation.Frame(0.15f, 1f);
            Assert.AreEqual(normal + PostLook.CompleteFlashEv * 0.5f, Post.Exposure, 1e-4f);
            presentation.Frame(0.2f, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f);

            // A flash does not survive into the next level.
            Post.Flash(1f, 1f);
            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f);

            // Whoever shows the flash itself can switch this one off.
            Post.CompleteFlash = false;
            Game.CompleteLevel();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f);
        }

        [Test]
        public void ReduceMotion_TurnsTheFlashesOff()
        {
            Prop block = BuildWithBlock();
            Settings.ReduceMotion = true;
            presentation.Frame(Sim.Dt, 1f);
            float normal = Post.Exposure;
            LookAt(block.Center);
            Click();
            Click();
            Assert.IsFalse(block.Held);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f);
            Game.CompleteLevel();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(normal, Post.Exposure, 1e-6f);
        }

        [Test]
        public void Dispose_LeavesNothingBehind()
        {
            int volumes = VolumeManager.instance.GetVolumes(~0).Length;
            int profiles = Resources.FindObjectsOfTypeAll<VolumeProfile>().Length;
            int components = Resources.FindObjectsOfTypeAll<VolumeComponent>().Length;

            Present(QualityTier.Low);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(volumes + 1, VolumeManager.instance.GetVolumes(~0).Length);
            Assert.IsFalse(BlurFeature.isActive);
            Camera camera = presentation.Camera;

            presentation.Dispose();
            presentation = null;
            Assert.AreEqual(volumes, VolumeManager.instance.GetVolumes(~0).Length, "the volume is gone");
            Assert.AreEqual(profiles, Resources.FindObjectsOfTypeAll<VolumeProfile>().Length, "its profile instance is destroyed");
            Assert.AreEqual(components, Resources.FindObjectsOfTypeAll<VolumeComponent>().Length, "and the profile's components");
            Assert.IsTrue(BlurFeature.isActive, "the shared renderer asset is as the setup step wrote it");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_RadiusPx"));
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Full"));
        }

        static readonly Type[] CountedTypes =
        {
            typeof(GameObject), typeof(Material), typeof(Mesh), typeof(Camera), typeof(Light), typeof(Texture), typeof(MonoBehaviour),
            typeof(VolumeProfile), typeof(VolumeComponent),
        };

        static string CountObjects()
        {
            var parts = new List<string>();
            foreach (Type type in CountedTypes) parts.Add(type.Name + " " + Resources.FindObjectsOfTypeAll(type).Length);
            return string.Join(", ", parts);
        }

        [Test]
        public void ASessionThroughLevelsAndTiers_LeavesNoObjectsBehind()
        {
            var levels = new LevelList(new[] { 0, 1, 2 }, id => id == 2
                ? (LevelDefinition)new NightLevel()
                : new AdHocLevel(ctx =>
                {
                    TestHelpers.Floor(ctx);
                    ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f));
                }));
            var host = new GameObject("Pipeline Test Runner") { hideFlags = HideFlags.DontSave };
            GameRunner runner = host.AddComponent<GameRunner>();
            try
            {
                runner.Begin(LaunchOptions.FromUrl("?plain=1"), new RunnerOptions
                {
                    Devices = new FakeDevices(), Levels = levels, Present = true, SkipTitle = true, Store = new MemoryStore(),
                    Presenters = PipelineSetup.PlainLitPresenters(),
                });
                Assert.IsNotNull(runner.Presentation.Get<PostLook>());
                Assert.IsNotNull(runner.Presentation.Get<QualityPresenter>());

                void Round()
                {
                    foreach (int id in levels.Ids)
                    {
                        runner.LoadLevel(id);
                        for (int i = 0; i < 5; i++) runner.Frame(Sim.Dt);
                        foreach (QualitySetting choice in new[] { QualitySetting.Low, QualitySetting.High, QualitySetting.Medium })
                        {
                            Settings.Quality = choice;
                            runner.Frame(Sim.Dt);
                        }
                        runner.Game.CompleteLevel();
                        runner.Frame(Sim.Dt);
                    }
                    runner.LoadLevel(0);
                    runner.Frame(Sim.Dt);
                }

                // Once for everything that is made once (materials, meshes, the pipeline's own resources).
                Round();
                string before = CountObjects();
                for (int i = 0; i < 3; i++) Round();
                Assert.AreEqual(before, CountObjects());
            }
            finally
            {
                runner.Shutdown();
                Object.DestroyImmediate(host);
            }
        }

        // The foundation's fuzzed session (PlatformTests) with these presenters and no others: every key at
        // random, pauses, restarts, level changes, frame times from nothing to half a minute.
        [Test]
        public void AFuzzedSession_LeavesNoObjectsBehind_AndTheProjectAsItWas()
        {
            var levels = new LevelList(new[] { 0, 1, 2 }, id => id == 2
                ? (LevelDefinition)new NightLevel()
                : new AdHocLevel(ctx =>
                {
                    TestHelpers.Floor(ctx);
                    ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f));
                    ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
                    ctx.SetSpawn(Vector3.zero, 0f);
                }));
            var host = new GameObject("Pipeline Fuzz Runner") { hideFlags = HideFlags.DontSave };
            GameRunner runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            bool ignoring = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                runner.Begin(LaunchOptions.FromUrl("?plain=1"), new RunnerOptions
                {
                    Devices = devices, Levels = levels, Present = true, SkipTitle = true, Store = new MemoryStore(),
                    Presenters = PipelineSetup.PlainLitPresenters(),
                });
                PostLook post = runner.Presentation.Get<PostLook>();
                QualityPresenter tiers = runner.Presentation.Get<QualityPresenter>();
                tiers.BeginAutomatic(new DeviceInfo("NVIDIA GeForce RTX 3060", 16384));
                foreach (int id in levels.Ids)
                {
                    runner.LoadLevel(id);
                    runner.Frame(Sim.Dt);
                }
                runner.LoadLevel(0);
                runner.Frame(Sim.Dt);
                string before = CountObjects();

                var random = new System.Random(20261003);
                float[] frameTimes = { 0f, 0.001f, 1f / 240f, 1f / 144f, 1f / 60f, 1f / 30f, 0.07f, 0.5f, 30f, float.NaN, -3f };
                int pauses = 0;
                for (int frame = 0; frame < 6000; frame++)
                {
                    devices.State.Focused = random.Next(40) != 0;
                    devices.State.MoveX = random.Next(3) - 1;
                    devices.State.MoveZ = random.Next(3) - 1;
                    devices.State.JumpPressed = random.Next(6) == 0;
                    devices.State.ClickPressed = random.Next(5) == 0;
                    // (Level changes are rare enough for the governor's five seconds of grace to run out in between.)
                    devices.State.RestartPressed = random.Next(900) == 0;
                    devices.State.EscapePressed = random.Next(60) == 0;
                    devices.State.NextLevelPressed = random.Next(1200) == 0;
                    devices.State.PreviousLevelPressed = random.Next(1500) == 0;
                    devices.State.Look = new Vector2(random.Next(-400, 401), random.Next(-200, 201));
                    runner.Frame(frameTimes[random.Next(frameTimes.Length)]);

                    if (runner.Flow.State == FlowState.Paused) pauses++;
                    Assert.IsFalse(float.IsNaN(post.Exposure), "frame " + frame);
                    Assert.IsFalse(float.IsNaN(post.RadiusAt1080), "frame " + frame);
                    Assert.That(post.FullBlur, Is.InRange(0f, 1f), "frame " + frame);
                    Assert.That(tiers.RenderScale, Is.InRange(0.3f, 1f), "frame " + frame);
                    Assert.AreEqual(runner.Presentation.Context.Quality, tiers.Tier, "frame " + frame);
                    Assert.AreEqual(Quality.LevelIndex(tiers.Tier), QualitySettings.GetQualityLevel(), "frame " + frame);
                }
                Assert.Greater(pauses, 20, "the session was paused now and then");
                Assert.Greater(tiers.Governor.StepsDown, 0, "and slow enough for the governor to act");

                if (runner.Flow.State != FlowState.Playing) runner.Flow.Resume();
                Settings.Quality = QualitySetting.Medium;
                runner.LoadLevel(0);
                runner.Frame(Sim.Dt);
                Assert.AreEqual(before, CountObjects());
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = ignoring;
                runner.Shutdown();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AFrame_AllocatesNothing()
        {
            Prop block = BuildWithBlock();
            QualityPresenter tiers = Tiers;
            PostLook post = Post;
            tiers.BeginAutomatic(new DeviceInfo("NVIDIA GeForce RTX 3060", 16384));
            // Everything that happens once (the census, a flash, the first step of the governor) first.
            LookAt(block.Center);
            Click();
            Click();
            for (int i = 0; i < 600; i++)
            {
                tiers.Frame(0.03f, 1f);
                post.Frame(0.03f, 1f);
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    tiers.Frame(Sim.Dt, 1f);
                    post.Frame(Sim.Dt, 1f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void TheWarmUp_CompilesTheCollectionOnce()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "needs a graphics device");
            ShaderWarmup.Reset();
            Assert.IsFalse(ShaderWarmup.Done);
            Assert.IsTrue(ShaderWarmup.Run());
            Assert.IsTrue(ShaderWarmup.Done);
            Assert.Greater(ShaderWarmup.Variants, 0);
            Assert.LessOrEqual(ShaderWarmup.Variants, TierSpec.MaxShaderPrograms);
            Assert.Greater(ShaderWarmup.Shaders, 0);
            Assert.IsTrue(Resources.Load<ShaderVariantCollection>(ShaderWarmup.Resource).isWarmedUp);
            Debug.Log("[Toybox] warm-up: " + ShaderWarmup.Variants + " variants of " + ShaderWarmup.Shaders + " shaders in " + ShaderWarmup.Milliseconds.ToString("0.0") + " ms");
            Assert.IsFalse(ShaderWarmup.Run(), "once per session");
        }
    }

    /// <summary>What ends up in the picture: the lens blur's shader on its own, and the tiers side by side.</summary>
    public sealed class PipelinePictureTests : SimTest
    {
        const int Width = 640, Height = 360;

        [SetUp]
        public void NeedsGraphics()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");
            Settings.Use(new MemoryStore());
        }

        [TearDown]
        public void DefaultSettings() => Settings.Use(null);

        // ---- The shader, drawn the way the full-screen feature draws it ---------------------------------

        // Black and white stripes two pixels wide: as sharp as a picture gets.
        static float[] DrawStripes(float radiusPx, float taps, float full, int size = 128)
        {
            var source = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    colors[y * size + x] = (x / 2) % 2 == 0 ? Color.white : Color.black;
            source.SetPixels(colors);
            source.Apply(false);

            var material = new Material(Shader.Find(PipelineSetup.MacroBandShader));
            var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var result = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            var block = new MaterialPropertyBlock();
            var commands = new CommandBuffer { name = "MacroBand test" };
            RenderTexture active = RenderTexture.active;
            try
            {
                Shader.SetGlobalFloat("_RadiusPx", radiusPx);
                Shader.SetGlobalFloat("_Taps", taps);
                Shader.SetGlobalFloat("_Full", full);
                block.SetTexture("_BlitTexture", source);
                block.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
                commands.SetRenderTarget(target);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, block);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                result.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                result.Apply(false);

                // Per row: the contrast between the stripes (index 0..size-1) and the mean (size..2 size-1).
                Color[] pixels = result.GetPixels();
                var rows = new float[size * 2];
                for (int y = 0; y < size; y++)
                {
                    float min = 1f, max = 0f, sum = 0f;
                    for (int x = 16; x < size - 16; x++)
                    {
                        float value = pixels[y * size + x].r;
                        min = Mathf.Min(min, value);
                        max = Mathf.Max(max, value);
                        sum += value;
                    }
                    rows[y] = max - min;
                    rows[size + y] = sum / (size - 32);
                }
                return rows;
            }
            finally
            {
                Shader.SetGlobalFloat("_RadiusPx", 0f);
                Shader.SetGlobalFloat("_Taps", 0f);
                Shader.SetGlobalFloat("_Full", 0f);
                RenderTexture.active = active;
                commands.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TheShader_BlursTheTopAndBottom_AndLeavesTheMiddleAlone()
        {
            const int size = 128;
            float[] rows = DrawStripes(4f, 8f, 0f, size);
            // The sharp band is the picture itself, bit for bit: |y - 0.5| < 0.24.
            for (int y = 36; y < 92; y++) Assert.AreEqual(1f, rows[y], 1e-6f, "row " + y + " is in the sharp band");
            // Toward the edges the stripes melt into each other, more and more.
            Assert.Less(rows[2], 0.35f, "the bottom edge");
            Assert.Less(rows[size - 3], 0.35f, "the top edge");
            Assert.Less(rows[size - 3], rows[108], "softer the further out");
            Assert.Less(rows[108], rows[98]);
            Assert.AreEqual(rows[2], rows[size - 3], 0.02f, "the same above and below");
            // Blurring neither brightens nor darkens.
            for (int y = 0; y < size; y++) Assert.AreEqual(0.5f, rows[size + y], 0.06f, "mean of row " + y);

            // Nothing set (no game running): the pass is a copy.
            float[] idle = DrawStripes(0f, 0f, 0f, size);
            for (int y = 0; y < size; y++) Assert.AreEqual(1f, idle[y], 1e-6f, "row " + y);
        }

        [Test]
        public void TheShader_BlursEverything_WhenFull()
        {
            const int size = 128;
            float[] rows = DrawStripes(6f, 16f, 1f, size);
            for (int y = 0; y < size; y++) Assert.Less(rows[y], 0.3f, "row " + y);
            float[] half = DrawStripes(6f, 16f, 0f, size);
            Assert.AreEqual(1f, half[size / 2], 1e-6f);
            // More taps are a smoother blur; either way the stripes are gone at the edge.
            Assert.Less(DrawStripes(6f, 12f, 0f, size)[2], 0.3f);
        }

        // ---- The tiers ---------------------------------------------------------------------------------

        Color32[] Picture(QualityTier tier, bool post = true, float lensBlur = 1f)
        {
            bool plain = Materials.Plain;
            bool async = ShaderUtil.allowAsyncCompilation;
            Presentation presentation = null;
            try
            {
                Materials.Plain = true;
                ShaderUtil.allowAsyncCompilation = false;
                Settings.LensBlur = lensBlur;
                Game = Game.Create(new GameOptions());
                Game.LoadLevel(new PostCheckLevel());
                List<PresenterRegistry.Entry> presenters = PipelineSetup.PlainLitPresenters();
                if (!post) presenters.RemoveAt(presenters.Count - 1);
                presentation = Presentation.Create(Game, new PresentationOptions { Plain = true, Presenters = presenters, Quality = tier });
                presentation.Frame(0f, 1f);
                int msaa = TierSpec.Of(tier).Msaa;
                // The first picture after a pipeline switch is not to be trusted.
                Object.DestroyImmediate(PipelineSetup.Photograph(presentation.Camera, Width, Height, msaa));
                Texture2D image = PipelineSetup.Photograph(presentation.Camera, Width, Height, msaa);
                Color32[] pixels = image.GetPixels32();
                Object.DestroyImmediate(image);
                return pixels;
            }
            finally
            {
                presentation?.Dispose();
                DisposeGame();
                Materials.Plain = plain;
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        static float Luma(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        // Mean brightness of a region given in fractions of the picture (y up).
        static float Mean(Color32[] pixels, float x0, float y0, float x1, float y1)
        {
            int left = Mathf.FloorToInt(x0 * Width), right = Mathf.Max(left + 1, Mathf.FloorToInt(x1 * Width));
            int bottom = Mathf.FloorToInt(y0 * Height), top = Mathf.Max(bottom + 1, Mathf.FloorToInt(y1 * Height));
            float sum = 0f;
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                    sum += Luma(pixels[y * Width + x]);
            return sum / ((right - left) * (top - bottom));
        }

        // RMS step in brightness between neighbours in a band of rows.
        static float Sharpness(Color32[] pixels, float y0, float y1)
        {
            int bottom = Mathf.FloorToInt(y0 * Height), top = Mathf.Min(Height - 1, Mathf.FloorToInt(y1 * Height));
            double sum = 0.0;
            int count = 0;
            for (int y = bottom; y < top; y++)
                for (int x = 0; x < Width - 1; x++)
                {
                    float here = Luma(pixels[y * Width + x]);
                    float dx = Luma(pixels[y * Width + x + 1]) - here, dy = Luma(pixels[(y + 1) * Width + x]) - here;
                    sum += dx * dx + dy * dy;
                    count++;
                }
            return Mathf.Sqrt((float)(sum / count));
        }

        // The dark posts right beside the lamp, where its glow would fall: a strip left and right of it.
        static float BesideTheLamp(Color32[] pixels)
        {
            // The lamp is 1.2 wide at 9 units: 0.073 of the picture's height to either side of the middle.
            float half = PostCheckLevel.LampSize * 0.5f / PostCheckLevel.LampPosition.z / (2f * Mathf.Tan(35f * Mathf.Deg2Rad));
            float aspect = (float)Height / Width;
            float inner = (half + 0.012f) * aspect, outer = (half + 0.05f) * aspect;
            return 0.5f * (Mean(pixels, 0.5f - outer, 0.52f, 0.5f - inner, 0.6f) + Mean(pixels, 0.5f + inner, 0.52f, 0.5f + outer, 0.6f));
        }

        [Test]
        public void MediumAndHigh_BloomAndSoftenTheEdges_LowIsCrisp_AndAllThreeAreVignetted()
        {
            Color32[] bare = Picture(QualityTier.Medium, post: false);
            Color32[] low = Picture(QualityTier.Low);
            Color32[] medium = Picture(QualityTier.Medium);
            Color32[] high = Picture(QualityTier.High);

            // The vignette (7.2, in the uber pass of every tier): the sky in a top corner against the sky
            // at the left edge half way up. Without post-processing both are the one background colour.
            float Corner(Color32[] p) => 0.5f * (Mean(p, 0f, 0.94f, 0.05f, 1f) + Mean(p, 0.95f, 0.94f, 1f, 1f));
            float Edge(Color32[] p) => Mean(p, 0f, 0.56f, 0.05f, 0.64f);
            Assert.AreEqual(1f, Corner(bare) / Edge(bare), 0.005f, "no post-processing, no vignette");
            foreach (Color32[] picture in new[] { low, medium, high })
            {
                float ratio = Corner(picture) / Edge(picture);
                Assert.Less(ratio, 0.97f, "the corners are darker");
                Assert.Greater(ratio, 0.85f, "but only a little: intensity 0.18");
            }

            // The lens blur (3.6, 7.3): the top and bottom bands lose sharpness on Medium and High, the
            // middle band does not; Low has none.
            float lowMiddle = Sharpness(low, 0.3f, 0.7f), mediumMiddle = Sharpness(medium, 0.3f, 0.7f), highMiddle = Sharpness(high, 0.3f, 0.7f);
            float lowEdges = Sharpness(low, 0f, 0.1f) + Sharpness(low, 0.9f, 1f);
            float mediumEdges = Sharpness(medium, 0f, 0.1f) + Sharpness(medium, 0.9f, 1f);
            float highEdges = Sharpness(high, 0f, 0.1f) + Sharpness(high, 0.9f, 1f);
            // (Against the middle band of the same picture: the tiers also differ in anti-aliasing.)
            Assert.Less(mediumEdges / mediumMiddle, lowEdges / lowMiddle * 0.85f, "Medium: softer at the top and bottom than Low");
            Assert.Less(highEdges, mediumEdges, "High: 5 px against 3.5 px");
            Assert.Greater(mediumMiddle, lowMiddle * 0.85f, "the middle band is not blurred on Medium");
            Assert.Greater(highMiddle, lowMiddle * 0.85f, "nor on High");

            // Bloom (7.2): the lamp glows onto the dark posts beside it on Medium and High only.
            float bareGlow = BesideTheLamp(bare), lowGlow = BesideTheLamp(low), mediumGlow = BesideTheLamp(medium), highGlow = BesideTheLamp(high);
            Assert.Greater(mediumGlow, lowGlow + 0.02f, "Medium: bloom around the lamp");
            Assert.Greater(highGlow, lowGlow + 0.02f, "High: bloom around the lamp");
            Assert.Less(Mathf.Abs(lowGlow - bareGlow), 0.06f, "Low: none (only the grade differs from the bare picture)");

            // The lamp itself clips to white on every tier (no tone curve holds it back).
            foreach (Color32[] picture in new[] { low, medium, high })
                Assert.Greater(Mean(picture, 0.49f, 0.49f, 0.51f, 0.51f), 0.97f);
        }

        [Test]
        public void TheLensBlursStrength_IsThePlayers()
        {
            Color32[] none = Picture(QualityTier.Medium, lensBlur: 0f);
            Color32[] full = Picture(QualityTier.Medium, lensBlur: 1f);
            float sharp = Sharpness(none, 0.9f, 1f), soft = Sharpness(full, 0.9f, 1f);
            Assert.Less(soft, sharp * 0.85f);
            Assert.AreEqual(Sharpness(none, 0.3f, 0.7f), Sharpness(full, 0.3f, 0.7f), 1e-4f, "the middle band never changes");
        }

        [Test]
        public void TheSamePictureTwice_IsTheSamePicture()
        {
            Color32[] first = Picture(QualityTier.Medium);
            Color32[] second = Picture(QualityTier.Medium);
            int different = 0;
            for (int i = 0; i < first.Length; i++)
                if (first[i].r != second[i].r || first[i].g != second[i].g || first[i].b != second[i].b) different++;
            Assert.AreEqual(0, different, "outside Play Mode nothing is random (no dither noise): tools can compare pictures");
        }
    }
}
