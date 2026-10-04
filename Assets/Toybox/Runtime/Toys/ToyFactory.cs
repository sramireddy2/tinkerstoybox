using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// One factory per toy of the campaign (LEVELS.md section 3). Each returns a toy: a GameObject with
    /// renderers and colliders, authored at scale 1, no Rigidbody - pass it to LevelContext.AddProp with
    /// the options from <see cref="ToyCatalog"/>, or use <see cref="ToyCatalog.Add"/>, which does both.
    ///
    /// The origin is the centre of the collider bounds unless the factory says otherwise. Visuals follow
    /// ART_BIBLE 4.1: bevelled edges, outline normals, object-space UVs, at most three draws and 3,000
    /// triangles; meshes are shared by all toys of a kind. Colliders are boxes, spheres and convex
    /// hulls; parts that are only looks (stems, bows, pips, wires) have none.
    ///
    /// A colour is a candy colour (Palette.Cherry ...); left out, the toy takes its own. Factories with
    /// a <c>grabbable</c> parameter also make the set-piece version of the shape: a plain prop in Birch
    /// (or the tone passed as the colour) without rim or pool, which the player reads as not theirs.
    /// </summary>
    public static partial class ToyFactory
    {
        static float Least(Vector3 size) => Mathf.Min(size.x, Mathf.Min(size.y, size.z));

        static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);   // an XY outline laid into the XZ plane (outline y -> z)

        // ---------------------------------------------------------------------------------------------------
        // Wooden block
        // ---------------------------------------------------------------------------------------------------

        /// <summary>A painted building block with a shape on every face. Box collider.</summary>
        public static GameObject WoodenBlock(Vector3 size, Color? color = null, bool grabbable = true)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Cherry, color, grabbable);
            var kit = new ToyKit("Wooden Block", MeshKit.Key("WoodenBlock", size.x, size.y, size.z) + look.Hex);
            kit.Box(size);
            kit.Visual("Visual", look.Main, () =>
            {
                Mesh body = MeshKit.RoundedBox(size, Least(size) * 0.07f);
                ToyGeo.Chip(body, look.Color, ToyGeo.Seed("block"));
                return new[] { ToyKit.At(body, Vector3.zero) };
            });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var parts = new List<MeshPart>();
                float height = Least(size) * 0.012f;
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    float radius = Mathf.Min(size[u], size[v]) * 0.27f;
                    // A circle, a diamond and a triangle: one pair of faces each.
                    int sides = axis == 0 ? 20 : axis == 1 ? 4 : 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 normal = Vector3.zero;
                        normal[axis] = sign;
                        parts.Add(ToyKit.Facing(ToyGeo.Dot(radius, sides, height), normal * (size[axis] * 0.5f), normal));
                    }
                }
                return parts;
            });
            return kit.Finish(look);
        }

        public static GameObject WoodenBlock(float size = 1f, Color? color = null, bool grabbable = true) =>
            WoodenBlock(new Vector3(size, size, size), color, grabbable);

        // ---------------------------------------------------------------------------------------------------
        // Cheese wedge
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 CheeseWedgeSize = new Vector3(0.8f, 0.5f, 1f);

        /// <summary>
        /// A wedge of plastic cheese: 0.8 wide (X), rising from a knife edge at -Z to 0.5 at +Z, 1.0 long.
        /// One convex collider with six corners; the holes are looks.
        /// </summary>
        public static GameObject CheeseWedge(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.GlossyPlastic, Palette.Lemon, color);
            var kit = new ToyKit("Cheese Wedge", "CheeseWedge");
            Vector3 half = CheeseWedgeSize * 0.5f;
            kit.Hull("Hull", () => new[]
            {
                new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
                new Vector3(-half.x, -half.y, half.z), new Vector3(half.x, -half.y, half.z),
                new Vector3(-half.x, half.y, half.z), new Vector3(half.x, half.y, half.z),
            });
            kit.Visual("Visual", look.Main, () =>
            {
                // The side of the wedge (z, y), its corners rounded - the thin one hardly: it is the knife edge
                // the player walks onto - then given its width along X.
                var side = new List<Vector2> { new Vector2(-half.z, -half.y), new Vector2(half.z, -half.y), new Vector2(half.z, half.y) };
                Mesh body = MeshKit.Extrude(ToyGeo.Fillet(side, 0.022f, 4, true), CheeseWedgeSize.x, 0.02f);
                return new[] { ToyKit.At(body, Vector3.zero, Quaternion.Euler(0f, -90f, 0f)) };
            });
            kit.Visual("Detail", look.Shade(0.42f), () =>
            {
                var parts = new List<MeshPart>();
                const float proud = 0.0015f;
                // On the slope: (how far up it, x, radius).
                Vector3 slopeNormal = new Vector3(0f, 1f, -0.5f).normalized;
                var slope = new[]
                {
                    new Vector3(0.30f, -0.18f, 0.07f), new Vector3(0.52f, 0.14f, 0.10f), new Vector3(0.74f, -0.12f, 0.085f),
                    new Vector3(0.86f, 0.22f, 0.05f), new Vector3(0.22f, 0.2f, 0.04f), new Vector3(0.62f, -0.28f, 0.035f),
                };
                foreach (Vector3 hole in slope)
                {
                    var on = new Vector3(hole.y, -half.y + CheeseWedgeSize.y * hole.x, -half.z + CheeseWedgeSize.z * hole.x);
                    parts.Add(ToyKit.Facing(ToyGeo.Dot(hole.z, 18, proud), on, slopeNormal));
                }
                // On the two sides: (z, y, radius).
                var side = new[] { new Vector3(0.25f, -0.05f, 0.075f), new Vector3(0.03f, -0.15f, 0.045f), new Vector3(0.39f, 0.07f, 0.04f), new Vector3(0.36f, -0.17f, 0.03f) };
                foreach (Vector3 hole in side)
                {
                    parts.Add(ToyKit.Facing(ToyGeo.Dot(hole.z, 18, proud), new Vector3(half.x, hole.y, hole.x), Vector3.right));
                    parts.Add(ToyKit.Facing(ToyGeo.Dot(hole.z * 0.85f, 18, proud), new Vector3(-half.x, hole.y - 0.01f, hole.x - 0.03f), Vector3.left));
                }
                // On the tall face: (x, y, radius).
                var back = new[] { new Vector3(-0.15f, 0.05f, 0.08f), new Vector3(0.2f, -0.08f, 0.055f), new Vector3(0.12f, 0.13f, 0.03f) };
                foreach (Vector3 hole in back)
                    parts.Add(ToyKit.Facing(ToyGeo.Dot(hole.z, 18, proud), new Vector3(hole.x, hole.y, half.z), Vector3.forward));
                return parts;
            });
            return kit.Finish(look);
        }

        // ---------------------------------------------------------------------------------------------------
        // Dominoes
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 DominoSize = new Vector3(1f, 2f, 0.3f);

        /// <summary>The Level 4 domino: 1.0 x 2.0 x 0.3, standing, pips toward -Z. Box collider.</summary>
        public static GameObject Domino(Color? color = null, bool grabbable = true) => DominoPiece(DominoSize.y, color, grabbable);

        /// <summary>
        /// A domino of any height: height x 0.5 height x 0.15 height (Y, X, Z), standing, pips toward -Z;
        /// it falls toward +Z. The pieces of <see cref="DominoSet"/> are these.
        /// </summary>
        public static GameObject DominoPiece(float height, Color? color = null, bool grabbable = true, int upperPips = 3, int lowerPips = 2, bool sail = false)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Grape, color, grabbable);
            var kit = new ToyKit("Domino", MeshKit.Key("Domino", height, upperPips, lowerPips, sail ? 1f : 0f) + look.Hex);
            kit.Box(DominoPieceSize(height));
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(DominoBody(height, look.Color, 2), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var parts = new List<MeshPart>();
                DominoMarks(parts, height, Matrix4x4.identity, upperPips, lowerPips, sail, true);
                return parts;
            });
            return kit.Finish(look);
        }

        /// <summary>Size of a domino piece of this height: (0.5 h, h, 0.15 h).</summary>
        public static Vector3 DominoPieceSize(float height) => new Vector3(0.5f * height, height, 0.15f * height);

        static Mesh DominoBody(float h, Color paint, int segments)
        {
            Mesh body = MeshKit.RoundedBox(DominoPieceSize(h), 0.014f * h, segments);
            ToyGeo.Chip(body, paint, ToyGeo.Seed("domino"));
            return body;
        }

        // The divider and the pips on the -Z face of a piece, and the paper sail on top of it.
        static void DominoMarks(List<MeshPart> parts, float h, Matrix4x4 pose, int upper, int lower, bool sail, bool fine)
        {
            float face = -0.075f * h;
            var bar = new Vector3(0.4f * h, 0.018f * h, 0.008f * h);
            parts.Add(new MeshPart(fine ? MeshKit.RoundedBox(bar, 0.003f * h, 1) : MeshKit.Box(bar), pose * Matrix4x4.Translate(new Vector3(0f, 0f, face))));
            Pips(parts, h, pose, upper, 0.25f * h, face, fine);
            Pips(parts, h, pose, lower, -0.25f * h, face, fine);
            if (!sail) return;
            // A paper sail on a mast: it faces along the piece's thickness, which is the way it falls.
            float top = 0.5f * h;
            var mast = new List<Vector3> { new Vector3(0f, top - 0.02f * h, 0f), new Vector3(0f, top + 0.8f * h, 0f) };
            parts.Add(new MeshPart(ToyGeo.Tube(mast, 0.018f * h, 0.018f * h, 5, Vector3.right, true), pose));
            var outline = new List<Vector2> { new Vector2(-0.3f * h, 0.1f * h), new Vector2(0.3f * h, 0.1f * h), new Vector2(0f, 0.78f * h) };
            parts.Add(new MeshPart(MeshKit.Extrude(ToyGeo.Fillet(outline, 0.03f * h, 2, true), 0.012f * h, 0.003f * h), pose * Matrix4x4.Translate(new Vector3(0f, top, 0.02f * h))));
        }

        static void Pips(List<MeshPart> parts, float h, Matrix4x4 pose, int count, float y, float face, bool fine)
        {
            float a = 0.125f * h;
            float radius = 0.042f * h;
            foreach (Vector2 at in PipLayout(count))
            {
                var position = new Vector3(at.x * a, y + at.y * a, face);
                parts.Add(new MeshPart(ToyGeo.Dot(radius, fine ? 12 : 8, 0.005f * h), pose * Matrix4x4.TRS(position, Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one)));
            }
        }

        // Where the pips of one half go, in units of the pip pitch.
        static Vector2[] PipLayout(int count)
        {
            switch (Mathf.Clamp(count, 0, 6))
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1f, 1f), new Vector2(1f, -1f) };
                case 3: return new[] { new Vector2(-1f, 1f), Vector2.zero, new Vector2(1f, -1f) };
                case 4: return new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
                case 5: return new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-1f, -1f), new Vector2(1f, -1f) };
                case 6: return new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
                default: return new Vector2[0];
            }
        }

        // ---- The domino set of Level 15 ---------------------------------------------------------------------

        public static readonly Vector3 DominoSetSize = new Vector3(3.2f, 1.5f, 0.8f);
        public const float DominoSetStripThickness = 0.05f;
        /// <summary>Name of the child that holds the seven pieces (their colliders and their visuals).</summary>
        public const string DominoSetPieces = "Pieces";
        public const int DominoSetCount = 7;

        static readonly float[] SetHeights = { 0.30f, 0.39f, 0.51f, 0.66f, 0.86f, 1.11f, 1.45f };
        static readonly float[] SetNearX = { 0.15f, 0.36f, 0.63f, 0.99f, 1.45f, 2.05f, 2.83f };

        /// <summary>Height of piece <paramref name="index"/> (0..6, smallest first) at scale 1.</summary>
        public static float DominoSetHeight(int index) => SetHeights[index];

        /// <summary>Centre of a piece in the set's own space at scale 1. The strip's small end is at x = -1.6.</summary>
        public static Vector3 DominoSetPieceCenter(int index)
        {
            float h = SetHeights[index];
            return new Vector3(SetNearX[index] - DominoSetSize.x * 0.5f + 0.075f * h, DominoSetBaseY + h * 0.5f, 0f);
        }

        /// <summary>
        /// A point of the hinge line of a piece in the set's own space at scale 1: the bottom edge of its
        /// far (+X) face, which runs along Z. The piece falls toward +X about it.
        /// </summary>
        public static Vector3 DominoSetHinge(int index) =>
            new Vector3(SetNearX[index] - DominoSetSize.x * 0.5f + 0.15f * SetHeights[index], DominoSetBaseY, 0f);

        /// <summary>The pose of a piece in the set: a <see cref="DominoPiece"/> turned so that it falls toward +X.</summary>
        public static Quaternion DominoSetPieceRotation => Quaternion.Euler(0f, 90f, 0f);

        /// <summary>Height of the strip's top, where the pieces stand, in the set's own space.</summary>
        public static float DominoSetBaseY => -DominoSetSize.y * 0.5f + DominoSetStripThickness;

        /// <summary>The pips of a piece of the set: (upper, lower).</summary>
        public static Vector2Int DominoSetPips(int index) => new Vector2Int(index % 6 + 1, (index + 2) % 6 + 1);

        /// <summary>
        /// A strip (3.2 x 0.05 x 0.8) carrying seven dominoes of growing height, small end toward -X; the
        /// first carries a paper sail. Eight box colliders. The origin is the centre of its 3.2 x 1.5 x 0.8
        /// bounds. The pieces - one child with a box collider each, plus their merged visuals - live under
        /// the child <see cref="DominoSetPieces"/>, so a gadget that knocks them over deactivates that
        /// child and puts bodies of its own (<see cref="DominoPiece"/>) in their place.
        /// </summary>
        public static GameObject DominoSet(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Lime, color);
            var kit = new ToyKit("Domino Set", "DominoSet" + look.Hex);
            var strip = new Vector3(DominoSetSize.x, DominoSetStripThickness, DominoSetSize.z);
            Vector3 stripCentre = new Vector3(0f, -DominoSetSize.y * 0.5f + DominoSetStripThickness * 0.5f, 0f);
            kit.Box(strip, stripCentre);
            kit.Visual("Strip", look.Main, () =>
            {
                Mesh mesh = MeshKit.RoundedBox(strip, 0.014f);
                ToyGeo.Chip(mesh, look.Color, ToyGeo.Seed("strip"));
                return new[] { ToyKit.At(mesh, stripCentre) };
            });

            Transform pieces = kit.Child(DominoSetPieces);
            for (int i = 0; i < DominoSetCount; i++)
            {
                Transform piece = kit.Child("Piece " + (i + 1), DominoSetPieceCenter(i), pieces);
                piece.localRotation = DominoSetPieceRotation;
                kit.Box(DominoPieceSize(SetHeights[i]), Vector3.zero, piece);
            }
            kit.Visual("Visual", look.Main, () =>
            {
                var parts = new List<MeshPart>();
                for (int i = 0; i < DominoSetCount; i++)
                    parts.Add(ToyKit.At(DominoBody(SetHeights[i], look.Color, 1), DominoSetPieceCenter(i), DominoSetPieceRotation));
                return parts;
            }, pieces);
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var parts = new List<MeshPart>();
                for (int i = 0; i < DominoSetCount; i++)
                {
                    Vector2Int pips = DominoSetPips(i);
                    DominoMarks(parts, SetHeights[i], Matrix4x4.TRS(DominoSetPieceCenter(i), DominoSetPieceRotation, Vector3.one), pips.x, pips.y, i == 0, false);
                }
                return parts;
            }, pieces);
            return kit.Finish(look, new Vector4(-1.05f, -0.6f, 0f, 0.5f), new Vector4(0f, -0.5f, 0f, 0.5f), new Vector4(1.05f, -0.3f, 0f, 0.55f));
        }

        // ---------------------------------------------------------------------------------------------------
        // Plank and rulers
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 PlankSize = new Vector3(0.6f, 0.12f, 3f);

        /// <summary>The Level 9 plank: 0.6 x 0.12 x 3.0, long axis Z. Box collider.</summary>
        public static GameObject Plank(Color? color = null, bool grabbable = true) => Plank(PlankSize, color, grabbable);

        /// <summary>A painted board with a nail in each corner; the long axis is the longer of X and Z.</summary>
        public static GameObject Plank(Vector3 size, Color? color = null, bool grabbable = true)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Lemon, color, grabbable);
            var kit = new ToyKit("Plank", MeshKit.Key("Plank", size.x, size.y, size.z) + look.Hex);
            kit.Box(size);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(Board(size, look.Color, "plank"), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Ink), () =>
            {
                var parts = new List<MeshPart>();
                bool alongZ = size.z >= size.x;
                float length = alongZ ? size.z : size.x, width = alongZ ? size.x : size.z;
                float radius = Mathf.Min(width * 0.07f, length * 0.03f);
                for (int end = -1; end <= 1; end += 2)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float a = end * (length * 0.5f - Mathf.Min(width * 0.35f, length * 0.2f)), b = side * width * 0.27f;
                        var on = new Vector3(alongZ ? b : a, size.y * 0.5f, alongZ ? a : b);
                        parts.Add(ToyKit.At(ToyGeo.Dot(radius, 10, radius * 0.25f), on));
                    }
                return parts;
            });
            return LongToy(kit, look, size);
        }

        static Mesh Board(Vector3 size, Color paint, string seed)
        {
            Mesh mesh = MeshKit.RoundedBox(size, Least(size) * 0.12f);
            ToyGeo.Chip(mesh, paint, ToyGeo.Seed(seed));
            return mesh;
        }

        // A long toy gets three pool spheres along its length instead of one big one (ART_BIBLE 9.4).
        static GameObject LongToy(ToyKit kit, ToyLook look, Vector3 size)
        {
            bool alongZ = size.z >= size.x;
            float length = alongZ ? size.z : size.x, width = alongZ ? size.x : size.z;
            if (length < width * 3f) return kit.Finish(look);
            float radius = Mathf.Max(width * 0.7f, length / 6f);
            Vector3 step = (alongZ ? Vector3.forward : Vector3.right) * (length / 3f);
            return kit.Finish(look, Sphere(-step, radius), Sphere(Vector3.zero, radius), Sphere(step, radius));
        }

        static Vector4 Sphere(Vector3 centre, float radius) => new Vector4(centre.x, centre.y, centre.z, radius);

        /// <summary>
        /// A ruler: a painted board (long axis Z) with tick marks along its +X edge. Box collider. The
        /// Level 7 seesaw plank is this shape; <see cref="CatapultRuler"/> adds the cap.
        /// </summary>
        public static GameObject Ruler(Vector3 size, Color? color = null, bool grabbable = true)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Lemon, color, grabbable);
            var kit = new ToyKit("Ruler", MeshKit.Key("Ruler", size.x, size.y, size.z) + look.Hex);
            kit.Box(size);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(Board(size, look.Color, "ruler"), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Ink), () => RulerTicks(size));
            return LongToy(kit, look, size);
        }

        static List<MeshPart> RulerTicks(Vector3 size)
        {
            var parts = new List<MeshPart>();
            int count = Mathf.Clamp(Mathf.RoundToInt(size.z / 0.125f), 8, 48);
            float spacing = size.z / count;
            float thickness = Mathf.Max(0.003f, size.y * 0.04f);
            for (int i = 1; i < count; i++)
            {
                float length = size.x * (i % 4 == 0 ? 0.36f : 0.2f);
                var tick = new Vector3(length, thickness, spacing * 0.16f);
                var on = new Vector3(size.x * 0.5f - size.x * 0.06f - length * 0.5f, size.y * 0.5f + thickness * 0.2f, -size.z * 0.5f + i * spacing);
                parts.Add(ToyKit.At(MeshKit.Box(tick), on));
            }
            return parts;
        }

        public static readonly Vector3 CatapultRulerPlank = new Vector3(0.6f, 0.08f, 4f);
        public const float CatapultCapRadius = 0.3f, CatapultCapHeight = 0.2f, CatapultMarbleRadius = 0.15f;
        /// <summary>Centre of the cap's floor in the ruler's own space: on top of the plank, at its +Z end.</summary>
        public static readonly Vector3 CatapultCapSeat = new Vector3(0f, 0.04f, 1.65f);
        /// <summary>Name of the child that draws the marble in the cap; a gadget that shoots it hides this.</summary>
        public const string CatapultMarble = "Marble";

        /// <summary>Where the marble sits, in the ruler's own space at scale 1.</summary>
        public static Vector3 CatapultMarbleCenter => CatapultCapSeat + new Vector3(0f, 0.03f + CatapultMarbleRadius, 0f);

        /// <summary>
        /// The Level 15 ruler: a plank 0.6 x 0.08 x 4.0 with a bottle cap (radius 0.3) on its +Z end holding
        /// a marble. A box and a short convex cylinder; the marble is a look. The origin is the centre of
        /// the plank, so a ruler lying on a floor has its origin 0.04 above it.
        /// </summary>
        public static GameObject CatapultRuler(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Lemon, color);
            var kit = new ToyKit("Catapult Ruler", "CatapultRuler" + look.Hex);
            Vector3 plank = CatapultRulerPlank;
            kit.Box(plank);
            kit.Hull("Cap", () =>
            {
                List<Vector3> points = ToyHull.Frustum(CatapultCapRadius, CatapultCapRadius, CatapultCapSeat.y, CatapultCapSeat.y + CatapultCapHeight, 12);
                for (int i = 0; i < points.Count; i++) points[i] += new Vector3(0f, 0f, CatapultCapSeat.z);
                return points;
            });
            kit.Visual("Visual", look.Main, () =>
            {
                // The cap is a cup: floor, wall with a flared rim, open top.
                var profile = new List<Vector2>
                {
                    new Vector2(0f, 0.03f), new Vector2(0.232f, 0.03f), new Vector2(0.262f, 0.06f), new Vector2(0.27f, 0.186f),
                    new Vector2(0.286f, 0.2f), new Vector2(0.3f, 0.19f), new Vector2(0.29f, 0.15f), new Vector2(0.276f, 0.02f),
                    new Vector2(0.262f, 0f), new Vector2(0f, 0f),
                };
                return new[]
                {
                    ToyKit.At(Board(plank, look.Color, "ruler"), Vector3.zero),
                    ToyKit.At(MeshKit.Lathe(ToyGeo.Fillet(profile, 0.01f, 2), 24), CatapultCapSeat),
                };
            });
            kit.Visual("Detail", look.Detail(Palette.Ink), () => RulerTicks(new Vector3(plank.x, plank.y, plank.z - 0.8f)).ConvertAll(
                part => new MeshPart(part.Mesh, Matrix4x4.Translate(new Vector3(0f, 0f, -0.4f)) * part.Matrix)));
            kit.Visual(CatapultMarble, Materials.Toy(ToyRecipe.Glass, Palette.Paper), () => new[]
            {
                ToyKit.At(MeshKit.Sphere(CatapultMarbleRadius, 20, 12), CatapultMarbleCenter),
            });
            return kit.Finish(look, Sphere(new Vector3(0f, 0f, -1.35f), 0.7f), Sphere(Vector3.zero, 0.7f), Sphere(new Vector3(0f, 0f, 1.35f), 0.7f));
        }

        // ---------------------------------------------------------------------------------------------------
        // Gift box
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// A wrapped present, 1 x 1 x 1: a solid cube (one box collider) with a ribbon and a bow, which are
        /// looks. The matryoshka boxes of Level 10 are three of these, revealed one inside the other.
        /// </summary>
        public static GameObject GiftBox(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Cardboard, Palette.Cherry, color);
            var kit = new ToyKit("Gift Box", "GiftBox");
            kit.Box(Vector3.one);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(MeshKit.RoundedBox(Vector3.one, 0.045f), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Paper, ToyRecipe.TapeStrip), () =>
            {
                const float proud = 1.012f, band = 0.17f;
                var parts = new List<MeshPart>
                {
                    ToyKit.At(MeshKit.RoundedBox(new Vector3(band, proud, proud), 0.051f), Vector3.zero),
                    ToyKit.At(MeshKit.RoundedBox(new Vector3(proud, proud, band), 0.051f), Vector3.zero),
                    ToyKit.At(MeshKit.RoundedBox(new Vector3(0.13f, 0.08f, 0.13f), 0.03f), new Vector3(0f, 0.53f, 0f)),
                };
                // Two loops of the bow, each a flat band that leaves the knot and comes back to it.
                for (int side = -1; side <= 1; side += 2)
                {
                    var loop = new List<Vector3>();
                    for (int i = 0; i <= 12; i++)
                    {
                        float angle = Mathf.PI * 2f * i / 12f;
                        loop.Add(new Vector3(side * 0.14f * (1f - Mathf.Cos(angle)), 0.535f + 0.085f * Mathf.Sin(angle), 0f));
                    }
                    parts.Add(ToyKit.At(ToyGeo.Tube(loop, 0.008f, 0.055f, 6, Vector3.right * side), Vector3.zero));
                    // And a tail lying on the lid.
                    var tail = new List<Vector3>
                    {
                        new Vector3(0f, 0.515f, side * 0.04f), new Vector3(side * 0.03f, 0.512f, side * 0.16f),
                        new Vector3(side * 0.09f, 0.512f, side * 0.27f), new Vector3(side * 0.11f, 0.512f, side * 0.36f),
                    };
                    parts.Add(ToyKit.At(ToyGeo.Tube(tail, 0.006f, 0.045f, 6, Vector3.up, true), Vector3.zero));
                }
                return parts;
            });
            return kit.Finish(look);
        }

        // ---------------------------------------------------------------------------------------------------
        // Doorway
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 DoorwaySize = new Vector3(1.6f, 2.5f, 0.3f);
        /// <summary>Width and height of the opening.</summary>
        public static readonly Vector2 DoorwayOpening = new Vector2(1.2f, 2.2f);

        /// <summary>
        /// A door frame, 1.6 x 2.5 x 0.3 with an opening of 1.2 x 2.2. The origin is the middle of the
        /// threshold, on the floor. Three boxes (two posts and the lintel) that never collide with the
        /// player's capsule (Collider.excludeLayers); queries and the hold march see them like any prop.
        /// </summary>
        public static GameObject Doorway(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Lime, color);
            var kit = new ToyKit("Doorway", "Doorway" + look.Hex);
            float post = (DoorwaySize.x - DoorwayOpening.x) * 0.5f, lintel = DoorwaySize.y - DoorwayOpening.y, depth = DoorwaySize.z;
            float postX = (DoorwayOpening.x + post) * 0.5f, lintelY = DoorwayOpening.y + lintel * 0.5f;
            var postSize = new Vector3(post, DoorwayOpening.y, depth);
            var lintelSize = new Vector3(DoorwaySize.x, lintel, depth);
            Collider[] colliders =
            {
                kit.Box(postSize, new Vector3(-postX, DoorwayOpening.y * 0.5f, 0f)),
                kit.Box(postSize, new Vector3(postX, DoorwayOpening.y * 0.5f, 0f)),
                kit.Box(lintelSize, new Vector3(0f, lintelY, 0f)),
            };
            foreach (Collider collider in colliders) collider.excludeLayers = Layers.PlayerMask;

            kit.Visual("Visual", look.Main, () =>
            {
                var parts = new List<MeshPart>();
                for (int side = -1; side <= 1; side += 2)
                {
                    Mesh mesh = MeshKit.RoundedBox(postSize, 0.018f);
                    ToyGeo.Chip(mesh, look.Color, ToyGeo.Seed("post" + side));
                    parts.Add(ToyKit.At(mesh, new Vector3(side * postX, DoorwayOpening.y * 0.5f, 0f)));
                }
                Mesh top = MeshKit.RoundedBox(lintelSize, 0.018f);
                ToyGeo.Chip(top, look.Color, ToyGeo.Seed("lintel"));
                parts.Add(ToyKit.At(top, new Vector3(0f, lintelY, 0f)));
                // The threshold is a look: the origin is on the floor in the middle of it.
                parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(DoorwayOpening.x + 0.02f, 0.02f, depth * 0.86f), 0.008f), new Vector3(0f, 0.01f, 0f)));
                return parts;
            });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var parts = new List<MeshPart>();
                for (int face = -1; face <= 1; face += 2)
                {
                    float z = face * (depth * 0.5f + 0.001f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(post * 0.36f, DoorwayOpening.y - 0.3f, 0.008f), 0.003f, 1), new Vector3(side * postX, DoorwayOpening.y * 0.5f, z)));
                        parts.Add(ToyKit.Facing(ToyGeo.Dot(post * 0.36f, 14, 0.012f), new Vector3(side * postX, lintelY, face * depth * 0.5f), Vector3.forward * face));
                    }
                    parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(DoorwayOpening.x - 0.1f, lintel * 0.3f, 0.008f), 0.003f, 1), new Vector3(0f, lintelY, z)));
                }
                return parts;
            });
            return kit.Finish(look, Sphere(new Vector3(-postX, 0.2f, 0f), 0.3f), Sphere(new Vector3(postX, 0.2f, 0f), 0.3f));
        }

        // ---------------------------------------------------------------------------------------------------
        // Playing card
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 PlayingCardSize = new Vector3(1.4f, 0.04f, 2f);

        /// <summary>The ace of spades, 1.4 x 0.04 x 2.0, lying flat with its face up and its top toward +Z. Box collider.</summary>
        public static GameObject PlayingCard(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Cardboard, Palette.Tangerine, color);
            var kit = new ToyKit("Playing Card", "PlayingCard");
            Vector3 size = PlayingCardSize;
            kit.Box(size);
            float top = size.y * 0.5f;
            kit.Visual("Visual", look.Main, () => new[]
            {
                ToyKit.At(MeshKit.Extrude(ToyGeo.RoundedRect(size.x, size.z, 0.14f, 5), size.y, 0.008f), Vector3.zero, Flat),
            });
            kit.Visual("Detail Face", look.Detail(Palette.Paper), () => new[]
            {
                ToyKit.At(MeshKit.Extrude(ToyGeo.RoundedRect(size.x - 0.2f, size.z - 0.2f, 0.08f, 4), 0.006f, 0.002f), new Vector3(0f, top + 0.002f, 0f), Flat),
            });
            kit.Visual("Detail Pips", look.Detail(Palette.Ink), () => new[]
            {
                ToyKit.At(MeshKit.Extrude(Spade(0.72f), 0.006f), new Vector3(0f, top + 0.007f, 0f), Flat),
                ToyKit.At(MeshKit.Extrude(Spade(0.2f), 0.006f), new Vector3(-0.43f, top + 0.007f, 0.68f), Flat),
                ToyKit.At(MeshKit.Extrude(Spade(0.2f), 0.006f), new Vector3(0.43f, top + 0.007f, -0.68f), Quaternion.Euler(0f, 180f, 0f) * Flat),
            });
            return kit.Finish(look);
        }

        // A spade pip, point up, centred, of the given height: a heart upside down on a flared stem.
        static List<Vector2> Spade(float size)
        {
            const float junction = 0.42f;
            const int steps = 40;
            var points = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(junction, Mathf.PI * 2f - junction, (float)i / steps);
                float sin = Mathf.Sin(t);
                float x = 16f * sin * sin * sin;
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                points.Add(new Vector2(x, -y));
            }
            points.Add(new Vector2(-4.6f, -15.5f));
            points.Add(new Vector2(4.6f, -15.5f));
            float scale = size / 32.5f;
            for (int i = 0; i < points.Count; i++) points[i] = new Vector2(points[i].x, points[i].y - 0.75f) * scale;
            return points;
        }

        // ---------------------------------------------------------------------------------------------------
        // Sponge
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 SpongeSize = new Vector3(1f, 0.5f, 0.7f);

        /// <summary>A bath sponge, 1.0 x 0.5 x 0.7: a lumpy block full of pores. Box collider.</summary>
        public static GameObject Sponge(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Sponge, Palette.Lagoon, color);
            var kit = new ToyKit("Sponge", "Sponge");
            kit.Box(SpongeSize);
            Vector3 core = SpongeSize - Vector3.one * 0.024f;
            int seed = ToyGeo.Seed("sponge");
            kit.Visual("Visual", look.Main, () =>
            {
                Mesh body = ToyGeo.GridBox(core, 0.07f, 2, 0.1f);
                ToyGeo.Displace(body, (p, n) => SpongeLump(p, seed));
                return new[] { ToyKit.At(body, Vector3.zero) };
            });
            kit.Visual("Detail", look.Shade(0.5f), () =>
            {
                var parts = new List<MeshPart>();
                uint random = (uint)ToyGeo.Seed("pores");
                Vector3 half = core * 0.5f;
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    int count = Mathf.RoundToInt(core[u] * core[v] * 16f) + 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                        for (int i = 0; i < count; i++)
                        {
                            float radius = ToyGeo.Range(ref random, 0.012f, 0.042f);
                            Vector3 normal = Vector3.zero, on = Vector3.zero;
                            normal[axis] = sign;
                            on[axis] = sign * half[axis];
                            on[u] = ToyGeo.Range(ref random, -1f, 1f) * (half[u] - 0.07f - radius);
                            on[v] = ToyGeo.Range(ref random, -1f, 1f) * (half[v] - 0.07f - radius);
                            on += normal * (SpongeLump(on, seed) - 0.002f);
                            parts.Add(ToyKit.Facing(ToyGeo.Dot(radius, 8, 0.006f), on, normal));
                        }
                }
                return parts;
            });
            return kit.Finish(look);
        }

        // 1.5% of the sponge's size, in two octaves.
        static float SpongeLump(Vector3 p, int seed) => 0.011f * ToyGeo.Noise(p * 4.5f, seed) + 0.004f * ToyGeo.Noise(p * 11f, seed + 7);

        // ---------------------------------------------------------------------------------------------------
        // Eraser
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 EraserSize = new Vector3(1.5f, 0.25f, 0.5f);
        /// <summary>Length of the eraser's top face; the ends between it and the 1.5 long bottom are 39.8 degree ramps.</summary>
        public const float EraserTopLength = 0.9f;

        /// <summary>
        /// A pink eraser: a slab 1.5 long (X) at the bottom and 0.9 at the top, 0.25 thick, 0.5 deep, whose
        /// two slanted ends are ramps the player walks up at any scale. One convex collider.
        /// </summary>
        public static GameObject Eraser(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Rubber, Palette.Bubblegum, color);
            var kit = new ToyKit("Eraser", "Eraser");
            Vector3 half = EraserSize * 0.5f;
            float top = EraserTopLength * 0.5f;
            kit.Hull("Hull", () => new[]
            {
                new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z), new Vector3(half.x, -half.y, half.z), new Vector3(-half.x, -half.y, half.z),
                new Vector3(-top, half.y, -half.z), new Vector3(top, half.y, -half.z), new Vector3(top, half.y, half.z), new Vector3(-top, half.y, half.z),
            });
            kit.Visual("Visual", look.Main, () =>
            {
                var outline = new List<Vector2> { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(top, half.y), new Vector2(-top, half.y) };
                return new[] { ToyKit.At(MeshKit.Extrude(ToyGeo.Fillet(outline, 0.014f, 3, true), EraserSize.z, 0.014f), Vector3.zero) };
            });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                // The maker's stripe: a printed band across the top and down both long sides.
                var parts = new List<MeshPart>();
                for (int k = -1; k <= 1; k += 2)
                {
                    float x = k * 0.07f;
                    parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(0.05f, 0.004f, EraserSize.z - 0.03f), 0.0015f, 1), new Vector3(x, half.y, 0f)));
                }
                return parts;
            });
            return kit.Finish(look, Sphere(new Vector3(-0.45f, 0f, 0f), 0.4f), Sphere(new Vector3(0.45f, 0f, 0f), 0.4f));
        }

        // ---------------------------------------------------------------------------------------------------
        // Key
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 KeySize = new Vector3(0.7f, 0.04f, 2f);

        /// <summary>
        /// A flat brass toy key, 0.7 x 0.04 x 2.0, lying level with its bow (0.7 x 0.6) at -Z and its
        /// blade (0.2 wide, 1.4 long) pointing to +Z, with a bit on the +X side short of the tip. Three
        /// convex colliders - bow, blade and bit - whose top edges are chamfered at 45 degrees, which is
        /// what lets the player walk onto it when it is a bridge.
        /// </summary>
        public static GameObject Key(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.BrushedMetal, Palette.Lemon, color);
            var kit = new ToyKit("Key", "Key");
            const float t = 0.02f, c = 0.04f;
            kit.Hull("Bow", () => ToyHull.Chamfered(-0.35f, 0.35f, -t, t, -1f, -0.4f, c, c, c, c));
            kit.Hull("Blade", () => ToyHull.Chamfered(-0.1f, 0.1f, -t, t, -0.44f, 1f, c, c, 0f, c));
            kit.Hull("Bit", () => ToyHull.Chamfered(0.06f, 0.25f, -t, t, 0.5f, 0.85f, 0f, c, c, c));
            kit.Visual("Visual", look.Main, () =>
            {
                // One outline (x, z): round the bow, up the -X side of the blade, round the tip, down the bit.
                const int arc = 5;
                var outline = new List<Vector2> { new Vector2(0.12f, -0.4f) };
                List<Vector2> bow = ToyGeo.RoundedRect(0.7f, 0.6f, 0.22f, arc);
                // The rounded rectangle runs counter-clockwise from its bottom right corner; the bow needs
                // it clockwise, from the +X end of its +Z edge all the way round to the -X end of that edge.
                int start = 2 * (arc + 1) - 1;   // the last point of the top right corner's arc: (0.13, top)
                for (int i = 0; i < bow.Count; i++)
                {
                    Vector2 p = bow[(start - i + bow.Count) % bow.Count];
                    outline.Add(new Vector2(p.x, p.y - 0.7f));
                }
                outline.AddRange(new[]
                {
                    new Vector2(-0.12f, -0.4f), new Vector2(-0.12f, -0.33f), new Vector2(-0.1f, -0.3f),
                    new Vector2(-0.1f, 0.93f), new Vector2(-0.04f, 1f), new Vector2(0.04f, 1f), new Vector2(0.1f, 0.93f),
                    new Vector2(0.1f, 0.85f), new Vector2(0.25f, 0.85f), new Vector2(0.25f, 0.73f), new Vector2(0.17f, 0.73f),
                    new Vector2(0.17f, 0.63f), new Vector2(0.25f, 0.63f), new Vector2(0.25f, 0.5f), new Vector2(0.1f, 0.5f),
                    new Vector2(0.1f, -0.3f), new Vector2(0.12f, -0.33f),
                });
                return new[] { ToyKit.At(MeshKit.Extrude(outline, KeySize.y, 0.018f), Vector3.zero, Flat) };
            });
            kit.Visual("Detail", look.Detail(Palette.Ink, ToyRecipe.Rubber), () => new[]
            {
                // The hole of the bow is painted on: the player walks across it.
                ToyKit.At(ToyGeo.Dot(0.13f, 20, 0.002f), new Vector3(0f, t, -0.74f)),
                ToyKit.Facing(ToyGeo.Dot(0.13f, 20, 0.002f), new Vector3(0f, -t, -0.74f), Vector3.down),
            });
            return kit.Finish(look, Sphere(new Vector3(0f, 0f, -0.7f), 0.36f), Sphere(new Vector3(0f, 0f, 0.3f), 0.5f));
        }

        // ---------------------------------------------------------------------------------------------------
        // Feather
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 FeatherSize = new Vector3(0.4f, 0.03f, 1f);
        /// <summary>Plan area of the feather at scale 1 (what the wind pushes against).</summary>
        public const float FeatherArea = 0.30f;

        /// <summary>
        /// A craft feather lying flat, quill along +Z: 0.4 x 0.03 x 1.0, 0.03 thick on the quill and 0.004
        /// at its outline, so the player walks onto it at any size. One convex collider (a lozenge of plan
        /// area 0.30 with a ridge); vane and quill are real geometry inside it.
        /// </summary>
        public static GameObject Feather(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Feather, Palette.Cherry, color);
            var kit = new ToyKit("Feather", "Feather" + look.Hex);
            kit.Hull("Hull", () =>
            {
                var points = new List<Vector3>();
                for (int side = -1; side <= 1; side += 2)
                {
                    float y = side * FeatherRim;
                    points.Add(new Vector3(0f, y, -0.5f));
                    points.Add(new Vector3(0.2f, y, -0.25f));
                    points.Add(new Vector3(0.2f, y, 0.25f));
                    points.Add(new Vector3(0f, y, 0.5f));
                    points.Add(new Vector3(-0.2f, y, 0.25f));
                    points.Add(new Vector3(-0.2f, y, -0.25f));
                    points.Add(new Vector3(0f, side * FeatherRidge, -0.3f));
                    points.Add(new Vector3(0f, side * FeatherRidge, 0.3f));
                }
                return points;
            });
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(FeatherVane(look), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var profile = new List<Vector2>
                {
                    new Vector2(0f, -0.5f), new Vector2(0.003f, -0.5f), new Vector2(0.0105f, -0.37f), new Vector2(0.0125f, -0.25f),
                    new Vector2(0.012f, 0f), new Vector2(0.009f, 0.25f), new Vector2(0.004f, 0.44f), new Vector2(0.002f, 0.47f), new Vector2(0f, 0.47f),
                };
                // The lathe turns about Y; the quill runs along Z.
                return new[] { ToyKit.At(MeshKit.Lathe(profile, 8, 80f), Vector3.zero, Flat) };
            });
            return kit.Finish(look);
        }

        const float FeatherRim = 0.002f, FeatherRidge = 0.015f;

        // Height of the collider's top surface above the feather's middle plane.
        static float FeatherTop(float x, float z)
        {
            x = Mathf.Abs(x);
            z = Mathf.Abs(z);
            float side = FeatherRidge - 0.065f * x;
            float end = FeatherRidge - 0.08125f * x - 0.065f * (z - 0.3f);
            return Mathf.Max(FeatherRim, Mathf.Min(side, end));
        }

        // Half width of the vane at a point along it (0 at the foot of the vane, 1 at the tip).
        static float FeatherWidth(float u, int side, float z)
        {
            float width = 0.19f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f)), 0.75f);
            // Three splits between the barbs: two on one side, one on the other.
            float[] notches = side > 0 ? new[] { 14f / 48f, 33f / 48f } : new[] { 24f / 48f };
            foreach (float notch in notches)
                width *= 1f - 0.6f * Mathf.Max(0f, 1f - Mathf.Abs(u - notch) * 48f / 1.5f);
            float hull = 0.2f * Mathf.Min(1f, (0.5f - Mathf.Abs(z)) / 0.25f);
            return Mathf.Max(0f, Mathf.Min(width, hull * 0.96f));
        }

        // The vane: a sheet on either side of the quill, with an upper and an under side, that lies
        // just inside the collider's surface. Its colour runs from the full candy colour along the quill
        // to the recipe's pale tone at the outline, in barbs: vertex tints over the recipe's base colour.
        static Mesh FeatherVane(ToyLook look)
        {
            const int steps = 48;
            Color pale = look.Recipe.BaseColor(look.Color), full = Palette.Lin(look.Color);
            var deep = new Color(full.r / Mathf.Max(pale.r, 0.02f), full.g / Mathf.Max(pale.g, 0.02f), full.b / Mathf.Max(pale.b, 0.02f), 1f);
            var b = new GeoBuilder();
            for (int layer = -1; layer <= 1; layer += 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    int first = b.Count;
                    for (int i = 0; i <= steps; i++)
                    {
                        float u = (float)i / steps;
                        float z = Mathf.Lerp(-0.36f, 0.49f, u);
                        float width = FeatherWidth(u, side, z);
                        for (int k = 0; k <= 2; k++)
                        {
                            float x = side * width * k * 0.5f;
                            float y = 0.0015f + 0.0045f * (1f - k * 0.5f);
                            y = Mathf.Min(y, Mathf.Max(0.0012f, FeatherTop(x, z) - 0.0008f));
                            float depth = (k == 0 ? 0.85f : k == 1 ? 0.55f : 0.1f) + (k > 0 && i % 2 == 1 ? 0.14f : 0f);
                            b.Tint = Color.LerpUnclamped(Color.white, deep, depth);
                            b.Add(new Vector3(x, layer * y, z), new Vector3(0f, layer, 0f), new Vector2(z, x));
                        }
                    }
                    for (int i = 0; i < steps; i++)
                        for (int k = 0; k < 2; k++)
                        {
                            int a = first + i * 3 + k;
                            b.Quad(a, a + 1, a + 4, a + 3);
                        }
                }
            Mesh mesh = b.Build("Vane");
            ToyGeo.SmoothNormals(mesh);
            return mesh;
        }
    }
}
