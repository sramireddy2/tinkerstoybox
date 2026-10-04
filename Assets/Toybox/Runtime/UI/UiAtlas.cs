using System;
using Toybox.Art;
using UnityEngine;

namespace Toybox.UI
{
    /// <summary>The shapes of the UI's sprite atlas.</summary>
    public enum UiShape
    {
        /// <summary>A solid block, for rules, bars and flat fills.</summary>
        White,
        /// <summary>Rounded rectangle, radius 18, nine-sliced with a border of 24.</summary>
        Panel,
        /// <summary>A disc nine-sliced down the middle: a fully rounded pill at any size up to 64 high.</summary>
        Pill,
        Circle,
        /// <summary>A hollow circle (the "at grab" marker of the ruler).</summary>
        Ring,
        /// <summary>The four-pane mark: the window glint, the reticle, the "O" of the logo.</summary>
        FourPane,
        /// <summary>One reticle pane with its Ink edge baked in.</summary>
        Pane,
        /// <summary>The action figure: "figure for scale".</summary>
        Figure,
        /// <summary>The slot a blister card hangs by.</summary>
        HangSlot,
    }

    /// <summary>Silhouettes of the hero toys, for the catalogue's level cards.</summary>
    public enum UiGlyph
    {
        Block, Wedge, Thimble, Apple, Domino, Feather, Eraser, Pebble, Marble, Plank, GiftBox, Card, Key, Sponge, Doorway, Gear,
    }

    /// <summary>
    /// The UI's one sprite sheet (ART_BIBLE 10.3): a 256 x 256 RGBA32 texture drawn in code when it is first
    /// asked for, so every Image of the UI shares a texture and batches. Shapes are white with their
    /// coverage in alpha, anti-aliased from a signed distance function; Images tint them.
    /// </summary>
    public static class UiAtlas
    {
        public const int Size = 256;
        public const int PanelSize = 56, PanelBorder = 24;
        public const int PillSize = 64, PillBorder = 31;
        public const int GlyphSize = 40;
        const int GlyphColumns = 6;

        static Texture2D texture;
        static Sprite[] shapes;
        static Sprite[] glyphs;

        static readonly int ShapeCount = Enum.GetValues(typeof(UiShape)).Length;
        static readonly int GlyphCount = Enum.GetValues(typeof(UiGlyph)).Length;

        /// <summary>How many times the atlas has been drawn (once per session, unless it was released).</summary>
        public static int Builds { get; private set; }

        public static Texture2D Texture
        {
            get
            {
                Ensure();
                return texture;
            }
        }

        public static Sprite Sprite(UiShape shape)
        {
            Ensure();
            return shapes[(int)shape];
        }

        public static Sprite Sprite(UiGlyph glyph)
        {
            Ensure();
            return glyphs[(int)glyph];
        }

        /// <summary>Where a shape sits in the atlas, in pixels from the bottom left.</summary>
        public static RectInt Rect(UiShape shape)
        {
            switch (shape)
            {
                case UiShape.Pill:
                case UiShape.Circle: return new RectInt(2, 190, PillSize, PillSize);
                case UiShape.Panel: return new RectInt(70, 190, PanelSize, PanelSize);
                case UiShape.FourPane: return new RectInt(130, 190, 48, 48);
                case UiShape.Figure: return new RectInt(182, 190, 24, 48);
                case UiShape.Ring: return new RectInt(210, 190, 32, 32);
                case UiShape.Pane: return new RectInt(210, 226, 20, 20);
                case UiShape.White: return new RectInt(238, 230, 4, 4);
                default: return new RectInt(2, 170, 56, 16);   // HangSlot
            }
        }

        public static RectInt Rect(UiGlyph glyph)
        {
            int index = (int)glyph;
            return new RectInt(2 + index % GlyphColumns * (GlyphSize + 2), 2 + index / GlyphColumns * (GlyphSize + 2), GlyphSize, GlyphSize);
        }

        /// <summary>The hero toy of a level: by its slug, else by its place in the campaign, else a block.</summary>
        public static UiGlyph GlyphFor(string slug, int id)
        {
            switch (slug)
            {
                case "cheese-wedge": return UiGlyph.Wedge;
                case "thimble-chasm": return UiGlyph.Thimble;
                case "shrinking-apple": return UiGlyph.Apple;
                case "domino-effect": return UiGlyph.Domino;
                case "fan-feather": return UiGlyph.Feather;
                case "bouncing-eraser": return UiGlyph.Eraser;
                case "teeter-totter": return UiGlyph.Pebble;
                case "funnel-physics": return UiGlyph.Marble;
                case "moving-train": return UiGlyph.Plank;
                case "matryoshka-boxes": return UiGlyph.GiftBox;
                case "blocking-lasers": return UiGlyph.Card;
                case "the-keyhole": return UiGlyph.Key;
                case "fishbowl": return UiGlyph.Sponge;
                case "infinite-hallway": return UiGlyph.Doorway;
                case "rube-goldberg": return UiGlyph.Gear;
            }
            return id >= 1 && id <= 15 ? (UiGlyph)id : UiGlyph.Block;
        }

        /// <summary>Destroys the texture and the sprites (tests; the next use draws them again).</summary>
        public static void Release()
        {
            if (shapes != null)
                foreach (Sprite sprite in shapes) Destroy(sprite);
            if (glyphs != null)
                foreach (Sprite sprite in glyphs) Destroy(sprite);
            Destroy(texture);
            shapes = null;
            glyphs = null;
            texture = null;
        }

        static void Destroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        static void Ensure()
        {
            if (texture != null && shapes != null) return;
            Builds++;
            var pixels = new Color32[Size * Size];
            // White everywhere, so bilinear filtering at a shape's edge never pulls in a dark fringe.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);

            // The block is drawn larger than its sprite, so stretching it never samples the empty pixels around it.
            Paint(pixels, new RectInt(236, 228, 8, 8), p => -4f);
            Paint(pixels, Rect(UiShape.Circle), p => Circle(p, PillSize * 0.5f - 1f));
            Paint(pixels, Rect(UiShape.Panel), p => Box(p, new Vector2(PanelSize * 0.5f - 1f, PanelSize * 0.5f - 1f), UiTheme.PanelRadius));
            Paint(pixels, Rect(UiShape.FourPane), FourPane);
            Paint(pixels, Rect(UiShape.Figure), Figure);
            Paint(pixels, Rect(UiShape.Ring), p => Mathf.Abs(Circle(p, 11.5f)) - 2.5f);
            Paint(pixels, Rect(UiShape.HangSlot), p => Box(p, new Vector2(26f, 6f), 6f));
            PaintPane(pixels, Rect(UiShape.Pane));
            foreach (UiGlyph glyph in Enum.GetValues(typeof(UiGlyph)))
            {
                UiGlyph which = glyph;
                Paint(pixels, Rect(glyph), p => Glyph(which, p));
            }

            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = "Toybox UI Atlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            shapes = new Sprite[ShapeCount];
            for (int i = 0; i < ShapeCount; i++)
            {
                var shape = (UiShape)i;
                Vector4 border = Vector4.zero;
                if (shape == UiShape.Panel) border = Vector4.one * PanelBorder;
                else if (shape == UiShape.Pill) border = Vector4.one * PillBorder;
                shapes[i] = Make(shape.ToString(), Rect(shape), border);
            }
            glyphs = new Sprite[GlyphCount];
            for (int i = 0; i < GlyphCount; i++) glyphs[i] = Make(((UiGlyph)i).ToString(), Rect((UiGlyph)i), Vector4.zero);
        }

        static Sprite Make(string name, RectInt rect, Vector4 border)
        {
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(rect.x, rect.y, rect.width, rect.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // Coverage of a shape from its signed distance (negative inside), sampled at pixel centres: a
        // one-pixel ramp across the edge.
        static void Paint(Color32[] pixels, RectInt rect, Func<Vector2, float> distance)
        {
            Vector2 centre = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            for (int y = 0; y < rect.height; y++)
            {
                for (int x = 0; x < rect.width; x++)
                {
                    float d = distance(new Vector2(x + 0.5f, y + 0.5f) - centre);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[(rect.y + y) * Size + rect.x + x] = new Color32(255, 255, 255, alpha);
                }
            }
        }

        // The reticle pane keeps its colours: Paper inside an Ink edge one fifth of its width (3 px in 5).
        static void PaintPane(Color32[] pixels, RectInt rect)
        {
            Vector2 centre = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            float half = rect.width * 0.5f - 1f;
            float edge = half * 2f / 5f;
            Color paper = Palette.Paper, ink = Palette.Ink;
            for (int y = 0; y < rect.height; y++)
            {
                for (int x = 0; x < rect.width; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - centre;
                    float outer = Box(p, new Vector2(half, half), half * 0.45f);
                    float inner = Box(p, new Vector2(half - edge, half - edge), (half - edge) * 0.35f);
                    Color colour = Color.Lerp(ink, paper, Mathf.Clamp01(0.5f - inner));
                    colour.a = Mathf.Clamp01(0.5f - outer);
                    pixels[(rect.y + y) * Size + rect.x + x] = colour;
                }
            }
        }

        // ---- distance functions (pixels, y up, origin at the middle of the sprite) ----

        static float Circle(Vector2 p, float radius) => p.magnitude - radius;

        static float Box(Vector2 p, Vector2 half, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            Vector2 q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        // A convex polygon, corners in counter-clockwise order, rounded by `radius`.
        static float Polygon(Vector2 p, float radius, params Vector2[] corners)
        {
            float d = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % corners.Length];
                Vector2 edge = (b - a).normalized;
                var normal = new Vector2(edge.y, -edge.x);
                d = Mathf.Max(d, Vector2.Dot(p - a, normal) + radius);
            }
            return d - radius;
        }

        static Vector2 Rotate(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(p.x * c + p.y * s, -p.x * s + p.y * c);
        }

        static float Union(float a, float b) => Mathf.Min(a, b);
        static float Cut(float a, float hole) => Mathf.Max(a, -hole);

        static float FourPane(Vector2 p)
        {
            // Four panes of 19 with a gap of 6: 44 across in a 48 cell.
            var q = new Vector2(Mathf.Abs(p.x) - 12.5f, Mathf.Abs(p.y) - 12.5f);
            return Box(q, new Vector2(9.5f, 9.5f), 4f);
        }

        // 24 x 48: a head, a body with shoulders, two legs.
        static float Figure(Vector2 p)
        {
            float head = Circle(p - new Vector2(0f, 17f), 5.5f);
            float body = Polygon(p, 2.5f, new Vector2(-8.5f, 9.5f), new Vector2(-5.5f, -6f), new Vector2(5.5f, -6f), new Vector2(8.5f, 9.5f));
            float legs = Union(Segment(p, new Vector2(-3.4f, -6f), new Vector2(-4.2f, -20f), 2.6f), Segment(p, new Vector2(3.4f, -6f), new Vector2(4.2f, -20f), 2.6f));
            return Union(head, Union(body, legs));
        }

        static float Glyph(UiGlyph glyph, Vector2 p)
        {
            switch (glyph)
            {
                case UiGlyph.Wedge:
                {
                    float wedge = Polygon(p, 2f, new Vector2(-18f, -11f), new Vector2(18f, -11f), new Vector2(18f, 11f));
                    return Cut(Cut(wedge, Circle(p - new Vector2(10f, -3f), 3.2f)), Circle(p - new Vector2(0f, -6.5f), 2.2f));
                }
                case UiGlyph.Thimble:
                {
                    float cup = Polygon(p, 4f, new Vector2(-12.5f, -10f), new Vector2(12.5f, -10f), new Vector2(10f, 16f), new Vector2(-10f, 16f));
                    float rim = Box(p - new Vector2(0f, -12f), new Vector2(16f, 3.5f), 3f);
                    float dimples = Union(Circle(p - new Vector2(-5f, 5f), 1.8f), Union(Circle(p - new Vector2(5f, 5f), 1.8f), Circle(p - new Vector2(0f, -1f), 1.8f)));
                    return Cut(Union(cup, rim), dimples);
                }
                case UiGlyph.Apple:
                {
                    float fruit = Union(Circle(p - new Vector2(-5f, -3f), 11f), Circle(p - new Vector2(5f, -3f), 11f));
                    float stem = Segment(p, new Vector2(0f, 6f), new Vector2(2.5f, 14f), 1.7f);
                    float leaf = Segment(p, new Vector2(4.5f, 12f), new Vector2(11f, 15f), 2.8f);
                    return Union(fruit, Union(stem, leaf));
                }
                case UiGlyph.Domino:
                {
                    Vector2 q = Rotate(p, -12f);
                    float tile = Box(q, new Vector2(9f, 17f), 3f);
                    float line = Box(q, new Vector2(6f, 0.9f), 0.9f);
                    float pips = Union(Circle(q - new Vector2(0f, 8.5f), 2.4f), Union(Circle(q - new Vector2(-3.5f, -5.5f), 2f), Circle(q - new Vector2(3.5f, -11.5f), 2f)));
                    return Cut(Cut(tile, line), pips);
                }
                case UiGlyph.Feather:
                {
                    Vector2 q = Rotate(p, 40f);
                    float vane = Mathf.Max(Circle(q - new Vector2(-13f, 3f), 21f), Circle(q - new Vector2(13f, 3f), 21f));
                    float quill = Segment(q, new Vector2(0f, -19f), new Vector2(0f, 12f), 1.1f);
                    return Union(Cut(vane, Segment(q, new Vector2(0f, -13f), new Vector2(0f, 20f), 0.9f)), quill);
                }
                case UiGlyph.Eraser:
                {
                    Vector2 q = Rotate(p, 8f);
                    float slab = Polygon(q, 2f, new Vector2(-18f, -7f), new Vector2(18f, -7f), new Vector2(11f, 7f), new Vector2(-11f, 7f));
                    return Cut(slab, Box(q - new Vector2(3f, 0f), new Vector2(1.2f, 9f), 0f));
                }
                case UiGlyph.Pebble:
                {
                    float stone = Union(Circle(p - new Vector2(-4f, -2f), 11.5f), Union(Circle(p - new Vector2(6f, 1f), 10f), Circle(p - new Vector2(1f, 5f), 9f)));
                    return stone;
                }
                case UiGlyph.Marble:
                {
                    float ball = Circle(p, 15f);
                    float swirl = Mathf.Max(Circle(p - new Vector2(-2f, 2f), 9f), -Circle(p - new Vector2(1.5f, -1f), 8.5f));
                    return Cut(ball, swirl);
                }
                case UiGlyph.Plank:
                {
                    Vector2 q = Rotate(p, 24f);
                    float board = Box(q, new Vector2(19f, 4.5f), 1.5f);
                    return Cut(Cut(board, Circle(q - new Vector2(-14f, 0f), 1.5f)), Circle(q - new Vector2(14f, 0f), 1.5f));
                }
                case UiGlyph.GiftBox:
                {
                    float box = Box(p - new Vector2(0f, -5f), new Vector2(13f, 11f), 2f);
                    float lid = Box(p - new Vector2(0f, 6.5f), new Vector2(15.5f, 3.5f), 1.5f);
                    float ribbon = Box(p - new Vector2(0f, -3f), new Vector2(2f, 14f), 0f);
                    float bow = Union(Circle(p - new Vector2(-4.5f, 13.5f), 4f), Circle(p - new Vector2(4.5f, 13.5f), 4f));
                    return Union(Cut(Union(box, lid), ribbon), bow);
                }
                case UiGlyph.Card:
                {
                    Vector2 q = Rotate(p, 12f);
                    float card = Box(q, new Vector2(11.5f, 16.5f), 3f);
                    float pip = Mathf.Abs(q.x) * 1.25f + Mathf.Abs(q.y) - 7f;
                    return Cut(card, pip * 0.62f);
                }
                case UiGlyph.Key:
                {
                    Vector2 q = Rotate(p, -30f);
                    float bow = Cut(Circle(q - new Vector2(-10f, 0f), 8f), Circle(q - new Vector2(-10f, 0f), 3.4f));
                    float blade = Segment(q, new Vector2(-3f, 0f), new Vector2(17f, 0f), 2.2f);
                    float bits = Union(Box(q - new Vector2(9f, -4.5f), new Vector2(1.8f, 3.5f), 0.5f), Box(q - new Vector2(14.5f, -3.8f), new Vector2(1.8f, 2.8f), 0.5f));
                    return Union(bow, Union(blade, bits));
                }
                case UiGlyph.Sponge:
                {
                    float block = Box(p, new Vector2(17f, 11f), 4f);
                    float holes = Union(Circle(p - new Vector2(-9f, 4f), 2.6f), Union(Circle(p - new Vector2(2f, 5f), 1.8f),
                        Union(Circle(p - new Vector2(9f, -1f), 2.8f), Union(Circle(p - new Vector2(-3f, -4f), 2.2f), Circle(p - new Vector2(-11f, -5f), 1.5f)))));
                    return Cut(block, holes);
                }
                case UiGlyph.Doorway:
                {
                    float frame = Box(p, new Vector2(13f, 18f), 2f);
                    float opening = Box(p - new Vector2(0f, -3.5f), new Vector2(8f, 15.5f), 0f);
                    return Cut(frame, opening);
                }
                case UiGlyph.Gear:
                {
                    float wheel = Circle(p, 11.5f);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 q = Rotate(p, i * 45f);
                        wheel = Union(wheel, Box(q - new Vector2(0f, 13.5f), new Vector2(3f, 3.5f), 1f));
                    }
                    return Cut(wheel, Circle(p, 4.5f));
                }
                default:
                    return Box(p, new Vector2(14f, 14f), 4f);
            }
        }
    }
}
