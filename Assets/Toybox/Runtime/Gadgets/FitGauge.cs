using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class FitGaugeOptions
    {
        public string Name = "Gauge";
        /// <summary>The socket whose window is shown. Null: <see cref="MinScale"/>..<see cref="MaxScale"/> (a pressure plate's window).</summary>
        public Socket Target;
        public float MinScale, MaxScale = float.MaxValue;
        /// <summary>The held toy is judged while its centre is inside.</summary>
        public Zone Near;
        /// <summary>Only toys with this tag are judged (null: any).</summary>
        public string Tag;
        /// <summary>Where to put a lamp (green: good, amber: too small or too big, dark: idle), or null.</summary>
        public Vector3? Lamp;
        public float LampRadius = 0.15f;
    }

    /// <summary>
    /// The sanctioned world-space cue for a held toy's true size (LEVELS 2.1): while the player holds a
    /// tagged toy near its target, the gauge says whether the toy, as big as it is right now, would fit.
    /// It only reads. Lamps and outline tints are driven by <see cref="State"/> and <see cref="Changed"/>.
    /// </summary>
    public sealed class FitGauge : Gadget
    {
        readonly FitGaugeOptions options;
        readonly SignalLamp lamp;

        public FitGauge(LevelContext ctx, FitGaugeOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Lamp.HasValue)
            {
                var holder = new GameObject("Gauge " + Name);
                holder.transform.SetParent(ctx.Root, false);
                holder.transform.position = options.Lamp.Value;
                Mesh mesh = MeshKit.Cached(MeshKit.Key("GadgetLamp", options.LampRadius), () => MeshKit.Sphere(options.LampRadius, 12, 8));
                lamp = GadgetKit.Lamp(holder.transform, mesh, Palette.Amber, Vector3.zero);
                lamp.Off();
            }
        }

        public FitState State { get; private set; } = FitState.Idle;
        /// <summary>The scale of the toy being judged (0 while idle).</summary>
        public float Scale { get; private set; }

        /// <summary>Inside the tick, when the state changes.</summary>
        public event Action<FitState> Changed;

        /// <summary>How a toy of this scale would fit the target. Pure.</summary>
        public FitState Fit(float scale)
        {
            if (options.Target != null) return options.Target.Fit(scale);
            if (scale < options.MinScale) return FitState.TooSmall;
            if (scale > options.MaxScale) return FitState.TooBig;
            return FitState.Good;
        }

        protected override void Tick(float dt)
        {
            Prop held = Game.Grabber.Held;
            FitState next = FitState.Idle;
            Scale = 0f;
            if (held != null && (options.Tag == null || held.HasTag(options.Tag)) && options.Near.Contains(held.Center))
            {
                Scale = held.Scale;
                next = options.Target != null ? options.Target.Fit(held) : Fit(held.Scale);
            }
            if (next != State)
            {
                State = next;
                Changed?.Invoke(next);
                Game.Events.RaiseGaugeChanged(Event(held != null ? held.Center : options.Near.Center, held, Scale, 0f, (int)next));
            }
            if (lamp == null) return;
            if (State == FitState.Idle) lamp.Off();
            else lamp.Set(State == FitState.Good ? Palette.Go : Palette.Amber);
            lamp.Pulse(Seconds);
        }

        public override void Reset()
        {
            base.Reset();
            State = FitState.Idle;
            Scale = 0f;
        }
    }
}
