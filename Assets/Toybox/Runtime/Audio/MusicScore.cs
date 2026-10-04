using System;
using System.Collections.Generic;
using Toybox.Engine;

namespace Toybox.Audio
{
    /// <summary>The layers of the toy music box, each with voices of its own.</summary>
    public enum MusicVoice
    {
        Lead,
        Pad,
        Bass,
        Kit,
    }

    /// <summary>One note for the scheduler to start: which clip, when (on the audio clock), how loud, at what rate.</summary>
    public struct MusicEvent
    {
        /// <summary>The sixteenth-note step the note belongs to, counted from the start of the tune.</summary>
        public int Step;
        /// <summary>Audio-clock time to start at (AudioSettings.dspTime).</summary>
        public double Time;
        public MusicVoice Voice;
        public SoundId Sound;
        /// <summary>The MIDI note that sounds (lead, bass, the pad's root); 0 for the kit.</summary>
        public int Note;
        public float Volume;
        public float Pitch;
    }

    /// <summary>
    /// The tune of one level (ART_BIBLE 11.4, "Composition"): a pure function from a sixteenth-note step to
    /// the notes that start on it.
    ///
    /// - Harmony: four add9 chords, two bars each (I-vi-IV-V in the mode's own chords).
    /// - Lead: a pentatonic random walk as a two-bar motif in A A' B A' form over those eight bars. Step
    ///   weights for 0 / +-1 / +-2 / leap are 0.2 / 0.5 / 0.25 / 0.05; an eighth note sounds with
    ///   probability 0.45. Two steps mutate every eight bars.
    /// - Its dice are seeded by the level id. Never game.Rng, never UnityEngine.Random.
    /// - Key and tempo come from the preset; the mode is the note set the lead picks from.
    /// </summary>
    public sealed class MusicScore
    {
        public const int StepsPerBar = 16;
        public const int BarsPerChord = 2;
        public const int StepsPerChord = StepsPerBar * BarsPerChord;
        /// <summary>Eight bars: one round of the harmony and of the A A' B A' form.</summary>
        public const int StepsPerCycle = StepsPerChord * MusicKey.ChordCount;
        /// <summary>Eighth notes in the two-bar motif.</summary>
        public const int Slots = StepsPerChord / 2;
        /// <summary>The slots of A that A' plays differently: the last half bar.</summary>
        public const int VariedFrom = 12;
        public const float NoteChance = 0.45f;
        /// <summary>The music box clips cover C4-C6, a semitone more either way with pitch.</summary>
        public const int LowestNote = 59, HighestNote = 85;

        public const float LeadVolume = 1f, PadVolume = 0.8f, BassVolume = 0.9f;
        public const float KickVolume = 0.9f, WoodVolume = 0.5f, ShakerVolume = 0.35f;

        public MusicKey Key { get; }
        public int Bpm { get; }
        public int Seed { get; }
        /// <summary>Night is slower and drops the kit to shaker only.</summary>
        public bool Night { get; }

        /// <summary>Seconds per sixteenth-note step.</summary>
        public double StepSeconds => 15.0 / Bpm;

        // The scale's notes within the music box's range, low to high: what the walk moves on.
        readonly int[] ladder;
        // Per eighth-note slot: an index into the ladder, or -1 for a rest.
        readonly int[] baseA = new int[Slots], baseB = new int[Slots], baseTail = new int[Slots - VariedFrom];
        readonly int[] a = new int[Slots], b = new int[Slots], tail = new int[Slots - VariedFrom];
        int mutatedTo;

        public MusicScore(int seed, MusicKey key, int bpm, bool night = false)
        {
            Seed = seed;
            Key = key;
            Bpm = Math.Min(200, Math.Max(40, bpm));
            Night = night;

            var notes = new List<int>();
            for (int note = LowestNote; note <= HighestNote; note++)
                if (key.Contains(note)) notes.Add(note);
            ladder = notes.ToArray();

            // The walk starts on the tonic nearest the middle of the range.
            int home = 0;
            for (int i = 0; i < ladder.Length; i++)
                if ((ladder[i] - key.Root) % 12 == 0 && Math.Abs(ladder[i] - 72) < Math.Abs(ladder[home] - 72)) home = i;
            if ((ladder[home] - key.Root) % 12 != 0) home = ladder.Length / 2;

            var rng = new SynthRng(SynthRng.Mix((uint)seed, 0xA11CEu));
            int end = Walk(baseA, 0, Slots, home, ref rng);
            // A' is A with another ending, walked on from where A stood before its last half bar.
            int before = home;
            for (int slot = 0; slot < VariedFrom; slot++)
                if (baseA[slot] >= 0) before = baseA[slot];
            var tailRng = new SynthRng(SynthRng.Mix((uint)seed, 0x7A11u));
            Walk(baseTail, 0, baseTail.Length, before, ref tailRng);
            // B answers from a little higher up.
            var bRng = new SynthRng(SynthRng.Mix((uint)seed, 0xB0B0u));
            Walk(baseB, 0, Slots, Clamp(end + 2), ref bRng);

            Reset();
        }

        /// <summary>The score of a level: its id is the seed, its environment preset gives key, tempo and night.</summary>
        public static MusicScore For(LevelDefinition level, EnvironmentPreset preset)
        {
            preset ??= EnvironmentPreset.None;
            return new MusicScore(level != null ? level.Id : 0, MusicKey.Of(preset), preset.Bpm, preset.Night);
        }

        /// <summary>The MIDI notes the lead can play, low to high.</summary>
        public IReadOnlyList<int> Ladder => ladder;

        /// <summary>Which of the four chords sounds at a step.</summary>
        public static int ChordAt(int step) => step < 0 ? 0 : step / StepsPerChord % MusicKey.ChordCount;

        /// <summary>Which round of the eight bars a step is in.</summary>
        public static int CycleAt(int step) => step < 0 ? 0 : step / StepsPerCycle;

        /// <summary>The start of the two-bar section a step is in.</summary>
        public static int SectionStart(int step) => step < 0 ? 0 : step - step % StepsPerChord;

        /// <summary>The MIDI note the lead starts on this step, or -1 for none.</summary>
        public int LeadAt(int step)
        {
            if (step < 0 || (step & 1) != 0) return -1;
            MutateTo(CycleAt(step));
            int inCycle = step % StepsPerCycle;
            int section = inCycle / StepsPerChord;
            int slot = inCycle % StepsPerChord / 2;
            int index;
            if (section == 2) index = b[slot];
            else if (section == 0 || slot < VariedFrom) index = a[slot];
            else index = tail[slot - VariedFrom];
            return index >= 0 ? ladder[index] : -1;
        }

        /// <summary>
        /// Appends the notes that start on a step. <paramref name="kit"/>: the toy kit has entered (after the
        /// level's first grab). <paramref name="holding"/>: the music box rests, the hold tone is the lead.
        /// </summary>
        public void Events(int step, double time, bool kit, bool holding, List<MusicEvent> into)
        {
            if (step < 0) return;
            int chord = ChordAt(step);
            int inBar = step % StepsPerBar;

            if (step % StepsPerChord == 0)
            {
                into.Add(new MusicEvent
                {
                    Step = step, Time = time, Voice = MusicVoice.Pad, Sound = SoundId.Pad0 + chord,
                    Note = SoundRecipes.PadRoot(Key, chord), Volume = PadVolume, Pitch = 1f,
                });
            }
            if (inBar == 0)
            {
                into.Add(new MusicEvent
                {
                    Step = step, Time = time, Voice = MusicVoice.Bass, Sound = SoundId.Bass0 + chord,
                    Note = SoundRecipes.BassNote(Key, chord), Volume = BassVolume, Pitch = 1f,
                });
            }

            if (!holding)
            {
                int note = LeadAt(step);
                if (note >= 0)
                {
                    SoundId box = Sounds.Box(note, out float pitch);
                    // The beat leans a little: downbeats full, the rest lighter.
                    float accent = inBar % 8 == 0 ? 1f : inBar % 4 == 0 ? 0.85f : 0.7f;
                    into.Add(new MusicEvent { Step = step, Time = time, Voice = MusicVoice.Lead, Sound = box, Note = note, Volume = LeadVolume * accent, Pitch = pitch });
                }
            }

            if (kit)
            {
                if (!Night)
                {
                    if (inBar == 0 || inBar == 8)
                        into.Add(new MusicEvent { Step = step, Time = time, Voice = MusicVoice.Kit, Sound = SoundId.KitKick, Volume = KickVolume, Pitch = 1f });
                    // The woodblock on the backbeat, with a pick-up into every fourth bar.
                    if (inBar == 4 || inBar == 12 || (inBar == 14 && step / StepsPerBar % 4 == 3))
                        into.Add(new MusicEvent { Step = step, Time = time, Voice = MusicVoice.Kit, Sound = SoundId.KitWood, Volume = WoodVolume, Pitch = 1f });
                }
                if ((inBar & 1) == 0)
                {
                    float volume = inBar % 4 == 2 ? ShakerVolume : ShakerVolume * 0.6f;
                    into.Add(new MusicEvent { Step = step, Time = time, Voice = MusicVoice.Kit, Sound = SoundId.KitShaker, Volume = volume, Pitch = 1f });
                }
            }
        }

        // ---- The walk ---------------------------------------------------------------------------------------

        int Clamp(int index) => index < 0 ? 0 : index >= ladder.Length ? ladder.Length - 1 : index;

        // Fills slots [from, to) with a random walk on the ladder from `position`; returns where it ended.
        int Walk(int[] motif, int from, int to, int position, ref SynthRng rng)
        {
            for (int slot = from; slot < to; slot++)
            {
                // The first eighth of a motif always sounds: the downbeat is the tune's anchor.
                bool sounds = rng.Value() < NoteChance || slot == 0;
                if (!sounds)
                {
                    motif[slot] = -1;
                    continue;
                }
                if (slot != 0) position = Move(position, ref rng);
                motif[slot] = position;
            }
            return position;
        }

        // One step of the walk: stay 0.2, a step 0.5, a skip 0.25, a leap 0.05. Reflects off the ends.
        int Move(int position, ref SynthRng rng)
        {
            float dice = rng.Value();
            int size = dice < 0.2f ? 0 : dice < 0.7f ? 1 : dice < 0.95f ? 2 : 3 + rng.Range(2);
            if (size == 0) return position;
            int next = position + ((rng.NextUInt() & 1u) == 0 ? -size : size);
            if (next < 0) next = -next;
            if (next >= ladder.Length) next = 2 * (ladder.Length - 1) - next;
            return Clamp(next);
        }

        void Reset()
        {
            Array.Copy(baseA, a, Slots);
            Array.Copy(baseB, b, Slots);
            Array.Copy(baseTail, tail, tail.Length);
            mutatedTo = 0;
        }

        // The motifs as they stand in a round: the base with two steps changed for every round before it.
        void MutateTo(int cycle)
        {
            if (cycle == mutatedTo) return;
            if (cycle < mutatedTo) Reset();
            for (int round = mutatedTo + 1; round <= cycle; round++)
            {
                var rng = new SynthRng(SynthRng.Mix((uint)Seed, 0xC0DE00u + (uint)round));
                for (int change = 0; change < 2; change++)
                {
                    int[] motif = (rng.NextUInt() & 1u) == 0 ? a : b;
                    // Never the downbeat.
                    int slot = 1 + rng.Range(Slots - 1);
                    if (rng.Value() >= NoteChance)
                    {
                        motif[slot] = -1;
                        continue;
                    }
                    // A new note near the one sounding before it.
                    int near = motif[0];
                    for (int earlier = 0; earlier < slot; earlier++)
                        if (motif[earlier] >= 0) near = motif[earlier];
                    motif[slot] = Move(near, ref rng);
                }
            }
            mutatedTo = cycle;
        }
    }

    /// <summary>
    /// The clock of the music (ART_BIBLE 11.4, "Clock"): it keeps the next sixteenth-note step in audio
    /// time and, whenever asked, hands out every step that begins within the look-ahead. 200 ms of
    /// look-ahead survives frame hitches; a step's time is computed from the start of the tune, never
    /// accumulated, so the tempo does not drift however the frames fall.
    ///
    /// It is not a MonoBehaviour: the audio presenter calls <see cref="Advance"/> once per frame with
    /// AudioSettings.dspTime and starts what comes back with AudioSource.PlayScheduled.
    /// </summary>
    public sealed class MusicBox
    {
        public const double LookAhead = 0.2;
        /// <summary>The first step sounds this long after <see cref="Start"/>.</summary>
        public const double StartDelay = 0.1;
        /// <summary>A step found this late (a hitch longer than the look-ahead) restarts its two-bar section instead.</summary>
        public const double MaxLate = 0.05;

        double origin;

        public MusicScore Score { get; private set; }
        public bool Running { get; private set; }
        /// <summary>The next step to hand out.</summary>
        public int NextStep { get; private set; }
        /// <summary>The toy kit plays (set after the first grab of the level).</summary>
        public bool Kit { get; set; }
        /// <summary>A toy is held: the music box rests and the hold tone is the lead.</summary>
        public bool Holding { get; set; }
        /// <summary>How many times a late clock made the tune restart a section.</summary>
        public int Resyncs { get; private set; }

        /// <summary>Audio-clock time at which a step begins.</summary>
        public double TimeOf(int step) => origin + step * (Score != null ? Score.StepSeconds : 0.0);

        public double NextTime => TimeOf(NextStep);

        /// <summary>Starts (or restarts) a tune: step <paramref name="atStep"/> sounds <see cref="StartDelay"/> after <paramref name="now"/>.</summary>
        public void Start(MusicScore score, double now, int atStep = 0)
        {
            Score = score ?? throw new ArgumentNullException(nameof(score));
            NextStep = Math.Max(0, atStep);
            origin = now + StartDelay - NextStep * score.StepSeconds;
            Running = true;
        }

        /// <summary>Stops handing out steps. <see cref="Resume"/> picks the tune up again.</summary>
        public void Stop() => Running = false;

        /// <summary>Carries on after a stop from the start of the two-bar section it was in, so pad and bass come back in together.</summary>
        public void Resume(double now)
        {
            if (Score == null) return;
            Start(Score, now, MusicScore.SectionStart(NextStep));
        }

        /// <summary>The chord sounding at this moment (0 before the tune starts).</summary>
        public int ChordAt(double now)
        {
            if (Score == null) return 0;
            double steps = (now - origin) / Score.StepSeconds;
            return steps <= 0 ? 0 : MusicScore.ChordAt((int)steps);
        }

        /// <summary>
        /// Appends every note of every step that begins before <paramref name="now"/> + the look-ahead and
        /// has not been handed out yet. Returns how many steps that were.
        /// </summary>
        public int Advance(double now, List<MusicEvent> into)
        {
            if (!Running || Score == null) return 0;
            if (NextTime < now - MaxLate || NextTime > now + LookAhead + 1.0)
            {
                // The clock ran away from us (a long hitch, a hidden tab): notes scheduled in the past would
                // all sound at once. Or it started over (the audio device changed) and the next step is far
                // in its future. Begin the section again instead.
                Resyncs++;
                Start(Score, now, MusicScore.SectionStart(NextStep));
            }
            int steps = 0;
            while (NextTime < now + LookAhead)
            {
                Score.Events(NextStep, NextTime, Kit, Holding, into);
                NextStep++;
                steps++;
            }
            return steps;
        }
    }
}
