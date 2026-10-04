using System;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Render;
using Toybox.UI;
using UnityEngine;

namespace Toybox.Platform
{
    public sealed class RunnerOptions
    {
        /// <summary>Where input comes from; null for the real keyboard and mouse.</summary>
        public IInputDevices Devices;
        /// <summary>The levels to walk through; null for every registered level.</summary>
        public LevelList Levels;
        /// <summary>Create the camera rig and the presenters. Off for runs that only need the game logic.</summary>
        public bool Present = true;
        /// <summary>Start playing at once even if the launch names no level (tests; the title is the default).</summary>
        public bool SkipTitle;
        /// <summary>Where progress is kept; null for the default store (PlayerPrefs while playing).</summary>
        public IPrefStore Store;
        /// <summary>The presenters to choose from; null for those of the game assembly.</summary>
        public System.Collections.Generic.IReadOnlyList<PresenterRegistry.Entry> Presenters;
    }

    /// <summary>
    /// Owns the Game in Play Mode and in the build: creates it together with the human input, the game
    /// flow and the presentation, feeds real time into it every frame, and turns keys and clicks into
    /// calls on the flow.
    ///
    /// Unity drives it through Start / Update / OnDestroy, but all of the work is in <see cref="Begin"/>,
    /// <see cref="Frame"/> and <see cref="Shutdown"/>, which take time and devices as arguments - so the
    /// same code runs in EditMode tests, where Unity calls none of the lifecycle methods.
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        /// <summary>Seconds between completing a level and loading the next one.</summary>
        public const float AdvanceDelay = GameFlow.DefaultAdvanceDelay;
        /// <summary>
        /// Longest frame that is simulated in full. A longer one (the tab was hidden, the machine hitched)
        /// is cut down to this, so the game pauses instead of fast-forwarding.
        /// </summary>
        public const float MaxFrameTime = 0.1f;
        /// <summary>
        /// A pointer capture that ends after it was held for at least this long was taken away (the browser's
        /// Esc, a lost tab) and pauses the game. One that ends sooner was never really granted - a browser
        /// that refuses the lock reports it for a frame or two - and play goes on with the right-button look.
        /// </summary>
        public const float PointerHoldTime = 0.25f;

        Autopilot autopilot;
        bool begun, beginning;
        // How long the mouse has been captured, as of the end of the last frame (0: it was not).
        float pointerHeldFor;

        public Game Game { get; private set; }
        public HumanInput Human { get; private set; }
        public GameFlow Flow { get; private set; }
        /// <summary>The camera rig and the presenters, or null when the run was begun without presentation.</summary>
        public Presentation Presentation { get; private set; }
        public LevelList Levels { get; private set; }
        public LaunchOptions Launch { get; private set; }

        /// <summary>The camera rig, or null without presentation.</summary>
        public CameraRig Rig => Presentation?.Rig;
        /// <summary>The debug HUD while it is the game's HUD, else null.</summary>
        public DebugHud Hud => Presentation?.Get<DebugHudPresenter>()?.Hud;

        /// <summary>Id (in <see cref="Levels"/>) of the level that is loaded or was last asked for.</summary>
        public int LevelId => Flow != null ? Flow.LevelId : -1;
        /// <summary>A bot plays each level's own solution instead of the human.</summary>
        public bool Autoplay { get; private set; }
        /// <summary>Why the bot gave up on the current level, or null.</summary>
        public string AutoplayError => autopilot != null && autopilot.Error != null ? autopilot.Error.Message : null;
        /// <summary>True between a level's completion and the load of its successor.</summary>
        public bool AdvancePending => Flow != null && Flow.AdvancePending;

        void Start()
        {
            if (!begun) Begin(LaunchOptions.FromEnvironment());
        }

        void Update() => Frame(Time.unscaledDeltaTime);

        // While the application quits, scenes are already being torn down by the time OnDestroy runs.
        void OnApplicationQuit() => Shutdown();

        void OnDestroy() => Shutdown();

        /// <summary>
        /// Creates the Game and everything around it. A launch that names a level or asks for autoplay
        /// starts playing at once; otherwise the title is shown over the level to continue with.
        /// </summary>
        public void Begin(LaunchOptions launch, RunnerOptions options = null)
        {
            if (begun) throw new InvalidOperationException("The GameRunner has already begun.");
            options ??= new RunnerOptions();
            begun = true;
            Launch = launch;

            Levels = options.Levels ?? LevelList.FromRegistry();
            Human = new HumanInput(options.Devices);
            // Before any level is built: materials are made while a level builds.
            Materials.Plain = launch.Plain;
            Game = Game.Create(new GameOptions { Input = Human });
            Flow = new GameFlow(Game, Levels, new Progress(options.Store));
            Game.Events.LevelLoaded += OnLevelLoaded;
            Flow.StateChanged += OnStateChanged;

            if (options.Present)
                Presentation = Presentation.Create(Game, new PresentationOptions { Plain = launch.Plain, Flow = Flow, Presenters = options.Presenters });

            Autoplay = launch.Autoplay;
            SyncFlow();
            beginning = true;
            if (launch.Autoplay || launch.NamesLevel || options.SkipTitle) Flow.StartLevel(Levels.Resolve(launch));
            else Flow.ShowTitle();
            beginning = false;
            Present(0f);
        }

        /// <summary>
        /// One rendered frame: input, keys and clicks for the flow, as many ticks as have become due while
        /// playing, the level-complete countdown, presentation.
        /// </summary>
        public void Frame(float realDeltaTime)
        {
            if (Game == null || Game.IsDisposed) return;

            // A capture that was really held and is gone by this frame was taken away.
            bool wasLocked = pointerHeldFor >= PointerHoldTime;
            // A click only captures the mouse while playing, or where no menu wants the click for itself.
            bool menus = Presentation != null && Presentation.HudProvided;
            Human.CaptureOnClick = Flow.Playing || !menus;
            // While the bot plays, the mouse must not fight it for the view; while not playing, nothing turns it.
            Human.Update(Autoplay || !Flow.Playing ? null : Game.Player);
            DeviceFrame keys = Human.Frame;
            if (keys.Focused) HandleKeys(keys, wasLocked, menus);
            // A pointer that was taken away while playing (the browser's Esc, a lost tab) pauses the game.
            else if (Flow.Playing && !Autoplay && wasLocked && !Human.PointerLocked) Flow.Pause();

            // Written so that a negative or NaN frame time counts as no time at all.
            float dt = realDeltaTime > 0f ? Mathf.Min(realDeltaTime, MaxFrameTime) : 0f;
            SyncFlow();
            if (Flow.Playing) Game.Step(dt);
            if (Game == null || Game.IsDisposed) return;
            Flow.Update(dt);

            ReportAutoplay();
            // Presenters get the real frame time, long frames included: a governor has to see them.
            Present(realDeltaTime);
            if (Human == null || !Human.PointerLocked) pointerHeldFor = 0f;
            else pointerHeldFor += realDeltaTime > 0f ? realDeltaTime : 0f;
        }

        void HandleKeys(DeviceFrame keys, bool wasLocked, bool menus)
        {
            if (keys.NextLevelPressed) NextLevel();
            else if (keys.PreviousLevelPressed) PreviousLevel();
            if (keys.AutoplayPressed) SetAutoplay(!Autoplay);
            if (Autoplay) return;

            switch (Flow.State)
            {
                case FlowState.Playing:
                    // Esc, or a pointer that was taken away from outside.
                    if (keys.EscapePressed || (wasLocked && !Human.PointerLocked)) Flow.Pause();
                    break;
                case FlowState.Paused:
                    if (keys.RestartPressed) Flow.Restart();
                    // Esc resumes - unless a menu has a card open over the pause card and wants Esc for that.
                    else if ((keys.EscapePressed && !(Presentation != null && Presentation.Context.EscapeClaimed)) || (!menus && keys.ClickPressed)) Flow.Resume();
                    break;
                case FlowState.Title:
                    if (!menus && keys.ClickPressed) Flow.StartLevel(Flow.LevelId);
                    break;
                case FlowState.LevelComplete:
                    if (keys.RestartPressed) Flow.Restart();
                    break;
            }
        }

        /// <summary>
        /// Loads a level of the list and plays it. A level that fails to build is reported and leaves the
        /// game without a level rather than taking the game loop down. Returns whether it loaded.
        /// </summary>
        public bool LoadLevel(int id)
        {
            if (Game == null || Game.IsDisposed) return false;
            SyncFlow();
            return Flow.StartLevel(id);
        }

        public void NextLevel() => LoadLevel(Levels.After(LevelId));

        public void PreviousLevel() => LoadLevel(Levels.Before(LevelId));

        /// <summary>
        /// Turning autoplay on restarts the level, because a solution starts from the level's initial state,
        /// and plays it from wherever the flow was. Turning it off hands the level over to the human as it is.
        /// </summary>
        public void SetAutoplay(bool on)
        {
            if (Game == null || Game.IsDisposed || Autoplay == on) return;
            Autoplay = on;
            SyncFlow();
            if (on && Game.Level != null)
            {
                // The reload raises LevelLoaded, which puts the bot in charge.
                Flow.Restart();
            }
            else
            {
                TakeControl();
                if (on) Flow.StartLevel(Flow.LevelId);
            }
        }

        /// <summary>Disposes the Game and everything created in Begin. Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (Game == null) return;
            Game game = Game;
            Game = null;
            autopilot = null;

            game.Events.LevelLoaded -= OnLevelLoaded;
            if (Flow != null)
            {
                Flow.StateChanged -= OnStateChanged;
                Flow.Dispose();
            }
            Presentation?.Dispose();
            Presentation = null;
            Human?.ReleasePointer();
            if (!game.IsDisposed) game.Dispose();
            Materials.Plain = false;
            Progress progress = Flow?.Progress;
            Flow = null;
            progress?.Store.Save();
        }

        // What the flow needs to know about who is playing.
        void SyncFlow()
        {
            Flow.RecordProgress = !Autoplay;
            Flow.ForceAutoAdvance = Autoplay;
        }

        void OnLevelLoaded(LevelEvent e) => TakeControl();

        void OnStateChanged(FlowChange change)
        {
            if (Human == null) return;
            bool menus = Presentation != null && Presentation.HudProvided;
            if (change.To == FlowState.Playing)
            {
                // Keys pressed in a menu are not presses in the game.
                Human.Clear();
                // Coming back from a menu the mouse is wanted again. Not at the very start: there the first click takes it.
                if (!Autoplay && menus && !beginning) Human.CapturePointer();
            }
            else if (change.From == FlowState.Playing)
            {
                // While not playing the pointer is free - where there are menus to point at.
                Human.Clear();
                if (menus || change.To == FlowState.Paused) Human.ReleasePointer();
            }
        }

        // Decides who steers: the bot with the level's solution while autoplay is on, else the human.
        void TakeControl()
        {
            autopilot = null;
            Human.Clear();
            if (Autoplay && Game.Level != null)
            {
                autopilot = Autopilot.Start(Game);
                Game.Input = autopilot;
            }
            else
            {
                Game.Input = Human;
            }
        }

        void ReportAutoplay()
        {
            if (autopilot == null || autopilot.Error == null || autopilot.Reported) return;
            autopilot.Reported = true;
            Debug.LogWarning("[Toybox] autoplay failed: " + autopilot.Error.Message);
            Game.Events.RaiseMessage(new MessageEvent { Text = "Autoplay got stuck - press P to take over", Seconds = 8f });
        }

        void Present(float dt)
        {
            if (Presentation == null || Game == null || Game.IsDisposed) return;
            PresentationContext context = Presentation.Context;
            context.Autoplay = Autoplay;
            context.AutoplayError = AutoplayError;
            context.PointerLocked = Human.PointerLocked;
            context.LookHeld = Human.Frame.LookButton;
            Presentation.Frame(dt, Game.Alpha);
        }

        /// <summary>
        /// The bot running the loaded level's Solve script, paced by the game's own ticks. A script that
        /// fails stops steering; the failure is kept for the runner to report instead of escaping into the
        /// game loop.
        /// </summary>
        sealed class Autopilot : IInputSource
        {
            BotRunner runner;

            public Exception Error { get; private set; }
            public bool Reported;
            public bool Finished => Error != null || runner == null || runner.Finished;

            public static Autopilot Start(Game game)
            {
                var autopilot = new Autopilot();
                try
                {
                    var bot = new Bot(game);
                    autopilot.runner = BotRunner.Attach(game, bot, game.Level.Solve(bot));
                }
                catch (Exception e)
                {
                    autopilot.Error = e;
                }
                return autopilot;
            }

            public InputFrame Sample()
            {
                if (Error != null || runner == null) return default;
                try
                {
                    return runner.Sample();
                }
                catch (Exception e)
                {
                    Error = e;
                    return default;
                }
            }
        }
    }
}
