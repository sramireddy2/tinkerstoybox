using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Art
{
    /// <summary>One input of <see cref="MeshKit.Merge"/>: a mesh, where it goes, and a vertex-colour tint.</summary>
    public struct MeshPart
    {
        public Mesh Mesh;
        public Matrix4x4 Matrix;
        /// <summary>Multiplied into the part's vertex colours (white when the mesh has none).</summary>
        public Color Color;

        public MeshPart(Mesh mesh, Matrix4x4 matrix)
        {
            Mesh = mesh;
            Matrix = matrix;
            Color = Color.white;
        }

        public MeshPart(Mesh mesh, Matrix4x4 matrix, Color color)
        {
            Mesh = mesh;
            Matrix = matrix;
            Color = color;
        }

        public MeshPart(Mesh mesh, Vector3 position) : this(mesh, Matrix4x4.Translate(position)) { }

        public MeshPart(Mesh mesh, Vector3 position, Quaternion rotation) : this(mesh, Matrix4x4.TRS(position, rotation, Vector3.one)) { }

        public MeshPart(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) : this(mesh, Matrix4x4.TRS(position, rotation, scale)) { }
    }

    /// <summary>
    /// Procedural meshes at their real size (ART_BIBLE 4.1 and 6.3). Every mesh made here has unit normals,
    /// object-space UVs with one repeat per unit (TEXCOORD0), the outline normal for the sticker pass
    /// (TEXCOORD3, see <see cref="MeshUtil.BakeOutlineNormals"/>), triangles that face along their normals,
    /// and bounds. Nothing here needs a graphics device, and the same arguments always give the same mesh.
    ///
    /// Each call builds a new readable mesh (HideFlags.DontSave) that the caller owns; share identical
    /// ones through <see cref="Cached"/>. Call mesh.UploadMeshData(true) once nothing reads it any more
    /// (not when a MeshCollider uses it).
    /// </summary>
    public static class MeshKit
    {
        /// <summary>Adjacent faces that turn by no more than this share a smooth normal (lathe profiles, extruded outlines).</summary>
        public const float DefaultSmoothAngle = 50f;

        const float Tiny = 1e-6f;

        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        static Mesh unitCylinderHull, unitWedgeHull;

        /// <summary>
        /// A mesh shared by everyone who asks with the same key; built on first use and kept for the
        /// session. Do not modify or destroy what this returns.
        /// </summary>
        public static Mesh Cached(string key, Func<Mesh> build)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (build == null) throw new ArgumentNullException(nameof(build));
            // An entry can be a destroyed object if something unloaded all assets; build it again then.
            if (Cache.TryGetValue(key, out Mesh mesh) && mesh != null) return mesh;
            mesh = build();
            if (mesh != null && string.IsNullOrEmpty(mesh.name)) mesh.name = key;
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>A cache key from a name and numbers, exact to the bit: "Box|1|0.5|2".</summary>
        public static string Key(string name, params float[] numbers)
        {
            var text = new System.Text.StringBuilder(name);
            foreach (float number in numbers)
                text.Append('|').Append(number.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return text.ToString();
        }

        // ------------------------------------------------------------------------------------------------
        // Boxes
        // ------------------------------------------------------------------------------------------------

        /// <summary>A hard-edged box centred on the origin: 12 triangles. For world geometry; toys take <see cref="RoundedBox"/>.</summary>
        public static Mesh Box(Vector3 size)
        {
            Vector3 half = size * 0.5f;
            var b = new Builder();
            for (int d = 0; d < 3; d++)
            {
                int u = (d + 1) % 3, v = (d + 2) % 3;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 normal = Vector3.zero;
                    normal[d] = s;
                    int first = b.Count;
                    for (int j = -1; j <= 1; j += 2)
                    {
                        for (int i = -1; i <= 1; i += 2)
                        {
                            Vector3 p = Vector3.zero;
                            p[d] = s * half[d];
                            p[u] = i * half[u];
                            p[v] = j * half[v];
                            b.Add(p, normal, new Vector2(p[u], p[v]));
                        }
                    }
                    b.Quad(first, first + 1, first + 3, first + 2);
                }
            }
            return b.Build("Box");
        }

        /// <summary>
        /// A box centred on the origin whose edges and corners are rounded with radius <paramref name="bevel"/>,
        /// with smooth normals across the rounding. <paramref name="segments"/> is the number of steps across
        /// each bevel (1 is a chamfer that still shades smoothly; 2 is the default for toys). The bevel is cut
        /// down to half the smallest dimension. Triangles: 12 * (segments + 1)^2 ... 24 * (segments + 1)^2 + ...;
        /// 300 at two segments.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float bevel, int segments = 2)
        {
            Vector3 half = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f;
            float radius = Mathf.Clamp(bevel, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            if (radius <= Tiny) return Box(half * 2f);
            segments = Mathf.Clamp(segments, 1, 8);

            Vector3 inner = half - new Vector3(radius, radius, radius);
            float[][] lines = { AxisLines(half.x, radius, segments), AxisLines(half.y, radius, segments), AxisLines(half.z, radius, segments) };
            var b = new Builder();
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
                            // A point of the sharp box, pulled onto the rounded one: out from the nearest point
                            // of the inner box by the radius. Two faces compute the same thing along their
                            // common edge, so the seams are closed and shade smoothly.
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
            return b.Build("Rounded Box");
        }

        // Grid lines of one axis of a rounded box: dense across the two bevels, nothing in between. The
        // spacing puts the bevel's normals at even angles between a face normal and the 45 degree edge.
        static float[] AxisLines(float half, float radius, int segments)
        {
            var lines = new List<float>();
            for (int k = 0; k <= segments; k++)
                lines.Add(-half + radius * (1f - Mathf.Tan(Mathf.PI * 0.25f * (1f - (float)k / segments))));
            for (int k = segments; k >= 0; k--)
            {
                float line = half - radius * (1f - Mathf.Tan(Mathf.PI * 0.25f * (1f - (float)k / segments)));
                // When the bevel is the whole half size the two inner lines coincide.
                if (line - lines[lines.Count - 1] > Tiny) lines.Add(line);
            }
            return lines.ToArray();
        }

        // ------------------------------------------------------------------------------------------------
        // Round things
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// An upright cylinder (axis along Y) centred on the origin. With a bevel its two rims are rounded
        /// in <paramref name="bevelSegments"/> steps; without, they are hard edges.
        /// </summary>
        public static Mesh Cylinder(float radius, float height, int sides = 24, float bevel = 0f, int bevelSegments = 2)
        {
            float half = Mathf.Abs(height) * 0.5f;
            radius = Mathf.Abs(radius);
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(radius, half));
            var profile = new List<Vector2> { new Vector2(0f, -half) };
            if (b <= Tiny)
            {
                profile.Add(new Vector2(radius, -half));
                profile.Add(new Vector2(radius, half));
            }
            else
            {
                bevelSegments = Mathf.Clamp(bevelSegments, 1, 8);
                for (int k = 0; k <= bevelSegments; k++)
                {
                    float angle = Mathf.PI * 0.5f * k / bevelSegments;
                    profile.Add(new Vector2(radius - b + b * Mathf.Sin(angle), -half + b - b * Mathf.Cos(angle)));
                }
                for (int k = 0; k <= bevelSegments; k++)
                {
                    float angle = Mathf.PI * 0.5f * k / bevelSegments;
                    profile.Add(new Vector2(radius - b + b * Mathf.Cos(angle), half - b + b * Mathf.Sin(angle)));
                }
            }
            profile.Add(new Vector2(0f, half));
            Mesh mesh = Lathe(profile, sides);
            mesh.name = "Cylinder";
            return mesh;
        }

        /// <summary>A sphere centred on the origin, with exact normals.</summary>
        public static Mesh Sphere(float radius, int segments = 24, int rings = 16)
        {
            segments = Mathf.Clamp(segments, 3, 256);
            rings = Mathf.Clamp(rings, 2, 256);
            radius = Mathf.Abs(radius);
            var b = new Builder();
            int stride = segments + 1;
            for (int ring = 0; ring <= rings; ring++)
            {
                float polar = Mathf.PI * ring / rings;
                float y = -Mathf.Cos(polar), ringRadius = Mathf.Sin(polar);
                for (int segment = 0; segment <= segments; segment++)
                {
                    float angle = Mathf.PI * 2f * segment / segments;
                    var normal = new Vector3(Mathf.Cos(angle) * ringRadius, y, Mathf.Sin(angle) * ringRadius);
                    b.Add(normal * radius, normal, new Vector2(angle * radius, polar * radius));
                }
            }
            for (int ring = 0; ring < rings; ring++)
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = ring * stride + segment;
                    b.Quad(a, a + 1, a + stride + 1, a + stride);
                }
            return b.Build("Sphere");
        }

        /// <summary>
        /// A surface of revolution about the Y axis. The profile is a polyline of (radius, height) points
        /// running from the bottom of the shape to its top; start and end on the axis (radius 0) for a
        /// closed solid. Where the profile turns by more than <paramref name="smoothAngle"/> degrees the
        /// edge is hard, otherwise it shades smoothly. U runs around the axis (one repeat per unit at the
        /// widest radius), V along the profile.
        /// </summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments, float smoothAngle = DefaultSmoothAngle)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            segments = Mathf.Clamp(segments, 3, 256);

            var points = new List<Vector2>();
            foreach (Vector2 point in profile)
            {
                var clean = new Vector2(Mathf.Max(0f, point.x), point.y);
                if (points.Count == 0 || (clean - points[points.Count - 1]).sqrMagnitude > Tiny * Tiny) points.Add(clean);
            }
            if (points.Count < 2) throw new ArgumentException("A lathe profile needs at least two different points.", nameof(profile));

            // Outward is to the right of a profile that runs upward. One that was given top to bottom is turned round.
            float swept = 0f;
            for (int i = 0; i + 1 < points.Count; i++) swept += (points[i].x + points[i + 1].x) * (points[i + 1].y - points[i].y);
            if (swept < 0f) points.Reverse();

            int count = points.Count;
            var edgeNormals = new Vector2[count - 1];
            float widest = 0f;
            for (int i = 0; i < count; i++) widest = Mathf.Max(widest, points[i].x);
            for (int i = 0; i + 1 < count; i++)
            {
                Vector2 along = points[i + 1] - points[i];
                edgeNormals[i] = new Vector2(along.y, -along.x).normalized;
            }

            // One row of vertices per profile point, two where the profile has a hard corner.
            var rowPoint = new List<Vector2>();
            var rowNormal = new List<Vector2>();
            var rowV = new List<float>();
            var lower = new int[count - 1];
            var upper = new int[count - 1];
            float cosSmooth = Mathf.Cos(Mathf.Clamp(smoothAngle, 0f, 180f) * Mathf.Deg2Rad);
            float length = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) length += (points[i] - points[i - 1]).magnitude;
                bool first = i == 0, last = i == count - 1;
                bool smooth = !first && !last && Vector2.Dot(edgeNormals[i - 1], edgeNormals[i]) >= cosSmooth - 1e-5f;
                if (first || last || smooth)
                {
                    // Weighted by the length of the two profile segments, for the reason given in Extrude:
                    // a long straight stretch (a flat cap, a cylinder's side) keeps its own normal up to
                    // the rounded corner next to it.
                    Vector2 normal = first ? edgeNormals[0] : last ? edgeNormals[count - 2]
                        : (edgeNormals[i - 1] * (points[i] - points[i - 1]).magnitude + edgeNormals[i] * (points[i + 1] - points[i]).magnitude).normalized;
                    rowPoint.Add(points[i]);
                    rowNormal.Add(normal);
                    rowV.Add(length);
                    if (!first) upper[i - 1] = rowPoint.Count - 1;
                    if (!last) lower[i] = rowPoint.Count - 1;
                }
                else
                {
                    rowPoint.Add(points[i]);
                    rowNormal.Add(edgeNormals[i - 1]);
                    rowV.Add(length);
                    upper[i - 1] = rowPoint.Count - 1;
                    rowPoint.Add(points[i]);
                    rowNormal.Add(edgeNormals[i]);
                    rowV.Add(length);
                    lower[i] = rowPoint.Count - 1;
                }
            }

            var b = new Builder();
            int stride = segments + 1;
            for (int row = 0; row < rowPoint.Count; row++)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float angle = Mathf.PI * 2f * s / segments;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    Vector2 p = rowPoint[row], n = rowNormal[row];
                    b.Add(new Vector3(p.x * cos, p.y, p.x * sin), new Vector3(n.x * cos, n.y, n.x * sin), new Vector2(angle * widest, rowV[row]));
                }
            }
            for (int i = 0; i + 1 < count; i++)
                for (int s = 0; s < segments; s++)
                {
                    int a = lower[i] * stride + s, c = upper[i] * stride + s;
                    // On the axis the quad is a triangle; the builder drops the degenerate half.
                    b.Quad(a, a + 1, c + 1, c);
                }
            return b.Build("Lathe");
        }

        // ------------------------------------------------------------------------------------------------
        // Flat things
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A 2D outline in the XY plane (a simple polygon without holes, either winding) given thickness
        /// along Z, centred on z = 0. With a bevel the rim of both faces is chamfered by that much, shaded
        /// smoothly. Outline corners sharper than <paramref name="smoothAngle"/> stay hard edges.
        /// </summary>
        public static Mesh Extrude(IList<Vector2> shape, float depth, float bevel = 0f, float smoothAngle = DefaultSmoothAngle)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var outline = new List<Vector2>();
            foreach (Vector2 point in shape)
                if (outline.Count == 0 || (point - outline[outline.Count - 1]).sqrMagnitude > Tiny * Tiny) outline.Add(point);
            if (outline.Count > 1 && (outline[0] - outline[outline.Count - 1]).sqrMagnitude <= Tiny * Tiny) outline.RemoveAt(outline.Count - 1);
            if (outline.Count < 3) throw new ArgumentException("An outline needs at least three different points.", nameof(shape));
            if (SignedArea(outline) < 0f) outline.Reverse();

            int count = outline.Count;
            float half = Mathf.Abs(depth) * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, half);

            // Outward normals of the edges (edge i runs from point i to point i + 1) and the mitred inset.
            var edgeNormals = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 along = (outline[(i + 1) % count] - outline[i]).normalized;
                edgeNormals[i] = new Vector2(along.y, -along.x);
            }
            var inset = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 before = edgeNormals[(i + count - 1) % count], after = edgeNormals[i];
                float denominator = 1f + Vector2.Dot(before, after);
                Vector2 offset = denominator > 0.15f ? (before + after) / denominator : (before + after).normalized * 3f;
                inset[i] = outline[i] - Vector2.ClampMagnitude(offset, 3f) * b;
            }

            var builder = new Builder();
            List<int> triangles = Triangulate(outline);

            // The two faces.
            int front = builder.Count;
            for (int i = 0; i < count; i++) builder.Add(new Vector3(inset[i].x, inset[i].y, half), Vector3.forward, inset[i]);
            int back = builder.Count;
            for (int i = 0; i < count; i++) builder.Add(new Vector3(inset[i].x, inset[i].y, -half), Vector3.back, inset[i]);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                builder.Triangle(front + triangles[t], front + triangles[t + 1], front + triangles[t + 2]);
                builder.Triangle(back + triangles[t], back + triangles[t + 1], back + triangles[t + 2]);
            }

            // The wall: per outline point one column of two vertices, or two columns at a hard corner.
            float cosSmooth = Mathf.Cos(Mathf.Clamp(smoothAngle, 0f, 180f) * Mathf.Deg2Rad);
            var startColumn = new int[count];   // the column edge i starts with (at point i)
            var endColumn = new int[count];     // the column edge i ends with (at point i + 1)
            float wall = half - b;
            float run = 0f;
            float perimeter = 0f;
            var edgeLengths = new float[count];
            for (int i = 0; i < count; i++)
            {
                edgeLengths[i] = (outline[(i + 1) % count] - outline[i]).magnitude;
                perimeter += edgeLengths[i];
            }
            for (int i = 0; i < count; i++)
            {
                int previous = (i + count - 1) % count;
                bool smooth = Vector2.Dot(edgeNormals[previous], edgeNormals[i]) >= cosSmooth - 1e-5f;
                Vector2 p = outline[i];
                if (smooth)
                {
                    // Weighted by the length of the edges: where a long straight side meets the short chords
                    // of a rounded corner, the side keeps its own normal from end to end (a flat face is lit
                    // flat) and the corner takes the whole turn. An evenly sampled curve is unaffected.
                    Vector2 n = (edgeNormals[previous] * edgeLengths[previous] + edgeNormals[i] * edgeLengths[i]).normalized;
                    int column = builder.Count;
                    builder.Add(new Vector3(p.x, p.y, wall), new Vector3(n.x, n.y, 0f), new Vector2(run, wall));
                    builder.Add(new Vector3(p.x, p.y, -wall), new Vector3(n.x, n.y, 0f), new Vector2(run, -wall));
                    startColumn[i] = column;
                    if (i == 0)
                    {
                        // The seam of the U coordinate: the last edge ends at the full perimeter, not back at zero.
                        int seam = builder.Count;
                        builder.Add(new Vector3(p.x, p.y, wall), new Vector3(n.x, n.y, 0f), new Vector2(perimeter, wall));
                        builder.Add(new Vector3(p.x, p.y, -wall), new Vector3(n.x, n.y, 0f), new Vector2(perimeter, -wall));
                        endColumn[previous] = seam;
                    }
                    else
                    {
                        endColumn[previous] = column;
                    }
                }
                else
                {
                    Vector2 n0 = edgeNormals[previous], n1 = edgeNormals[i];
                    int column = builder.Count;
                    // The seam of the U coordinate falls on the first point: the last edge ends at the full perimeter.
                    float endRun = i == 0 ? perimeter : run;
                    builder.Add(new Vector3(p.x, p.y, wall), new Vector3(n0.x, n0.y, 0f), new Vector2(endRun, wall));
                    builder.Add(new Vector3(p.x, p.y, -wall), new Vector3(n0.x, n0.y, 0f), new Vector2(endRun, -wall));
                    builder.Add(new Vector3(p.x, p.y, wall), new Vector3(n1.x, n1.y, 0f), new Vector2(run, wall));
                    builder.Add(new Vector3(p.x, p.y, -wall), new Vector3(n1.x, n1.y, 0f), new Vector2(run, -wall));
                    endColumn[previous] = column;
                    startColumn[i] = column + 2;
                }
                run += (outline[(i + 1) % count] - outline[i]).magnitude;
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int a = startColumn[i], c = endColumn[i];
                if (wall > Tiny) builder.Quad(a, c, c + 1, a + 1);
                if (b > Tiny)
                {
                    // The chamfers reuse the face's and the wall's vertices, so the shading runs smoothly from one to the other.
                    builder.Quad(front + i, front + next, c, a);
                    builder.Quad(back + i, back + next, c + 1, a + 1);
                }
            }
            return builder.Build("Extrusion");
        }

        /// <summary>
        /// A ramp centred on the origin: <paramref name="length"/> along Z, <paramref name="width"/> along X,
        /// rising from nothing at the -Z end to <paramref name="height"/> at the +Z end. The bevel chamfers
        /// the rims of its two triangular sides.
        /// </summary>
        public static Mesh Wedge(float width, float height, float length, float bevel = 0f)
        {
            float l = Mathf.Abs(length) * 0.5f, h = Mathf.Abs(height) * 0.5f;
            var profile = new List<Vector2> { new Vector2(-l, -h), new Vector2(l, -h), new Vector2(l, h) };
            Mesh side = Extrude(profile, Mathf.Abs(width), bevel);
            // The outline was drawn in (z, y) and extruded along the third axis: turn that axis onto X.
            Mesh wedge = Merge("Wedge", new[] { new MeshPart(side, Matrix4x4.Rotate(Quaternion.Euler(0f, -90f, 0f))) });
            Release(side);
            return wedge;
        }

        /// <summary>Destroys a mesh this class made that nothing uses any more (an intermediate of a merge).</summary>
        public static void Release(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }

        /// <summary>A flat rectangle in the XZ plane facing up, centred on the origin and divided into cells.</summary>
        public static Mesh Grid(Vector2 size, int cellsX, int cellsZ)
        {
            cellsX = Mathf.Clamp(cellsX, 1, 255);
            cellsZ = Mathf.Clamp(cellsZ, 1, 255);
            var b = new Builder();
            for (int j = 0; j <= cellsZ; j++)
                for (int i = 0; i <= cellsX; i++)
                {
                    float x = size.x * ((float)i / cellsX - 0.5f), z = size.y * ((float)j / cellsZ - 0.5f);
                    b.Add(new Vector3(x, 0f, z), Vector3.up, new Vector2(x, z));
                }
            int stride = cellsX + 1;
            for (int j = 0; j < cellsZ; j++)
                for (int i = 0; i < cellsX; i++)
                {
                    int a = j * stride + i;
                    b.Quad(a, a + 1, a + stride + 1, a + stride);
                }
            return b.Build("Grid");
        }

        // ------------------------------------------------------------------------------------------------
        // Merging
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// One mesh from many (one draw call per material is the budget). Positions, normals and outline
        /// normals are transformed by each part's matrix; UVs are copied; vertex colours are multiplied by
        /// the part's colour and only written if any part ends up non-white. Outline normals are averaged
        /// again across the merged mesh, so parts that touch get one silhouette. Sources must be readable.
        /// </summary>
        public static Mesh Merge(string name, IList<MeshPart> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var partPositions = new List<Vector3>();
            var partNormals = new List<Vector3>();
            var partUvs = new List<Vector2>();
            var partColors = new List<Color>();
            var partTriangles = new List<int>();
            bool tinted = false;

            for (int p = 0; p < parts.Count; p++)
            {
                Mesh mesh = parts[p].Mesh;
                if (mesh == null) continue;
                Matrix4x4 matrix = parts[p].Matrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                bool mirrored = matrix.determinant < 0f;
                Color tint = parts[p].Color;

                mesh.GetVertices(partPositions);
                mesh.GetNormals(partNormals);
                mesh.GetUVs(0, partUvs);
                mesh.GetColors(partColors);
                int offset = positions.Count;
                for (int i = 0; i < partPositions.Count; i++)
                {
                    positions.Add(matrix.MultiplyPoint3x4(partPositions[i]));
                    Vector3 normal = i < partNormals.Count ? normalMatrix.MultiplyVector(partNormals[i]) : Vector3.up;
                    normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up);
                    uvs.Add(i < partUvs.Count ? partUvs[i] : Vector2.zero);
                    Color color = (i < partColors.Count ? partColors[i] : Color.white) * tint;
                    if (color != Color.white) tinted = true;
                    colors.Add(color);
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    mesh.GetTriangles(partTriangles, sub);
                    for (int t = 0; t + 2 < partTriangles.Count; t += 3)
                    {
                        triangles.Add(offset + partTriangles[t]);
                        triangles.Add(offset + partTriangles[mirrored ? t + 2 : t + 1]);
                        triangles.Add(offset + partTriangles[mirrored ? t + 1 : t + 2]);
                    }
                }
            }

            var merged = new Mesh { name = name ?? "Merged", hideFlags = HideFlags.DontSave };
            if (positions.Count > 65535) merged.indexFormat = IndexFormat.UInt32;
            merged.SetVertices(positions);
            merged.SetNormals(normals);
            merged.SetUVs(0, uvs);
            if (tinted) merged.SetColors(colors);
            merged.SetTriangles(triangles, 0);
            merged.RecalculateBounds();
            MeshUtil.BakeOutlineNormals(merged);
            return merged;
        }

        // ------------------------------------------------------------------------------------------------
        // Collision hulls
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Collision hull of an upright cylinder: a 24-sided prism in the box [-0.5, 0.5]^3, sized with the
        /// collider's transform. Shared; do not modify. (Kept vertex for vertex as the simulation was tuned with.)
        /// </summary>
        public static Mesh UnitCylinderHull => unitCylinderHull != null ? unitCylinderHull : unitCylinderHull = BuildCylinderHull();

        /// <summary>Collision hull of a ramp in the box [-0.5, 0.5]^3: full height at +Z, nothing at -Z. Shared; do not modify.</summary>
        public static Mesh UnitWedgeHull => unitWedgeHull != null ? unitWedgeHull : unitWedgeHull = BuildWedgeHull();

        static Mesh BuildCylinderHull()
        {
            const int sides = 24;
            const float h = 0.5f, r = 0.5f;
            var hull = new HullBuilder();
            var top = new Vector3[sides];
            var bottom = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                // Clockwise seen from above, which is the outward winding for the top cap.
                float angle = -i * Mathf.PI * 2f / sides;
                var rim = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                top[i] = rim + Vector3.up * h;
                bottom[sides - 1 - i] = rim + Vector3.down * h;
            }
            hull.Face(top);
            hull.Face(bottom);
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Vector3 a = top[i], c = top[next];
                Vector3 normalA = new Vector3(a.x, 0f, a.z).normalized, normalC = new Vector3(c.x, 0f, c.z).normalized;
                int v0 = hull.Vertex(a, normalA);
                int v1 = hull.Vertex(new Vector3(a.x, -h, a.z), normalA);
                int v2 = hull.Vertex(new Vector3(c.x, -h, c.z), normalC);
                int v3 = hull.Vertex(c, normalC);
                hull.Triangle(v0, v2, v3);
                hull.Triangle(v0, v1, v2);
            }
            return hull.Build("Cylinder Hull");
        }

        static Mesh BuildWedgeHull()
        {
            const float h = 0.5f;
            Vector3 backLeft = new Vector3(-h, -h, -h), backRight = new Vector3(h, -h, -h);
            Vector3 frontLeft = new Vector3(-h, -h, h), frontRight = new Vector3(h, -h, h);
            Vector3 topLeft = new Vector3(-h, h, h), topRight = new Vector3(h, h, h);
            var hull = new HullBuilder();
            hull.Face(frontLeft, backLeft, backRight, frontRight);  // bottom
            hull.Face(backLeft, topLeft, topRight, backRight);      // slope
            hull.Face(frontRight, topRight, topLeft, frontLeft);    // tall face (+Z)
            hull.Face(frontLeft, topLeft, backLeft);                // left side
            hull.Face(backRight, topRight, frontRight);             // right side
            return hull.Build("Wedge Hull");
        }

        // The mesh builder of milestone 1, kept for the two hulls so that their vertex data is unchanged.
        sealed class HullBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public int Vertex(Vector3 position, Vector3 normal)
            {
                vertices.Add(position);
                normals.Add(normal);
                return vertices.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            /// <summary>A flat polygon with its own vertices. Corners are given clockwise as seen from outside.</summary>
            public void Face(params Vector3[] corners)
            {
                Vector3 normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).normalized;
                int first = vertices.Count;
                foreach (Vector3 corner in corners) Vertex(corner, normal);
                for (int i = 1; i + 1 < corners.Length; i++) Triangle(first, first + i, first + i + 1);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------------------------------------

        static float SignedArea(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        // Ear clipping of a simple counter-clockwise polygon: indices into it, three per triangle.
        static List<int> Triangulate(List<Vector2> polygon)
        {
            var result = new List<int>();
            var remaining = new List<int>();
            for (int i = 0; i < polygon.Count; i++) remaining.Add(i);

            int guard = polygon.Count * polygon.Count + 16;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count], ib = remaining[i], ic = remaining[(i + 1) % remaining.Count];
                    Vector2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                    if (Cross(b - a, c - b) <= 1e-9f) continue;   // a reflex or flat corner is not an ear
                    bool empty = true;
                    for (int k = 0; k < remaining.Count && empty; k++)
                    {
                        int other = remaining[k];
                        if (other == ia || other == ib || other == ic) continue;
                        if (InTriangle(polygon[other], a, b, c)) empty = false;
                    }
                    if (!empty) continue;
                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                // A degenerate outline (all corners flat): close it with a fan rather than loop forever.
                if (!clipped) break;
            }
            for (int i = 1; i + 1 < remaining.Count; i++)
            {
                result.Add(remaining[0]);
                result.Add(remaining[i]);
                result.Add(remaining[i + 1]);
            }
            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d0 = Cross(b - a, p - a), d1 = Cross(c - b, p - b), d2 = Cross(a - c, p - c);
            return d0 >= 0f && d1 >= 0f && d2 >= 0f;
        }

        /// <summary>
        /// Collects vertices and triangles. Triangles are wound to face along their vertices' normals
        /// whatever order they are given in, and degenerate ones are dropped.
        /// </summary>
        sealed class Builder
        {
            readonly List<Vector3> positions = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            public int Count => positions.Count;

            public int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                positions.Add(position);
                normals.Add(normal);
                uvs.Add(uv);
                return positions.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                // Unity's front face is the one whose corners run clockwise; cross(b - a, c - a) points out of it.
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (face.sqrMagnitude < 1e-14f) return;
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
                if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                MeshUtil.BakeOutlineNormals(mesh);
                return mesh;
            }
        }
    }

    /// <summary>Operations on meshes that already exist (ART_BIBLE 4.1).</summary>
    public static class MeshUtil
    {
        /// <summary>
        /// Writes the outline normal into TEXCOORD3: for every vertex, the average of the (different)
        /// normals of all vertices at the same position. A hard-edged mesh extruded along its own normals
        /// tears open at every edge; extruded along these it grows as one closed shell, which is what the
        /// sticker border needs. Every toy mesh passes through here (MeshKit does it for its own).
        /// </summary>
        public static void BakeOutlineNormals(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            if (normals.Count != positions.Count)
            {
                mesh.RecalculateNormals();
                mesh.GetNormals(normals);
            }

            // Positions are matched on a grid much finer than any feature, relative to the mesh's size.
            Bounds bounds = mesh.bounds;
            float cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 1e-5f;
            if (cell <= 0f) cell = 1e-6f;
            var groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                if (!groups.TryGetValue(key, out List<int> group)) groups[key] = group = new List<int>(4);
                group.Add(i);
            }

            var outline = new Vector3[positions.Count];
            var unique = new List<Vector3>(8);
            foreach (List<int> group in groups.Values)
            {
                // A normal that several vertices share (a UV seam) counts once, or it would pull the average its way.
                unique.Clear();
                foreach (int index in group)
                {
                    Vector3 normal = normals[index];
                    bool seen = false;
                    for (int k = 0; k < unique.Count && !seen; k++) seen = Vector3.Dot(unique[k], normal) > 0.9999f;
                    if (!seen) unique.Add(normal);
                }
                Vector3 sum = Vector3.zero;
                foreach (Vector3 normal in unique) sum += normal;
                foreach (int index in group)
                    outline[index] = sum.sqrMagnitude > 1e-10f ? sum.normalized : normals[index];
            }
            mesh.SetUVs(3, outline);
        }

        /// <summary>
        /// The UV rule for toys: object-space, one texture repeat per authored unit, each vertex projected
        /// along the axis its normal leans to most. Never world-space - a world-anchored texture would slide
        /// across a held toy. MeshKit's meshes already follow the rule; this is for meshes made elsewhere.
        /// </summary>
        public static void ObjectSpaceUVs(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            var uvs = new Vector2[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                Vector3 n = i < normals.Count ? normals[i] : Vector3.up;
                float x = Mathf.Abs(n.x), y = Mathf.Abs(n.y), z = Mathf.Abs(n.z);
                if (y >= x && y >= z) uvs[i] = new Vector2(p.x, p.z);
                else if (x >= z) uvs[i] = new Vector2(p.z, p.y);
                else uvs[i] = new Vector2(p.x, p.y);
            }
            mesh.SetUVs(0, uvs);
        }

        /// <summary>Sets every vertex colour (the tint that <c>_BaseColor</c> is multiplied with; white by default).</summary>
        public static void SetColor(Mesh mesh, Color color)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            mesh.SetColors(colors);
        }

        public static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) indices += mesh.GetIndexCount(sub);
            return (int)(indices / 3);
        }
    }
}
