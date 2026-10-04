using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// Water one can read (LEVELS 2.3, Level 13). A <see cref="WaterVolume"/> draws its own flat surface;
    /// seen from the side - through the glass of a bowl - a flat sheet is a hairline. This adds, per volume
    /// that draws a surface:
    ///
    /// - the body: the footprint's wall from the floor up to the surface, tinted by height (deep and
    ///   dense at the bottom, lighter at the top - a vertex tint, no depth fade, the same on every tier);
    /// - the waterline: a Paper band along the top of the body and round the edge of the surface, which
    ///   brightens while the level is moving.
    ///
    /// And for every <see cref="Sponge"/>: the toy itself goes from dry to soaked as it fills - darker,
    /// warmer and glossy, by a property block on its own renderers, so it stays soaked in the hand - with
    /// ripples on the water while it drinks or is wrung out.
    /// </summary>
    [Presenter(238, ProvidesLook = true)]
    public sealed class WaterVisuals : GadgetVisual
    {
        /// <summary>The body sits this far inside the footprint, so it never fights the walls of its container.</summary>
        public const float Inset = 0.994f;
        public const float BodyAlphaTop = 0.34f, BodyAlphaBottom = 0.66f;
        /// <summary>A soaked sponge: its colour to this power (darker and more saturated), times this.</summary>
        public const float WetPower = 2.2f, WetGain = 0.72f;
        public const float WetSmoothness = 0.62f, WetCoat = 0.5f, WetEnv = 0.4f;
        const float RippleEvery = 0.3f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int CoatId = Shader.PropertyToID("_Coat");
        static readonly int EnvId = Shader.PropertyToID("_Env");

        sealed class Pool
        {
            public WaterVolume Water;
            public Transform Body, Line;
            public MeshRenderer BodyRenderer, LineRenderer;
            public MaterialPropertyBlock Block;
            public float LastY;
            /// <summary>0 still .. 1 the level is moving, eased.</summary>
            public float Moving;
        }

        sealed class Soak
        {
            public Sponge Sponge;
            public Renderer[] Renderers;
            public Vector4[] Base;
            public float[] Smoothness, Coat, Env;
            public float Applied = -1f;
            public float Clock;
        }

        readonly List<Pool> pools = new List<Pool>();
        readonly List<Soak> soaks = new List<Soak>();
        MaterialPropertyBlock block;
        CuePool ripples;

        public int Count => pools.Count;
        public int SpongeCount => soaks.Count;
        public Renderer BodyOf(WaterVolume water) => Find(water)?.BodyRenderer;
        public Renderer LineOf(WaterVolume water) => Find(water)?.LineRenderer;
        /// <summary>How much the level of a volume is seen to move, 0..1.</summary>
        public float MovingOf(WaterVolume water) => Find(water)?.Moving ?? 0f;
        /// <summary>The wetness last written to a sponge's renderers, 0 dry .. 1 soaked (-1: never).</summary>
        public float WetnessOf(Sponge sponge)
        {
            for (int i = 0; i < soaks.Count; i++)
                if (soaks[i].Sponge == sponge) return soaks[i].Applied;
            return -1f;
        }
        /// <summary>Ripples on show right now.</summary>
        public int Ripples => ripples != null ? ripples.Alive : 0;
        public int RipplesStarted => ripples != null ? ripples.Started : 0;

        /// <summary>The colour a soaked toy of this (linear) colour has.</summary>
        public static Vector4 Wet(Vector4 dry) =>
            new Vector4(Mathf.Pow(Mathf.Max(0f, dry.x), WetPower) * WetGain, Mathf.Pow(Mathf.Max(0f, dry.y), WetPower) * WetGain, Mathf.Pow(Mathf.Max(0f, dry.z), WetPower) * WetGain, dry.w);

        protected override void Begin()
        {
            block ??= new MaterialPropertyBlock();
            ripples = new CuePool("Water Ripples", Root, GadgetFx.Pick(Tier, 4, 8, 12), GadgetFx.Edge(Materials.Dip));
        }

        protected override void Adopt(Gadget gadget)
        {
            if (gadget is Sponge sponge)
            {
                AdoptSponge(sponge);
                return;
            }
            // A volume that draws no surface is plumbing (a drain, a reservoir out of sight): nothing to show.
            if (!(gadget is WaterVolume water) || water.Surface == null || !water.Footprint.IsValid) return;

            Dip dip = Materials.Dip;
            Zone footprint = water.Footprint;
            Color bottom = GadgetFx.Lin(Palette.Mix(dip.Deep, Palette.Ink, dip.Night ? 0f : 0.25f), 1f, BodyAlphaBottom);
            Color top = GadgetFx.Lin(dip.Night ? dip.Light : dip.Deep, 1f, BodyAlphaTop);
            float across = footprint.Shape == ZoneShape.Box ? Mathf.Min(footprint.Size.x, footprint.Size.z) : footprint.Radius * 2f;
            float band = Mathf.Clamp(across * 0.03f, 0.05f, 0.16f);
            Color paper = GadgetFx.Lin(Palette.Paper, 1f, 0.9f);
            Color clear = paper;
            clear.a = 0f;

            var body = new FxMesh("Water Body");
            var line = new FxMesh("Waterline");
            if (footprint.Shape == ZoneShape.Box)
            {
                Vector3 x = Vector3.right * (footprint.Size.x * 0.5f * Inset), z = Vector3.forward * (footprint.Size.z * 0.5f * Inset);
                Vector3[] corners = { -x - z, x - z, x + z, -x + z };
                Vector3 lift = Vector3.up * 0.006f;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = corners[i], b = corners[(i + 1) % 4];
                    body.Quad(a, b, b + Vector3.up, a + Vector3.up, bottom, bottom, top, top);
                    line.Quad(a + Vector3.down * band, b + Vector3.down * band, b, a, clear, clear, paper, paper);
                    // The edge of the surface: a strip just inside it, lying on the water.
                    var innerA = new Vector3(a.x - Mathf.Sign(a.x) * band, 0f, a.z - Mathf.Sign(a.z) * band);
                    var innerB = new Vector3(b.x - Mathf.Sign(b.x) * band, 0f, b.z - Mathf.Sign(b.z) * band);
                    line.Quad(a + lift, b + lift, innerB + lift, innerA + lift, paper, paper, clear, clear);
                }
            }
            else
            {
                const int sides = 40;
                float radius = footprint.Radius * Inset;
                Vector3 previous = Vector3.right;
                for (int i = 1; i <= sides; i++)
                {
                    float angle = Mathf.PI * 2f * i / sides;
                    var next = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 a = previous * radius, b = next * radius;
                    body.Quad(a, b, b + Vector3.up, a + Vector3.up, bottom, bottom, top, top);
                    line.Quad(a + Vector3.down * band, b + Vector3.down * band, b, a, clear, clear, paper, paper);
                    Vector3 lift = Vector3.up * 0.006f;
                    line.Quad(a + lift, b + lift, next * (radius - band) + lift, previous * (radius - band) + lift, paper, paper, clear, clear);
                    // (the strip on the surface thins out toward the middle)
                    previous = next;
                }
            }
            body.Apply();
            line.Apply();
            Keep(body.Mesh);
            Keep(line.Mesh);

            var pool = new Pool { Water = water, Block = new MaterialPropertyBlock(), LastY = water.SurfaceY };
            pool.BodyRenderer = Visual("Water Body " + water.Name, body.Mesh, GadgetFx.Flat("Water Body", FlatBlend.Alpha, FlatShape.Quad, 0f, -2));
            pool.LineRenderer = Visual("Waterline " + water.Name, line.Mesh, GadgetFx.Flat("Waterline", FlatBlend.Alpha, FlatShape.Quad, 0f, 3));
            pool.Body = pool.BodyRenderer.transform;
            pool.Line = pool.LineRenderer.transform;
            Quaternion rotation = footprint.Shape == ZoneShape.Box ? footprint.Rotation : Quaternion.identity;
            pool.Body.rotation = rotation;
            pool.Line.rotation = rotation;
            pools.Add(pool);
            Place(pool, 0f);
        }

        void AdoptSponge(Sponge sponge)
        {
            if (sponge.Prop == null || sponge.Prop.Removed) return;
            var found = new List<Renderer>();
            foreach (Renderer renderer in sponge.Prop.GameObject.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                // Only the game's own toy shader has the numbers this turns.
                if (material == null || material.shader == null || material.shader.name != "Toybox/ToyLit" || !material.HasProperty(BaseColorId)) continue;
                found.Add(renderer);
            }
            var soak = new Soak
            {
                Sponge = sponge, Renderers = found.ToArray(), Base = new Vector4[found.Count],
                Smoothness = new float[found.Count], Coat = new float[found.Count], Env = new float[found.Count],
            };
            for (int i = 0; i < found.Count; i++)
            {
                Material material = found[i].sharedMaterial;
                soak.Base[i] = material.GetVector(BaseColorId);
                soak.Smoothness[i] = material.GetFloat(SmoothnessId);
                soak.Coat[i] = material.GetFloat(CoatId);
                soak.Env[i] = material.GetFloat(EnvId);
            }
            soaks.Add(soak);
        }

        protected override void Draw(float dt, float alpha)
        {
            for (int i = 0; i < pools.Count; i++) Place(pools[i], dt);
            for (int i = 0; i < soaks.Count; i++) Wet(soaks[i], dt);
            ripples?.Update(dt, Context.Camera != null ? Context.Camera.transform : null);
        }

        void Place(Pool pool, float dt)
        {
            WaterVolume water = pool.Water;
            if (pool.BodyRenderer == null || pool.LineRenderer == null) return;
            float depth = water.Depth, y = water.SurfaceY;
            bool wet = !water.Disposed && depth > 1e-3f;
            pool.BodyRenderer.enabled = wet;
            pool.LineRenderer.enabled = wet;
            if (dt > 0f)
            {
                bool moving = Mathf.Abs(y - pool.LastY) > 0.02f * dt;
                pool.Moving = Mathf.MoveTowards(pool.Moving, moving ? 1f : 0f, dt / (moving ? 0.12f : 0.6f));
            }
            pool.LastY = y;
            if (!wet) return;
            Vector3 centre = water.Footprint.Center;
            pool.Body.position = new Vector3(centre.x, water.FloorY, centre.z);
            pool.Body.localScale = new Vector3(1f, depth, 1f);
            pool.Line.position = new Vector3(centre.x, y, centre.z);
            float gain = 1f + 0.9f * pool.Moving;
            pool.Block.SetVector(GadgetFx.ColorId, new Vector4(gain, gain, gain, 1f));
            pool.LineRenderer.SetPropertyBlock(pool.Block);
        }

        void Wet(Soak soak, float dt)
        {
            Sponge sponge = soak.Sponge;
            if (sponge.Disposed || sponge.Prop.Removed) return;
            float wetness = sponge.Saturation01;
            if (Mathf.Abs(wetness - soak.Applied) > 1f / 128f || (wetness <= 0f && soak.Applied > 0f) || (wetness >= 1f && soak.Applied < 1f))
            {
                soak.Applied = wetness;
                for (int i = 0; i < soak.Renderers.Length; i++)
                {
                    Renderer renderer = soak.Renderers[i];
                    if (renderer == null) continue;
                    // Whatever else the block holds (a sweep, a squash, a rim fade) is read first and kept.
                    renderer.GetPropertyBlock(block);
                    block.SetVector(BaseColorId, Vector4.Lerp(soak.Base[i], Wet(soak.Base[i]), wetness));
                    block.SetFloat(SmoothnessId, Mathf.Lerp(soak.Smoothness[i], WetSmoothness, wetness));
                    block.SetFloat(CoatId, Mathf.Lerp(soak.Coat[i], WetCoat, wetness));
                    block.SetFloat(EnvId, Mathf.Lerp(soak.Env[i], WetEnv, wetness));
                    renderer.SetPropertyBlock(block);
                }
            }

            // Ripples where it drinks (drawn in) or is wrung out (pushed away). Never for a toy in the hand.
            WaterVolume over = sponge.Over;
            if (sponge.Activity == SpongeActivity.Idle || over == null || sponge.Prop.Held || ripples == null)
            {
                soak.Clock = 0f;
                return;
            }
            soak.Clock -= dt;
            if (soak.Clock > 0f) return;
            soak.Clock = RippleEvery;
            Vector3 centre = sponge.Prop.Center;
            var at = new Vector3(centre.x, over.SurfaceY + 0.012f, centre.z);
            float radius = Mathf.Max(0.3f, sponge.Prop.Radius);
            Color paper = GadgetFx.Lin(Palette.Paper, 1f, 0.75f);
            if (sponge.Activity == SpongeActivity.Soaking) ripples.Ring(at, Vector3.up, radius * 1.7f, radius * 0.9f, paper, 0.6f, true);
            else ripples.Ring(at, Vector3.up, radius * 0.9f, radius * 1.9f, paper, 0.7f, true);
        }

        Pool Find(WaterVolume water)
        {
            for (int i = 0; i < pools.Count; i++)
                if (pools[i].Water == water) return pools[i];
            return null;
        }

        protected override void Forget()
        {
            pools.Clear();
            soaks.Clear();
            ripples?.Destroy();
            ripples = null;
        }
    }
}
