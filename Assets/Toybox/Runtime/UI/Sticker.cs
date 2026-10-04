using UnityEngine;
using UnityEngine.UI;

namespace Toybox.UI
{
    public enum StickerShape
    {
        /// <summary>Rounded rectangle, radius 18.</summary>
        Panel,
        /// <summary>Fully rounded.</summary>
        Pill,
        Circle,
        /// <summary>A panel with a hang-tab slot at the top: a blister card.</summary>
        Card,
    }

    /// <summary>
    /// The one building block of the UI (ART_BIBLE 10.3): the same die-cut border and hard offset shadow
    /// the held toy wears. A sticker is three Images of one atlas shape -
    ///
    ///   1. the peel shadow: Ink at 18%, offset (4, -6), hard;
    ///   2. the die-cut border: Paper, 5 px larger on every side;
    ///   3. the face: Paper for panels, Ink for HUD pills, Cherry for the primary button, a dip tone for level cards
    ///
    /// - and a content rect on the face for whatever it carries. The root rect is the face's rect and is
    /// placed by whoever makes the sticker; the <see cref="UiTween"/> beside it moves an inner rect, so
    /// motion never fights layout.
    /// </summary>
    public sealed class Sticker : MonoBehaviour
    {
        Vector2 lift, shadowOffset = UiTheme.ShadowOffset;
        float radius = UiTheme.PanelRadius;

        public StickerShape Shape { get; private set; }
        /// <summary>The sticker's own rect: the face. Position and size it; do not animate it.</summary>
        public RectTransform Rect { get; private set; }
        /// <summary>What the tween scales, turns and peels: everything of the sticker, hinged at the top edge.</summary>
        public RectTransform Motion { get; private set; }
        /// <summary>Border, face and content: what a hover lifts and a press pushes down.</summary>
        public RectTransform Body { get; private set; }
        /// <summary>Fills the face; labels and icons go here.</summary>
        public RectTransform Content { get; private set; }
        public Image Shadow { get; private set; }
        public Image Border { get; private set; }
        public Image Face { get; private set; }
        /// <summary>The hang-tab slot of a card, else null.</summary>
        public Image Slot { get; private set; }
        public CanvasGroup Group { get; private set; }
        public UiTween Tween { get; private set; }

        public Vector2 Size
        {
            get => Rect.sizeDelta;
            set
            {
                Rect.sizeDelta = value;
                Layout();
            }
        }

        /// <summary>Corner radius of a panel or card (18 unless set); pills and circles are always fully round.</summary>
        public float Radius
        {
            get => radius;
            set
            {
                radius = Mathf.Max(1f, value);
                Layout();
            }
        }

        /// <summary>Where the body sits relative to its rest position (hover lifts it, a press pushes it down).</summary>
        public Vector2 Lift
        {
            get => lift;
            set
            {
                lift = value;
                Body.anchoredPosition = value;
            }
        }

        /// <summary>Where the peel shadow lies relative to the sticker's rest position.</summary>
        public Vector2 ShadowOffset
        {
            get => shadowOffset;
            set
            {
                shadowOffset = value;
                Shadow.rectTransform.anchoredPosition = value;
            }
        }

        public Color FaceColor
        {
            get => Face.color;
            set => Face.color = value;
        }

        public Color BorderColor
        {
            get => Border.color;
            set => Border.color = value;
        }

        public float Alpha
        {
            get => Group.alpha;
            set => Group.alpha = value;
        }

        /// <summary>
        /// A sticker under <paramref name="parent"/>, anchored to its middle until it is placed. It starts
        /// shown; call <c>Tween.Hide(true)</c> for one that should stick on later.
        /// </summary>
        public static Sticker Create(Transform parent, string name, StickerShape shape, Color face, Vector2 size)
        {
            RectTransform rect = UiKit.Rect(parent, name);
            rect.Place(UiKit.Centre, Vector2.zero, size);
            Sticker sticker = rect.gameObject.AddComponent<Sticker>();
            sticker.Build(shape, face);
            return sticker;
        }

        void Build(StickerShape shape, Color face)
        {
            Shape = shape;
            Rect = (RectTransform)transform;
            Group = gameObject.AddComponent<CanvasGroup>();
            Group.blocksRaycasts = true;
            Group.interactable = true;

            Motion = UiKit.Rect(Rect, "Motion");
            Motion.anchorMin = Vector2.zero;
            Motion.anchorMax = Vector2.one;
            Motion.pivot = UiKit.Top;
            Motion.offsetMin = Vector2.zero;
            Motion.offsetMax = Vector2.zero;

            UiShape sprite = shape == StickerShape.Pill ? UiShape.Pill : shape == StickerShape.Circle ? UiShape.Circle : UiShape.Panel;
            Shadow = UiKit.Image(Motion, "Shadow", sprite, UiTheme.Shadow);
            Shadow.rectTransform.Fill(-UiTheme.BorderWidth);
            Shadow.rectTransform.anchoredPosition = shadowOffset;

            Body = UiKit.Rect(Motion, "Body");
            Body.Fill();
            Border = UiKit.Image(Body, "Border", sprite, UiTheme.Paper);
            Border.rectTransform.Fill(-UiTheme.BorderWidth);
            Face = UiKit.Image(Body, "Face", sprite, face);
            Face.rectTransform.Fill();
            if (shape == StickerShape.Card)
            {
                Slot = UiKit.Image(Body, "Slot", UiShape.HangSlot, UiTheme.Faint);
                Slot.rectTransform.Place(UiKit.Top, new Vector2(0f, -14f), new Vector2(56f, 16f));
            }
            Content = UiKit.Rect(Body, "Content");
            Content.Fill();

            Tween = gameObject.AddComponent<UiTween>();
            Tween.Bind(this);
            Layout();
        }

        // Sliced corners come out at the sprite's own radius; the multiplier scales them to the one wanted.
        void Layout()
        {
            float height = Rect.sizeDelta.y;
            if (Shape == StickerShape.Pill)
            {
                Face.pixelsPerUnitMultiplier = Multiplier(height);
                float outer = Multiplier(height + UiTheme.BorderWidth * 2f);
                Border.pixelsPerUnitMultiplier = outer;
                Shadow.pixelsPerUnitMultiplier = outer;
            }
            else if (Shape == StickerShape.Panel || Shape == StickerShape.Card)
            {
                Face.pixelsPerUnitMultiplier = UiTheme.PanelRadius / radius;
                float outer = UiTheme.PanelRadius / (radius + UiTheme.BorderWidth);
                Border.pixelsPerUnitMultiplier = outer;
                Shadow.pixelsPerUnitMultiplier = outer;
            }
        }

        static float Multiplier(float height) => UiAtlas.PillBorder * 2f / Mathf.Max(1f, height);
    }
}
