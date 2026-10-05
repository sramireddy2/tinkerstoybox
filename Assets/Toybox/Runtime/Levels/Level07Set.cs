using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    /// <summary>
    /// The looks of Level 7's set: books, paint and odds and ends that are only drawn. Nothing added here
    /// has a collider - whatever must stop the player or a held toy is a collider the level adds itself,
    /// and the looks stay inside it (or behind it). Parts are collected per material and merged into one
    /// mesh each when the set is finished (level statics are a draw per material, ART_BIBLE 12.1).
    /// </summary>
    sealed class Level07Set
    {
        /// <summary>Which side of a book lying flat shows its spine; the other three show the pages.</summary>
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

        /// <summary>Any mesh of the level's own (cached or kept by the caller), where it stands.</summary>
        public void Shape(Material material, Mesh mesh, Vector3 centre, Quaternion rotation, float tone = 1f) =>
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);

        /// <summary>A flat disc: a cylinder with its axis along the rotation's up.</summary>
        public void Disc(Material material, Vector3 centre, float radius, float thickness, Quaternion rotation, float tone = 1f)
        {
            const int sides = 48;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level07/Disc", radius, thickness, sides), () => MeshKit.Cylinder(radius, thickness, sides));
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>
        /// A flat ring between two radii, <paramref name="thickness"/> thick, its axis along the rotation's up.
        /// </summary>
        public void Ring(Material material, Vector3 centre, float inner, float outer, float thickness, Quaternion rotation, float tone = 1f)
        {
            Add(material, RingMesh(inner, outer, thickness), Matrix4x4.TRS(centre, rotation, Vector3.one), tone);
        }

        /// <summary>The mesh of a flat ring lying in the XZ plane, centred on the origin.</summary>
        public static Mesh RingMesh(float inner, float outer, float thickness) =>
            MeshKit.Cached(MeshKit.Key("Level07/Ring", inner, outer, thickness), () =>
            {
                float half = thickness * 0.5f;
                // A closed loop that never touches the axis: under, up the outside, across the top, down the inside.
                var loop = new List<Vector2>
                {
                    new Vector2(inner, -half), new Vector2(outer, -half), new Vector2(outer, half), new Vector2(inner, half), new Vector2(inner, -half),
                };
                return MeshKit.Lathe(loop, 48);
            });

        /// <summary>
        /// A book in the box centre +- size / 2, turned by <paramref name="rotation"/> about that centre: two
        /// covers and a spine in the cover material, and a block of pages that stands back a hair from the
        /// other three sides. The covers fill the box, so a collider of the same box is never smaller than
        /// what is drawn.
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
        public void Stroke(Material paint, Vector3 from, Vector3 to, Vector3 normal, float width, float proud = 0.02f)
        {
            const float thickness = 0.016f;
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            Box(paint, (from + to) * 0.5f + normal * (proud + thickness * 0.5f), new Vector3(width, thickness, length), Quaternion.LookRotation(along / length, normal));
        }

        /// <summary>
        /// A dashed circle painted on a surface: <paramref name="count"/> strips round the centre, each
        /// covering <paramref name="duty"/> of its share of the circle. <paramref name="right"/> and
        /// <paramref name="up"/> span the surface; the strips lie a hair proud of it.
        /// </summary>
        public void DashedCircle(Material paint, Vector3 centre, Vector3 right, Vector3 up, float radius, float width, int count, float duty, float proud = 0.02f)
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
        /// meshes belong to the level and are destroyed with it. A set that stands between the sun and the
        /// level can be drawn without casting a shadow.
        /// </summary>
        public GameObject Finish(LevelContext ctx, string name, bool castShadows = true)
        {
            var root = new GameObject(name);
            for (int i = 0; i < materials.Count; i++)
            {
                Mesh merged = MeshKit.Merge(name + " " + i, parts[i]);
                var visual = new GameObject(materials[i] != null ? materials[i].name : "Visual");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = merged;
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
                ctx.OnDispose(() => MeshKit.Release(merged));
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
