using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.Render
{
    /// <summary>
    /// One column of ART_BIBLE 7.3 (quality tiers), 5.3 (shadows) and 12.1 (budgets): everything that
    /// differs between Low, Medium and High, in one place. The setup step writes the URP asset of a tier
    /// from it, the pipeline's presenters apply it at runtime, and the other areas read their own numbers
    /// here (<c>TierSpec.Of(context.Quality).PoolSpheres</c>).
    /// </summary>
    public sealed class TierSpec
    {
        /// <summary>Every tier's first cascade ends this far from the camera, so near shadows look the same everywhere.</summary>
        public const float FirstCascade = 20f;
        /// <summary>Materials alive per level (12.1).</summary>
        public const int MaxMaterials = 40;
        /// <summary>Compiled shader programs, warmed up behind the loading screen (12.2).</summary>
        public const int MaxShaderPrograms = 40;

        public QualityTier Tier { get; private set; }
        /// <summary>The name of the tier's Quality level ("Low", "Medium", "High") and the suffix of its URP asset.</summary>
        public string Name { get; private set; }

        // ---- 7.3 ---------------------------------------------------------------------------------------
        /// <summary>The most pixels the 3D picture is rendered with; a bigger screen is rendered at a lower scale.</summary>
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public int PixelBudget => PixelWidth * PixelHeight;
        public int Msaa { get; private set; }
        /// <summary>FXAA on the camera (Low, which has no MSAA).</summary>
        public bool Fxaa { get; private set; }
        public bool Bloom { get; private set; }
        public bool BloomHighQuality { get; private set; }
        public int BloomIterations { get; private set; }
        public bool MacroBand { get; private set; }
        public int MacroTaps { get; private set; }
        /// <summary>Lens blur radius at the picture's edge in pixels at 1080p, before the player's strength setting.</summary>
        public float MacroRadiusPx { get; private set; }
        /// <summary>Colour pool spheres (<c>_PoolCount</c>).</summary>
        public int PoolSpheres { get; private set; }
        public int DetailSize { get; private set; }
        public int DetailAniso { get; private set; }
        public bool DetailBump { get; private set; }
        public bool GlassBackShell { get; private set; }
        public bool LightShaft { get; private set; }
        public int DustMotes { get; private set; }
        public int Confetti { get; private set; }
        /// <summary>How far the governor lowers the render scale before it drops a tier (7.4).</summary>
        public float ScaleFloor { get; private set; }

        // ---- 5.3 ---------------------------------------------------------------------------------------
        /// <summary><c>mainLightShadowmapResolution</c>.</summary>
        public int ShadowResolution { get; private set; }
        public float ShadowDistance { get; private set; }
        public int Cascades { get; private set; }
        /// <summary><c>cascade2Split</c>; used with two cascades.</summary>
        public float Cascade2Split { get; private set; }
        /// <summary><c>cascade3Split</c>; used with three cascades.</summary>
        public Vector2 Cascade3Split { get; private set; }
        /// <summary>1 Low (4 taps), 2 Medium (9 taps), 3 High (16 taps): URP's SoftShadowQuality.</summary>
        public int SoftShadowQuality { get; private set; }
        /// <summary>The atlas URP allocates: two cascades use half the height.</summary>
        public int ShadowAtlasWidth => ShadowResolution;
        public int ShadowAtlasHeight => Cascades == 2 ? ShadowResolution / 2 : ShadowResolution;

        // ---- 12.1 --------------------------------------------------------------------------------------
        public int MaxDrawsMain { get; private set; }
        public int MaxDrawsShadow { get; private set; }
        public int MaxDrawsFrame { get; private set; }
        public int MaxTriangles { get; private set; }
        public int MaxParticles { get; private set; }
        /// <summary>Render-target memory, in megabytes.</summary>
        public int MaxRenderTargetMb { get; private set; }
        /// <summary>GPU frame time on the tier's reference machine, in milliseconds.</summary>
        public float GpuMs { get; private set; }

        TierSpec() { }

        public override string ToString() => Name;

        public static readonly TierSpec Low = new TierSpec
        {
            Tier = QualityTier.Low, Name = "Low",
            PixelWidth = 1536, PixelHeight = 864, Msaa = 1, Fxaa = true,
            Bloom = false, BloomHighQuality = false, BloomIterations = 4,
            MacroBand = false, MacroTaps = 0, MacroRadiusPx = 0f,
            PoolSpheres = 6, DetailSize = 128, DetailAniso = 1, DetailBump = false, GlassBackShell = false,
            LightShaft = false, DustMotes = 0, Confetti = 60, ScaleFloor = 0.6f,
            ShadowResolution = 2048, ShadowDistance = 110f, Cascades = 2, Cascade2Split = 0.18f, Cascade3Split = new Vector2(0.1f, 0.3f), SoftShadowQuality = 1,
            MaxDrawsMain = 130, MaxDrawsShadow = 120, MaxDrawsFrame = 300, MaxTriangles = 150000, MaxParticles = 60, MaxRenderTargetMb = 20, GpuMs = 12f,
        };

        public static readonly TierSpec Medium = new TierSpec
        {
            Tier = QualityTier.Medium, Name = "Medium",
            PixelWidth = 1920, PixelHeight = 1080, Msaa = 2, Fxaa = false,
            Bloom = true, BloomHighQuality = false, BloomIterations = 4,
            MacroBand = true, MacroTaps = 8, MacroRadiusPx = 3.5f,
            PoolSpheres = 10, DetailSize = 256, DetailAniso = 4, DetailBump = false, GlassBackShell = true,
            LightShaft = true, DustMotes = 120, Confetti = 150, ScaleFloor = 0.8f,
            ShadowResolution = 4096, ShadowDistance = 170f, Cascades = 2, Cascade2Split = 0.118f, Cascade3Split = new Vector2(0.1f, 0.3f), SoftShadowQuality = 2,
            MaxDrawsMain = 160, MaxDrawsShadow = 140, MaxDrawsFrame = 350, MaxTriangles = 200000, MaxParticles = 270, MaxRenderTargetMb = 75, GpuMs = 10f,
        };

        public static readonly TierSpec High = new TierSpec
        {
            Tier = QualityTier.High, Name = "High",
            PixelWidth = 2560, PixelHeight = 1440, Msaa = 4, Fxaa = false,
            Bloom = true, BloomHighQuality = true, BloomIterations = 5,
            MacroBand = true, MacroTaps = 12, MacroRadiusPx = 5f,
            PoolSpheres = 16, DetailSize = 256, DetailAniso = 4, DetailBump = true, GlassBackShell = true,
            LightShaft = true, DustMotes = 300, Confetti = 300, ScaleFloor = 0.8f,
            ShadowResolution = 4096, ShadowDistance = 240f, Cascades = 3, Cascade2Split = 0.25f, Cascade3Split = new Vector2(0.083f, 0.33f), SoftShadowQuality = 3,
            MaxDrawsMain = 180, MaxDrawsShadow = 210, MaxDrawsFrame = 450, MaxTriangles = 250000, MaxParticles = 600, MaxRenderTargetMb = 180, GpuMs = 12f,
        };

        /// <summary>Low, Medium, High: indexed by <c>(int)QualityTier</c>.</summary>
        public static readonly IReadOnlyList<TierSpec> All = new[] { Low, Medium, High };

        public static TierSpec Of(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low: return Low;
                case QualityTier.High: return High;
                default: return Medium;
            }
        }
    }

    /// <summary>What the starting tier is decided from (ART_BIBLE 7.4); a value, so the table can be tested.</summary>
    public readonly struct DeviceInfo
    {
        /// <summary><c>SystemInfo.graphicsDeviceName</c>: in a browser the unmasked renderer string, where it is exposed.</summary>
        public readonly string Name;
        public readonly int MaxTextureSize;
        /// <summary>A phone or a tablet.</summary>
        public readonly bool Handheld;
        /// <summary>The HDR colour format (B10G11R11) can be rendered to.</summary>
        public readonly bool HdrRenderable;

        public DeviceInfo(string name, int maxTextureSize, bool handheld = false, bool hdrRenderable = true)
        {
            Name = name;
            MaxTextureSize = maxTextureSize;
            Handheld = handheld;
            HdrRenderable = hdrRenderable;
        }

        /// <summary>The machine the game runs on.</summary>
        public static DeviceInfo Current => new DeviceInfo(
            SystemInfo.graphicsDeviceName,
            SystemInfo.maxTextureSize,
            SystemInfo.deviceType == DeviceType.Handheld || Application.isMobilePlatform,
            Quality.HdrRenderable);
    }

    /// <summary>
    /// The rules of ART_BIBLE 7.3 / 7.4 as functions, and the way from a tier to its Quality level and URP
    /// asset. The presenter that applies them is <see cref="QualityPresenter"/>.
    /// </summary>
    public static class Quality
    {
        /// <summary>The render scale never goes below this because of the screen's size alone.</summary>
        public const float MinBaseScale = 0.5f;
        /// <summary>URP treats a render scale this close to 1 as 1.</summary>
        public const float ScaleThreshold = 0.05f;

        /// <summary>True if the HDR colour target of the look (R11G11B10 float) can be rendered to on this device.</summary>
        public static bool HdrRenderable =>
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
            SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render);

        /// <summary>
        /// The tier a session starts on when the player has not chosen one (7.4). Weak and unknown-weak
        /// devices start on Low; everything else, including a masked or empty name, on Medium. High is
        /// never chosen automatically.
        /// </summary>
        public static QualityTier StartingTier(DeviceInfo device)
        {
            if (!device.HdrRenderable) return QualityTier.Low;
            string name = device.Name ?? "";
            if (Has(name, "SwiftShader") || Has(name, "llvmpipe") || Has(name, "Basic Render")) return QualityTier.Low;
            if (device.MaxTextureSize < 4096 || device.Handheld) return QualityTier.Low;
            if (Has(name, "Intel") && (Has(name, "HD Graphics") || Has(name, "UHD"))) return QualityTier.Low;
            return QualityTier.Medium;
        }

        /// <summary>
        /// The render scale a screen of this size gets on a tier before the governor has its say:
        /// <c>clamp(sqrt(budget / (width x height)), 0.5, 1)</c>.
        /// </summary>
        public static float BaseScale(TierSpec tier, int width, int height)
        {
            if (width <= 0 || height <= 0) return 1f;
            float scale = Mathf.Sqrt(tier.PixelBudget / ((float)width * height));
            return Mathf.Clamp(scale, MinBaseScale, 1f);
        }

        /// <summary>The scale handed to URP: the screen's scale times the governor's, in steps of 0.01.</summary>
        public static float RenderScale(TierSpec tier, int width, int height, float governorScale)
        {
            float scale = BaseScale(tier, width, height) * Mathf.Clamp(governorScale, 0.1f, 1f);
            return Mathf.Round(scale * 100f) / 100f;
        }

        /// <summary>The Quality level of a tier (by name), or -1 if the project has no such level.</summary>
        public static int LevelIndex(QualityTier tier)
        {
            EnsureLevels();
            return levelIndex[(int)tier];
        }

        /// <summary>The URP asset of a tier's Quality level, or null.</summary>
        public static UniversalRenderPipelineAsset PipelineOf(QualityTier tier)
        {
            int index = LevelIndex(tier);
            return index < 0 ? null : QualitySettings.GetRenderPipelineAssetAt(index) as UniversalRenderPipelineAsset;
        }

        /// <summary>The tier of the Quality level in force, or null if it is not one of the three.</summary>
        public static QualityTier? ActiveTier
        {
            get
            {
                EnsureLevels();
                int level = QualitySettings.GetQualityLevel();
                for (int i = 0; i < levelIndex.Length; i++)
                    if (levelIndex[i] == level) return (QualityTier)i;
                return null;
            }
        }

        /// <summary>The renderer feature of this type and name on the renderer the tiers share, or null.</summary>
        public static T RendererFeature<T>(string name) where T : ScriptableRendererFeature
        {
            var pipeline = (PipelineOf(QualityTier.Medium) ?? GraphicsSettings.currentRenderPipeline) as UniversalRenderPipelineAsset;
            if (pipeline == null) return null;
            ReadOnlySpan<ScriptableRendererData> renderers = pipeline.rendererDataList;
            for (int r = 0; r < renderers.Length; r++)
            {
                ScriptableRendererData data = renderers[r];
                if (data == null) continue;
                List<ScriptableRendererFeature> features = data.rendererFeatures;
                for (int f = 0; f < features.Count; f++)
                    if (features[f] is T match && match.name == name) return match;
            }
            return null;
        }

        /// <summary>
        /// What the render targets of a tier cost at this size, in bytes (12.1): the colour and depth
        /// targets with their MSAA samples, the resolved copy, the copy the lens blur reads, the bloom
        /// pyramid, the colour LUT and the shadow atlas. An estimate from formats, not a measurement.
        /// </summary>
        public static long RenderTargetBytes(TierSpec tier, int width, int height, float renderScale = 1f)
        {
            long pixels = (long)(width * renderScale) * (long)(height * renderScale);
            long perPixel = 4L * tier.Msaa + 4L * tier.Msaa;      // R11G11B10 colour + D24S8, per sample
            if (tier.Msaa > 1) perPixel += 4;                     // the resolved colour
            if (tier.MacroBand) perPixel += 4;                    // the copy MacroBand samples
            long bytes = pixels * perPixel;
            if (tier.Bloom) bytes += pixels / 4 * 4 * 8 / 3;      // half-resolution pyramid, down and up: 2 x 4/3
            bytes += 1024L * 32 * 4;                              // colour LUT strip
            bytes += (long)tier.ShadowAtlasWidth * tier.ShadowAtlasHeight * 2;   // 16-bit depth
            return bytes;
        }

        static readonly int[] levelIndex = { -1, -1, -1 };
        static int levelCount = -1;

        // QualitySettings.names allocates; the levels only change when the setup step rewrites them.
        static void EnsureLevels()
        {
            if (levelCount == QualitySettings.count && levelCount >= 0) return;
            string[] names = QualitySettings.names;
            levelCount = names.Length;
            for (int i = 0; i < levelIndex.Length; i++) levelIndex[i] = Array.IndexOf(names, TierSpec.All[i].Name);
        }

        /// <summary>Forgets the cached Quality levels (the setup step calls it after rewriting them).</summary>
        public static void RefreshLevels() => levelCount = -1;

        static bool Has(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// The governor of ART_BIBLE 7.4, as a state machine that is fed frame times and says what tier and
    /// what share of the render scale the picture should have. It touches nothing itself, so it can be
    /// tested with made-up frames; <see cref="QualityPresenter"/> applies what it decides.
    ///
    /// Browsers quantise frame time to the display refresh, so it counts slow frames instead of averaging:
    /// 12 slow frames among the last 120 are a step down - the render scale by 0.1 down to the tier's
    /// floor, then one tier with the scale back at 1. After 20 s without a slow frame it steps up once per
    /// session: the scale by 0.1 if it had been lowered, else from Medium to High if the session started
    /// on Medium. A step up that is followed by slow frames within 10 s is taken back and nothing is ever
    /// raised again (<see cref="Locked"/>).
    /// </summary>
    public sealed class QualityGovernor
    {
        /// <summary>A frame longer than this is slow.</summary>
        public const float SlowFrame = 0.020f;
        /// <summary>A frame longer than this is a tab switch, a load or a GC, and is ignored.</summary>
        public const float IgnoredFrame = 0.100f;
        /// <summary>So many ignored frames in a row are not a hiccup any more: from then on they count as slow.</summary>
        public const int IgnoredRun = 5;
        /// <summary>Seconds after a level load that are not judged.</summary>
        public const float LoadGrace = 5f;
        /// <summary>Seconds after a step that are not judged: the reallocation itself hitches.</summary>
        public const float SettleTime = 3f;
        /// <summary>Seconds without a slow frame before the one step up.</summary>
        public const float CalmTime = 20f;
        /// <summary>A step down this soon after the step up takes it back.</summary>
        public const float ProbeTime = 10f;
        public const float ScaleStep = 0.1f;
        public const int Window = 120;
        public const int SlowLimit = 12;

        readonly bool[] window = new bool[Window];
        int head, filled, slow, longRun;
        float grace, settle, calm, probe;
        bool probing;
        QualityTier probeTier;
        float probeScale;

        /// <summary>The tier the session started on.</summary>
        public QualityTier StartTier { get; private set; }
        public QualityTier Tier { get; private set; }
        /// <summary>The governor's share of the render scale: 1 down to the tier's floor.</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>The one step up of the session has been taken.</summary>
        public bool SteppedUp { get; private set; }
        /// <summary>The step up did not hold and was taken back; nothing is raised again this session.</summary>
        public bool Locked { get; private set; }
        public int StepsDown { get; private set; }
        public int StepsUp { get; private set; }
        /// <summary>Slow frames among the last <see cref="Window"/> judged ones.</summary>
        public int SlowInWindow => slow;
        /// <summary>Seconds since the last slow frame that was judged.</summary>
        public float Calm => calm;
        /// <summary>True while frames are not judged (after a level load or a step).</summary>
        public bool Waiting => grace > 0f || settle > 0f;

        public QualityGovernor(QualityTier start)
        {
            StartTier = start;
            Tier = start;
        }

        /// <summary>A level was loaded: the next <see cref="LoadGrace"/> seconds are not judged.</summary>
        public void LevelLoaded()
        {
            grace = LoadGrace;
            ClearWindow();
        }

        /// <summary>
        /// One rendered frame of play took <paramref name="dt"/> seconds. Returns true if <see cref="Tier"/>
        /// or <see cref="Scale"/> changed.
        /// </summary>
        public bool Frame(float dt)
        {
            if (!(dt > 0f)) return false;
            if (dt > IgnoredFrame)
            {
                // One long frame is somebody else's fault. A run of them is the picture itself.
                if (++longRun < IgnoredRun) return false;
            }
            else
            {
                longRun = 0;
            }

            if (grace > 0f)
            {
                grace -= dt;
                return false;
            }
            if (probing) probe += dt;
            if (settle > 0f)
            {
                settle -= dt;
                return false;
            }
            if (probing && probe > ProbeTime) probing = false;

            bool isSlow = dt > SlowFrame;
            Push(isSlow);
            calm = isSlow ? 0f : calm + dt;

            if (slow >= SlowLimit) return StepDown();
            if (!SteppedUp && !Locked && calm >= CalmTime) return StepUp();
            return false;
        }

        bool StepDown()
        {
            if (probing)
            {
                // The step up did not hold: back to where the picture ran smoothly, and never up again.
                probing = false;
                Locked = true;
                Tier = probeTier;
                Scale = probeScale;
                AfterStep();
                StepsDown++;
                return true;
            }

            float floor = TierSpec.Of(Tier).ScaleFloor;
            if (Scale > floor + 0.001f)
            {
                Scale = Mathf.Max(floor, Mathf.Round((Scale - ScaleStep) * 100f) / 100f);
            }
            else if (Tier > QualityTier.Low)
            {
                Tier = Tier - 1;
                Scale = 1f;
            }
            else
            {
                // Low at its floor: there is nothing left to give up. Start counting afresh.
                ClearWindow();
                return false;
            }
            AfterStep();
            StepsDown++;
            return true;
        }

        bool StepUp()
        {
            QualityTier tier = Tier;
            float scale = Scale;
            if (scale < 1f - 0.001f) scale = Mathf.Min(1f, Mathf.Round((scale + ScaleStep) * 100f) / 100f);
            else if (StartTier == QualityTier.Medium && tier == QualityTier.Medium) tier = QualityTier.High;
            else return false;

            probeTier = Tier;
            probeScale = Scale;
            probing = true;
            probe = 0f;
            SteppedUp = true;
            Tier = tier;
            Scale = scale;
            AfterStep();
            StepsUp++;
            return true;
        }

        void AfterStep()
        {
            settle = SettleTime;
            calm = 0f;
            ClearWindow();
        }

        void Push(bool isSlow)
        {
            if (filled == Window)
            {
                if (window[head]) slow--;
            }
            else
            {
                filled++;
            }
            window[head] = isSlow;
            if (isSlow) slow++;
            head = (head + 1) % Window;
        }

        void ClearWindow()
        {
            head = 0;
            filled = 0;
            slow = 0;
            longRun = 0;
        }
    }

    /// <summary>
    /// What a level puts in front of the camera, counted from the scene (12.1): an upper bound, before any
    /// culling. If the bound is within the budget the level is; if it is over, look at the live counters of
    /// <see cref="QualityPresenter"/> or the Frame Debugger.
    /// </summary>
    public struct SceneCensus
    {
        /// <summary>Enabled renderers under game.Root.</summary>
        public int Renderers;
        /// <summary>Draws of the main pass: one per renderer and material.</summary>
        public int Draws;
        /// <summary>Draws into one shadow cascade: one per shadow-casting renderer and material.</summary>
        public int ShadowCasterDraws;
        public int Triangles;
        /// <summary>Distinct materials on those renderers.</summary>
        public int Materials;
        /// <summary>Particles alive.</summary>
        public int Particles;

        /// <summary>What is over a tier's budget ("draws 190/160, materials 44/40"), or null if nothing is.</summary>
        public string Over(TierSpec tier)
        {
            string text = null;
            Check(ref text, "draws", Draws, tier.MaxDrawsMain);
            Check(ref text, "shadow draws", ShadowCasterDraws * tier.Cascades, tier.MaxDrawsShadow);
            Check(ref text, "triangles", Triangles, tier.MaxTriangles);
            Check(ref text, "materials", Materials, TierSpec.MaxMaterials);
            Check(ref text, "particles", Particles, tier.MaxParticles);
            return text;
        }

        public override string ToString() =>
            Renderers + " renderers, " + Draws + " draws, " + ShadowCasterDraws + " shadow caster draws per cascade, " +
            Triangles + " triangles, " + Materials + " materials, " + Particles + " particles";

        static void Check(ref string text, string name, int value, int budget)
        {
            if (value <= budget) return;
            text = (text == null ? "" : text + ", ") + name + " " + value + "/" + budget;
        }

        static readonly List<Renderer> renderers = new List<Renderer>();
        static readonly List<Material> materials = new List<Material>();
        static readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        static readonly HashSet<Material> distinct = new HashSet<Material>();

        /// <summary>Counts everything under game.Root: the level, its room and whatever presenters added.</summary>
        public static SceneCensus Take(Game game)
        {
            var census = new SceneCensus();
            if (game == null || game.IsDisposed || game.Root == null) return census;

            game.Root.GetComponentsInChildren(false, renderers);
            distinct.Clear();
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                renderer.GetSharedMaterials(materials);
                int draws = 0;
                for (int m = 0; m < materials.Count; m++)
                {
                    if (materials[m] == null) continue;
                    draws++;
                    distinct.Add(materials[m]);
                }
                if (draws == 0) continue;

                census.Renderers++;
                bool visible = renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
                if (visible) census.Draws += draws;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off) census.ShadowCasterDraws += draws;
                if (visible && renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    Mesh mesh = filter.sharedMesh;
                    for (int s = 0; s < mesh.subMeshCount; s++) census.Triangles += (int)(mesh.GetIndexCount(s) / 3);
                }
            }
            census.Materials = distinct.Count;

            game.Root.GetComponentsInChildren(false, particles);
            for (int i = 0; i < particles.Count; i++)
                if (particles[i] != null) census.Particles += particles[i].particleCount;

            renderers.Clear();
            materials.Clear();
            particles.Clear();
            distinct.Clear();
            return census;
        }
    }

    /// <summary>
    /// The quality tier in force (ART_BIBLE 7.3, 7.4): it decides the tier a session starts on, switches
    /// the Quality level (and with it the URP asset) when the tier changes, keeps the render scale within
    /// the tier's pixel budget, runs the governor, and keeps the cheap counters of section 12. It provides
    /// neither look nor HUD, so it runs with the plain look too. Everyone else reads
    /// <c>context.Quality</c> / <c>context.QualityChanged</c> and <see cref="TierSpec"/>.
    ///
    /// The tier comes from, in this order: the player's manual choice (<c>Settings.Quality</c>); the tier
    /// the presentation was created with (Shots' -toyboxQuality, tests); in Play Mode and in the build
    /// with Auto, the device table and then the governor. Outside Play Mode nothing is decided
    /// automatically unless <see cref="BeginAutomatic"/> is called, so tools and tests render what they
    /// asked for.
    ///
    /// Global state it changes - the Quality level, the render scale of the tier assets, the camera's HDR
    /// flag - is put back by Dispose.
    /// </summary>
    [Presenter(10)]
    public sealed class QualityPresenter : IPresenter
    {
        static readonly HashSet<int> reported = new HashSet<int>();

        Game game;
        PresentationContext context;
        bool attached, changing;
        int previousLevel = -1;
        bool previousHdr;
        int width, height;
        int censusIn = -1;
        float appliedScale = 1f;
        ProfilerRecorder drawCalls, setPassCalls, triangles;

        /// <summary>The tier in force (the same as <c>context.Quality</c>).</summary>
        public QualityTier Tier { get; private set; } = QualityTier.Medium;
        public TierSpec Spec => TierSpec.Of(Tier);
        /// <summary>The player chose a tier: no governor.</summary>
        public bool Manual => Settings.ForcedTier != null;
        /// <summary>The governor, while the tier is decided automatically; else null.</summary>
        public QualityGovernor Governor { get; private set; }
        /// <summary>The governor is being fed: Auto, in play.</summary>
        public bool Governing => Governor != null && !Manual;
        /// <summary>What the starting tier was decided from; meaningful once <see cref="BeginAutomatic"/> ran.</summary>
        public DeviceInfo Device { get; private set; }
        /// <summary>The HDR colour target is in use. False on a device that cannot render to it: bright things must then stay at or below 1.</summary>
        public bool Hdr { get; private set; } = true;
        /// <summary>The render scale handed to URP.</summary>
        public float RenderScale => appliedScale;
        /// <summary>The size of the picture the render scale was worked out for.</summary>
        public Vector2Int Size => new Vector2Int(width, height);
        /// <summary>A size to work the render scale out for instead of the camera's (tests, tools); null for the camera's.</summary>
        public Vector2Int? SizeOverride { get; set; }

        // ---- Counters (ART_BIBLE 12) -------------------------------------------------------------------
        /// <summary>Frames presented while playing, and how many of them were slow (over 20 ms, up to 100 ms).</summary>
        public int Frames { get; private set; }
        public int SlowFrames { get; private set; }
        /// <summary>The longest frame so far that was not ignored, in seconds.</summary>
        public float WorstFrame { get; private set; }
        /// <summary>What the loaded level puts in front of the camera; taken two frames after it loaded.</summary>
        public SceneCensus Census { get; private set; }
        /// <summary>Draw calls / SetPass calls / triangles of the last frame as the engine counted them, or -1 where it does not.</summary>
        public long LiveDrawCalls => Read(drawCalls);
        public long LiveSetPassCalls => Read(setPassCalls);
        public long LiveTriangles => Read(triangles);
        /// <summary>Estimated render-target memory of the tier in force at the current size, in bytes.</summary>
        public long RenderTargetBytes => Quality.RenderTargetBytes(Spec, width, height, appliedScale);

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            attached = true;
            previousLevel = QualitySettings.GetQualityLevel();
            Quality.RefreshLevels();

            Camera camera = context.Camera;
            if (camera != null)
            {
                previousHdr = camera.allowHDR;
                Hdr = !context.HasGraphics || Quality.HdrRenderable;
                camera.allowHDR = Hdr;
            }

            if (context.HasGraphics)
            {
                drawCalls = Recorder("Draw Calls Count");
                setPassCalls = Recorder("SetPass Calls Count");
                triangles = Recorder("Triangles Count");
            }

            game.Events.LevelLoaded += OnLevelLoaded;
            context.QualityChanged += OnQualityChanged;
            Settings.Changed += OnSettingChanged;

            Tier = Settings.ForcedTier ?? context.Quality;
            // In the game the device decides where an Auto session starts, and the governor takes it from
            // there. Tools and tests (no Play Mode) render the tier they asked for.
            if (Application.isPlaying && context.Flow != null)
            {
                ShaderWarmup.Run();
                BeginAutomatic(DeviceInfo.Current);
            }
            else
            {
                Apply(Tier, 1f);
            }
            if (game.Level != null) censusIn = 2;
        }

        /// <summary>
        /// Decides the tier automatically from here on: the starting tier from the device table (unless
        /// the player chose one) and then the governor. The game calls this itself; tests call it with a
        /// made-up device.
        /// </summary>
        public void BeginAutomatic(DeviceInfo device)
        {
            if (!attached) return;
            Device = device;
            QualityTier start = Quality.StartingTier(device);
            Governor = new QualityGovernor(start);
            if (game.Level != null) Governor.LevelLoaded();
            Apply(Settings.ForcedTier ?? start, 1f);
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached) return;

            if (context.State == FlowState.Playing && dt > 0f)
            {
                if (dt <= QualityGovernor.IgnoredFrame)
                {
                    Frames++;
                    if (dt > QualityGovernor.SlowFrame) SlowFrames++;
                    if (dt > WorstFrame) WorstFrame = dt;
                }
                if (Governing && Governor.Frame(dt)) Apply(Governor.Tier, Governor.Scale);
            }

            // The screen was resized: the same pixel budget is another scale.
            Vector2Int size = CurrentSize();
            if (size.x != width || size.y != height) Apply(Tier, Governing ? Governor.Scale : 1f);

            if (censusIn >= 0 && censusIn-- == 0) TakeCensus();
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelLoaded;
            context.QualityChanged -= OnQualityChanged;
            Settings.Changed -= OnSettingChanged;
            if (drawCalls.Valid) drawCalls.Dispose();
            if (setPassCalls.Valid) setPassCalls.Dispose();
            if (triangles.Valid) triangles.Dispose();

            // The tier assets are shared project state: leave them as the setup step wrote them.
            for (int i = 0; i < TierSpec.All.Count; i++)
            {
                UniversalRenderPipelineAsset pipeline = Quality.PipelineOf(TierSpec.All[i].Tier);
                if (pipeline != null && pipeline.renderScale != 1f) pipeline.renderScale = 1f;
            }
            if (previousLevel >= 0 && previousLevel < QualitySettings.count && QualitySettings.GetQualityLevel() != previousLevel)
                QualitySettings.SetQualityLevel(previousLevel, true);
            if (context.Camera != null) context.Camera.allowHDR = previousHdr;
            Governor = null;
        }

        // Makes a tier the one in force: its Quality level (and URP asset), its render scale, and the
        // context's tier, which is what everyone else listens to.
        void Apply(QualityTier tier, float governorScale)
        {
            Tier = tier;
            TierSpec spec = TierSpec.Of(tier);

            int level = Quality.LevelIndex(tier);
            if (level >= 0 && QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);

            Vector2Int size = CurrentSize();
            width = size.x;
            height = size.y;
            appliedScale = Quality.RenderScale(spec, width, height, governorScale);
            UniversalRenderPipelineAsset pipeline = Quality.PipelineOf(tier);
            if (pipeline != null && !Mathf.Approximately(pipeline.renderScale, appliedScale)) pipeline.renderScale = appliedScale;

            if (context.Quality != tier)
            {
                changing = true;
                try
                {
                    context.Quality = tier;
                }
                finally
                {
                    changing = false;
                }
            }
        }

        Vector2Int CurrentSize()
        {
            if (SizeOverride.HasValue) return SizeOverride.Value;
            Camera camera = context.Camera;
            return camera != null ? new Vector2Int(camera.pixelWidth, camera.pixelHeight) : new Vector2Int(Screen.width, Screen.height);
        }

        void OnLevelLoaded(LevelEvent e)
        {
            Governor?.LevelLoaded();
            censusIn = 2;
        }

        // Somebody else set the tier (a debug key, a test): follow, so that level and scale match it.
        void OnQualityChanged(QualityTier tier)
        {
            if (changing || !attached) return;
            Apply(tier, 1f);
        }

        void OnSettingChanged(Setting setting)
        {
            if (setting != Setting.Quality || !attached) return;
            QualityTier? forced = Settings.ForcedTier;
            if (forced.HasValue)
            {
                Apply(forced.Value, 1f);
            }
            else if (Governor != null)
            {
                // Back to Auto: a governor of its own again, from the tier the device starts on.
                Governor = new QualityGovernor(Quality.StartingTier(Device));
                Governor.LevelLoaded();
                Apply(Governor.Tier, 1f);
            }
        }

        void TakeCensus()
        {
            Census = SceneCensus.Take(game);
            if (game.Level == null) return;
            string over = Census.Over(Spec);
            // Once per level and session: a restart must not repeat it.
            if (over != null && reported.Add(game.Level.Id * 4 + (int)Tier))
                Debug.LogWarning("[Toybox] budget: level " + game.Level.Id + " may exceed the " + Spec.Name + " budget before culling: " + over + " (" + Census + ")");
        }

        static ProfilerRecorder Recorder(string name)
        {
            try
            {
                return ProfilerRecorder.StartNew(ProfilerCategory.Render, name);
            }
            catch (Exception)
            {
                return default;
            }
        }

        static long Read(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue : -1;
    }
}
