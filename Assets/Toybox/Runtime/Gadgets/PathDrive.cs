using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>
    /// One leg of a scripted path. Each kind is a closed form: where the body is after t seconds on the
    /// leg depends on nothing but where the leg started.
    /// </summary>
    public abstract class PathSegment
    {
        /// <summary>Where the leg starts: the end of the one before it, unless the leg names a start of its own.</summary>
        public Vector3 Start { get; private set; }
        public float Duration { get; protected set; }
        public Vector3 End => Evaluate(Duration);

        internal void Prepare(Vector3 start)
        {
            Start = Begin(start);
            Duration = Mathf.Max(0f, Measure());
        }

        /// <summary>The start the leg uses, given where the path has got to.</summary>
        protected virtual Vector3 Begin(Vector3 start) => start;
        protected abstract float Measure();
        /// <summary>Position after t seconds on the leg (0..Duration).</summary>
        public abstract Vector3 Evaluate(float t);
        /// <summary>Velocity after t seconds on the leg.</summary>
        public virtual Vector3 Velocity(float t)
        {
            float a = Mathf.Clamp(t - Sim.Dt * 0.5f, 0f, Duration), b = Mathf.Clamp(t + Sim.Dt * 0.5f, 0f, Duration);
            return b > a ? (Evaluate(b) - Evaluate(a)) / (b - a) : Vector3.zero;
        }

        /// <summary>Along a straight line to a point with constant acceleration (a ball down a chute, a ball along a shelf).</summary>
        public static PathSegment Roll(Vector3 to, float acceleration, float startSpeed = 0f, Vector3? from = null) =>
            new RollSegment { To = to, Acceleration = acceleration, StartSpeed = startSpeed, From = from };

        /// <summary>Free flight from a velocity until the body comes down to a height.</summary>
        public static PathSegment Ballistic(Vector3 velocity, float untilY, Vector3? from = null) =>
            new BallisticSegment { InitialVelocity = velocity, UntilY = untilY, From = from };

        /// <summary>A smooth move to a point in a fixed time.</summary>
        public static PathSegment Ease(Vector3 to, float seconds) => new EaseSegment { To = to, Seconds = seconds };

        /// <summary>Through a list of points at constant parameter speed (Catmull-Rom).</summary>
        public static PathSegment Spline(IList<Vector3> points, float seconds) => new SplineSegment { Points = new List<Vector3>(points), Seconds = seconds };

        /// <summary>Stays where it is.</summary>
        public static PathSegment Hold(float seconds) => new HoldSegment { Seconds = seconds };

        sealed class RollSegment : PathSegment
        {
            public Vector3 To;
            public Vector3? From;
            public float Acceleration, StartSpeed;
            Vector3 direction;
            float length;

            protected override Vector3 Begin(Vector3 start) => From ?? start;

            protected override float Measure()
            {
                Vector3 along = To - Start;
                length = along.magnitude;
                direction = length > 1e-6f ? along / length : Vector3.zero;
                if (length <= 1e-6f) return 0f;
                if (Mathf.Abs(Acceleration) < 1e-6f) return StartSpeed > 1e-6f ? length / StartSpeed : 0f;
                // v0 t + a t^2 / 2 = length
                float discriminant = StartSpeed * StartSpeed + 2f * Acceleration * length;
                return discriminant <= 0f ? 0f : (-StartSpeed + Mathf.Sqrt(discriminant)) / Acceleration;
            }

            public override Vector3 Evaluate(float t)
            {
                t = Mathf.Clamp(t, 0f, Duration);
                return Start + direction * Mathf.Min(length, StartSpeed * t + 0.5f * Acceleration * t * t);
            }

            public override Vector3 Velocity(float t) => direction * (StartSpeed + Acceleration * Mathf.Clamp(t, 0f, Duration));
        }

        sealed class BallisticSegment : PathSegment
        {
            public Vector3 InitialVelocity;
            public Vector3? From;
            public float UntilY;

            protected override Vector3 Begin(Vector3 start) => From ?? start;

            protected override float Measure()
            {
                // y0 + vy t - g t^2 / 2 = untilY, on the way down.
                float g = Game.Gravity;
                float discriminant = InitialVelocity.y * InitialVelocity.y + 2f * g * (Start.y - UntilY);
                if (discriminant < 0f) return Mathf.Max(0f, 2f * InitialVelocity.y / g);
                return (InitialVelocity.y + Mathf.Sqrt(discriminant)) / g;
            }

            public override Vector3 Evaluate(float t)
            {
                t = Mathf.Clamp(t, 0f, Duration);
                return Start + InitialVelocity * t + Vector3.down * (0.5f * Game.Gravity * t * t);
            }

            public override Vector3 Velocity(float t) => InitialVelocity + Vector3.down * (Game.Gravity * Mathf.Clamp(t, 0f, Duration));
        }

        sealed class EaseSegment : PathSegment
        {
            public Vector3 To;
            public float Seconds;

            protected override float Measure() => Seconds;

            public override Vector3 Evaluate(float t) => Duration <= 0f ? To : Vector3.Lerp(Start, To, GadgetKit.Smooth(t / Duration));
        }

        sealed class SplineSegment : PathSegment
        {
            public List<Vector3> Points;
            public float Seconds;

            protected override float Measure() => Points.Count > 0 ? Seconds : 0f;

            public override Vector3 Evaluate(float t)
            {
                int spans = Points.Count;
                if (spans == 0) return Start;
                float u = Duration <= 0f ? spans : Mathf.Clamp01(t / Duration) * spans;
                int span = Mathf.Min(spans - 1, Mathf.FloorToInt(u));
                float f = u - span;
                Vector3 p0 = Point(span - 1), p1 = Point(span), p2 = Point(span + 1), p3 = Point(span + 2);
                float f2 = f * f, f3 = f2 * f;
                return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3);
            }

            // Knot i: 0 is the start, 1.. are the points; clamped at both ends.
            Vector3 Point(int i)
            {
                i = Mathf.Clamp(i, 0, Points.Count);
                return i == 0 ? Start : Points[i - 1];
            }
        }

        sealed class HoldSegment : PathSegment
        {
            public float Seconds;
            protected override float Measure() => Seconds;
            public override Vector3 Evaluate(float t) => Start;
            public override Vector3 Velocity(float t) => Vector3.zero;
        }
    }

    public sealed class PathDriveOptions
    {
        public string Name = "Path";
        /// <summary>The prop to drive (taken with BeginDrive at Start). Its centre follows the path.</summary>
        public Prop Body;
        /// <summary>Instead of a prop: a kinematic body of the level.</summary>
        public Mover Mover;
        public IList<PathSegment> Segments;
        /// <summary>Added to every path point: for a ball whose lowest point follows a surface, one radius along the surface normal.</summary>
        public Vector3 Offset;
        /// <summary>The velocity a driven prop is handed back to physics with (null: the last leg's end velocity).</summary>
        public Vector3? EndVelocity;
        /// <summary>The body ignores the player's capsule while it is driven.</summary>
        public bool GhostToPlayer;
    }

    /// <summary>
    /// A body on rails (LEVELS 2.2): a list of legs - Roll, Ballistic, Ease, Spline, Hold - each a closed
    /// form, so the position is a function of the ticks since Start. The machine of Level 15 is made of
    /// these; nothing in a run is left to the solver.
    /// </summary>
    public sealed class PathDrive : Gadget
    {
        readonly PathDriveOptions options;
        readonly List<PathSegment> segments = new List<PathSegment>();
        Mover mover;
        int tick;
        int segment;
        float segmentStart;
        bool finishing;
        bool ghosted;

        public PathDrive(LevelContext ctx, PathDriveOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Body == null && options.Mover == null) throw new ArgumentException("A path drive needs a prop or a mover.", nameof(options));
            if (options.Segments != null) segments.AddRange(options.Segments);
        }

        public bool Running { get; private set; }
        /// <summary>The leg in progress (the number of legs once it has finished).</summary>
        public int Segment => segment;
        /// <summary>The legs. They may be replaced while the drive is not running.</summary>
        public List<PathSegment> Segments => segments;
        /// <summary>Seconds the whole path takes (after Start).</summary>
        public float Duration { get; private set; }
        public Vector3 Offset
        {
            get => options.Offset;
            set => options.Offset = value;
        }

        /// <summary>Inside the tick: a leg ended.</summary>
        public event Action<int> SegmentEnded;
        public event Action Finished;

        /// <summary>
        /// Takes the body and runs the path from where it is. False if it is already running, has no legs,
        /// or the prop cannot be driven (held, removed).
        /// </summary>
        public bool Start()
        {
            if (Running || segments.Count == 0) return false;
            Vector3 point;
            if (options.Body != null)
            {
                mover = options.Body.BeginDrive();
                if (mover == null) return false;
                point = options.Body.Center - options.Offset;
            }
            else
            {
                mover = options.Mover;
                point = mover.Position - options.Offset;
            }
            Duration = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].Prepare(point);
                point = segments[i].End;
                Duration += segments[i].Duration;
            }
            tick = 0;
            segment = 0;
            segmentStart = 0f;
            finishing = false;
            Running = true;
            if (options.GhostToPlayer && options.Body != null)
            {
                GadgetKit.IgnorePlayer(Game, options.Body.Colliders, true);
                ghosted = true;
            }
            return true;
        }

        /// <summary>Stops where it is; a driven prop goes on with the velocity it had.</summary>
        public void Abort() => Stop(false);

        /// <summary>The path point after this many seconds (without the offset). Valid after Start.</summary>
        public Vector3 PointAt(float seconds)
        {
            float start = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                if (seconds <= start + segments[i].Duration || i == segments.Count - 1)
                    return segments[i].Evaluate(seconds - start);
                start += segments[i].Duration;
            }
            return Vector3.zero;
        }

        protected override void Tick(float dt)
        {
            if (!Running) return;
            // Grabbed, respawned or removed on the way.
            if (options.Body != null && (options.Body.Removed || !options.Body.Driven))
            {
                Stop(false);
                return;
            }
            if (finishing)
            {
                // The step that brought it to the end has run: hand it back.
                Stop(true);
                return;
            }

            tick++;
            float t = tick * Sim.Dt;
            while (segment < segments.Count && t >= segmentStart + segments[segment].Duration - 1e-5f)
            {
                segmentStart += segments[segment].Duration;
                int ended = segment++;
                SegmentEnded?.Invoke(ended);
                Game.Events.RaisePathSegmentEnded(Event(segments[ended].End + options.Offset, options.Body, 0f, 0f, ended));
                if (!Running) return;
            }
            Vector3 point;
            if (segment >= segments.Count)
            {
                point = segments[segments.Count - 1].End;
                finishing = true;
            }
            else
            {
                point = segments[segment].Evaluate(t - segmentStart);
            }
            Place(point + options.Offset);
        }

        void Place(Vector3 centre)
        {
            if (options.Body != null)
            {
                Prop prop = options.Body;
                Quaternion rotation = prop.Rotation;
                mover.MoveTo(centre - rotation * (prop.LocalCenter * prop.Scale), rotation);
            }
            else
            {
                mover.MoveTo(centre);
            }
        }

        void Stop(bool completed)
        {
            if (!Running) return;
            Running = false;
            Prop prop = options.Body;
            if (prop != null)
            {
                if (!prop.Removed && prop.Driven)
                {
                    Vector3 velocity = completed
                        ? options.EndVelocity ?? segments[segments.Count - 1].Velocity(segments[segments.Count - 1].Duration)
                        : mover.Velocity;
                    prop.EndDrive(velocity);
                }
                if (ghosted && !prop.Removed) GadgetKit.IgnorePlayer(Game, prop.Colliders, false);
            }
            ghosted = false;
            if (!completed) return;
            Finished?.Invoke();
            Game.Events.RaisePathFinished(Event(prop != null && !prop.Removed ? prop.Center : mover.Position, prop));
        }

        public override void Reset()
        {
            base.Reset();
            Stop(false);
            segment = 0;
        }
    }
}
