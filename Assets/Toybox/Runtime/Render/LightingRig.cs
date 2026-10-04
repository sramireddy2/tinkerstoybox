using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// The numbers of the lighting rig (ART_BIBLE 5.1) for one room: what <see cref="LightingRig"/> pushes
    /// into the sun and the global uniforms. Colours are linear and already multiplied by their
    /// intensity, exactly as the shaders receive them. Pure arithmetic on the environment descriptor.
    /// </summary>
    public struct LightingValues
    {
        public const float AmbientSky = 0.45f, AmbientEquator = 0.42f, AmbientGround = 0.40f;
        public const float NightAmbientSky = 0.34f, NightAmbientEquator = 0.30f, NightAmbientGround = 0.35f;
        public const float KickGain = 0.20f, KickAzimuthOffset = 145f, KickElevation = 20f;
        public const float EnvCeilGain = 1.6f, EnvFloorGain = 0.7f;
        public const float HazeStart = 60f, HazeEnd = 150f, HazeMax = 0.5f;

        public Color SunColor;          // sRGB, for Light.color
        public float SunIntensity;
        public Quaternion SunRotation;
        public Vector4 AmbSky, AmbEquator, AmbGround;
        public Vector4 KickDir, KickColor;
        public Vector4 WinDir, WinRight, WinUp, GlintColor;
        public Vector4 EnvCeil, EnvWall, EnvFloor;
        public float ToyGlowGain;
        public Vector4 HazeColor, HazeParams;
        public Vector4 WinO, WinU, WinV, WinN;
        public float WinMullion;
        public Vector4 PatchColor;
        public Vector4 ShellMin, ShellMax;
        /// <summary>The camera's clear colour: the haze the far walls dissolve into (sRGB).</summary>
        public Color Background;

        /// <summary>The rig for the room a level stands in. A level without a room keeps the sun and the ambient, and has no patch.</summary>
        public static LightingValues For(EnvironmentDescriptor env)
        {
            EnvironmentPreset preset = env.Preset;
            Dip dip = preset.Dip;
            bool night = preset.Night;
            Color white = Color.white;
            Color light = Palette.Lin(dip.Light), mid = Palette.Lin(dip.Mid), deep = Palette.Lin(dip.Deep);

            var v = new LightingValues
            {
                SunColor = preset.SunColor,
                SunIntensity = preset.SunIntensity,
                SunRotation = env.SunRotation,
                ToyGlowGain = preset.ToyGlowGain,
                WinMullion = EnvironmentDescriptor.WindowMullion,
                Background = dip.Haze,
            };

            if (night)
            {
                Color sky = Palette.Lin(Palette.NightAmbient);
                v.AmbSky = Rgb(sky, NightAmbientSky);
                v.AmbEquator = Rgb(Color.LerpUnclamped(sky, light, 0.5f), NightAmbientEquator);
                v.AmbGround = Rgb(deep, NightAmbientGround);
            }
            else
            {
                v.AmbSky = Rgb(white, AmbientSky);
                v.AmbEquator = Rgb(Color.LerpUnclamped(white, light, 0.5f), AmbientEquator);
                v.AmbGround = Rgb(deep, AmbientGround);
            }

            // The kicker: unshadowed, from behind and across the sun, low.
            v.KickDir = EnvironmentSolver.SunDirection(KickElevation, env.SunAzimuth + KickAzimuthOffset);
            v.KickColor = Rgb(light, KickGain);

            v.WinDir = env.GlintDir;
            v.WinRight = env.GlintRight;
            v.WinUp = env.GlintUp;
            v.GlintColor = Rgb(Palette.Lin(night ? Palette.Moon : Palette.GlintDay), 1f);
            v.EnvCeil = Rgb(white, EnvCeilGain);
            v.EnvWall = Rgb(light, 1f);
            v.EnvFloor = Rgb(mid, EnvFloorGain);

            v.HazeColor = Rgb(Palette.Lin(dip.Haze), 1f);
            v.HazeParams = new Vector4(preset.HazeDensity, env.GroundY + HazeStart, env.GroundY + HazeEnd, HazeMax);

            v.WinO = Point(env.WindowCenter);
            v.WinU = env.WindowU;
            v.WinV = env.WindowV;
            v.WinN = env.WindowNormal;
            v.PatchColor = env.HasRoom ? Rgb(Palette.Lin(preset.PatchColor), preset.PatchGain) : Vector4.zero;
            if (env.HasRoom)
            {
                v.ShellMin = Point(env.ShellMin);
                v.ShellMax = Point(env.ShellMax);
            }
            else
            {
                // No shell: put its planes out of reach of the corner gradient.
                v.ShellMin = new Vector4(-1e5f, -1e5f, -1e5f, 1f);
                v.ShellMax = new Vector4(1e5f, 1e5f, 1e5f, 1f);
            }
            return v;
        }

        static Vector4 Rgb(Color linear, float gain) => new Vector4(linear.r * gain, linear.g * gain, linear.b * gain, 1f);
        static Vector4 Point(Vector3 p) => new Vector4(p.x, p.y, p.z, 1f);
    }

    /// <summary>
    /// The lighting rig (ART_BIBLE 5): one real light - the sun, the only shadow caster - and everything
    /// else as global uniforms, which keeps the rig identical in a Shots capture and in the Web build.
    /// On every level load it reads <c>game.Environment</c> and sets the sun, the ambient and kicker
    /// colours, the glint basis and StudioEnv bands the toys reflect, the haze, the window patch and the
    /// shell's box; it mirrors the ambient into RenderSettings for anything stock, switches skybox and
    /// fog off, and clears the camera to the haze colour.
    ///
    /// These globals are this presenter's and nobody else sets them: _AmbSky, _AmbEquator, _AmbGround,
    /// _KickDir, _KickColor, _WinDir, _WinRight, _WinUp, _GlintColor, _EnvCeil, _EnvWall, _EnvFloor,
    /// _ToyGlowGain, _HazeColor, _HazeParams, _WinO, _WinU, _WinV, _WinN, _WinMullion, _PatchColor,
    /// _ShellMin, _ShellMax.
    /// </summary>
    [Presenter(100, ProvidesLook = true)]
    public sealed class LightingRig : IPresenter
    {
        static readonly int AmbSkyId = Shader.PropertyToID("_AmbSky");
        static readonly int AmbEquatorId = Shader.PropertyToID("_AmbEquator");
        static readonly int AmbGroundId = Shader.PropertyToID("_AmbGround");
        static readonly int KickDirId = Shader.PropertyToID("_KickDir");
        static readonly int KickColorId = Shader.PropertyToID("_KickColor");
        static readonly int WinDirId = Shader.PropertyToID("_WinDir");
        static readonly int WinRightId = Shader.PropertyToID("_WinRight");
        static readonly int WinUpId = Shader.PropertyToID("_WinUp");
        static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
        static readonly int EnvCeilId = Shader.PropertyToID("_EnvCeil");
        static readonly int EnvWallId = Shader.PropertyToID("_EnvWall");
        static readonly int EnvFloorId = Shader.PropertyToID("_EnvFloor");
        static readonly int ToyGlowGainId = Shader.PropertyToID("_ToyGlowGain");
        static readonly int HazeColorId = Shader.PropertyToID("_HazeColor");
        static readonly int HazeParamsId = Shader.PropertyToID("_HazeParams");
        static readonly int WinOId = Shader.PropertyToID("_WinO");
        static readonly int WinUId = Shader.PropertyToID("_WinU");
        static readonly int WinVId = Shader.PropertyToID("_WinV");
        static readonly int WinNId = Shader.PropertyToID("_WinN");
        static readonly int WinMullionId = Shader.PropertyToID("_WinMullion");
        static readonly int PatchColorId = Shader.PropertyToID("_PatchColor");
        static readonly int ShellMinId = Shader.PropertyToID("_ShellMin");
        static readonly int ShellMaxId = Shader.PropertyToID("_ShellMax");

        /// <summary>light.shadowNearPlane (ART_BIBLE 5.3).</summary>
        public const float ShadowNearPlane = 0.2f;

        Game game;
        PresentationContext context;
        GameObject sunObject;
        SceneLighting previous;
        CameraClearFlags previousClear;
        Color previousBackground;
        bool attached;

        /// <summary>The one Light of the game.</summary>
        public Light Sun { get; private set; }
        /// <summary>What was applied last.</summary>
        public LightingValues Values { get; private set; }

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;

            sunObject = new GameObject("Sun") { hideFlags = HideFlags.DontSave };
            sunObject.transform.SetParent(context.Root, false);
            Sun = sunObject.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 1f;
            Sun.shadowNearPlane = ShadowNearPlane;
            // Bias comes from the pipeline asset (UniversalAdditionalLightData.usePipelineSettings is the default).

            previous = SceneLighting.Capture();
            if (context.Camera != null)
            {
                previousClear = context.Camera.clearFlags;
                previousBackground = context.Camera.backgroundColor;
            }
            attached = true;

            game.Events.LevelLoaded += OnLevelLoaded;
            Apply();
        }

        public void Frame(float dt, float alpha) { }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelLoaded;
            previous.Restore();
            if (context.Camera != null)
            {
                context.Camera.clearFlags = previousClear;
                context.Camera.backgroundColor = previousBackground;
            }
            // Nothing of the room may leak into whatever renders next with these shaders.
            Shader.SetGlobalVector(PatchColorId, Vector4.zero);
            if (sunObject != null) Sim.Destroy(sunObject);
            sunObject = null;
            Sun = null;
        }

        void OnLevelLoaded(LevelEvent e) => Apply();

        /// <summary>Reads the loaded level's room and sets the rig. Without a level: the sunny-rug light and no room.</summary>
        public void Apply()
        {
            EnvironmentDescriptor env = game.Environment ?? EnvironmentSolver.Solve(EnvironmentPreset.None, new Bounds(Vector3.zero, Vector3.zero), 0f);
            LightingValues v = LightingValues.For(env);
            Values = v;

            if (Sun != null)
            {
                Sun.color = v.SunColor;
                Sun.intensity = v.SunIntensity;
                Sun.transform.rotation = v.SunRotation;
            }

            Shader.SetGlobalVector(AmbSkyId, v.AmbSky);
            Shader.SetGlobalVector(AmbEquatorId, v.AmbEquator);
            Shader.SetGlobalVector(AmbGroundId, v.AmbGround);
            Shader.SetGlobalVector(KickDirId, v.KickDir);
            Shader.SetGlobalVector(KickColorId, v.KickColor);
            Shader.SetGlobalVector(WinDirId, v.WinDir);
            Shader.SetGlobalVector(WinRightId, v.WinRight);
            Shader.SetGlobalVector(WinUpId, v.WinUp);
            Shader.SetGlobalVector(GlintColorId, v.GlintColor);
            Shader.SetGlobalVector(EnvCeilId, v.EnvCeil);
            Shader.SetGlobalVector(EnvWallId, v.EnvWall);
            Shader.SetGlobalVector(EnvFloorId, v.EnvFloor);
            Shader.SetGlobalFloat(ToyGlowGainId, v.ToyGlowGain);
            Shader.SetGlobalVector(HazeColorId, v.HazeColor);
            Shader.SetGlobalVector(HazeParamsId, v.HazeParams);
            Shader.SetGlobalVector(WinOId, v.WinO);
            Shader.SetGlobalVector(WinUId, v.WinU);
            Shader.SetGlobalVector(WinVId, v.WinV);
            Shader.SetGlobalVector(WinNId, v.WinN);
            Shader.SetGlobalFloat(WinMullionId, v.WinMullion);
            Shader.SetGlobalVector(PatchColorId, v.PatchColor);
            Shader.SetGlobalVector(ShellMinId, v.ShellMin);
            Shader.SetGlobalVector(ShellMaxId, v.ShellMax);

            // The same ambient for anything stock (RenderSettings takes sRGB colours). No skybox, no fog:
            // the sky is a card outside the window and haze is computed in RoomLit only.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Srgb(v.AmbSky);
            RenderSettings.ambientEquatorColor = Srgb(v.AmbEquator);
            RenderSettings.ambientGroundColor = Srgb(v.AmbGround);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            if (context.Camera != null)
            {
                context.Camera.clearFlags = CameraClearFlags.SolidColor;
                context.Camera.backgroundColor = v.Background;
            }
        }

        static Color Srgb(Vector4 linear) => Palette.Srgb(new Color(linear.x, linear.y, linear.z, 1f));

        /// <summary>The scene-wide lighting settings this rig overrides (they belong to the active scene).</summary>
        readonly struct SceneLighting
        {
            readonly AmbientMode mode;
            readonly Color sky, equator, ground;
            readonly Material skybox;
            readonly bool fog;

            SceneLighting(AmbientMode mode, Color sky, Color equator, Color ground, Material skybox, bool fog)
            {
                this.mode = mode;
                this.sky = sky;
                this.equator = equator;
                this.ground = ground;
                this.skybox = skybox;
                this.fog = fog;
            }

            public static SceneLighting Capture() => new SceneLighting(RenderSettings.ambientMode, RenderSettings.ambientSkyColor,
                RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.skybox, RenderSettings.fog);

            public void Restore()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.skybox = skybox;
                RenderSettings.fog = fog;
            }
        }
    }
}
