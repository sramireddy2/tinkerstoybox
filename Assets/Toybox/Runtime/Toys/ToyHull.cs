using System;
using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// Collision hulls of the toy catalog: closed convex meshes at their real size, built from the hull's
    /// corner points. PhysX makes its own hull from the vertices of a convex MeshCollider, but the
    /// simulation also measures a prop's volume from the collider mesh (PropGeometry), so the mesh has to
    /// be that hull, closed and without overlapping triangles - not just a cloud of points.
    /// </summary>
    internal static class ToyHull
    {
        /// <summary>A hull shared by every toy that asks with the same key. Do not modify what this returns.</summary>
        public static Mesh Cached(string key, Func<IList<Vector3>> points) =>
            MeshKit.Cached("Hull|" + key, () => Build(key + " Hull", points()));

        /// <summary>An n-sided frustum about the Y axis: the thimble, a spool, a cap.</summary>
        public static List<Vector3> Frustum(float bottomRadius, float topRadius, float bottomY, float topY, int sides)
        {
            var points = new List<Vector3>(sides * 2);
            for (int i = 0; i < sides; i++)
            {
                float angle = Mathf.PI * 2f * i / sides;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                points.Add(new Vector3(cos * bottomRadius, bottomY, sin * bottomRadius));
                points.Add(new Vector3(cos * topRadius, topY, sin * topRadius));
            }
            return points;
        }

        /// <summary>
        /// A box whose top face is drawn in by <paramref name="inset"/> on the sides named: a 45 degree
        /// chamfer when the inset equals the height. x0..x1, z0..z1 is the footprint, y0..y1 the height.
        /// </summary>
        public static List<Vector3> Chamfered(float x0, float x1, float y0, float y1, float z0, float z1,
            float insetX0, float insetX1, float insetZ0, float insetZ1)
        {
            return new List<Vector3>
            {
                new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1),
                new Vector3(x0 + insetX0, y1, z0 + insetZ0), new Vector3(x1 - insetX1, y1, z0 + insetZ0),
                new Vector3(x1 - insetX1, y1, z1 - insetZ1), new Vector3(x0 + insetX0, y1, z1 - insetZ1),
            };
        }

        /// <summary>
        /// The convex hull of the points as a closed triangle mesh, wound outward. Brute force over all
        /// triples (fine for the few dozen points a toy's hull has): a triple whose plane has every other
        /// point on one side is a face; the points on that plane are triangulated as one convex polygon, so
        /// a face with many corners (the rim of a frustum) is covered exactly once.
        /// </summary>
        public static Mesh Build(string name, IList<Vector3> source)
        {
            var points = new List<Vector3>();
            Bounds bounds = new Bounds(source[0], Vector3.zero);
            foreach (Vector3 p in source) bounds.Encapsulate(p);
            float size = Mathf.Max(bounds.size.magnitude, 1e-6f);
            float weld = size * 1e-6f;
            foreach (Vector3 p in source)
            {
                bool known = false;
                for (int i = 0; i < points.Count && !known; i++) known = (points[i] - p).sqrMagnitude <= weld * weld;
                if (!known) points.Add(p);
            }
            int count = points.Count;
            if (count < 4) throw new ArgumentException("A hull needs at least four different points.", nameof(source));
            if (count > 255) throw new ArgumentException("A convex collider has at most 255 points.", nameof(source));

            float eps = size * 2e-5f;
            var planes = new List<Vector4>();
            var triangles = new List<int>();
            var face = new List<int>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    for (int k = j + 1; k < count; k++)
                    {
                        Vector3 normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                        float length = normal.magnitude;
                        if (length < size * size * 1e-8f) continue;
                        normal /= length;
                        float d = Vector3.Dot(normal, points[i]);
                        bool above = false, below = false;
                        for (int m = 0; m < count && !(above && below); m++)
                        {
                            float side = Vector3.Dot(normal, points[m]) - d;
                            if (side > eps) above = true;
                            else if (side < -eps) below = true;
                        }
                        if (above && below) continue;
                        if (above)
                        {
                            normal = -normal;
                            d = -d;
                        }
                        bool seen = false;
                        for (int p = 0; p < planes.Count && !seen; p++)
                        {
                            Vector4 plane = planes[p];
                            seen = Vector3.Dot(new Vector3(plane.x, plane.y, plane.z), normal) > 1f - 1e-5f && Mathf.Abs(plane.w - d) <= eps * 4f;
                        }
                        if (seen) continue;
                        planes.Add(new Vector4(normal.x, normal.y, normal.z, d));

                        face.Clear();
                        for (int m = 0; m < count; m++)
                            if (Mathf.Abs(Vector3.Dot(normal, points[m]) - d) <= eps * 2f) face.Add(m);
                        AddFace(points, face, normal, triangles);
                    }

            if (triangles.Count < 12) throw new ArgumentException("The points of '" + name + "' lie in a plane; that is not a hull.", nameof(source));
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // The points of one face, in order around it (a 2D convex hull in the face's plane), as a fan.
        static void AddFace(List<Vector3> points, List<int> face, Vector3 normal, List<int> triangles)
        {
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            Vector3 centre = Vector3.zero;
            foreach (int index in face) centre += points[index];
            centre /= face.Count;
            // Every point of a hull face is on the face's outline or inside it; sorting by angle about the
            // middle and dropping the ones that do not turn the outline gives the polygon.
            face.Sort((a, b) =>
            {
                Vector3 pa = points[a] - centre, pb = points[b] - centre;
                float angleA = Mathf.Atan2(Vector3.Dot(pa, v), Vector3.Dot(pa, u));
                float angleB = Mathf.Atan2(Vector3.Dot(pb, v), Vector3.Dot(pb, u));
                int order = angleA.CompareTo(angleB);
                return order != 0 ? order : a.CompareTo(b);
            });
            for (int i = 1; i + 1 < face.Count; i++)
            {
                int a = face[0], b = face[i], c = face[i + 1];
                Vector3 cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (cross.sqrMagnitude < 1e-20f) continue;
                // Front faces run clockwise: cross(b - a, c - a) points out of them.
                bool flip = Vector3.Dot(cross, normal) < 0f;
                triangles.Add(a);
                triangles.Add(flip ? c : b);
                triangles.Add(flip ? b : c);
            }
        }
    }
}
