using UnityEngine;

namespace Toybox.Art
{
    /// <summary>
    /// Passive tag on a toy's root GameObject: what the toy catalog reports about a toy to the render and
    /// audio side (ART_BIBLE 4.1 and section 13, ask 4) - its material recipe, its candy colour and, for
    /// long or ring-shaped toys, up to three pool proxy spheres. It has no behaviour.
    ///
    /// A toy factory calls <see cref="Tag"/> on what it returns. The render side reads it through
    /// <see cref="Of"/> (for a prop: <c>ToyInfo.Of(prop.GameObject)</c>); a toy without a tag is treated as
    /// glossy plastic in <see cref="Palette.Paper"/> with one pool sphere from the prop's bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToyInfo : MonoBehaviour
    {
        static readonly Vector4[] NoProxies = new Vector4[0];

        // Properties, not fields: this is runtime wiring and must stay out of Unity's serialization.
        /// <summary>The recipe of the toy's main material (it decides pool tint, squash and landing sound).</summary>
        public ToyRecipe Recipe { get; set; } = ToyRecipe.GlossyPlastic;
        /// <summary>The toy's candy colour, sRGB.</summary>
        public Color Candy { get; set; } = Palette.Paper;
        /// <summary>
        /// Pool proxy spheres in the toy's own space at scale 1 (xyz centre, w radius), at most three. Empty:
        /// one sphere at the centre of the prop's colliders with 0.8 of its bounding radius.
        /// </summary>
        public Vector4[] PoolProxies { get; set; } = NoProxies;

        /// <summary>The tag of a toy, or null if its factory did not leave one.</summary>
        public static ToyInfo Of(GameObject toy) => toy != null ? toy.GetComponent<ToyInfo>() : null;

        /// <summary>Tags a toy (replacing an earlier tag) and returns the tag.</summary>
        public static ToyInfo Tag(GameObject toy, ToyRecipe recipe, Color candy, params Vector4[] poolProxies)
        {
            if (poolProxies != null && poolProxies.Length > 3)
                throw new System.ArgumentException("A toy has at most three pool proxy spheres.", nameof(poolProxies));
            ToyInfo info = toy.GetComponent<ToyInfo>();
            if (info == null) info = toy.AddComponent<ToyInfo>();
            info.Recipe = recipe ?? ToyRecipe.GlossyPlastic;
            info.Candy = candy;
            info.PoolProxies = poolProxies ?? NoProxies;
            return info;
        }
    }
}
