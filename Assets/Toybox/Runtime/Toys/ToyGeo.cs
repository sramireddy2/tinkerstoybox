using System;
using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// Collects vertices and triangles for the shapes MeshKit has no generator for. Follows MeshKit's
    /// rules: triangles are wound to face along their vertices' normals, UVs are object-space with one
    /// repeat per unit, and the finished mesh carries the outline normal in TEXCOORD3.
    /// </summary>
    internal sealed class GeoBuilder
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        bool tinted;

        /// <summary>
        /// The vertex colour of the vertices added from now on: a tint the toy shader multiplies the base
        /// colour with (linear, not clamped). White unless set.
        /// </summary>
        public Color Tint = Color.white;

        public int Count => positions.Count;

        public Vector3 PositionOf(int vertex) => positions[vertex];

        public int Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            positions.Add(position);
            normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up);
            uvs.Add(uv);
            colors.Add(Tint);
            tinted |= Tint != Color.white;
            return positions.Count - 1;
        }

        /// <summary>A vertex whose UV follows the object-space rule: projected along the axis its normal leans to most.</summary>
        public int Add(Vector3 position, Vector3 normal) => Add(position, normal, ToyGeo.ObjectUV(position, normal));

        public void Triangle(int a, int b, int c)
        {
            // Unity's front face is the one whose corners run clockwise; cross(b - a, c - a) points out of it.
            Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (face.sqrMagnitude < 1e-16f) return;
            bool flip = Vector3.Dot(face, normals[a] + normals[b] + normals[c]) < 0f;
            triangles.Add(a);
            triangles.Add(flip ? c : b);
            triangles.Add(flip ? b : c);
        }

        /// <summary>Corners in order around the quad.</summary>
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
            if (tinted) mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshUtil.BakeOutlineNormals(mesh);
            return mesh;
        }
    }

    /// <summary>
    /// Geometry helpers of the toy catalog: seeded noise, the shapes MeshKit does not make (a subdivided
    /// rounded box, tubes, buttons), mesh operations (displacement, chipped paint) and 2D outlines.
    /// Everything is deterministic and needs no graphics device.
    /// </summary>
    internal static class ToyGeo
    {
        const float Tiny = 1e-6f;

        // ------------------------------------------------------------------------------------------------
        // Seeded noise (never UnityEngine.Random, never game.Rng: art must not disturb the simulation)
        // ------------------------------------------------------------------------------------------------

        /// <summary>FNV-1a of a name: the seed of everything random about a toy.</summary>
        public static int Seed(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        /// <summary>A number in [0, 1) for a lattice point.</summary>
        public static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(z * 1440662683) + (uint)(seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 15;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        /// <summary>Smooth value noise in [-1, 1] with one cell per unit.</summary>
        public static float Noise(Vector3 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = Smooth(p.x - x), fy = Smooth(p.y - y), fz = Smooth(p.z - z);
            float a = Mathf.Lerp(Hash01(x, y, z, seed), Hash01(x + 1, y, z, seed), fx);
            float b = Mathf.Lerp(Hash01(x, y + 1, z, seed), Hash01(x + 1, y + 1, z, seed), fx);
            float c = Mathf.Lerp(Hash01(x, y, z + 1, seed), Hash01(x + 1, y, z + 1, seed), fx);
            float d = Mathf.Lerp(Hash01(x, y + 1, z + 1, seed), Hash01(x + 1, y + 1, z + 1, seed), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz) * 2f - 1f;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>The next number in [0, 1) of a xorshift sequence.</summary>
        public static float Next(ref uint state)
        {
            unchecked
            {
                if (state == 0u) state = 0x9E3779B9u;
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFFu) / 16777216f;
            }
        }

        public static float Range(ref uint state, float min, float max) => min + (max - min) * Next(ref state);

        // ------------------------------------------------------------------------------------------------
        // Shapes
        // ------------------------------------------------------------------------------------------------

        /// <summary>The UV rule for toys: object-space, projected along the axis the normal leans to most.</summary>
        public static Vector2 ObjectUV(Vector3 p, Vector3 n)
        {
            float x = Mathf.Abs(n.x), y = Mathf.Abs(n.y), z = Mathf.Abs(n.z);
            if (y >= x && y >= z) return new Vector2(p.x, p.z);
            if (x >= z) return new Vector2(p.z, p.y);
            return new Vector2(p.x, p.y);
        }

        /// <summary>
        /// MeshKit.RoundedBox with its flat faces divided into cells of at most <paramref name="cell"/>, so
        /// that the surface can be displaced (the sponge). Same vertices along shared edges, smooth normals.
        /// </summary>
        public static Mesh GridBox(Vector3 size, float bevel, int segments, float cell)
        {
            Vector3 half = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f;
            float radius = Mathf.Clamp(bevel, 1e-4f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            segments = Mathf.Clamp(segments, 1, 8);
            Vector3 inner = half - new Vector3(radius, radius, radius);
            float[][] lines = { GridLines(half.x, radius, segments, cell), GridLines(half.y, radius, segments, cell), GridLines(half.z, radius, segments, cell) };
            var b = new GeoBuilder();
            for (int d = 0; d < 3; d++)
            {
                int u = (d + 1) % 3, v = (d + 2) % 3;
                float[] us = lines[u], vs = lines[v];
                for (int s = -1; s <= 1; s += 2)
                {
                    int first = b.Count;
                    for (int j = 0; j < vs.Length; j++)
                    {
                        for (int i = 0; i < us.Length; i++)
                        {
                            Vector3 p = Vector3.zero;
                            p[d] = s * half[d];
                            p[u] = us[i];
                            p[v] = vs[j];
                            var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                            Vector3 normal = (p - core).normalized;
                            Vector3 position = core + normal * radius;
                            b.Add(position, normal, new Vector2(position[u], position[v]));
                        }
                    }
                    for (int j = 0; j + 1 < vs.Length; j++)
                        for (int i = 0; i + 1 < us.Length; i++)
                        {
                            int a = first + j * us.Length + i;
                            b.Quad(a, a + 1, a + us.Length + 1, a + us.Length);
                        }
                }
            }
            return b.Build("Grid Box");
        }

        static float[] GridLines(float half, float radius, int segments, float cell)
        {
            var lines = new List<float>();
            for (int k = 0; k <= segments; k++)
                lines.Add(-half + radius * (1f - Mathf.Tan(Mathf.PI * 0.25f * (1f - (float)k / segments))));
            float inner = half - radius;
            int cells = cell > Tiny ? Mathf.Clamp(Mathf.CeilToInt(2f * inner / cell), 1, 64) : 1;
            for (int c = 1; c < cells; c++) lines.Add(-inner + 2f * inner * c / cells);
            for (int k = segments; k >= 0; k--)
            {
                float line = half - radius * (1f - Mathf.Tan(Mathf.PI * 0.25f * (1f - (float)k / segments)));
                if (line - lines[lines.Count - 1] > Tiny) lines.Add(line);
            }
            return lines.ToArray();
        }

        /// <summary>
        /// A circle (or an ellipse) swept along an open path. <paramref name="radiusA"/> is the half size
        /// along <paramref name="normalHint"/> (carried along the path without twisting), <paramref name="radiusB"/>
        /// the half size across it; <paramref name="taper"/> scales both along the way (0 at the start, 1 at
        /// the end of the path). The ends are open unless capped: hide them inside another part.
        /// </summary>
        public static Mesh Tube(IList<Vector3> path, float radiusA, float radiusB, int sides, Vector3 normalHint, bool caps = false, Func<float, float> taper = null)
        {
            int count = path.Count;
            sides = Mathf.Clamp(sides, 3, 64);
            var b = new GeoBuilder();
            Vector3 n = normalHint;
            float run = 0f;
            float around = Mathf.PI * (radiusA + radiusB);
            int stride = sides + 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 t = Tangent(path, i);
                n -= t * Vector3.Dot(n, t);
                if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
                n.Normalize();
                Vector3 bn = Vector3.Cross(t, n);
                float k = taper != null ? Mathf.Max(0f, taper(count > 1 ? (float)i / (count - 1) : 0f)) : 1f;
                if (i > 0) run += Vector3.Distance(path[i], path[i - 1]);
                for (int s = 0; s <= sides; s++)
                {
                    float angle = Mathf.PI * 2f * s / sides;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    Vector3 offset = n * (cos * radiusA * k) + bn * (sin * radiusB * k);
                    Vector3 normal = n * (cos * radiusB) + bn * (sin * radiusA);
                    b.Add(path[i] + offset, normal, new Vector2(run, around * s / sides));
                }
            }
            for (int i = 0; i + 1 < count; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * stride + s;
                    b.Quad(a, a + 1, a + stride + 1, a + stride);
                }
            if (caps && count > 1)
            {
                Cap(b, path[0], -Tangent(path, 0), 0, sides);
                Cap(b, path[count - 1], Tangent(path, count - 1), (count - 1) * stride, sides);
            }
            return b.Build("Tube");
        }

        static void Cap(GeoBuilder b, Vector3 centre, Vector3 normal, int ring, int sides)
        {
            // The cap gets vertices of its own (a hard edge), copied from the ring it closes.
            int middle = b.Add(centre, normal);
            int first = b.Count;
            for (int s = 0; s <= sides; s++) b.Add(b.PositionOf(ring + s), normal);
            for (int s = 0; s < sides; s++) b.Triangle(middle, first + s, first + s + 1);
        }

        static Vector3 Tangent(IList<Vector3> path, int i)
        {
            int last = path.Count - 1;
            Vector3 t = path[Mathf.Min(i + 1, last)] - path[Mathf.Max(i - 1, 0)];
            return t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.forward;
        }

        /// <summary>
        /// A button: a disc of the given radius standing on the XZ plane (y = 0), <paramref name="height"/>
        /// tall, with a softly chamfered rim. Pips, nail heads, painted holes. 3 x sides triangles.
        /// </summary>
        public static Mesh Dot(float radius, int sides, float height)
        {
            sides = Mathf.Clamp(sides, 3, 64);
            var b = new GeoBuilder();
            float top = radius * 0.86f;
            int centre = b.Add(new Vector3(0f, height, 0f), Vector3.up);
            int ring = b.Count;
            for (int s = 0; s <= sides; s++)
            {
                float angle = Mathf.PI * 2f * s / sides;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                b.Add(radial * top + Vector3.up * height, Vector3.up);
                b.Add(radial * radius, (radial + Vector3.up * 0.35f).normalized);
            }
            for (int s = 0; s < sides; s++)
            {
                int a = ring + s * 2;
                b.Triangle(centre, a, a + 2);
                b.Quad(a, a + 1, a + 3, a + 2);
            }
            return b.Build("Dot");
        }

        // ------------------------------------------------------------------------------------------------
        // Mesh operations
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Moves every vertex along its normal by <paramref name="offset"/>(position, normal) and gives the
        /// mesh smooth normals again. For meshes that shade smoothly all over (a sphere, a rounded box):
        /// vertices that share a position share a normal there, so seams stay closed.
        /// </summary>
        public static void Displace(Mesh mesh, Func<Vector3, Vector3, float> offset)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            var keys = new Vector3[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                keys[i] = positions[i];
                positions[i] += normals[i] * offset(positions[i], normals[i]);
            }
            mesh.SetVertices(positions);
            SmoothNormals(mesh, keys);
            mesh.RecalculateBounds();
            MeshUtil.BakeOutlineNormals(mesh);
        }

        /// <summary>
        /// Area-weighted smooth normals, shared by all vertices with the same key position (the positions
        /// before a displacement, so that vertices which were welded stay welded).
        /// </summary>
        public static void SmoothNormals(Mesh mesh, Vector3[] keys = null)
        {
            Vector3[] positions = mesh.vertices;
            int[] triangles = mesh.triangles;
            keys ??= positions;
            Bounds bounds = mesh.bounds;
            float cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 1e-5f;
            if (cell <= 0f) cell = 1e-6f;
            var group = new int[positions.Length];
            var index = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = keys[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                if (!index.TryGetValue(key, out int g)) index[key] = g = index.Count;
                group[i] = g;
            }
            var sums = new Vector3[index.Count];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                sums[group[a]] += face;
                sums[group[b]] += face;
                sums[group[c]] += face;
            }
            Vector3[] old = mesh.normals;
            var normals = new Vector3[positions.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 sum = sums[group[i]];
                normals[i] = sum.sqrMagnitude > 1e-20f ? sum.normalized : (i < old.Length ? old[i] : Vector3.up);
            }
            mesh.normals = normals;
        }

        /// <summary>How <see cref="Chip"/> recognises the vertices of a bevel.</summary>
        public enum Bevels
        {
            /// <summary>A rounded box: the normal is not along an axis.</summary>
            Box,
            /// <summary>A lathed shape about Y: the normal is neither radial nor along the axis.</summary>
            Lathe,
        }

        /// <summary>
        /// Chipped paint (ART_BIBLE 4.3, painted wood): tints the vertices of the bevels up to 30% toward
        /// Birch with seeded noise. The tint is a vertex colour, which the toy shader multiplies the base
        /// colour with, so it is the ratio of the two colours in linear light.
        /// </summary>
        public static void Chip(Mesh mesh, Color paint, int seed, Bevels bevels = Bevels.Box, float amount = 0.3f)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            Color from = Palette.Lin(paint), to = Palette.Lin(Palette.Birch);
            var ratio = new Color(Ratio(to.r, from.r), Ratio(to.g, from.g), Ratio(to.b, from.b), 1f);
            float size = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            float frequency = size > Tiny ? 7f / size : 1f;
            var colors = new Color[positions.Count];
            for (int i = 0; i < colors.Length; i++)
            {
                Vector3 n = normals[i];
                bool bevel;
                if (bevels == Bevels.Lathe) bevel = Mathf.Abs(n.y) > 0.08f && Mathf.Abs(n.y) < 0.996f;
                else bevel = Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z))) < 0.996f;
                if (!bevel)
                {
                    colors[i] = Color.white;
                    continue;
                }
                // The same for every vertex at a position, whichever face it belongs to.
                float noise = Noise(positions[i] * frequency + new Vector3(17.3f, 5.1f, 9.7f), seed) * 0.5f + 0.5f;
                float t = amount * Mathf.SmoothStep(0.25f, 0.8f, noise);
                colors[i] = Color.LerpUnclamped(Color.white, ratio, t);
                colors[i].a = 1f;
            }
            mesh.SetColors(colors);
        }

        static float Ratio(float to, float from) => Mathf.Min(to / Mathf.Max(from, 0.02f), 6f);

        // ------------------------------------------------------------------------------------------------
        // 2D outlines and profiles
        // ------------------------------------------------------------------------------------------------

        public static List<Vector2> Circle(float radius, int sides, float phase = 0f)
        {
            var points = new List<Vector2>(sides);
            for (int i = 0; i < sides; i++)
            {
                float angle = phase + Mathf.PI * 2f * i / sides;
                points.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }
            return points;
        }

        /// <summary>A rectangle centred on the origin with rounded corners, counter-clockwise.</summary>
        public static List<Vector2> RoundedRect(float width, float height, float radius, int segments)
        {
            float w = width * 0.5f, h = height * 0.5f;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(w, h));
            var points = new List<Vector2>();
            if (r <= Tiny)
            {
                points.Add(new Vector2(w, -h));
                points.Add(new Vector2(w, h));
                points.Add(new Vector2(-w, h));
                points.Add(new Vector2(-w, -h));
                return points;
            }
            segments = Mathf.Clamp(segments, 1, 32);
            var centres = new[] { new Vector2(w - r, -h + r), new Vector2(w - r, h - r), new Vector2(-w + r, h - r), new Vector2(-w + r, -h + r) };
            for (int corner = 0; corner < 4; corner++)
            {
                float start = -Mathf.PI * 0.5f + corner * Mathf.PI * 0.5f;
                for (int k = 0; k <= segments; k++)
                {
                    float angle = start + Mathf.PI * 0.5f * k / segments;
                    points.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
                }
            }
            return points;
        }

        /// <summary>
        /// Rounds the corners of a polyline (or of a closed outline): each corner is replaced by a curve
        /// that leaves the two edges at most <paramref name="radius"/> from it (less on short edges).
        /// Lathe profiles and extruded outlines then shade smoothly across what were hard edges.
        /// </summary>
        public static List<Vector2> Fillet(IList<Vector2> points, float radius, int segments, bool closed = false)
        {
            int count = points.Count;
            segments = Mathf.Clamp(segments, 1, 16);
            var result = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                bool end = !closed && (i == 0 || i == count - 1);
                Vector2 p = points[i];
                if (end)
                {
                    result.Add(p);
                    continue;
                }
                Vector2 previous = points[(i + count - 1) % count], next = points[(i + 1) % count];
                float before = (p - previous).magnitude, after = (next - p).magnitude;
                if (before < Tiny || after < Tiny)
                {
                    result.Add(p);
                    continue;
                }
                Vector2 d1 = (p - previous) / before, d2 = (next - p) / after;
                if (Vector2.Dot(d1, d2) > 0.9995f)
                {
                    result.Add(p);
                    continue;
                }
                float reach = Mathf.Min(radius, 0.45f * Mathf.Min(before, after));
                Vector2 a = p - d1 * reach, b = p + d2 * reach;
                for (int k = 0; k <= segments; k++)
                {
                    float s = (float)k / segments;
                    result.Add(a * ((1f - s) * (1f - s)) + p * (2f * s * (1f - s)) + b * (s * s));
                }
            }
            return result;
        }

        /// <summary>A Catmull-Rom curve through the points, with <paramref name="subdivisions"/> steps per span.</summary>
        public static List<Vector2> Spline(IList<Vector2> points, int subdivisions)
        {
            int count = points.Count;
            subdivisions = Mathf.Clamp(subdivisions, 1, 16);
            var result = new List<Vector2>();
            for (int i = 0; i + 1 < count; i++)
            {
                Vector2 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(i + 2, count - 1)];
                for (int k = 0; k < subdivisions; k++)
                {
                    float t = (float)k / subdivisions, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(points[count - 1]);
            return result;
        }
    }
}
