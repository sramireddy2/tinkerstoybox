using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Toybox.Audio
{
    /// <summary>The two sounds a menu makes.</summary>
    public enum UiSound
    {
        Hover,
        Click,
    }

    /// <summary>
    /// The game's sound (ART_BIBLE 11): it listens to the simulation's events and to the game flow and turns
    /// them into effects and music. It provides neither look nor HUD, so it runs with the plain look too.
    ///
    /// - Clips come from the <see cref="SoundBank"/>, synthesised 4 ms a frame behind the title screen.
    /// - Nothing sounds before the player's first click or key press (<see cref="Unlocked"/>): a browser
    ///   only starts audio after a gesture, and whatever was started earlier would burst out at once.
    /// - The game's sounds pause with the game; menu sounds do not. The music fades out and comes back in
    ///   at the start of its two-bar section.
    /// - Volumes come from <see cref="Settings"/>: the master volume is the AudioListener's, effects and
    ///   music scale their own voices.
    ///
    /// Outside Play Mode there are no AudioSources and no clips are made (see <see cref="AudioOutput"/>):
    /// tests and the screenshot tool run the same decisions silently.
    /// </summary>
    [Presenter(300)]
    public sealed class AudioPresenter : IPresenter
    {
        /// <summary>What a test or tool may decide instead of the defaults. Set before the presentation is created.</summary>
        public sealed class Options
        {
            /// <summary>Create AudioSources (default: only in Play Mode).</summary>
            public bool? Audible;
            /// <summary>Create them muted.</summary>
            public bool Muted;
            /// <summary>The bank to play from (default: the shared one where audible, none otherwise).</summary>
            public SoundBank Bank;
            /// <summary>The audio clock (default: AudioSettings.dspTime where audible, the sum of the frame times otherwise).</summary>
            public Func<double> Clock;
            /// <summary>Start unlocked, as if the player had already clicked.</summary>
            public bool Unlocked;
        }

        /// <summary>Overrides for the next presenter that attaches; null for the defaults.</summary>
        public static Options Overrides;

        /// <summary>The presenter of the running game, for callers without a PresentationContext; null if there is none.</summary>
        public static AudioPresenter Active { get; private set; }

        /// <summary>Milliseconds of synthesis per frame while the bank is not complete.</summary>
        public const double GenerationBudgetMs = 4.0;
        /// <summary>The hold tone reaches a new pitch in about this long.</summary>
        public const float HoldPortamento = 0.06f;
        /// <summary>The hold tone fades in and out over this long.</summary>
        public const float HoldFade = 0.04f;
        /// <summary>A scale jump of more than this in one tick ticks.</summary>
        public const float JumpThreshold = 0.05f;
        /// <summary>Seconds the music takes to fade out when the game pauses or the level ends.</summary>
        public const float MusicFadeOut = 0.25f;
        public const int MaxLandsPerFrame = 4;
        /// <summary>Landings quieter than this are not played.</summary>
        public const float MinLandVolume = 0.03f;
        /// <summary>The player's own landing is silent below this speed and full at <see cref="PlayerLandFullSpeed"/>.</summary>
        public const float PlayerLandMinSpeed = 2f, PlayerLandFullSpeed = 14f;

        readonly MusicBox music = new MusicBox();
        readonly List<MusicEvent> notes = new List<MusicEvent>(32);
        Game game;
        PresentationContext context;
        AudioOutput output;
        SoundBank bank;
        GadgetSounds gadgets;
        Func<double> clock;
        double silentClock;
        bool audible, attached, disposed;
        float previousListenerVolume;
        AudioListener ownListener;

        MusicScore score;
        MusicKey key = MusicKey.CMajor;
        bool musicBegun, levelDone;
        float musicFadeTarget = 1f;

        Prop focus;
        bool holding;
        float heldDiameter = 1f, heldScale;
        float holdSemis, holdGain;
        double lastJump = double.NegativeInfinity;
        int landsThisFrame;

        /// <summary>The player has clicked or pressed a key: sound may start.</summary>
        public bool Unlocked { get; private set; }
        /// <summary>The voices, and the account of what was played.</summary>
        public AudioOutput Output => output;
        /// <summary>The bank the clips come from; null in a silent run.</summary>
        public SoundBank Bank => bank;
        /// <summary>The music's clock.</summary>
        public MusicBox Music => music;
        /// <summary>The tune of the loaded level.</summary>
        public MusicScore Score => score;
        /// <summary>The key the loaded level's sound is in.</summary>
        public MusicKey Key => key;
        /// <summary>The audio clock as of the last frame.</summary>
        public double Now => output != null ? output.Now : 0.0;

        // ---- Lifecycle ----------------------------------------------------------------------------------------

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            Options options = Overrides ?? new Options();
            audible = options.Audible ?? Application.isPlaying;
            bank = options.Bank ?? (audible ? SoundBank.Shared : null);
            clock = options.Clock;
            output = new AudioOutput(bank, context.Root, audible, options.Muted);
            output.Now = Clock(0f);

            if (audible)
            {
                previousListenerVolume = AudioListener.volume;
                // The camera rig brings a listener in Play Mode; make sure there is one either way.
                if (context.Camera != null && context.Camera.GetComponent<AudioListener>() == null)
                    ownListener = context.Camera.gameObject.AddComponent<AudioListener>();
            }
            ApplyVolumes();

            GameEvents events = game.Events;
            events.LevelLoaded += OnLevelLoaded;
            events.LevelUnloading += OnLevelUnloading;
            events.LevelCompleted += OnLevelCompleted;
            events.PropGrabbed += OnPropGrabbed;
            events.PropHeld += OnPropHeld;
            events.PropDropped += OnPropDropped;
            events.PropImpact += OnPropImpact;
            events.PlayerLanded += OnPlayerLanded;
            events.ExitLockChanged += OnExitLockChanged;
            Settings.Changed += OnSettingChanged;
            if (context.Flow != null) context.Flow.StateChanged += OnStateChanged;
            gadgets = GadgetSounds.Bind(events, OnGadget);
            attached = true;
            Active = this;

            if (options.Unlocked) Unlock();
            // Attached to a game that already has a level (the screenshot tool): as if it had just loaded.
            if (game.Level != null) BeginLevel();
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached || disposed) return;
            double now = Clock(dt);
            output.Now = now;
            landsThisFrame = 0;

            if (bank != null && !bank.Complete) bank.Pump(GenerationBudgetMs);
            if (!Unlocked && Gesture()) Unlock();

            bool playing = context.State == FlowState.Playing;
            if (Unlocked && playing)
            {
                FocusTick();
                RunMusic(now);
            }
            HoldTone(dt, now, playing);

            // The music's fade: quick out, and gone once it is silent.
            if (output.MusicFade != musicFadeTarget)
            {
                float step = dt / MusicFadeOut;
                output.MusicFade = musicFadeTarget > output.MusicFade ? 1f : Mathf.Max(musicFadeTarget, output.MusicFade - step);
                if (output.MusicFade <= 0f) output.StopMusic();
            }
            output.Apply();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Active == this) Active = null;
            // Also after an Attach that failed half way: whatever was subscribed by then is left again.
            if (game != null)
            {
                GameEvents events = game.Events;
                events.LevelLoaded -= OnLevelLoaded;
                events.LevelUnloading -= OnLevelUnloading;
                events.LevelCompleted -= OnLevelCompleted;
                events.PropGrabbed -= OnPropGrabbed;
                events.PropHeld -= OnPropHeld;
                events.PropDropped -= OnPropDropped;
                events.PropImpact -= OnPropImpact;
                events.PlayerLanded -= OnPlayerLanded;
                events.ExitLockChanged -= OnExitLockChanged;
            }
            Settings.Changed -= OnSettingChanged;
            if (context != null && context.Flow != null) context.Flow.StateChanged -= OnStateChanged;
            gadgets?.Unbind();
            gadgets = null;

            music.Stop();
            if (output != null)
            {
                output.Dispose();
                if (audible) AudioListener.volume = previousListenerVolume;
            }
            if (ownListener != null) Sim.Destroy(ownListener);
            ownListener = null;
        }

        double Clock(float dt)
        {
            if (clock != null) return clock();
            if (audible) return AudioSettings.dspTime;
            silentClock += dt > 0f ? dt : 0f;
            return silentClock;
        }

        // ---- Unlocking ----------------------------------------------------------------------------------------

        /// <summary>
        /// Lets sound start. Called by itself on the player's first click or key press; a menu may call it
        /// from its own click handler ("Click to play").
        /// </summary>
        public void Unlock()
        {
            if (Unlocked) return;
            Unlocked = true;
        }

        // A user gesture this frame: the pointer was captured, the look button is down, or a button or key
        // is. The devices are only read where there is a player in front of them.
        bool Gesture()
        {
            if (context.PointerLocked || context.LookHeld) return true;
            if (!audible || !Application.isPlaying) return false;
            try
            {
                Mouse mouse = Mouse.current;
                if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed)) return true;
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.anyKey.isPressed) return true;
                Touchscreen touch = Touchscreen.current;
                if (touch != null && touch.primaryTouch.press.isPressed) return true;
            }
            catch (Exception)
            {
                // A device that cannot be read is no gesture.
            }
            return false;
        }

        /// <summary>Sound may be made for the game right now: unlocked and not torn down.</summary>
        bool Sounding => Unlocked && !disposed && output != null;

        // ---- What others may ask for --------------------------------------------------------------------------

        /// <summary>A menu sound, in any flow state (once unlocked). The click is tuned to the level's key.</summary>
        public void PlayUi(UiSound sound)
        {
            if (!Sounding) return;
            if (sound == UiSound.Hover) output.Play(SoundId.UiHover);
            else output.Play(SoundId.UiClick, 1f, Synth.Ratio(key.Transpose));
        }

        /// <summary>Plays a clip in 2D (once unlocked). Returns the voice, or -1.</summary>
        public int Play(SoundId id, float volume = 1f, float pitch = 1f) => Sounding ? output.Play(id, volume, pitch) : -1;

        /// <summary>Plays a clip at a world position (once unlocked). Returns the voice, or -1.</summary>
        public int PlayAt(SoundId id, Vector3 position, float volume = 1f, float pitch = 1f) => Sounding ? output.PlayAt(id, position, volume, pitch) : -1;

        /// <summary>The rate that moves a clip written with its tonic on <paramref name="writtenTonic"/> (a pitch class) into the level's key.</summary>
        public float KeyPitch(int writtenTonic = 0)
        {
            int shift = (((key.Root - writtenTonic) % 12) + 12) % 12;
            if (shift > 6) shift -= 12;
            return Synth.Ratio(shift);
        }

        // ---- Levels and flow ----------------------------------------------------------------------------------

        void OnLevelLoaded(LevelEvent e) => BeginLevel();

        void BeginLevel()
        {
            EnvironmentPreset preset = game.Environment != null ? game.Environment.Preset : EnvironmentPreset.None;
            MusicScore next = MusicScore.For(game.Level, preset);
            bool sameTune = score != null && next.Seed == score.Seed && next.Key == score.Key && next.Bpm == score.Bpm && next.Night == score.Night;
            key = next.Key;

            // A restart of the same level keeps its tune going; another level begins its own.
            if (!sameTune || levelDone)
            {
                music.Stop();
                output.StopMusic();
                score = next;
                musicBegun = false;
            }
            if (bank != null && bank.SetKey(key, next.Bpm)) output.StopMusic();

            // What was paused belongs to the level that is gone.
            output.DropPaused();
            levelDone = false;
            musicFadeTarget = 1f;
            output.MusicFade = 1f;
            // The toy kit enters after the first grab of the level.
            music.Kit = false;
            music.Holding = false;
            focus = null;
            EndHold();
        }

        void OnLevelUnloading(LevelEvent e)
        {
            focus = null;
            EndHold();
        }

        void OnLevelCompleted(LevelEvent e)
        {
            levelDone = true;
            EndHold();
            music.Stop();
            musicFadeTarget = 0f;
            if (!Sounding) return;
            // Shutter, arpeggio, swell and stamp, rendered in this level's key and tempo.
            output.Play(SoundId.LevelComplete);
        }

        void OnStateChanged(FlowChange change)
        {
            if (disposed) return;
            if (change.To == FlowState.Playing)
            {
                output.ResumeEffects();
                musicFadeTarget = levelDone ? 0f : 1f;
            }
            else if (change.From == FlowState.Playing && change.To != FlowState.LevelComplete)
            {
                // Paused, the title, the level select: the game's sounds wait, the music fades out.
                output.PauseEffects();
                music.Stop();
                musicFadeTarget = 0f;
            }
        }

        void OnSettingChanged(Setting setting)
        {
            if (setting == Setting.MasterVolume || setting == Setting.MusicVolume || setting == Setting.SfxVolume) ApplyVolumes();
        }

        void ApplyVolumes()
        {
            output.SfxVolume = Settings.SfxVolume;
            output.MusicVolume = Settings.MusicVolume;
            // "AudioListener.volume = 0.8" of the gain staging is the master volume's default.
            if (audible) AudioListener.volume = Settings.MasterVolume;
        }

        // ---- Music -------------------------------------------------------------------------------------------

        bool MusicReady => bank == null || (bank.KeyReady && bank.Ready(SoundId.Box8) && bank.Ready(SoundId.KitKick));

        void RunMusic(double now)
        {
            if (score == null || levelDone) return;
            if (!music.Running)
            {
                if (!MusicReady) return;
                if (musicBegun) music.Resume(now);
                else music.Start(score, now);
                musicBegun = true;
                musicFadeTarget = 1f;
                output.MusicFade = 1f;
            }
            notes.Clear();
            music.Advance(now, notes);
            for (int i = 0; i < notes.Count; i++) output.Schedule(notes[i]);
        }

        /// <summary>The chord sounding now (the first before the tune starts).</summary>
        public int CurrentChord => musicBegun ? music.ChordAt(Now) : 0;

        // ---- Mechanic feedback ---------------------------------------------------------------------------------

        // A 25 ms tick when the aim ray comes to rest on a toy that can be grabbed.
        void FocusTick()
        {
            if (game.Level == null || game.Grabber.IsHolding)
            {
                focus = null;
                return;
            }
            Prop now = game.Grabber.Focus;
            if (now != null && now != focus) output.Play(SoundId.FocusTick);
            focus = now;
        }

        void OnPropGrabbed(PropHoldEvent e)
        {
            holding = true;
            music.Holding = true;
            music.Kit = true;
            heldDiameter = Diameter(e);
            heldScale = e.Scale;
            // The tone starts on its note; from here it glides.
            holdSemis = PitchLaw.HoldSemis(heldDiameter, key, CurrentChord);
            if (!Sounding) return;
            output.Play(SoundId.Grab, 1f, PitchLaw.Pitch(PitchLaw.SnappedSemis(heldDiameter, PitchLaw.GrabNote, key)));
        }

        void OnPropHeld(PropHoldEvent e)
        {
            if (!holding) return;
            float scale = e.Scale;
            // The toy jumped to another surface: say that it happened, not where.
            if (Sounding && heldScale > 0f && Mathf.Abs(scale / heldScale - 1f) > JumpThreshold && Now - lastJump > 0.05)
            {
                output.Play(SoundId.HoldJump);
                lastJump = Now;
            }
            heldScale = scale;
            heldDiameter = Diameter(e);
        }

        void OnPropDropped(PropHoldEvent e)
        {
            bool wasHolding = holding;
            float diameter = Diameter(e);
            int chord = CurrentChord;
            EndHold();
            if (!Sounding || !wasHolding) return;
            // The thock is the toy's true size, tuned to the chord under it.
            output.Play(SoundId.ReleaseThock, 1f, PitchLaw.Pitch(PitchLaw.ThockSemis(diameter, key, chord)));
            if (e.Factor > 2f) output.Play(SoundId.ReleaseSub);
            else if (e.Factor < 0.5f) output.Play(SoundId.ReleaseBell);
        }

        static float Diameter(PropHoldEvent e) => Mathf.Max(2f * e.Radius, 1e-4f);

        void EndHold()
        {
            holding = false;
            music.Holding = false;
        }

        // The hold loop: its pitch follows the toy's true size, snapped to the pentatonic of the chord that
        // is sounding, with 60 ms of portamento. While it sounds the music box rests: it is the lead.
        void HoldTone(float dt, double now, bool playing)
        {
            // While the game stands still the loop is paused along with the other game sounds.
            if (!playing && output.EffectsPaused) return;
            bool on = holding && Sounding && playing;
            holdGain = Mathf.MoveTowards(holdGain, on ? 1f : 0f, dt > 0f ? dt / HoldFade : 1f);
            if (on)
            {
                float target = PitchLaw.HoldSemis(heldDiameter, key, CurrentChord);
                // Three time constants to the portamento: 95% of the way there in 60 ms.
                holdSemis += (target - holdSemis) * (1f - Mathf.Exp(-dt * 3f / HoldPortamento));
            }
            output.Hold(holdGain, PitchLaw.Pitch(holdSemis));
        }

        void OnPropImpact(PropImpactEvent e)
        {
            if (!Sounding || e.Prop == null || e.Prop.Removed || e.Prop.Held) return;
            if (landsThisFrame >= MaxLandsPerFrame) return;
            float volume = PitchLaw.LandVolume(e.Speed, e.Mass);
            if (volume < MinLandVolume) return;
            // A pile coming down in one frame must not add up past full scale: each further landing is quieter.
            volume /= 1f + 0.5f * landsThisFrame;
            landsThisFrame++;

            float diameter = 2f * e.Prop.Radius;
            ToyInfo info = ToyInfo.Of(e.Prop.GameObject);
            LandSound material = info != null && info.Recipe != null ? info.Recipe.Sound : LandSound.Plastic;
            output.PlayAt(Sounds.Land(material), e.Point, volume, PitchLaw.LandPitch(diameter));
            // Something heavy came down: the floor answers.
            float sub = PitchLaw.LandSubVolume(e.Speed, e.Mass);
            if (sub > MinLandVolume) output.PlayAt(SoundId.ReleaseSub, e.Point, sub);
        }

        void OnPlayerLanded(PlayerLandEvent e)
        {
            if (!Sounding || e.ImpactSpeed < PlayerLandMinSpeed) return;
            output.Play(SoundId.PlayerLand, Mathf.Clamp01(e.ImpactSpeed / PlayerLandFullSpeed));
        }

        void OnExitLockChanged(ExitEvent e)
        {
            // A level locks its exits while it is being built; that is not news.
            if (!Sounding || e.Exit == null || game.Level == null || game.LevelTicks < 2) return;
            output.PlayAt(e.Locked ? SoundId.ExitClose : SoundId.ExitOpen, e.Exit.Position, 1f, KeyPitch());
        }

        // A gadget's event (see GadgetSounds). The button's two notes are a fourth, written E5-A5; A is moved
        // onto the key's root, so it is sol-do in every mode. The exit's chimes are written on C and moved
        // the same way; landings, the bell and the pluck are not tuned.
        void OnGadget(SoundId sound, bool placed, Vector3 position)
        {
            if (!Sounding || context.State != FlowState.Playing) return;
            float pitch = sound == SoundId.ButtonPress || sound == SoundId.ButtonRelease ? KeyPitch(9)
                : sound == SoundId.ExitOpen || sound == SoundId.ExitClose ? KeyPitch() : 1f;
            if (placed) output.PlayAt(sound, position, 1f, pitch);
            else output.Play(sound, 1f, pitch);
        }
    }
}
