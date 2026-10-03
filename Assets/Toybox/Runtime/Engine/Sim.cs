using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>Constants and helpers shared by everything that runs inside the manually ticked simulation.</summary>
    public static class Sim
    {
        /// <summary>Fixed simulation step in seconds.</summary>
        public const float Dt = 1f / 60f;

        /// <summary>
        /// Destroys an object so that it is gone from physics and from queries before this call returns,
        /// in Play Mode and outside it.
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
            // Object.Destroy is deferred to the end of the frame, but a level can be torn down and rebuilt
            // and ticked within one frame; deactivating removes the colliders from the physics scene now.
            if (obj is GameObject go)
            {
                go.SetActive(false);
                go.transform.SetParent(null, false);
            }
            Object.Destroy(obj);
        }
    }
}
