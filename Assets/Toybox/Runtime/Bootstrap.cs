using Toybox.Platform;
using UnityEngine;

namespace Toybox
{
    /// <summary>
    /// Entry point of the single scene. Everything else is built from code: this only creates the
    /// GameRunner, which creates the Game, the input, the camera and the HUD.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        void Awake()
        {
            new GameObject("Game Runner").AddComponent<GameRunner>();
        }
    }
}
