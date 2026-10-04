using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class PropCarrierOptions
    {
        public string Name = "Carrier";
        /// <summary>The moving beds: a <see cref="Train"/>, or any other set. Construct the carrier after it.</summary>
        public ICarrierBeds Beds;
        /// <summary>Only props with this tag are gripped (null: any dynamic prop).</summary>
        public string AcceptTag;
        /// <summary>The prop has to lie flat: its up axis this close to vertical (0.94: 20 degrees).</summary>
        public float FlatDot = 0.94f;
        /// <summary>
        /// Optional limits on the prop's reach from <see cref="RadiusCenter"/> (x, z): its nearest point no
        /// nearer than MinRadius, its farthest corner no farther than MaxRadius. A plank that would sweep
        /// the station or cut into the tower is not gripped.
        /// </summary>
        public float MinRadius, MaxRadius = float.MaxValue;
        public Vector2 RadiusCenter;
        /// <summary>Seconds over which a gripped prop is eased flat onto the deck.</summary>
        public float EaseSeconds = 0.1f;
    }

    /// <summary>
    /// Velcro beds (LEVELS 2.2, Level 9): a flat tagged prop that comes to lie on a moving bed is gripped -
    /// taken over with BeginDrive, eased onto the deck and then moved rigidly with the bed, so it carries
    /// the player like the bed does. A grab releases it.
    /// </summary>
    public sealed class PropCarrier : Gadget
    {
        sealed class Carried
        {
            public Prop Prop;
            public Mover Mover;
            public int Bed;
            public Vector3 FromPosition, ToPosition;
            public Quaternion FromRotation, ToRotation;
            public int Tick;
        }

        static readonly Collider[] Hits = new Collider[32];

        readonly PropCarrierOptions options;
        readonly List<Carried> carried = new List<Carried>();
        readonly int easeTicks;

        public PropCarrier(LevelContext ctx, PropCarrierOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Beds == null) throw new ArgumentException("A prop carrier needs beds.", nameof(options));
            easeTicks = Ticks(options.EaseSeconds, 1);
        }

        /// <summary>True while at least one prop is gripped.</summary>
        public bool Captured => carried.Count > 0;
        /// <summary>The bed (0 based; a train's wagon number minus one) of the first gripped prop, or -1.</summary>
        public int CarIndex => carried.Count > 0 ? carried[0].Bed : -1;
        /// <summary>The first gripped prop, or null.</summary>
        public Prop CarriedProp => carried.Count > 0 ? carried[0].Prop : null;
        public int Count => carried.Count;

        /// <summary>Inside the tick: the prop and its bed.</summary>
        public event Action<Prop, int> PropCaptured;
        public event Action<Prop> PropReleased;

        public bool IsCarrying(Prop prop)
        {
            for (int i = 0; i < carried.Count; i++)
                if (carried[i].Prop == prop) return true;
            return false;
        }

        /// <summary>Lets go of a gripped prop: it goes on with the velocity of its bed.</summary>
        public void Release(Prop prop)
        {
            for (int i = 0; i < carried.Count; i++)
            {
                if (carried[i].Prop != prop) continue;
                Carried entry = carried[i];
                carried.RemoveAt(i);
                if (!prop.Removed && prop.Driven) prop.EndDrive(entry.Mover.Velocity);
                Dropped(prop);
                return;
            }
        }

        protected override void Tick(float dt)
        {
            for (int i = 0; i < carried.Count; i++)
            {
                Carried entry = carried[i];
                Prop prop = entry.Prop;
                // Grabbed (a grab ends the drive), respawned or removed.
                if (prop.Removed || !prop.Driven)
                {
                    carried.RemoveAt(i--);
                    Dropped(prop);
                    continue;
                }
                if (entry.Tick < easeTicks) entry.Tick++;
                float t = GadgetKit.Smooth((float)entry.Tick / easeTicks);
                options.Beds.BedPose(entry.Bed, out Vector3 bedPosition, out Quaternion bedRotation);
                Vector3 local = Vector3.Lerp(entry.FromPosition, entry.ToPosition, t);
                Quaternion turn = Quaternion.Slerp(entry.FromRotation, entry.ToRotation, t);
                entry.Mover.MoveTo(bedPosition + bedRotation * local, bedRotation * turn);
                GadgetKit.SteadyRider(Game, entry.Mover);
            }

            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (!GadgetKit.IsLoose(prop) || prop.Driven || prop.Body.isKinematic || prop.BodyKind != PropBody.Dynamic) continue;
                if (options.AcceptTag != null && !prop.HasTag(options.AcceptTag)) continue;
                Vector3 up = prop.Rotation * Vector3.up;
                if (Mathf.Abs(up.y) < options.FlatDot) continue;
                int bed = BedUnder(prop);
                if (bed < 0 || !WithinRadius(prop)) continue;
                Grip(prop, bed, up);
            }
        }

        // The bed the prop lies on: of the beds whose box it touches, the one whose middle it is nearest to
        // (a plank that only brushes the corner of the next wagon belongs to the one under it), or -1.
        int BedUnder(Prop prop)
        {
            Vector3 size = options.Beds.BedSize;
            float reach = prop.Radius + size.magnitude;
            Vector3 centre = prop.Center;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int b = 0; b < options.Beds.BedCount; b++)
            {
                Mover mover = options.Beds.BedMover(b);
                Vector3 position = mover.Position;
                if ((position - centre).sqrMagnitude > reach * reach) continue;
                Quaternion rotation = mover.Rotation;
                Vector3 middle = position + Vector3.up * (size.y * 0.5f);
                int count = Game.PhysicsScene.OverlapBox(middle, size * 0.5f, Hits, rotation, Layers.PropMask, QueryTriggerInteraction.Ignore);
                bool touches = false;
                for (int i = 0; i < count && !touches; i++) touches = PropRef.Of(Hits[i]) == prop;
                if (!touches) continue;
                float distance = (GadgetKit.ClosestPoint(prop, middle) - middle).sqrMagnitude;
                if (distance >= bestDistance - 1e-6f) continue;
                bestDistance = distance;
                best = b;
            }
            return best;
        }

        bool WithinRadius(Prop prop)
        {
            if (options.MinRadius <= 0f && options.MaxRadius >= float.MaxValue * 0.5f) return true;
            var axis = new Vector3(options.RadiusCenter.x, prop.Center.y, options.RadiusCenter.y);
            Vector3 nearest = GadgetKit.ClosestPoint(prop, axis);
            nearest.y = axis.y;
            if ((nearest - axis).magnitude < options.MinRadius) return false;
            Vector3 half = prop.LocalHalfExtents * prop.Scale;
            Vector3 centre = prop.Center;
            Quaternion rotation = prop.Rotation;
            float farthest = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -half.x : half.x, (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                Vector3 point = centre + rotation * offset;
                point.y = axis.y;
                farthest = Mathf.Max(farthest, (point - axis).magnitude);
            }
            return farthest <= options.MaxRadius;
        }

        void Grip(Prop prop, int bed, Vector3 up)
        {
            Mover bedMover = options.Beds.BedMover(bed);
            Vector3 bedPosition = bedMover.Position;
            Quaternion bedRotation = bedMover.Rotation;
            Vector3 position = prop.Position;
            Quaternion rotation = prop.Rotation;
            Vector3 centre = prop.Center;

            Mover mover = prop.BeginDrive();
            if (mover == null) return;

            // Lying flat on the deck: the tilt taken out about its centre, its underside on the deck's top.
            Quaternion flat = Quaternion.FromToRotation(up, up.y >= 0f ? Vector3.up : Vector3.down) * rotation;
            var flatCentre = new Vector3(centre.x, bedPosition.y + prop.LocalHalfExtents.y * prop.Scale, centre.z);
            Vector3 flatPosition = flatCentre - flat * (prop.LocalCenter * prop.Scale);

            Quaternion inverse = Quaternion.Inverse(bedRotation);
            var entry = new Carried
            {
                Prop = prop, Mover = mover, Bed = bed,
                FromPosition = inverse * (position - bedPosition), FromRotation = inverse * rotation,
                ToPosition = inverse * (flatPosition - bedPosition), ToRotation = inverse * flat,
            };
            carried.Add(entry);
            PropCaptured?.Invoke(prop, bed);
            Game.Events.RaiseCarrierCaptured(Event(centre, prop, 0f, 0f, bed));

            // It moves with the bed from this very tick.
            entry.Tick = 1;
            float t = GadgetKit.Smooth(1f / easeTicks);
            options.Beds.BedPose(bed, out Vector3 nextPosition, out Quaternion nextRotation);
            mover.MoveTo(nextPosition + nextRotation * Vector3.Lerp(entry.FromPosition, entry.ToPosition, t),
                nextRotation * Quaternion.Slerp(entry.FromRotation, entry.ToRotation, t));
        }

        void Dropped(Prop prop)
        {
            PropReleased?.Invoke(prop);
            Game.Events.RaiseCarrierDropped(Event(prop.Removed ? Vector3.zero : prop.Center, prop));
        }

        public override void Reset()
        {
            base.Reset();
            for (int i = carried.Count - 1; i >= 0; i--) Release(carried[i].Prop);
        }
    }
}
