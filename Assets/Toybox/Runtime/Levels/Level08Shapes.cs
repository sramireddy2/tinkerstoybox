using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// The meshes Level 8 is made of that no kit has: the lid with its three round holes, bands and dashes
    /// of paint on the lid and on the cones, the ring of lamp studs round a funnel's mouth, the bars of the
    /// gate, the shaft of light over a funnel that has its marble. Angles are degrees in the XZ plane, 0
    /// along +X and 90 along +Z. Renderers only, except the lid, which is also its own collider.
    /// </summary>
    internal static class Level08Shapes
    {
        /// <summary>
        /// The top of the lid: the rectangle x0..x1, z0..z1 at y = 0, facing up, with a round hole per entry
        /// of <paramref name="holes"/> (x of the axis, radius; all on the line z = holeZ, in order of x). A
        /// hole is a polygon of <paramref name="segments"/> sides circumscribed about its radius, corner for
        /// corner the rim of a <see cref="Toybox.Gadgets.Funnel"/> with as many segments, so lid and funnel
        /// meet without a gap. Every corner is shared by the triangles round it (no T-junctions): a marble
        /// rolls over it as over one sheet. Good for a renderer and for a (non-convex) MeshCollider.
        /// </summary>
        public static Mesh Lid(float x0, float x1, float z0, float z1, float holeZ, IList<Vector2> holes, int segments)
        {
            var positions = new List<Vector3>();
            var triangles = new List<int>();
            var index = new Dictionary<long, int>();

            int Corner(float x, float z)
            {
                long key = (long)Mathf.RoundToInt(x * 2000f) * 1000003L + Mathf.RoundToInt(z * 2000f);
                if (index.TryGetValue(key, out int found)) return found;
                positions.Add(new Vector3(x, 0f, z));
                index[key] = positions.Count - 1;
                return positions.Count - 1;
            }

            void Triangle(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return;
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (Mathf.Abs(face.y) < 1e-9f) return;
                triangles.Add(a);
                triangles.Add(face.y > 0f ? b : c);
                triangles.Add(face.y > 0f ? c : b);
            }

            // A strip without a hole: two rectangles that meet on the holes' line.
            void Gap(float from, float to)
            {
                if (to - from < 1e-4f) return;
                int a = Corner(from, z0), b = Corner(to, z0), c = Corner(from, holeZ), d = Corner(to, holeZ), e = Corner(from, z1), f = Corner(to, z1);
                Triangle(a, b, d);
                Triangle(a, d, c);
                Triangle(c, d, f);
                Triangle(c, f, e);
            }

            int half = segments / 2, quarter = segments / 4;
            float circumscribe = 1f / Mathf.Cos(Mathf.PI / segments);
            float x = x0;
            for (int h = 0; h < holes.Count; h++)
            {
                float axis = holes[h].x, radius = holes[h].y * circumscribe;
                Gap(x, axis - radius);

                int Rim(int j)
                {
                    j = (j % segments + segments) % segments;
                    // The two corners on the holes' line are exactly on it (sin of pi is not quite zero).
                    if (j == 0) return Corner(axis + radius, holeZ);
                    if (j == half) return Corner(axis - radius, holeZ);
                    float angle = Mathf.PI * 2f * j / segments;
                    return Corner(axis + Mathf.Cos(angle) * radius, holeZ + Mathf.Sin(angle) * radius);
                }

                // The near part: from each of the strip's two near corners a fan to its quarter of the rim.
                int nearLeft = Corner(axis - radius, z0), nearRight = Corner(axis + radius, z0);
                for (int j = half; j < half + quarter; j++) Triangle(nearLeft, Rim(j), Rim(j + 1));
                Triangle(nearLeft, Rim(half + quarter), nearRight);
                for (int j = half + quarter; j < segments; j++) Triangle(nearRight, Rim(j), Rim(j + 1));

                // The far part, the same way.
                int farLeft = Corner(axis - radius, z1), farRight = Corner(axis + radius, z1);
                for (int j = 0; j < quarter; j++) Triangle(farRight, Rim(j), Rim(j + 1));
                Triangle(farRight, Rim(quarter), farLeft);
                for (int j = quarter; j < half; j++) Triangle(farLeft, Rim(j), Rim(j + 1));

                x = axis + radius;
            }
            Gap(x, x1);

            var normals = new List<Vector3>(positions.Count);
            var uvs = new List<Vector2>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(positions[i].x, positions[i].z));
            }
            var mesh = new Mesh { name = "Level08 Lid", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A band of paint (or of anything thin) round an upright axis: the strip between two points of a
        /// profile, (radius, height) each, given <paramref name="thickness"/> upward. Flat on a floor when
        /// the two heights are equal; lying on a cone when they are not.
        /// </summary>
        public static Mesh Band(float radius0, float y0, float radius1, float y1, float thickness, int segments = 48)
        {
            var profile = new List<Vector2>
            {
                new Vector2(radius0, y0), new Vector2(radius1, y1), new Vector2(radius1, y1 + thickness),
                new Vector2(radius0, y0 + thickness), new Vector2(radius0, y0),
            };
            Mesh mesh = MeshKit.Lathe(profile, segments);
            mesh.name = "Level08 Band";
            return mesh;
        }

        /// <summary>
        /// Dashes of paint on the inside of a cone (or on a floor): the strip between two rings about the Y
        /// axis, (radius, height) each, from one angle to another, cut into <paramref name="dashes"/> arcs
        /// that each cover <paramref name="duty"/> of their share. It faces up and toward the axis.
        /// </summary>
        public static Mesh ConeDashes(float radius0, float y0, float radius1, float y1, float fromDegrees, float toDegrees, int dashes, float duty)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            // Across the strip, outward and up; the face looks along the perpendicular that points up.
            var across = new Vector2(radius1 - radius0, y1 - y0);
            Vector2 facing = new Vector2(-across.y, across.x).normalized;
            if (facing.y < 0f) facing = -facing;
            float share = (toDegrees - fromDegrees) / dashes;
            for (int d = 0; d < dashes; d++)
            {
                float from = fromDegrees + (d + (1f - duty) * 0.5f) * share, to = from + share * duty;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) / 5f));
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(from, to, (float)s / steps) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 normal = dir * facing.x + Vector3.up * facing.y;
                    positions.Add(dir * radius0 + Vector3.up * y0);
                    positions.Add(dir * radius1 + Vector3.up * y1);
                    normals.Add(normal);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0f, s));
                    uvs.Add(new Vector2(1f, s));
                    if (s == 0) continue;
                    int p = positions.Count - 4;
                    AddFacing(triangles, positions, normal, p, p + 2, p + 1);
                    AddFacing(triangles, positions, normal, p + 1, p + 2, p + 3);
                }
            }
            var mesh = new Mesh { name = "Level08 Cone Dashes", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Adds a triangle wound so that its front looks along the normal.
        static void AddFacing(List<int> triangles, List<Vector3> positions, Vector3 normal, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            bool keep = Vector3.Dot(face, normal) >= 0f;
            triangles.Add(a);
            triangles.Add(keep ? b : c);
            triangles.Add(keep ? c : b);
        }

        /// <summary>A ring of low round studs standing on a floor (their feet at y = 0), one mesh.</summary>
        public static Mesh Studs(float ringRadius, int count, float studRadius, float height)
        {
            Mesh stud = MeshKit.Cylinder(studRadius, height, 12, Mathf.Min(studRadius, height) * 0.45f);
            var parts = new List<MeshPart>(count);
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 2f * (i + 0.5f) / count;
                parts.Add(new MeshPart(stud, new Vector3(Mathf.Cos(a) * ringRadius, height * 0.5f, Mathf.Sin(a) * ringRadius)));
            }
            Mesh merged = MeshKit.Merge("Level08 Studs", parts);
            MeshKit.Release(stud);
            return merged;
        }

        /// <summary>
        /// The gate: upright round bars between two rails, centred on the origin, <paramref name="width"/>
        /// by <paramref name="height"/>, in the XY plane.
        /// </summary>
        public static Mesh Grille(float width, float height, int bars, float barRadius, float railHeight, float depth)
        {
            var parts = new List<MeshPart>();
            Mesh bar = MeshKit.Cylinder(barRadius, height - railHeight, 10);
            float pitch = width / bars;
            for (int i = 0; i < bars; i++)
                parts.Add(new MeshPart(bar, new Vector3(-width * 0.5f + pitch * (i + 0.5f), 0f, 0f)));
            Mesh rail = MeshKit.RoundedBox(new Vector3(width, railHeight, depth), railHeight * 0.2f);
            parts.Add(new MeshPart(rail, new Vector3(0f, height * 0.5f - railHeight * 0.5f, 0f)));
            parts.Add(new MeshPart(rail, new Vector3(0f, -height * 0.5f + railHeight * 0.5f, 0f)));
            Mesh merged = MeshKit.Merge("Level08 Grille", parts);
            MeshKit.Release(bar);
            MeshKit.Release(rail);
            return merged;
        }

        /// <summary>
        /// A shaft of light: the side of an upright cone frustum from (radius0, y0) to (radius1, y1), seen
        /// from both sides, with vertex colours that run from white at the bottom to black at the top (an
        /// additive material fades with them).
        /// </summary>
        public static Mesh Shaft(float radius0, float y0, float radius1, float y1, int segments = 32)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.PI * 2f * s / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                positions.Add(dir * radius0 + Vector3.up * y0);
                positions.Add(dir * radius1 + Vector3.up * y1);
                normals.Add(dir);
                normals.Add(dir);
                uvs.Add(new Vector2((float)s / segments, 0f));
                uvs.Add(new Vector2((float)s / segments, 1f));
                colors.Add(Color.white);
                colors.Add(new Color(0f, 0f, 0f, 1f));
                if (s == 0) continue;
                int p = positions.Count - 4;
                triangles.Add(p);
                triangles.Add(p + 1);
                triangles.Add(p + 2);
                triangles.Add(p + 2);
                triangles.Add(p + 1);
                triangles.Add(p + 3);
            }
            var mesh = new Mesh { name = "Level08 Shaft", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
