using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>Passive tag on a kinematic body. It lets the player find the Mover it is standing on.</summary>
    [DisallowMultipleComponent]
    public sealed class MoverRef : MonoBehaviour
    {
        public Mover Mover { get; set; }
    }
}
