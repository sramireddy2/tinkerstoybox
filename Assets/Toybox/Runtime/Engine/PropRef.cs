using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>Passive tag on a prop's root GameObject. It has no behaviour; it only leads back to the Prop.</summary>
    [DisallowMultipleComponent]
    public sealed class PropRef : MonoBehaviour
    {
        // A property, not a field: this is runtime wiring and must stay out of Unity's serialization.
        public Prop Prop { get; set; }

        /// <summary>The prop a collider belongs to (any child collider of the toy), or null.</summary>
        public static Prop Of(Collider collider)
        {
            if (collider == null) return null;
            Rigidbody body = collider.attachedRigidbody;
            PropRef reference = body != null
                ? body.GetComponent<PropRef>()
                : collider.GetComponentInParent<PropRef>();
            return reference != null ? reference.Prop : null;
        }
    }
}
