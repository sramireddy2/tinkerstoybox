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
        /// <summary>
        /// The end of the game: what follows the completion of the campaign's last level. The level stays
        /// loaded behind it, standing still, until the player plays again, opens the catalogue or goes
        /// back to the title.
        /// </summary>
        Ending,
    }

    public struct FlowChange
    {
        public FlowState From, To;
    }

    /// <summary>
    /// What the ending has to say about the campaign: how much of it is collected, and what it took.
    /// For the player it is their saved progress (best runs, over as many sessions as it took); while a
    /// bot plays it is what was played since the game began, and nothing of it is kept.
    /// </summary>
    public struct CampaignReport
    {
        /// <summary>The levels of the campaign, in order.</summary>
        public int[] Levels;
        /// <summary>For each of them: completed.</summary>
        public bool[] Collected;
        public int CollectedCount;
        /// <summary>Seconds of the completed levels' runs added up.</summary>
        public float Seconds;
        /// <summary>Pick-ups of those runs added up.</summary>
        public int PickUps;
        /// <summary>A bot played: these are its runs of this session, not the player's progress.</summary>
        public bool Watched;

        public int Total => Levels != null ? Levels.Length : 0;
        public bool Full => Total > 0 && CollectedCount == Total;
    }

    /// <summary>
    /// Where the player is in the game, as a small state machine: Title, Playing, Paused, LevelSelect,
    /// LevelComplete, Ending. It owns which level is loaded, keeps the player's progress, and tells listeners when
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
        // What was completed since the game began, by whoever played: level id to the run's seconds and pick-ups.
        readonly System.Collections.Generic.Dictionary<int, Vector2> played = new System.Collections.Generic.Dictionary<int, Vector2>();
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
        /// <summary>How often a toy was picked up since the level was loaded, by whoever plays.</summary>
        public int PickUps { get; private set; }
        /// <summary>True while the level that is loaded is the campaign's last: what follows its completion is the ending.</summary>
        public bool AtLastLevel => Levels.InCampaign(LevelId) && LevelId == Levels.Last;

        public GameFlow(Game game, LevelList levels, Progress progress = null)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            Levels = levels ?? throw new ArgumentNullException(nameof(levels));
            Progress = progress ?? new Progress();
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelCompleted += OnLevelCompleted;
            game.Events.PropGrabbed += OnPropGrabbed;
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

        /// <summary>
        /// The level after the current one, from any state. After the last comes the first again: this is
        /// the step of the level keys. What follows a completion is <see cref="Advance"/>.
        /// </summary>
        public bool NextLevel() => Alive && StartLevel(Levels.After(LevelId));

        /// <summary>
        /// What follows a level's completion (the card's "Next", and the countdown): the next level of the
        /// campaign - or, when the level just completed was its last, the ending. The campaign does not
        /// wrap round. Anywhere but on a completed last level this is <see cref="NextLevel"/>.
        /// </summary>
        public bool Advance()
        {
            if (!Alive) return false;
            if (State == FlowState.LevelComplete && AtLastLevel) return ShowEnding();
            return NextLevel();
        }

        /// <summary>
        /// The end of the game, from any state: the level stays loaded behind it and stands still. For
        /// the player the bookmark goes back to the first level - a fresh game after the ending starts
        /// at the beginning, with everything that was collected kept.
        /// </summary>
        public bool ShowEnding()
        {
            if (!Alive || State == FlowState.Ending) return false;
            if (RecordProgress)
            {
                Progress.RecordEnding();
                Progress.LastPlayed = Levels.First;
            }
            Enter(FlowState.Ending);
            return true;
        }

        /// <summary>The campaign as the ending tells it: see <see cref="CampaignReport"/>.</summary>
        public CampaignReport Report()
        {
            if (RecordProgress) return ReportOf(Levels, Progress);
            System.Collections.Generic.IReadOnlyList<int> campaign = Levels.Campaign;
            var report = new CampaignReport { Levels = new int[campaign.Count], Collected = new bool[campaign.Count], Watched = true };
            for (int i = 0; i < campaign.Count; i++)
            {
                report.Levels[i] = campaign[i];
                if (!played.TryGetValue(campaign[i], out Vector2 run)) continue;
                report.Collected[i] = true;
                report.CollectedCount++;
                report.Seconds += run.x;
                report.PickUps += Mathf.RoundToInt(run.y);
            }
            return report;
        }

        /// <summary>The campaign as saved progress has it: what is collected, the best times and their pick-ups added up.</summary>
        public static CampaignReport ReportOf(LevelList levels, Progress progress)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            System.Collections.Generic.IReadOnlyList<int> campaign = levels.Campaign;
            var report = new CampaignReport { Levels = new int[campaign.Count], Collected = new bool[campaign.Count] };
            for (int i = 0; i < campaign.Count; i++)
            {
                int id = campaign[i];
                report.Levels[i] = id;
                if (!progress.IsCompleted(id)) continue;
                report.Collected[i] = true;
                report.CollectedCount++;
                report.Seconds += Mathf.Max(0f, progress.BestTime(id));
                report.PickUps += Mathf.Max(0, progress.PickUps(id));
            }
            return report;
        }

        /// <summary>The level before the current one (before the first comes the last), from any state.</summary>
        public bool PreviousLevel() => Alive && StartLevel(Levels.Before(LevelId));

        /// <summary>
        /// Back to the title. The level stays loaded behind it, standing still - except after the ending:
        /// there the title stands over the first level again, ready for a fresh game.
        /// </summary>
        public bool ReturnToTitle()
        {
            if (!Alive || State == FlowState.Title) return false;
            if (State == FlowState.Ending)
            {
                ShowTitle(Levels.First);
                return true;
            }
            Enter(FlowState.Title);
            return true;
        }

        /// <summary>Opens the catalogue from the title, the pause menu, the level-complete card or the ending.</summary>
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
            if (advanceTimer <= 0f) Advance();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelCompleted -= OnLevelCompleted;
            game.Events.PropGrabbed -= OnPropGrabbed;
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
            PickUps = 0;
            // A level may load its successor by itself; follow it if it is one of ours.
            if (e.Id >= 0 && Levels.Has(e.Id)) LevelId = e.Id;
            // A level that was restarted or replaced while its completion was being celebrated is played again.
            if (State == FlowState.LevelComplete) Enter(FlowState.Playing);
        }

        void OnLevelCompleted(LevelEvent e)
        {
            LastCompletion = e;
            if (Levels.InCampaign(LevelId)) played[LevelId] = new Vector2(Mathf.Max(0f, e.Time), PickUps);
            LastCompletionWasBest = RecordProgress && Levels.InCampaign(LevelId) && Progress.RecordCompletion(LevelId, e.Time, PickUps);
            // The card is the natural place to stop for the day, and a session that ends on it continues with
            // the level after the one just finished - not with that one again, as if it had not been saved.
            // (Not after the last: its bookmark stays until Finish, and the ending puts it back to the first.
            // After() would wrap round to the first here.)
            if (RecordProgress && Levels.InCampaign(LevelId) && LevelId != Levels.Last) Progress.LastPlayed = Levels.After(LevelId);
            Enter(FlowState.LevelComplete);
        }

        void OnPropGrabbed(PropHoldEvent e) => PickUps++;
    }
}
