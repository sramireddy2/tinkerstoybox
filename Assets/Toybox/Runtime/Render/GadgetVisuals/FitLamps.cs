using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>What a fit lamp says.</summary>
    public enum FitTint
    {
        /// <summary>Amber, pulsing slowly: nothing fits yet.</summary>
        Waiting,
        /// <summary>Green, steady: it fits (a gauge), it is seated (a socket).</summary>
        Fits,
        /// <summary>Red, blinking fast: the wrong size.</summary>
        Wrong,
        /// <summary>Amber, blinking fast: the right size, pointing the wrong way.</summary>
        Turned,
    }

    /// <summary>
    /// The lamps of <see cref="FitGauge"/> and <see cref="Socket"/> (LEVELS 2.1): the sanctioned
    /// world-space cue for a toy's true size. The lamp's own emission says amber (waiting), green (fits)
    /// or red (wrong size); state never rides on hue alone - waiting pulses once a second, fitting is
    /// steady, wrong blinks three times a second.
    ///
    /// A gauge's lamp also wears a read-out while a toy is being judged: a pale band - the sizes that
    /// fit - and a ring for the toy in the hand, inside the band's hole when it is too small, outside
    /// when too big, on the band and green when it fits. The ring moves as the player steps back and
    /// forth, which is the whole lesson of Levels 2 and 8.
    ///
    /// A socket's lamp turns red while a toy it refused still lies in it.
    /// The lamps are world objects (not on the held toy), turned to face the eye.
    /// </summary>
    [Presenter(236, ProvidesLook = true)]
    public sealed class FitLamps : GadgetVisual
    {
        /// <summary>The read-out's reference radius in lamp radii; sizes map onto 2^(log2(size / middle) x Spread) of it.</summary>
        public const float RingRadius = 3.2f, Spread = 0.45f, Range = 2f;
        public const float BandAlpha = 0.5f, MinBand = 0.14f;
        /// <summary>A refused toy keeps a socket's lamp red at least this long.</summary>
        public const float RejectSeconds = 0.8f;

        sealed class GaugeLamp
        {
            public FitGauge Gauge;
            public Renderer Lamp;
            public float Min, Max, Middle, Radius;
            public Transform Rings, Ring;
            public MeshRenderer BandRenderer, RingRenderer;
            public MaterialPropertyBlock Block;
            public float Shown;
            public FitTint Tint;
            public float RingAt;
        }

        sealed class SocketLamp
        {
            public Socket Socket;
            public Renderer Lamp;
            public Prop Refused;
            public float RefusedFor;
            public FitTint Tint;
        }

        readonly List<GaugeLamp> gauges = new List<GaugeLamp>();
        readonly List<SocketLamp> sockets = new List<SocketLamp>();

        public int GaugeCount => gauges.Count;
        public int SocketCount => sockets.Count;

        /// <summary>What the lamp of a gauge or socket shows right now; Waiting for one without a lamp.</summary>
        public FitTint TintOf(Gadget gadget)
        {
            for (int i = 0; i < gauges.Count; i++)
                if (gauges[i].Gauge == gadget) return gauges[i].Tint;
            for (int i = 0; i < sockets.Count; i++)
                if (sockets[i].Socket == gadget) return sockets[i].Tint;
            return FitTint.Waiting;
        }

        /// <summary>How visible a gauge's read-out is, 0..1.</summary>
        public float ReadoutOf(FitGauge gauge)
        {
            GaugeLamp lamp = Find(gauge);
            return lamp != null ? lamp.Shown : 0f;
        }

        /// <summary>The toy ring's radius over the band's middle radius: under 1 too small, over 1 too big.</summary>
        public float RingOf(FitGauge gauge)
        {
            GaugeLamp lamp = Find(gauge);
            return lamp != null ? lamp.RingAt : 1f;
        }

        /// <summary>The band of a gauge as radii over the reference radius: (inner, outer).</summary>
        public Vector2 BandOf(FitGauge gauge)
        {
            GaugeLamp lamp = Find(gauge);
            return lamp != null ? Band(lamp) : Vector2.one;
        }

        public Renderer RingRendererOf(FitGauge gauge) => Find(gauge)?.RingRenderer;

        /// <summary>The signal a tint is shown with.</summary>
        public static Signal SignalOf(FitTint tint)
        {
            switch (tint)
            {
                case FitTint.Fits: return Palette.Go;
                case FitTint.Wrong: return GadgetFx.Wrong;
                case FitTint.Turned: return GadgetFx.Turned;
                default: return Palette.Amber;
            }
        }

        /// <summary>The window of scales a gauge calls Good, found by asking it (its options are its own). 0 and infinity for open ends.</summary>
        public static void Window(FitGauge gauge, out float min, out float max)
        {
            const float low = 1e-3f, high = 1e3f;
            min = 0f;
            max = float.PositiveInfinity;
            if (gauge.Fit(low) == FitState.TooSmall) min = Boundary(gauge, low, high, FitState.TooSmall, true);
            if (gauge.Fit(high) == FitState.TooBig) max = Boundary(gauge, Mathf.Max(low, min), high, FitState.TooBig, false);
        }

        // The scale at which the answer stops (or starts) being "state", by bisection on a log scale.
        static float Boundary(FitGauge gauge, float from, float to, FitState state, bool stateBelow)
        {
            float a = Mathf.Log(from), b = Mathf.Log(to);
            for (int i = 0; i < 40; i++)
            {
                float middle = (a + b) * 0.5f;
                bool isState = gauge.Fit(Mathf.Exp(middle)) == state;
                if (isState == stateBelow) a = middle;
                else b = middle;
            }
            return Mathf.Exp(stateBelow ? b : a);
        }

        protected override void Subscribe(GameEvents events) => events.SocketRejected += OnSocketRejected;
        protected override void Unsubscribe(GameEvents events) => events.SocketRejected -= OnSocketRejected;

        protected override void Adopt(Gadget gadget)
        {
            if (gadget is Socket socket)
            {
                if (socket.Lamp == null || socket.Lamp.Renderer == null) return;
                sockets.Add(new SocketLamp { Socket = socket, Lamp = socket.Lamp.Renderer });
                return;
            }
            if (!(gadget is FitGauge gauge) || gauge.Lamp == null || gauge.Lamp.Renderer == null) return;

            var lamp = new GaugeLamp { Gauge = gauge, Lamp = gauge.Lamp.Renderer, Block = new MaterialPropertyBlock() };
            Window(gauge, out lamp.Min, out lamp.Max);
            bool closedBelow = lamp.Min > 0f, closedAbove = !float.IsInfinity(lamp.Max);
            lamp.Middle = closedBelow && closedAbove ? Mathf.Sqrt(lamp.Min * lamp.Max) : closedBelow ? lamp.Min * 1.5f : closedAbove ? lamp.Max / 1.5f : 1f;
            lamp.Radius = Mathf.Max(0.05f, lamp.Lamp.bounds.extents.x) * RingRadius;

            var rings = new GameObject("Fit Readout " + gauge.Name) { hideFlags = HideFlags.DontSave };
            rings.transform.SetParent(Root, false);
            rings.transform.position = lamp.Lamp.transform.position;
            Keep(rings);
            lamp.Rings = rings.transform;
            Vector2 band = Band(lamp);
            lamp.BandRenderer = Visual("Band", GadgetFx.Annulus(band.x, band.y, 48), GadgetFx.Flat("Fit Band", FlatBlend.Alpha, FlatShape.Quad, 0f, 1), rings.transform);
            lamp.BandRenderer.transform.localScale = Vector3.one * lamp.Radius;
            lamp.RingRenderer = Visual("Ring", GadgetFx.Annulus(0.93f, 1.07f, 48), GadgetFx.Flat("Fit Ring", FlatBlend.Alpha, FlatShape.Quad, 0f, 2), rings.transform);
            lamp.Ring = lamp.RingRenderer.transform;
            rings.SetActive(false);
            gauges.Add(lamp);
        }

        // Where a scale sits on the read-out, as a multiple of the reference radius.
        static float At(GaugeLamp lamp, float scale)
        {
            if (scale <= 0f) return Mathf.Pow(2f, -Range * Spread);
            if (float.IsInfinity(scale)) return Mathf.Pow(2f, Range * Spread);
            return Mathf.Pow(2f, Mathf.Clamp(Mathf.Log(scale / lamp.Middle, 2f), -Range, Range) * Spread);
        }

        static Vector2 Band(GaugeLamp lamp)
        {
            float inner = At(lamp, lamp.Min), outer = At(lamp, lamp.Max);
            // A window too narrow to see still gets a band one can find.
            if (outer - inner < MinBand)
            {
                float middle = (inner + outer) * 0.5f;
                inner = middle - MinBand * 0.5f;
                outer = middle + MinBand * 0.5f;
            }
            return new Vector2(inner, outer);
        }

        protected override void Draw(float dt, float alpha)
        {
            float time = Now;
            Transform camera = Context.Camera != null ? Context.Camera.transform : null;

            for (int i = 0; i < gauges.Count; i++)
            {
                GaugeLamp lamp = gauges[i];
                if (lamp.Lamp == null) continue;
                FitState state = lamp.Gauge.Disposed ? FitState.Idle : lamp.Gauge.State;
                lamp.Tint = state == FitState.Good ? FitTint.Fits
                    : state == FitState.TooSmall || state == FitState.TooBig ? FitTint.Wrong
                    : state == FitState.Backwards ? FitTint.Turned
                    : FitTint.Waiting;
                Signal signal = SignalOf(lamp.Tint);
                Color emission = signal.EmissionAt(signal.GainAt(time));
                Materials.SetEmission(lamp.Lamp, emission);

                bool judging = state != FitState.Idle;
                lamp.Shown = Mathf.MoveTowards(lamp.Shown, judging ? 1f : 0f, dt / 0.15f);
                bool visible = lamp.Shown > 0.001f;
                if (lamp.Rings.gameObject.activeSelf != visible) lamp.Rings.gameObject.SetActive(visible);
                if (!visible) continue;

                if (judging) lamp.RingAt = At(lamp, lamp.Gauge.Scale);
                float shown = GadgetFx.Smooth(lamp.Shown);
                lamp.Rings.position = lamp.Lamp.transform.position;
                if (camera != null)
                {
                    Vector3 toEye = camera.position - lamp.Rings.position;
                    if (toEye.sqrMagnitude > 1e-6f) lamp.Rings.rotation = Quaternion.LookRotation(-toEye.normalized, camera.up);
                }
                lamp.Rings.localScale = Vector3.one * shown;
                lamp.Ring.localScale = Vector3.one * (lamp.Radius * lamp.RingAt);

                Color paper = GadgetFx.Lin(Palette.Paper, 1f, BandAlpha * shown);
                lamp.Block.SetVector(GadgetFx.ColorId, paper);
                lamp.BandRenderer.SetPropertyBlock(lamp.Block);
                // The ring carries the lamp's own light, so the two never disagree.
                emission.a = shown;
                lamp.Block.SetVector(GadgetFx.ColorId, emission);
                lamp.RingRenderer.SetPropertyBlock(lamp.Block);
            }

            for (int i = 0; i < sockets.Count; i++)
            {
                SocketLamp lamp = sockets[i];
                if (lamp.Lamp == null) continue;
                Socket socket = lamp.Socket;
                if (lamp.Refused != null)
                {
                    lamp.RefusedFor += dt;
                    Prop prop = lamp.Refused;
                    bool lies = !socket.Disposed && !prop.Removed && !prop.Held && !socket.Seated && !socket.Seating && socket.Capture.Contains(prop.Center);
                    if (socket.Seated || socket.Seating || (!lies && lamp.RefusedFor >= RejectSeconds)) lamp.Refused = null;
                }
                lamp.Tint = socket.Seated ? FitTint.Fits : lamp.Refused != null ? FitTint.Wrong : FitTint.Waiting;
                Signal signal = SignalOf(lamp.Tint);
                Materials.SetEmission(lamp.Lamp, signal.EmissionAt(signal.GainAt(time)));
            }
        }

        void OnSocketRejected(GadgetEvent e)
        {
            for (int i = 0; i < sockets.Count; i++)
            {
                if (sockets[i].Socket != e.Gadget) continue;
                sockets[i].Refused = e.Prop;
                sockets[i].RefusedFor = 0f;
            }
        }

        GaugeLamp Find(FitGauge gauge)
        {
            for (int i = 0; i < gauges.Count; i++)
                if (gauges[i].Gauge == gauge) return gauges[i];
            return null;
        }

        protected override void Forget()
        {
            gauges.Clear();
            sockets.Clear();
        }
    }
}
