using System;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Platform
{
    public enum FlowState
    {
        /// <summary>The title screen. A level is loaded behind it and stands still.</summary>
        Title,
        /// <summary>The only state in which the simulation ticks.</summary>
        Playing,
        Paused,
        /// <summary>The catalogue of levels, opened from the title, the pause menu or the level-complete card.</summary>
        LevelSelect,
        /// <summary>Between a level's completion and the start of the next one.</summary>
        LevelComplete,
    }

    public struct FlowChange
    {
        public FlowState From, To;
    }

    /// <summary>
    /// Where the player is in the game, as a small state machine: Title, Playing, Paused, LevelSelect,
    /// LevelComplete. It owns which level is loaded, keeps the player's progress, and tells listeners when
    /// the state changes. It decides nothing about input or pictures: GameRunner turns keys and clicks into
    /// calls on it and only ticks the simulation while the state is Playing; menus call the same methods.
    ///
    /// Every method is safe to call in any state; a call that makes no sense where the flow is does
    /// nothing and returns false.
    /// </summary>
    public sealed class GameFlow : IDisposable
    {
        /// <summary>Seconds between a level's completion and the automatic start of the next one.</summary>
        public const float DefaultAdvanceDelay = 2.5f;

        readonly Game game;
        FlowState returnTo = FlowState.Title;
        float advanceTimer;
        bool disposed;

        /// <summary>Raised after the state has changed.</summary>
        public event Action<FlowChange> StateChanged;

        public Game Game => game;
        public LevelList Levels { get; }
        public Progress Progress { get; }
        public FlowState State { get; private set; } = FlowState.Title;
        public bool Playing => State == FlowState.Playing;

        /// <summary>Id (in <see cref="Levels"/>) of the level that is loaded or was last asked for.</summary>
        public int LevelId { get; private set; } = -1;

        /// <summary>
        /// Completions count as the player's progress and the level started last is remembered. The runner
        /// turns this off while a bot plays.
        /// </summary>
        public bool RecordProgress { get; set; } = true;

        /// <summary>
        /// While true, LevelComplete moves on to the next level by itself after <see cref="AdvanceDelay"/>.
        /// A menu that shows a "Next" button turns it off.
        /// </summary>
        public bool AutoAdvance { get; set; } = true;
        /// <summary>Moves on by itself whatever <see cref="AutoAdvance"/> says (a bot is playing; set by the runner).</summary>
        public bool ForceAutoAdvance { get; set; }
        public float AdvanceDelay { get; set; } = DefaultAdvanceDelay;
        /// <summary>True while LevelComplete is counting down to the next level.</summary>
        public bool AdvancePending => State == FlowState.LevelComplete && (AutoAdvance || ForceAutoAdvance);
        /// <summary>Seconds left of that countdown.</summary>
        public float AdvanceIn => AdvancePending ? Mathf.Max(0f, advanceTimer) : 0f;

        /// <summary>The completion that put the flow into LevelComplete: level, time and ticks.</summary>
        public LevelEvent LastCompletion { get; private set; }
        /// <summary>Whether that completion was a new best time for its level.</summary>
        public bool LastCompletionWasBest { get; private set; }

        public GameFlow(Game game, LevelList levels, Progress progress = null)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            Levels = levels ?? throw new ArgumentNullException(nameof(levels));
            Progress = progress ?? new Progress();
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelCompleted += OnLevelCompleted;
        }

        /// <summary>The level a fresh session opens with: the campaign level played last, or the first.</summary>
        public int ContinueLevel
        {
            get
            {
                int last = Progress.LastPlayed;
                return Levels.InCampaign(last) ? last : Levels.First;
            }
        }

        /// <summary>
        /// Shows the title with a level standing still behind it (the one to continue with unless another
        /// is named). Returns whether the level loaded.
        /// </summary>
        public bool ShowTitle(int backdropLevel = -1)
        {
            if (!Alive) return false;
            int id = Levels.Has(backdropLevel) ? backdropLevel : ContinueLevel;
            bool loaded = Load(id, false);
            Enter(FlowState.Title);
            return loaded;
        }

        /// <summary>
        /// Loads a level of the list and plays it, from any state. A level that fails to build is reported
        /// and leaves the game without a level rather than taking the game loop down. Returns whether it
        /// loaded. Starting the level that already stands untouched behind the title does not load it again.
        /// </summary>
        public bool StartLevel(int id)
        {
            if (!Alive) return false;
            bool fresh = State != FlowState.Playing && id == LevelId && game.Level != null && game.LevelTicks == 0 && !game.LevelCompleted;
            bool loaded = fresh || Load(id, true);
            // The bookmark only moves within the campaign: a visit to the sandbox does not lose the player's place.
            if (loaded && RecordProgress && Levels.InCampaign(id)) Progress.LastPlayed = id;
            Enter(FlowState.Playing);
            return loaded;
        }

        /// <summary>Starts the current level over and plays it, from any state.</summary>
        public bool Restart()
        {
            if (!Alive) return false;
            if (game.Level != null) game.RestartLevel();
            Enter(FlowState.Playing);
            return game.Level != null;
        }

        /// <summary>Playing to Paused.</summary>
        public bool Pause()
        {
            if (!Alive || State != FlowState.Playing) return false;
            Enter(FlowState.Paused);
            return true;
        }

        /// <summary>Paused to Playing.</summary>
        public bool Resume()
        {
            if (!Alive || State != FlowState.Paused) return false;
            Enter(FlowState.Playing);
            return true;
        }

        /// <summary>The level after the current one (after the last comes the first again), from any state.</summary>
        public bool NextLevel() => Alive && StartLevel(Levels.After(LevelId));

        /// <summary>The level before the current one (before the first comes the last), from any state.</summary>
        public bool PreviousLevel() => Alive && StartLevel(Levels.Before(LevelId));

        /// <summary>Back to the title. The level stays loaded behind it, standing still.</summary>
        public bool ReturnToTitle()
        {
            if (!Alive || State == FlowState.Title) return false;
            Enter(FlowState.Title);
            return true;
        }

        /// <summary>Opens the catalogue from the title, the pause menu or the level-complete card.</summary>
        public bool OpenLevelSelect()
        {
            if (!Alive || State == FlowState.LevelSelect || State == FlowState.Playing) return false;
            returnTo = State;
            Enter(FlowState.LevelSelect);
            return true;
        }

        /// <summary>Closes the catalogue without choosing: back to where it was opened from.</summary>
        public bool CloseLevelSelect()
        {
            if (!Alive || State != FlowState.LevelSelect) return false;
            Enter(returnTo);
            return true;
        }

        /// <summary>Once per rendered frame, with the real time that passed: runs the LevelComplete countdown.</summary>
        public void Update(float dt)
        {
            if (!Alive || !AdvancePending) return;
            if (dt > 0f) advanceTimer -= dt;
            if (advanceTimer <= 0f) NextLevel();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelCompleted -= OnLevelCompleted;
        }

        bool Alive => !disposed && !game.IsDisposed;

        bool Load(int id, bool report)
        {
            LevelId = id;
            try
            {
                game.LoadLevel(Levels.Create(id));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (report && !game.IsDisposed)
                    game.Events.RaiseMessage(new MessageEvent { Text = "Level " + id + " failed to load: " + e.Message, Seconds = 10f });
                return false;
            }
        }

        void Enter(FlowState state)
        {
            if (state == FlowState.LevelComplete) advanceTimer = AdvanceDelay;
            if (State == state) return;
            var change = new FlowChange { From = State, To = state };
            State = state;
            Action<FlowChange> handlers = StateChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                // A faulty listener must neither stop the flow nor starve the other listeners.
                try
                {
                    ((Action<FlowChange>)handler)(change);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        void OnLevelLoaded(LevelEvent e)
        {
            // A level may load its successor by itself; follow it if it is one of ours.
            if (e.Id >= 0 && Levels.Has(e.Id)) LevelId = e.Id;
            // A level that was restarted or replaced while its completion was being celebrated is played again.
            if (State == FlowState.LevelComplete) Enter(FlowState.Playing);
        }

        void OnLevelCompleted(LevelEvent e)
        {
            LastCompletion = e;
            LastCompletionWasBest = RecordProgress && Levels.InCampaign(LevelId) && Progress.RecordCompletion(LevelId, e.Time);
            Enter(FlowState.LevelComplete);
        }
    }
}
