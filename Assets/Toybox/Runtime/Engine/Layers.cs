using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>Physics layer indices and masks. The names are assigned by ProjectSetup; keep both in sync.</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int Prop = 8;
        public const int Player = 9;
        public const int Held = 10;
        public const int Trigger = 11;

        public const int DefaultMask = 1 << Default;
        public const int PropMask = 1 << Prop;
        public const int PlayerMask = 1 << Player;
        public const int HeldMask = 1 << Held;
        public const int TriggerMask = 1 << Trigger;

        /// <summary>Everything that blocks movement, grabbing and the held-prop projection: world + props.</summary>
        public const int SolidMask = DefaultMask | PropMask;

        /// <summary>What a trigger volume can sense.</summary>
        public const int SensedMask = PropMask | PlayerMask;

        /// <summary>World, props and player collide with each other; Held and Trigger collide with nothing.</summary>
        public static void Configure()
        {
            for (int other = 0; other < 32; other++)
            {
                Physics.IgnoreLayerCollision(Held, other, true);
                Physics.IgnoreLayerCollision(Trigger, other, true);
            }
            int[] solid = { Default, Prop, Player };
            for (int a = 0; a < solid.Length; a++)
                for (int b = a; b < solid.Length; b++)
                    Physics.IgnoreLayerCollision(solid[a], solid[b], false);
        }

        /// <summary>Puts a whole hierarchy on one layer.</summary>
        public static void SetRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetRecursively(root.GetChild(i), layer);
        }
    }
}
