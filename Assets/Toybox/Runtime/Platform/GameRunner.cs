using System;
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
        /// <summary>Create the camera rig and the HUD. Off for runs that only need the game logic.</summary>
        public bool Present = true;
    }

    /// <summary>
    /// Owns the Game in Play Mode and in the build: creates it together with the human input, the camera
    /// rig and the HUD, feeds real time into it every frame, and moves from level to level.
    ///
    /// Unity drives it through Start / Update / OnDestroy, but all of the work is in <see cref="Begin"/>,
    /// <see cref="Frame"/> and <see cref="Shutdown"/>, which take time and devices as arguments - so the
    /// same code runs in EditMode tests, where Unity calls none of the lifecycle methods.
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        /// <summary>Seconds between completing a level and loading the next one.</summary>
        public const float AdvanceDelay = 2.5f;
        /// <summary>
        /// Longest frame that is simulated in full. A longer one (the tab was hidden, the machine hitched)
        /// is cut down to this, so the game pauses instead of fast-forwarding.
        /// </summary>
        public const float MaxFrameTime = 0.1f;

        Autopilot autopilot;
        float advanceTimer;
        bool advancePending;
        bool begun;

        public Game Game { get; private set; }
        public HumanInput Human { get; private set; }
        public CameraRig Rig { get; private set; }
        public DebugHud Hud { get; private set; }
        public LevelList Levels { get; private set; }

        /// <summary>Id (in <see cref="Levels"/>) of the level that is loaded or was last asked for.</summary>
        public int LevelId { get; private set; } = -1;
        /// <summary>A bot plays each level's own solution instead of the human.</summary>
        public bool Autoplay { get; private set; }
        /// <summary>Why the bot gave up on the current level, or null.</summary>
        public string AutoplayError => autopilot != null && autopilot.Error != null ? autopilot.Error.Message : null;
        /// <summary>True between a level's completion and the load of its successor.</summary>
        public bool AdvancePending => advancePending;

        void Start()
        {
            if (!begun) Begin(LaunchOptions.FromUrl(Application.absoluteURL));
        }

        void Update() => Frame(Time.unscaledDeltaTime);

        // While the application quits, scenes are already being torn down by the time OnDestroy runs.
        void OnApplicationQuit() => Shutdown();

        void OnDestroy() => Shutdown();

        /// <summary>Creates the Game and everything around it and loads the first level.</summary>
        public void Begin(LaunchOptions launch, RunnerOptions options = null)
        {
            if (begun) throw new InvalidOperationException("The GameRunner has already begun.");
            options ??= new RunnerOptions();
            begun = true;

            Levels = options.Levels ?? LevelList.FromRegistry();
            Human = new HumanInput(options.Devices);
            Game = Game.Create(new GameOptions { Input = Human });
            Game.Events.LevelLoaded += OnLevelLoaded;
            Game.Events.LevelCompleted += OnLevelCompleted;

            if (options.Present)
            {
                Rig = CameraRig.Create(Game);
                Hud = gameObject.AddComponent<DebugHud>();
                Hud.Bind(Game);
            }

            Autoplay = launch.Autoplay;
            LoadLevel(Levels.Resolve(launch));
            PushHudState();
        }

        /// <summary>One rendered frame: input, debug keys, as many ticks as have become due, level flow, camera.</summary>
        public void Frame(float realDeltaTime)
        {
            if (Game == null || Game.IsDisposed) return;

            // While the bot plays, the mouse must not fight it for the view.
            Human.Update(Autoplay ? null : Game.Player);
            DeviceFrame keys = Human.Frame;
            if (keys.Focused)
            {
                if (keys.NextLevelPressed) NextLevel();
                else if (keys.PreviousLevelPressed) PreviousLevel();
                if (keys.AutoplayPressed) SetAutoplay(!Autoplay);
            }

            // Written so that a negative or NaN frame time counts as no time at all.
            float dt = realDeltaTime > 0f ? Mathf.Min(realDeltaTime, MaxFrameTime) : 0f;
            Game.Step(dt);

            if (advancePending)
            {
                advanceTimer -= dt;
                if (advanceTimer <= 0f)
                {
                    advancePending = false;
                    LoadLevel(Levels.After(LevelId));
                }
            }

            PushHudState();
            Rig?.Apply();
        }

        /// <summary>
        /// Loads a level of the list. A level that fails to build is reported and leaves the game without a
        /// level rather than taking the game loop down. Returns whether it loaded.
        /// </summary>
        public bool LoadLevel(int id)
        {
            if (Game == null || Game.IsDisposed) return false;
            advancePending = false;
            LevelId = id;
            try
            {
                Game.LoadLevel(Levels.Create(id));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Game.Events.RaiseMessage(new MessageEvent { Text = "Level " + id + " failed to load: " + e.Message, Seconds = 10f });
                return false;
            }
        }

        public void NextLevel() => LoadLevel(Levels.After(LevelId));

        public void PreviousLevel() => LoadLevel(Levels.Before(LevelId));

        /// <summary>
        /// Turning autoplay on restarts the level, because a solution starts from the level's initial state.
        /// Turning it off hands the level over to the human as it is.
        /// </summary>
        public void SetAutoplay(bool on)
        {
            if (Game == null || Game.IsDisposed || Autoplay == on) return;
            Autoplay = on;
            if (on && Game.Level != null)
            {
                // The reload raises LevelLoaded, which puts the bot in charge.
                Game.RestartLevel();
            }
            else
            {
                TakeControl();
            }
        }

        /// <summary>Disposes the Game and everything created in Begin. Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (Game == null) return;
            Game game = Game;
            Game = null;
            autopilot = null;
            advancePending = false;

            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelCompleted -= OnLevelCompleted;
            if (Hud != null)
            {
                Hud.Unbind();
                Sim.Destroy(Hud);
                Hud = null;
            }
            Rig?.Dispose();
            Rig = null;
            Human?.ReleasePointer();
            if (!game.IsDisposed) game.Dispose();
        }

        void OnLevelLoaded(LevelEvent e)
        {
            advancePending = false;
            // A level may load its successor by itself; follow it if it is one of ours.
            if (e.Id >= 0 && Levels.Has(e.Id)) LevelId = e.Id;
            TakeControl();
        }

        void OnLevelCompleted(LevelEvent e)
        {
            advancePending = true;
            advanceTimer = AdvanceDelay;
            if (Hud != null)
            {
                int next = Levels.After(LevelId);
                Hud.BannerNote = next == LevelId ? "once more from the top" : "next level coming up";
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

        void PushHudState()
        {
            if (autopilot != null && autopilot.Error != null && !autopilot.Reported)
            {
                autopilot.Reported = true;
                Debug.LogWarning("[Toybox] autoplay failed: " + autopilot.Error.Message);
                Game.Events.RaiseMessage(new MessageEvent { Text = "Autoplay got stuck - press P to take over", Seconds = 8f });
            }
            if (Hud == null) return;
            Hud.Autoplay = Autoplay;
            // The prompt also goes away while the right-button fallback is in use.
            Hud.ClickToPlay = !Autoplay && !Human.PointerLocked && !Human.Frame.LookButton;
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
