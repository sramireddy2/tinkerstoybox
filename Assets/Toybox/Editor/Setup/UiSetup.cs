using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Toybox.Art;
using Toybox.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The setup steps of the user interface (orders 400-499), ART_BIBLE 10.1 and 7.5 item 7:
    ///
    ///   400  the TextMeshPro essential resources (settings, shaders, the default font), if they are missing
    ///   410  the two static font assets, baked from the TTF files into Resources/Fonts
    ///   420  the three material presets - outline and underlay are shader_feature keywords, so the sticker
    ///        text style only survives a player build as a material asset
    ///   430  the material the menus show a camera picture with (the pause card, the level-complete card)
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run -UnityArgs '-toyboxSteps','UiSetup'
    ///
    /// -toyboxRebakeFonts bakes the fonts again in place (after the TTF files or the character list changed).
    /// </summary>
    public static class UiSetup
    {
        public const string FontSourceDir = SetupUtil.Root + "/Fonts";
        public const string FontsDir = SetupUtil.ResourcesDir + "/Fonts";
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string TmpEssentials = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        const string SdfShader = "TextMeshPro/Mobile/Distance Field";

        const int AtlasSize = 1024;
        const int AtlasPadding = 8;
        const int DisplaySamplingSize = 72, BodySamplingSize = 48;
        // If the glyphs of a face do not fit one atlas at the size asked for, the size comes down in these steps.
        const int SamplingStep = 4, MinSamplingSize = 32;

        public static string DisplayFontPath => FontsDir + "/" + UiFonts.DisplayName + ".asset";
        public static string BodyFontPath => FontsDir + "/" + UiFonts.BodyName + ".asset";
        public static string PresetPath(string name) => FontsDir + "/" + name + ".mat";

        [SetupStep(400)]
        public static void ImportTextMeshProEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath) != null && Shader.Find(SdfShader) != null) return;
            if (!File.Exists(Path.GetFullPath(TmpEssentials)))
                throw new InvalidOperationException("The TMP essential resources are not where they are expected: " + TmpEssentials);
            AssetDatabase.ImportPackage(TmpEssentials, false);
            AssetDatabase.Refresh();
            bool imported = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath) != null;
            Debug.Log("[Toybox] ui setup: TMP essential resources " + (imported ? "imported" : "are being imported - run the setup once more to bake the fonts"));
        }

        [SetupStep(410)]
        public static void BakeFonts()
        {
            RequireEssentials();
            SetupUtil.EnsureFolder(FontsDir);
            bool rebake = ToyboxArgs.Has("-toyboxRebakeFonts");
            TMP_FontAsset display = Bake(FontSourceDir + "/Unbounded-Bold.ttf", DisplayFontPath, DisplaySamplingSize, rebake);
            TMP_FontAsset body = Bake(FontSourceDir + "/Figtree-SemiBold.ttf", BodyFontPath, BodySamplingSize, rebake);

            // Per-character fallback: what one face lacks is taken from the other (and after that from TMP's default).
            Fallback(body, display);
            Fallback(display, body);
            AssetDatabase.SaveAssets();
        }

        [SetupStep(420)]
        public static void CreateMaterialPresets()
        {
            RequireEssentials();
            TMP_FontAsset display = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            if (display != null)
            {
                Preset(UiFonts.DisplayStickerPreset, display, sticker: true);
                Preset(UiFonts.DisplayPlainPreset, display, sticker: false);
            }
            if (body != null) Preset(UiFonts.BodyPlainPreset, body, sticker: false);
            if (display == null || body == null)
                Debug.LogWarning("[Toybox] ui setup: a font asset is missing, so its material presets were not made; the UI falls back to the TMP default font.");
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A camera picture on a card must not be see-through where the camera left alpha at zero, and the
        /// default UI shader multiplies by the texture's alpha. Unlit/Texture ignores it; a shader only
        /// ships if a material asset under Resources points at it.
        /// </summary>
        [SetupStep(430)]
        public static void CreateFrameMaterial()
        {
            SetupUtil.EnsureFolder(SetupUtil.ResourcesDir + "/UI");
            SetupUtil.LoadOrCreateMaterial(FrameMaterialPath, "Unlit/Texture");
            AssetDatabase.SaveAssets();
        }

        public const string FrameMaterialPath = SetupUtil.ResourcesDir + "/" + MenuBackdrop.FrameMaterialPath + ".mat";

        static void RequireEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath) == null || Shader.Find(SdfShader) == null)
                throw new InvalidOperationException("The TMP essential resources are not imported yet (UiSetup.ImportTextMeshProEssentials). Run the setup once more.");
        }

        // One static font asset with its atlas and its material as sub-assets. An existing one is kept
        // unless a rebake is asked for; then it is filled again in place, so references to it survive.
        static TMP_FontAsset Bake(string ttfPath, string assetPath, int samplingSize, bool rebake)
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null && !rebake && existing.atlasPopulationMode == AtlasPopulationMode.Static && existing.characterTable.Count > 0)
                return existing;

            Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogWarning("[Toybox] ui setup: " + ttfPath + " is not in the repository; the UI falls back to the TMP default font.");
                return existing;
            }

            string name = Path.GetFileNameWithoutExtension(assetPath);
            TMP_FontAsset baked = null;
            string missing = null;
            int size = samplingSize;
            for (; size >= MinSamplingSize; size -= SamplingStep)
            {
                // One atlas, no more: with multi-atlas support off, glyphs that do not fit are reported as missing.
                baked = TMP_FontAsset.CreateFontAsset(font, size, AtlasPadding, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
                if (baked == null) throw new InvalidOperationException("TextMeshPro could not read " + ttfPath + ".");
                baked.TryAddCharacters(UiFonts.Characters, out missing, true);
                if (Fits(font, size, missing)) break;
                Object.DestroyImmediate(baked.material);
                Object.DestroyImmediate(baked.atlasTexture);
                Object.DestroyImmediate(baked);
                baked = null;
            }
            if (baked == null) throw new InvalidOperationException("The glyphs of " + ttfPath + " do not fit a " + AtlasSize + " atlas even at " + MinSamplingSize + " px.");

            int pairs = Prune(baked);
            baked.atlasPopulationMode = AtlasPopulationMode.Static;
            Texture2D atlas = baked.atlasTexture;
            Material material = baked.material;

            if (existing != null)
            {
                // In place: the main object keeps its id, the old atlas and material go.
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    if (sub != null && sub != existing) AssetDatabase.RemoveObjectFromAsset(sub);
                EditorUtility.CopySerialized(baked, existing);
                // The atlas and the material become the asset's before the scratch copy goes: a font
                // asset that is destroyed takes an atlas that is not saved anywhere with it.
                AssetDatabase.AddObjectToAsset(atlas, existing);
                AssetDatabase.AddObjectToAsset(material, existing);
                Object.DestroyImmediate(baked);
                baked = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(baked, assetPath);
                AssetDatabase.AddObjectToAsset(atlas, baked);
                AssetDatabase.AddObjectToAsset(material, baked);
            }
            baked.name = name;
            atlas.name = name + " Atlas";
            material.name = name + " Material";
            // The pixels are only needed on the GPU: static, nothing is ever added.
            SetupUtil.SetField(atlas, "m_IsReadable", false);
            EditorUtility.SetDirty(baked);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);

            string lacks = string.IsNullOrEmpty(missing) ? "none" : Describe(missing);
            Debug.Log("[Toybox] ui setup: baked " + name + " at " + size + " px, " + baked.characterTable.Count + " characters, " + pairs +
                      " kerning pairs; missing from the face: " + lacks);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }

        // The face's feature tables are read whole: tens of thousands of kerning pairs with glyphs that are
        // not in the atlas, and ligatures whose glyphs are not either (an "fi" would come out as a missing
        // glyph). Kerning between the glyphs that are there is kept; everything else goes. Returns the pairs kept.
        static int Prune(TMP_FontAsset font)
        {
            var glyphs = new HashSet<uint>();
            foreach (UnityEngine.TextCore.Glyph glyph in font.glyphTable) glyphs.Add(glyph.index);
            TMP_FontFeatureTable features = font.fontFeatureTable;
            features.ligatureRecords?.Clear();
            features.multipleSubstitutionRecords?.Clear();
            features.MarkToBaseAdjustmentRecords?.Clear();
            features.MarkToMarkAdjustmentRecords?.Clear();
            if (features.glyphPairAdjustmentRecords == null) return 0;
            features.glyphPairAdjustmentRecords.RemoveAll(pair =>
                !glyphs.Contains(pair.firstAdjustmentRecord.glyphIndex) || !glyphs.Contains(pair.secondAdjustmentRecord.glyphIndex));
            features.SortGlyphPairAdjustmentRecords();
            return features.glyphPairAdjustmentRecords.Count;
        }

        // True if every character the face has made it into the atlas: what is still missing is missing from the face itself.
        static bool Fits(Font font, int size, string missing)
        {
            if (string.IsNullOrEmpty(missing)) return true;
            if (FontEngine.LoadFontFace(font, size) != FontEngineError.Success) return true;
            foreach (char c in missing)
                if (FontEngine.TryGetGlyphIndex(c, out uint index) && index != 0) return false;
            return true;
        }

        static string Describe(string characters)
        {
            var parts = new List<string>();
            foreach (char c in characters) parts.Add("'" + c + "' U+" + ((int)c).ToString("X4"));
            return string.Join(", ", parts);
        }

        static void Fallback(TMP_FontAsset asset, TMP_FontAsset fallback)
        {
            if (asset == null || fallback == null) return;
            if (asset.fallbackFontAssetTable == null) asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (asset.fallbackFontAssetTable.Count == 1 && asset.fallbackFontAssetTable[0] == fallback) return;
            asset.fallbackFontAssetTable.Clear();
            asset.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(asset);
        }

        // A material preset of a font asset: the font's own material, plus (for the sticker style) the
        // die-cut edge in Paper and the hard peel shadow in Ink at 18% (ART_BIBLE 10.1).
        static void Preset(string name, TMP_FontAsset font, bool sticker)
        {
            Material source = font.material;
            Material preset = SetupUtil.LoadOrCreate(PresetPath(name), () => new Material(source));
            bool changed = false;
            if (preset.shader != source.shader)
            {
                preset.shader = source.shader;
                changed = true;
            }
            changed |= Texture(preset, ShaderUtilities.ID_MainTex, font.atlasTexture);
            changed |= Number(preset, ShaderUtilities.ID_TextureWidth, font.atlasWidth);
            changed |= Number(preset, ShaderUtilities.ID_TextureHeight, font.atlasHeight);
            changed |= Number(preset, ShaderUtilities.ID_GradientScale, source.GetFloat(ShaderUtilities.ID_GradientScale));
            changed |= Number(preset, ShaderUtilities.ID_WeightNormal, source.GetFloat(ShaderUtilities.ID_WeightNormal));
            changed |= Number(preset, ShaderUtilities.ID_WeightBold, source.GetFloat(ShaderUtilities.ID_WeightBold));

            Color shadow = Palette.Ink;
            shadow.a = sticker ? UiFonts.StickerUnderlayAlpha : 0f;
            changed |= Number(preset, ShaderUtilities.ID_OutlineWidth, sticker ? UiFonts.StickerOutline : 0f);
            // The edge is added around the glyph, not carved out of it.
            changed |= Number(preset, ShaderUtilities.ID_FaceDilate, sticker ? UiFonts.StickerOutline : 0f);
            changed |= Colour(preset, ShaderUtilities.ID_OutlineColor, Palette.Paper);
            changed |= Colour(preset, ShaderUtilities.ID_UnderlayColor, shadow);
            changed |= Number(preset, ShaderUtilities.ID_UnderlayOffsetX, sticker ? UiFonts.StickerUnderlayOffset.x : 0f);
            changed |= Number(preset, ShaderUtilities.ID_UnderlayOffsetY, sticker ? UiFonts.StickerUnderlayOffset.y : 0f);
            changed |= Number(preset, ShaderUtilities.ID_UnderlayDilate, sticker ? UiFonts.StickerOutline : 0f);
            changed |= Number(preset, ShaderUtilities.ID_UnderlaySoftness, 0f);
            changed |= Keyword(preset, ShaderUtilities.Keyword_Outline, sticker);
            changed |= Keyword(preset, ShaderUtilities.Keyword_Underlay, sticker);
            if (!changed) return;
            ShaderUtilities.UpdateShaderRatios(preset);
            EditorUtility.SetDirty(preset);
        }

        static bool Number(Material material, int id, float value)
        {
            if (!material.HasProperty(id) || Mathf.Approximately(material.GetFloat(id), value)) return false;
            material.SetFloat(id, value);
            return true;
        }

        static bool Colour(Material material, int id, Color value)
        {
            if (!material.HasProperty(id) || material.GetColor(id) == value) return false;
            material.SetColor(id, value);
            return true;
        }

        static bool Texture(Material material, int id, Texture value)
        {
            if (!material.HasProperty(id) || material.GetTexture(id) == value) return false;
            material.SetTexture(id, value);
            return true;
        }

        static bool Keyword(Material material, string keyword, bool on)
        {
            if (material.IsKeywordEnabled(keyword) == on) return false;
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
            return true;
        }
    }
}
