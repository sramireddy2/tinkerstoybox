using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The laser rain, drawn (LEVELS 2.3, Level 11; ART_BIBLE 4.4). Per emitter:
    ///
    /// - the beams: two crossed strips per beam from its lens to where the simulation says it ends, soft
    ///   across their width, in laser red (<see cref="Palette.Laser"/>: the owner's brief says red laser
    ///   pointers), each with a thin near-white heart down its middle, as a laser pointer's beam has.
    ///   They are blended, not added, so a corridor seen through fifty rows of them is a curtain of red
    ///   lines and never a white-out (the gadget's own plain beams, which add up, are switched off while
    ///   this presenter draws);
    /// - a dot at the end of every beam, lying on whatever stopped it: a red disc (blended, so that it is
    ///   red on a pale floor too) with a white-hot spark in it, and (Medium, High) a faint wash of the
    ///   same light round it - a floor the beams reach is speckled red;
    /// - under every beam that something roofs over, a cell of shade on the ground the beam would have
    ///   reached: a deeper tint of the room's own hue, cut to the lattice. The lane behind a card is the
    ///   patch with no dots in it and a hard-edged shadow over it;
    /// - on the underside of whatever roofs a beam over, where the beam meets it from above, a pinpoint of
    ///   the same light: seen from underneath a roof is paper, and every beam it keeps off is a glow in it
    ///   (LEVELS, Level 11: "every beam is a red pinpoint seen through the paper").
    ///
    /// A held toy stops nothing in the simulation, so beams are drawn straight through it and no dot
    /// ever lies on it. Four draws per emitter. The beams' and the dots' meshes keep their shape: when a
    /// beam's length changes only that beam's corners are moved (and only it looks for the underside of
    /// its roof again); the shade and the glow, which have a piece per roofed beam, are written again
    /// from what is remembered of every beam.
    /// </summary>
    [Presenter(232, ProvidesLook = true)]
    public sealed class LaserVisuals : GadgetVisual
    {
        /// <summary>A beam strip's half width as a share of the lattice pitch, and its limits in units (its soft edge takes half of it).</summary>
        public const float BeamShare = 0.11f, BeamMin = 0.03f, BeamMax = 0.08f;
        public const float BeamAlpha = 0.9f;
        /// <summary>The beam's near-white heart: this share of the strip's width, this opaque.</summary>
        public const float CoreShare = 0.22f, CoreAlpha = 0.75f;
        /// <summary>A dot's radius as a share of the lattice pitch, and its limits in units.</summary>
        public const float DotShare = 0.24f, DotMin = 0.05f, DotMax = 0.16f;
        /// <summary>The white-hot spark in a dot: this share of its radius.</summary>
        public const float SparkShare = 0.5f;
        /// <summary>The wash round a dot: this many pitches wide, this share of the beam's light.</summary>
        public const float WashShare = 0.95f, WashGain = 0.1f;
        /// <summary>How strongly the shade tints (the multiply blend's alpha).</summary>
        public const float ShadeAlpha = 0.42f;
        /// <summary>The pinpoint under a roof: this share of a dot's radius, this share of the beam's light.</summary>
        public const float GlowShare = 0.5f, GlowGain = 0.8f;
        /// <summary>Quads of the beam mesh per beam: two red strips, their two hearts, the dot's disc.</summary>
        public const int BeamQuads = 5;
        const float Lift = 0.02f;

        /// <summary>The beams' light (linear, HDR): what the strips, the dots, the wash and the glow are tinted with.</summary>
        public static Color Light => Palette.Laser.Emission;

        /// <summary>The heart of a beam and the spark of its dot (linear): near white, with the beam's red carrying the bloom.</summary>
        public static Color Core
        {
            get
            {
                Color core = Palette.Lin(Palette.LaserCore);
                core.r = Mathf.Max(core.r, Light.r);
                return core;
            }
        }

        sealed class Rig
        {
            public LaserRain Rain;
            public float[] Lengths;
            public bool[] Roofed, Glows;
            public Vector3[] Under;
            public FxMesh Beams, Dots, Shade, Glow;
            public MeshRenderer BeamRenderer, DotRenderer, ShadeRenderer, GlowRenderer;
            public Renderer Own;
            public Vector3 Right, Up;
            public float Pitch;
            public int Shaded, Glowing, DotQuads;
            public float Height;
        }

        readonly List<Rig> rigs = new List<Rig>();

        /// <summary>Emitters drawn right now.</summary>
        public int Count => rigs.Count;
        /// <summary>Dots of the i-th emitter: one per beam.</summary>
        public int DotCount(int index) => rigs[index].Lengths.Length;
        /// <summary>Beams of the i-th emitter that are roofed over (their ground is shaded).</summary>
        public int ShadedCount(int index) => rigs[index].Shaded;
        /// <summary>Roofed beams of the i-th emitter whose roof has an underside in the open: it glows there.</summary>
        public int GlowCount(int index) => rigs[index].Glowing;
        public Renderer BeamRendererOf(int index) => rigs[index].BeamRenderer;
        public Renderer DotRendererOf(int index) => rigs[index].DotRenderer;
        public Renderer ShadeRendererOf(int index) => rigs[index].ShadeRenderer;
        public Renderer GlowRendererOf(int index) => rigs[index].GlowRenderer;
        public Mesh BeamMeshOf(int index) => rigs[index].Beams.Mesh;
        public Mesh DotMeshOf(int index) => rigs[index].Dots.Mesh;
        public Mesh ShadeMeshOf(int index) => rigs[index].Shade.Mesh;
        public Mesh GlowMeshOf(int index) => rigs[index].Glow.Mesh;
        /// <summary>How often a beam's roof was looked for (one ray each): only for beams whose length changed.</summary>
        public int RoofLooks { get; private set; }
        /// <summary>How often a beam mesh was laid out from scratch (its triangles and UVs written) rather than moved.</summary>
        public int Layouts { get; private set; }

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is LaserRain rain) || rain.BeamCount == 0) return;
            int n = rain.BeamCount;
            var rig = new Rig
            {
                Rain = rain, Lengths = new float[n], Roofed = new bool[n], Glows = new bool[n], Under = new Vector3[n],
                Beams = new FxMesh("Laser Beams"), Dots = new FxMesh("Laser Dots"), Shade = new FxMesh("Laser Shade"), Glow = new FxMesh("Laser Glow"),
            };
            Keep(rig.Beams.Mesh);
            Keep(rig.Dots.Mesh);
            Keep(rig.Shade.Mesh);
            Keep(rig.Glow.Mesh);

            // The lattice's own frame, read off its first two beams (rows run along "right").
            Vector3 direction = rain.Direction;
            Vector3 right = Vector3.zero;
            rig.Pitch = 0.45f;
            if (rain.BeamCount > 1)
            {
                Vector3 step = rain.BeamOrigin(1) - rain.BeamOrigin(0);
                step -= direction * Vector3.Dot(step, direction);
                if (step.sqrMagnitude > 1e-6f)
                {
                    rig.Pitch = step.magnitude;
                    right = step / rig.Pitch;
                }
            }
            if (right == Vector3.zero) right = Vector3.Cross(Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up, direction).normalized;
            rig.Right = right;
            rig.Up = Vector3.Cross(direction, right).normalized;

            // The shade first (it darkens what is behind it, and must not darken a beam in front of it), then
            // the beams, then the light they leave where they land.
            Color shade = GadgetFx.Lin(Materials.Dip.Deep, 0.85f, ShadeAlpha);
            rig.ShadeRenderer = Visual("Laser Shade " + rain.Name, rig.Shade.Mesh,
                Materials.Flat(new FlatRecipe { Name = "Laser Shade " + Materials.Dip.Name, Color = shade, Blend = FlatBlend.Multiply, QueueOffset = -3 }));
            rig.BeamRenderer = Visual("Laser Beams " + rain.Name, rig.Beams.Mesh, GadgetFx.Flat("Laser Beam Soft", FlatBlend.Alpha, FlatShape.SoftDisc, 0.8f, 0));
            rig.DotRenderer = Visual("Laser Dots " + rain.Name, rig.Dots.Mesh, GadgetFx.Flat("Laser Dot", FlatBlend.Additive, FlatShape.SoftDisc, 0.75f, 1));
            rig.GlowRenderer = Visual("Laser Glow " + rain.Name, rig.Glow.Mesh, GadgetFx.Flat("Laser Dot", FlatBlend.Additive, FlatShape.SoftDisc, 0.75f, 1));
            for (int i = 0; i < rig.Lengths.Length; i++) rig.Lengths[i] = -1f;
            rig.Height = -1f;

            // The gadget's own beams stand in where nobody draws better ones.
            rig.Own = rain.BeamRenderer;
            if (rig.Own != null) rig.Own.enabled = false;
            rigs.Add(rig);
        }

        protected override void Draw(float dt, float alpha)
        {
            float height = Game.Player.Height;
            for (int r = 0; r < rigs.Count; r++)
            {
                Rig rig = rigs[r];
                if (rig.Rain.Disposed) continue;
                // The player's height says which beams count as roofed: another height, everything again.
                bool all = !Mathf.Approximately(rig.Height, height);
                bool changed = all;
                for (int i = 0; i < rig.Lengths.Length && !changed; i++) changed = rig.Rain.BeamLength(i) != rig.Lengths[i];
                if (changed) Write(rig, height, all);
            }
        }

        // Brings the picture up to date with the beams' lengths. Everything, or - the usual case, a toy
        // moving through the rain - only the beams whose length changed.
        void Write(Rig rig, float playerHeight, bool all)
        {
            LaserRain rain = rig.Rain;
            int n = rig.Lengths.Length;
            Vector3 direction = rain.Direction;
            Color light = Light;
            Color beam = light;
            beam.a = BeamAlpha;
            Color core = Core;
            Color heart = core;
            heart.a = CoreAlpha;
            Color wash = new Color(light.r * WashGain, light.g * WashGain, light.b * WashGain, 1f);
            Color glow = new Color(light.r * GlowGain, light.g * GlowGain, light.b * GlowGain, 1f);
            PhysicsScene physics = Game.PhysicsScene;
            float half = Mathf.Clamp(rig.Pitch * BeamShare, BeamMin, BeamMax);
            float dot = Mathf.Clamp(rig.Pitch * DotShare, DotMin, DotMax);
            float washRadius = rig.Pitch * WashShare;
            bool withWash = Tier != QualityTier.Low;
            int dotQuads = withWash ? 2 : 1;
            // A beam is roofed when it ends a player's height or more above the ground it would have reached.
            float needed = rain.Range - playerHeight;
            float cell = rig.Pitch / Mathf.Sqrt(3f);
            Vector3 x = rig.Right * half, y = rig.Up * half;
            Vector3 cx = x * CoreShare, cy = y * CoreShare;
            Vector3 dx = rig.Right * dot, dy = rig.Up * dot;
            Vector3 sx = dx * SparkShare, sy = dy * SparkShare;
            Vector3 wx = rig.Right * washRadius, wy = rig.Up * washRadius;

            // The beams' and the dots' meshes have the same quads for as long as the emitter and the tier
            // are the same: laid out once, their corners moved from then on.
            bool layout = rig.Beams.QuadCount != n * BeamQuads || rig.Dots.QuadCount != n * dotQuads || rig.DotQuads != dotQuads;
            if (layout)
            {
                // Across a strip the UV runs along the disc mask's diameter: bright in the middle, gone at the edge.
                var across0 = new Vector2(0f, 0.5f);
                var across1 = new Vector2(1f, 0.5f);
                Vector3 o = Vector3.zero;
                rig.Beams.Clear();
                rig.Dots.Clear();
                for (int i = 0; i < n; i++)
                {
                    rig.Beams.Quad(o, o, o, o, beam, beam, beam, beam, across0, across1, across1, across0);
                    rig.Beams.Quad(o, o, o, o, beam, beam, beam, beam, across0, across1, across1, across0);
                    rig.Beams.Quad(o, o, o, o, heart, heart, heart, heart, across0, across1, across1, across0);
                    rig.Beams.Quad(o, o, o, o, heart, heart, heart, heart, across0, across1, across1, across0);
                    rig.Beams.Quad(o, o, o, o, beam);
                    if (withWash) rig.Dots.Quad(o, o, o, o, wash);
                    rig.Dots.Quad(o, o, o, o, core);
                }
                rig.DotQuads = dotQuads;
                Layouts++;
                all = true;
            }

            bool shadeChanged = all, glowChanged = all;
            rig.Height = playerHeight;
            for (int i = 0; i < n; i++)
            {
                float length = rain.BeamLength(i);
                if (!all && length == rig.Lengths[i]) continue;
                rig.Lengths[i] = length;
                Vector3 origin = rain.BeamOrigin(i);
                Vector3 tip = origin + direction * length;
                int q = i * BeamQuads;
                rig.Beams.Move(q, origin - x, origin + x, tip + x, tip - x);
                rig.Beams.Move(q + 1, origin - y, origin + y, tip + y, tip - y);
                rig.Beams.Move(q + 2, origin - cx, origin + cx, tip + cx, tip - cx);
                rig.Beams.Move(q + 3, origin - cy, origin + cy, tip + cy, tip - cy);

                Vector3 end = origin + direction * (length - Lift);
                rig.Beams.Move(q + 4, end - dx - dy, end + dx - dy, end + dx + dy, end - dx + dy);
                int d = i * dotQuads;
                if (withWash) rig.Dots.Move(d++, end - wx - wy, end + wx - wy, end + wx + wy, end - wx + wy);
                rig.Dots.Move(d, end - sx - sy, end + sx - sy, end + sx + sy, end - sx + sy);

                bool roofed = length <= needed;
                shadeChanged |= roofed != rig.Roofed[i];
                rig.Roofed[i] = roofed;

                // The underside of the roof, found from the ground the beam would have reached: looking back
                // up the beam, the first thing in the way (never a held toy; a block that stands on that
                // ground has no underside, and the look starts inside it and meets nothing).
                bool glows = false;
                Vector3 under = default;
                float room = rain.Range - length - Lift;
                if (roofed && room >= 0.1f)
                {
                    RoofLooks++;
                    Vector3 ground = origin + direction * (rain.Range - Lift);
                    if (physics.Raycast(ground, -direction, out RaycastHit hit, room, Layers.SolidMask, QueryTriggerInteraction.Ignore) && hit.distance >= 0.1f)
                    {
                        glows = true;
                        under = hit.point + direction * Lift;
                    }
                }
                glowChanged |= glows != rig.Glows[i] || (glows && under != rig.Under[i]);
                rig.Glows[i] = glows;
                rig.Under[i] = under;
            }
            if (layout)
            {
                rig.Beams.Apply();
                rig.Dots.Apply();
            }
            else
            {
                rig.Beams.ApplyMoved();
                rig.Dots.ApplyMoved();
            }

            if (shadeChanged)
            {
                rig.Shaded = 0;
                rig.Shade.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!rig.Roofed[i]) continue;
                    rig.Shaded++;
                    rig.Shade.Polygon(rain.BeamOrigin(i) + direction * (rain.Range - Lift * 1.5f), rig.Right, rig.Up, cell, 6, 30f, Color.white);
                }
                rig.Shade.Apply();
                rig.ShadeRenderer.enabled = rig.Shaded > 0;
            }
            if (glowChanged)
            {
                rig.Glowing = 0;
                rig.Glow.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!rig.Glows[i]) continue;
                    rig.Glowing++;
                    rig.Glow.Square(rig.Under[i], rig.Right, rig.Up, dot * GlowShare, glow);
                }
                rig.Glow.Apply();
                rig.GlowRenderer.enabled = rig.Glowing > 0;
            }
        }

        protected override void Forget()
        {
            // Whoever is left without this presenter (the plain look) gets the gadget's own beams back.
            for (int i = 0; i < rigs.Count; i++)
                if (rigs[i].Own != null) rigs[i].Own.enabled = true;
            rigs.Clear();
        }
    }
}
