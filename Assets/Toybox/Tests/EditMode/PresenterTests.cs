using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    // Presenters of the test assembly. The game only looks for presenters in its own assembly, so these
    // exist for these tests alone.
    public abstract class ProbePresenter : IPresenter
    {
        public static readonly List<string> Log = new List<string>();
        public static readonly List<ProbePresenter> Alive = new List<ProbePresenter>();

        public PresentationContext Context;
        public Game Game;
        public int Frames;
        public float LastDt, LastAlpha;
        public bool Disposed;
        public readonly List<string> LevelEvents = new List<string>();

        string Name => GetType().Name;

        public virtual void Attach(Game game, PresentationContext context)
        {
            Game = game;
            Context = context;
            Log.Add(Name + ".Attach");
            Alive.Add(this);
            game.Events.LevelLoaded += OnLoaded;
            game.Events.LevelUnloading += OnUnloading;
        }

        public virtual void Frame(float dt, float alpha)
        {
            Frames++;
            LastDt = dt;
            LastAlpha = alpha;
            Log.Add(Name + ".Frame");
        }

        public void Dispose()
        {
            Disposed = true;
            Log.Add(Name + ".Dispose");
            Alive.Remove(this);
            if (Game == null) return;
            Game.Events.LevelLoaded -= OnLoaded;
            Game.Events.LevelUnloading -= OnUnloading;
        }

        void OnLoaded(LevelEvent e) => LevelEvents.Add("loaded");
        void OnUnloading(LevelEvent e) => LevelEvents.Add("unloading");
    }

    [Presenter(5, ProvidesLook = true, Fallback = true)]
    public sealed class FallbackLookProbe : ProbePresenter { }

    [Presenter(10)]
    public sealed class NeutralProbe : ProbePresenter { }

    [Presenter(15)]
    public sealed class FragileProbe : ProbePresenter
    {
        public static bool ThrowInAttach, ThrowInFrame;

        public override void Attach(Game game, PresentationContext context)
        {
            base.Attach(game, context);
            if (ThrowInAttach) throw new InvalidOperationException("probe broke in Attach");
        }

        public override void Frame(float dt, float alpha)
        {
            base.Frame(dt, alpha);
            if (ThrowInFrame) throw new InvalidOperationException("probe broke in Frame");
        }
    }

    [Presenter(20, ProvidesLook = true)]
    public sealed class LookProbe : ProbePresenter { }

    [Presenter(30, ProvidesHud = true)]
    public sealed class HudProbe : ProbePresenter { }

    [Presenter(40, ProvidesHud = true, Fallback = true)]
    public sealed class FallbackHudProbe : ProbePresenter { }

    /// <summary>
    /// The presenter seam: discovery by attribute, the rule that picks who runs (plain, fallbacks,
    /// ProvidesLook / ProvidesHud), the lifecycle a Presentation gives its presenters, what the context
    /// carries, and that GameRunner and Shots drive the very same list.
    /// </summary>
    public class PresenterTests
    {
        Game game;
        Presentation presentation;
        GameObject host;
        GameRunner runner;

        [SetUp]
        public void Reset()
        {
            ProbePresenter.Log.Clear();
            ProbePresenter.Alive.Clear();
            FragileProbe.ThrowInAttach = false;
            FragileProbe.ThrowInFrame = false;
        }

        [TearDown]
        public void Dispose()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            Materials.Tier = QualityTier.Medium;
            Materials.Plain = false;
        }

        static List<PresenterRegistry.Entry> Probes() => PresenterRegistry.Discover(typeof(PresenterTests).Assembly);

        static List<PresenterRegistry.Entry> Probes(params Type[] types)
        {
            var kept = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in Probes())
                if (Array.IndexOf(types, entry.Type) >= 0) kept.Add(entry);
            return kept;
        }

        static string[] Names(IEnumerable<PresenterRegistry.Entry> entries)
        {
            var names = new List<string>();
            foreach (PresenterRegistry.Entry entry in entries) names.Add(entry.Type.Name);
            return names.ToArray();
        }

        Game Yard()
        {
            game = Game.Create();
            game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx, 30f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }));
            return game;
        }

        // ------------------------------------------------------------------------------------------
        // Discovery and the rule
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Discovery_FindsMarkedPresentersInOrder()
        {
            List<PresenterRegistry.Entry> probes = Probes();
            CollectionAssert.AreEqual(new[] { "FallbackLookProbe", "NeutralProbe", "FragileProbe", "LookProbe", "HudProbe", "FallbackHudProbe" }, Names(probes));
            Assert.AreEqual(5, probes[0].Order);
            Assert.IsTrue(probes[0].ProvidesLook && probes[0].Fallback && !probes[0].ProvidesHud);
            Assert.IsFalse(probes[1].ProvidesLook || probes[1].ProvidesHud || probes[1].Fallback);
            Assert.IsTrue(probes[4].ProvidesHud && !probes[4].Fallback);

            // The game's own: found once, sorted, and the two stand-ins are among them as fallbacks.
            IReadOnlyList<PresenterRegistry.Entry> all = PresenterRegistry.All;
            Assert.AreSame(all, PresenterRegistry.All);
            PresenterRegistry.Entry plainLook = null, debugHud = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0) Assert.LessOrEqual(all[i - 1].Order, all[i].Order, "sorted by order");
                Assert.AreEqual(typeof(GameRunner).Assembly, all[i].Type.Assembly, "only the game assembly is searched");
                Assert.IsNotNull(all[i].Type.GetConstructor(Type.EmptyTypes), all[i].Type.Name + " needs a parameterless constructor");
                if (all[i].Type == typeof(PlainLook)) plainLook = all[i];
                if (all[i].Type == typeof(DebugHudPresenter)) debugHud = all[i];
            }
            Assert.IsNotNull(plainLook, "PlainLook is a presenter");
            Assert.IsTrue(plainLook.ProvidesLook && plainLook.Fallback);
            Assert.IsNotNull(debugHud, "DebugHudPresenter is a presenter");
            Assert.IsTrue(debugHud.ProvidesHud && debugHud.Fallback);
        }

        [Test]
        public void TheRule_PlainOrNobodyElse_ActivatesTheFallbacks()
        {
            List<PresenterRegistry.Entry> probes = Probes();

            // The game has its own look and HUD: the stand-ins are retired.
            CollectionAssert.AreEqual(new[] { "NeutralProbe", "FragileProbe", "LookProbe", "HudProbe" }, Names(PresenterRegistry.Select(probes, false)));
            // Plain: the stand-ins, and whatever is neither look nor HUD (audio, say).
            CollectionAssert.AreEqual(new[] { "FallbackLookProbe", "NeutralProbe", "FragileProbe", "FallbackHudProbe" }, Names(PresenterRegistry.Select(probes, true)));

            // Nobody provides the look yet: the plain look stands in, while the HUD is the game's own.
            List<PresenterRegistry.Entry> noLook = Probes(typeof(FallbackLookProbe), typeof(NeutralProbe), typeof(HudProbe), typeof(FallbackHudProbe));
            CollectionAssert.AreEqual(new[] { "FallbackLookProbe", "NeutralProbe", "HudProbe" }, Names(PresenterRegistry.Select(noLook, false)));
            // And the other way round.
            List<PresenterRegistry.Entry> noHud = Probes(typeof(FallbackLookProbe), typeof(LookProbe), typeof(FallbackHudProbe));
            CollectionAssert.AreEqual(new[] { "LookProbe", "FallbackHudProbe" }, Names(PresenterRegistry.Select(noHud, false)));
            // Nothing but fallbacks: both run, plain or not.
            List<PresenterRegistry.Entry> bare = Probes(typeof(FallbackLookProbe), typeof(FallbackHudProbe));
            CollectionAssert.AreEqual(new[] { "FallbackLookProbe", "FallbackHudProbe" }, Names(PresenterRegistry.Select(bare, false)));
            CollectionAssert.AreEqual(new[] { "FallbackLookProbe", "FallbackHudProbe" }, Names(PresenterRegistry.Select(bare, true)));
        }

        [Test]
        public void TheGamesOwnPresenters_PlainLookAndDebugHudStandInUntilSomebodyProvides()
        {
            Yard();
            presentation = Presentation.Create(game);
            Assert.AreEqual(!presentation.LookProvided, presentation.Get<PlainLook>() != null, "the plain look is active exactly while nothing else provides the look");
            Assert.AreEqual(!presentation.HudProvided, presentation.Get<DebugHudPresenter>() != null, "and the debug HUD while nothing else provides the HUD");
            presentation.Dispose();

            // Plain: both, whatever else the game has.
            presentation = Presentation.Create(game, new PresentationOptions { Plain = true });
            Assert.IsNotNull(presentation.Get<PlainLook>());
            Assert.IsNotNull(presentation.Get<DebugHudPresenter>());
            Assert.IsFalse(presentation.LookProvided);
            Assert.IsFalse(presentation.HudProvided);
            Assert.IsTrue(presentation.Context.Plain);
            foreach (IPresenter presenter in presentation.Presenters)
            {
                var attribute = (PresenterAttribute)Attribute.GetCustomAttribute(presenter.GetType(), typeof(PresenterAttribute));
                Assert.IsTrue(attribute.Fallback || !(attribute.ProvidesLook || attribute.ProvidesHud), presenter.GetType().Name + " must not run with the plain look");
            }
            presentation.Dispose();

            // A look of its own retires the plain look; the debug HUD stays.
            var entries = new List<PresenterRegistry.Entry>(PresenterRegistry.All);
            entries.AddRange(Probes(typeof(LookProbe)));
            var withLook = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in entries)
                if (entry.Fallback || entry.Type == typeof(LookProbe)) withLook.Add(entry);
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = withLook });
            Assert.IsNull(presentation.Get<PlainLook>());
            Assert.IsNotNull(presentation.Get<LookProbe>());
            Assert.IsNotNull(presentation.Get<DebugHudPresenter>());
            Assert.IsTrue(presentation.LookProvided);
            Assert.IsFalse(presentation.HudProvided);
        }

        [Test]
        public void PlainLookAndDebugHud_ComeAndGoWithoutATrace()
        {
            Yard();
            AmbientMode ambient = RenderSettings.ambientMode;
            Color sky = RenderSettings.ambientSkyColor;
            int objects = game.Root.GetComponentsInChildren<Transform>(true).Length;

            presentation = Presentation.Create(game, new PresentationOptions { Plain = true });
            PlainLook look = presentation.Get<PlainLook>();
            Assert.AreEqual(LightType.Directional, look.Sun.type);
            Assert.AreNotEqual(LightShadows.None, look.Sun.shadows);
            Assert.IsTrue(look.Sun.transform.IsChildOf(presentation.Context.Root), "presenters keep their objects under the presentation root");
            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode);
            Assert.AreEqual(PlainLook.Background, presentation.Camera.backgroundColor);
            Assert.AreEqual(1, game.Root.GetComponentsInChildren<Light>(true).Length, "one light, and it is the plain look's - the rig has none");

            DebugHud hud = presentation.Get<DebugHudPresenter>().Hud;
            Assert.IsNotNull(hud);
            Assert.IsTrue(hud.transform.IsChildOf(presentation.Context.Root));
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(hud.ClickToPlay, "no flow, pointer free: the prompt shows");
            presentation.Context.PointerLocked = true;
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsFalse(hud.ClickToPlay);
            presentation.Context.Autoplay = true;
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(hud.Autoplay);

            // It follows the game through a level change: the banner comes and goes with the events.
            game.CompleteLevel();
            Assert.IsTrue(hud.BannerVisible);
            game.RestartLevel();
            Assert.IsFalse(hud.BannerVisible);
            Assert.IsTrue(look.Sun != null, "the look's objects survive a level load");

            presentation.Dispose();
            presentation = null;
            Assert.IsTrue(hud == null);
            Assert.IsTrue(look.Sun == null);
            Assert.AreEqual(ambient, RenderSettings.ambientMode, "scene lighting is put back");
            Assert.AreEqual(sky, RenderSettings.ambientSkyColor);
            Assert.AreEqual(objects, game.Root.GetComponentsInChildren<Transform>(true).Length, "nothing is left under the game's root");
        }

        // ------------------------------------------------------------------------------------------
        // Lifecycle and context
        // ------------------------------------------------------------------------------------------

        [Test]
        public void APresentation_AttachesFramesAndDisposesItsPresentersInOrder()
        {
            Yard();
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = Probes() });
            CollectionAssert.AreEqual(new[] { "NeutralProbe.Attach", "FragileProbe.Attach", "LookProbe.Attach", "HudProbe.Attach" }, ProbePresenter.Log);
            Assert.AreEqual(4, presentation.Presenters.Count);
            Assert.IsTrue(presentation.LookProvided);
            Assert.IsTrue(presentation.HudProvided);

            // What they are given to work with.
            NeutralProbe probe = presentation.Get<NeutralProbe>();
            PresentationContext context = probe.Context;
            Assert.AreSame(presentation.Context, context);
            Assert.AreSame(game, probe.Game);
            Assert.AreSame(game, context.Game);
            Assert.AreSame(presentation, context.Presentation);
            Assert.IsNotNull(context.Camera);
            Assert.AreSame(presentation.Rig.Camera, context.Camera);
            Assert.AreSame(presentation.Rig, context.Rig);
            Assert.IsTrue(context.Camera.transform.IsChildOf(game.Root.transform));
            Assert.AreSame(game.Root.transform, context.Root.parent, "the presentation root is in the simulation's scene");
            Assert.AreEqual(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, context.HasGraphics);
            Assert.IsFalse(context.Plain);
            Assert.AreEqual(QualityTier.Medium, context.Quality, "Auto starts on Medium until the pipeline says otherwise");
            Assert.IsNull(context.Flow);
            Assert.AreEqual(FlowState.Playing, context.State, "without a flow (tools) the game is simply being played");
            Assert.AreEqual(0f, context.UnscaledTime);

            // Frames: the camera first, then the presenters in order, with the frame's dt and alpha.
            ProbePresenter.Log.Clear();
            game.Player.Yaw = 30f;
            presentation.Frame(0.02f, 0.25f);
            CollectionAssert.AreEqual(new[] { "NeutralProbe.Frame", "FragileProbe.Frame", "LookProbe.Frame", "HudProbe.Frame" }, ProbePresenter.Log);
            Assert.AreEqual(0.02f, probe.LastDt);
            Assert.AreEqual(0.25f, probe.LastAlpha);
            Assert.Less(Quaternion.Angle(game.Player.LookRotation, context.Camera.transform.rotation), 1e-3f, "the camera is placed before the presenters run");
            Assert.AreEqual(0.02f, context.DeltaTime);
            presentation.Frame(0.03f, 1f);
            presentation.Frame(-1f, 1f);
            presentation.Frame(float.NaN, 1f);
            Assert.AreEqual(0.05f, context.UnscaledTime, 1e-6f, "unscaled time is the sum of the frames; nonsense counts as nothing");
            Assert.AreEqual(0f, probe.LastDt);
            Assert.AreEqual(4, context.FrameCount);
            Assert.AreEqual(4, probe.Frames);

            // Level changes reach them through the game's events.
            game.RestartLevel();
            CollectionAssert.AreEqual(new[] { "unloading", "loaded" }, probe.LevelEvents);

            // The quality tier is published through the context, and materials follow it.
            var tiers = new List<QualityTier>();
            context.QualityChanged += tiers.Add;
            context.Quality = QualityTier.High;
            context.Quality = QualityTier.High;
            CollectionAssert.AreEqual(new[] { QualityTier.High }, tiers);
            Assert.AreEqual(QualityTier.High, Materials.Tier);

            // Dispose: in reverse order, and then the rig and the root are gone.
            ProbePresenter.Log.Clear();
            Camera camera = context.Camera;
            Transform root = context.Root;
            presentation.Dispose();
            presentation.Dispose();
            CollectionAssert.AreEqual(new[] { "HudProbe.Dispose", "LookProbe.Dispose", "FragileProbe.Dispose", "NeutralProbe.Dispose" }, ProbePresenter.Log);
            Assert.IsTrue(camera == null);
            Assert.IsTrue(root == null);
            Assert.AreEqual(0, ProbePresenter.Alive.Count);
            Assert.DoesNotThrow(() => presentation.Frame(Sim.Dt, 1f), "a frame after Dispose is a no-op");
            presentation = null;
        }

        [Test]
        public void APresentation_StartsOnThePlayersTier()
        {
            Yard();
            var store = new MemoryStore();
            Settings.Use(store);
            try
            {
                Settings.Quality = QualitySetting.Low;
                presentation = Presentation.Create(game, new PresentationOptions { Presenters = Probes(typeof(NeutralProbe)) });
                Assert.AreEqual(QualityTier.Low, presentation.Context.Quality);
                Assert.AreEqual(QualityTier.Low, Materials.Tier);
                presentation.Dispose();

                presentation = Presentation.Create(game, new PresentationOptions { Presenters = Probes(typeof(NeutralProbe)), Quality = QualityTier.High });
                Assert.AreEqual(QualityTier.High, presentation.Context.Quality, "a tool may ask for a tier outright");
            }
            finally
            {
                Settings.Use(null);
            }
        }

        [Test]
        public void APresenterThatThrows_IsReportedAndRetired_AndTheOthersCarryOn()
        {
            Yard();
            FragileProbe.ThrowInAttach = true;
            LogAssert.Expect(LogType.Exception, new Regex("probe broke in Attach"));
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = Probes() });
            Assert.IsNull(presentation.Get<FragileProbe>(), "a presenter that cannot start is left out");
            Assert.AreEqual(3, presentation.Presenters.Count);
            CollectionAssert.Contains(ProbePresenter.Log, "FragileProbe.Dispose");
            presentation.Dispose();

            FragileProbe.ThrowInAttach = false;
            ProbePresenter.Log.Clear();
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = Probes() });
            FragileProbe fragile = presentation.Get<FragileProbe>();
            Assert.IsNotNull(fragile);
            presentation.Frame(Sim.Dt, 1f);

            FragileProbe.ThrowInFrame = true;
            LogAssert.Expect(LogType.Exception, new Regex("probe broke in Frame"));
            ProbePresenter.Log.Clear();
            Assert.DoesNotThrow(() => presentation.Frame(Sim.Dt, 1f));
            CollectionAssert.AreEqual(new[] { "NeutralProbe.Frame", "FragileProbe.Frame", "FragileProbe.Dispose", "LookProbe.Frame", "HudProbe.Frame" }, ProbePresenter.Log,
                "the frame goes on for the others");
            Assert.IsTrue(fragile.Disposed);
            Assert.IsNull(presentation.Get<FragileProbe>());

            // It is reported once, not every frame.
            ProbePresenter.Log.Clear();
            presentation.Frame(Sim.Dt, 1f);
            CollectionAssert.AreEqual(new[] { "NeutralProbe.Frame", "LookProbe.Frame", "HudProbe.Frame" }, ProbePresenter.Log);
        }

        // ------------------------------------------------------------------------------------------
        // The three drivers
        // ------------------------------------------------------------------------------------------

        sealed class WalkLevel : LevelDefinition
        {
            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 30f);
                ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }
        }

        [Test]
        public void GameRunner_DrivesThePresenters_AndTellsThemWhoPlays()
        {
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=0"), new RunnerOptions
            {
                Devices = devices,
                Levels = new LevelList(new[] { 0, 1 }, id => new WalkLevel()),
                Store = new MemoryStore(),
                Presenters = Probes(typeof(NeutralProbe), typeof(FallbackLookProbe), typeof(FallbackHudProbe)),
            });

            Assert.IsNotNull(runner.Presentation);
            NeutralProbe probe = runner.Presentation.Get<NeutralProbe>();
            Assert.IsNotNull(probe);
            Assert.IsNotNull(runner.Presentation.Get<FallbackLookProbe>(), "nobody else provides the look");
            Assert.AreSame(runner.Flow, probe.Context.Flow);
            Assert.AreSame(runner.Rig, probe.Context.Rig);
            Assert.AreEqual(1, probe.Frames, "Begin presents once, so the first picture is right");
            Assert.IsNull(runner.Hud, "the debug HUD is not among these presenters");

            runner.Frame(Sim.Dt);
            runner.Frame(Sim.Dt * 0.5f);
            Assert.AreEqual(3, probe.Frames, "one presentation frame per runner frame, tick or no tick");
            Assert.AreEqual(runner.Game.Alpha, probe.LastAlpha);
            Assert.AreEqual(FlowState.Playing, probe.Context.State);
            Assert.IsFalse(probe.Context.PointerLocked);
            Assert.IsFalse(probe.Context.Autoplay);

            devices.State.ClickPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(probe.Context.PointerLocked);
            CollectionAssert.AreEqual(new[] { "loaded" }, probe.LevelEvents, "the presenters were attached before the first level loaded");
            probe.LevelEvents.Clear();
            devices.State.LookButton = true;
            devices.State.AutoplayPressed = true;
            runner.Frame(Sim.Dt);
            Assert.IsTrue(probe.Context.Autoplay);
            Assert.IsTrue(probe.Context.LookHeld);
            CollectionAssert.AreEqual(new[] { "unloading", "loaded" }, probe.LevelEvents, "turning autoplay on restarts the level");

            // Not playing: the simulation stands still, the presentation does not.
            runner.SetAutoplay(false);
            runner.Flow.Pause();
            int frames = probe.Frames, ticks = runner.Game.TickCount;
            for (int i = 0; i < 10; i++) runner.Frame(Sim.Dt);
            Assert.AreEqual(frames + 10, probe.Frames);
            Assert.AreEqual(ticks, runner.Game.TickCount);
            Assert.AreEqual(FlowState.Paused, probe.Context.State);

            runner.Shutdown();
            Assert.IsTrue(probe.Disposed, "shutting down disposes the presenters");
            Assert.AreEqual(0, ProbePresenter.Alive.Count);
            Assert.IsNull(runner.Presentation);
        }

        [Test]
        public void GameRunner_WithoutPresentation_HasNoneAndStillPlays()
        {
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            runner.Begin(LaunchOptions.FromUrl("?level=0"), new RunnerOptions
            {
                Devices = new FakeDevices(),
                Levels = new LevelList(new[] { 0 }, id => new WalkLevel()),
                Store = new MemoryStore(),
                Present = false,
            });
            Assert.IsNull(runner.Presentation);
            Assert.IsNull(runner.Rig);
            Assert.IsNull(runner.Hud);
            int ticks = runner.Game.TickCount;
            runner.Frame(Sim.Dt);
            Assert.AreEqual(ticks + 1, runner.Game.TickCount);
        }

        [Test]
        public void LaunchOptions_PlainComesFromTheUrl()
        {
            Assert.IsTrue(LaunchOptions.FromUrl("https://example.com/?plain=1").Plain);
            Assert.IsTrue(LaunchOptions.FromUrl("?level=3&plain").Plain, "a bare 'plain' counts as on");
            Assert.IsFalse(LaunchOptions.FromUrl("?plain=0").Plain);
            Assert.IsFalse(LaunchOptions.FromUrl("?level=3").Plain);
            Assert.IsTrue(LaunchOptions.FromUrl("?level=3").NamesLevel);
            Assert.IsTrue(LaunchOptions.FromUrl("?level=cheese-wedge").NamesLevel);
            Assert.IsFalse(LaunchOptions.FromUrl("?plain=1&autoplay=1").NamesLevel);

            // The runner hands it on to the materials and to the presentation.
            host = new GameObject("Test Game Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            runner.Begin(LaunchOptions.FromUrl("?level=0&plain=1"), new RunnerOptions
            {
                Devices = new FakeDevices(),
                Levels = new LevelList(new[] { 0 }, id => new WalkLevel()),
                Store = new MemoryStore(),
            });
            Assert.IsTrue(runner.Launch.Plain);
            Assert.IsTrue(Materials.Plain, "the level was built with plain materials");
            Assert.IsTrue(runner.Presentation.Context.Plain);
            Assert.IsNotNull(runner.Presentation.Get<PlainLook>());
            Assert.IsNotNull(runner.Hud);
            runner.Shutdown();
            Assert.IsFalse(Materials.Plain, "and the flag does not outlive the run");
        }

        [Test]
        public void Shots_DrivesTheSamePresenters()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");

            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxPresenterShots");
            List<PresenterRegistry.Entry> presenters = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                if (entry.Fallback) presenters.Add(entry);
            presenters.AddRange(Probes(typeof(NeutralProbe)));

            List<string> files = Shots.Run(new ShotRequest
            {
                Level = 3,
                Definition = new WalkLevel(),
                Times = new[] { 0f, 0.5f },
                Width = 160,
                Height = 90,
                OutputDirectory = directory,
                Presenters = presenters,
                Quality = QualityTier.Low,
            });

            Assert.AreEqual(2, files.Count);
            foreach (string file in files) Assert.IsTrue(File.Exists(file), file);
            CollectionAssert.Contains(ProbePresenter.Log, "NeutralProbe.Attach");
            int frames = ProbePresenter.Log.FindAll(line => line == "NeutralProbe.Frame").Count;
            Assert.GreaterOrEqual(frames, 30, "a frame for every tick of the half second, and one before each picture");
            Assert.AreEqual("NeutralProbe.Dispose", ProbePresenter.Log[ProbePresenter.Log.Count - 1], "disposed when the shots are done");
            Assert.AreEqual(0, ProbePresenter.Alive.Count);
            Assert.IsNull(Game.Current);

            Assert.AreEqual(QualityTier.High, Shots.ParseQuality("HIGH"));
            Assert.AreEqual(QualityTier.Low, Shots.ParseQuality(" low "));
            Assert.IsNull(Shots.ParseQuality("ultra"));
            Assert.IsNull(Shots.ParseQuality(null));
        }
    }
}
