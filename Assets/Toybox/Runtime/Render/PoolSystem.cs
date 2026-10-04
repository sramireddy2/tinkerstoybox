using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// Colour pools, the splash ring and first-impact dust (ART_BIBLE 9.4, 9.5): how a toy's true size
    /// is told once it has been let go.
    ///
    /// Every grabbable toy that is not in the hand sits in a pool of its own colour, drawn by
    /// Toybox/RoomLit from the globals this presenter sets every frame (<c>_PoolPos</c>, <c>_PoolTint</c>,
    /// <c>_PoolCount</c>, <c>_PoolGain</c>): the toy's centre (or up to three proxy spheres from its
    /// <see cref="ToyInfo"/>) with 0.8 of its true radius. The tier decides how many pools there are (6 /
    /// 10 / 16); the ones that count most from where the camera stands get them, and fade over 200 ms as
    /// they enter and leave that set. On a grab the toy's pool stays where it was and drains in 120 ms;
    /// on release it floods back at the toy's new size, and a ring of the toy's colour runs out across
    /// every room surface from one to three times its radius (<c>_Splash</c>, <c>_SplashTint</c>). The
    /// first impact after a release raises a ring of Paper dust discs of constant world size and a
    /// second, weaker ring.
    ///
    /// Nothing here allocates per frame.
    /// </summary>
    [Presenter(210, ProvidesLook = true)]
    public sealed class PoolSystem : IPresenter
    {
        /// <summary>Size of the shader's arrays.</summary>
        public const int MaxPools = 16;
        public const float RadiusFactor = 0.8f, MinRadius = 0.08f;
        public const float FadeTime = 0.2f, DrainTime = 0.12f;
        public const float FloodTime = 0.38f, FloodPeakTime = 0.14f, FloodPeak = 1.25f;
        public const float SplashTime = 0.35f, SplashFrom = 1f, SplashTo = 3f, SplashStrength = 0.6f, ImpactSplashStrength = 0.3f;
        public const float FocusBoost = 0.2f, FocusTime = 0.12f;
        public const float HighVisibilityGain = 1.5f;
        /// <summary>
        /// What Toybox/RoomLit does with a pool beyond ART_BIBLE 3.3 (the constants of RoomLitLib.hlsl, here
        /// for whoever computes a pool by hand): the share of the halo and the ring shown in the toy's own
        /// hue whatever the surface's colour, the most all halos together add at one point (times the
        /// gain), and how much stronger than 9.4 the ring is drawn. The halo also takes the square root
        /// of its facing term.
        /// </summary>
        public const float OwnHue = 0.7f, HaloCap = 0.45f, RingBoost = 1.5f;
        /// <summary>
        /// A toy without proxy spheres of its own that is this many times longer than it is wide gets three
        /// spheres along its length instead of one around all of it (ART_BIBLE 9.4: "aspect above 3"; a
        /// plank's single sphere would reach every wall of the level once the plank is a bridge).
        /// </summary>
        public const float LongAspect = 3f;
        public const int DustDiscs = 8;
        public const float DustMinSize = 0.15f, DustMaxSize = 0.3f;

        static readonly int PoolPosId = Shader.PropertyToID("_PoolPos");
        static readonly int PoolTintId = Shader.PropertyToID("_PoolTint");
        static readonly int PoolCountId = Shader.PropertyToID("_PoolCount");
        static readonly int PoolGainId = Shader.PropertyToID("_PoolGain");
        static readonly int SplashId = Shader.PropertyToID("_Splash");
        static readonly int SplashTintId = Shader.PropertyToID("_SplashTint");

        /// <summary>How many pools a tier draws (the loop bound of the room shader).</summary>
        public static int PoolsFor(QualityTier tier) => tier == QualityTier.Low ? 6 : tier == QualityTier.Medium ? 10 : MaxPools;

        /// <summary>Pool strength this long after a release: 0, up to 1.25 at 140 ms, settled at 1 by 380 ms.</summary>
        public static float Flood(float seconds)
        {
            if (seconds <= 0f) return 0f;
            if (seconds < FloodPeakTime) return FloodPeak * Mathf.SmoothStep(0f, 1f, seconds / FloodPeakTime);
            if (seconds < FloodTime) return Mathf.Lerp(FloodPeak, 1f, Mathf.SmoothStep(0f, 1f, (seconds - FloodPeakTime) / (FloodTime - FloodPeakTime)));
            return 1f;
        }

        sealed class Entry
        {
            public Prop Prop;
            public ToyInfo Info;
            /// <summary>The pool's colour, linear (the toy's base colour times the recipe's pool tint).</summary>
            public Color Tint;
            public bool Pools;
            /// <summary>0..1: how far the pool has faded in.</summary>
            public float Shown;
            /// <summary>Seconds since the release that is flooding the pool, or negative.</summary>
            public float Flood = -1f;
            /// <summary>0..1: eased "the crosshair is on it".</summary>
            public float Focus;
            public bool AwaitingImpact;
            public bool Selected;
            /// <summary>Not drawn yet: a toy that is there when the level starts has its pool at once.</summary>
            public bool Fresh = true;
            public int Stamp;
            /// <summary>The toy's pool spheres as measured last (xyz centre, w radius), and how many there are.</summary>
            public readonly Vector4[] Spheres = new Vector4[3];
            public int Count = 1;
            /// <summary>The strength its pool was drawn with last frame; 0 if it was not drawn.</summary>
            public float Pushed;
        }

        struct Sphere
        {
            public Entry Entry;
            public int Index;
            public float Score;
        }

        struct Ghost
        {
            public Vector4 Position;
            public Color Tint;
            public float Strength;
            public float Left;
        }

        readonly Dictionary<Prop, Entry> byProp = new Dictionary<Prop, Entry>();
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Sphere> candidates = new List<Sphere>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly Vector4[] positions = new Vector4[MaxPools];
        readonly Vector4[] tints = new Vector4[MaxPools];

        Game game;
        PresentationContext context;
        GameObject dustObject;
        ParticleSystem dust;
        int stamp;
        bool attached;

        // The splash ring: one at a time.
        Vector3 splashCentre;
        float splashRadius, splashStrength, splashAge = -1f;
        Color splashColour;

        /// <summary>Pools pushed to the shader this frame.</summary>
        public int Count { get; private set; }
        /// <summary>xyz centre, w radius of pool <paramref name="index"/> as pushed this frame.</summary>
        public Vector4 Position(int index) => positions[index];
        /// <summary>rgb linear colour, a strength of pool <paramref name="index"/> as pushed this frame.</summary>
        public Vector4 Tint(int index) => tints[index];
        /// <summary>_PoolGain as pushed this frame.</summary>
        public float Gain { get; private set; }
        /// <summary>True while a splash ring is running.</summary>
        public bool SplashActive => splashAge >= 0f;
        /// <summary>The ring's radius now (0 when none runs).</summary>
        public float SplashRingRadius { get; private set; }
        /// <summary>The ring's strength now (0 when none runs).</summary>
        public float SplashRingStrength { get; private set; }
        /// <summary>Dust discs raised since the level was loaded.</summary>
        public int DustRaised { get; private set; }

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            game.Events.LevelLoaded += OnLevelChanged;
            game.Events.LevelUnloading += OnLevelChanged;
            game.Events.PropGrabbed += OnGrabbed;
            game.Events.PropDropped += OnDropped;
            game.Events.PropImpact += OnImpact;
            attached = true;
            if (context.HasGraphics) CreateDust();
            Push(0);
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelChanged;
            game.Events.LevelUnloading -= OnLevelChanged;
            game.Events.PropGrabbed -= OnGrabbed;
            game.Events.PropDropped -= OnDropped;
            game.Events.PropImpact -= OnImpact;
            Reset();
            Push(0);
            if (dustObject != null) Sim.Destroy(dustObject);
            dustObject = null;
            dust = null;
        }

        void OnLevelChanged(LevelEvent e) => Reset();

        void Reset()
        {
            byProp.Clear();
            entries.Clear();
            candidates.Clear();
            ghosts.Clear();
            splashAge = -1f;
            DustRaised = 0;
            if (dust != null) dust.Clear(true);
        }

        // ---- Events -----------------------------------------------------------------------------------------

        // The toy leaves the list; its pool stays frozen where it was and drains.
        void OnGrabbed(PropHoldEvent e)
        {
            Entry entry = Find(e.Prop);
            if (entry == null) return;
            // By the time the event arrives the toy is already where the hold put it; the pool is left
            // where it was drawn last.
            if (entry.Pushed > 0.01f)
                for (int i = 0; i < entry.Count; i++)
                    ghosts.Add(new Ghost { Position = entry.Spheres[i], Tint = entry.Tint, Strength = entry.Pushed, Left = DrainTime });
            entry.Shown = 0f;
            entry.Pushed = 0f;
            entry.Flood = -1f;
            entry.AwaitingImpact = false;
        }

        // The toy is matter again: its pool floods at its true size and a ring runs out from it.
        void OnDropped(PropHoldEvent e)
        {
            if (e.Prop == null || e.Prop.Removed) return;
            Entry entry = Find(e.Prop);
            if (entry == null || !entry.Pools) return;
            entry.Shown = 1f;
            entry.Flood = 0f;
            entry.AwaitingImpact = true;
            Splash(e.Prop.Center, e.Radius, entry.Tint, SplashStrength);
        }

        // First impact after a release: dust at the toy's true footprint and a second, weaker ring.
        void OnImpact(PropImpactEvent e)
        {
            Entry entry = Find(e.Prop);
            if (entry == null || !entry.AwaitingImpact || e.Prop.Removed) return;
            entry.AwaitingImpact = false;
            Vector3 normal = e.Normal.sqrMagnitude > 1e-6f ? e.Normal.normalized : Vector3.up;
            Vector3 centre = e.Prop.Center - normal * Vector3.Dot(e.Prop.Center - e.Point, normal);
            Dust(centre, normal, e.Prop.Radius * RadiusFactor);
            Splash(e.Prop.Center, e.Prop.Radius, entry.Tint, ImpactSplashStrength);
        }

        /// <summary>
        /// Starts a splash ring: from <paramref name="radius"/> to three times that in 350 ms, in
        /// <paramref name="colour"/> (linear), fading from <paramref name="strength"/> to nothing. There is
        /// one ring; a new one takes over unless the running one is still the stronger.
        /// </summary>
        public void Splash(Vector3 centre, float radius, Color colour, float strength)
        {
            if (radius <= 0f || strength <= 0f) return;
            if (splashAge >= 0f && splashStrength * (1f - splashAge / SplashTime) > strength) return;
            splashCentre = centre;
            splashRadius = radius;
            splashColour = colour;
            splashStrength = strength;
            splashAge = 0f;
        }

        /// <summary>
        /// A ring of eight Paper discs of constant world size around <paramref name="centre"/>, in the plane
        /// with this normal: they look tiny beside a giant, which is the point.
        /// </summary>
        public void Dust(Vector3 centre, Vector3 normal, float radius)
        {
            DustRaised += DustDiscs;
            if (dust == null) return;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < DustDiscs; i++)
            {
                // Deterministic: the discs are spread evenly, sized and sped by their index.
                float angle = (i + 0.37f) * Mathf.PI * 2f / DustDiscs;
                float vary = ((i * 5) % DustDiscs) / (float)(DustDiscs - 1);
                Vector3 outward = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                emit.position = centre + outward * radius + normal * 0.08f;
                emit.velocity = outward * Mathf.Lerp(0.7f, 1.3f, vary) + normal * Mathf.Lerp(0.5f, 0.9f, 1f - vary);
                emit.startSize = Mathf.Lerp(DustMinSize, DustMaxSize, vary);
                emit.startLifetime = Mathf.Lerp(0.45f, 0.7f, vary);
                dust.Emit(emit, 1);
            }
        }

        // ---- Frame ------------------------------------------------------------------------------------------

        public void Frame(float dt, float alpha)
        {
            if (!attached || game.IsDisposed) return;
            int limit = PoolsFor(context.Quality);
            Vector3 eye = context.Camera != null ? context.Camera.transform.position : game.Player.Eye;

            Sync();
            Prop focus = game.Grabber.Focus;

            // Candidates: every sphere of every toy that may have a pool, scored by how much it matters from here.
            candidates.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                entry.Selected = false;
                entry.Focus = Mathf.MoveTowards(entry.Focus, entry.Prop == focus ? 1f : 0f, dt / FocusTime);
                if (entry.Flood >= 0f)
                {
                    entry.Flood += dt;
                    if (entry.Flood >= FloodTime) entry.Flood = -1f;
                }
                entry.Pushed = 0f;
                if (entry.Prop.Removed || entry.Prop.Held) continue;
                Measure(entry);
                if (!Eligible(entry)) continue;
                for (int s = 0; s < entry.Count; s++)
                {
                    Vector4 sphere = entry.Spheres[s];
                    float distance = Mathf.Max((new Vector3(sphere.x, sphere.y, sphere.z) - eye).sqrMagnitude, 1e-4f);
                    candidates.Add(new Sphere { Entry = entry, Index = s, Score = sphere.w * sphere.w / distance });
                }
            }

            // The best `limit` of them, in place at the front of the list.
            int chosen = Mathf.Min(limit, candidates.Count);
            for (int slot = 0; slot < chosen; slot++)
            {
                int best = slot;
                for (int k = slot + 1; k < candidates.Count; k++)
                    if (candidates[k].Score > candidates[best].Score) best = k;
                if (best != slot)
                {
                    Sphere swap = candidates[slot];
                    candidates[slot] = candidates[best];
                    candidates[best] = swap;
                }
                candidates[slot].Entry.Selected = true;
            }

            // Toys fade in as they enter the set and out as they leave it (or stop being liftable).
            float step = dt / FadeTime;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Fresh)
                {
                    entry.Fresh = false;
                    if (entry.Selected) entry.Shown = 1f;
                }
                entry.Shown = Mathf.MoveTowards(entry.Shown, entry.Selected ? 1f : 0f, step);
            }

            int n = 0;
            // Pools left behind by toys that were picked up, draining.
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost ghost = ghosts[i];
                ghost.Left -= dt;
                if (ghost.Left <= 0f)
                {
                    ghosts.RemoveAt(i);
                    continue;
                }
                ghosts[i] = ghost;
                if (n < MaxPools) Put(n++, ghost.Position, ghost.Tint, ghost.Strength * ghost.Left / DrainTime);
            }
            // The chosen ones.
            for (int slot = 0; slot < chosen && n < MaxPools; slot++)
            {
                Entry entry = candidates[slot].Entry;
                entry.Pushed = Strength(entry);
                Put(n++, entry.Spheres[candidates[slot].Index], entry.Tint, entry.Pushed);
            }
            // And whatever is still fading out, while there is room.
            for (int i = 0; i < entries.Count && n < MaxPools; i++)
            {
                Entry entry = entries[i];
                if (entry.Selected || entry.Shown <= 0.001f || entry.Prop.Removed || entry.Prop.Held) continue;
                entry.Pushed = Strength(entry);
                for (int s = 0; s < entry.Count && n < MaxPools; s++) Put(n++, entry.Spheres[s], entry.Tint, entry.Pushed);
            }

            Push(n);
            AnimateSplash(dt);
            if (dust != null) dust.Simulate(dt, true, false, false);
        }

        static float Strength(Entry entry) =>
            entry.Shown * (entry.Flood >= 0f ? Flood(entry.Flood) : 1f) * (1f + FocusBoost * entry.Focus);

        void Put(int index, Vector4 position, Color tint, float strength)
        {
            positions[index] = position;
            tints[index] = new Vector4(tint.r, tint.g, tint.b, strength);
        }

        void Push(int count)
        {
            Count = count;
            for (int i = count; i < MaxPools; i++)
            {
                positions[i] = Vector4.zero;
                tints[i] = Vector4.zero;
            }
            float gain = game != null && game.Environment != null ? game.Environment.Preset.PoolGain : EnvironmentPreset.SunnyRug.PoolGain;
            if (Settings.HighVisibility) gain *= HighVisibilityGain;
            Gain = gain;
            Shader.SetGlobalVectorArray(PoolPosId, positions);
            Shader.SetGlobalVectorArray(PoolTintId, tints);
            Shader.SetGlobalFloat(PoolCountId, count);
            Shader.SetGlobalFloat(PoolGainId, gain);
            if (count == 0 && splashAge < 0f) Shader.SetGlobalVector(SplashTintId, Vector4.zero);
        }

        void AnimateSplash(float dt)
        {
            if (splashAge < 0f)
            {
                SplashRingRadius = 0f;
                SplashRingStrength = 0f;
                Shader.SetGlobalVector(SplashTintId, Vector4.zero);
                return;
            }
            splashAge += dt;
            if (splashAge >= SplashTime)
            {
                splashAge = -1f;
                SplashRingRadius = 0f;
                SplashRingStrength = 0f;
                Shader.SetGlobalVector(SplashTintId, Vector4.zero);
                return;
            }
            float t = Mathf.Clamp01(splashAge / SplashTime);
            SplashRingRadius = splashRadius * Mathf.Lerp(SplashFrom, SplashTo, t);
            SplashRingStrength = splashStrength * (1f - t);
            Shader.SetGlobalVector(SplashId, new Vector4(splashCentre.x, splashCentre.y, splashCentre.z, splashRadius));
            Shader.SetGlobalVector(SplashTintId, new Vector4(splashColour.r * SplashRingStrength, splashColour.g * SplashRingStrength, splashColour.b * SplashRingStrength, SplashRingRadius));
        }

        // ---- Toys -------------------------------------------------------------------------------------------

        // One entry per prop of the level; props that are gone lose theirs.
        void Sync()
        {
            stamp++;
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed) continue;
                if (!byProp.TryGetValue(prop, out Entry entry)) entry = Add(prop);
                entry.Stamp = stamp;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Stamp == stamp) continue;
                byProp.Remove(entries[i].Prop);
                entries.RemoveAt(i);
            }
        }

        Entry Find(Prop prop)
        {
            if (prop == null) return null;
            if (byProp.TryGetValue(prop, out Entry entry)) return entry;
            return prop.Removed ? null : Add(prop);
        }

        Entry Add(Prop prop)
        {
            ToyInfo info = ToyInfo.Of(prop.GameObject);
            // A toy without a tag counts as glossy plastic in Paper.
            ToyRecipe recipe = info != null ? info.Recipe : ToyRecipe.GlossyPlastic;
            var entry = new Entry
            {
                Prop = prop,
                Info = info,
                Pools = recipe == null || recipe.Pool,
                Tint = (recipe ?? ToyRecipe.GlossyPlastic).PoolColor(info != null ? info.Candy : Palette.Paper),
            };
            byProp[prop] = entry;
            entries.Add(entry);
            return entry;
        }

        // Candy plus rim plus pool means "you can lift this": only grabbable toys that are not in the hand.
        static bool Eligible(Entry entry) => entry.Pools && !entry.Prop.Removed && entry.Prop.Grabbable && !entry.Prop.Held;

        // Where the toy's pool is now: the prop's world centre with 0.8 of its true radius, or the proxy
        // spheres its toy supplies (up to three, in the toy's own space at scale 1).
        static void Measure(Entry entry)
        {
            Prop prop = entry.Prop;
            Vector4[] proxies = entry.Info != null ? entry.Info.PoolProxies : null;
            if (proxies != null && proxies.Length > 0)
            {
                entry.Count = Mathf.Min(proxies.Length, 3);
                for (int i = 0; i < entry.Count; i++)
                {
                    Vector3 centre = prop.Transform.TransformPoint(new Vector3(proxies[i].x, proxies[i].y, proxies[i].z));
                    entry.Spheres[i] = new Vector4(centre.x, centre.y, centre.z, Mathf.Max(proxies[i].w * prop.Scale, MinRadius));
                }
                return;
            }
            // A long toy whose factory gave it no proxies: three spheres along its longest side.
            Vector3 half = prop.LocalHalfExtents;
            int axis = half.x >= half.y && half.x >= half.z ? 0 : half.y >= half.z ? 1 : 2;
            float longest = half[axis], a = half[(axis + 1) % 3], b = half[(axis + 2) % 3];
            if (longest >= LongAspect * Mathf.Max(Mathf.Max(a, b), 1e-4f))
            {
                float third = longest / 3f;
                float radius = Mathf.Max(RadiusFactor * Mathf.Sqrt(a * a + b * b + third * third) * prop.Scale, MinRadius);
                entry.Count = 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 local = prop.LocalCenter;
                    local[axis] += (i - 1) * 2f * third;
                    Vector3 centre = prop.Transform.TransformPoint(local);
                    entry.Spheres[i] = new Vector4(centre.x, centre.y, centre.z, radius);
                }
                return;
            }
            Vector3 middle = prop.Center;
            entry.Count = 1;
            entry.Spheres[0] = new Vector4(middle.x, middle.y, middle.z, Mathf.Max(RadiusFactor * prop.Radius, MinRadius));
        }

        // ---- Dust -------------------------------------------------------------------------------------------

        void CreateDust()
        {
            dustObject = new GameObject("Impact Dust") { hideFlags = HideFlags.DontSave };
            dustObject.transform.SetParent(context.Root, false);
            dust = dustObject.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = dust.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 96;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startColor = Palette.Paper;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            ParticleSystem.EmissionModule emission = dust.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.enabled = false;

            // The discs slow down and thin out.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = dust.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            ParticleSystem.ColorOverLifetimeModule fade = dust.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = dustObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = Materials.Flat(new FlatRecipe { Name = "Dust", Color = Color.white, Shape = FlatShape.SoftDisc, Soft = 0.35f, Blend = FlatBlend.Alpha });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
