using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// A kinematic body driven by code: call MoveTo once per tick with where it should be after this
    /// step. It pushes dynamic bodies out of the way and reports its velocity, so the player can ride it.
    /// A tick without a MoveTo call means the body is at rest.
    /// </summary>
    public sealed class Mover
    {
        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public Rigidbody Body { get; }

        /// <summary>Linear velocity implied by this tick's MoveTo, in units per second.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Angular velocity implied by this tick's MoveTo, in radians per second (world axes).</summary>
        public Vector3 AngularVelocity { get; private set; }

        // The turn this tick's MoveTo makes, kept as a rotation: see PointVelocity.
        Quaternion turn = Quaternion.identity;
        bool turning;

        public Vector3 Position => Body.position;
        public Quaternion Rotation => Body.rotation;

        /// <summary>
        /// True while the mover's prop is held by the player (only possible for a Kinematic prop that was
        /// made grabbable on purpose): MoveTo does nothing until it is dropped.
        /// </summary>
        public bool Suspended { get; internal set; }

        internal Mover(Rigidbody body)
        {
            Body = body;
            GameObject = body.gameObject;
            Transform = body.transform;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            MoverRef reference = GameObject.GetComponent<MoverRef>();
            if (reference == null) reference = GameObject.AddComponent<MoverRef>();
            reference.Mover = this;
        }

        public void MoveTo(Vector3 position) => MoveTo(position, Body.rotation);

        public void MoveTo(Vector3 position, Quaternion rotation)
        {
            if (Suspended) return;
            Vector3 fromPosition = Body.position;
            Quaternion fromRotation = Body.rotation;
            Velocity = (position - fromPosition) / Sim.Dt;

            Quaternion delta = rotation * Quaternion.Inverse(fromRotation);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f) degrees -= 360f;
            turning = Mathf.Abs(degrees) > 1e-4f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x);
            AngularVelocity = turning ? axis * (degrees * Mathf.Deg2Rad / Sim.Dt) : Vector3.zero;
            turn = turning ? delta : Quaternion.identity;

            Body.MovePosition(position);
            Body.MoveRotation(rotation);
        }

        /// <summary>Jumps to a pose without implying any velocity.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Velocity = Vector3.zero;
            AngularVelocity = Vector3.zero;
            turn = Quaternion.identity;
            turning = false;
            Transform.SetPositionAndRotation(position, rotation);
            Body.position = position;
            Body.rotation = rotation;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Velocity of the point of this body that is currently at worldPoint: where one more step like the
        /// last one takes that point, divided by the step. For a turning body that is the chord of the arc,
        /// not its tangent, so whoever moves with this velocity stays on the circle instead of spiralling out.
        /// </summary>
        public Vector3 PointVelocity(Vector3 worldPoint)
        {
            if (!turning) return Velocity;
            Vector3 arm = worldPoint - Body.position;
            return Velocity + (turn * arm - arm) / Sim.Dt;
        }

        internal void BeginTick()
        {
            Velocity = Vector3.zero;
            AngularVelocity = Vector3.zero;
            turn = Quaternion.identity;
            turning = false;
        }
    }
}
