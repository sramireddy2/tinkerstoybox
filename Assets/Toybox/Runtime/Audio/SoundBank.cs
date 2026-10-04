using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Toybox.Engine;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Toybox.Audio
{
    /// <summary>
    /// Every clip of the game, synthesised into float arrays and wrapped with <c>AudioClip.Create</c> +
    /// <c>SetData</c> (ART_BIBLE 11.1: the Web player has no audio thread to synthesise on).
    ///
    /// Generation is time-sliced: <see cref="Pump"/> does stages of about a millisecond until its budget
    /// is used up, in the order UI, grab, hold, release, land, button, level complete, music bank. A sound
    /// that is asked for before it exists is simply not there yet (<see cref="Ready"/>). The few clips that
    /// depend on a level's key and tempo (the level-complete stinger, bass and pad) are rendered again when
    /// <see cref="SetKey"/> names another key; the rest are made once.
    ///
    /// The bank keeps count of what it cost: milliseconds of synthesis, the longest single stage, and the
    /// bytes of samples it holds.
    /// </summary>
    public sealed class SoundBank : IDisposable
    {
        sealed class Entry
        {
            public float[] Samples;
            public AudioClip Clip;
            public int Length;
            public bool Ready;
            public double Milliseconds, LongestStage;
        }

        static SoundBank shared;

        readonly Entry[] entries = new Entry[Sounds.Count];
        readonly List<SoundId> queue = new List<SoundId>();
        readonly Stopwatch watch = new Stopwatch();
        readonly SoundRecipes.Scratch scratch = new SoundRecipes.Scratch();
        IEnumerator<float[]> current;
        SoundId currentId;
        double currentMilliseconds, currentLongest;
        bool clipsFailed;

        /// <summary>
        /// The bank the game plays from: it makes AudioClips and drops the sample arrays once a clip has
        /// them. It lives for the whole session, so restarting the presentation does not synthesise again.
        /// </summary>
        public static SoundBank Shared => shared ??= new SoundBank { MakeClips = true };

        /// <summary>Disposes the shared bank (its clips with it); the next use makes a new one.</summary>
        public static void ResetShared()
        {
            shared?.Dispose();
            shared = null;
        }

        /// <summary>Wrap every finished sound in an AudioClip.</summary>
        public bool MakeClips { get; set; }
        /// <summary>Keep the sample arrays after a clip was made (always kept without clips).</summary>
        public bool KeepSamples { get; set; }

        /// <summary>The key and tempo the per-key clips are (being) rendered in.</summary>
        public MusicKey Key { get; private set; } = MusicKey.CMajor;
        public int Bpm { get; private set; }
        public bool HasKey => Bpm > 0;

        // ---- What it cost -------------------------------------------------------------------------------

        /// <summary>Milliseconds spent synthesising so far (every stage, clip creation included).</summary>
        public double SynthMilliseconds { get; private set; }
        /// <summary>The longest single stage so far, milliseconds: how far one frame can overshoot its budget.</summary>
        public double LongestStageMilliseconds { get; private set; }
        public int StageCount { get; private set; }
        /// <summary>Stages that took longer than <see cref="LongStage"/> ms (in the editor: mostly a garbage collection falling into one).</summary>
        public int LongStages { get; private set; }
        /// <summary>How many of those had a garbage collection run inside them.</summary>
        public int LongStagesWithCollection { get; private set; }
        public const double LongStage = 4.0;
        /// <summary>Sounds finished so far (per-key ones count again when the key changes).</summary>
        public int Generated { get; private set; }

        /// <summary>Bytes of samples held now, as 32-bit floats: clips and kept arrays.</summary>
        public long SampleBytes
        {
            get
            {
                long samples = 0;
                for (int i = 0; i < entries.Length; i++)
                    if (entries[i] != null && entries[i].Ready) samples += entries[i].Length;
                return samples * sizeof(float);
            }
        }

        /// <summary>True when nothing is waiting to be generated.</summary>
        public bool Complete => current == null && queue.Count == 0;

        /// <summary>Sounds still to be generated, the one in progress included.</summary>
        public int Pending => queue.Count + (current != null ? 1 : 0);

        public SoundBank()
        {
            // The order of the enum is the order of generation; the per-key clips wait for a key.
            for (int i = 1; i < Sounds.Count; i++)
                if (!Sounds.Spec((SoundId)i).PerKey) queue.Add((SoundId)i);
        }

        // ---- Lookups --------------------------------------------------------------------------------------

        public bool Ready(SoundId id)
        {
            Entry entry = entries[(int)id];
            return entry != null && entry.Ready;
        }

        /// <summary>The clip, or null while it is not generated (or where the bank makes no clips).</summary>
        public AudioClip Clip(SoundId id)
        {
            Entry entry = entries[(int)id];
            return entry != null && entry.Ready ? entry.Clip : null;
        }

        /// <summary>The samples, where they are kept.</summary>
        public float[] Samples(SoundId id)
        {
            Entry entry = entries[(int)id];
            return entry != null && entry.Ready ? entry.Samples : null;
        }

        /// <summary>Length of the finished clip in seconds; 0 while it is not generated.</summary>
        public float Seconds(SoundId id)
        {
            Entry entry = entries[(int)id];
            return entry != null && entry.Ready ? (float)entry.Length / Sounds.Spec(id).Rate : 0f;
        }

        /// <summary>Milliseconds this sound took to synthesise.</summary>
        public double Milliseconds(SoundId id) => entries[(int)id] != null ? entries[(int)id].Milliseconds : 0.0;

        /// <summary>True once every clip that does not depend on the key is there.</summary>
        public bool StaticReady
        {
            get
            {
                for (int i = 1; i < Sounds.Count; i++)
                    if (!Sounds.Spec((SoundId)i).PerKey && !Ready((SoundId)i)) return false;
                return true;
            }
        }

        /// <summary>True once the level-complete stinger, bass and pad of the current key are there.</summary>
        public bool KeyReady
        {
            get
            {
                if (!HasKey) return false;
                for (int i = 1; i < Sounds.Count; i++)
                    if (Sounds.Spec((SoundId)i).PerKey && !Ready((SoundId)i)) return false;
                return true;
            }
        }

        // ---- Generation -------------------------------------------------------------------------------------

        /// <summary>
        /// Names the key and tempo of the level: the per-key clips are queued (the stinger with the effects,
        /// bass and pad at the end) and those of another key are dropped. Stop whatever plays them first.
        /// Returns whether anything changed.
        /// </summary>
        public bool SetKey(MusicKey key, int bpm)
        {
            bpm = Mathf.Clamp(bpm, 40, 200);
            if (HasKey && key == Key && bpm == Bpm) return false;
            Key = key;
            Bpm = bpm;

            if (current != null && Sounds.Spec(currentId).PerKey) current = null;
            queue.RemoveAll(id => Sounds.Spec(id).PerKey);
            for (int i = 1; i < Sounds.Count; i++)
            {
                var id = (SoundId)i;
                if (!Sounds.Spec(id).PerKey) continue;
                Drop(id);
                // Sorted insert: the enum's order is the order of generation.
                int at = 0;
                while (at < queue.Count && queue[at] < id) at++;
                queue.Insert(at, id);
            }
            return true;
        }

        /// <summary>
        /// Generates for about <paramref name="milliseconds"/>: at least one stage, and then further stages
        /// while a typical one still fits into what is left (a stage that has begun is finished). Returns
        /// true when there is nothing left to do.
        /// </summary>
        public bool Pump(double milliseconds)
        {
            if (Complete) return true;
            watch.Restart();
            double spent = 0;
            do
            {
                int collections = GC.CollectionCount(0);
                Stage();
                double now = watch.Elapsed.TotalMilliseconds;
                double stage = now - spent;
                spent = now;
                StageCount++;
                SynthMilliseconds += stage;
                currentMilliseconds += stage;
                if (stage > currentLongest) currentLongest = stage;
                if (stage > LongestStageMilliseconds) LongestStageMilliseconds = stage;
                if (stage > LongStage)
                {
                    LongStages++;
                    if (GC.CollectionCount(0) != collections) LongStagesWithCollection++;
                }
                if (current == null) Finish();
            }
            while (!Complete && spent + SynthMilliseconds / StageCount < milliseconds);
            watch.Stop();
            // The scratch buffer is only worth keeping while there is work.
            if (Complete) scratch.Release();
            return Complete;
        }

        /// <summary>Everything at once (tests, tools).</summary>
        public void GenerateAll()
        {
            while (!Pump(double.MaxValue)) { }
        }

        float[] finished;

        // One stage of the sound in progress, starting the next sound if there is none.
        void Stage()
        {
            if (current == null)
            {
                currentId = queue[0];
                queue.RemoveAt(0);
                currentMilliseconds = 0;
                currentLongest = 0;
                finished = null;
                current = SoundRecipes.Stages(currentId, Key, HasKey ? Bpm : SoundRecipes.DefaultBpm, scratch);
            }
            if (current.MoveNext())
            {
                if (current.Current != null) finished = current.Current;
                // The last stage hands over the samples; wrapping them is part of it.
                if (finished != null)
                {
                    Store(currentId, finished);
                    current = null;
                }
            }
            else
            {
                current = null;
            }
        }

        void Finish()
        {
            Entry entry = entries[(int)currentId];
            if (entry != null)
            {
                entry.Milliseconds = currentMilliseconds;
                entry.LongestStage = currentLongest;
            }
            finished = null;
        }

        void Store(SoundId id, float[] samples)
        {
            Drop(id);
            var entry = new Entry { Samples = samples, Length = samples.Length, Ready = true };
            // Without audio (see below) nothing will ever play the samples: no reason to hold on to them.
            if (MakeClips && clipsFailed && !KeepSamples) entry.Samples = null;
            if (MakeClips && !clipsFailed)
            {
                try
                {
                    SoundSpec spec = Sounds.Spec(id);
                    // Not streamed: the Web player cannot stream a created clip.
                    AudioClip clip = AudioClip.Create(id.ToString(), samples.Length, 1, spec.Rate, false);
                    clip.SetData(samples, 0);
                    entry.Clip = clip;
                    if (!KeepSamples) entry.Samples = null;
                }
                catch (Exception e)
                {
                    // No audio device, audio disabled: the game is silent, not broken.
                    clipsFailed = true;
                    if (!KeepSamples) entry.Samples = null;
                    Debug.LogWarning("[Toybox] audio clips cannot be created, the game stays silent: " + e.Message);
                }
            }
            entries[(int)id] = entry;
            Generated++;
        }

        void Drop(SoundId id)
        {
            Entry entry = entries[(int)id];
            if (entry == null) return;
            if (entry.Clip != null) Sim.Destroy(entry.Clip);
            entries[(int)id] = null;
        }

        public void Dispose()
        {
            current = null;
            queue.Clear();
            scratch.Release();
            for (int i = 0; i < entries.Length; i++) Drop((SoundId)i);
            if (ReferenceEquals(shared, this)) shared = null;
        }

        /// <summary>A table of what was generated: length, level and cost per sound, and the totals.</summary>
        public string Report()
        {
            var text = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            text.AppendLine("sound            rate   seconds  peak dBFS   rms dBFS   centroid Hz   synth ms  longest stage");
            for (int i = 1; i < Sounds.Count; i++)
            {
                var id = (SoundId)i;
                Entry entry = entries[i];
                if (entry == null || !entry.Ready) continue;
                SoundSpec spec = Sounds.Spec(id);
                text.Append(id.ToString().PadRight(16)).Append(spec.Rate.ToString(c).PadLeft(6));
                text.Append(((float)entry.Length / spec.Rate).ToString("0.000", c).PadLeft(9));
                if (entry.Samples != null)
                {
                    text.Append(SoundStats.PeakDb(entry.Samples).ToString("0.0", c).PadLeft(11));
                    text.Append(SoundStats.RmsDb(entry.Samples).ToString("0.0", c).PadLeft(11));
                    text.Append(SoundStats.Centroid(entry.Samples, spec.Rate, 0, Math.Min(entry.Length, 16384)).ToString("0", c).PadLeft(14));
                }
                else
                {
                    text.Append(spec.PeakDb.ToString("0.0", c).PadLeft(11)).Append("          -             -");
                }
                text.Append(entry.Milliseconds.ToString("0.00", c).PadLeft(11)).Append(entry.LongestStage.ToString("0.00", c).PadLeft(15)).AppendLine();
            }
            text.Append("total: ").Append(Generated).Append(" sounds, ");
            text.Append((SampleBytes / (1024.0 * 1024.0)).ToString("0.00", c)).Append(" MB of samples, ");
            text.Append(SynthMilliseconds.ToString("0.0", c)).Append(" ms of synthesis in ").Append(StageCount).Append(" stages, longest stage ");
            text.Append(LongestStageMilliseconds.ToString("0.00", c)).Append(" ms, ").Append(LongStages).Append(" stages over ").Append(LongStage.ToString("0", c)).Append(" ms (")
                .Append(LongStagesWithCollection).Append(" of them with a garbage collection inside)");
            return text.ToString();
        }
    }
}
