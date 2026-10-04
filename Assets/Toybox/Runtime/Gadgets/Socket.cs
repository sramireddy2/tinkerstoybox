using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>How a toy fits where it is wanted. Shared by sockets, gauges and rejection messages.</summary>
    public enum FitState
    {
        /// <summary>Nothing is being judged.</summary>
        Idle,
        TooSmall,
        Good,
        TooBig,
        /// <summary>The right size, pointing the wrong way.</summary>
        Backwards,
    }

    public sealed class SocketOptions
    {
        public string Name = "Socket";
        /// <summary>Only props with this tag are taken.</summary>
        public string AcceptTag;
        /// <summary>A prop is a candidate while its centre is inside.</summary>
        public Zone Capture;
        /// <summary>The scale window that is accepted.</summary>
        public float MinScale, MaxScale = float.MaxValue;
        /// <summary>
        /// Optional heading test: this axis of the prop (in its own space; zero for no test), flattened onto
        /// the floor, has to lie within <see cref="YawTolerance"/> degrees of <see cref="YawReference"/>.
        /// </summary>
        public Vector3 YawAxis;
        public Vector3 YawReference = Vector3.forward;
        public float YawTolerance = 30f;
        /// <summary>A prop moving faster than this is not taken (yet).</summary>
        public float MaxSpeed = 3f;
        /// <summary>The pose of the prop's transform when seated, as a function of its (final) scale.</summary>
        public Func<float, Pose> SeatPose;
        /// <summary>Optional: the scale the prop is given while it is eased in, as a function of the scale it arrived with.</summary>
        public Func<float, float> SeatScale;
        public float EaseSeconds = 0.25f;
        /// <summary>Optional: after the ease the prop descends under gravity until its top is at this height.</summary>
        public float? ThenFallTo;
        /// <summary>Optional: after that it turns by this many degrees about <see cref="ThenTurnAxis"/> (its own axis) - a key in a lock.</summary>
        public float ThenTurnDegrees;
        public Vector3 ThenTurnAxis = Vector3.forward;
        public float ThenTurnSeconds = 0.4f;
        /// <summary>The prop is no longer grabbable from the moment it is taken.</summary>
        public bool LockOnSeat;
        /// <summary>
        /// True: a prop released inside the capture zone with the conditions met is taken at once; nothing
        /// has to land. False: it has to have come to rest there first.
        /// </summary>
        public bool AirCapture = true;
        /// <summary>Where to put a signal lamp (amber waiting, green seated), or null for none.</summary>
        public Vector3? Lamp;
        public float LampRadius = 0.15f;
    }

    /// <summary>
    /// A place that takes a tagged toy of the right size (LEVELS 2.1): the well that takes the thimble, the
    /// keyhole, the four stations of the machine. A candidate in the window is taken over (BeginDrive) and
    /// eased to its seat as a function of the ticks since capture; one outside the window is rejected once
    /// per drop and left to physics.
    /// </summary>
    public sealed class Socket : Gadget
    {
        enum Phase
        {
            Empty,
            Easing,
            Falling,
            Turning,
            Seated,
        }

        const float RestSpeed = 0.5f;

        readonly SocketOptions options;
        readonly HashSet<Prop> rejected = new HashSet<Prop>();
        readonly HashSet<Prop> ignored = new HashSet<Prop>();
        readonly int easeTicks, turnTicks;
        readonly SignalLamp lamp;

        Phase phase;
        Prop prop;
        Mover mover;
        bool wasGrabbable;
        Vector3 fromPosition;
        Quaternion fromRotation;
        float fromScale, toScale;
        Pose seat;
        int tick;
        float fallSpeed, fallY, fallEndY;

        public Socket(LevelContext ctx, SocketOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.SeatPose == null) throw new ArgumentException("A socket needs a SeatPose.", nameof(options));
            easeTicks = Ticks(options.EaseSeconds, 1);
            turnTicks = Ticks(options.ThenTurnSeconds, 1);
            if (options.Lamp.HasValue)
            {
                var holder = new GameObject("Socket " + Name);
                holder.transform.SetParent(ctx.Root, false);
                holder.transform.position = options.Lamp.Value;
                Mesh mesh = MeshKit.Cached(MeshKit.Key("GadgetLamp", options.LampRadius), () => MeshKit.Sphere(options.LampRadius, 12, 8));
                lamp = GadgetKit.Lamp(holder.transform, mesh, Palette.Amber, Vector3.zero);
            }
        }

        /// <summary>The socket's lamp (for presentation), or null without one.</summary>
        public SignalLamp Lamp => lamp;

        public Zone Capture => options.Capture;
        public float MinScale => options.MinScale;
        public float MaxScale => options.MaxScale;
        /// <summary>True once the prop sits in its seat (after the ease, the fall and the turn).</summary>
        public bool Seated => phase == Phase.Seated;
        /// <summary>True while a captured prop is on its way to the seat.</summary>
        public bool Seating => phase == Phase.Easing || phase == Phase.Falling || phase == Phase.Turning;
        /// <summary>The prop in the seat or on its way there, or null.</summary>
        public Prop SeatedProp => prop;
        public Signal Signal => Seated ? Palette.Go : Palette.Amber;

        /// <summary>Inside the tick.</summary>
        public event Action<Prop> OnSeated;
        public event Action<Prop> OnUnseated;
        /// <summary>Once per drop of a prop that does not fit: the prop and why.</summary>
        public event Action<Prop, FitState> OnRejected;

        /// <summary>How a prop of this scale fits, heading aside. Pure.</summary>
        public FitState Fit(float scale)
        {
            if (scale < options.MinScale) return FitState.TooSmall;
            if (scale > options.MaxScale) return FitState.TooBig;
            return FitState.Good;
        }

        /// <summary>How this prop fits as it is now: its scale, then its heading.</summary>
        public FitState Fit(Prop candidate)
        {
            if (candidate == null) return FitState.Idle;
            FitState fit = Fit(candidate.Scale);
            if (fit != FitState.Good) return fit;
            return HeadingFits(candidate.Rotation) ? FitState.Good : FitState.Backwards;
        }

        public bool HeadingFits(Quaternion rotation)
        {
            if (options.YawAxis == Vector3.zero) return true;
            Vector3 axis = rotation * options.YawAxis;
            axis.y = 0f;
            Vector3 reference = options.YawReference;
            reference.y = 0f;
            if (axis.sqrMagnitude < 1e-6f || reference.sqrMagnitude < 1e-6f) return false;
            return Vector3.Angle(axis, reference) <= options.YawTolerance;
        }

        protected override void Tick(float dt)
        {
            switch (phase)
            {
                case Phase.Empty:
                    Look();
                    break;
                case Phase.Seated:
                    if (prop.Removed || !prop.Driven) Unseat(true);
                    break;
                default:
                    Drive();
                    break;
            }
            if (lamp != null)
            {
                lamp.Set(Signal);
                lamp.Pulse(Seconds);
            }
        }

        void Look()
        {
            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop candidate = props[i];
                if (candidate.Removed) continue;
                if (options.AcceptTag != null && !candidate.HasTag(options.AcceptTag)) continue;
                if (candidate.Held)
                {
                    // In the hand again: the next drop is judged afresh.
                    rejected.Remove(candidate);
                    ignored.Remove(candidate);
                    continue;
                }
                if (!candidate.GameObject.activeInHierarchy || candidate.BodyKind != PropBody.Dynamic) continue;
                if (!options.Capture.Contains(candidate.Center))
                {
                    rejected.Remove(candidate);
                    ignored.Remove(candidate);
                    continue;
                }
                if (candidate.Driven || ignored.Contains(candidate)) continue;

                FitState fit = Fit(candidate);
                if (fit != FitState.Good)
                {
                    if (rejected.Add(candidate))
                    {
                        OnRejected?.Invoke(candidate, fit);
                        Game.Events.RaiseSocketRejected(Event(candidate.Center, candidate, candidate.Scale, 0f, (int)fit));
                    }
                    continue;
                }
                float limit = options.AirCapture ? options.MaxSpeed : Mathf.Min(options.MaxSpeed, RestSpeed);
                if (candidate.Velocity.sqrMagnitude > limit * limit) continue;
                if (Take(candidate)) return;
            }
        }

        bool Take(Prop candidate)
        {
            Mover drive = candidate.BeginDrive();
            if (drive == null) return false;
            prop = candidate;
            mover = drive;
            wasGrabbable = candidate.Grabbable;
            if (options.LockOnSeat) candidate.Grabbable = false;
            fromPosition = candidate.Position;
            fromRotation = candidate.Rotation;
            fromScale = candidate.Scale;
            toScale = options.SeatScale != null ? Mathf.Clamp(options.SeatScale(fromScale), candidate.MinScale, candidate.MaxScale) : fromScale;
            seat = options.SeatPose(toScale);
            tick = 0;
            phase = Phase.Easing;
            Drive();
            return true;
        }

        void Drive()
        {
            // Grabbed (a grab ends the drive), respawned or removed on the way in.
            if (prop.Removed || !prop.Driven)
            {
                Unseat(false);
                return;
            }

            if (phase == Phase.Easing)
            {
                tick++;
                float t = GadgetKit.Smooth((float)tick / easeTicks);
                if (!Mathf.Approximately(fromScale, toScale)) prop.SetScale(Mathf.Lerp(fromScale, toScale, t));
                mover.MoveTo(Vector3.Lerp(fromPosition, seat.position, t), Quaternion.Slerp(fromRotation, seat.rotation, t));
                if (tick < easeTicks) return;
                tick = 0;
                if (options.ThenFallTo.HasValue)
                {
                    // Where the transform ends up when the prop's top is at the height asked for.
                    GadgetKit.VerticalExtent(prop, out _, out float top);
                    float topAboveOrigin = top - prop.Position.y;
                    fallY = seat.position.y;
                    fallEndY = options.ThenFallTo.Value - topAboveOrigin;
                    fallSpeed = 0f;
                    phase = fallY > fallEndY ? Phase.Falling : NextAfterFall();
                }
                else
                {
                    phase = NextAfterFall();
                }
                if (phase == Phase.Seated) Seat();
                return;
            }

            if (phase == Phase.Falling)
            {
                fallSpeed += Game.Gravity * Sim.Dt;
                fallY = Mathf.Max(fallEndY, fallY - fallSpeed * Sim.Dt);
                seat.position = new Vector3(seat.position.x, fallY, seat.position.z);
                mover.MoveTo(seat.position, seat.rotation);
                if (fallY > fallEndY) return;
                phase = NextAfterFall();
                if (phase == Phase.Seated) Seat();
                return;
            }

            // Turning.
            tick++;
            float turned = options.ThenTurnDegrees * GadgetKit.Smooth((float)tick / turnTicks);
            mover.MoveTo(seat.position, seat.rotation * Quaternion.AngleAxis(turned, options.ThenTurnAxis));
            if (tick < turnTicks) return;
            phase = Phase.Seated;
            Seat();
        }

        Phase NextAfterFall() => Mathf.Abs(options.ThenTurnDegrees) > 1e-3f ? Phase.Turning : Phase.Seated;

        void Seat()
        {
            OnSeated?.Invoke(prop);
            Game.Events.RaiseSocketSeated(Event(prop.Center, prop, prop.Scale));
        }

        void Unseat(bool wasSeated)
        {
            Prop left = prop;
            prop = null;
            mover = null;
            phase = Phase.Empty;
            if (left == null) return;
            if (!left.Removed) left.Grabbable = wasGrabbable || left.Held;
            if (!wasSeated) return;
            OnUnseated?.Invoke(left);
            Game.Events.RaiseSocketUnseated(Event(left.Removed ? options.Capture.Center : left.Center, left));
        }

        /// <summary>
        /// Lets go of the prop in the seat (or on its way there) and hands it back to physics with this
        /// velocity. It is not taken again until it has left the capture zone or been picked up.
        /// </summary>
        public void Release(Vector3 velocity = default)
        {
            if (prop == null) return;
            Prop left = prop;
            bool wasSeated = phase == Phase.Seated;
            if (!left.Removed && left.Driven) left.EndDrive(velocity);
            ignored.Add(left);
            Unseat(wasSeated);
        }

        public override void Reset()
        {
            base.Reset();
            Release();
            rejected.Clear();
            ignored.Clear();
        }
    }
}
