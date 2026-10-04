using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Toys
{
    /// <summary>
    /// The figure-glyph of a toy for the UI (ART_BIBLE 10.5, the level select's blister cards): its
    /// silhouette in the pose of <see cref="ToyDef.Portrait"/>, as a coverage mask. What is printed or stuck
    /// on the toy's body - pips, the holes of the cheese, the spade on the card - is cut out of it, the way a
    /// stencil would show it. Drawn on the CPU from the toy's own meshes: no camera, no graphics device,
    /// the same picture every time.
    /// </summary>
    public static class ToySilhouette
    {
        const int Samples = 3;          // per pixel and axis
        const float Margin = 0.06f;     // of the picture, on every side

        static readonly Dictionary<long, byte[]> Masks = new Dictionary<long, byte[]>();
        static readonly Dictionary<long, Texture2D> Textures = new Dictionary<long, Texture2D>();

        /// <summary>
        /// The glyph as size x size coverage values (0 = empty, 255 = toy), row by row from the bottom, as a
        /// texture wants them. Cached; do not modify the array.
        /// </summary>
        public static byte[] Mask(ToyId id, int size = 128)
        {
            size = Mathf.Clamp(size, 8, 512);
            long key = ((long)id << 16) | (uint)size;
            if (!Masks.TryGetValue(key, out byte[] mask)) Masks[key] = mask = Render(ToyCatalog.Get(id), size);
            return mask;
        }

        /// <summary>
        /// The glyph as a white texture whose alpha is the mask - tint it with the colour of your choice.
        /// Cached and shared; null without a graphics device.
        /// </summary>
        public static Texture2D Texture(ToyId id, int size = 128)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            size = Mathf.Clamp(size, 8, 512);
            long key = ((long)id << 16) | (uint)size;
            if (Textures.TryGetValue(key, out Texture2D texture) && texture != null) return texture;
            byte[] mask = Mask(id, size);
            var pixels = new Color32[mask.Length];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, mask[i]);
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Silhouette " + id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Textures[key] = texture;
            return texture;
        }

        static byte[] Render(ToyDef def, int size)
        {
            // The toy is built dormant: it never becomes active, so its colliders never enter a physics scene.
            bool previous = ToyKit.Dormant;
            ToyKit.Dormant = true;
            GameObject toy = null;
            try
            {
                toy = def.Build();
                var body = new List<Vector3>();
                var detail = new List<Vector3>();
                Matrix4x4 toRoot = toy.transform.worldToLocalMatrix;
                Matrix4x4 pose = Matrix4x4.Rotate(def.Portrait);
                foreach (MeshFilter filter in toy.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    Matrix4x4 matrix = pose * toRoot * filter.transform.localToWorldMatrix;
                    List<Vector3> target = filter.gameObject.name.StartsWith("Detail", System.StringComparison.Ordinal) ? detail : body;
                    Vector3[] vertices = mesh.vertices;
                    int[] triangles = mesh.triangles;
                    for (int t = 0; t < triangles.Length; t++) target.Add(matrix.MultiplyPoint3x4(vertices[triangles[t]]));
                }
                return Rasterize(body, detail, size);
            }
            finally
            {
                ToyKit.Dormant = previous;
                if (toy != null) Sim.Destroy(toy);
            }
        }

        // The viewer looks along +Z: x is right, y is up, a smaller z is nearer.
        static byte[] Rasterize(List<Vector3> body, List<Vector3> detail, int size)
        {
            var mask = new byte[size * size];
            if (body.Count + detail.Count == 0) return mask;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (List<Vector3> list in new[] { body, detail })
                foreach (Vector3 p in list)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            int grid = size * Samples;
            float extent = Mathf.Max(max.x - min.x, max.y - min.y, 1e-6f);
            float scale = grid * (1f - 2f * Margin) / extent;
            Vector2 middle = (min + max) * 0.5f;
            var offset = new Vector2(grid * 0.5f - middle.x * scale, grid * 0.5f - middle.y * scale);

            var depth = new float[grid * grid];
            var layers = new byte[grid * grid];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;
            // The body: every triangle, whichever way it faces, and how near it is.
            Fill(body, scale, offset, grid, false, depth, layers);
            // What is on it: each layer of detail in front of the body flips the stencil.
            var bodyDepth = (float[])depth.Clone();
            var covered = new bool[grid * grid];
            for (int i = 0; i < covered.Length; i++) covered[i] = depth[i] < float.MaxValue;
            Fill(detail, scale, offset, grid, true, bodyDepth, layers);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int sum = 0;
                    for (int j = 0; j < Samples; j++)
                        for (int i = 0; i < Samples; i++)
                        {
                            int s = (y * Samples + j) * grid + x * Samples + i;
                            bool odd = (layers[s] & 1) == 1;
                            if (covered[s] ? !odd : layers[s] > 0) sum++;
                        }
                    mask[y * size + x] = (byte)(sum * 255 / (Samples * Samples));
                }
            return mask;
        }

        // Body pass: writes the nearest depth. Detail pass: counts, per sample, the front-facing triangles
        // that are nearer than the body.
        static void Fill(List<Vector3> triangles, float scale, Vector2 offset, int grid, bool count, float[] depth, byte[] layers)
        {
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector3 a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float ax = a.x * scale + offset.x, ay = a.y * scale + offset.y;
                float bx = b.x * scale + offset.x, by = b.y * scale + offset.y;
                float cx = c.x * scale + offset.x, cy = c.y * scale + offset.y;
                float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Mathf.Abs(area) < 1e-9f) continue;
                // Front faces run clockwise on screen, which is a negative area with y up.
                if (count && area > 0f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
                int x1 = Mathf.Min(grid - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
                int y1 = Mathf.Min(grid - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
                float inverse = 1f / area;
                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * inverse;
                        float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * inverse;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                        float z = w0 * a.z + w1 * b.z + w2 * c.z;
                        int s = y * grid + x;
                        if (count)
                        {
                            if (z < depth[s] && layers[s] < 255) layers[s]++;
                        }
                        else if (z < depth[s])
                        {
                            depth[s] = z;
                        }
                    }
                }
            }
        }
    }
}
