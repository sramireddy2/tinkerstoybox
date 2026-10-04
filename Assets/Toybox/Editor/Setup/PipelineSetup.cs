using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DefaultFormat = UnityEngine.Experimental.Rendering.DefaultFormat;
using GraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat;
using GraphicsFormatUsage = UnityEngine.Experimental.Rendering.GraphicsFormatUsage;
using Object = UnityEngine.Object;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The setup steps of the render pipeline (orders 100-199; ART_BIBLE 3.8, 5.3, 7.3, 7.5): the
    /// MacroBand template material, the shared renderer asset and its lens-blur feature, the three URP
    /// tier assets and their Quality levels, the post-processing volume profile, the URP global settings
    /// and the shader variant collection.
    ///
    /// They run first among the render areas. The steps of the other areas add their own renderer feature
    /// and layer masks to the same ToyboxRenderer.asset afterwards, so that asset is only ever updated in
    /// place - nothing here deletes or recreates it, and nothing here touches the layer masks.
    ///
    /// All numbers come from <see cref="TierSpec"/>, the same table the game reads at runtime.
    /// </summary>
    public static class PipelineSetup
    {
        public const string MacroBandShader = "Toybox/MacroBand";
        public const string MacroBandPath = SetupUtil.MaterialsDir + "/MacroBand.mat";
        public const string VolumeProfilePath = SetupUtil.ResourcesDir + "/" + PostLook.ProfileResource + ".asset";
        public const string VariantsPath = SetupUtil.ResourcesDir + "/" + ShaderWarmup.Resource + ".shadervariants";
        const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
        const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        /// <summary>The five hand-written shaders of ART_BIBLE 3.1.</summary>
        public static readonly string[] GameShaders = { "Toybox/ToyLit", "Toybox/RoomLit", "Toybox/Sticker", "Toybox/Flat", MacroBandShader };

        // The shadow keyword sets URP 17.6 really switches between with these assets (verified in
        // MainLightShadowCasterPass / ShadowUtils): cascades with soft shadows while something casts a
        // shadow, and the single-map keyword with an empty shadow map while nothing does (the renderer
        // strips the "shadows off" variant, soft or not depending on the frame before).
        const string ShadowsMain = "_MAIN_LIGHT_SHADOWS", ShadowsCascade = "_MAIN_LIGHT_SHADOWS_CASCADE", ShadowsSoft = "_SHADOWS_SOFT";
        static readonly string[][] LitKeywordSets =
        {
            new[] { ShadowsCascade, ShadowsSoft },
            new[] { ShadowsMain },
            new[] { ShadowsMain, ShadowsSoft },
        };

        /// <summary>Where the URP asset of a tier lives: Settings/ToyboxURP_Low.asset, _Medium, _High.</summary>
        public static string TierAssetPath(QualityTier tier) => SetupUtil.SettingsDir + "/ToyboxURP_" + TierSpec.Of(tier).Name + ".asset";

        // ---- 100: the template material of the lens blur ------------------------------------------------
        // The renderer feature references it, and it sits under Resources: either keeps the shader in the build.
        [SetupStep(100)]
        public static void CreateMacroBandMaterial()
        {
            SetupUtil.LoadOrCreateMaterial(MacroBandPath, MacroBandShader);
        }

        // ---- 110: the renderer the three tiers share ----------------------------------------------------
        [SetupStep(110)]
        public static void ConfigureRenderer()
        {
            UniversalRendererData renderer = LoadRenderer();
            bool changed = false;
            changed |= SetupUtil.SetField(renderer, "m_RenderingMode", (int)RenderingMode.Forward);
            changed |= SetupUtil.SetField(renderer, "m_DepthPrimingMode", (int)DepthPrimingMode.Disabled);
            // The lens blur and the sticker pass both work on the intermediate colour target.
            changed |= SetupUtil.SetField(renderer, "m_IntermediateTextureMode", (int)IntermediateTextureMode.Always);
            // The sticker pass writes and tests stencil (ART_BIBLE 8).
            changed |= SetupUtil.SetField(renderer, "m_DepthAttachmentFormat", (int)DepthFormat.Depth_24_Stencil_8);
            if (renderer.postProcessData == null)
            {
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
                if (renderer.postProcessData == null) throw new InvalidOperationException("URP's PostProcessData.asset was not found in the package.");
                EditorUtility.SetDirty(renderer);
                changed = true;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MacroBandPath);
            if (material == null) throw new InvalidOperationException(MacroBandPath + " does not exist (the step that creates it failed).");
            FullScreenPassRendererFeature blur = SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, PostLook.MacroBandFeature);
            bool blurChanged = false;
            if (blur.passMaterial != material) { blur.passMaterial = material; blurChanged = true; }
            if (blur.passIndex != 0) { blur.passIndex = 0; blurChanged = true; }
            if (blur.injectionPoint != FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing)
            {
                blur.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
                blurChanged = true;
            }
            if (!blur.fetchColorBuffer) { blur.fetchColorBuffer = true; blurChanged = true; }
            if (blur.requirements != ScriptableRenderPassInput.None) { blur.requirements = ScriptableRenderPassInput.None; blurChanged = true; }
            if (blur.bindDepthStencilAttachment) { blur.bindDepthStencilAttachment = false; blurChanged = true; }
            // PostLook switches it off on Low while a game runs; on disk it is on.
            if (!blur.isActive) { blur.SetActive(true); blurChanged = true; }
            if (blurChanged) EditorUtility.SetDirty(blur);

            changed |= blurChanged;
            changed |= SetupUtil.OrderRendererFeatures(renderer, PostLook.MacroBandFeature, "Sticker");
            if (changed) renderer.SetDirty();
        }

        // ---- 120: one URP asset per tier ----------------------------------------------------------------
        [SetupStep(120)]
        public static void CreateTierAssets()
        {
            UniversalRendererData renderer = LoadRenderer();
            foreach (TierSpec tier in TierSpec.All)
            {
                UniversalRenderPipelineAsset asset = SetupUtil.LoadOrCreate(TierAssetPath(tier.Tier), () => UniversalRenderPipelineAsset.Create(renderer));
                ConfigureTierAsset(asset, tier, renderer);
            }
        }

        static void ConfigureTierAsset(UniversalRenderPipelineAsset asset, TierSpec tier, UniversalRendererData renderer)
        {
            // The one shared renderer, and nothing else in the list.
            var serialized = new SerializedObject(asset);
            SerializedProperty renderers = SetupUtil.Field(serialized, "m_RendererDataList");
            if (renderers.arraySize != 1 || renderers.GetArrayElementAtIndex(0).objectReferenceValue != renderer)
            {
                renderers.arraySize = 1;
                renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            SetupUtil.SetField(asset, "m_DefaultRendererIndex", 0);

            // 7.3. No depth texture and no opaque texture: no depth prepass, no copies.
            SetupUtil.SetField(asset, "m_RequireDepthTexture", false);
            SetupUtil.SetField(asset, "m_RequireOpaqueTexture", false);
            SetupUtil.SetField(asset, "m_SupportsTerrainHoles", false);
            SetupUtil.SetField(asset, "m_SupportsHDR", true);
            SetupUtil.SetField(asset, "m_HDRColorBufferPrecision", (int)HDRColorBufferPrecision._32Bits);
            SetupUtil.SetField(asset, "m_MSAA", tier.Msaa);
            // The authored scale. At runtime QualityPresenter sets it from the pixel budget and puts it back.
            SetupUtil.SetField(asset, "m_RenderScale", 1f);
            SetupUtil.SetField(asset, "m_UpscalingFilter", 1);      // UpscalingFilterSelection.Linear
            SetupUtil.SetField(asset, "m_EnableLODCrossFade", false);

            SetupUtil.SetField(asset, "m_MainLightRenderingMode", (int)LightRenderingMode.PerPixel);
            SetupUtil.SetField(asset, "m_MainLightShadowsSupported", true);
            SetupUtil.SetField(asset, "m_MainLightShadowmapResolution", tier.ShadowResolution);
            SetupUtil.SetField(asset, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.Disabled);
            SetupUtil.SetField(asset, "m_AdditionalLightShadowsSupported", false);
            SetupUtil.SetField(asset, "m_ReflectionProbeBlending", false);
            SetupUtil.SetField(asset, "m_ReflectionProbeBoxProjection", false);
            SetupUtil.SetField(asset, "m_ReflectionProbeAtlas", false);

            // 5.3. Cascades always (never one), soft shadows always: the keywords the shaders are built with.
            SetupUtil.SetField(asset, "m_ShadowDistance", tier.ShadowDistance);
            SetupUtil.SetField(asset, "m_ShadowCascadeCount", tier.Cascades);
            SetupUtil.SetField(asset, "m_Cascade2Split", tier.Cascade2Split);
            SetupUtil.SetField(asset, "m_Cascade3Split", tier.Cascade3Split);
            SetupUtil.SetField(asset, "m_CascadeBorder", 0.1f);
            SetupUtil.SetField(asset, "m_ShadowDepthBias", 1f);
            SetupUtil.SetField(asset, "m_ShadowNormalBias", 1f);
            SetupUtil.SetField(asset, "m_AnyShadowsSupported", true);
            SetupUtil.SetField(asset, "m_SoftShadowsSupported", true);
            SetupUtil.SetField(asset, "m_ConservativeEnclosingSphere", true);
            SetupUtil.SetField(asset, "m_SoftShadowQuality", tier.SoftShadowQuality);

            // (7.3 also asks for dynamic batching; URP 17.6 has retired it. The field is still serialized,
            // and the windowed editor logs an error on every pipeline start while it is on - so: off.)
            SetupUtil.SetField(asset, "m_SupportsDynamicBatching", false);
            SetupUtil.SetField(asset, "m_UseSRPBatcher", true);
            SetupUtil.SetField(asset, "m_MixedLightingSupported", false);
            SetupUtil.SetField(asset, "m_SupportsLightCookies", false);
            SetupUtil.SetField(asset, "m_SupportsLightLayers", false);
            SetupUtil.SetField(asset, "m_UseAdaptivePerformance", false);
            SetupUtil.SetField(asset, "m_ColorGradingMode", (int)ColorGradingMode.LowDynamicRange);
            SetupUtil.SetField(asset, "m_ColorGradingLutSize", 32);
            SetupUtil.SetField(asset, "m_SupportDataDrivenLensFlare", false);
            SetupUtil.SetField(asset, "m_SupportScreenSpaceLensFlare", false);
        }

        // ---- 130: three Quality levels, all of them in the WebGL build, Medium the default ---------------
        [SetupStep(130)]
        public static void ConfigureQualityLevels()
        {
            var assets = new UniversalRenderPipelineAsset[TierSpec.All.Count];
            for (int i = 0; i < assets.Length; i++)
            {
                assets[i] = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(TierAssetPath(TierSpec.All[i].Tier));
                if (assets[i] == null) throw new InvalidOperationException(TierAssetPath(TierSpec.All[i].Tier) + " does not exist (the step that creates it failed).");
            }
            const int medium = (int)QualityTier.Medium;

            Object settings = SetupUtil.ProjectSettingsAsset(QualitySettingsPath);
            var serialized = new SerializedObject(settings);
            SerializedProperty levels = SetupUtil.Field(serialized, "m_QualitySettings");
            SerializedProperty current = SetupUtil.Field(serialized, "m_CurrentQuality");
            bool changed = false;

            if (levels.arraySize != assets.Length)
            {
                // The level in force must exist throughout.
                current.intValue = 0;
                levels.arraySize = assets.Length;
                changed = true;
            }
            for (int i = 0; i < assets.Length; i++)
            {
                TierSpec tier = TierSpec.All[i];
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                changed |= Set(level, "name", tier.Name);
                changed |= Set(level, "customRenderPipeline", assets[i]);
                // What still counts under URP. (Shadows, lights and MSAA come from the tier's URP asset.)
                changed |= Set(level, "antiAliasing", tier.Msaa > 1 ? tier.Msaa : 0);
                changed |= Set(level, "globalTextureMipmapLimit", 0);
                changed |= Set(level, "anisotropicTextures", tier.DetailAniso > 1 ? 1 : 0);   // per texture | off
                changed |= Set(level, "vSyncCount", 1);
                changed |= Set(level, "lodBias", 1f);
                changed |= Set(level, "maximumLODLevel", 0);
                changed |= Set(level, "skinWeights", 2);
                changed |= Set(level, "softParticles", false);
                changed |= Set(level, "realtimeReflectionProbes", false);
                changed |= Set(level, "streamingMipmapsActive", false);
                changed |= Set(level, "particleRaycastBudget", 64);
                SerializedProperty excluded = level.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null && excluded.arraySize != 0)
                {
                    excluded.arraySize = 0;
                    changed = true;
                }
            }

            // Medium is where every platform starts; the game then picks its own tier (ART_BIBLE 7.4).
            foreach (SerializedProperty platform in SetupUtil.Field(serialized, "m_PerPlatformDefaultQuality"))
            {
                SerializedProperty level = platform.FindPropertyRelative("second");
                if (level == null || level.intValue == medium) continue;
                level.intValue = medium;
                changed = true;
            }
            if (current.intValue != medium)
            {
                current.intValue = medium;
                changed = true;
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            Quality.RefreshLevels();
            if (QualitySettings.GetQualityLevel() != medium) QualitySettings.SetQualityLevel(medium, true);
            // What a Quality level without an asset of its own would fall back to.
            if (GraphicsSettings.defaultRenderPipeline != assets[medium]) GraphicsSettings.defaultRenderPipeline = assets[medium];
            if (changed) AssetDatabase.SaveAssets();
        }

        // ---- 140: the volume profile the game instantiates (7.2, 7.5) ----------------------------------
        [SetupStep(140)]
        public static void CreateVolumeProfile()
        {
            VolumeProfile profile = SetupUtil.LoadOrCreate(VolumeProfilePath, () => ScriptableObject.CreateInstance<VolumeProfile>());
            bool changed = false;

            // The values of Medium on a day preset; PostLook adjusts its instance per preset and tier.
            Bloom bloom = Component<Bloom>(profile, ref changed);
            changed |= Override(bloom, bloom.threshold, 1.1f);
            changed |= Override(bloom, bloom.intensity, EnvironmentPreset.SunnyRug.Bloom);
            changed |= Override(bloom, bloom.scatter, 0.6f);
            changed |= Override(bloom, bloom.clamp, 8f);
            changed |= Override(bloom, bloom.tint, Color.white);
            changed |= Override(bloom, bloom.highQualityFiltering, TierSpec.Medium.BloomHighQuality);
            changed |= Override(bloom, bloom.downscale, BloomDownscaleMode.Half);
            changed |= Override(bloom, bloom.maxIterations, TierSpec.Medium.BloomIterations);

            // No curve (see PostLook.Tonemap for why not Neutral). ACES is banned: it desaturates and
            // hue-shifts the candy.
            Tonemapping tonemapping = Component<Tonemapping>(profile, ref changed);
            changed |= Override(tonemapping, tonemapping.mode, PostLook.Tonemap);

            ColorAdjustments adjustments = Component<ColorAdjustments>(profile, ref changed);
            changed |= Override(adjustments, adjustments.postExposure, EnvironmentPreset.SunnyRug.Exposure);
            changed |= Override(adjustments, adjustments.contrast, 8f);
            changed |= Override(adjustments, adjustments.saturation, 6f);

            // URP hands the colour to the shader as it is and multiplies the linear picture with it, so it
            // is stored linear here (the art bible's convention: every colour reaches a shader linear).
            Vignette vignette = Component<Vignette>(profile, ref changed);
            changed |= Override(vignette, vignette.color, Palette.Lin(Palette.Ink));
            changed |= Override(vignette, vignette.intensity, 0.18f);
            changed |= Override(vignette, vignette.smoothness, 0.45f);
            changed |= Override(vignette, vignette.center, new Vector2(0.5f, 0.5f));
            changed |= Override(vignette, vignette.rounded, false);

            if (changed) EditorUtility.SetDirty(profile);
        }

        // ---- 150: URP global settings (7.5) --------------------------------------------------------------
        [SetupStep(150)]
        public static void ConfigureGlobalSettings()
        {
            RenderPipelineGlobalSettings global = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if (global == null)
            {
                global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(GlobalSettingsPath);
                if (global == null) throw new InvalidOperationException("There are no URP global settings: " + GlobalSettingsPath + " does not exist.");
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(global);
            }

            // When on, URP assumes no profile is created or changed at runtime and strips the bloom, tonemap
            // and dither variants of the uber shader that no profile asset uses. The game does change its
            // profile at runtime.
            var stripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            if (stripping == null) throw new InvalidOperationException("The URP global settings have no URPShaderStrippingSetting.");
            if (stripping.stripUnusedPostProcessingVariants)
            {
                stripping.stripUnusedPostProcessingVariants = false;
                EditorUtility.SetDirty(global);
            }
        }

        // ---- 190: the shader variant collection the game warms up (3.8) ----------------------------------
        // Late among the pipeline's steps, and safe to run again whenever a shader was added:
        //   tools\unity.ps1 exec -Method Toybox.EditorTools.PipelineSetup.WriteShaderVariants
        [SetupStep(190)]
        public static void WriteShaderVariants()
        {
            var wanted = new List<ShaderVariantCollection.ShaderVariant>();
            foreach (string name in GameShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning("[Toybox] setup: the shader '" + name + "' does not exist yet; it is not in the variant collection. Run PipelineSetup.WriteShaderVariants again once it does.");
                    continue;
                }
                if (ShaderUtil.ShaderHasError(shader))
                {
                    Debug.LogWarning("[Toybox] setup: the shader '" + name + "' does not compile; it is not in the variant collection.");
                    continue;
                }
                AddVariants(shader, wanted);
            }

            ShaderVariantCollection collection = SetupUtil.LoadOrCreate(VariantsPath, () => new ShaderVariantCollection());
            bool same = collection.variantCount == wanted.Count;
            for (int i = 0; same && i < wanted.Count; i++) same = collection.Contains(wanted[i]);
            if (same) return;

            collection.Clear();
            foreach (ShaderVariantCollection.ShaderVariant variant in wanted) collection.Add(variant);
            EditorUtility.SetDirty(collection);
            Debug.Log("[Toybox] setup: " + VariantsPath + " lists " + collection.variantCount + " variants of " + collection.shaderCount + " shaders.");
        }

        /// <summary>
        /// The variants of one shader the game draws with: per kind of pass one program, and for a lit
        /// pass (one that declares the main-light shadow keywords) one per shadow state instead.
        /// </summary>
        static void AddVariants(Shader shader, List<ShaderVariantCollection.ShaderVariant> variants)
        {
            bool lit = shader.keywordSpace.FindKeyword(ShadowsCascade).isValid;
            var done = new HashSet<PassType>();
            var lightMode = new ShaderTagId("LightMode");
            for (int pass = 0; pass < shader.passCount; pass++)
            {
                // Unity hands LightMode values back in capitals ("SHADOWCASTER").
                string mode = (shader.FindPassTagValue(pass, lightMode).name ?? "").ToUpperInvariant();
                // Not drawn by this pipeline: there is no depth texture and no depth priming.
                if (mode == "DEPTHONLY" || mode == "DEPTHNORMALS" || mode == "META") continue;

                PassType type;
                if (mode.Length == 0 || mode == "ALWAYS") type = PassType.Normal;
                else if (mode == "SRPDEFAULTUNLIT") type = PassType.ScriptableRenderPipelineDefaultUnlit;
                else if (mode == "SHADOWCASTER") type = PassType.ShadowCaster;
                else type = PassType.ScriptableRenderPipeline;
                if (!done.Add(type)) continue;

                if (lit && type == PassType.ScriptableRenderPipeline)
                {
                    foreach (string[] keywords in LitKeywordSets) TryAdd(variants, shader, type, keywords);
                }
                else
                {
                    TryAdd(variants, shader, type);
                }
            }
        }

        static bool TryAdd(List<ShaderVariantCollection.ShaderVariant> variants, Shader shader, PassType type, params string[] keywords)
        {
            try
            {
                // This constructor checks that the shader has such a pass and such keywords.
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader, type, keywords));
                return true;
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning("[Toybox] setup: no variant " + shader.name + " / " + type + " / [" + string.Join(" ", keywords) + "]: " + e.Message);
                return false;
            }
        }

        static UniversalRendererData LoadRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CoreSetup.RendererPath);
            if (renderer == null) throw new InvalidOperationException(CoreSetup.RendererPath + " does not exist: run the core setup steps first.");
            return renderer;
        }

        // The volume component of a type on the profile asset, added as a sub-asset if it is missing (the
        // way URP's own inspector does it).
        static T Component<T>(VolumeProfile profile, ref bool changed) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>();
                component.name = typeof(T).Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
                changed = true;
            }
            if (!component.active)
            {
                component.active = true;
                EditorUtility.SetDirty(component);
                changed = true;
            }
            return component;
        }

        static bool Override<T>(VolumeComponent component, VolumeParameter<T> parameter, T value)
        {
            if (parameter.overrideState && EqualityComparer<T>.Default.Equals(parameter.value, value)) return false;
            parameter.overrideState = true;
            parameter.value = value;
            EditorUtility.SetDirty(component);
            return true;
        }

        static bool Set(SerializedProperty parent, string name, string value)
        {
            SerializedProperty property = Relative(parent, name);
            if (property.stringValue == value) return false;
            property.stringValue = value;
            return true;
        }

        static bool Set(SerializedProperty parent, string name, int value)
        {
            SerializedProperty property = Relative(parent, name);
            if (property.intValue == value) return false;
            property.intValue = value;
            return true;
        }

        static bool Set(SerializedProperty parent, string name, bool value)
        {
            SerializedProperty property = Relative(parent, name);
            if (property.propertyType != SerializedPropertyType.Boolean) return Set(parent, name, value ? 1 : 0);
            if (property.boolValue == value) return false;
            property.boolValue = value;
            return true;
        }

        static bool Set(SerializedProperty parent, string name, float value)
        {
            SerializedProperty property = Relative(parent, name);
            if (property.floatValue == value) return false;
            property.floatValue = value;
            return true;
        }

        static bool Set(SerializedProperty parent, string name, Object value)
        {
            SerializedProperty property = Relative(parent, name);
            if (property.objectReferenceValue == value) return false;
            property.objectReferenceValue = value;
            return true;
        }

        static SerializedProperty Relative(SerializedProperty parent, string name)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property == null) throw new InvalidOperationException("A Quality level has no field '" + name + "'.");
            return property;
        }

        // ---- Looking at the result ----------------------------------------------------------------------

        /// <summary>
        /// Renders a level at all three tiers and reports numbers measured in the pictures, for checking
        /// the tiers against ART_BIBLE 7.3 by eye and by figure:
        ///
        ///   tools\unity.ps1 exec -Method Toybox.EditorTools.PipelineSetup.CaptureTiers
        ///       -UnityArgs '-toyboxLevel','0','-toyboxTimes','0,3','-toyboxOut','tools/out/shots/tiers'
        ///
        /// Writes &lt;out&gt;/low|medium|high/levelNN-tSS.png. With -toyboxPlainLit the level is drawn with the
        /// plain look (URP Lit, the plain sun) and this area's post-processing on top - a picture that
        /// depends on nobody else's presenters. -toyboxCheck draws <see cref="PostCheckLevel"/> that way
        /// instead of a registered level. -toyboxSize WxH as for Shots.
        /// </summary>
        public static void CaptureTiers()
        {
            int level = ToyboxArgs.GetInt("-toyboxLevel", 0);
            string directory = ToyboxArgs.Get("-toyboxOut", "tools/out/shots/tiers");
            bool check = ToyboxArgs.Has("-toyboxCheck");
            bool plainLit = check || ToyboxArgs.Has("-toyboxPlainLit");
            int width = 1280, height = 720;
            Shots.ParseSize(ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);

            foreach (TierSpec tier in TierSpec.All)
            {
                var request = new ShotRequest
                {
                    Level = level,
                    Definition = check ? new PostCheckLevel() : null,
                    Times = Shots.ParseTimes(ToyboxArgs.Get("-toyboxTimes", "0")),
                    Width = width,
                    Height = height,
                    OutputDirectory = Path.Combine(directory, tier.Name.ToLowerInvariant()),
                    Quality = tier.Tier,
                    Plain = plainLit,
                    Presenters = plainLit ? PlainLitPresenters() : null,
                };
                foreach (string file in Capture(request))
                    Debug.Log("[Toybox] tier " + tier.Name + ": " + Path.GetFileName(file) + ": " + Measure(file));
                Debug.Log("[Toybox] tier " + tier.Name + ": render targets at " + width + "x" + height + " about " +
                    (Quality.RenderTargetBytes(tier, width, height) / (1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture) + " MB, at the tier's full size " +
                    (Quality.RenderTargetBytes(tier, tier.PixelWidth, tier.PixelHeight) / (1024f * 1024f)).ToString("0.0", CultureInfo.InvariantCulture) + " MB (budget " + tier.MaxRenderTargetMb + ")");
            }
        }

        /// <summary>
        /// Shots.Run through an HDR target: the request's level, played by its bot, photographed at the
        /// request's times the way the game puts it on the screen. (Shots itself renders into an 8-bit
        /// texture, and URP takes the format of its intermediate colour target from a camera's target
        /// texture - so in a Shots picture nothing is brighter than 1 and bloom has nothing to work with.)
        /// The overview is not supported. Returns the files written.
        /// </summary>
        public static List<string> Capture(ShotRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (Application.isPlaying) throw new InvalidOperationException("PipelineSetup.Capture works outside Play Mode only.");
            string directory = request.OutputDirectory ?? "tools/out/shots";
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), directory);
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var files = new List<string>();

            Game.Current?.Dispose();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            bool previousPlain = Materials.Plain;
            Game game = null;
            Presentation presentation = null;
            try
            {
                Materials.Plain = request.Plain;
                game = Game.Create(new GameOptions());
                game.LoadLevel(request.Definition ?? LevelRegistry.Get(request.Level));
                presentation = Presentation.Create(game, new PresentationOptions { Plain = request.Plain, Quality = request.Quality, Presenters = request.Presenters });
                int msaa = TierSpec.Of(presentation.Context.Quality).Msaa;
                presentation.Frame(0f, 1f);
                // The first render after the pipeline was (re)created is not to be trusted (see Shots.Warm).
                Object.DestroyImmediate(Photograph(presentation.Camera, request.Width, request.Height, msaa));

                var bot = new Bot(game);
                BotRunner script = null;
                try
                {
                    script = new BotRunner(game.Level.Solve(bot));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Toybox] bot error: " + e.Message);
                }

                float[] times = request.Times != null && request.Times.Length > 0 ? (float[])request.Times.Clone() : new[] { 0f };
                Array.Sort(times);
                int tick = 0;
                foreach (float time in times)
                {
                    for (int until = Mathf.RoundToInt(Mathf.Max(0f, time) / Sim.Dt); tick < until; tick++)
                    {
                        try
                        {
                            if (script != null && !script.Advance()) script = null;
                        }
                        catch (Exception e)
                        {
                            script = null;
                            Debug.LogWarning("[Toybox] bot error: " + e.Message);
                        }
                        game.Tick();
                        presentation.Frame(Sim.Dt, 1f);
                    }
                    presentation.Frame(0f, 1f);
                    string name = "level" + request.Level.ToString("00", CultureInfo.InvariantCulture) + "-t" + time.ToString("00.##", CultureInfo.InvariantCulture) + ".png";
                    string path = Path.Combine(directory, name);
                    Texture2D image = Photograph(presentation.Camera, request.Width, request.Height, msaa);
                    try
                    {
                        File.WriteAllBytes(path, image.EncodeToPNG());
                    }
                    finally
                    {
                        Object.DestroyImmediate(image);
                    }
                    Debug.Log("[Toybox] shot: " + path);
                    files.Add(path);
                }

                // What the level costs, against the budget of the tier it was drawn at (ART_BIBLE 12.1).
                TierSpec tier = TierSpec.Of(presentation.Context.Quality);
                SceneCensus census = SceneCensus.Take(game);
                string over = census.Over(tier);
                Debug.Log("[Toybox] census (" + tier.Name + "): " + census + (over == null ? "; within the budget" : "; over the budget before culling: " + over));
            }
            finally
            {
                presentation?.Dispose();
                game?.Dispose();
                Materials.Plain = previousPlain;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return files;
        }

        /// <summary>
        /// One picture of what a camera sees, as the 8-bit sRGB image a screen would show. The camera
        /// renders into an HDR target of the game's own colour format, as it does on the screen, and the
        /// result is encoded afterwards. The caller destroys the texture.
        /// </summary>
        public static Texture2D Photograph(Camera camera, int width, int height, int msaa = 1) => Shots.Photograph(camera, width, height, msaa);

        /// <summary>
        /// The presenters of a picture that only this area decides: the plain look, forced to stay even
        /// though PostLook provides look too, the quality presenter and PostLook.
        /// </summary>
        public static List<PresenterRegistry.Entry> PlainLitPresenters() => new List<PresenterRegistry.Entry>
        {
            new PresenterRegistry.Entry(typeof(PlainLook), new PresenterAttribute(-1000) { ProvidesLook = true, Fallback = true }),
            new PresenterRegistry.Entry(typeof(QualityPresenter), new PresenterAttribute(10)),
            // Without its ProvidesLook flag, so that the plain look does not retire it.
            new PresenterRegistry.Entry(typeof(PostLook), new PresenterAttribute(20)),
        };

        /// <summary>
        /// Numbers read from a picture: how bright the middle and the corners are (the vignette), how much
        /// of it is clipped to white (glints and bloom cores), and how sharp the middle band and the top
        /// and bottom bands are (the lens blur).
        /// </summary>
        public static string Measure(string file)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(file))) return "unreadable";
                Color32[] pixels = image.GetPixels32();
                int w = image.width, h = image.height;
                float centre = MeanLuma(pixels, w, h, 0.4f, 0.4f, 0.6f, 0.6f);
                float corners = (MeanLuma(pixels, w, h, 0f, 0f, 0.06f, 0.06f) + MeanLuma(pixels, w, h, 0.94f, 0f, 1f, 0.06f) +
                                 MeanLuma(pixels, w, h, 0f, 0.94f, 0.06f, 1f) + MeanLuma(pixels, w, h, 0.94f, 0.94f, 1f, 1f)) / 4f;
                int clipped = 0;
                foreach (Color32 pixel in pixels)
                    if (pixel.r >= 250 && pixel.g >= 250 && pixel.b >= 250) clipped++;
                float middle = Sharpness(pixels, w, h, 0.35f, 0.65f);
                float edges = (Sharpness(pixels, w, h, 0f, 0.1f) + Sharpness(pixels, w, h, 0.9f, 1f)) / 2f;
                return string.Format(CultureInfo.InvariantCulture,
                    "centre luma {0:0.000}, corner luma {1:0.000}, clipped {2:0.00}%, sharpness middle {3:0.00} / top+bottom {4:0.00}",
                    centre, corners, 100f * clipped / pixels.Length, middle, edges);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        static float Luma(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        static float MeanLuma(Color32[] pixels, int w, int h, float x0, float y0, float x1, float y1)
        {
            int left = Mathf.FloorToInt(x0 * w), right = Mathf.Max(left + 1, Mathf.FloorToInt(x1 * w));
            int bottom = Mathf.FloorToInt(y0 * h), top = Mathf.Max(bottom + 1, Mathf.FloorToInt(y1 * h));
            float sum = 0f;
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                    sum += Luma(pixels[y * w + x]);
            return sum / ((right - left) * (top - bottom));
        }

        // The RMS step in brightness between neighbouring pixels of the rows y0..y1, in 1/255. (The mean
        // step would not do: a blurred edge is as tall as a sharp one, only less steep.)
        static float Sharpness(Color32[] pixels, int w, int h, float y0, float y1)
        {
            int bottom = Mathf.FloorToInt(y0 * h), top = Mathf.Min(h - 1, Mathf.FloorToInt(y1 * h));
            double sum = 0.0;
            int count = 0;
            for (int y = bottom; y < top; y++)
                for (int x = 0; x < w - 1; x++)
                {
                    float here = Luma(pixels[y * w + x]);
                    float dx = Luma(pixels[y * w + x + 1]) - here, dy = Luma(pixels[(y + 1) * w + x]) - here;
                    sum += dx * dx + dy * dy;
                    count++;
                }
            return count > 0 ? 255f * Mathf.Sqrt((float)(sum / count)) : 0f;
        }
    }

    /// <summary>
    /// A level made for looking at post-processing, not for playing: small candy blocks close to the
    /// camera (detail in the bottom band of the picture), a row of tall posts further away (detail in the
    /// top band) and one lamp far above the bloom threshold at the crosshair. It is not registered; the
    /// capture tool and the pipeline's tests build it by hand.
    /// </summary>
    public sealed class PostCheckLevel : LevelDefinition
    {
        /// <summary>Where the lamp is, and how big: the middle of the picture from the spawn point.</summary>
        public static readonly Vector3 LampPosition = new Vector3(0f, 1.55f, 9f);
        public const float LampSize = 1.2f;
        /// <summary>The lamp's emission, linear: far above the bloom threshold.</summary>
        public const float LampGain = 6f;

        public override void Build(LevelContext ctx)
        {
            ctx.AddStatic(Toys.BasicToys.Slab(new Vector3(60f, 1f, 80f), Palette.Mint.Mid), new Vector3(0f, -0.5f, 30f));
            for (int row = 0; row < 4; row++)
                for (int i = -6; i <= 6; i++)
                    ctx.AddStatic(Toys.BasicToys.Block(0.3f, Palette.Candy[(i + 6 + row) % Palette.Candy.Length]), new Vector3(i * 0.6f, 0.15f, 2.4f + row * 0.7f));
            for (int i = -12; i <= 12; i++)
                ctx.AddStatic(Toys.BasicToys.Slab(new Vector3(0.5f, 30f, 0.5f), i % 2 == 0 ? Palette.Ink : Palette.Paper), new Vector3(i * 1.5f, 15f, 24f));
            Material lamp = Materials.Emissive(ToyRecipe.Lamp, Palette.Paper, Color.white * LampGain);
            ctx.AddStatic(Toys.BasicToys.Slab(new Vector3(LampSize, LampSize, LampSize), lamp), LampPosition);
            ctx.SetSpawn(Vector3.zero, 0f);
        }
    }
}
