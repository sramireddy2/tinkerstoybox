using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum SeesawSide
    {
        A,
        B,
    }

    public enum SeesawState
    {
        /// <summary>The rest side is down.</summary>
        Rest,
        /// <summary>The strike side is on its way down.</summary>
        Swing,
        /// <summary>The strike side is down and something heavy holds it there.</summary>
        Tipped,
        /// <summary>On its way back to rest.</summary>
        Return,
    }

    public sealed class SeesawOptions
    {
        public string Name = "Seesaw";
        /// <summary>A point of the pivot line: the plank's underside lies on it.</summary>
        public Vector3 Pivot;
        /// <summary>The direction arm A points in, seen from above. Zero: Cross(Axis, up).</summary>
        public Vector3 ArmADirection;
        /// <summary>The pivot line (only used while <see cref="ArmADirection"/> is zero).</summary>
        public Vector3 Axis = Vector3.right;
        /// <summary>Lengths of the two arms, from the pivot to the tips.</summary>
        public float ArmA = 10f, ArmB = 5f;
        public float Width = 3f, Thickness = 0.2f;
        /// <summary>The plank's top is shorter than its underside by this much at either tip: a ramp to walk on by (negative: 3 x thickness).</summary>
        public float TipTaper = -1f;
        /// <summary>
        /// Instead of a plank of its own: a prop that lies on the pivot in the rest pose (a seated ruler).
        /// The seesaw drives it (BeginDrive) and keeps its pose relative to the lever.
        /// </summary>
        public Prop Plank;
        /// <summary>The side that is down with nobody on it.</summary>
        public SeesawSide RestSide = SeesawSide.A;
        /// <summary>The plank's own weight, as a mass at the rest side's tip. It only decides which side is down with nobody on it.</summary>
        public float PlankBias = 1f;
        /// <summary>A load that always sits at the rest side's tip and counts as rider load (the marble in the ruler's cap).</summary>
        public float RiderBias;
        /// <summary>The pads reach this far past the plank's edges, and <see cref="PadHeight"/> above it.</summary>
        public float TipMargin = 1f;
        public float PadHeight = 8f;
        /// <summary>Degrees per second of the swing back.</summary>
        public float ReturnRate = 60f;
        /// <summary>The height the tips come down to.</summary>
        public float FloorY;
        /// <summary>Distance from the pivot, on the rest side, of something the seesaw shoots (0: nothing). See ProjectileLaunched.</summary>
        public float ProjectileArm;
        /// <summary>Distance from the pivot, on the rest side, of the painted seat (0: none). Only a mark.</summary>
        public float SeatArm;
    }

    /// <summary>
    /// A lever (LEVELS 2.2, Levels 7 and 15). The rule is closed-form: when the strike side out-weighs the
    /// rider side, f = clamp((M - m r) / (M + m r^2), 0.05, 1) with r = rider arm / strike arm; the strike
    /// tip accelerates down at f g until it meets the floor (travel h), and a rider at distance x from the
    /// pivot leaves with the vertical speed r sqrt(2 g f h) x / riderArm. The plank is a kinematic mover
    /// whose pose is a function of the ticks since the strike; the release sets the rider's velocity
    /// explicitly, so the height does not depend on what the solver makes of a fast-rising platform.
    /// </summary>
    public sealed class Seesaw : Gadget
    {
        /// <summary>Below this much load on the strike side a tipped seesaw swings back.</summary>
        public const float HoldLoad = 0.5f;
        const float TouchSkin = 0.12f;

        static readonly Collider[] Hits = new Collider[64];

        readonly SeesawOptions options;
        readonly Vector3 dirA;
        readonly float restAngle, tippedAngle;
        readonly float strikeArm, riderArm;
        readonly Mover mover;
        readonly Rigidbody body;
        readonly Collider[] colliders;
        readonly Prop plankProp;
        readonly Vector3 relPosition;
        readonly Quaternion relRotation;
        readonly SignalLamp lamp;

        float angle;
        float swingFrom, swingTravel, swingF;
        int swingTick;
        bool swingLanded;
        float returnSpeed;
        bool ghosted;
        int riderTicks;
        float riderDistance;

        public Seesaw(LevelContext ctx, SeesawOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.ArmA <= 0f || options.ArmB <= 0f) throw new ArgumentException("A seesaw needs two arms.", nameof(options));
            Vector3 toward = options.ArmADirection;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-8f) toward = Vector3.Cross(options.Axis, Vector3.up);
            toward.y = 0f;
            dirA = toward.sqrMagnitude > 1e-8f ? toward.normalized : Vector3.forward;

            float height = Mathf.Max(0f, options.Pivot.y - options.FloorY);
            float downA = Mathf.Asin(Mathf.Clamp01(height / options.ArmA)) * Mathf.Rad2Deg;
            float downB = -Mathf.Asin(Mathf.Clamp01(height / options.ArmB)) * Mathf.Rad2Deg;
            bool restA = options.RestSide == SeesawSide.A;
            restAngle = restA ? downA : downB;
            tippedAngle = restA ? downB : downA;
            strikeArm = restA ? options.ArmB : options.ArmA;
            riderArm = restA ? options.ArmA : options.ArmB;
            angle = restAngle;

            if (options.Plank != null)
            {
                plankProp = options.Plank;
                mover = plankProp.BeginDrive();
                if (mover == null) throw new InvalidOperationException("The seesaw's plank " + plankProp + " cannot be driven (it is held or removed).");
                FrameAt(restAngle, out Vector3 origin, out Quaternion rotation);
                Quaternion inverse = Quaternion.Inverse(rotation);
                relPosition = inverse * (plankProp.Position - origin);
                relRotation = inverse * plankProp.Rotation;
                colliders = plankProp.Colliders;
            }
            else
            {
                GameObject plank = BuildPlank(out lamp);
                FrameAt(restAngle, out Vector3 origin, out Quaternion rotation);
                mover = ctx.AddKinematic(plank, origin, rotation);
                relPosition = Vector3.zero;
                relRotation = Quaternion.identity;
                colliders = plank.GetComponentsInChildren<Collider>(true);
            }
            body = mover.Body;
        }

        public SeesawState State { get; private set; } = SeesawState.Rest;
        /// <summary>True from the moment a swing has thrown a rider until the seesaw is back at rest.</summary>
        public bool Launched { get; private set; }
        /// <summary>Degrees the tip of arm A is below the horizontal (negative: above).</summary>
        public float Angle => angle;
        public float RestAngle => restAngle;
        public float TippedAngle => tippedAngle;
        public Mover Mover => mover;
        /// <summary>False once a plank prop has been taken away from it (grabbed): the seesaw does nothing any more.</summary>
        public bool Attached => plankProp == null || (!plankProp.Removed && plankProp.Driven);
        /// <summary>The loads the last tick measured.</summary>
        public float StrikeLoad { get; private set; }
        public float RiderLoad { get; private set; }
        /// <summary>f of the last strike.</summary>
        public float LastF => swingF;
        /// <summary>The speed the last launch gave its rider.</summary>
        public float LastLaunchSpeed { get; private set; }
        public Signal Signal => Launched ? Palette.Go : Palette.Amber;

        /// <summary>Inside the tick: the strike load M and f.</summary>
        public event Action<float, float> SeesawStruck;
        /// <summary>The rider (null: the player) and the vertical speed it left with.</summary>
        public event Action<Prop, float> SeesawLaunched;
        public event Action SeesawReturned;
        /// <summary>With a ProjectileArm: where the projectile leaves from and its velocity (straight up).</summary>
        public event Action<Vector3, Vector3> ProjectileLaunched;

        /// <summary>f for a strike load M against a rider load m (the closed-form rule; pure).</summary>
        public float F(float strikeLoad, float riderLoad)
        {
            float r = riderArm / strikeArm;
            return Mathf.Clamp((strikeLoad - riderLoad * r) / (strikeLoad + riderLoad * r * r), 0.05f, 1f);
        }

        /// <summary>How far the strike tip travels in a full swing.</summary>
        public float StrikeTravel => TipHeight(false, restAngle) - options.FloorY;

        /// <summary>The vertical speed a full swing with this f gives a rider at this distance from the pivot.</summary>
        public float LaunchSpeed(float f, float distance) =>
            riderArm / strikeArm * Mathf.Sqrt(2f * Game.Gravity * f * StrikeTravel) * distance / riderArm;

        /// <summary>World position of a point of the plank: along arm A (negative: arm B), above its underside, at an A-down angle.</summary>
        public Vector3 PointAt(float alongA, float above, float atAngle)
        {
            FrameAt(atAngle, out Vector3 origin, out Quaternion rotation);
            return origin + rotation * new Vector3(0f, above, alongA);
        }

        protected override void Tick(float dt)
        {
            if (!Attached) return;
            MeasureLoads();

            switch (State)
            {
                case SeesawState.Rest:
                    if (ShouldStrike()) BeginSwing();
                    break;
                case SeesawState.Tipped:
                    if (StrikeLoad < HoldLoad)
                    {
                        State = SeesawState.Return;
                        returnSpeed = 0f;
                    }
                    break;
            }

            if (State == SeesawState.Swing) TickSwing();
            else if (State == SeesawState.Return) TickReturn(dt);

            if (ghosted && State != SeesawState.Swing && !Overlapping())
            {
                GadgetKit.IgnorePlayer(Game, colliders, false);
                ghosted = false;
            }
            if (lamp != null)
            {
                lamp.Set(Signal);
                lamp.Pulse(Seconds);
            }
        }

        bool ShouldStrike() =>
            StrikeLoad >= HoldLoad && StrikeLoad * strikeArm > (RiderLoad + options.PlankBias) * riderArm;

        void BeginSwing()
        {
            // With nobody on the rest side the plank's own weight is what the strike lifts.
            float m = Mathf.Max(RiderLoad, options.PlankBias);
            swingF = F(StrikeLoad, m);
            swingFrom = TipHeight(false, angle);
            swingTravel = Mathf.Max(0f, swingFrom - options.FloorY);
            swingTick = 0;
            swingLanded = false;
            Launched = false;
            State = SeesawState.Swing;
            SeesawStruck?.Invoke(StrikeLoad, swingF);
            Game.Events.RaiseSeesawStruck(Event(PointAt(StrikeSign * strikeArm, 0f, angle), null, StrikeLoad, swingF));
        }

        void TickSwing()
        {
            if (swingLanded)
            {
                // The tick after the tip met the floor: the plank is at rest, the riders are let go.
                Launch();
                State = SeesawState.Tipped;
                return;
            }
            swingTick++;
            float t = swingTick * Sim.Dt;
            float drop = Mathf.Min(swingTravel, 0.5f * swingF * Game.Gravity * t * t);
            float next = AngleForTip(false, swingFrom - drop);
            MoveTo(next, true);
            if (drop >= swingTravel) swingLanded = true;
        }

        void Launch()
        {
            float tipSpeed = riderArm / strikeArm * Mathf.Sqrt(2f * Game.Gravity * swingF * swingTravel);
            if (riderTicks > 0 && riderDistance > 0f)
            {
                float speed = tipSpeed * Mathf.Min(riderDistance, riderArm) / riderArm;
                Player player = Game.Player;
                if (speed > 1.5f)
                {
                    // Straight up, plus whatever way the player is steering: not the sideways part of the
                    // plank's swing, which would fling them some twelve units toward the pivot (measured).
                    InputFrame input = Game.LastInput;
                    Vector3 wish = Quaternion.Euler(0f, player.Yaw, 0f) * new Vector3(input.MoveX, 0f, input.MoveZ);
                    if (wish.sqrMagnitude > 1f) wish.Normalize();
                    Vector3 velocity = wish * ((input.Sprint ? Player.SprintSpeed : Player.WalkSpeed) * player.Scale);
                    player.SetVelocity(new Vector3(velocity.x, speed, velocity.z));
                    Launched = true;
                    LastLaunchSpeed = speed;
                    SeesawLaunched?.Invoke(null, speed);
                    Game.Events.RaiseSeesawLaunched(Event(player.Position, null, speed, swingF));
                }
            }
            if (options.ProjectileArm > 0f)
            {
                float speed = tipSpeed * options.ProjectileArm / riderArm;
                Vector3 from = PointAt(-StrikeSign * options.ProjectileArm, options.Thickness, angle);
                var velocity = new Vector3(0f, speed, 0f);
                ProjectileLaunched?.Invoke(from, velocity);
                Game.Events.RaiseSeesawProjectile(Event(from, null, speed, swingF));
            }
        }

        void TickReturn(float dt)
        {
            // Struck again on the way back: down it goes, from where it is.
            if (ShouldStrike())
            {
                BeginSwing();
                TickSwing();
                return;
            }
            returnSpeed = Mathf.MoveTowards(returnSpeed, options.ReturnRate, options.ReturnRate * 4f * dt);
            float next = Mathf.MoveTowards(angle, restAngle, returnSpeed * dt);
            if (!MoveTo(next, false))
            {
                // The player is under the arm that comes down: wait.
                returnSpeed = 0f;
                return;
            }
            if (!Mathf.Approximately(angle, restAngle)) return;
            angle = restAngle;
            State = SeesawState.Rest;
            Launched = false;
            SeesawReturned?.Invoke();
            Game.Events.RaiseSeesawReturned(Event(options.Pivot));
        }

        // Moves the plank to an angle. A swing in progress goes through a player who is in the way (the
        // capsule is ignored until they have separated); anything else refuses and returns false.
        bool MoveTo(float next, bool force)
        {
            PlankPose(next, out Vector3 position, out Quaternion rotation);
            bool riding = GadgetKit.PlayerStandsOn(Game, body);
            if (!riding && !ghosted && GadgetKit.WouldCrush(Game, mover.Transform, colliders, position, rotation))
            {
                if (!force) return false;
                GadgetKit.IgnorePlayer(Game, colliders, true);
                ghosted = true;
            }
            angle = next;
            mover.MoveTo(position, rotation);
            return true;
        }

        bool Overlapping() => GadgetKit.OverlapsPlayer(Game, colliders);

        void MeasureLoads()
        {
            FrameAt(angle, out Vector3 origin, out Quaternion rotation);
            Quaternion inverse = Quaternion.Inverse(rotation);
            float length = options.ArmA + options.ArmB;
            var half = new Vector3(options.Width * 0.5f + TouchSkin, options.Thickness * 0.5f + TouchSkin, length * 0.5f + TouchSkin);
            Vector3 centre = origin + rotation * new Vector3(0f, options.Thickness * 0.5f, (options.ArmA - options.ArmB) * 0.5f);
            int count = Game.PhysicsScene.OverlapBox(centre, half, Hits, rotation, Layers.PropMask, QueryTriggerInteraction.Ignore);

            float loadA = 0f, loadB = 0f;
            Prop previous = null;
            // Colliders of one prop come in a row often enough; a prop with several is still counted once.
            for (int i = 0; i < count; i++)
            {
                Prop prop = PropRef.Of(Hits[i]);
                if (prop == null || prop == plankProp || prop == previous || !GadgetKit.IsLoose(prop)) continue;
                bool seen = false;
                for (int k = 0; k < i; k++)
                    if (PropRef.Of(Hits[k]) == prop) seen = true;
                if (seen) continue;
                previous = prop;
                Vector3 local = inverse * (prop.Center - origin);
                if (Mathf.Abs(local.x) > options.Width * 0.5f + options.TipMargin || local.y < 0f || local.y > options.PadHeight) continue;
                if (local.z > options.ArmA * 0.1f && local.z <= options.ArmA + options.TipMargin) loadA += prop.Mass;
                else if (local.z < -options.ArmB * 0.1f && local.z >= -options.ArmB - options.TipMargin) loadB += prop.Mass;
            }

            Player player = Game.Player;
            if (GadgetKit.PlayerStandsOn(Game, body))
            {
                Vector3 local = inverse * (player.Position - origin);
                if (local.z > 0f) loadA += Player.Mass;
                else loadB += Player.Mass;
                // How far out on the rest side the rider stands (0 on the strike side).
                riderDistance = Mathf.Max(0f, -StrikeSign * local.z);
                riderTicks = 3;
            }
            else if (riderTicks > 0)
            {
                riderTicks--;
            }

            bool restA = options.RestSide == SeesawSide.A;
            StrikeLoad = restA ? loadB : loadA;
            RiderLoad = (restA ? loadA : loadB) + options.RiderBias;
        }

        // +1 if the strike side is along arm A, -1 if it is arm B.
        float StrikeSign => options.RestSide == SeesawSide.A ? -1f : 1f;

        // Height of a tip at an A-down angle.
        float TipHeight(bool restSide, float atAngle)
        {
            bool tipA = restSide == (options.RestSide == SeesawSide.A);
            float sine = Mathf.Sin(atAngle * Mathf.Deg2Rad);
            return tipA ? options.Pivot.y - options.ArmA * sine : options.Pivot.y + options.ArmB * sine;
        }

        // The A-down angle at which a tip is at a height.
        float AngleForTip(bool restSide, float height)
        {
            bool tipA = restSide == (options.RestSide == SeesawSide.A);
            float arm = tipA ? options.ArmA : options.ArmB;
            float sine = Mathf.Clamp((height - options.Pivot.y) / arm, -1f, 1f);
            return (tipA ? -1f : 1f) * Mathf.Asin(sine) * Mathf.Rad2Deg;
        }

        // The lever's frame: origin on the pivot, z along arm A, y out of the plank's top.
        void FrameAt(float atAngle, out Vector3 origin, out Quaternion rotation)
        {
            float radians = atAngle * Mathf.Deg2Rad;
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            origin = options.Pivot;
            rotation = Quaternion.LookRotation(dirA * c - Vector3.up * s, Vector3.up * c + dirA * s);
        }

        void PlankPose(float atAngle, out Vector3 position, out Quaternion rotation)
        {
            FrameAt(atAngle, out Vector3 origin, out Quaternion frame);
            position = origin + frame * relPosition;
            rotation = frame * relRotation;
        }

        /// <summary>Back to rest, at once.</summary>
        public override void Reset()
        {
            base.Reset();
            State = SeesawState.Rest;
            Launched = false;
            angle = restAngle;
            returnSpeed = 0f;
            riderTicks = 0;
            if (ghosted)
            {
                GadgetKit.IgnorePlayer(Game, colliders, false);
                ghosted = false;
            }
            if (!Attached) return;
            PlankPose(angle, out Vector3 position, out Quaternion rotation);
            mover.Teleport(position, rotation);
        }

        // A steel plank whose origin is on the pivot line, under it: arm A along +z. Its top is shorter than
        // its underside at both tips, so the tip that lies on the floor is a ramp, not a step.
        GameObject BuildPlank(out SignalLamp seat)
        {
            float w = options.Width * 0.5f, t = options.Thickness;
            float taper = options.TipTaper >= 0f ? options.TipTaper : t * 3f;
            taper = Mathf.Min(taper, Mathf.Min(options.ArmA, options.ArmB) * 0.5f);
            float a = options.ArmA, b = -options.ArmB;
            var corners = new[]
            {
                new Vector3(-w, 0f, b), new Vector3(w, 0f, b), new Vector3(w, 0f, a), new Vector3(-w, 0f, a),
                new Vector3(-w, t, b + taper), new Vector3(w, t, b + taper), new Vector3(w, t, a - taper), new Vector3(-w, t, a - taper),
            };
            var faces = new[]
            {
                new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 },
            };
            Mesh mesh = MeshKit.Cached(MeshKit.Key("SeesawPlank", options.ArmA, options.ArmB, options.Width, t, taper),
                () => GadgetKit.ConvexMesh("SeesawPlank", corners, faces));
            var plank = new GameObject("Seesaw " + Name);
            MeshCollider collider = plank.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
            plank.AddComponent<MeshFilter>().sharedMesh = mesh;
            plank.AddComponent<MeshRenderer>().sharedMaterial = GadgetKit.Metal;
            seat = null;
            if (options.SeatArm > 0f)
            {
                float radius = Mathf.Min(options.Width * 0.3f, 0.6f);
                float along = (options.RestSide == SeesawSide.A ? 1f : -1f) * options.SeatArm;
                seat = GadgetKit.Lamp(plank.transform, GadgetKit.DiscMesh(radius, 0.02f), Palette.Amber, new Vector3(0f, t + 0.012f, along));
            }
            return plank;
        }
    }
}
