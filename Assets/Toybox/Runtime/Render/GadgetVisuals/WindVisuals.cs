using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// Air made visible (LEVELS 2.2, Levels 5 and 15). For every <see cref="WindStream"/>:
    ///
    /// - the stream's volume: its four long sides as a faint tint that thins out downstream, so the box
    ///   the wind acts in can be seen from outside and from within;
    /// - streamers: ribbons that ride the stream from one end to the other, die-cut like everything else
    ///   (a Paper core on a deeper edge, so they read on a pale wall and in the night room alike). Their
    ///   speed and their number follow the stream's strength; a calm stream shows nothing;
    /// - the blades of any desk fan that stands at the mouth of a stream, turning as hard as it blows.
    ///
    /// Two draws per stream. Nothing is drawn on a held toy: a fan in the hand winds down.
    /// </summary>
    [Presenter(234, ProvidesLook = true)]
    public sealed class WindVisuals : GadgetVisual
    {
        const int Segments = 5;
        /// <summary>How opaque the volume's sides are at the mouth and at the far end, at full strength; and its edges.</summary>
        public const float VolumeAlpha = 0.3f, VolumeFarAlpha = 0.06f, EdgeAlpha = 0.6f;
        /// <summary>Degrees per second: a fan in a stream of strength 1, and the most any fan turns.</summary>
        public const float FanSpeed = 900f, FanSpeedMax = 1440f;

        sealed class Stream
        {
            public WindStream Wind;
            public FxMesh Ribbons;
            public MeshRenderer RibbonRenderer, VolumeRenderer;
            public MaterialPropertyBlock Block;
            public Vector3 Centre, Axis, A, B;
            public float Length, HalfA, HalfB, Width;
            public int Count;
            /// <summary>0 calm .. 1 blowing, eased.</summary>
            public float Shown;
            public float Travel;
        }

        sealed class Fan
        {
            public Transform Blades;
            public Quaternion Rest;
            public Prop Prop;
            public float Speed, Angle;
        }

        readonly List<Stream> streams = new List<Stream>();
        readonly List<Fan> fans = new List<Fan>();
        Color core, edge, tint;

        public int Count => streams.Count;
        public int FanCount => fans.Count;
        /// <summary>Ribbons of the i-th stream.</summary>
        public int StreamerCount(int index) => streams[index].Count;
        /// <summary>How visible the i-th stream is, 0 (calm) .. 1.</summary>
        public float ShownOf(int index) => streams[index].Shown;
        public Renderer RibbonRendererOf(int index) => streams[index].RibbonRenderer;
        public Renderer VolumeRendererOf(int index) => streams[index].VolumeRenderer;
        /// <summary>Degrees per second the i-th fan's blades turn at.</summary>
        public float FanSpeedOf(int index) => fans[index].Speed;
        public Transform FanBladesOf(int index) => fans[index].Blades;

        protected override void Begin()
        {
            Dip dip = Materials.Dip;
            core = GadgetFx.Lin(Palette.Paper, 1.1f);
            // A pale room wants a darker edge; the night room is dark already and takes its own light tone.
            edge = dip.Night ? GadgetFx.Lin(dip.Light) : GadgetFx.Lin(Palette.Mix(dip.Deep, Palette.Ink, 0.35f));
            tint = dip.Night ? GadgetFx.Lin(dip.Light, 1.3f) : GadgetFx.Lin(Palette.Mix(dip.Deep, Palette.Ink, 0.15f));

            // Fans: every "Blades" child the toy factory made, wherever a level put its fan.
            if (Game.LevelRoot == null) return;
            foreach (Transform t in Game.LevelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != ToyFactory.DeskFanBlades || t.parent == null) continue;
                Collider collider = t.parent.GetComponentInChildren<Collider>(true);
                fans.Add(new Fan { Blades = t, Rest = t.localRotation, Prop = collider != null ? PropRef.Of(collider) : null });
            }
        }

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is WindStream wind)) return;
            Zone box = wind.Box;
            Quaternion rotation = box.Rotation;
            Vector3 size = box.Size;
            var stream = new Stream { Wind = wind, Centre = box.Center, Ribbons = new FxMesh("Wind Streamers"), Block = new MaterialPropertyBlock() };
            Keep(stream.Ribbons.Mesh);

            // The box axis the wind blows along is the stream's length; the other two span its cross-section.
            Vector3 direction = wind.Direction;
            int along = 0;
            float best = -1f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = Vector3.zero;
                axis[i] = 1f;
                float dot = Mathf.Abs(Vector3.Dot(rotation * axis, direction));
                if (dot > best)
                {
                    best = dot;
                    along = i;
                }
            }
            int a = (along + 1) % 3, b = (along + 2) % 3;
            Vector3 unit = Vector3.zero;
            unit[along] = 1f;
            Vector3 lengthAxis = rotation * unit;
            if (Vector3.Dot(lengthAxis, direction) < 0f) lengthAxis = -lengthAxis;
            unit = Vector3.zero;
            unit[a] = 1f;
            stream.A = rotation * unit;
            unit = Vector3.zero;
            unit[b] = 1f;
            stream.B = rotation * unit;
            stream.Axis = lengthAxis;
            stream.Length = size[along];
            stream.HalfA = size[a] * 0.5f;
            stream.HalfB = size[b] * 0.5f;
            float across = Mathf.Min(size[a], size[b]);
            stream.Width = Mathf.Clamp(across * 0.022f, 0.035f, 0.3f);
            float area = size[a] * size[b];
            int baseCount = GadgetFx.Pick(Tier, 12, 22, 34);
            stream.Count = Mathf.Clamp(Mathf.RoundToInt(baseCount * Mathf.Sqrt(area / 16f)), 6, baseCount * 2);
            stream.Shown = wind.Strength > 0f ? 1f : 0f;

            stream.VolumeRenderer = Visual("Wind Volume " + wind.Name, Keep(VolumeMesh(stream)), GadgetFx.Flat("Wind Volume", FlatBlend.Alpha, FlatShape.Quad, 0f, -1));
            stream.RibbonRenderer = Visual("Wind Streamers " + wind.Name, stream.Ribbons.Mesh, GadgetFx.Flat("Wind Streamer", FlatBlend.Alpha, FlatShape.SoftDisc, 0.55f, 1));
            streams.Add(stream);
        }

        // The four long sides of the stream, in world space: tinted at the mouth, clear at the far end.
        Mesh VolumeMesh(Stream s)
        {
            var fx = new FxMesh("Wind Volume");
            Vector3 mouth = s.Centre - s.Axis * (s.Length * 0.5f), far = s.Centre + s.Axis * (s.Length * 0.5f);
            Color near = tint, gone = tint;
            near.a = VolumeAlpha;
            gone.a = VolumeFarAlpha;
            // Its four long edges, in Paper: the outline of the tunnel the air runs in.
            Color edgeNear = core, edgeFar = core;
            edgeNear.a = EdgeAlpha;
            edgeFar.a = EdgeAlpha * 0.25f;
            float line = Mathf.Clamp(Mathf.Min(s.HalfA, s.HalfB) * 0.03f, 0.025f, 0.12f);
            for (int side = 0; side < 4; side++)
            {
                Vector3 normal = side == 0 ? s.A * s.HalfA : side == 1 ? s.B * s.HalfB : side == 2 ? -s.A * s.HalfA : -s.B * s.HalfB;
                Vector3 span = side % 2 == 0 ? s.B * s.HalfB : s.A * s.HalfA;
                fx.Quad(mouth + normal - span, mouth + normal + span, far + normal + span, far + normal - span, near, near, gone, gone);
                Vector3 inset = span.normalized * line;
                fx.Quad(mouth + normal - span, mouth + normal - span + inset, far + normal - span + inset, far + normal - span, edgeNear, edgeNear, edgeFar, edgeFar);
                fx.Quad(mouth + normal + span - inset, mouth + normal + span, far + normal + span, far + normal + span - inset, edgeNear, edgeNear, edgeFar, edgeFar);
            }
            // And the mouth itself: a frame where the stream begins.
            for (int side = 0; side < 4; side++)
            {
                Vector3 normal = side == 0 ? s.A * s.HalfA : side == 1 ? s.B * s.HalfB : side == 2 ? -s.A * s.HalfA : -s.B * s.HalfB;
                Vector3 span = side % 2 == 0 ? s.B * s.HalfB : s.A * s.HalfA;
                Vector3 inward = -normal.normalized * line;
                fx.Quad(mouth + normal - span, mouth + normal + span, mouth + normal + span + inward, mouth + normal - span + inward, edgeNear);
            }
            fx.Apply();
            return fx.Mesh;
        }

        protected override void Draw(float dt, float alpha)
        {
            Transform camera = Context.Camera != null ? Context.Camera.transform : null;
            Vector3 eye = camera != null ? camera.position : Vector3.zero;
            for (int i = 0; i < streams.Count; i++) DrawStream(streams[i], dt, eye);
            for (int i = 0; i < fans.Count; i++) TurnFan(fans[i], dt);
        }

        void DrawStream(Stream s, float dt, Vector3 eye)
        {
            float strength = s.Wind.Disposed || !s.Wind.Enabled ? 0f : s.Wind.Strength;
            s.Shown = Mathf.MoveTowards(s.Shown, strength > 0f ? 1f : 0f, dt / 0.4f);
            bool visible = s.Shown > 0.001f;
            s.RibbonRenderer.enabled = visible;
            s.VolumeRenderer.enabled = visible;
            if (!visible) return;

            float gain = s.Shown * Mathf.Clamp(0.45f + 0.55f * strength, 0.45f, 1.3f);
            s.Block.SetVector(GadgetFx.ColorId, new Vector4(1f, 1f, 1f, Mathf.Min(1.5f, gain)));
            s.VolumeRenderer.SetPropertyBlock(s.Block);

            // Faster with the strength, but always fast enough to read as moving air.
            float speed = Mathf.Clamp(3.5f + 4.5f * Mathf.Sqrt(Mathf.Max(0f, strength)), 3.5f, 16f);
            s.Travel += speed * dt;
            float time = Now;

            s.Ribbons.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                // The wide deep edges first, the Paper cores on top of them.
                bool isCore = pass == 1;
                Color tone = isCore ? core : edge;
                float halfWidth = s.Width * (isCore ? 0.5f : 1.3f);
                float opacity = (isCore ? 0.95f : 0.5f) * Mathf.Min(1f, gain);
                for (int i = 0; i < s.Count; i++)
                {
                    float phase = GadgetFx.Hash(i, 1);
                    float u = (GadgetFx.Hash(i, 2) * 2f - 1f) * s.HalfA * 0.84f;
                    float v = (GadgetFx.Hash(i, 3) * 2f - 1f) * s.HalfB * 0.84f;
                    float length = s.Length * Mathf.Lerp(0.16f, 0.32f, GadgetFx.Hash(i, 4));
                    float run = s.Length + length;
                    float head = Mathf.Repeat(phase * run + s.Travel * Mathf.Lerp(0.8f, 1.25f, GadgetFx.Hash(i, 5)), run);
                    float sway = Mathf.Min(s.HalfA, s.HalfB) * 0.07f;

                    Vector3 previous = Vector3.zero, previousSide = Vector3.zero;
                    Color previousColor = default;
                    for (int k = 0; k <= Segments; k++)
                    {
                        float t = (float)k / Segments;
                        float along = head - length * t;
                        float wave = along * 1.9f + time * 2.6f + phase * 6.283f;
                        Vector3 p = s.Centre + s.Axis * (Mathf.Clamp(along, 0f, s.Length) - s.Length * 0.5f)
                            + s.A * (u + Mathf.Sin(wave) * sway) + s.B * (v + Mathf.Cos(wave * 0.83f) * sway);
                        // Thin at both tips; gone where the ribbon is outside the stream.
                        float taper = Mathf.Sin(Mathf.PI * t);
                        float inside = Mathf.Clamp01(along / (s.Length * 0.1f)) * Mathf.Clamp01((s.Length - along) / (s.Length * 0.1f));
                        Vector3 toEye = eye - p;
                        Vector3 side = Vector3.Cross(s.Axis, toEye);
                        side = side.sqrMagnitude > 1e-8f ? side.normalized * (halfWidth * (0.35f + 0.65f * taper)) : s.A * halfWidth;
                        Color color = tone;
                        color.a = opacity * taper * inside;
                        if (k > 0)
                        {
                            s.Ribbons.Quad(previous - previousSide, previous + previousSide, p + side, p - side, previousColor, previousColor, color, color,
                                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
                        }
                        previous = p;
                        previousSide = side;
                        previousColor = color;
                    }
                }
            }
            s.Ribbons.Apply();
        }

        void TurnFan(Fan fan, float dt)
        {
            if (fan.Blades == null) return;
            float target = 0f;
            bool inHand = fan.Prop != null && (fan.Prop.Held || fan.Prop.Removed);
            if (!inHand)
            {
                Vector3 hub = fan.Blades.position;
                Vector3 forward = fan.Blades.parent.forward;
                float reach = 1.2f * Mathf.Abs(fan.Blades.lossyScale.x) + 0.25f;
                for (int i = 0; i < streams.Count; i++)
                {
                    WindStream wind = streams[i].Wind;
                    if (wind.Disposed || !wind.Enabled || wind.Strength <= 0f) continue;
                    if (Vector3.Dot(wind.Direction, forward) < 0.5f) continue;
                    if ((wind.Box.ClosestPoint(hub) - hub).sqrMagnitude > reach * reach) continue;
                    target = Mathf.Max(target, Mathf.Min(FanSpeedMax, FanSpeed * Mathf.Sqrt(wind.Strength)));
                }
            }
            if (target <= 0f && fan.Speed <= 0f) return;
            // Spins up briskly and coasts down.
            fan.Speed = Mathf.MoveTowards(fan.Speed, target, (target > fan.Speed ? 1800f : 500f) * dt);
            fan.Angle = Mathf.Repeat(fan.Angle + fan.Speed * dt, 360f);
            fan.Blades.localRotation = fan.Rest * Quaternion.Euler(0f, 0f, fan.Angle);
        }

        protected override void Forget()
        {
            streams.Clear();
            fans.Clear();
        }
    }
}
