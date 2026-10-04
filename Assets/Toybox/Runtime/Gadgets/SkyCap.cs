using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>
    /// The lid of a level that has no ceiling of its own: an invisible static slab on the Default layer.
    /// A toy held against the open sky grows until its clamp; under a cap it stops at the cap, and nothing
    /// can be projected over the level's walls. Not ticked.
    /// </summary>
    public static class SkyCap
    {
        /// <summary>
        /// Adds a slab over the rectangle xMin..xMax, zMin..zMax whose underside is at <paramref name="y"/>.
        /// It has a collider and no renderer.
        /// </summary>
        public static GameObject Add(LevelContext ctx, float xMin, float xMax, float zMin, float zMax, float y, float thickness = 1f)
        {
            var size = new Vector3(Mathf.Abs(xMax - xMin), Mathf.Max(0.3f, thickness), Mathf.Abs(zMax - zMin));
            var cap = new GameObject("SkyCap") { layer = Layers.Default };
            cap.AddComponent<BoxCollider>().size = size;
            return ctx.AddStatic(cap, new Vector3((xMin + xMax) * 0.5f, y + size.y * 0.5f, (zMin + zMax) * 0.5f));
        }
    }
}
