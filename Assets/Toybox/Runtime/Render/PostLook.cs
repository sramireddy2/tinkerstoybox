using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Volume = UnityEngine.Rendering.Volume;

namespace Toybox.Render
{
    /// <summary>
    /// Post-processing of the look (ART_BIBLE 7): the camera's post flags, the one global Volume whose
    /// profile is an instance of Resources/Volumes/ToyboxPost.asset (bloom, neutral tonemapping, colour
    /// adjustments, vignette) adjusted per environment preset and per tier, the MacroBand lens blur, and
    /// the exposure flashes of section 9 (the shutter on release, the white flash of a completed level).
    ///
    /// Per tier (7.3): Low has no bloom and no lens blur and uses FXAA instead of MSAA; Medium and High
    /// have both, High with high-quality bloom filtering and one more bloom level. The lens blur's radius
    /// follows <c>Settings.LensBlur</c> and the height of the picture; while the game is paused it ramps to
    /// a full-screen blur (10.5).
    ///
    /// Nothing here is saved: the profile is an instance, the lens blur's numbers are shader globals, and
    /// the one change made to a shared asset (the MacroBand feature is switched off on Low) is taken back
    /// by Dispose.
    /// </summary>
    [Presenter(20, ProvidesLook = true)]
    public sealed class PostLook : IPresenter
    {
        /// <summary>The volume profile asset, under Resources (7.5: never built from nothing at runtime).</summary>
        public const string ProfileResource = "Volumes/ToyboxPost";
        /// <summary>The name of the stock full-screen feature on ToyboxRenderer.asset.</summary>
        public const string MacroBandFeature = "MacroBand";

        /// <summary>Release (9.4): the shutter, +0.12 EV decaying to 0 over 90 ms.</summary>
        public const float ReleaseFlashEv = 0.12f, ReleaseFlashSeconds = 0.09f;
        /// <summary>Level complete (9.7): +1.5 EV for one frame, fading over 300 ms.</summary>
        public const float CompleteFlashEv = 1.5f, CompleteFlashSeconds = 0.3f;
        /// <summary>Pause (10.5): a full-screen blur of 14 px (at 1080p), reached in 180 ms.</summary>
        public const float PauseBlurPx = 14f, PauseBlurSeconds = 0.18f;
        /// <summary>The widest disc the shader samples; the pause blur uses all of them.</summary>
        public const int MaxTaps = 16;
        /// <summary>
        /// The tonemapping mode of the profile. ART_BIBLE 7.2 asks for Neutral and, in 5.1, for a sunlit
        /// floor within 4% of its authored colour; URP's Neutral curve cannot do both - it bends everything
        /// above middle grey (linear 0.5 comes out as 0.42, 1.0 as 0.63) and showed the Mint floor 13%
        /// darker and greyer, at any exposure. Without a curve the palette is shown as authored and
        /// whatever exceeds 1 (glints, signals) clips to white under its bloom, which is what 5.1 asks for.
        /// </summary>
        public const TonemappingMode Tonemap = TonemappingMode.None;
        /// <summary>
        /// Added to every preset's exposure (ART_BIBLE 6.4 gives +0.2 EV for all six). That figure was
        /// written for the Neutral curve, which darkens; without a curve +0.2 EV puts every sunlit
        /// "light" top inside the window patch above 1, where it clips to white (5.1: "adjust
        /// postExposure first"). With the trim a sunlit mid floor shows at its authored colour and a light
        /// top in the patch keeps its hue.
        /// </summary>
        public const float ExposureTrim = -0.2f;

        static readonly int RadiusPxId = Shader.PropertyToID("_RadiusPx");
        static readonly int TapsId = Shader.PropertyToID("_Taps");
        static readonly int FullId = Shader.PropertyToID("_Full");

        Game game;
        PresentationContext context;
        bool attached;
        GameObject volumeObject;
        UniversalAdditionalCameraData cameraData;
        CameraFlags previous;
        FullScreenPassRendererFeature macroBand;
        float flashEv, flashSeconds, flashAge = float.MaxValue;
        bool flashFresh;
        float baseExposure, appliedExposure = float.NaN;
        float full;

        /// <summary>The global volume; its <c>profile</c> is this presenter's own instance.</summary>
        public Volume Volume { get; private set; }
        public VolumeProfile Profile { get; private set; }
        public Bloom Bloom { get; private set; }
        public Tonemapping Tonemapping { get; private set; }
        public ColorAdjustments ColorAdjustments { get; private set; }
        public Vignette Vignette { get; private set; }

        /// <summary>Post exposure in EV as last written: the preset's plus the flash.</summary>
        public float Exposure => float.IsNaN(appliedExposure) ? baseExposure : appliedExposure;
        /// <summary>What the flash adds right now, in EV.</summary>
        public float FlashEv => FlashAt(flashAge);
        /// <summary>Set false if somebody else shows the level-complete flash.</summary>
        public bool CompleteFlash { get; set; } = true;

        /// <summary>
        /// How much of the picture the lens blur covers beyond its band: 0 in play, 1 a full-screen blur.
        /// It follows <see cref="FullBlurTarget"/> over <see cref="PauseBlurSeconds"/>.
        /// </summary>
        public float FullBlur => full;
        /// <summary>Where <see cref="FullBlur"/> is heading; null (the default) for "1 while paused, else 0".</summary>
        public float? FullBlurTarget { get; set; }
        /// <summary>The full-screen blur has arrived (or the tier has none): the pause card may capture the frame.</summary>
        public bool FullBlurReached => !TierSpec.Of(context.Quality).MacroBand || Mathf.Abs(full - Target()) < 0.001f;

        /// <summary>The lens blur as it stands this frame: radius in pixels at 1080p, taps, and whether the pass runs at all.</summary>
        public float RadiusAt1080 { get; private set; }
        public int Taps { get; private set; }
        public bool MacroBandActive { get; private set; }

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            attached = true;

            Camera camera = context.Camera;
            if (camera != null)
            {
                cameraData = camera.GetUniversalAdditionalCameraData();
                previous = CameraFlags.Capture(camera, cameraData);
                cameraData.renderPostProcessing = context.HasGraphics;
                // Mandatory in the game: pastel gradients band without it. Outside Play Mode it is left off,
                // because URP picks the noise offset at random and a tool's picture must be reproducible.
                cameraData.dithering = Application.isPlaying;
                cameraData.stopNaN = false;
            }

            var shared = Resources.Load<VolumeProfile>(ProfileResource);
            if (shared == null)
            {
                Debug.LogError("[Toybox] Resources/" + ProfileResource + ".asset is missing: run Toybox.EditorTools.ProjectSetup.Run. Post-processing stays at URP's defaults.");
            }
            else
            {
                volumeObject = new GameObject("Post Volume") { hideFlags = HideFlags.DontSave };
                volumeObject.transform.SetParent(context.Root, false);
                Volume = volumeObject.AddComponent<Volume>();
                Volume.isGlobal = true;
                Volume.priority = 10f;
                Volume.sharedProfile = shared;
                // Volume.profile clones the shared profile and its components: edits stay out of the asset.
                Profile = Volume.profile;
                Profile.hideFlags = HideFlags.DontSave;
                foreach (VolumeComponent component in Profile.components) component.hideFlags = HideFlags.DontSave;
                if (Profile.TryGet(out Bloom bloom)) Bloom = bloom;
                if (Profile.TryGet(out Tonemapping tonemapping)) Tonemapping = tonemapping;
                if (Profile.TryGet(out ColorAdjustments adjustments)) ColorAdjustments = adjustments;
                if (Profile.TryGet(out Vignette vignette)) Vignette = vignette;
            }

            macroBand = Quality.RendererFeature<FullScreenPassRendererFeature>(MacroBandFeature);

            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.PropDropped += OnPropDropped;
            game.Events.LevelCompleted += OnLevelCompleted;
            context.QualityChanged += OnQualityChanged;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;

            ApplyTier(context.Quality);
            ApplyPreset();
            Frame(0f, 1f);
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached) return;

            // ---- Exposure: the preset's, plus whatever flash is under way --------------------------------
            // The frame a flash starts on shows it in full; it fades from the next one on.
            if (flashFresh) flashFresh = false;
            else if (flashAge < flashSeconds) flashAge += dt;
            float exposure = baseExposure + FlashAt(flashAge);
            if (ColorAdjustments != null && exposure != appliedExposure)
            {
                ColorAdjustments.postExposure.value = exposure;
                appliedExposure = exposure;
            }

            // ---- Lens blur -------------------------------------------------------------------------------
            TierSpec tier = TierSpec.Of(context.Quality);
            float target = tier.MacroBand ? Target() : 0f;
            full = tier.MacroBand ? Mathf.MoveTowards(full, target, dt / PauseBlurSeconds) : 0f;
            float lens = tier.MacroRadiusPx * Settings.LensBlur;
            RadiusAt1080 = Mathf.Lerp(lens, PauseBlurPx, full);
            Taps = full > 0f ? MaxTaps : tier.MacroTaps;
            // With the strength at zero and no pause there is nothing to blur: skip the pass and its copy.
            bool active = tier.MacroBand && macroBand != null && RadiusAt1080 > 0.01f;
            if (macroBand != null && macroBand.isActive != active) macroBand.SetActive(active);
            MacroBandActive = active;
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.PropDropped -= OnPropDropped;
            game.Events.LevelCompleted -= OnLevelCompleted;
            context.QualityChanged -= OnQualityChanged;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

            // The renderer asset is shared project state: leave the feature on, as the setup step wrote it,
            // and drawing nothing.
            if (macroBand != null && !macroBand.isActive) macroBand.SetActive(true);
            macroBand = null;
            Shader.SetGlobalFloat(RadiusPxId, 0f);
            Shader.SetGlobalFloat(TapsId, 0f);
            Shader.SetGlobalFloat(FullId, 0f);

            if (Profile != null)
            {
                foreach (VolumeComponent component in Profile.components)
                    if (component != null) Sim.Destroy(component);
                Sim.Destroy(Profile);
            }
            if (volumeObject != null) Sim.Destroy(volumeObject);
            Profile = null;
            Volume = null;
            volumeObject = null;
            Bloom = null;
            Tonemapping = null;
            ColorAdjustments = null;
            Vignette = null;

            if (cameraData != null && context.Camera != null) previous.Restore(context.Camera, cameraData);
            cameraData = null;
        }

        /// <summary>
        /// An exposure flash: <paramref name="ev"/> stops brighter at once, fading to nothing over
        /// <paramref name="seconds"/>. A flash under way is replaced if the new one is brighter than what
        /// is left of it. "Reduce motion" turns flashes off.
        /// </summary>
        public void Flash(float ev, float seconds)
        {
            if (!attached || Settings.ReduceMotion || !(seconds > 0f) || !(ev > 0f)) return;
            if (ev < FlashAt(flashAge)) return;
            flashEv = ev;
            flashSeconds = seconds;
            flashAge = 0f;
            flashFresh = true;
        }

        float FlashAt(float age) => age < flashSeconds ? flashEv * (1f - Mathf.Clamp01(age / flashSeconds)) : 0f;

        float Target() => Mathf.Clamp01(FullBlurTarget ?? (context.State == FlowState.Paused ? 1f : 0f));

        // What differs between the tiers (7.3): bloom, its filtering and pyramid depth, and the camera's
        // anti-aliasing. The lens blur follows in Frame.
        void ApplyTier(QualityTier quality)
        {
            TierSpec tier = TierSpec.Of(quality);
            if (Bloom != null)
            {
                Bloom.active = tier.Bloom;
                Bloom.highQualityFiltering.value = tier.BloomHighQuality;
                Bloom.maxIterations.value = tier.BloomIterations;
            }
            if (cameraData != null)
            {
                cameraData.antialiasing = tier.Fxaa ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
                // Low has no MSAA; a tool's render target must not bring its own.
                context.Camera.allowMSAA = tier.Msaa > 1;
            }
        }

        // What differs between the rooms (6.4): bloom intensity and exposure.
        void ApplyPreset()
        {
            EnvironmentPreset preset = game.Environment != null ? game.Environment.Preset : EnvironmentPreset.None;
            if (Bloom != null) Bloom.intensity.value = preset.Bloom;
            baseExposure = preset.Exposure + ExposureTrim;
        }

        void OnLevelLoaded(LevelEvent e)
        {
            ApplyPreset();
            // A flash belongs to the level it happened in.
            flashAge = float.MaxValue;
        }

        void OnQualityChanged(QualityTier tier) => ApplyTier(tier);

        void OnPropDropped(PropHoldEvent e)
        {
            // Also raised when a held prop is removed or respawned: nothing was put down then.
            if (e.Prop == null || e.Prop.Removed) return;
            Flash(ReleaseFlashEv, ReleaseFlashSeconds);
        }

        void OnLevelCompleted(LevelEvent e)
        {
            if (CompleteFlash) Flash(CompleteFlashEv, CompleteFlashSeconds);
        }

        // The blur radius is in pixels of the target being rendered, which only the camera about to render
        // knows (a tool renders the same camera into textures of any size).
        void OnBeginCamera(ScriptableRenderContext renderContext, Camera camera)
        {
            float radius = 0f;
            if (MacroBandActive && camera.cameraType == CameraType.Game)
            {
                float scale = 1f;
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline && Mathf.Abs(1f - pipeline.renderScale) >= Quality.ScaleThreshold)
                    scale = pipeline.renderScale;
                radius = RadiusAt1080 * camera.pixelHeight * scale / 1080f;
            }
            Shader.SetGlobalFloat(RadiusPxId, radius);
            Shader.SetGlobalFloat(TapsId, Taps);
            Shader.SetGlobalFloat(FullId, full);
        }

        /// <summary>The camera settings this presenter changes.</summary>
        readonly struct CameraFlags
        {
            readonly bool postProcessing, dithering, stopNaN, msaa;
            readonly AntialiasingMode antialiasing;

            CameraFlags(bool postProcessing, bool dithering, bool stopNaN, bool msaa, AntialiasingMode antialiasing)
            {
                this.postProcessing = postProcessing;
                this.dithering = dithering;
                this.stopNaN = stopNaN;
                this.msaa = msaa;
                this.antialiasing = antialiasing;
            }

            public static CameraFlags Capture(Camera camera, UniversalAdditionalCameraData data) =>
                new CameraFlags(data.renderPostProcessing, data.dithering, data.stopNaN, camera.allowMSAA, data.antialiasing);

            public void Restore(Camera camera, UniversalAdditionalCameraData data)
            {
                data.renderPostProcessing = postProcessing;
                data.dithering = dithering;
                data.stopNaN = stopNaN;
                data.antialiasing = antialiasing;
                camera.allowMSAA = msaa;
            }
        }
    }
}
