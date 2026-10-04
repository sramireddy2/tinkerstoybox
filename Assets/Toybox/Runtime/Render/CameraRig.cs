using System;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Toybox.Render
{
    /// <summary>
    /// The first-person camera, and nothing else: where it is, where it looks, its field of view and its
    /// depth range. Lighting, background, post-processing and everything else about the picture belong
    /// to presenters, which get the camera through the PresentationContext.
    ///
    /// The rig's objects live under game.Root, which keeps them in the simulation's scene. In Play Mode
    /// that is an ordinary scene, but outside it (the screenshot tool, tests) it is a preview scene, which
    /// only a camera that is in it and bound to it (Camera.scene) will render.
    ///
    /// It is not a MonoBehaviour: whoever owns the Game calls <see cref="Apply()"/> after stepping it
    /// (Presentation.Frame does).
    /// </summary>
    public sealed class CameraRig : IDisposable
    {
        /// <summary>Near plane per unit of player scale. Small, because a held prop can come within 0.35 of the eye.</summary>
        public const float NearPlane = 0.05f;
        /// <summary>The shell is 400 across and the play space within +-150 of it, so nothing is farther away than this.</summary>
        public const float FarPlane = 700f;

        readonly Game game;
        bool disposed;

        /// <summary>Parent of the rig's own objects, a child of game.Root.</summary>
        public GameObject Root { get; }
        public Camera Camera { get; }

        CameraRig(Game game)
        {
            this.game = game;

            Root = new GameObject("Camera Rig") { hideFlags = HideFlags.DontSave };
            Root.transform.SetParent(game.Root.transform, false);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(Root.transform, false);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.fieldOfView = Settings.FieldOfView;
            Camera.nearClipPlane = NearPlane;
            Camera.farClipPlane = FarPlane;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            UniversalAdditionalCameraData cameraData = Camera.GetUniversalAdditionalCameraData();
            cameraData.renderShadows = true;
            cameraData.renderPostProcessing = false;
            // Audio only exists while playing; a listener in an editor preview scene would just be noise.
            if (Application.isPlaying) cameraObject.AddComponent<AudioListener>();

            game.Events.LevelLoaded += OnLevelLoaded;
            BindScene();
            Apply(1f);
        }

        public static CameraRig Create(Game game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            return new CameraRig(game);
        }

        /// <summary>Moves the camera to where the player looks from, between the last two ticks.</summary>
        public void Apply() => Apply(game.Alpha);

        /// <summary>
        /// alpha 0 is the eye one tick ago, 1 the eye now. The simulation's own loop passes Game.Alpha;
        /// tools that tick by hand pass 1. The held prop is drawn on this camera's crosshair: the view
        /// turns with every rendered frame and the eye is interpolated, while the simulation only places
        /// the prop once per tick.
        /// </summary>
        public void Apply(float alpha)
        {
            if (disposed || Camera == null || game.IsDisposed) return;
            Player player = game.Player;
            Vector3 eye = player.EyeAt(alpha);
            Quaternion look = player.LookRotation;
            Camera.transform.SetPositionAndRotation(eye, look);
            game.Grabber.Present(eye, look);
            // A shrunken player sees a world of giants, an enlarged one a toy landscape: scale the depth range along.
            float scale = player.Scale;
            Camera.nearClipPlane = NearPlane * scale;
            Camera.farClipPlane = FarPlane * Mathf.Max(1f, scale);
            Camera.fieldOfView = Settings.FieldOfView;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            game.Events.LevelLoaded -= OnLevelLoaded;
            // Game.Dispose destroys game.Root and the rig with it; then there is nothing left to do here.
            if (Root != null) Sim.Destroy(Root);
        }

        void OnLevelLoaded(LevelEvent e)
        {
            // Every level load moves the simulation into a new scene.
            BindScene();
            Apply(1f);
        }

        void BindScene()
        {
            if (Camera == null) return;
            // In Play Mode the simulation's scene renders like any other; leave the camera unrestricted so
            // it also shows whatever presentation code puts into other scenes.
            if (!Application.isPlaying) Camera.scene = game.Scene;
        }
    }
}
