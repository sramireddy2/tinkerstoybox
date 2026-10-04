using System.Text;
using TMPro;
using UnityEngine;

namespace Toybox.UI
{
    public enum UiFont
    {
        /// <summary>Unbounded Bold: numbers, titles, the readout, short button labels.</summary>
        Display,
        /// <summary>Figtree SemiBold: hints, blurbs, settings.</summary>
        Body,
    }

    /// <summary>
    /// The two typefaces of the UI (ART_BIBLE 10.1) as static TextMeshPro font assets, baked by
    /// UiSetup into Resources/Fonts together with their material presets. Nothing is rasterised at
    /// runtime. If the baked assets are not there (the TTF files were not in the repository when the
    /// setup ran), every label uses TextMeshPro's default font asset instead; labels shrink to fit, so
    /// the layout does not depend on which face it is.
    /// </summary>
    public static class UiFonts
    {
        public const string DisplayName = "Unbounded-Bold SDF", BodyName = "Figtree-SemiBold SDF";
        public const string DisplayStickerPreset = "Display-Sticker", DisplayPlainPreset = "Display-Plain", BodyPlainPreset = "Body-Plain";
        public const string DefaultFolder = "Fonts";

        /// <summary>The sticker text style: the die-cut edge in Paper and the peel shadow in Ink (10.1).</summary>
        public const float StickerOutline = 0.2f, StickerUnderlayAlpha = 0.18f;
        public static readonly Vector2 StickerUnderlayOffset = new Vector2(0.5f, -0.7f);

        /// <summary>Everything the font assets hold: ASCII 32-126 and the handful of marks the UI writes.</summary>
        public static readonly string Characters = BuildCharacters();
        public const string ExtraCharacters = "×÷·—–‘’“”…°±";

        static string folder = DefaultFolder;
        static bool loaded;
        static TMP_FontAsset display, body, fallback;
        static Material displaySticker, displayPlain, bodyPlain;

        /// <summary>The Resources folder the assets are loaded from. Tests point it elsewhere to see the fallback.</summary>
        public static string Folder
        {
            get => folder;
            set
            {
                folder = string.IsNullOrEmpty(value) ? DefaultFolder : value;
                loaded = false;
            }
        }

        /// <summary>
        /// False if TextMeshPro's own resources are not in the project (the setup never ran): no text can be
        /// made then, and the presenters show nothing rather than let TextMeshPro open its importer.
        /// </summary>
        public static bool Available
        {
            get
            {
                Load();
                return fallback != null || display != null;
            }
        }

        public static TMP_FontAsset Display
        {
            get
            {
                Load();
                return display != null ? display : fallback;
            }
        }

        public static TMP_FontAsset Body
        {
            get
            {
                Load();
                return body != null ? body : fallback;
            }
        }

        /// <summary>True if the display face is the baked Unbounded (not the stand-in).</summary>
        public static bool HasDisplay
        {
            get
            {
                Load();
                return display != null;
            }
        }

        public static bool HasBody
        {
            get
            {
                Load();
                return body != null;
            }
        }

        public static TMP_FontAsset Font(UiFont font) => font == UiFont.Display ? Display : Body;

        /// <summary>
        /// The material preset for a face: the plain one, or the sticker style (display face only). Null with
        /// the stand-in font, which then keeps its own material.
        /// </summary>
        public static Material Preset(UiFont font, bool sticker = false)
        {
            Load();
            if (font == UiFont.Body) return body != null ? bodyPlain : null;
            if (display == null) return null;
            return sticker && displaySticker != null ? displaySticker : displayPlain;
        }

        /// <summary>Forgets what was loaded (tests, after the setup has baked new assets).</summary>
        public static void Reload() => loaded = false;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            display = Resources.Load<TMP_FontAsset>(folder + "/" + DisplayName);
            body = Resources.Load<TMP_FontAsset>(folder + "/" + BodyName);
            displaySticker = Resources.Load<Material>(folder + "/" + DisplayStickerPreset);
            displayPlain = Resources.Load<Material>(folder + "/" + DisplayPlainPreset);
            bodyPlain = Resources.Load<Material>(folder + "/" + BodyPlainPreset);
            // Not through TMP_Settings.instance: in the editor that opens the resource importer when the settings are missing.
            TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
            fallback = settings != null ? TMP_Settings.defaultFontAsset : null;
            if (settings == null)
            {
                // Without TextMeshPro's settings a text object cannot be made at all.
                display = null;
                body = null;
            }
        }

        static string BuildCharacters()
        {
            var builder = new StringBuilder(128);
            for (char c = (char)32; c <= 126; c++) builder.Append(c);
            builder.Append(ExtraCharacters);
            return builder.ToString();
        }
    }
}
