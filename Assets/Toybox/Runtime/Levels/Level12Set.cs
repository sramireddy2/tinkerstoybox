using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    /// <summary>
    /// The looks of Level 12's set: boards, books, socks and paint that are only drawn. Nothing added here
    /// has a collider - whatever must stop the player or a held toy is a collider the level adds itself,
    /// and the looks stay inside it (or behind it, or above the sky cap). Parts are collected per material
    /// and merged into one mesh each when the set is finished (level statics are a draw per material,
    /// ART_BIBLE 12.1). The same idea as Level 7's set, with this level's own shapes.
    /// </summary>
    sealed class Level12Set
    {
        /// <summary>Which side of a book shows its spine; the other three show the pages.</summary>
        public enum Side
        {
            NegX,
            PosX,
            NegZ,
            PosZ,
        }

        readonly List<Material> materials = new List<Material>();
        readonly List<List<MeshPart>> parts = new List<List<MeshPart>>();

        /// <summary>A box. <paramref name="tone"/> darkens it (1 leaves the material's tone alone).</summary>
        public void Box(Material material, Vector3 centre, Vector3 size, Quaternion rotation, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Box", size.x, size.y, size.z), () => MeshKit.Box(size));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        public void Box(Material material, Vector3 centre, Vector3 size, float tone = 1f) =>
            Box(material, centre, size, Quaternion.identity, tone);

        /// <summary>The box between two corners.</summary>
        public void Span(Material material, Vector3 min, Vector3 max, float tone = 1f) =>
            Box(material, (min + max) * 0.5f, max - min, Quaternion.identity, tone);

        /// <summary>A box with rounded edges (a folded sock, a cushion).</summary>
        public void Soft(Material material, Vector3 centre, Vector3 size, float bevel, Quaternion rotation, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("RoundedBox", size.x, size.y, size.z, bevel), () => MeshKit.RoundedBox(size, bevel));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>Any mesh of the level's own (cached or kept by the caller), where it stands.</summary>
        public void Shape(Material material, Mesh mesh, Vector3 centre, Quaternion rotation, float tone = 1f) =>
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);

        /// <summary>A flat disc: a cylinder with its axis along the rotation's up.</summary>
        public void Disc(Material material, Vector3 centre, float radius, float thickness, Quaternion rotation, float tone = 1f)
        {
            const int sides = 32;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level12/Disc", radius, thickness, sides), () => MeshKit.Cylinder(radius, thickness, sides));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>
        /// A book in the box centre +- size / 2, turned by <paramref name="rotation"/> about that centre: two
        /// covers and a spine in the cover material, and a block of pages that stands back a hair from the
        /// other three sides. Its thickness is its y; the covers fill the box, so a collider of the same box
        /// is never smaller than what is drawn.
        /// </summary>
        public void Book(Material cover, Material pages, Vector3 centre, Vector3 size, Side spine, Quaternion rotation, float tone = 1f, Material topCover = null)
        {
            float board = Mathf.Min(0.14f, size.y * 0.12f);
            float inset = Mathf.Min(0.04f, Mathf.Min(size.x, size.z) * 0.05f);
            float inner = size.y - 2f * board;
            Vector3 up = Vector3.up * ((size.y - board) * 0.5f);
            Place(topCover != null ? topCover : cover, centre, rotation, up, new Vector3(size.x, board, size.z), tone);
            Place(cover, centre, rotation, -up, new Vector3(size.x, board, size.z), tone);

            bool alongX = spine == Side.NegX || spine == Side.PosX;
            float sign = spine == Side.NegX || spine == Side.NegZ ? -1f : 1f;
            Vector3 spineSize = alongX ? new Vector3(board, inner, size.z) : new Vector3(size.x, inner, board);
            Vector3 spineAt = alongX ? new Vector3(sign * (size.x - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (size.z - board) * 0.5f);
            Place(cover, centre, rotation, spineAt, spineSize, tone);

            Vector3 pageSize = alongX ? new Vector3(size.x - inset - board, inner, size.z - 2f * inset) : new Vector3(size.x - 2f * inset, inner, size.z - inset - board);
            Vector3 pageAt = alongX ? new Vector3(sign * (inset - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (inset - board) * 0.5f);
            Place(pages, centre, rotation, pageAt, pageSize, 1f);
        }

        /// <summary>One straight stroke of paint from <paramref name="from"/> to <paramref name="to"/>, a hair proud of the surface.</summary>
        public void Stroke(Material paint, Vector3 from, Vector3 to, Vector3 normal, float width, float proud = 0.01f, float thickness = 0.008f)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            Box(paint, (from + to) * 0.5f + normal * (proud + thickness * 0.5f), new Vector3(width, thickness, length), Quaternion.LookRotation(along / length, normal));
        }

        /// <summary>
        /// A dashed line of paint from <paramref name="from"/> to <paramref name="to"/>: whole dashes, evenly
        /// spread, with a dash at either end. A stretch of the line can be left out (a door in the way).
        /// </summary>
        public void Dashes(Material paint, Vector3 from, Vector3 to, Vector3 normal, float width, float dash, float gap, float proud = 0.01f,
            float skipFrom = 0f, float skipTo = 0f, float thickness = 0.008f)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            along /= length;
            int count = Mathf.Max(1, Mathf.RoundToInt((length + gap) / (dash + gap)));
            float step = count > 1 ? (length - dash) / (count - 1) : 0f;
            for (int i = 0; i < count; i++)
            {
                float a = count > 1 ? i * step : (length - dash) * 0.5f, b = a + dash;
                if (skipTo > skipFrom && b > skipFrom && a < skipTo) continue;
                Stroke(paint, from + along * a, from + along * b, normal, width, proud, thickness);
            }
        }

        /// <summary>
        /// Takes over the looks of a set piece that is already in the level (a block, a spool): its meshes
        /// join the set where they stand and its own renderers go. Its colliders stay where they are.
        /// </summary>
        public void Absorb(GameObject piece)
        {
            foreach (MeshRenderer renderer in piece.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Add(renderer.sharedMaterial, filter.sharedMesh, renderer.transform.localToWorldMatrix, 1f);
                Sim.Destroy(renderer);
                Sim.Destroy(filter);
            }
        }

        /// <summary>
        /// Merges what was collected into one renderer per material under a new object of the level. The
        /// meshes are kept for the level's next build (<see cref="SetMeshes"/>). A set that stands between
        /// the sun and the level can be drawn without casting a shadow.
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

        void Add(Material material, Mesh mesh, Matrix4x4 matrix, float tone)
        {
            int index = materials.IndexOf(material);
            if (index < 0)
            {
                index = materials.Count;
                materials.Add(material);
                parts.Add(new List<MeshPart>());
            }
            parts[index].Add(new MeshPart(mesh, matrix, new Color(tone, tone, tone, 1f)));
        }
    }
}
