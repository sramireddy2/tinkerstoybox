using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// The meshes Level 13 is drawn with that no kit has: the round glass of the bowl, the bed of gravel
    /// with the tower's floor cut out of it, the shoe print, the wind-up goldfish. Everything is built
    /// about the bowl's axis (the Y axis); angles are degrees in the XZ plane, 0 along +X and 90 along +Z.
    /// Renderers only - the level's colliders are boxes of its own.
    /// </summary>
    internal static class Level13Shapes
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
        /// A round wall: the ring between two radii and two heights, all the way round, with its inner and
        /// outer faces (smooth), its top and its underside.
        /// </summary>
        public static Mesh Ring(float inner, float outer, float y0, float y1, int steps)
        {
            var bag = new Bag();
            steps = Mathf.Max(8, steps);
            const int stride = 8;
            for (int i = 0; i <= steps; i++)
            {
                float degrees = 360f * i / steps;
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
            return bag.Build("Level13 Ring");
        }

        /// <summary>
        /// The bed of gravel: a disc of the given radius whose top is at y = 0, so thick, with a notch cut
        /// into its +Z side for the tower's floor (x within the half width, z beyond notchZ).
        /// </summary>
        public static Mesh GravelBed(float radius, float thickness, float notchHalfWidth, float notchZ, int steps)
        {
            var outline = new List<Vector2>();
            float edge = Mathf.Sqrt(Mathf.Max(0f, radius * radius - notchHalfWidth * notchHalfWidth));
            float from = Mathf.Atan2(edge, notchHalfWidth) * Mathf.Rad2Deg;      // the notch's +X corner on the rim
            float to = from - (360f - 2f * (90f - from));                         // round the long way to its -X corner
            steps = Mathf.Max(12, steps);
            for (int i = 0; i <= steps; i++)
            {
                Vector3 dir = Radial(Mathf.Lerp(from, to, (float)i / steps));
                outline.Add(new Vector2(dir.x * radius, dir.z * radius));
            }
            outline.Add(new Vector2(-notchHalfWidth, notchZ));
            outline.Add(new Vector2(notchHalfWidth, notchZ));
            Mesh flat = MeshKit.Extrude(outline, thickness);
            // The outline was drawn in (x, z) and extruded along the third axis: lay it down.
            Mesh laid = MeshKit.Merge("Level13 Gravel", new[] { new MeshPart(flat, new Vector3(0f, -thickness * 0.5f, 0f), Quaternion.Euler(90f, 0f, 0f)) });
            MeshKit.Release(flat);
            return laid;
        }

        /// <summary>A shoe sole in the XY plane, toe up: half a circle for the toe, a smaller half for the heel.</summary>
        public static Mesh Sole()
        {
            var outline = new List<Vector2>();
            for (int i = 0; i <= 12; i++)
            {
                float a = Mathf.PI * i / 12f;
                outline.Add(new Vector2(Mathf.Cos(a) * 0.16f, 0.17f + Mathf.Sin(a) * 0.16f));
            }
            for (int i = 0; i <= 12; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / 12f;
                outline.Add(new Vector2(Mathf.Cos(a) * 0.115f, -0.2f + Mathf.Sin(a) * 0.115f));
            }
            return MeshKit.Extrude(outline, 0.004f);
        }

        /// <summary>
        /// The body of the wind-up goldfish, lying along Z with its nose at +Z: a fat lens in the YZ plane,
        /// so thick across. Its belly is at y = 0.
        /// </summary>
        public static Mesh FishBody(float length, float height, float thickness)
        {
            var outline = new List<Vector2>();
            const int steps = 14;
            // Along the back from the tail to the nose, then along the belly back again.
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                outline.Add(new Vector2(Mathf.Lerp(-length * 0.5f, length * 0.5f, t), height * 0.5f + Mathf.Sin(t * Mathf.PI) * height * 0.5f * Mathf.Lerp(0.55f, 1f, t)));
            }
            for (int i = 1; i < steps; i++)
            {
                float t = 1f - (float)i / steps;
                outline.Add(new Vector2(Mathf.Lerp(-length * 0.5f, length * 0.5f, t), height * 0.5f - Mathf.Sin(t * Mathf.PI) * height * 0.5f * Mathf.Lerp(0.55f, 1f, t)));
            }
            Mesh side = MeshKit.Extrude(outline, thickness, thickness * 0.3f);
            // Drawn in (z, y) and extruded along the third axis: turn that axis onto X.
            Mesh body = MeshKit.Merge("Level13 Fish", new[] { new MeshPart(side, Matrix4x4.Rotate(Quaternion.Euler(0f, -90f, 0f))) });
            MeshKit.Release(side);
            return body;
        }

        /// <summary>The goldfish's tail fin: a triangle in the YZ plane with its point at the origin, opening toward -Z.</summary>
        public static Mesh FishTail(float length, float height, float thickness)
        {
            var outline = new List<Vector2> { new Vector2(0f, 0f), new Vector2(-length, height * 0.5f), new Vector2(-length * 0.72f, 0f), new Vector2(-length, -height * 0.5f) };
            Mesh side = MeshKit.Extrude(outline, thickness);
            Mesh tail = MeshKit.Merge("Level13 Fish Tail", new[] { new MeshPart(side, Matrix4x4.Rotate(Quaternion.Euler(0f, -90f, 0f))) });
            MeshKit.Release(side);
            return tail;
        }
    }
}
