using System.Collections.Generic;
using Toybox.Engine;
using Toybox.Render;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The setup steps of toy shading (orders 300-399; ART_BIBLE 3.8, 7.5 item 6, 8.1 steps 2 and 5):
    ///
    /// - Resources/Materials/ToyLit.mat is pointed at Toybox/ToyLit, which makes every toy material the
    ///   game's own and keeps the shader in a player build;
    /// - Resources/Materials/Sticker.mat is created for Toybox/Sticker;
    /// - every universal renderer of the project gets the StickerFeature with a serialized reference to
    ///   Sticker.mat, loses the Held layer from the masks of its ordinary passes, and has its features put
    ///   in the order MacroBand, Sticker.
    ///
    /// The last two belong together and happen in one step: with the masks changed and no sticker pass a
    /// held toy would not be drawn at all. The pipeline's steps run earlier and may have made the renderer
    /// asset anew; whatever they left, this brings it to the same state every time.
    /// </summary>
    public static class ToyShadingSetup
    {
        public const string ToyShaderName = "Toybox/ToyLit";
        public const string StickerShaderName = "Toybox/Sticker";
        public const string StickerPath = SetupUtil.MaterialsDir + "/Sticker.mat";

        [SetupStep(300)]
        static void ToyTemplate()
        {
            Shader shader = Shader.Find(ToyShaderName);
            Material before = AssetDatabase.LoadAssetAtPath<Material>(CoreSetup.ToyLitPath);
            bool repointed = before != null && shader != null && before.shader != shader;
            Material material = SetupUtil.LoadOrCreateMaterial(CoreSetup.ToyLitPath, ToyShaderName);
            if (!repointed) return;

            // The template was URP Lit until now. Runtime code sets every property of its clones by name,
            // but the template itself should hold the shader's defaults, not what URP Lit left behind
            // (smoothness 0.5, its keywords, a render queue of its own).
            var defaults = new Material(shader);
            material.CopyPropertiesFromMaterial(defaults);
            material.shaderKeywords = new string[0];
            material.renderQueue = -1;
            material.enableInstancing = false;
            Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(material);
        }

        [SetupStep(310)]
        static void StickerTemplate() => SetupUtil.LoadOrCreateMaterial(StickerPath, StickerShaderName);

        [SetupStep(320)]
        static void StickerPass()
        {
            Material sticker = AssetDatabase.LoadAssetAtPath<Material>(StickerPath);
            if (sticker == null) throw new System.InvalidOperationException("'" + StickerPath + "' does not exist: the sticker material step has to run first.");
            List<UniversalRendererData> renderers = Renderers();
            if (renderers.Count == 0) throw new System.InvalidOperationException("The project has no universal renderer asset ('" + CoreSetup.RendererPath + "' is missing).");
            foreach (UniversalRendererData renderer in renderers) Configure(renderer, sticker);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Every universal renderer the game can render with: the shared one, and whatever the pipeline
        /// assets of the graphics and quality settings and of the Settings folder list.
        /// </summary>
        public static List<UniversalRendererData> Renderers()
        {
            var found = new List<UniversalRendererData>();
            void Add(ScriptableRendererData data)
            {
                if (data is UniversalRendererData universal && !found.Contains(universal)) found.Add(universal);
            }
            void AddAll(RenderPipelineAsset asset)
            {
                if (!(asset is UniversalRenderPipelineAsset urp)) return;
                foreach (ScriptableRendererData data in urp.rendererDataList) Add(data);
            }

            Add(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CoreSetup.RendererPath));
            AddAll(GraphicsSettings.defaultRenderPipeline);
            for (int level = 0; level < QualitySettings.names.Length; level++) AddAll(QualitySettings.GetRenderPipelineAssetAt(level));
            if (AssetDatabase.IsValidFolder(SetupUtil.SettingsDir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { SetupUtil.SettingsDir }))
                    AddAll(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid)));
                foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { SetupUtil.SettingsDir }))
                    Add(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            return found;
        }

        /// <summary>
        /// Brings one renderer to the state the held-object rule needs. Returns true if anything changed;
        /// a second call changes nothing.
        /// </summary>
        public static bool Configure(UniversalRendererData renderer, Material sticker)
        {
            if (renderer == null) throw new System.ArgumentNullException(nameof(renderer));
            bool changed = false;

            // The sticker pass, with the reference that keeps Toybox/Sticker in the build.
            int featuresBefore = renderer.rendererFeatures.Count;
            StickerFeature feature = SetupUtil.EnsureRendererFeature<StickerFeature>(renderer, StickerFeature.FeatureName);
            changed |= renderer.rendererFeatures.Count != featuresBefore;
            if (feature.Material != sticker) changed |= SetupUtil.SetField(feature, "material", sticker);
            if (!feature.isActive)
            {
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                changed = true;
            }

            // The ordinary passes skip the held toy. The camera's culling mask still has the layer, so the
            // toy is in the cull results the sticker pass draws from.
            int held = Layers.HeldMask;
            if ((renderer.opaqueLayerMask.value & held) != 0)
            {
                renderer.opaqueLayerMask = renderer.opaqueLayerMask.value & ~held;
                changed = true;
            }
            if ((renderer.transparentLayerMask.value & held) != 0)
            {
                renderer.transparentLayerMask = renderer.transparentLayerMask.value & ~held;
                changed = true;
            }
            if ((renderer.prepassLayerMask.value & held) != 0)
            {
                renderer.prepassLayerMask = renderer.prepassLayerMask.value & ~held;
                changed = true;
            }

            // The border and the peel shadow each touch a pixel once through the stencil.
            DepthFormat depth = renderer.depthAttachmentFormat;
            if (depth != DepthFormat.Default && !GraphicsFormatUtility.IsStencilFormat((GraphicsFormat)depth))
            {
                Debug.LogWarning("[Toybox] " + renderer.name + ": depth attachment format " + depth + " has no stencil; the sticker pass needs one. Set to Default.");
                renderer.depthAttachmentFormat = DepthFormat.Default;
                changed = true;
            }

            // The lens blur comes before the sticker: it is on the lens, so the lens cannot blur it.
            changed |= SetupUtil.OrderRendererFeatures(renderer, "MacroBand", StickerFeature.FeatureName);

            if (changed)
            {
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
            }
            return changed;
        }
    }
}
