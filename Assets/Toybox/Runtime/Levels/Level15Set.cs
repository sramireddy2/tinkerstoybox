using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    /// <summary>
    /// The looks of Level 15's set: books, paint, pipes and odds and ends that are only drawn. Nothing
    /// added here has a collider - whatever must stop the player or a held toy is a collider the level
    /// adds itself, and the looks stay inside it (or behind it). Parts are collected per material and
    /// merged into one mesh each when the set is finished (level statics are a draw per material).
    /// Paint, lit windows and lamps are not materials at all but colours (<see cref="Look"/>).
    /// </summary>
    sealed class Level15Set
    {
        /// <summary>Which side of a book shows its spine; the other three show the pages.</summary>
        public enum Side
        {
            NegX,
            PosX,
            NegZ,
            PosZ,
        }

        /// <summary>
        /// What a part is drawn with: a material of its own, or a colour of paint. Paint of every colour is one
        /// unlit material (<see cref="Ink"/>) with the colour in the part's vertices, so a dozen colours of
        /// paint and light are one material and one draw.
        /// </summary>
        public readonly struct Look
        {
            public readonly Material Material;
            /// <summary>Linear, as the unlit shader takes it.</summary>
            public readonly Color Tint;
            public readonly bool Painted;

            Look(Material material, Color tint, bool painted)
            {
                Material = material;
                Tint = tint;
                Painted = painted;
            }

            public static implicit operator Look(Material material) => new Look(material, Color.white, false);
            public static implicit operator Look(Color linear) => new Look(null, linear, true);
        }

        /// <summary>The unlit white that every colour of paint is a tint of (one for all sets of the level).</summary>
        public static Material Ink => Materials.Flat(Color.white);

        readonly List<Material> materials = new List<Material>();
        readonly List<List<MeshPart>> parts = new List<List<MeshPart>>();

        /// <summary>A box. <paramref name="tone"/> darkens it (1 leaves the material's tone alone).</summary>
        public void Box(Look material, Vector3 centre, Vector3 size, Quaternion rotation, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Box", size.x, size.y, size.z), () => MeshKit.Box(size));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        public void Box(Look material, Vector3 centre, Vector3 size, float tone = 1f) =>
            Box(material, centre, size, Quaternion.identity, tone);

        /// <summary>The box between two corners.</summary>
        public void Span(Look material, Vector3 min, Vector3 max, float tone = 1f) =>
            Box(material, (min + max) * 0.5f, max - min, Quaternion.identity, tone);

        /// <summary>A box with softened edges.</summary>
        public void Soft(Look material, Vector3 centre, Vector3 size, Quaternion rotation, float bevel, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("RoundedBox", size.x, size.y, size.z, bevel), () => MeshKit.RoundedBox(size, bevel));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>Any mesh of the level's own (cached or kept by the caller), where it stands.</summary>
        public void Shape(Look material, Mesh mesh, Vector3 centre, Quaternion rotation, float tone = 1f) =>
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);

        /// <summary>A cylinder with its axis along the rotation's up.</summary>
        public void Disc(Look material, Vector3 centre, float radius, float thickness, Quaternion rotation, float tone = 1f, int sides = 32)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level15/Disc", radius, thickness, sides), () => MeshKit.Cylinder(radius, thickness, sides));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>A round bar from one point to another.</summary>
        public void Rod(Look material, Vector3 from, Vector3 to, float radius, float tone = 1f, int sides = 12)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level15/Rod", radius, length, sides), () => MeshKit.Cylinder(radius, length, sides));
            Add(material, mesh, Matrix4x4.TRS((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, along / length), Vector3.one), tone);
        }

        /// <summary>A ball.</summary>
        public void Ball(Look material, Vector3 centre, float radius, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level15/Ball", radius), () => MeshKit.Sphere(radius, 16, 10));
            Add(material, mesh, Matrix4x4.Translate(centre), tone);
        }

        /// <summary>A ring between two radii, <paramref name="thickness"/> thick, its axis along the rotation's up.</summary>
        public void Ring(Look material, Vector3 centre, float inner, float outer, float thickness, Quaternion rotation, float tone = 1f)
        {
            Add(material, RingMesh(inner, outer, thickness), Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>The mesh of a flat ring lying in the XZ plane, centred on the origin.</summary>
        public static Mesh RingMesh(float inner, float outer, float thickness) =>
            MeshKit.Cached(MeshKit.Key("Level15/Ring", inner, outer, thickness), () =>
            {
                float half = thickness * 0.5f;
                // A closed loop that never touches the axis: under, up the outside, across the top, down the inside.
                var loop = new List<Vector2>
                {
                    new Vector2(inner, -half), new Vector2(outer, -half), new Vector2(outer, half), new Vector2(inner, half), new Vector2(inner, -half),
                };
                return MeshKit.Lathe(loop, 40);
            });

        /// <summary>
        /// A book in the box centre +- size / 2, turned by <paramref name="rotation"/> about that centre: two
        /// covers and a spine in the cover material, and a block of pages that stands back a hair from the
        /// other three sides. The covers lie on the box's two faces across <paramref name="thin"/> (0 x, 1 y,
        /// 2 z); the covers fill the box, so a collider of the same box is never smaller than what is drawn.
        /// </summary>
        public void Book(Material cover, Material pages, Vector3 centre, Vector3 size, int thin, Side spine, Quaternion rotation, float tone = 1f)
        {
            // Work in a frame in which the book lies flat (thin axis = y), then turn it back.
            Quaternion lay = thin == 1 ? Quaternion.identity : thin == 0 ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            Vector3 flat = thin == 1 ? size : thin == 0 ? new Vector3(size.y, size.x, size.z) : new Vector3(size.x, size.z, size.y);
            Quaternion turn = rotation * lay;

            float board = Mathf.Min(0.14f, flat.y * 0.12f);
            float inset = Mathf.Min(0.05f, Mathf.Min(flat.x, flat.z) * 0.05f);
            float inner = flat.y - 2f * board;
            Vector3 up = Vector3.up * ((flat.y - board) * 0.5f);
            Place(cover, centre, turn, up, new Vector3(flat.x, board, flat.z), tone);
            Place(cover, centre, turn, -up, new Vector3(flat.x, board, flat.z), tone);

            bool alongX = spine == Side.NegX || spine == Side.PosX;
            float sign = spine == Side.NegX || spine == Side.NegZ ? -1f : 1f;
            Vector3 spineSize = alongX ? new Vector3(board, inner, flat.z) : new Vector3(flat.x, inner, board);
            Vector3 spineAt = alongX ? new Vector3(sign * (flat.x - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (flat.z - board) * 0.5f);
            Place(cover, centre, turn, spineAt, spineSize, tone);

            Vector3 pageSize = alongX ? new Vector3(flat.x - inset - board, inner, flat.z - 2f * inset) : new Vector3(flat.x - 2f * inset, inner, flat.z - inset - board);
            Vector3 pageAt = alongX ? new Vector3(sign * (inset - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (inset - board) * 0.5f);
            Place(pages, centre, turn, pageAt, pageSize, 1f);
        }

        /// <summary>One straight stroke of paint from <paramref name="from"/> to <paramref name="to"/>, a hair proud of the surface.</summary>
        public void Stroke(Look paint, Vector3 from, Vector3 to, Vector3 normal, float width, float proud = 0.02f)
        {
            const float thickness = 0.016f;
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            Box(paint, (from + to) * 0.5f + normal * (proud + thickness * 0.5f), new Vector3(width, thickness, length), Quaternion.LookRotation(along / length, normal));
        }

        /// <summary>A dashed stroke: as many dashes as fit at about <paramref name="pitch"/>, each covering <paramref name="duty"/> of its share.</summary>
        public void Dashes(Look paint, Vector3 from, Vector3 to, Vector3 normal, float width, float pitch, float duty = 0.6f, float proud = 0.02f)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / pitch));
            Vector3 step = along / count;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = from + step * (i + (1f - duty) * 0.5f);
                Stroke(paint, a, a + step * duty, normal, width, proud);
            }
        }

        /// <summary>The dashed outline of a rectangle on a surface spanned by <paramref name="right"/> and <paramref name="up"/>.</summary>
        public void DashedRect(Look paint, Vector3 centre, Vector3 right, Vector3 up, float width, float height, float line, float pitch, float proud = 0.02f)
        {
            Vector3 normal = Vector3.Cross(up, right).normalized;
            Vector3 r = right.normalized * (width * 0.5f), u = up.normalized * (height * 0.5f);
            Dashes(paint, centre - r - u, centre + r - u, normal, line, pitch, 0.6f, proud);
            Dashes(paint, centre + r - u, centre + r + u, normal, line, pitch, 0.6f, proud);
            Dashes(paint, centre + r + u, centre - r + u, normal, line, pitch, 0.6f, proud);
            Dashes(paint, centre - r + u, centre - r - u, normal, line, pitch, 0.6f, proud);
        }

        /// <summary>
        /// A dashed circle painted on a surface: <paramref name="count"/> strips round the centre, each
        /// covering <paramref name="duty"/> of its share of the circle. <paramref name="right"/> and
        /// <paramref name="up"/> span the surface; the strips lie a hair proud of it.
        /// </summary>
        public void DashedCircle(Look paint, Vector3 centre, Vector3 right, Vector3 up, float radius, float width, int count, float duty, float proud = 0.02f)
        {
            const float thickness = 0.016f;
            Vector3 normal = Vector3.Cross(up, right).normalized;
            float length = 2f * radius * Mathf.Sin(Mathf.PI * duty / count);
            for (int i = 0; i < count; i++)
            {
                float a = 2f * Mathf.PI * (i + 0.5f) / count;
                Vector3 radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Vector3 tangent = -right * Mathf.Sin(a) + up * Mathf.Cos(a);
                Vector3 at = centre + radial * radius + normal * (proud + thickness * 0.5f);
                Box(paint, at, new Vector3(width, thickness, length), Quaternion.LookRotation(tangent, normal));
            }
        }

        /// <summary>
        /// Takes over the looks of a set piece that is already in the level (a block, a spool): its meshes
        /// join the set where they stand and its own renderers go. Its colliders stay where they are.
        /// </summary>
        public void Absorb(GameObject piece, System.Func<Material, Material> instead = null)
        {
            foreach (MeshRenderer renderer in piece.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Material material = renderer.sharedMaterial;
                if (instead != null) material = instead(material) ?? material;
                Add(material, filter.sharedMesh, renderer.transform.localToWorldMatrix, 1f);
                Sim.Destroy(renderer);
                Sim.Destroy(filter);
            }
        }

        /// <summary>
        /// Merges what was collected into one renderer per material under a new object of the level. The
        /// meshes are kept for the level's next build (<see cref="SetMeshes"/>). Paint is drawn without
        /// casting a shadow.
        /// </summary>
        public GameObject Finish(LevelContext ctx, string name, bool castShadows = true)
        {
            var root = new GameObject(name);
            for (int i = 0; i < materials.Count; i++)
            {
                Mesh merged = SetMeshes.Take(ctx, name + " " + i, parts[i]);
                var visual = new GameObject(materials[i] != null ? materials[i].name : "Visual");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = merged;
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            materials.Clear();
            parts.Clear();
            return ctx.AddStatic(root, Vector3.zero);
        }

        void Place(Material material, Vector3 centre, Quaternion rotation, Vector3 offset, Vector3 size, float tone) =>
            Box(material, centre + rotation * offset, size, rotation, tone);

        void Add(Look look, Mesh mesh, Matrix4x4 matrix, float tone)
        {
            Material material = look.Painted ? Ink : look.Material;
            int index = materials.IndexOf(material);
            if (index < 0)
            {
                index = materials.Count;
                materials.Add(material);
                parts.Add(new List<MeshPart>());
            }
            Color colour = look.Painted ? new Color(look.Tint.r * tone, look.Tint.g * tone, look.Tint.b * tone, 1f) : new Color(tone, tone, tone, 1f);
            parts[index].Add(new MeshPart(mesh, matrix, colour));
        }
    }
}
