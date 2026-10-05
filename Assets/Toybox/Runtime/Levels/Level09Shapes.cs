using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// The meshes Level 9 is drawn with that no kit has: paint on the round wall of the lighthouse, books
    /// (lying in a stack, standing in a row) and the trestles under the track. Renderers only - the level's
    /// colliders are boxes and cylinders of their own.
    ///
    /// Round things are built about the Y axis. A bearing is the train's: degrees in the XZ plane, 0 toward
    /// -Z (the station), growing toward +X.
    /// </summary>
    internal static class Level09Shapes
    {
        /// <summary>Collects vertices and triangles; a triangle is wound to face along its corners' normals.</summary>
        sealed class Bag
        {
            readonly List<Vector3> positions = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            public int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                positions.Add(position);
                normals.Add(normal);
                uvs.Add(uv);
                return positions.Count - 1;
            }

            void Triangle(int a, int b, int c)
            {
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (face.sqrMagnitude < 1e-14f) return;
                bool flip = Vector3.Dot(face, normals[a] + normals[b] + normals[c]) < 0f;
                triangles.Add(a);
                triangles.Add(flip ? c : b);
                triangles.Add(flip ? b : c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                var white = new Color[positions.Count];
                for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                mesh.SetColors(white);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>The unit vector from the axis toward a bearing.</summary>
        public static Vector3 Radial(float bearing)
        {
            float a = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
        }

        /// <summary>The way a train travels at a bearing (toward growing bearings).</summary>
        public static Vector3 Tangent(float bearing)
        {
            float a = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        // A point on the outside of a round wall: so far along its surface from the bearing, at a height.
        static Vector3 OnWall(float radius, float bearing, float along, float y, out Vector3 normal)
        {
            normal = Radial(bearing + along / radius * Mathf.Rad2Deg);
            return normal * radius + Vector3.up * y;
        }

        /// <summary>
        /// A patch of paint on the outside of a round wall: <paramref name="width"/> wide along the surface,
        /// centred on the bearing, from one height to another. Its UVs are the unit square (u from the left
        /// as seen from outside), so a shape mask sits on it as on a flat card.
        /// </summary>
        public static Mesh WallPatch(float radius, float bearing, float width, float y0, float y1)
        {
            var bag = new Bag();
            int steps = Mathf.Max(2, Mathf.CeilToInt(width / 0.2f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                // Seen from outside, growing bearings run to the left.
                float along = (0.5f - t) * width;
                bag.Add(OnWall(radius, bearing, along, y0, out Vector3 normal), normal, new Vector2(t, 0f));
                bag.Add(OnWall(radius, bearing, along, y1, out normal), normal, new Vector2(t, 1f));
            }
            for (int i = 0; i < steps; i++)
            {
                int p = i * 2;
                bag.Quad(p, p + 1, p + 3, p + 2);
            }
            return bag.Build("Level09 Wall Patch");
        }

        /// <summary>
        /// A dashed line of paint on the outside of a round wall. The path is given flat, as seen from
        /// outside looking at the bearing: x along the surface (to the right), y up. Dashes are spaced evenly
        /// so that the path starts and ends with one.
        /// </summary>
        public static Mesh WallDashes(float radius, float bearing, IList<Vector2> path, float stroke, float dash, float gap)
        {
            var bag = new Bag();
            var lengths = new float[path.Count];
            for (int i = 1; i < path.Count; i++) lengths[i] = lengths[i - 1] + (path[i] - path[i - 1]).magnitude;
            float total = lengths[path.Count - 1];
            int count = Mathf.Max(1, Mathf.RoundToInt((total + gap) / (dash + gap)));
            float period = (total + gap) / count, length = period - gap;
            for (int d = 0; d < count; d++)
            {
                float from = d * period, to = Mathf.Min(total, from + length);
                int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) / 0.15f));
                int previous = -1;
                for (int s = 0; s <= steps; s++)
                {
                    float at = Mathf.Lerp(from, to, (float)s / steps);
                    Sample(path, lengths, at, out Vector2 point, out Vector2 tangent);
                    Vector2 side = new Vector2(-tangent.y, tangent.x) * (stroke * 0.5f);
                    Vector2 a = point - side, b = point + side;
                    // To the right as seen from outside is toward smaller bearings.
                    int index = bag.Add(OnWall(radius, bearing, -a.x, a.y, out Vector3 normalA), normalA, new Vector2(0f, at));
                    bag.Add(OnWall(radius, bearing, -b.x, b.y, out Vector3 normalB), normalB, new Vector2(1f, at));
                    if (previous >= 0) bag.Quad(previous, previous + 1, index + 1, index);
                    previous = index;
                }
            }
            return bag.Build("Level09 Wall Dashes");
        }

        static void Sample(IList<Vector2> path, float[] lengths, float at, out Vector2 point, out Vector2 tangent)
        {
            int i = 1;
            while (i < path.Count - 1 && lengths[i] < at) i++;
            float span = Mathf.Max(1e-6f, lengths[i] - lengths[i - 1]);
            point = Vector2.Lerp(path[i - 1], path[i], Mathf.Clamp01((at - lengths[i - 1]) / span));
            tangent = (path[i] - path[i - 1]).normalized;
        }

        // ---- Books -----------------------------------------------------------------------------------------

        const float Board = 0.24f, Spine = 0.3f, Inset = 0.2f;
        // How far the pages of a standing book end below its boards.
        const float PageDrop = 0.07f;

        static void Box(List<MeshPart> parts, Vector3 min, Vector3 max)
        {
            Vector3 size = max - min;
            if (size.x <= 1e-3f || size.y <= 1e-3f || size.z <= 1e-3f) return;
            parts.Add(new MeshPart(MeshKit.Box(size), (min + max) * 0.5f));
        }

        /// <summary>
        /// A book lying flat between two corners: two boards and a spine go to <paramref name="covers"/>,
        /// the block of pages (set back from the three open edges) to <paramref name="pages"/>. The spine is
        /// on the -X side (<paramref name="spineSide"/> below 0) or the +X side.
        /// </summary>
        public static void LyingBook(List<MeshPart> covers, List<MeshPart> pages, Vector3 min, Vector3 max, int spineSide, bool topBoard = true)
        {
            Box(covers, min, new Vector3(max.x, min.y + Board, max.z));
            if (topBoard) Box(covers, new Vector3(min.x, max.y - Board, min.z), max);
            float y0 = min.y + Board, y1 = max.y - Board;
            if (spineSide < 0)
            {
                Box(covers, new Vector3(min.x, y0, min.z), new Vector3(min.x + Spine, y1, max.z));
                Box(pages, new Vector3(min.x + Spine, y0, min.z + Inset), new Vector3(max.x - Inset, y1, max.z - Inset));
            }
            else
            {
                Box(covers, new Vector3(max.x - Spine, y0, min.z), new Vector3(max.x, y1, max.z));
                Box(pages, new Vector3(min.x + Inset, y0, min.z + Inset), new Vector3(max.x - Spine, y1, max.z - Inset));
            }
        }

        /// <summary>
        /// A row of books standing on a floor, side by side along X (or along Z), their spines on the face
        /// that looks at the level. The row fills the box between the two corners in plan; each book's top
        /// is <paramref name="top"/> plus its entry of a fixed table times <paramref name="spread"/>.
        /// <paramref name="spineToward"/> is +1 if the level lies toward the greater coordinate across the row.
        /// </summary>
        public static void StandingBooks(List<MeshPart> covers, List<MeshPart> pages, Vector3 min, Vector3 max, bool alongX, int spineToward, float top, float spread, int seed)
        {
            float from = alongX ? min.x : min.z, to = alongX ? max.x : max.z;
            float at = from;
            for (int i = seed; at < to - 0.05f; i++)
            {
                float thick = Mathf.Min(Thickness[i % Thickness.Length], to - at);
                // A sliver at the end of the row joins the book before it.
                if (to - (at + thick) < 0.9f) thick = to - at;
                float y1 = top + Height[i % Height.Length] * spread;
                // Not as deep as the row: books of different depths, all flush with the face the level sees.
                float depth = (alongX ? max.z - min.z : max.x - min.x) * Depth[i % Depth.Length];
                Vector3 a, b;
                if (alongX)
                {
                    float z0 = spineToward > 0 ? max.z - depth : min.z, z1 = spineToward > 0 ? max.z : min.z + depth;
                    a = new Vector3(at, min.y, z0);
                    b = new Vector3(at + thick, y1, z1);
                    // Boards on either side, the spine on the face the level sees, pages a little lower.
                    Box(covers, a, new Vector3(a.x + Board, b.y, b.z));
                    Box(covers, new Vector3(b.x - Board, a.y, a.z), b);
                    float s0 = spineToward > 0 ? b.z - Spine : a.z, s1 = spineToward > 0 ? b.z : a.z + Spine;
                    Box(covers, new Vector3(a.x + Board, a.y, s0), new Vector3(b.x - Board, b.y, s1));
                    Box(pages, new Vector3(a.x + Board, a.y, spineToward > 0 ? a.z + Inset : s1), new Vector3(b.x - Board, b.y - PageDrop, spineToward > 0 ? s0 : b.z - Inset));
                }
                else
                {
                    float x0 = spineToward > 0 ? max.x - depth : min.x, x1 = spineToward > 0 ? max.x : min.x + depth;
                    a = new Vector3(x0, min.y, at);
                    b = new Vector3(x1, y1, at + thick);
                    Box(covers, a, new Vector3(b.x, b.y, a.z + Board));
                    Box(covers, new Vector3(a.x, a.y, b.z - Board), b);
                    float s0 = spineToward > 0 ? b.x - Spine : a.x, s1 = spineToward > 0 ? b.x : a.x + Spine;
                    Box(covers, new Vector3(s0, a.y, a.z + Board), new Vector3(s1, b.y, b.z - Board));
                    Box(pages, new Vector3(spineToward > 0 ? a.x + Inset : s1, a.y, a.z + Board), new Vector3(spineToward > 0 ? s0 : b.x - Inset, b.y - PageDrop, b.z - Board));
                }
                at += thick;
            }
        }

        // Fixed tables (lengths without a common factor), so that a row never repeats within sight.
        static readonly float[] Thickness = { 2.2f, 1.4f, 3f, 1.8f, 2.6f, 1.2f, 2f, 3.4f, 1.6f, 2.4f, 1.3f };
        static readonly float[] Height = { 0.2f, 1f, -0.6f, 0.5f, -1f, 0.8f, 0f, -0.3f, 0.9f, -0.8f, 0.4f, -0.5f, 0.7f };
        static readonly float[] Depth = { 1f, 0.82f, 0.94f, 0.7f, 1f, 0.88f, 0.76f };

        /// <summary>Merges the parts into one mesh and destroys them.</summary>
        public static Mesh Merge(string name, List<MeshPart> parts)
        {
            Mesh merged = MeshKit.Merge(name, parts);
            for (int i = 0; i < parts.Count; i++) MeshKit.Release(parts[i].Mesh);
            return merged;
        }

        // ---- Trestles --------------------------------------------------------------------------------------

        /// <summary>
        /// What the track stands on, about the axis: two stringers under the sleepers, and a trestle bent
        /// (two raking posts, a cap, two ties) every <paramref name="pitch"/> degrees, from
        /// <paramref name="top"/> (the underside of the sleepers) down to <paramref name="floor"/>.
        /// </summary>
        public static Mesh Trestles(float radius, float gauge, float top, float floor, float pitch, float firstBearing)
        {
            var parts = new List<MeshPart>();
            const float stringer = 0.26f, cap = 0.4f, post = 0.42f;
            // Stringers: chords under each rail.
            const float step = 10f;
            for (float bearing = 0f; bearing < 360f - 0.01f; bearing += step)
            {
                float mid = bearing + step * 0.5f;
                Quaternion along = Quaternion.LookRotation(Tangent(mid), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    float r = radius + side * gauge;
                    float chord = 2f * r * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.04f;
                    parts.Add(new MeshPart(MeshKit.Box(new Vector3(0.3f, stringer, chord)), Radial(mid) * (r * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad)) + Vector3.up * (top - stringer * 0.5f), along));
                }
            }
            float capTop = top - stringer, postTop = capTop - cap * 0.5f;
            for (float bearing = firstBearing; bearing < firstBearing + 360f - 0.01f; bearing += pitch)
            {
                Vector3 out_ = Radial(bearing), along = Tangent(bearing);
                Quaternion across = Quaternion.LookRotation(out_, Vector3.up);
                parts.Add(new MeshPart(MeshKit.Box(new Vector3(cap, cap, gauge * 2f + 1.3f)), out_ * radius + Vector3.up * (capTop - cap * 0.5f), across));
                float height = postTop - floor;
                for (int side = -1; side <= 1; side += 2)
                {
                    // Each post rakes outward by a fifth of its height... a tenth is plenty for a toy trestle.
                    float rTop = radius + side * gauge, rBottom = rTop + side * height * 0.1f;
                    Vector3 a = out_ * rBottom + Vector3.up * floor, b = out_ * rTop + Vector3.up * postTop;
                    Vector3 up = (b - a).normalized;
                    parts.Add(new MeshPart(MeshKit.Box(new Vector3(post, (b - a).magnitude, post)), (a + b) * 0.5f, Quaternion.LookRotation(Vector3.Cross(out_, up).normalized, up)));
                }
                // Two ties across the posts.
                for (int tie = 1; tie <= 2; tie++)
                {
                    float y = Mathf.Lerp(postTop, floor, tie / 3f);
                    float spread = gauge + (postTop - y) * 0.1f;
                    parts.Add(new MeshPart(MeshKit.Box(new Vector3(0.24f, 0.3f, spread * 2f + 0.7f)), out_ * radius + Vector3.up * y + along * 0.02f, across));
                }
            }
            return Merge("Level09 Trestles", parts);
        }
    }
}
