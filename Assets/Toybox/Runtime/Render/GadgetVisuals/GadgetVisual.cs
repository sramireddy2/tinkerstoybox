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
    /// The shape every gadget presenter has: it looks at the gadgets of the level that is loaded
    /// (<see cref="GadgetList"/>), adopts the ones it knows how to draw, draws them every frame from
    /// their public state, and throws everything away when the level is unloaded or the quality tier
    /// changes. Renderers only - nothing here has a collider, and nothing here is read by the simulation.
    ///
    /// The objects of a presenter live under one child of the presentation's root; meshes and other
    /// assets it makes per level go through <see cref="Keep{T}"/> and are destroyed with the level.
    /// Presenters of this kind are part of the look: the plain look (stand-in materials without
    /// blending) leaves them out.
    /// </summary>
    public abstract class GadgetVisual : IPresenter
    {
        readonly List<Object> kept = new List<Object>();
        LevelContext builtFor;
        QualityTier builtTier;
        int seen;
        bool attached;

        protected Game Game { get; private set; }
        protected PresentationContext Context { get; private set; }
        /// <summary>Parent of everything this presenter creates.</summary>
        protected Transform Root { get; private set; }
        /// <summary>Seconds of presentation: what pulses and streamers animate on.</summary>
        protected float Now => Context != null ? Context.UnscaledTime : 0f;
        protected QualityTier Tier => builtTier;

        /// <summary>True between Attach and Dispose.</summary>
        public bool Attached => attached;
        /// <summary>How many objects the presenter keeps alive for the current level (meshes, renderers' objects).</summary>
        public int KeptCount => kept.Count;

        public void Attach(Game game, PresentationContext context)
        {
            Game = game;
            Context = context;
            var root = new GameObject(GetType().Name) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(context.Root, false);
            Root = root.transform;
            attached = true;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelUnloading += OnLevelUnloading;
            Subscribe(game.Events);
            Sync();
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached || Game.IsDisposed) return;
            Sync();
            if (builtFor != null) Draw(dt, alpha);
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            Game.Events.LevelLoaded -= OnLevelLoaded;
            Game.Events.LevelUnloading -= OnLevelUnloading;
            Unsubscribe(Game.Events);
            Clear();
            Release();
            // Game.Dispose destroys game.Root and this with it; then there is nothing left to do here.
            if (Root != null) Sim.Destroy(Root.gameObject);
            Root = null;
        }

        /// <summary>Subscribe to the gadget mirrors this presenter reacts to. Called once, in Attach.</summary>
        protected virtual void Subscribe(GameEvents events) { }
        protected virtual void Unsubscribe(GameEvents events) { }
        /// <summary>A level (or a tier) begins: nothing has been adopted yet.</summary>
        protected virtual void Begin() { }
        /// <summary>A gadget of the level, in construction order. Take it if it is yours.</summary>
        protected virtual void Adopt(Gadget gadget) { }
        /// <summary>Once per rendered frame while a level is loaded.</summary>
        protected abstract void Draw(float dt, float alpha);
        /// <summary>Forget the level: lists are emptied here; kept objects are destroyed right after.</summary>
        protected virtual void Forget() { }
        /// <summary>The presenter is going away: whatever outlives levels (a camera, a render texture).</summary>
        protected virtual void Release() { }

        /// <summary>Destroyed when the level is unloaded.</summary>
        protected T Keep<T>(T made) where T : Object
        {
            if (made != null) kept.Add(made);
            return made;
        }

        /// <summary>A renderer of its own under <see cref="Root"/>: no shadows, no collider.</summary>
        protected MeshRenderer Visual(string name, Mesh mesh, Material material, Transform parent = null)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent != null ? parent : Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Keep(go);
            return renderer;
        }

        void OnLevelLoaded(LevelEvent e) => Sync();

        void OnLevelUnloading(LevelEvent e) => Clear();

        void Sync()
        {
            LevelContext ctx = Game.IsDisposed ? null : Game.Context;
            if (ctx != builtFor || (ctx != null && Context.Quality != builtTier))
            {
                Clear();
                builtFor = ctx;
                builtTier = Context.Quality;
                if (ctx != null) Begin();
            }
            if (ctx == null) return;
            IReadOnlyList<Gadget> gadgets = GadgetList.Of(ctx);
            // By index: a gadget made after the level was built (a spawner) is adopted when it appears.
            for (; seen < gadgets.Count; seen++) Adopt(gadgets[seen]);
        }

        void Clear()
        {
            builtFor = null;
            seen = 0;
            Forget();
            for (int i = kept.Count - 1; i >= 0; i--)
                if (kept[i] != null) Sim.Destroy(kept[i]);
            kept.Clear();
        }
    }

    /// <summary>What the gadget presenters share: the tints, the per-tier counts, and the meshes effects are cut from.</summary>
    public static class GadgetFx
    {
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// "This does not fit": a red that blinks three times a second. Beside the art bible's signals
        /// (amber pulses slowly, go is steady) it is told apart by its motion as much as by its hue.
        /// </summary>
        public static readonly Signal Wrong = new Signal("Wrong", Palette.Hex("#FF4A32"), 2.4f, 1f, 3f);
        /// <summary>The right size, pointing the wrong way: amber, blinking fast.</summary>
        public static readonly Signal Turned = new Signal("Turned", Palette.Amber.Color, Palette.Amber.Gain, 0.9f, 3f);

        public static int Pick(QualityTier tier, int low, int medium, int high) =>
            tier == QualityTier.Low ? low : tier == QualityTier.High ? high : medium;

        public static float Pick(QualityTier tier, float low, float medium, float high) =>
            tier == QualityTier.Low ? low : tier == QualityTier.High ? high : medium;

        /// <summary>A linear colour from an sRGB one, times a gain, with an alpha.</summary>
        public static Color Lin(Color srgb, float gain = 1f, float alpha = 1f)
        {
            Color linear = Palette.Lin(srgb);
            return new Color(linear.r * gain, linear.g * gain, linear.b * gain, alpha);
        }

        /// <summary>The same number for the same arguments, 0..1: effects look the same on every run and in every picture.</summary>
        public static float Hash(int index, int salt)
        {
            unchecked
            {
                uint h = (uint)index * 374761393u + (uint)salt * 668265263u + 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// The deeper edge a Paper mark is die-cut with in a day room, so that it reads on a pale wall or a
        /// sunlit floor. Clear in the night room: Paper stands out there by itself.
        /// </summary>
        public static Color Edge(Dip dip, float alpha = 0.55f) =>
            dip == null || dip.Night ? new Color(0f, 0f, 0f, 0f) : Lin(Palette.Mix(dip.Deep, Palette.Ink, 0.35f), 1f, alpha);

        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A unit square in the XY plane with the unit square of UVs (the shape mask of Toybox/Flat lives there). Shared.</summary>
        public static Mesh Quad => MeshKit.Cached("GadgetFx Quad", () =>
        {
            var mesh = new Mesh { name = "GadgetFx Quad", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        });

        /// <summary>A flat ring in the XY plane between two radii, white. Shared per pair of radii.</summary>
        public static Mesh Annulus(float inner, float outer, int segments = 40) =>
            MeshKit.Cached(MeshKit.Key("GadgetFx Annulus", inner, outer, segments), () =>
            {
                var fx = new FxMesh("GadgetFx Annulus");
                fx.Annulus(Vector3.zero, Vector3.right, Vector3.up, inner, outer, segments, Color.white, Color.white);
                fx.Apply();
                return fx.Mesh;
            });

        /// <summary>The unlit material every effect here is drawn with: colour and alpha come from the vertices (or a property block).</summary>
        public static Material Flat(string name, FlatBlend blend, FlatShape shape = FlatShape.Quad, float soft = 0f, int queueOffset = 0) =>
            Materials.Flat(new FlatRecipe { Name = name, Color = Color.white, Shape = shape, Blend = blend, Soft = soft, QueueOffset = queueOffset });
    }

    /// <summary>
    /// A mesh that is written again whenever what it shows changes: quads with a colour per corner, in
    /// world space. The lists are kept, so writing it allocates nothing once it has reached its size.
    /// </summary>
    public sealed class FxMesh
    {
        static readonly Vector2 Uv00 = new Vector2(0f, 0f), Uv10 = new Vector2(1f, 0f), Uv11 = new Vector2(1f, 1f), Uv01 = new Vector2(0f, 1f);

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public Mesh Mesh { get; }
        public int VertexCount => vertices.Count;
        public int QuadCount => triangles.Count / 6;

        public FxMesh(string name)
        {
            Mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            Mesh.MarkDynamic();
        }

        public void Clear()
        {
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();
        }

        /// <summary>A quad a-b-c-d (in order round its edge) with one colour and the whole unit square of UVs.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color) => Quad(a, b, c, d, color, color, color, color, Uv00, Uv10, Uv11, Uv01);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd) => Quad(a, b, c, d, ca, cb, cc, cd, Uv00, Uv10, Uv11, Uv01);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            colors.Add(ca);
            colors.Add(cb);
            colors.Add(cc);
            colors.Add(cd);
            uvs.Add(ua);
            uvs.Add(ub);
            uvs.Add(uc);
            uvs.Add(ud);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
        }

        /// <summary>A square of the given half size about a centre, spanned by two unit vectors.</summary>
        public void Square(Vector3 centre, Vector3 right, Vector3 up, float half, Color color)
        {
            Vector3 x = right * half, y = up * half;
            Quad(centre - x - y, centre + x - y, centre + x + y, centre - x + y, color);
        }

        /// <summary>A flat ring between two radii; the UVs are the middle of the unit square (no shape mask).</summary>
        public void Annulus(Vector3 centre, Vector3 right, Vector3 up, float inner, float outer, int segments, Color innerColor, Color outerColor)
        {
            var middle = new Vector2(0.5f, 0.5f);
            Vector3 previous = right;
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector3 next = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Quad(centre + previous * inner, centre + previous * outer, centre + next * outer, centre + next * inner,
                    innerColor, outerColor, outerColor, innerColor, middle, middle, middle, middle);
                previous = next;
            }
        }

        /// <summary>A filled regular polygon (a fan of quads degenerate at the centre); no shape mask.</summary>
        public void Polygon(Vector3 centre, Vector3 right, Vector3 up, float radius, int sides, float startDegrees, Color color)
        {
            var middle = new Vector2(0.5f, 0.5f);
            float start = startDegrees * Mathf.Deg2Rad;
            Vector3 previous = right * Mathf.Cos(start) + up * Mathf.Sin(start);
            for (int i = 1; i <= sides; i++)
            {
                float angle = start + Mathf.PI * 2f * i / sides;
                Vector3 next = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Quad(centre, centre + previous * radius, centre + next * radius, centre, color, color, color, color, middle, middle, middle, middle);
                previous = next;
            }
        }

        /// <summary>Writes the lists into the mesh. An empty mesh draws nothing.</summary>
        public void Apply()
        {
            Mesh.Clear();
            if (vertices.Count == 0) return;
            Mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            Mesh.SetVertices(vertices);
            Mesh.SetUVs(0, uvs);
            Mesh.SetColors(colors);
            Mesh.SetTriangles(triangles, 0, true);
        }
    }

    /// <summary>
    /// Short-lived marks in the world: soft rings that grow and fade (something happened here), thin
    /// hoops that hang for a moment (you are thrown through these), and streaks that stand (this is how
    /// high it went). Two meshes and two draws however many are alive, nothing at all while none is.
    /// </summary>
    public sealed class CuePool
    {
        public const int HoopSegments = 28;

        struct Cue
        {
            public bool Streak, Hoop, Outlined;
            public float Age, Life;
            public Vector3 Position;
            /// <summary>The ring lies in the plane with this normal; zero: it faces the camera.</summary>
            public Vector3 Normal;
            public float From, To;
            public Color Color;
        }

        /// <summary>An outlined hoop: its Paper core and the whole band it is cut from, as shares of its radius.</summary>
        public const float HoopCoreInner = 0.945f, HoopCoreOuter = 0.98f, HoopBandInner = 0.925f;
        /// <summary>An outlined ring's edge is the same ring, this much bigger; an outlined streak's, this much wider.</summary>
        public const float RingEdge = 1.07f, StreakEdge = 1.4f;

        readonly Cue[] cues;
        readonly FxMesh rings, streaks;
        readonly MeshRenderer ringRenderer, streakRenderer;
        readonly Color edge;
        int alive;

        public int Capacity => cues.Length;
        /// <summary>Cues on show right now.</summary>
        public int Alive => alive;
        /// <summary>Cues started since the pool was made.</summary>
        public int Started { get; private set; }
        public Renderer RingRenderer => ringRenderer;
        public Renderer StreakRenderer => streakRenderer;
        public Mesh RingMesh => rings.Mesh;
        public Mesh StreakMesh => streaks.Mesh;
        /// <summary>True if marks that ask to be outlined get an edge here (a day room).</summary>
        public bool Outlines => edge.a > 0f;

        /// <param name="edge">The tone outlined marks are die-cut with (<see cref="GadgetFx.Edge"/>); clear for none.</param>
        public CuePool(string name, Transform parent, int capacity, Color edge = default)
        {
            this.edge = edge;
            cues = new Cue[Mathf.Max(1, capacity)];
            rings = new FxMesh(name + " Rings");
            streaks = new FxMesh(name + " Streaks");
            ringRenderer = Make(name + " Rings", parent, rings.Mesh, GadgetFx.Flat("Cue Ring", FlatBlend.Alpha, FlatShape.Ring, 0.25f, 2));
            streakRenderer = Make(name + " Streaks", parent, streaks.Mesh, GadgetFx.Flat("Cue Streak", FlatBlend.Alpha, FlatShape.SoftDisc, 0.8f, 2));
        }

        static MeshRenderer Make(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>
        /// A ring at a point, growing (or shrinking) from one radius to another while it fades. Normal zero: it
        /// faces the camera. Outlined (for Paper marks; a signal's own hue needs none): die-cut with the pool's edge.
        /// </summary>
        public void Ring(Vector3 position, Vector3 normal, float fromRadius, float toRadius, Color color, float seconds, bool outlined = false) =>
            Start(new Cue { Outlined = outlined, Position = position, Normal = normal, From = fromRadius, To = toRadius, Color = color, Life = Mathf.Max(0.05f, seconds) });

        /// <summary>
        /// A thin, crisp hoop in the plane with the given normal: for marks the eye passes close to or
        /// through, where a soft ring would fill the screen.
        /// </summary>
        public void Hoop(Vector3 position, Vector3 normal, float fromRadius, float toRadius, Color color, float seconds, bool outlined = false) =>
            Start(new Cue { Hoop = true, Outlined = outlined, Position = position, Normal = normal, From = fromRadius, To = toRadius, Color = color, Life = Mathf.Max(0.05f, seconds) });

        /// <summary>A vertical streak from a point up to a height, of a width, fading from the bottom.</summary>
        public void Streak(Vector3 position, float height, float width, Color color, float seconds, bool outlined = false) =>
            Start(new Cue { Streak = true, Outlined = outlined, Position = position, From = width, To = height, Color = color, Life = Mathf.Max(0.05f, seconds) });

        void Start(Cue cue)
        {
            // A full pool gives its oldest cue away: the newest event is the one being looked at.
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < cues.Length; i++)
            {
                if (cues[i].Life <= 0f)
                {
                    slot = i;
                    break;
                }
                float progress = cues[i].Age / cues[i].Life;
                if (progress > oldest)
                {
                    oldest = progress;
                    slot = i;
                }
            }
            cues[slot] = cue;
            Started++;
        }

        public void Clear()
        {
            for (int i = 0; i < cues.Length; i++) cues[i] = default;
            alive = 0;
            if (ringRenderer != null) ringRenderer.enabled = false;
            if (streakRenderer != null) streakRenderer.enabled = false;
        }

        /// <summary>Ages the cues and writes the two meshes. <paramref name="camera"/> may be null (rings then lie flat).</summary>
        public void Update(float dt, Transform camera)
        {
            if (ringRenderer == null) return;
            int ringCount = 0, streakCount = 0;
            alive = 0;
            rings.Clear();
            streaks.Clear();
            Vector3 eye = camera != null ? camera.position : Vector3.zero;
            for (int i = 0; i < cues.Length; i++)
            {
                if (cues[i].Life <= 0f) continue;
                cues[i].Age += dt;
                Cue cue = cues[i];
                if (cue.Age >= cue.Life)
                {
                    cues[i] = default;
                    continue;
                }
                alive++;
                float t = cue.Age / cue.Life;
                Color color = cue.Color;
                bool outlined = cue.Outlined && edge.a > 0f;
                Color rim = edge;
                if (cue.Streak)
                {
                    // Stands, then fades; always turned about the vertical toward the eye.
                    float fade = 1f - GadgetFx.Smooth((t - 0.4f) / 0.6f);
                    color.a *= fade;
                    rim.a *= fade;
                    Vector3 toEye = eye - cue.Position;
                    toEye.y = 0f;
                    Vector3 side = toEye.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, toEye.normalized) : Vector3.right;
                    Vector3 half = side * (cue.From * 0.5f);
                    Vector3 top = cue.Position + Vector3.up * cue.To;
                    var left = new Vector2(0f, 0.5f);
                    var right = new Vector2(1f, 0.5f);
                    if (outlined)
                    {
                        Color rimFaint = rim;
                        rimFaint.a *= 0.15f;
                        Vector3 wide = half * StreakEdge;
                        streaks.Quad(cue.Position - wide, cue.Position + wide, top + wide, top - wide, rimFaint, rimFaint, rim, rim, left, right, right, left);
                    }
                    Color faint = color;
                    faint.a *= 0.15f;
                    streaks.Quad(cue.Position - half, cue.Position + half, top + half, top - half, faint, faint, color, color, left, right, right, left);
                    streakCount++;
                }
                else
                {
                    float eased = 1f - (1f - t) * (1f - t);
                    float radius = Mathf.Lerp(cue.From, cue.To, eased);
                    float fade = 1f - t * t;
                    color.a *= fade;
                    rim.a *= fade;
                    Vector3 normal = cue.Normal;
                    if (normal.sqrMagnitude < 1e-6f) normal = camera != null ? (eye - cue.Position).normalized : Vector3.up;
                    if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
                    Vector3 x = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                    Vector3 y = Vector3.Cross(normal, x);
                    if (cue.Hoop)
                    {
                        // Drawn with the streaks: plain quads, no shape mask. Outlined, the band is a little wider
                        // (inward: the hoop is as big as before) and the Paper runs down its middle.
                        if (outlined)
                        {
                            streaks.Annulus(cue.Position, x, y, radius * HoopBandInner, radius, HoopSegments, rim, rim);
                            streaks.Annulus(cue.Position, x, y, radius * HoopCoreInner, radius * HoopCoreOuter, HoopSegments, color, color);
                        }
                        else streaks.Annulus(cue.Position, x, y, radius * 0.955f, radius, HoopSegments, color, color);
                        streakCount++;
                    }
                    else
                    {
                        // The edge is the same ring a little bigger, laid down first: it shows round the outside.
                        if (outlined) rings.Square(cue.Position, x, y, radius * RingEdge, rim);
                        rings.Square(cue.Position, x, y, radius, color);
                        ringCount++;
                    }
                }
            }
            rings.Apply();
            streaks.Apply();
            ringRenderer.enabled = ringCount > 0;
            streakRenderer.enabled = streakCount > 0;
        }

        public void Destroy()
        {
            if (ringRenderer != null) Sim.Destroy(ringRenderer.gameObject);
            if (streakRenderer != null) Sim.Destroy(streakRenderer.gameObject);
            Sim.Destroy(rings.Mesh);
            Sim.Destroy(streaks.Mesh);
        }
    }
}
