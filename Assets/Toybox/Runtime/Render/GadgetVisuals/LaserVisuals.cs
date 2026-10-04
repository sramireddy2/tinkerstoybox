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
    ///   across their width, in the Hazard signal. They are blended, not added, so a corridor seen
    ///   through fifty rows of them is a curtain of magenta lines and never a white-out (the gadget's own
    ///   plain beams, which add up, are switched off while this presenter draws);
    /// - a dot at the end of every beam, lying on whatever stopped it, and (Medium, High) a faint wash of
    ///   the same light round it - a floor the beams reach is speckled and pink;
    /// - under every beam that something roofs over, a cell of shade on the ground the beam would have
    ///   reached: a deeper tint of the room's own hue, cut to the lattice. The lane behind a card is the
    ///   patch with no dots in it and a hard-edged shadow over it.
    ///
    /// A held toy stops nothing in the simulation, so beams are drawn straight through it and no dot
    /// ever lies on it. Three draws per emitter, rebuilt only when a beam's length changes.
    /// </summary>
    [Presenter(232, ProvidesLook = true)]
    public sealed class LaserVisuals : GadgetVisual
    {
        /// <summary>A beam strip's half width as a share of the lattice pitch, and its limits in units (its soft edge takes half of it).</summary>
        public const float BeamShare = 0.11f, BeamMin = 0.03f, BeamMax = 0.08f;
        public const float BeamAlpha = 0.9f;
        /// <summary>A dot's radius as a share of the lattice pitch, and its limits in units.</summary>
        public const float DotShare = 0.24f, DotMin = 0.05f, DotMax = 0.16f;
        /// <summary>The wash round a dot: this many pitches wide, this share of the beam's light.</summary>
        public const float WashShare = 0.95f, WashGain = 0.1f;
        /// <summary>How strongly the shade tints (the multiply blend's alpha).</summary>
        public const float ShadeAlpha = 0.42f;
        const float Lift = 0.02f;

        sealed class Rig
        {
            public LaserRain Rain;
            public float[] Lengths;
            public FxMesh Beams, Dots, Shade;
            public MeshRenderer BeamRenderer, DotRenderer, ShadeRenderer;
            public Renderer Own;
            public Vector3 Right, Up;
            public float Pitch;
            public int Shaded;
            public float Height;
        }

        readonly List<Rig> rigs = new List<Rig>();

        /// <summary>Emitters drawn right now.</summary>
        public int Count => rigs.Count;
        /// <summary>Dots of the i-th emitter: one per beam.</summary>
        public int DotCount(int index) => rigs[index].Lengths.Length;
        /// <summary>Beams of the i-th emitter that are roofed over (their ground is shaded).</summary>
        public int ShadedCount(int index) => rigs[index].Shaded;
        public Renderer BeamRendererOf(int index) => rigs[index].BeamRenderer;
        public Renderer DotRendererOf(int index) => rigs[index].DotRenderer;
        public Renderer ShadeRendererOf(int index) => rigs[index].ShadeRenderer;
        public Mesh BeamMeshOf(int index) => rigs[index].Beams.Mesh;
        public Mesh DotMeshOf(int index) => rigs[index].Dots.Mesh;
        public Mesh ShadeMeshOf(int index) => rigs[index].Shade.Mesh;

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is LaserRain rain) || rain.BeamCount == 0) return;
            var rig = new Rig
            {
                Rain = rain, Lengths = new float[rain.BeamCount], Beams = new FxMesh("Laser Beams"), Dots = new FxMesh("Laser Dots"), Shade = new FxMesh("Laser Shade"),
            };
            Keep(rig.Beams.Mesh);
            Keep(rig.Dots.Mesh);
            Keep(rig.Shade.Mesh);

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
            for (int i = 0; i < rig.Lengths.Length; i++) rig.Lengths[i] = -1f;

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
                bool changed = !Mathf.Approximately(rig.Height, height);
                for (int i = 0; i < rig.Lengths.Length && !changed; i++) changed = rig.Rain.BeamLength(i) != rig.Lengths[i];
                if (changed) Rebuild(rig, height);
            }
        }

        void Rebuild(Rig rig, float playerHeight)
        {
            LaserRain rain = rig.Rain;
            Vector3 direction = rain.Direction;
            Color light = Palette.Hazard.Emission;
            Color beam = light;
            beam.a = BeamAlpha;
            Color wash = new Color(light.r * WashGain, light.g * WashGain, light.b * WashGain, 1f);
            float half = Mathf.Clamp(rig.Pitch * BeamShare, BeamMin, BeamMax);
            float dot = Mathf.Clamp(rig.Pitch * DotShare, DotMin, DotMax);
            float washRadius = rig.Pitch * WashShare;
            bool withWash = Tier != QualityTier.Low;
            // A beam is roofed when it ends a player's height or more above the ground it would have reached.
            float needed = rain.Range - playerHeight;
            float cell = rig.Pitch / Mathf.Sqrt(3f);
            // Across a strip the UV runs along the disc mask's diameter: bright in the middle, gone at the edge.
            var across0 = new Vector2(0f, 0.5f);
            var across1 = new Vector2(1f, 0.5f);
            Vector3 x = rig.Right * half, y = rig.Up * half;

            rig.Height = playerHeight;
            rig.Shaded = 0;
            rig.Beams.Clear();
            rig.Dots.Clear();
            rig.Shade.Clear();
            for (int i = 0; i < rig.Lengths.Length; i++)
            {
                float length = rain.BeamLength(i);
                rig.Lengths[i] = length;
                Vector3 origin = rain.BeamOrigin(i);
                Vector3 tip = origin + direction * length;
                rig.Beams.Quad(origin - x, origin + x, tip + x, tip - x, beam, beam, beam, beam, across0, across1, across1, across0);
                rig.Beams.Quad(origin - y, origin + y, tip + y, tip - y, beam, beam, beam, beam, across0, across1, across1, across0);

                Vector3 end = origin + direction * (length - Lift);
                if (withWash) rig.Dots.Square(end, rig.Right, rig.Up, washRadius, wash);
                rig.Dots.Square(end, rig.Right, rig.Up, dot, light);
                if (length > needed) continue;
                rig.Shaded++;
                rig.Shade.Polygon(origin + direction * (rain.Range - Lift * 1.5f), rig.Right, rig.Up, cell, 6, 30f, Color.white);
            }
            rig.Beams.Apply();
            rig.Dots.Apply();
            rig.Shade.Apply();
            rig.ShadeRenderer.enabled = rig.Shaded > 0;
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
