using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// The meshes Level 2 is drawn with that no kit has: curved walls, the paint on them and on the floor,
    /// the striped pad. Everything is built about the origin with the silo's axis as the Y axis; angles are
    /// degrees in the XZ plane, 0 along +X and 90 along +Z. Renderers only - the level's colliders are
    /// boxes and convex prisms of their own.
    /// </summary>
    internal static class Level02Shapes
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

            public void Triangle(int a, int b, int c)
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

            /// <summary>A flat quad with its own corners, in order round it.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int first = Add(a, normal, new Vector2(0f, 0f));
                Add(b, normal, new Vector2(1f, 0f));
                Add(c, normal, new Vector2(1f, 1f));
                Add(d, normal, new Vector2(0f, 1f));
                Quad(first, first + 1, first + 2, first + 3);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Vector3 Radial(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>
        /// A piece of a round wall: the part of the ring between two radii and two heights from one angle to
        /// another, with its inner and outer faces (smooth), top, underside and the two cut ends.
        /// </summary>
        public static Mesh Ring(float inner, float outer, float y0, float y1, float fromDegrees, float toDegrees, int steps)
        {
            var bag = new Bag();
            steps = Mathf.Max(1, steps);
            int stride = 8;
            for (int i = 0; i <= steps; i++)
            {
                float degrees = Mathf.Lerp(fromDegrees, toDegrees, (float)i / steps);
                Vector3 dir = Radial(degrees);
                float arc = degrees * Mathf.Deg2Rad;
                Vector3 a = dir * inner, b = dir * outer;
                bag.Add(a + Vector3.up * y0, -dir, new Vector2(arc * inner, y0));
                bag.Add(a + Vector3.up * y1, -dir, new Vector2(arc * inner, y1));
                bag.Add(b + Vector3.up * y0, dir, new Vector2(arc * outer, y0));
                bag.Add(b + Vector3.up * y1, dir, new Vector2(arc * outer, y1));
                bag.Add(a + Vector3.up * y1, Vector3.up, new Vector2(a.x, a.z));
                bag.Add(b + Vector3.up * y1, Vector3.up, new Vector2(b.x, b.z));
                bag.Add(a + Vector3.up * y0, Vector3.down, new Vector2(a.x, a.z));
                bag.Add(b + Vector3.up * y0, Vector3.down, new Vector2(b.x, b.z));
            }
            for (int i = 0; i < steps; i++)
            {
                int p = i * stride, q = p + stride;
                bag.Quad(p, p + 1, q + 1, q);
                bag.Quad(p + 2, p + 3, q + 3, q + 2);
                bag.Quad(p + 4, p + 5, q + 5, q + 4);
                bag.Quad(p + 6, p + 7, q + 7, q + 6);
            }
            Cap(bag, inner, outer, y0, y1, fromDegrees, -1f);
            Cap(bag, inner, outer, y0, y1, toDegrees, 1f);
            return bag.Build("Level02 Ring");
        }

        static void Cap(Bag bag, float inner, float outer, float y0, float y1, float degrees, float sign)
        {
            Vector3 dir = Radial(degrees);
            Vector3 normal = new Vector3(-dir.z, 0f, dir.x) * sign;
            bag.Quad(dir * inner + Vector3.up * y0, dir * outer + Vector3.up * y0, dir * outer + Vector3.up * y1, dir * inner + Vector3.up * y1, normal);
        }

        /// <summary>
        /// A band of paint lying flat (facing up) round a hole: from the radius <paramref name="inner"/> out
        /// to <paramref name="outer"/>, from one angle to another, cut off where |x| would pass
        /// <paramref name="halfWidth"/> (the corridor's walls).
        /// </summary>
        public static Mesh FloorBand(float inner, float outer, float fromDegrees, float toDegrees, int steps, float halfWidth)
        {
            var bag = new Bag();
            steps = Mathf.Max(1, steps);
            for (int i = 0; i <= steps; i++)
            {
                Vector3 dir = Radial(Mathf.Lerp(fromDegrees, toDegrees, (float)i / steps));
                float limit = Mathf.Abs(dir.x) > 1e-4f ? halfWidth / Mathf.Abs(dir.x) : float.MaxValue;
                float reach = Mathf.Max(inner, Mathf.Min(outer, limit));
                bag.Add(dir * inner, Vector3.up, new Vector2(0f, 0f));
                bag.Add(dir * reach, Vector3.up, new Vector2(1f, 0f));
            }
            for (int i = 0; i < steps; i++)
            {
                int p = i * 2;
                bag.Quad(p, p + 1, p + 3, p + 2);
            }
            return bag.Build("Level02 Floor Band");
        }

        /// <summary>
        /// A dashed line of paint on a flat wall that runs along Z: the wall's face is the plane x = 0 of the
        /// mesh and looks along <paramref name="normal"/> (+X or -X). From <paramref name="z0"/> to
        /// <paramref name="z1"/>, its middle at the height <paramref name="y"/>, <paramref name="tall"/> high;
        /// dashes are spaced evenly so that the line starts and ends with one.
        /// </summary>
        public static Mesh WallDashes(float z0, float z1, float y, float tall, float dash, float gap, Vector3 normal)
        {
            var bag = new Bag();
            float total = z1 - z0;
            int count = Mathf.Max(1, Mathf.RoundToInt((total + gap) / (dash + gap)));
            float period = (total + gap) / count, length = period - gap, half = tall * 0.5f;
            for (int d = 0; d < count; d++)
            {
                float from = z0 + d * period, to = from + length;
                bag.Quad(new Vector3(0f, y - half, from), new Vector3(0f, y - half, to), new Vector3(0f, y + half, to), new Vector3(0f, y + half, from), normal);
            }
            return bag.Build("Level02 Wall Dashes");
        }

        /// <summary>
        /// Every other stripe of a striped disc lying flat (facing up): stripes run at 45 degrees, each
        /// <paramref name="width"/> wide; <paramref name="odd"/> picks which half of them. Two of these, one
        /// per material, make a hazard surface (ART_BIBLE 4.4).
        /// </summary>
        public static Mesh StripedDisc(float radius, float width, bool odd)
        {
            var bag = new Bag();
            var along = new Vector3(1f, 0f, 1f) * 0.70710678f;
            var across = new Vector3(-1f, 0f, 1f) * 0.70710678f;
            int count = Mathf.CeilToInt(radius / width);
            for (int k = -count; k < count; k++)
            {
                if (((k % 2) + 2) % 2 != (odd ? 1 : 0)) continue;
                float u0 = Mathf.Max(-radius, k * width), u1 = Mathf.Min(radius, (k + 1) * width);
                if (u1 <= u0) continue;
                // The stripe in a few slices, so that its ends follow the disc's edge.
                const int slices = 3;
                for (int s = 0; s < slices; s++)
                {
                    float a = Mathf.Lerp(u0, u1, (float)s / slices), b = Mathf.Lerp(u0, u1, (float)(s + 1) / slices);
                    float va = Mathf.Sqrt(Mathf.Max(0f, radius * radius - a * a)), vb = Mathf.Sqrt(Mathf.Max(0f, radius * radius - b * b));
                    bag.Quad(along * a - across * va, along * b - across * vb, along * b + across * vb, along * a + across * va, Vector3.up);
                }
            }
            return bag.Build("Level02 Striped Disc");
        }

        /// <summary>
        /// Every other stripe of a striped band on the inside of a round wall, from one height to another:
        /// stripes lean at 45 degrees, each <paramref name="width"/> wide (measured across it), and there is
        /// an even number of them so that the two halves close the circle.
        /// </summary>
        public static Mesh StripedBand(float radius, float y0, float y1, float width, bool odd)
        {
            var bag = new Bag();
            float height = y1 - y0, circumference = 2f * Mathf.PI * radius;
            int count = Mathf.Max(2, Mathf.RoundToInt(circumference / (2f * width * 1.41421356f)) * 2);
            float step = circumference / count;
            for (int k = 0; k < count; k++)
            {
                if ((k % 2 == 1) != odd) continue;
                float u = k * step;
                int first = bag.Add(OnCylinder(radius, u, y0, out Vector3 normal), normal, new Vector2(0f, 0f));
                bag.Add(OnCylinder(radius, u + step, y0, out normal), normal, new Vector2(1f, 0f));
                bag.Add(OnCylinder(radius, u + step + height, y1, out normal), normal, new Vector2(1f, 1f));
                bag.Add(OnCylinder(radius, u + height, y1, out normal), normal, new Vector2(0f, 1f));
                bag.Quad(first, first + 1, first + 2, first + 3);
            }
            return bag.Build("Level02 Striped Band");
        }

        static Vector3 OnCylinder(float radius, float arc, float y, out Vector3 normal)
        {
            float a = arc / radius;
            normal = new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a));
            return new Vector3(radius * Mathf.Cos(a), y, radius * Mathf.Sin(a));
        }

        /// <summary>
        /// A dashed line painted on the inside of a round wall of the given radius. The path is given flat,
        /// as seen from the axis looking along +Z: x across, y up; every point is pushed out along +Z onto
        /// the wall. Dashes are spaced evenly so that the path starts and ends with one.
        /// </summary>
        public static void Dashes(List<MeshPart> parts, IList<Vector2> path, float radius, float width, float dash, float gap)
        {
            if (path == null || path.Count < 2) return;
            var lengths = new float[path.Count];
            for (int i = 1; i < path.Count; i++) lengths[i] = lengths[i - 1] + (path[i] - path[i - 1]).magnitude;
            float total = lengths[path.Count - 1];
            if (total < 1e-4f) return;

            int count = Mathf.Max(1, Mathf.RoundToInt((total + gap) / (dash + gap)));
            float period = (total + gap) / count;
            float length = period - gap;
            var bag = new Bag();
            for (int d = 0; d < count; d++)
            {
                float from = d * period, to = Mathf.Min(total, from + length);
                int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) / 0.25f));
                int first = -1;
                for (int s = 0; s <= steps; s++)
                {
                    float at = Mathf.Lerp(from, to, (float)s / steps);
                    Sample(path, lengths, at, out Vector2 point, out Vector2 tangent);
                    var side = new Vector2(-tangent.y, tangent.x) * (width * 0.5f);
                    int index = bag.Add(OnWall(point - side, radius, out Vector3 normalA), normalA, new Vector2(0f, at));
                    bag.Add(OnWall(point + side, radius, out Vector3 normalB), normalB, new Vector2(1f, at));
                    if (first >= 0) bag.Quad(first, first + 1, index + 1, index);
                    first = index;
                }
            }
            Mesh mesh = bag.Build("Level02 Dashes");
            parts.Add(new MeshPart(mesh, Matrix4x4.identity));
        }

        static void Sample(IList<Vector2> path, float[] lengths, float at, out Vector2 point, out Vector2 tangent)
        {
            int i = 1;
            while (i < path.Count - 1 && lengths[i] < at) i++;
            float span = Mathf.Max(1e-6f, lengths[i] - lengths[i - 1]);
            float t = Mathf.Clamp01((at - lengths[i - 1]) / span);
            point = Vector2.Lerp(path[i - 1], path[i], t);
            tangent = (path[i] - path[i - 1]).normalized;
        }

        static Vector3 OnWall(Vector2 flat, float radius, out Vector3 normal)
        {
            float x = Mathf.Clamp(flat.x, -radius * 0.98f, radius * 0.98f);
            float z = Mathf.Sqrt(radius * radius - x * x);
            normal = new Vector3(-x, 0f, -z) / radius;
            return new Vector3(x, flat.y, z);
        }

        /// <summary>The corners of an upright prism over a footprint in the XZ plane, for a convex hull.</summary>
        public static List<Vector3> Prism(IList<Vector2> footprint, float y0, float y1)
        {
            var points = new List<Vector3>(footprint.Count * 2);
            for (int i = 0; i < footprint.Count; i++)
            {
                points.Add(new Vector3(footprint[i].x, y0, footprint[i].y));
                points.Add(new Vector3(footprint[i].x, y1, footprint[i].y));
            }
            return points;
        }
    }
}
