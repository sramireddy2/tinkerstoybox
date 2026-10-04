using TMPro;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>
    /// The small helpers every piece of the UI is built with: rect transforms placed by hand (no layout
    /// groups, so nothing rebuilds behind the code's back), atlas images and labels.
    /// </summary>
    public static class UiKit
    {
        public static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 TopLeft = new Vector2(0f, 1f), Top = new Vector2(0.5f, 1f), TopRight = new Vector2(1f, 1f);
        public static readonly Vector2 Left = new Vector2(0f, 0.5f), Right = new Vector2(1f, 0.5f);
        public static readonly Vector2 BottomLeft = new Vector2(0f, 0f), Bottom = new Vector2(0.5f, 0f), BottomRight = new Vector2(1f, 0f);

        /// <summary>An empty rect under a parent, on the UI layer.</summary>
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave, layer = UiTheme.Layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// Places a rect: anchored to a point of its parent (0..1), with its own pivot there plus an offset
        /// in reference pixels, at a fixed size.
        /// </summary>
        public static RectTransform Place(this RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Place(this RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size) => rect.Place(anchor, anchor, position, size);

        /// <summary>Fills the parent, inset by a margin on every side.</summary>
        public static RectTransform Fill(this RectTransform rect, float margin = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Centre;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
            return rect;
        }

        /// <summary>An atlas image. It does not take clicks unless asked to.</summary>
        public static Image Image(Transform parent, string name, UiShape shape, Color color, bool raycast = false)
        {
            RectTransform rect = Rect(parent, name);
            Image image = rect.gameObject.AddComponent<Image>();
            SetShape(image, shape);
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Image Image(Transform parent, string name, UiGlyph glyph, Color color)
        {
            RectTransform rect = Rect(parent, name);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiAtlas.Sprite(glyph);
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static void SetShape(Image image, UiShape shape)
        {
            image.sprite = UiAtlas.Sprite(shape);
            image.type = shape == UiShape.Panel || shape == UiShape.Pill ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        }

        /// <summary>
        /// A label in one of the two faces. Every label shrinks to fit its rect, down to 60% of its size
        /// (ART_BIBLE 10.1), and never takes clicks. <paramref name="sticker"/> asks for the die-cut text
        /// style (display face only).
        /// </summary>
        public static TextMeshProUGUI Label(Transform parent, string name, string text, UiFont font, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool wrap = false, bool sticker = false)
        {
            RectTransform rect = Rect(parent, name);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = UiFonts.Font(font);
            Material preset = UiFonts.Preset(font, sticker);
            if (preset != null) label.fontSharedMaterial = preset;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = size * UiTheme.AutoSizeFloor;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.richText = true;
            label.text = text ?? "";
            return label;
        }

        /// <summary>The face a button label is set in: display up to 12 characters, body beyond (10.1).</summary>
        public static UiFont ButtonFont(string text) => text != null && text.Length > UiTheme.DisplayLabelLimit ? UiFont.Body : UiFont.Display;

        public static void Destroy(Object target)
        {
            if (target != null) Sim.Destroy(target);
        }

        /// <summary>"0:42.3" below ten minutes, "12:05" from there on.</summary>
        public static string Time(float seconds, bool tenths = true)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            if (tenths && minutes < 10)
            {
                int whole = (int)rest;
                int tenth = Mathf.Min(9, (int)((rest - whole) * 10f));
                return minutes + ":" + whole.ToString("00") + "." + tenth;
            }
            return minutes + ":" + ((int)rest).ToString("00");
        }
    }
}
