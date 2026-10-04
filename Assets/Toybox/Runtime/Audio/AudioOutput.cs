using System;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Audio
{
    /// <summary>A sound that was started, as the output remembers it (for tests and for a debug overlay).</summary>
    public struct PlayedSound
    {
        public SoundId Id;
        /// <summary>Audio-clock time it starts at (later than "now" for a scheduled note).</summary>
        public double Time;
        /// <summary>The gain asked for, before the settings' volumes.</summary>
        public float Volume;
        public float Pitch;
        public bool Spatial;
        public Vector3 Position;
        public bool Scheduled;
    }

    /// <summary>
    /// The voices (ART_BIBLE 11.1): 12 pooled sources for effects, 12 for music and 1 for the hold loop.
    /// Pitch and loudness are the only things changed at play time.
    ///
    /// It keeps its own account of every voice - what it plays, since when, until when - and drives
    /// AudioSources from that account only where it was created <c>audible</c> (Play Mode and the build).
    /// Outside Play Mode there are no sources at all: tests and the screenshot tool run the very same
    /// decisions without a sound leaving the machine, and read them back from <see cref="Count"/> and
    /// <see cref="Last"/>.
    /// </summary>
    public sealed class AudioOutput : IDisposable
    {
        public const int EffectVoices = 12;
        public const int LeadVoices = 4, PadVoices = 2, BassVoices = 2, KitVoices = 4;
        public const int MusicVoices = LeadVoices + PadVoices + BassVoices + KitVoices;
        /// <summary>Landing sounds are 3D with a linear roll-off between these distances.</summary>
        public const float SpatialMin = 4f, SpatialMax = 160f;
        /// <summary>Length assumed for a sound the bank has not generated (silent runs).</summary>
        public const float NominalSeconds = 1f;
        const int HistorySize = 256;

        sealed class Voice
        {
            public AudioSource Source;
            public SoundId Sound;
            public double Start = double.NegativeInfinity, End = double.NegativeInfinity;
            public float Gain, Pitch = 1f;
            public bool Paused;
            public double Remaining;
            public SoundGroup Group;
        }

        static readonly int[] GroupStart = { 0, LeadVoices, LeadVoices + PadVoices, LeadVoices + PadVoices + BassVoices };
        static readonly int[] GroupSize = { LeadVoices, PadVoices, BassVoices, KitVoices };

        readonly SoundBank bank;
        readonly Voice[] effects = new Voice[EffectVoices];
        readonly Voice[] music = new Voice[MusicVoices];
        readonly Voice hold = new Voice { Group = SoundGroup.Effect };
        readonly PlayedSound[] history = new PlayedSound[HistorySize];
        readonly int[] counts = new int[Sounds.Count];
        GameObject root;
        float sfxVolume = 1f, musicVolume = 1f, musicFade = 1f;
        float holdGain, holdPitch = 1f;
        bool musicDirty;

        /// <summary>There are AudioSources behind the voices.</summary>
        public bool Audible { get; }
        // The sources are still there (the game's root, and they with it, can be destroyed before the output is disposed).
        bool Sourced => Audible && root != null;
        /// <summary>The audio clock, set by the owner every frame (AudioSettings.dspTime where there is sound).</summary>
        public double Now { get; set; }
        /// <summary>Sounds started so far, scheduled notes included.</summary>
        public int Played { get; private set; }
        /// <summary>Sounds that were asked for before the bank had them.</summary>
        public int Skipped { get; private set; }
        /// <summary>Effects that took over a voice that was still sounding (more than twelve at once).</summary>
        public int Stolen { get; private set; }
        /// <summary>Music notes that cut another note of their layer short (it would still have sounded when they start).</summary>
        public int Cuts { get; private set; }
        public bool HoldPlaying { get; private set; }
        public float HoldPitch => holdPitch;
        public float HoldGain => holdGain;
        public bool EffectsPaused { get; private set; }

        /// <param name="bank">Where clips and their lengths come from; may be null for a silent output.</param>
        /// <param name="parent">Parent for the source objects.</param>
        /// <param name="audible">Create AudioSources and play through them.</param>
        /// <param name="muted">Create them muted (tests of the real path).</param>
        public AudioOutput(SoundBank bank, Transform parent, bool audible, bool muted = false)
        {
            this.bank = bank;
            Audible = audible;
            for (int i = 0; i < effects.Length; i++) effects[i] = new Voice { Group = SoundGroup.Effect };
            for (int i = 0; i < music.Length; i++) music[i] = new Voice { Group = SoundGroup.Music };
            if (!audible) return;

            root = new GameObject("Audio") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            for (int i = 0; i < effects.Length; i++) effects[i].Source = MakeSource("Effect " + i, muted);
            for (int i = 0; i < music.Length; i++) music[i].Source = MakeSource("Music " + i, muted);
            hold.Source = MakeSource("Hold", muted);
            hold.Source.loop = true;
        }

        AudioSource MakeSource(string name, bool muted)
        {
            var holder = new GameObject(name) { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(root.transform, false);
            AudioSource source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.mute = muted;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = SpatialMin;
            source.maxDistance = SpatialMax;
            source.reverbZoneMix = 0f;
            return source;
        }

        // ---- Volumes ----------------------------------------------------------------------------------------

        /// <summary>The settings' effect volume: multiplies every effect voice and the hold loop.</summary>
        public float SfxVolume
        {
            get => sfxVolume;
            set
            {
                if (sfxVolume == value) return;
                sfxVolume = value;
                if (!Sourced) return;
                for (int i = 0; i < effects.Length; i++) effects[i].Source.volume = effects[i].Gain * sfxVolume;
                hold.Source.volume = holdGain * sfxVolume;
            }
        }

        /// <summary>The settings' music volume.</summary>
        public float MusicVolume
        {
            get => musicVolume;
            set
            {
                if (musicVolume == value) return;
                musicVolume = value;
                musicDirty = true;
            }
        }

        /// <summary>A fade on everything the music voices play, 0..1 (the owner ramps it around pauses and level ends).</summary>
        public float MusicFade
        {
            get => musicFade;
            set
            {
                value = Mathf.Clamp01(value);
                if (musicFade == value) return;
                musicFade = value;
                musicDirty = true;
            }
        }

        /// <summary>Pushes changed music volumes to the sources. Once per frame.</summary>
        public void Apply()
        {
            if (!musicDirty) return;
            musicDirty = false;
            if (!Sourced) return;
            for (int i = 0; i < music.Length; i++) music[i].Source.volume = music[i].Gain * musicVolume * musicFade;
        }

        // ---- Effects ------------------------------------------------------------------------------------------

        /// <summary>Plays a clip in 2D. Returns the voice, or -1 if the bank does not have the clip yet.</summary>
        public int Play(SoundId id, float volume = 1f, float pitch = 1f) => Start(id, volume, pitch, false, default);

        /// <summary>Plays a clip in 3D at a world position (spatial blend 1, linear roll-off 4-160).</summary>
        public int PlayAt(SoundId id, Vector3 position, float volume = 1f, float pitch = 1f) => Start(id, volume, pitch, true, position);

        int Start(SoundId id, float volume, float pitch, bool spatial, Vector3 position)
        {
            if (id == SoundId.None || !(volume > 0f)) return -1;
            if (!Length(id, out float seconds))
            {
                Skipped++;
                return -1;
            }
            pitch = Mathf.Clamp(pitch, 0.05f, PitchLaw.MaxPitch);
            volume = Mathf.Clamp01(volume);

            // A voice that has finished; else the one that started longest ago; one that is paused only if
            // there is no other (a menu click while the game's sounds wait must not cut one of them short).
            int chosen = -1;
            for (int i = 0; i < effects.Length && chosen < 0; i++)
                if (!effects[i].Paused && effects[i].End <= Now) chosen = i;
            if (chosen < 0)
            {
                for (int i = 0; i < effects.Length; i++)
                    if (!effects[i].Paused && (chosen < 0 || effects[i].Start < effects[chosen].Start)) chosen = i;
            }
            if (chosen < 0)
            {
                for (int i = 0; i < effects.Length; i++)
                    if (chosen < 0 || effects[i].Start < effects[chosen].Start) chosen = i;
            }

            Voice voice = effects[chosen];
            if (voice.Paused || voice.End > Now) Stolen++;
            voice.Sound = id;
            voice.Start = Now;
            voice.End = Now + seconds / pitch;
            voice.Gain = volume;
            voice.Pitch = pitch;
            voice.Paused = false;
            voice.Group = Sounds.Spec(id).Group;
            if (Sourced)
            {
                AudioSource source = voice.Source;
                source.Stop();
                source.clip = bank.Clip(id);
                source.pitch = pitch;
                source.volume = volume * sfxVolume;
                source.spatialBlend = spatial ? 1f : 0f;
                source.transform.position = spatial ? position : Vector3.zero;
                source.Play();
            }
            Remember(new PlayedSound { Id = id, Time = Now, Volume = volume, Pitch = pitch, Spatial = spatial, Position = position });
            return chosen;
        }

        // The length of a clip in seconds. Without a bank (or without clips to wait for) every sound exists.
        bool Length(SoundId id, out float seconds)
        {
            seconds = bank != null ? bank.Seconds(id) : 0f;
            if (seconds > 0f) return true;
            seconds = NominalSeconds;
            return !Audible;
        }

        /// <summary>
        /// The game stands still: every game sound that is playing waits (UI sounds carry on), and the hold
        /// loop with them.
        /// </summary>
        public void PauseEffects()
        {
            if (EffectsPaused) return;
            EffectsPaused = true;
            for (int i = 0; i < effects.Length; i++)
            {
                Voice voice = effects[i];
                if (voice.Group == SoundGroup.Ui || voice.End <= Now) continue;
                voice.Paused = true;
                voice.Remaining = voice.End - Now;
                if (Sourced) voice.Source.Pause();
            }
            if (HoldPlaying && Sourced) hold.Source.Pause();
        }

        public void ResumeEffects()
        {
            if (!EffectsPaused) return;
            EffectsPaused = false;
            for (int i = 0; i < effects.Length; i++)
            {
                Voice voice = effects[i];
                if (!voice.Paused) continue;
                voice.Paused = false;
                voice.End = Now + voice.Remaining;
                if (Sourced) voice.Source.UnPause();
            }
            if (HoldPlaying && Sourced) hold.Source.UnPause();
        }

        /// <summary>Stops the voices that are waiting for the game to go on (their level is gone); the rest ring out.</summary>
        public void DropPaused()
        {
            for (int i = 0; i < effects.Length; i++)
            {
                Voice voice = effects[i];
                if (!voice.Paused) continue;
                voice.Paused = false;
                voice.End = double.NegativeInfinity;
                if (Sourced) voice.Source.Stop();
            }
        }

        /// <summary>Stops every effect voice at once.</summary>
        public void StopEffects()
        {
            for (int i = 0; i < effects.Length; i++)
            {
                Voice voice = effects[i];
                voice.Paused = false;
                voice.End = double.NegativeInfinity;
                if (Sourced) voice.Source.Stop();
            }
        }

        // ---- The hold loop --------------------------------------------------------------------------------------

        /// <summary>
        /// Starts the hold loop if it is not running and sets its gain and pitch (the owner ramps both every
        /// frame: 60 ms of portamento, a short fade in and out). Never spatialised. A gain of 0 stops it.
        /// </summary>
        public void Hold(float gain, float pitch)
        {
            gain = Mathf.Clamp01(gain);
            pitch = Mathf.Clamp(pitch, 0.05f, PitchLaw.MaxPitch);
            if (gain <= 0f)
            {
                StopHold();
                return;
            }
            if (!HoldPlaying)
            {
                if (!Length(SoundId.Hold, out _))
                {
                    return;
                }
                HoldPlaying = true;
                hold.Sound = SoundId.Hold;
                hold.Start = Now;
                if (Sourced)
                {
                    hold.Source.clip = bank.Clip(SoundId.Hold);
                    hold.Source.pitch = pitch;
                    hold.Source.volume = gain * sfxVolume;
                    hold.Source.Play();
                    if (EffectsPaused) hold.Source.Pause();
                }
                Remember(new PlayedSound { Id = SoundId.Hold, Time = Now, Volume = gain, Pitch = pitch });
            }
            else if (Sourced)
            {
                if (pitch != holdPitch) hold.Source.pitch = pitch;
                if (gain != holdGain) hold.Source.volume = gain * sfxVolume;
            }
            holdGain = gain;
            holdPitch = pitch;
        }

        public void StopHold()
        {
            if (!HoldPlaying) return;
            HoldPlaying = false;
            holdGain = 0f;
            if (Sourced) hold.Source.Stop();
        }

        // ---- Music ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Starts a note at an exact time on the audio clock (AudioSource.PlayScheduled), on the next voice
        /// of its layer. Returns the voice, or -1 if the bank does not have the clip yet.
        /// </summary>
        public int Schedule(in MusicEvent note)
        {
            if (!Length(note.Sound, out float seconds))
            {
                Skipped++;
                return -1;
            }
            float pitch = Mathf.Clamp(note.Pitch, 0.05f, PitchLaw.MaxPitch);

            // Within the layer: a voice that has rung out; else one that will have by the time the note
            // starts (only the last, inaudible part of its tail is lost); else the one that started first.
            int first = GroupStart[(int)note.Voice], size = GroupSize[(int)note.Voice];
            int chosen = -1;
            for (int i = first; i < first + size && chosen < 0; i++)
                if (music[i].End <= Now) chosen = i;
            if (chosen < 0)
            {
                for (int i = first; i < first + size; i++)
                    if (music[i].End <= note.Time && (chosen < 0 || music[i].End < music[chosen].End)) chosen = i;
            }
            if (chosen < 0)
            {
                chosen = first;
                for (int i = first + 1; i < first + size; i++)
                    if (music[i].Start < music[chosen].Start) chosen = i;
                Cuts++;
            }

            Voice voice = music[chosen];
            voice.Sound = note.Sound;
            voice.Start = note.Time;
            voice.End = note.Time + seconds / pitch;
            voice.Gain = Mathf.Clamp01(note.Volume);
            voice.Pitch = pitch;
            if (Sourced)
            {
                AudioSource source = voice.Source;
                source.Stop();
                source.clip = bank.Clip(note.Sound);
                source.pitch = pitch;
                source.volume = voice.Gain * musicVolume * musicFade;
                source.PlayScheduled(note.Time);
            }
            Remember(new PlayedSound { Id = note.Sound, Time = note.Time, Volume = voice.Gain, Pitch = pitch, Scheduled = true });
            return chosen;
        }

        /// <summary>Stops every music voice at once, scheduled notes included.</summary>
        public void StopMusic()
        {
            for (int i = 0; i < music.Length; i++)
            {
                music[i].End = double.NegativeInfinity;
                if (Sourced) music[i].Source.Stop();
            }
        }

        // ---- The account ------------------------------------------------------------------------------------------

        /// <summary>Effect voices sounding now (paused ones included).</summary>
        public int EffectsSounding
        {
            get
            {
                int sounding = 0;
                for (int i = 0; i < effects.Length; i++)
                    if (effects[i].Paused || effects[i].End > Now) sounding++;
                return sounding;
            }
        }

        /// <summary>Music voices sounding or scheduled now.</summary>
        public int MusicSounding
        {
            get
            {
                int sounding = 0;
                for (int i = 0; i < music.Length; i++)
                    if (music[i].End > Now) sounding++;
                return sounding;
            }
        }

        /// <summary>How many times a sound was started since the output was created.</summary>
        public int Count(SoundId id) => counts[(int)id];

        /// <summary>The sound started <paramref name="back"/> starts ago (0: the latest). False if there is none that far back.</summary>
        public bool Last(int back, out PlayedSound sound)
        {
            if (back < 0 || back >= Played || back >= HistorySize)
            {
                sound = default;
                return false;
            }
            sound = history[(Played - 1 - back) % HistorySize];
            return true;
        }

        /// <summary>The latest start of this sound still in the history (the last 256 starts).</summary>
        public bool Last(SoundId id, out PlayedSound sound)
        {
            for (int back = 0; Last(back, out sound); back++)
                if (sound.Id == id) return true;
            sound = default;
            return false;
        }

        /// <summary>The source behind an effect voice, the hold loop (index -1) or a music voice (index 100+); null where there is none.</summary>
        public AudioSource Source(int voice)
        {
            if (voice == -1) return hold.Source;
            if (voice >= 100) return voice - 100 < music.Length ? music[voice - 100].Source : null;
            return voice >= 0 && voice < effects.Length ? effects[voice].Source : null;
        }

        void Remember(in PlayedSound sound)
        {
            history[Played % HistorySize] = sound;
            Played++;
            counts[(int)sound.Id]++;
        }

        public void Dispose()
        {
            if (root != null)
            {
                if (Sourced)
                {
                    for (int i = 0; i < effects.Length; i++) effects[i].Source.Stop();
                    for (int i = 0; i < music.Length; i++) music[i].Source.Stop();
                    hold.Source.Stop();
                }
                Sim.Destroy(root);
            }
            root = null;
            HoldPlaying = false;
        }
    }
}
