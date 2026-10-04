using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// The plain look: one directional light with soft shadows, gradient ambient light and a solid
    /// background colour, for stock URP Lit materials. It is the debug look (?plain=1, -toyboxPlain) and
    /// the stand-in for as long as no presenter provides the game's own look.
    /// </summary>
    [Presenter(-1000, ProvidesLook = true, Fallback = true)]
    public sealed class PlainLook : IPresenter
    {
        public static readonly Color Background = new Color(0.60f, 0.78f, 0.93f);
        static readonly Color SunColor = new Color(1.00f, 0.96f, 0.88f);
        const float SunIntensity = 1.05f;
        static readonly Vector3 SunAngles = new Vector3(52f, -38f, 0f);
        static readonly Color AmbientSky = new Color(0.72f, 0.80f, 0.95f);
        static readonly Color AmbientEquator = new Color(0.62f, 0.60f, 0.66f);
        static readonly Color AmbientGround = new Color(0.47f, 0.42f, 0.40f);

        Lighting previousLighting;
        GameObject sunObject;
        bool attached;

        public Light Sun { get; private set; }

        public void Attach(Game game, PresentationContext context)
        {
            sunObject = new GameObject("Plain Sun") { hideFlags = HideFlags.DontSave };
            sunObject.transform.SetParent(context.Root, false);
            sunObject.transform.rotation = Quaternion.Euler(SunAngles);
            Sun = sunObject.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = SunColor;
            Sun.intensity = SunIntensity;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;

            if (context.Camera != null)
            {
                context.Camera.clearFlags = CameraClearFlags.SolidColor;
                context.Camera.backgroundColor = Background;
            }

            // Ambient light and fog belong to the active scene, not to the camera; put them back on Dispose.
            previousLighting = Lighting.Capture();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.fog = false;
            attached = true;
        }

        public void Frame(float dt, float alpha) { }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            previousLighting.Restore();
            if (sunObject != null) Sim.Destroy(sunObject);
            sunObject = null;
            Sun = null;
        }

        /// <summary>The scene-wide lighting settings this look overrides.</summary>
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
