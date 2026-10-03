using UnityEngine;

namespace Toybox.Engine
{
    public enum VolumeShape
    {
        Box,
        Sphere,
    }

    /// <summary>A box or sphere, centered on whatever pose it is used with. Triggers, checkpoints and exits are made of these.</summary>
    public readonly struct Volume
    {
        public readonly VolumeShape Shape;
        /// <summary>Full size of a box (unused for spheres).</summary>
        public readonly Vector3 Size;
        public readonly float Radius;

        Volume(VolumeShape shape, Vector3 size, float radius)
        {
            Shape = shape;
            Size = size;
            Radius = radius;
        }

        public static Volume Box(Vector3 size) => new Volume(VolumeShape.Box, size, 0f);
        public static Volume Box(float x, float y, float z) => Box(new Vector3(x, y, z));
        public static Volume Sphere(float radius) => new Volume(VolumeShape.Sphere, Vector3.zero, radius);

        /// <summary>Distance from the center down to the lowest point (for an upright volume).</summary>
        public float HalfHeight => Shape == VolumeShape.Box ? Size.y * 0.5f : Radius;

        /// <summary>Colliders on the given layers touching the volume. Returns how many were written to results.</summary>
        public int Overlap(PhysicsScene physics, Vector3 position, Quaternion rotation, Collider[] results, int layerMask)
        {
            return Shape == VolumeShape.Box
                ? physics.OverlapBox(position, Size * 0.5f, results, rotation, layerMask, QueryTriggerInteraction.Ignore)
                : physics.OverlapSphere(position, Radius, results, layerMask, QueryTriggerInteraction.Ignore);
        }

        public bool Contains(Vector3 position, Quaternion rotation, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(rotation) * (point - position);
            if (Shape == VolumeShape.Sphere) return local.sqrMagnitude <= Radius * Radius;
            Vector3 half = Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
