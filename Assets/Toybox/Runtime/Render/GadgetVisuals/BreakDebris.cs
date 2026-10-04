using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// What is left of a <see cref="Breakable"/> (LEVELS 2.1: "debris is presentation only"). When a
    /// barricade breaks its wall is simply gone from the simulation; here its blocks fly on in the
    /// direction of the blow, tumble, bounce once on the ground the wall stood on and shrink away. A body
    /// that is flattened instead (the alarm clock) sheds a handful of small bits. A hit that was too
    /// weak rings the wall with a small Paper ring where it landed.
    ///
    /// The chunks are one mesh in the wall's own material, written every frame while they fly (one draw,
    /// one shadow draw); they have no colliders and nothing can stand on them. Deterministic: the same
    /// break throws the same chunks.
    /// </summary>
    [Presenter(246, ProvidesLook = true)]
    public sealed class BreakDebris : GadgetVisual
    {
        public const float Seconds = 1.9f, FadeSeconds = 0.45f;
        const float Restitution = 0.32f;

        struct Chunk
        {
            public Vector3 Position, Velocity, Half, Spin;
            public Quaternion Rotation;
            public float Age, Life, Floor;
            public bool Bounced;
        }

        sealed class Burst
        {
            public Breakable Source;
            public Chunk[] Chunks;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public float Age;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
        }

        static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        readonly List<Burst> bursts = new List<Burst>();
        CuePool rings;

        /// <summary>Breaks that still have chunks in the air.</summary>
        public int Count => bursts.Count;
        /// <summary>Breaks shown since the level was loaded.</summary>
        public int Broken { get; private set; }
        /// <summary>Too-weak hits shown since the level was loaded.</summary>
        public int Bonks { get; private set; }
        public int ChunkCount(int index) => bursts[index].Chunks.Length;
        public Renderer RendererOf(int index) => bursts[index].Renderer;
        /// <summary>Where chunk <paramref name="chunk"/> of burst <paramref name="index"/> is.</summary>
        public Vector3 ChunkPosition(int index, int chunk) => bursts[index].Chunks[chunk].Position;

        protected override void Subscribe(GameEvents events)
        {
            events.BreakableBroke += OnBroke;
            events.BreakableBonked += OnBonked;
        }

        protected override void Unsubscribe(GameEvents events)
        {
            events.BreakableBroke -= OnBroke;
            events.BreakableBonked -= OnBonked;
        }

        protected override void Begin()
        {
            rings = new CuePool("Break Rings", Root, 4, GadgetFx.Edge(Materials.Dip));
            Broken = 0;
            Bonks = 0;
        }

        void OnBonked(GadgetEvent e)
        {
            if (rings == null) return;
            Bonks++;
            float size = e.Prop != null && !e.Prop.Removed ? Mathf.Clamp(e.Prop.Radius * 0.6f, 0.25f, 3f) : 0.5f;
            rings.Ring(e.Position, Vector3.zero, size * 0.3f, size, GadgetFx.Lin(Palette.Paper, 1.1f, 0.8f), 0.3f, true);
        }

        void OnBroke(GadgetEvent e)
        {
            if (!(e.Gadget is Breakable breakable) || rings == null) return;
            Broken++;
            Zone box = breakable.Bounds;
            Quaternion rotation = box.Rotation;
            Vector3 size = box.Size;
            Vector3 direction = breakable.Direction;
            float speed = Mathf.Clamp(e.Value, 2f, 14f);

            // The wall's own material; a body of the level's keeps its look.
            Material material = null;
            bool gone = true;
            foreach (Renderer renderer in breakable.Body.GetComponentsInChildren<Renderer>(true))
            {
                if (material == null) material = renderer.sharedMaterial;
                gone &= !renderer.enabled;
            }
            if (material == null) material = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);

            int budget = GadgetFx.Pick(Tier, 10, 18, 30);
            Chunk[] chunks;
            float floor = box.Center.y - Mathf.Abs((rotation * Vector3.up).y) * size.y * 0.5f - Mathf.Abs((rotation * Vector3.right).y) * size.x * 0.5f - Mathf.Abs((rotation * Vector3.forward).y) * size.z * 0.5f;
            if (gone)
            {
                // The courses of the wall, as the gadget lays them; thinned out to the tier's budget.
                int columns = Mathf.Clamp(Mathf.RoundToInt(size.x / 1.8f), 1, 8), rows = Mathf.Clamp(Mathf.RoundToInt(size.y / 1.2f), 1, 10);
                while (columns * rows > budget && (columns > 1 || rows > 1))
                {
                    if (columns >= rows && columns > 1) columns--;
                    else rows--;
                }
                var block = new Vector3(size.x / columns, size.y / rows, size.z);
                chunks = new Chunk[columns * rows];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        int i = r * columns + c;
                        var local = new Vector3(-size.x * 0.5f + (c + 0.5f) * block.x, -size.y * 0.5f + (r + 0.5f) * block.y, 0f);
                        Vector3 outward = rotation * new Vector3(local.x / Mathf.Max(0.5f, size.x * 0.5f), 0f, 0f);
                        float high = (r + 0.5f) / rows;
                        chunks[i] = new Chunk
                        {
                            Position = box.Center + rotation * local,
                            Rotation = rotation,
                            Half = block * (0.5f * Mathf.Lerp(0.72f, 0.9f, GadgetFx.Hash(i, 11))),
                            Velocity = direction * (speed * Mathf.Lerp(0.35f, 0.85f, GadgetFx.Hash(i, 12))) + outward * Mathf.Lerp(0.5f, 3f, GadgetFx.Hash(i, 13))
                                + Vector3.up * Mathf.Lerp(1.5f, 5.5f, GadgetFx.Hash(i, 14)) * (0.5f + high),
                            Spin = new Vector3(GadgetFx.Hash(i, 15) - 0.5f, GadgetFx.Hash(i, 16) - 0.5f, GadgetFx.Hash(i, 17) - 0.5f) * 520f,
                            Life = Seconds * Mathf.Lerp(0.75f, 1f, GadgetFx.Hash(i, 18)),
                            Floor = floor,
                        };
                    }
                }
            }
            else
            {
                // Flattened, not gone: a ring of small bits leaves sideways from where it was hit.
                int count = Mathf.Min(budget, 8);
                float bit = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.09f, 0.05f, 0.4f);
                chunks = new Chunk[count];
                for (int i = 0; i < count; i++)
                {
                    float angle = (i + GadgetFx.Hash(i, 21)) * Mathf.PI * 2f / count;
                    var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    chunks[i] = new Chunk
                    {
                        Position = box.Center + around * (Mathf.Min(size.x, size.z) * 0.35f),
                        Rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f),
                        Half = Vector3.one * (bit * Mathf.Lerp(0.6f, 1f, GadgetFx.Hash(i, 22))),
                        Velocity = around * Mathf.Lerp(2f, 5f, GadgetFx.Hash(i, 23)) + Vector3.up * Mathf.Lerp(3f, 6f, GadgetFx.Hash(i, 24)),
                        Spin = new Vector3(GadgetFx.Hash(i, 25) - 0.5f, GadgetFx.Hash(i, 26) - 0.5f, GadgetFx.Hash(i, 27) - 0.5f) * 700f,
                        Life = Seconds * 0.7f,
                        Floor = floor,
                    };
                }
            }

            var burst = new Burst { Source = breakable, Chunks = chunks, Mesh = new Mesh { name = "Break Debris", hideFlags = HideFlags.DontSave } };
            burst.Mesh.MarkDynamic();
            Keep(burst.Mesh);
            burst.Renderer = Visual("Break Debris " + breakable.Name, burst.Mesh, material);
            burst.Renderer.shadowCastingMode = ShadowCastingMode.On;
            burst.Renderer.receiveShadows = true;
            Topology(burst);
            Write(burst);
            bursts.Add(burst);

            // The blow itself: a Paper ring where it landed.
            float reach = Mathf.Clamp(Mathf.Max(size.x, size.y) * 0.5f, 0.6f, 6f);
            rings.Ring(e.Position, Vector3.zero, reach * 0.25f, reach, GadgetFx.Lin(Palette.Paper, 1.2f, 0.85f), 0.4f, true);
        }

        // Triangles and UVs never change: 24 corners and 12 triangles a chunk.
        static void Topology(Burst burst)
        {
            int count = burst.Chunks.Length;
            var triangles = new List<int>(count * 36);
            var uvs = new List<Vector2>(count * 24);
            for (int i = 0; i < count * 24; i++)
            {
                burst.Vertices.Add(Vector3.zero);
                burst.Normals.Add(Vector3.up);
            }
            for (int c = 0; c < count; c++)
            {
                for (int f = 0; f < 6; f++)
                {
                    int first = c * 24 + f * 4;
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(0f, 1f));
                    // Corners go -u-v, -u+v, +u+v, +u-v with v = normal x u: Cross(c - a, b - a) is the normal.
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 1);
                    triangles.Add(first);
                    triangles.Add(first + 3);
                    triangles.Add(first + 2);
                }
            }
            burst.Mesh.SetVertices(burst.Vertices);
            burst.Mesh.SetNormals(burst.Normals);
            burst.Mesh.SetUVs(0, uvs);
            burst.Mesh.SetTriangles(triangles, 0, false);
        }

        static void Write(Burst burst)
        {
            for (int c = 0; c < burst.Chunks.Length; c++)
            {
                Chunk chunk = burst.Chunks[c];
                float left = chunk.Life - chunk.Age;
                float shrink = left <= 0f ? 0f : GadgetFx.Smooth(left / FadeSeconds);
                Vector3 half = chunk.Half * shrink;
                for (int f = 0; f < 6; f++)
                {
                    Vector3 normal = Faces[f];
                    // Two axes across the face, turning counter-clockwise seen from outside.
                    Vector3 u = f < 2 ? Vector3.forward : f < 4 ? Vector3.right : Vector3.up;
                    Vector3 v = Vector3.Cross(normal, u);
                    Vector3 centre = Vector3.Scale(normal, half);
                    Vector3 du = Vector3.Scale(u, half), dv = Vector3.Scale(v, half);
                    int first = c * 24 + f * 4;
                    Vector3 n = chunk.Rotation * normal;
                    burst.Vertices[first] = chunk.Position + chunk.Rotation * (centre - du - dv);
                    burst.Vertices[first + 1] = chunk.Position + chunk.Rotation * (centre - du + dv);
                    burst.Vertices[first + 2] = chunk.Position + chunk.Rotation * (centre + du + dv);
                    burst.Vertices[first + 3] = chunk.Position + chunk.Rotation * (centre + du - dv);
                    burst.Normals[first] = n;
                    burst.Normals[first + 1] = n;
                    burst.Normals[first + 2] = n;
                    burst.Normals[first + 3] = n;
                }
            }
            burst.Mesh.SetVertices(burst.Vertices);
            burst.Mesh.SetNormals(burst.Normals);
            burst.Mesh.RecalculateBounds();
        }

        protected override void Draw(float dt, float alpha)
        {
            for (int b = bursts.Count - 1; b >= 0; b--)
            {
                Burst burst = bursts[b];
                burst.Age += dt;
                if (burst.Age >= Seconds || burst.Renderer == null)
                {
                    // Spent: the mesh and its object stay kept until the level goes, but draw nothing.
                    if (burst.Renderer != null) burst.Renderer.enabled = false;
                    bursts.RemoveAt(b);
                    continue;
                }
                if (dt <= 0f) continue;
                for (int c = 0; c < burst.Chunks.Length; c++)
                {
                    Chunk chunk = burst.Chunks[c];
                    chunk.Age += dt;
                    chunk.Velocity += Vector3.down * (Game.Gravity * dt);
                    chunk.Position += chunk.Velocity * dt;
                    float rest = chunk.Floor + Mathf.Min(chunk.Half.x, Mathf.Min(chunk.Half.y, chunk.Half.z));
                    if (chunk.Position.y < rest && chunk.Velocity.y < 0f)
                    {
                        chunk.Position.y = rest;
                        // One real bounce, then it skids and settles.
                        chunk.Velocity = new Vector3(chunk.Velocity.x * 0.6f, chunk.Bounced ? 0f : -chunk.Velocity.y * Restitution, chunk.Velocity.z * 0.6f);
                        chunk.Spin *= chunk.Bounced ? 0f : 0.4f;
                        chunk.Bounced = true;
                    }
                    if (chunk.Spin != Vector3.zero) chunk.Rotation = Quaternion.Euler(chunk.Spin * dt) * chunk.Rotation;
                    burst.Chunks[c] = chunk;
                }
                Write(burst);
            }
            rings?.Update(dt, Context.Camera != null ? Context.Camera.transform : null);
        }

        protected override void Forget()
        {
            bursts.Clear();
            rings?.Destroy();
            rings = null;
        }
    }
}
