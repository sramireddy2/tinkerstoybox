using System;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum SailState
    {
        /// <summary>An ordinary prop.</summary>
        Loose,
        /// <summary>Lying flat in the stream, big enough to be caught: pinned where it lies, still grabbable.</summary>
        Moored,
        /// <summary>A rider is aboard but the sail is too small: it lifts a little and flops back.</summary>
        Stall,
        /// <summary>Carrying its rider across.</summary>
        Glide,
        /// <summary>Arrived and handed back to physics.</summary>
        Docked,
    }

    public sealed class SailRaftOptions
    {
        public string Name = "Sail";
        public Prop Prop;
        /// <summary>The stream that carries it. The glide runs along its direction.</summary>
        public WindStream Stream;
        /// <summary>The sail moors while its centre is inside (the part of the deck that is in the stream).</summary>
        public Zone Launch;
        /// <summary>Plan area of the sail at scale 1.</summary>
        public float Area = 0.30f;
        /// <summary>Lift per unit of area.</summary>
        public float LiftPressure = 7.78f;
        /// <summary>Below this scale the wind blows it away instead of mooring it.</summary>
        public float MoorAbove = 2.5f;
        /// <summary>What it has to carry: the player.</summary>
        public float RiderMass = Player.Mass;
        /// <summary>Seconds the rider has to stand on it before it goes.</summary>
        public float BoardDelay = 0.6f;
        public float CruiseSpeed = 6f;
        /// <summary>How far it accelerates and brakes, in units per second squared.</summary>
        public float Acceleration = 6f;
        /// <summary>How high above where it lay it flies.</summary>
        public float CruiseHeight = 1.2f;
        /// <summary>Where it comes down: x and z of its centre, and the height of the surface it settles on.</summary>
        public Vector3 Landing;
        /// <summary>Over this many units of travel it eases onto the line through <see cref="Landing"/> and turns into the wind.</summary>
        public float AlignDistance = 8f;
        public float RiseSeconds = 1f, DescendSeconds = 0.8f;
        /// <summary>Seconds without a rider before a gliding sail is let go (it falls and is respawned by the kill plane).</summary>
        public float AbandonSeconds = 1f;
    }

    /// <summary>
    /// Size is area (LEVELS 2.2, Level 5): a feather laid in the gale carries the player if it is wide
    /// enough. The rule is a real formula of scale - capacity(s) = LiftPressure x Area x s^2 / g - mass(s),
    /// it flies when that reaches the rider's mass - and the ride itself is a scripted mover whose pose is
    /// a function of the ticks since launch.
    /// </summary>
    public sealed class SailRaft : Gadget
    {
        const float MoorSpeed = 0.2f;
        const float MoorSeconds = 0.3f;
        const float FlatDot = 0.9397f; // 20 degrees
        const float StallRise = 0.3f;
        const float StallSeconds = 1f;

        readonly SailRaftOptions options;
        readonly Prop prop;
        readonly int moorTicks, boardTicks, abandonTicks;
        readonly Vector3 direction;

        Mover mover;
        bool wasGrabbable;
        int still, aboard, riderless, tick;
        bool stalled;

        // The pose it was moored at, and the glide computed from it.
        Vector3 startPosition;
        Quaternion startRotation, endRotation;
        float distance, lateral, heightDrop;
        float accelTime, cruiseTime, travelTime;
        Vector3 side;

        public SailRaft(LevelContext ctx, SailRaftOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            prop = options.Prop ?? throw new ArgumentException("A sail raft needs its prop.", nameof(options));
            Vector3 wind = options.Stream != null ? options.Stream.Direction : Vector3.forward;
            wind.y = 0f;
            direction = wind.sqrMagnitude > 1e-6f ? wind.normalized : Vector3.forward;
            side = Vector3.Cross(Vector3.up, direction);
            moorTicks = Ticks(MoorSeconds, 1);
            boardTicks = Ticks(options.BoardDelay, 1);
            abandonTicks = Ticks(options.AbandonSeconds, 1);
        }

        public SailState State { get; private set; } = SailState.Loose;
        public Prop Prop => prop;
        /// <summary>True while the player stands on the sail.</summary>
        public bool RiderAboard => !prop.Removed && Game.Player.Grounded && Game.Player.GroundProp == prop;

        /// <summary>Inside the tick.</summary>
        public event Action SailMoored;
        /// <summary>Capacity / rider mass of the sail that could not lift.</summary>
        public event Action<float> SailStalled;
        public event Action SailLaunched;
        public event Action SailDocked;

        /// <summary>What a sail of this scale can carry besides itself: lift minus its own mass.</summary>
        public float Capacity(float scale)
        {
            float mass = Mathf.Max(Prop.MinMass, prop.Density * prop.BaseVolume * scale * scale * scale);
            return options.LiftPressure * options.Area * scale * scale / Game.Gravity - mass;
        }

        /// <summary>Does a sail of this scale carry the rider?</summary>
        public bool Carries(float scale) => Capacity(scale) >= options.RiderMass;

        protected override void Tick(float dt)
        {
            if (prop.Removed) return;
            switch (State)
            {
                case SailState.Loose:
                case SailState.Docked:
                    TickLoose();
                    break;
                case SailState.Moored:
                    TickMoored();
                    break;
                case SailState.Stall:
                    TickStall();
                    break;
                case SailState.Glide:
                    TickGlide();
                    break;
            }
        }

        void TickLoose()
        {
            if (State == SailState.Docked)
            {
                // It stays docked until somebody moves it; then it is an ordinary prop again.
                if (prop.Held) State = SailState.Loose;
                return;
            }
            bool candidate = GadgetKit.IsLoose(prop) && !prop.Driven && !prop.Body.isKinematic
                             && prop.Scale >= options.MoorAbove
                             && options.Launch.Contains(prop.Center)
                             && Mathf.Abs((prop.Rotation * Vector3.up).y) >= FlatDot
                             && prop.Velocity.sqrMagnitude < MoorSpeed * MoorSpeed;
            still = candidate ? still + 1 : 0;
            if (still < moorTicks) return;
            still = 0;
            mover = prop.BeginDrive();
            if (mover == null) return;
            State = SailState.Moored;
            aboard = 0;
            stalled = false;
            SailMoored?.Invoke();
            Game.Events.RaiseSailMoored(Event(prop.Center, prop, prop.Scale));
        }

        bool StillOurs()
        {
            if (prop.Driven) return true;
            // Grabbed (a grab ends the drive) or respawned: an ordinary prop again.
            prop.Grabbable = State == SailState.Glide ? wasGrabbable : prop.Grabbable;
            State = SailState.Loose;
            still = 0;
            return false;
        }

        void TickMoored()
        {
            if (!StillOurs()) return;
            bool rider = RiderAboard;
            if (!rider)
            {
                aboard = 0;
                stalled = false;
                return;
            }
            // After a stall the rider has to step off and on again.
            if (stalled || ++aboard < boardTicks) return;
            aboard = 0;
            float capacity = Capacity(prop.Scale);
            if (capacity >= options.RiderMass)
            {
                Launch();
                return;
            }
            stalled = true;
            State = SailState.Stall;
            tick = 0;
            startPosition = prop.Position;
            float share = capacity / options.RiderMass;
            SailStalled?.Invoke(share);
            Game.Events.RaiseSailStalled(Event(prop.Center, prop, share));
        }

        void TickStall()
        {
            if (!StillOurs()) return;
            tick++;
            int total = Ticks(StallSeconds, 1);
            float t = Mathf.Clamp01((float)tick / total);
            mover.MoveTo(startPosition + Vector3.up * (StallRise * Mathf.Sin(t * Mathf.PI)), prop.Rotation);
            if (tick >= total) State = SailState.Moored;
        }

        void Launch()
        {
            startPosition = prop.Position;
            startRotation = prop.Rotation;
            Vector3 centre = prop.Center;
            Vector3 toLanding = options.Landing - centre;
            distance = Mathf.Max(0f, Vector3.Dot(toLanding, direction));
            lateral = Vector3.Dot(toLanding, side);
            // It comes down until its underside is on the landing's height.
            GadgetKit.VerticalExtent(prop, out float underside, out _);
            heightDrop = options.Landing.y + 0.01f - underside;

            // Turn so that its long axis (z) lies along the wind, whichever end is nearer.
            Vector3 forward = startRotation * Vector3.forward;
            forward.y = 0f;
            Vector3 heading = Vector3.Dot(forward, direction) >= 0f ? direction : -direction;
            endRotation = forward.sqrMagnitude > 1e-6f
                ? Quaternion.FromToRotation(forward.normalized, heading) * startRotation
                : startRotation;

            // A trapezoid of speed: up to cruise, along, down to a stop on the landing.
            float a = Mathf.Max(0.1f, options.Acceleration), v = Mathf.Max(0.1f, options.CruiseSpeed);
            float ramp = v * v / a;
            if (distance >= ramp)
            {
                accelTime = v / a;
                cruiseTime = (distance - ramp) / v;
            }
            else
            {
                accelTime = Mathf.Sqrt(distance / a);
                cruiseTime = 0f;
            }
            travelTime = accelTime * 2f + cruiseTime;

            wasGrabbable = prop.Grabbable;
            prop.Grabbable = false;
            State = SailState.Glide;
            tick = 0;
            riderless = 0;
            SailLaunched?.Invoke();
            Game.Events.RaiseSailLaunched(Event(centre, prop, prop.Scale));
        }

        // Distance travelled along the wind after t seconds of glide.
        float Travelled(float t)
        {
            float a = Mathf.Max(0.1f, options.Acceleration);
            float peak = a * accelTime;
            if (t <= accelTime) return 0.5f * a * t * t;
            float u = 0.5f * a * accelTime * accelTime;
            if (t <= accelTime + cruiseTime) return u + peak * (t - accelTime);
            u += peak * cruiseTime;
            float d = Mathf.Min(t - accelTime - cruiseTime, accelTime);
            return u + peak * d - 0.5f * a * d * d;
        }

        void TickGlide()
        {
            if (!prop.Driven)
            {
                // Respawned by level code in mid-flight.
                prop.Grabbable = wasGrabbable;
                State = SailState.Loose;
                return;
            }

            riderless = RiderAboard ? 0 : riderless + 1;
            if (riderless >= abandonTicks)
            {
                // Nobody aboard: it is let go and sinks. Over the canyon the kill plane brings it back.
                prop.Grabbable = wasGrabbable;
                prop.EndDrive(mover.Velocity);
                State = SailState.Loose;
                still = 0;
                return;
            }

            tick++;
            float t = tick * Sim.Dt;
            float u = Mathf.Min(distance, Travelled(Mathf.Min(t, travelTime)));
            float align = GadgetKit.Smooth(options.AlignDistance > 0f ? u / Mathf.Min(options.AlignDistance, Mathf.Max(distance, 1e-3f)) : 1f);
            float rise = options.CruiseHeight * GadgetKit.Smooth(t / Mathf.Max(options.RiseSeconds, Sim.Dt));
            float descend = GadgetKit.Smooth((t - travelTime) / Mathf.Max(options.DescendSeconds, Sim.Dt));
            float height = Mathf.Lerp(rise, heightDrop, descend);
            Vector3 position = startPosition + direction * u + side * (lateral * align) + Vector3.up * height;
            mover.MoveTo(position, Quaternion.Slerp(startRotation, endRotation, align));

            if (t < travelTime + options.DescendSeconds) return;
            prop.Grabbable = wasGrabbable;
            prop.EndDrive(Vector3.zero);
            State = SailState.Docked;
            SailDocked?.Invoke();
            Game.Events.RaiseSailDocked(Event(prop.Center, prop));
        }

        public override void Reset()
        {
            base.Reset();
            if (!prop.Removed)
            {
                if (State == SailState.Glide) prop.Grabbable = wasGrabbable;
                if (prop.Driven) prop.EndDrive(Vector3.zero);
            }
            State = SailState.Loose;
            still = aboard = riderless = tick = 0;
            stalled = false;
        }
    }
}
