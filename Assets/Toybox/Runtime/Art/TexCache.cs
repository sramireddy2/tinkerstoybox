using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Art
{
    /// <summary>
    /// The procedural detail textures of ART_BIBLE 4.2: one packed data texture per kind, R = albedo
    /// modulation, G = smoothness modulation, B = height, with 128 as "no change" in every channel. They
    /// are made on first use and kept for the session; <see cref="Materials"/> puts them on toy materials.
    ///
    /// - 256 x 256 (the feather's is 256 x 64), linear, with mips, repeating, trilinear, anisotropy 4. On
    ///   the Low tier they are half that size with anisotropy 1 (the same picture, averaged down).
    /// - Deterministic: every texture is drawn with a local xorshift seeded with the FNV-1a hash of its
    ///   name. Never UnityEngine.Random and never game.Rng - making a texture must not disturb the
    ///   simulation.
    /// - A headless run (no graphics device) makes nothing and hands out Texture2D.grayTexture.
    /// - <see cref="Pixels"/> is the picture itself as plain data, graphics device or not.
    ///
    /// Budget: nine textures, under 3 MB, under 60 ms of generation in total.
    /// </summary>
    public static class TexCache
    {
        /// <summary>Edge length of a detail texture on Medium and High.</summary>
        public const int Size = 256;
        /// <summary>The feather texture is a strip: the rachis runs along U.</summary>
        public const int BarbHeight = 64;
        /// <summary>The value that means "no change" in every channel.</summary>
        public const byte Neutral = 128;

        static readonly Dictionary<int, Texture2D> Cache = new Dictionary<int, Texture2D>();
        static readonly DetailTexture[] Kinds = (DetailTexture[])Enum.GetValues(typeof(DetailTexture));
        static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();

        /// <summary>How many textures exist right now.</summary>
        public static int Count
        {
            get
            {
                int count = 0;
                foreach (Texture2D texture in Cache.Values)
                    if (texture != null) count++;
                return count;
            }
        }

        /// <summary>Memory of the textures that exist, mips included.</summary>
        public static long Bytes
        {
            get
            {
                long bytes = 0;
                foreach (Texture2D texture in Cache.Values)
                    if (texture != null) bytes += (long)texture.width * texture.height * 4L * 4L / 3L;
                return bytes;
            }
        }

        /// <summary>Milliseconds spent generating textures this session.</summary>
        public static double GenerationMilliseconds => Clock.Elapsed.TotalMilliseconds;

        /// <summary>True where textures can be made at all: there is a graphics device.</summary>
        public static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>The detail texture of a kind for the tier materials are made for.</summary>
        public static Texture2D Get(DetailTexture kind) => Get(kind, Materials.Tier);

        /// <summary>The detail texture of a kind at a tier's size, made now if it does not exist yet.</summary>
        public static Texture2D Get(DetailTexture kind, QualityTier tier)
        {
            if (!HasGraphics) return Texture2D.grayTexture;
            bool low = tier == QualityTier.Low && kind != DetailTexture.Neutral;
            int key = Key(kind, low);
            if (Cache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            Clock.Start();
            Color32[] pixels = Pixels(kind, out int width, out int height);
            if (low) pixels = Halve(pixels, ref width, ref height);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            {
                name = "Detail " + kind + (low ? " (low)" : ""),
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = low ? 1 : 4,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            Clock.Stop();
            Cache[key] = texture;
            return texture;
        }

        /// <summary>
        /// The texture a recipe's material samples at a tier. The orange peel of glossy plastic is height
        /// only, and the bump is off below High - so below High glossy plastic takes the neutral texture
        /// and the peel is never made.
        /// </summary>
        public static Texture2D For(ToyRecipe recipe, QualityTier tier) => Get(KindFor(recipe, tier), tier);

        /// <summary>Which texture <see cref="For"/> hands out.</summary>
        public static DetailTexture KindFor(ToyRecipe recipe, QualityTier tier)
        {
            if (recipe == null) return DetailTexture.Neutral;
            if (recipe.Detail == DetailTexture.Peel && tier != QualityTier.High) return DetailTexture.Neutral;
            return recipe.Detail;
        }

        /// <summary>
        /// For the loading screen: makes one texture that does not exist yet at this tier and returns true,
        /// or returns false when all of them exist. Call it once per frame until it says false.
        /// </summary>
        public static bool WarmNext(QualityTier tier)
        {
            if (!HasGraphics) return false;
            foreach (DetailTexture kind in Kinds)
            {
                if (kind == DetailTexture.Peel && tier != QualityTier.High) continue;
                bool low = tier == QualityTier.Low && kind != DetailTexture.Neutral;
                if (Cache.TryGetValue(Key(kind, low), out Texture2D cached) && cached != null) continue;
                Get(kind, tier);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Destroys the textures of the other size once the tier has changed (and every material has been
        /// given the texture of the new one), so that no more than nine are alive.
        /// </summary>
        public static void Trim(QualityTier tier)
        {
            bool keepLow = tier == QualityTier.Low;
            var stale = new List<int>();
            foreach (KeyValuePair<int, Texture2D> entry in Cache)
            {
                DetailTexture kind = (DetailTexture)(entry.Key & 0xFF);
                bool low = (entry.Key & 0x100) != 0;
                bool wanted = kind == DetailTexture.Neutral || (low == keepLow && (kind != DetailTexture.Peel || tier == QualityTier.High));
                if (entry.Value == null || !wanted) stale.Add(entry.Key);
            }
            foreach (int key in stale)
            {
                Destroy(Cache[key]);
                Cache.Remove(key);
            }
        }

        /// <summary>
        /// Destroys every texture and makes those again that toy materials are using, so that none of them
        /// is left with a destroyed one. The rest come back on first use.
        /// </summary>
        public static void Clear()
        {
            foreach (Texture2D texture in Cache.Values) Destroy(texture);
            Cache.Clear();
            Materials.RepointDetailMaps();
        }

        static int Key(DetailTexture kind, bool low) => (int)kind | (low ? 0x100 : 0);

        static void Destroy(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        // ---------------------------------------------------------------------------------------------------
        // The pictures (plain data)
        // ---------------------------------------------------------------------------------------------------

        /// <summary>FNV-1a of a name: the seed of a texture's random numbers.</summary>
        public static uint Seed(string name)
        {
            uint hash = 2166136261u;
            if (name != null)
                foreach (char c in name)
                {
                    hash ^= (byte)c;
                    hash *= 16777619u;
                }
            return hash;
        }

        /// <summary>
        /// The pixels of a detail texture at full size, row by row from v = 0. The same on every machine
        /// and in every run; needs no graphics device.
        /// </summary>
        public static Color32[] Pixels(DetailTexture kind, out int width, out int height)
        {
            width = kind == DetailTexture.Neutral ? 4 : Size;
            height = kind == DetailTexture.Neutral ? 4 : kind == DetailTexture.Barb ? BarbHeight : Size;
            var canvas = new Canvas(width, height);
            var rng = new Xorshift(Seed(kind.ToString()));
            switch (kind)
            {
                case DetailTexture.Peel: Peel(canvas, ref rng); break;
                case DetailTexture.Brush: Brush(canvas, ref rng); break;
                case DetailTexture.Stipple: Stipple(canvas, ref rng); break;
                case DetailTexture.Streak: Streak(canvas, ref rng); break;
                case DetailTexture.Fibre: Fibre(canvas, ref rng); break;
                case DetailTexture.Speckle: Speckle(canvas, ref rng); break;
                case DetailTexture.Pore: Pore(canvas, ref rng); break;
                case DetailTexture.Barb: Barb(canvas, ref rng); break;
            }
            return canvas.ToPixels();
        }

        /// <summary>The same picture at half the size: every pixel the average of four.</summary>
        public static Color32[] Halve(Color32[] pixels, ref int width, ref int height)
        {
            int w = Mathf.Max(1, width / 2), h = Mathf.Max(1, height / 2);
            var half = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color32 a = pixels[2 * y * width + 2 * x], b = pixels[2 * y * width + 2 * x + 1];
                    Color32 c = pixels[(2 * y + 1) * width + 2 * x], d = pixels[(2 * y + 1) * width + 2 * x + 1];
                    half[y * w + x] = new Color32(
                        (byte)((a.r + b.r + c.r + d.r + 2) / 4), (byte)((a.g + b.g + c.g + d.g + 2) / 4),
                        (byte)((a.b + b.b + c.b + d.b + 2) / 4), 255);
                }
            }
            width = w;
            height = h;
            return half;
        }

        // Offsets from 128 per channel, as floats while the picture is drawn.
        sealed class Canvas
        {
            public readonly int Width, Height;
            public readonly float[] R, G, B;

            public Canvas(int width, int height)
            {
                Width = width;
                Height = height;
                R = new float[width * height];
                G = new float[width * height];
                B = new float[width * height];
            }

            /// <summary>Index of a pixel; both coordinates wrap, because the texture repeats.</summary>
            public int At(int x, int y)
            {
                x %= Width;
                if (x < 0) x += Width;
                y %= Height;
                if (y < 0) y += Height;
                return y * Width + x;
            }

            public Color32[] ToPixels()
            {
                var pixels = new Color32[R.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(Pack(R[i]), Pack(G[i]), Pack(B[i]), 255);
                return pixels;
            }

            static byte Pack(float offset) => (byte)Mathf.Clamp(Mathf.RoundToInt(Neutral + offset), 0, 255);
        }

        struct Xorshift
        {
            uint state;

            public Xorshift(uint seed) => state = seed != 0 ? seed : 0x9E3779B9u;

            public uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            /// <summary>0 (inclusive) to 1 (exclusive).</summary>
            public float Unit() => (Next() >> 8) * (1f / 16777216f);
            public float Signed() => Unit() * 2f - 1f;
            public float Range(float from, float to) => from + (to - from) * Unit();
            public int Int(int count) => (int)(Next() % (uint)count);
            public float Sign() => (Next() & 0x10000u) != 0 ? 1f : -1f;
        }

        // Glossy plastic (High only): orange peel. B: two octaves of value noise, cells 16 px and 8 px, +-20.
        static void Peel(Canvas c, ref Xorshift rng)
        {
            float[] coarse = Lattice(c.Width / 16, c.Height / 16, ref rng), fine = Lattice(c.Width / 8, c.Height / 8, ref rng);
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                    c.B[y * c.Width + x] = 20f * (ValueNoise(coarse, c.Width / 16, c.Height / 16, x / 16f, y / 16f) * (2f / 3f)
                                                 + ValueNoise(fine, c.Width / 8, c.Height / 8, x / 8f, y / 8f) * (1f / 3f));
        }

        static float[] Lattice(int cellsX, int cellsY, ref Xorshift rng)
        {
            var lattice = new float[cellsX * cellsY];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = rng.Signed();
            return lattice;
        }

        // Smoothly interpolated lattice values, wrapping at the edges. -1..1.
        static float ValueNoise(float[] lattice, int cellsX, int cellsY, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = x0 % cellsX, xb = (x0 + 1) % cellsX, ya = y0 % cellsY, yb = (y0 + 1) % cellsY;
            float bottom = Mathf.Lerp(lattice[ya * cellsX + xa], lattice[ya * cellsX + xb], fx);
            float top = Mathf.Lerp(lattice[yb * cellsX + xa], lattice[yb * cellsX + xb], fx);
            return Mathf.Lerp(bottom, top, fy);
        }

        // Painted wood: 40 horizontal strokes, each a full-width band 3-9 px tall: R +-10, B +-15, G -+5.
        static void Brush(Canvas c, ref Xorshift rng)
        {
            var rows = new float[c.Height];
            for (int stroke = 0; stroke < 40; stroke++)
            {
                int start = rng.Int(c.Height), tall = 3 + rng.Int(7);
                float strength = rng.Range(0.4f, 1f) * rng.Sign();
                for (int i = 0; i < tall; i++)
                {
                    // Rounded across the stroke: a ridge of paint, not a step.
                    float profile = Mathf.Sin(Mathf.PI * (i + 0.5f) / tall);
                    int row = (start + i) % c.Height;
                    rows[row] = Mathf.Clamp(rows[row] + strength * profile, -1f, 1f);
                }
            }
            for (int y = 0; y < c.Height; y++)
            {
                for (int x = 0; x < c.Width; x++)
                {
                    int i = y * c.Width + x;
                    c.R[i] = 10f * rows[y];
                    c.B[i] = 15f * rows[y];
                    c.G[i] = -5f * rows[y];
                }
            }
        }

        // Rubber: 900 dots of radius 1.5-2.5 px with Gaussian falloff: B +40, G -10.
        static void Stipple(Canvas c, ref Xorshift rng)
        {
            for (int dot = 0; dot < 900; dot++)
            {
                float cx = rng.Unit() * c.Width, cy = rng.Unit() * c.Height, radius = rng.Range(1.5f, 2.5f);
                float sigma = radius * 0.5f;
                int reach = Mathf.CeilToInt(radius * 2f);
                int x0 = Mathf.FloorToInt(cx), y0 = Mathf.FloorToInt(cy);
                for (int y = y0 - reach; y <= y0 + reach; y++)
                {
                    for (int x = x0 - reach; x <= x0 + reach; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float bump = Mathf.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma));
                        int i = c.At(x, y);
                        c.B[i] = Mathf.Max(c.B[i], 40f * bump);
                        c.G[i] = Mathf.Min(c.G[i], -10f * bump);
                    }
                }
            }
        }

        // Brushed metal: every row has a value of its own, +-16, and along the row runs noise that a 24 px
        // wrap-around box blur has drawn out into streaks. G at full strength, R at half.
        static void Streak(Canvas c, ref Xorshift rng)
        {
            const int Blur = 24;
            var noise = new float[c.Width];
            for (int y = 0; y < c.Height; y++)
            {
                float row = 16f * rng.Signed();
                for (int x = 0; x < c.Width; x++) noise[x] = 16f * rng.Signed();
                float sum = 0f;
                for (int k = 0; k < Blur; k++) sum += noise[k];
                for (int x = 0; x < c.Width; x++)
                {
                    // The blur takes most of the contrast out of white noise; 2.5 gives it back.
                    float value = Mathf.Clamp(0.6f * row + 2.5f * sum / Blur, -16f, 16f);
                    int i = y * c.Width + (x + Blur / 2) % c.Width;
                    c.G[i] = value;
                    c.R[i] = value * 0.5f;
                    sum += noise[(x + Blur) % c.Width] - noise[x];
                }
            }
        }

        // Felt, fabric: 3000 strokes 6-14 px long at random angles, each adding +-2 to R and +-6 to B
        // (accumulating, clamped +-24).
        static void Fibre(Canvas c, ref Xorshift rng)
        {
            for (int stroke = 0; stroke < 3000; stroke++)
            {
                float x = rng.Unit() * c.Width, y = rng.Unit() * c.Height, angle = rng.Unit() * Mathf.PI * 2f;
                float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle), sign = rng.Sign();
                int length = 6 + rng.Int(9);
                for (int step = 0; step < length; step++)
                {
                    int i = c.At(Mathf.FloorToInt(x + dx * step), Mathf.FloorToInt(y + dy * step));
                    c.R[i] = Mathf.Clamp(c.R[i] + 2f * sign, -24f, 24f);
                    c.B[i] = Mathf.Clamp(c.B[i] + 6f * sign, -24f, 24f);
                }
            }
        }

        // Cardboard, paper. Top half: 4000 one-pixel speckles, R +-15. Bottom quarter: sine flute stripes
        // of period 6 px, B +-30, R +-8 (edge faces are UV-mapped there).
        static void Speckle(Canvas c, ref Xorshift rng)
        {
            int half = c.Height / 2, quarter = c.Height / 4;
            for (int speck = 0; speck < 4000; speck++)
            {
                int i = c.At(rng.Int(c.Width), half + rng.Int(c.Height - half));
                c.R[i] = 15f * rng.Signed();
            }
            // A whole number of flutes across the texture, so that they meet at the seam: 43 of 5.95 px.
            float flutes = Mathf.Round(c.Width / 6f);
            for (int y = 0; y < quarter; y++)
            {
                for (int x = 0; x < c.Width; x++)
                {
                    float wave = Mathf.Sin((x + 0.5f) / c.Width * flutes * Mathf.PI * 2f);
                    int i = y * c.Width + x;
                    c.B[i] = 30f * wave;
                    c.R[i] = 8f * wave;
                }
            }
        }

        // Sponge: 220 ellipses with 2-9 px semi-axes: R -58 (albedo x 0.55), B -60. Plus 600 single-pixel
        // dots at half strength.
        static void Pore(Canvas c, ref Xorshift rng)
        {
            for (int pore = 0; pore < 220; pore++)
            {
                float cx = rng.Unit() * c.Width, cy = rng.Unit() * c.Height;
                float a = rng.Range(2f, 9f), b = rng.Range(2f, 9f), angle = rng.Unit() * Mathf.PI;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                int reach = Mathf.CeilToInt(Mathf.Max(a, b)) + 1;
                int x0 = Mathf.FloorToInt(cx), y0 = Mathf.FloorToInt(cy);
                for (int y = y0 - reach; y <= y0 + reach; y++)
                {
                    for (int x = x0 - reach; x <= x0 + reach; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float u = (dx * cos + dy * sin) / a, v = (-dx * sin + dy * cos) / b;
                        // 1 inside, falling to 0 over about a pixel at the rim.
                        float cover = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * Mathf.Min(a, b));
                        if (cover <= 0f) continue;
                        int i = c.At(x, y);
                        c.R[i] = Mathf.Min(c.R[i], -58f * cover);
                        c.B[i] = Mathf.Min(c.B[i], -60f * cover);
                    }
                }
            }
            for (int dot = 0; dot < 600; dot++)
            {
                int i = c.At(rng.Int(c.Width), rng.Int(c.Height));
                c.R[i] = Mathf.Min(c.R[i], -29f);
                c.B[i] = Mathf.Min(c.B[i], -30f);
            }
        }

        // Feather: 256 x 64. 90 lines at +-35 degrees from the centre line (the rachis runs along U):
        // R +-18, B +-40, G +10. Half of them rise from the rachis, half fall from it.
        static void Barb(Canvas c, ref Xorshift rng)
        {
            float cos = Mathf.Cos(35f * Mathf.Deg2Rad), sin = Mathf.Sin(35f * Mathf.Deg2Rad);
            float middle = c.Height * 0.5f;
            int steps = Mathf.CeilToInt(middle / sin);
            for (int line = 0; line < 90; line++)
            {
                float x = rng.Unit() * c.Width, sign = rng.Sign();
                float side = (line & 1) == 0 ? 1f : -1f;
                for (int step = 0; step < steps; step++)
                {
                    float py = middle + side * (step + 0.5f) * sin;
                    if (py < 0f || py >= c.Height) break;
                    // No wrap in V: the two vanes end at the feather's edge.
                    int i = Mathf.FloorToInt(py) * c.Width + c.At(Mathf.FloorToInt(x + step * cos), 0);
                    c.R[i] = Mathf.Clamp(c.R[i] + 18f * sign, -18f, 18f);
                    c.B[i] = Mathf.Clamp(c.B[i] + 40f * sign, -40f, 40f);
                    c.G[i] = 10f;
                }
            }
        }
    }
}
