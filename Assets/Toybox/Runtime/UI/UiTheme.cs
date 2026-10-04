using Toybox.Art;
using UnityEngine;

namespace Toybox.UI
{
    /// <summary>
    /// The numbers of the UI style guide (ART_BIBLE 10.2 - 10.4) in one place. Sizes are reference pixels
    /// of a 1920 x 1080 canvas that is scaled to the screen's height.
    /// </summary>
    public static class UiTheme
    {
        public const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;
        /// <summary>The UI layer: canvases and everything on them.</summary>
        public const int Layer = 5;

        // 10.2 sizes
        public const float LogoSize = 120f, NumberSize = 96f, TitleSize = 40f, ButtonSize = 24f, ReadoutSize = 22f, BodySize = 20f, SmallSize = 16f;
        /// <summary>Every label shrinks to fit, down to this share of its size (10.1).</summary>
        public const float AutoSizeFloor = 0.6f;
        /// <summary>Button labels up to this many characters are set in the display face, longer ones in the body face.</summary>
        public const int DisplayLabelLimit = 12;

        // 10.3 the sticker
        public const float BorderWidth = 5f;
        public static readonly Vector2 ShadowOffset = new Vector2(4f, -6f);
        public const float ShadowAlpha = 0.18f;
        public const float PanelRadius = 18f;

        // 10.4 motion
        public const float Fast = 0.09f, Medium = 0.18f, Slow = 0.32f;
        public const float StickScale = 0.9f, StickAngle = -3f, StickOvershoot = 1.56f;
        public static readonly Vector2 PressOffset = new Vector2(2f, -3f), PressShadow = new Vector2(2f, -3f);
        public static readonly Vector2 HoverOffset = new Vector2(-1f, 2f), HoverShadow = new Vector2(5f, -8f);

        // 10.2 colours (sRGB, as UGUI vertex colours want them)
        public static Color Paper => Palette.Paper;
        public static Color Ink => Palette.Ink;
        public static Color Primary => Palette.Cherry;
        public static Color Smaller => Palette.Lagoon;
        public static Color Larger => Palette.Cherry;
        public static Color Locked => Palette.Kraft;
        public static Color Shadow => Alpha(Palette.Ink, ShadowAlpha);
        /// <summary>Ink thinned out: rules, tracks, the hang-tab slot.</summary>
        public static Color Faint => Alpha(Palette.Ink, 0.22f);

        public static Color Alpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>Back-out easing with the style guide's overshoot: 0 at 0, 1 at 1, above 1 on the way.</summary>
        public static float BackOut(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + (StickOvershoot + 1f) * t * t * t + StickOvershoot * t * t;
        }

        public static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        public static float EaseOut(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t;
        }

        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
