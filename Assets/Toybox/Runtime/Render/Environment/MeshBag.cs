using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// Collects geometry for one draw call of the room: every mesh, quad and box that shares a material
    /// goes into one bag and comes out as one mesh in world space (ART_BIBLE 12.1: draw calls are the
    /// scarce resource). Vertex colours are what Toybox/RoomLit multiplies the tone with (baked ambient
    /// occlusion) and what Toybox/Flat takes as its colour; they are linear and may exceed 1.
    /// </summary>
    public sealed class MeshBag
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        static readonly List<Vector3> SourcePositions = new List<Vector3>();
        static readonly List<Vector3> SourceNormals = new List<Vector3>();
        static readonly List<Vector2> SourceUvs = new List<Vector2>();
        static readonly List<Color> SourceColors = new List<Color>();
        static readonly List<int> SourceTriangles = new List<int>();

        public int VertexCount => positions.Count;
        public int TriangleCount => triangles.Count / 3;
        /// <summary>Number of indices so far: pass to <see cref="ShadeBase"/> to shade only what is added from here on.</summary>
        public int IndexCount => triangles.Count;
        public bool Empty => triangles.Count == 0;

        public Vector3 Position(int vertex) => positions[vertex];
        public Vector3 Normal(int vertex) => normals[vertex];
        public Color Color(int vertex) => colors[vertex];
        /// <summary>The vertex of index <paramref name="i"/> (three per triangle, 0 .. IndexCount - 1).</summary>
        public int Index(int i) => triangles[i];

        /// <summary>A mesh (readable) placed by a matrix, its vertex colours multiplied by <paramref name="tint"/>.</summary>
        public void Add(Mesh mesh, Matrix4x4 matrix, Color tint)
        {
            if (mesh == null) return;
            mesh.GetVertices(SourcePositions);
            mesh.GetNormals(SourceNormals);
            mesh.GetUVs(0, SourceUvs);
            mesh.GetColors(SourceColors);
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            bool mirrored = matrix.determinant < 0f;
            int offset = positions.Count;
            for (int i = 0; i < SourcePositions.Count; i++)
            {
                positions.Add(matrix.MultiplyPoint3x4(SourcePositions[i]));
                Vector3 normal = i < SourceNormals.Count ? normalMatrix.MultiplyVector(SourceNormals[i]) : Vector3.up;
                normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up);
                uvs.Add(i < SourceUvs.Count ? SourceUvs[i] : Vector2.zero);
                colors.Add(i < SourceColors.Count ? SourceColors[i] * tint : tint);
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(SourceTriangles, sub);
                for (int t = 0; t + 2 < SourceTriangles.Count; t += 3)
                {
                    triangles.Add(offset + SourceTriangles[t]);
                    triangles.Add(offset + SourceTriangles[mirrored ? t + 2 : t + 1]);
                    triangles.Add(offset + SourceTriangles[mirrored ? t + 1 : t + 2]);
                }
            }
        }

        public void Add(Mesh mesh, Matrix4x4 matrix) => Add(mesh, matrix, UnityEngine.Color.white);

        /// <summary>
        /// A flat quad, corners in order around it, facing along <paramref name="normal"/> whichever way
        /// the corners run. UVs are the unit square (a: 0,0  b: 1,0  c: 1,1  d: 0,1).
        /// </summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color ca, Color cb, Color cc, Color cd)
        {
            int first = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            colors.Add(ca); colors.Add(cb); colors.Add(cc); colors.Add(cd);
            // Unity's front face is the one whose corners run clockwise: cross(b - a, c - a) points out of it.
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            Triangle(first, flip ? first + 2 : first + 1, flip ? first + 1 : first + 2);
            Triangle(first, flip ? first + 3 : first + 2, flip ? first + 2 : first + 3);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color) => Quad(a, b, c, d, normal, color, color, color, color);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal) => Quad(a, b, c, d, normal, UnityEngine.Color.white);

        /// <summary>One triangle facing along <paramref name="normal"/>, whichever way its corners run.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color ca, Color cb, Color cc)
        {
            int first = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c);
            for (int i = 0; i < 3; i++) normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0.5f, 1f));
            colors.Add(ca); colors.Add(cb); colors.Add(cc);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            Triangle(first, flip ? first + 2 : first + 1, flip ? first + 1 : first + 2);
        }

        /// <summary>A hard-edged box: 12 triangles.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            Vector3 x = rotation * new Vector3(size.x * 0.5f, 0f, 0f), y = rotation * new Vector3(0f, size.y * 0.5f, 0f), z = rotation * new Vector3(0f, 0f, size.z * 0.5f);
            Face(center + x, y, z, x, color);
            Face(center - x, y, z, -x, color);
            Face(center + y, z, x, y, color);
            Face(center - y, z, x, -y, color);
            Face(center + z, x, y, z, color);
            Face(center - z, x, y, -z, color);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation) => Box(center, size, rotation, UnityEngine.Color.white);

        void Face(Vector3 center, Vector3 u, Vector3 v, Vector3 outward, Color color) =>
            Quad(center - u - v, center + u - v, center + u + v, center - u + v, outward.normalized, color);

        void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>
        /// Baked ambient occlusion at the foot of a piece (ART_BIBLE 6.3): everything added since
        /// <paramref name="fromIndex"/> (an earlier <see cref="IndexCount"/>) is darkened to
        /// <paramref name="dark"/> at <paramref name="baseY"/>, fading out <paramref name="height"/> above
        /// it. Triangles that reach across the top of the fade are cut there, so the gradient is exactly
        /// that tall whatever the piece's own tessellation.
        /// </summary>
        public void ShadeBase(int fromIndex, float baseY, float height = 6f, float dark = 0.75f)
        {
            if (fromIndex >= triangles.Count || height <= 0f) return;
            int firstVertex = int.MaxValue;
            for (int i = fromIndex; i < triangles.Count; i++) firstVertex = Mathf.Min(firstVertex, triangles[i]);
            SplitAtHeight(fromIndex, baseY + height);
            for (int v = firstVertex; v < positions.Count; v++)
            {
                float t = Mathf.Clamp01((positions[v].y - baseY) / height);
                float shade = Mathf.Lerp(dark, 1f, t);
                Color color = colors[v];
                colors[v] = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
            }
        }

        // Cuts every triangle from fromIndex on along the plane y = h. New vertices are shared between the
        // two triangles of an edge, so smooth shading stays smooth.
        void SplitAtHeight(int fromIndex, float h)
        {
            const float eps = 1e-4f;
            var cut = new Dictionary<long, int>();
            var polygon = new List<int>(5);
            var sides = new List<int>(5);
            var above = new List<int>(4);
            var below = new List<int>(4);
            int end = triangles.Count;
            for (int t = fromIndex; t + 2 < end; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                int sa = Side(positions[a].y - h, eps), sb = Side(positions[b].y - h, eps), sc = Side(positions[c].y - h, eps);
                bool anyAbove = sa > 0 || sb > 0 || sc > 0, anyBelow = sa < 0 || sb < 0 || sc < 0;
                if (!anyAbove || !anyBelow) continue;

                polygon.Clear();
                sides.Clear();
                Walk(a, sa, b, sb, h, cut, polygon, sides);
                Walk(b, sb, c, sc, h, cut, polygon, sides);
                Walk(c, sc, a, sa, h, cut, polygon, sides);

                above.Clear();
                below.Clear();
                for (int i = 0; i < polygon.Count; i++)
                {
                    if (sides[i] >= 0) above.Add(polygon[i]);
                    if (sides[i] <= 0) below.Add(polygon[i]);
                }

                bool reused = false;
                Fan(above, t, ref reused);
                Fan(below, t, ref reused);
            }
        }

        static int Side(float distance, float eps) => distance > eps ? 1 : distance < -eps ? -1 : 0;

        // Adds the start of an edge and, if the edge crosses the plane, the crossing point after it.
        void Walk(int from, int fromSide, int to, int toSide, float h, Dictionary<long, int> cut, List<int> polygon, List<int> sides)
        {
            polygon.Add(from);
            sides.Add(fromSide);
            if (fromSide * toSide >= 0) return;
            long key = from < to ? ((long)from << 32) | (uint)to : ((long)to << 32) | (uint)from;
            if (!cut.TryGetValue(key, out int vertex))
            {
                float k = (h - positions[from].y) / (positions[to].y - positions[from].y);
                Vector3 position = Vector3.LerpUnclamped(positions[from], positions[to], k);
                position.y = h;
                vertex = positions.Count;
                positions.Add(position);
                Vector3 normal = Vector3.LerpUnclamped(normals[from], normals[to], k);
                normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : normals[from]);
                uvs.Add(Vector2.LerpUnclamped(uvs[from], uvs[to], k));
                colors.Add(UnityEngine.Color.LerpUnclamped(colors[from], colors[to], k));
                cut[key] = vertex;
            }
            polygon.Add(vertex);
            sides.Add(0);
        }

        // Triangulates a convex polygon as a fan; the first triangle takes the place of the one that was cut.
        void Fan(List<int> polygon, int slot, ref bool reused)
        {
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                if (!reused)
                {
                    triangles[slot] = polygon[0];
                    triangles[slot + 1] = polygon[i];
                    triangles[slot + 2] = polygon[i + 1];
                    reused = true;
                }
                else
                {
                    Triangle(polygon[0], polygon[i], polygon[i + 1]);
                }
            }
        }

        /// <summary>
        /// The mesh of everything collected, or null if the bag is empty. It is uploaded and no longer
        /// readable; whoever asked for it destroys it (<see cref="MeshKit.Release"/>).
        /// </summary>
        public Mesh Build(string name)
        {
            if (Empty) return null;
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
