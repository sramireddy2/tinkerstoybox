using System;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.Render
{
    /// <summary>
    /// The first-person camera and a plain, temporary lighting setup: one directional light with soft
    /// shadows, gradient ambient light and a solid background colour. A later milestone replaces the look;
    /// the seam is that this class only reads Game state and subscribes to Game events.
    ///
    /// The rig's objects live under game.Root, which keeps them in the simulation's scene. In Play Mode
    /// that is an ordinary scene, but outside it (the screenshot tool, tests) it is a preview scene, which
    /// only a camera that is in it and bound to it (Camera.scene) will render.
    ///
    /// It is not a MonoBehaviour: whoever owns the Game calls <see cref="Apply()"/> after stepping it.
    /// </summary>
    public sealed class CameraRig : IDisposable
    {
        /// <summary>Vertical, in degrees: about 91 horizontally on a 16:9 screen. Wider than this stretches props near the edges.</summary>
        public const float FieldOfView = 60f;
        /// <summary>Near plane per unit of player scale. Small, because a held prop can come within 0.35 of the eye.</summary>
        public const float NearPlane = 0.05f;
        /// <summary>The play space is within +-150 on every axis, so nothing is farther away than this.</summary>
        public const float FarPlane = 600f;

        public static readonly Color Background = new Color(0.60f, 0.78f, 0.93f);
        static readonly Color SunColor = new Color(1.00f, 0.96f, 0.88f);
        const float SunIntensity = 1.05f;
        static readonly Vector3 SunAngles = new Vector3(52f, -38f, 0f);
        static readonly Color AmbientSky = new Color(0.72f, 0.80f, 0.95f);
        static readonly Color AmbientEquator = new Color(0.62f, 0.60f, 0.66f);
        static readonly Color AmbientGround = new Color(0.47f, 0.42f, 0.40f);

        readonly Game game;
        readonly Lighting previousLighting;
        bool disposed;

        /// <summary>Parent of the rig's own objects, a child of game.Root.</summary>
        public GameObject Root { get; }
        public Camera Camera { get; }
        public Light Sun { get; }

        CameraRig(Game game)
        {
            this.game = game;

            Root = new GameObject("Camera Rig") { hideFlags = HideFlags.DontSave };
            Root.transform.SetParent(game.Root.transform, false);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(Root.transform, false);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Background;
            Camera.fieldOfView = FieldOfView;
            Camera.nearClipPlane = NearPlane;
            Camera.farClipPlane = FarPlane;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            UniversalAdditionalCameraData cameraData = Camera.GetUniversalAdditionalCameraData();
            cameraData.renderShadows = true;
            cameraData.renderPostProcessing = false;
            // Audio only exists while playing; a listener in an editor preview scene would just be noise.
            if (Application.isPlaying) cameraObject.AddComponent<AudioListener>();

            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(Root.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(SunAngles);
            Sun = sunObject.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = SunColor;
            Sun.intensity = SunIntensity;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;

            // Ambient light and fog belong to the active scene, not to the camera; put them back on Dispose.
            previousLighting = Lighting.Capture();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.fog = false;

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
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            game.Events.LevelLoaded -= OnLevelLoaded;
            previousLighting.Restore();
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

        /// <summary>The scene-wide lighting settings the rig overrides.</summary>
        readonly struct Lighting
        {
            readonly AmbientMode mode;
            readonly Color sky, equator, ground;
            readonly bool fog;

            Lighting(AmbientMode mode, Color sky, Color equator, Color ground, bool fog)
            {
                this.mode = mode;
                this.sky = sky;
                this.equator = equator;
                this.ground = ground;
                this.fog = fog;
            }

            public static Lighting Capture() => new Lighting(RenderSettings.ambientMode, RenderSettings.ambientSkyColor,
                RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.fog);

            public void Restore()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.fog = fog;
            }
        }
    }
}
