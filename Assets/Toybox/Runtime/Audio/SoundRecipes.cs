using System;
using System.Collections.Generic;

namespace Toybox.Audio
{
    /// <summary>
    /// What is written into each clip: the recipes of ART_BIBLE 11.3 (effects) and 11.4 (the music bank),
    /// as pure functions from a <see cref="SoundId"/> (and, for the few clips rendered per level, a key and
    /// a tempo) to samples. <see cref="Dry"/> is the recipe alone; <see cref="Render"/> adds the baked
    /// room, the peak level and the cut tail. Nothing here reads a clock, a random source other than its
    /// own seeded noise, or any state: the same call gives the same samples, bit for bit.
    ///
    /// <see cref="Stages"/> is the same work in pieces of about a millisecond, for the bank's time-sliced
    /// generation behind the title screen.
    /// </summary>
    public static class SoundRecipes
    {
        const int R = Synth.Rate;
        const float Sqrt2Inv = 0.70710678f;
        /// <summary>Samples of reverb per stage (about half a millisecond of work).</summary>
        const int Chunk = 16384;
        /// <summary>The tempo assumed where a caller gives none (sunny-rug's).</summary>
        public const int DefaultBpm = 84;
        /// <summary>Notes of an add9 pad chord, and saws per note.</summary>
        public const int PadNotes = 4, PadSaws = 3;
        public const float PadDetuneCents = 7f, PadCutoffHz = 600f, PadAttack = 0.4f, PadRelease = 0.4f;

        // ---- Land materials (base Hz; mode ratios; decay time constants in ms) -----------------------------
        static readonly float[] PlasticRatios = { 1f, 2.3f, 3.9f }, PlasticDecays = { 70f, 45f, 30f };
        static readonly float[] WoodRatios = { 1f, 2.76f, 5.4f }, WoodDecays = { 110f, 60f, 35f };
        static readonly float[] RubberRatios = { 1f, 1.5f }, RubberDecays = { 90f, 60f };
        static readonly float[] MetalRatios = { 1f, 2.76f, 5.40f, 8.93f }, MetalDecays = { 900f, 600f, 400f, 250f };
        static readonly float[] GlassRatios = { 1f, 2.32f, 4.25f }, GlassDecays = { 500f, 300f, 200f };
        public const float PlasticHz = 420f, WoodHz = 300f, RubberHz = 140f, MetalHz = 900f, GlassHz = 1600f;

        /// <summary>Base frequency, mode ratios and decays (ms) of a modal landing clip; false for the noise-based ones.</summary>
        public static bool Modes(SoundId id, out float baseHz, out float[] ratios, out float[] decaysMs)
        {
            switch (id)
            {
                case SoundId.LandPlastic: baseHz = PlasticHz; ratios = PlasticRatios; decaysMs = PlasticDecays; return true;
                case SoundId.LandWood: baseHz = WoodHz; ratios = WoodRatios; decaysMs = WoodDecays; return true;
                case SoundId.LandRubber: baseHz = RubberHz; ratios = RubberRatios; decaysMs = RubberDecays; return true;
                case SoundId.LandMetal: baseHz = MetalHz; ratios = MetalRatios; decaysMs = MetalDecays; return true;
                case SoundId.LandGlass: baseHz = GlassHz; ratios = GlassRatios; decaysMs = GlassDecays; return true;
                default: baseHz = 0f; ratios = null; decaysMs = null; return false;
            }
        }

        // ---- Entry points ------------------------------------------------------------------------------------

        /// <summary>The finished clip of a sound that does not depend on the key (or in C major at 84 BPM).</summary>
        public static float[] Render(SoundId id) => Render(id, MusicKey.CMajor, DefaultBpm);

        /// <summary>The finished clip: recipe, baked room, peak level, cut tail.</summary>
        public static float[] Render(SoundId id, MusicKey key, int bpm)
        {
            float[] last = null;
            IEnumerator<float[]> stages = Stages(id, key, bpm);
            while (stages.MoveNext())
                if (stages.Current != null) last = stages.Current;
            return last;
        }

        public static float[] Dry(SoundId id) => Dry(id, MusicKey.CMajor, DefaultBpm);

        /// <summary>The recipe alone: no room, no level set, no cut.</summary>
        public static float[] Dry(SoundId id, MusicKey key, int bpm)
        {
            if (Sounds.IsPad(id))
            {
                float[] pad = PadBuffer(bpm);
                for (int note = 0; note < PadNotes; note++) PadNote(pad, key, id - SoundId.Pad0, note);
                PadShape(pad, bpm);
                return pad;
            }
            return DryNow(id, key, Math.Max(1, bpm));
        }

        /// <summary>
        /// A buffer that is handed from one sound's generation to the next, so that the reverberated signal
        /// (the longest array each sound needs, and garbage a moment later) is not allocated 43 times over.
        /// </summary>
        public sealed class Scratch
        {
            float[] samples;

            public float[] Take(int length)
            {
                if (samples == null || samples.Length < length) samples = new float[length];
                return samples;
            }

            public void Release() => samples = null;
        }

        /// <summary>
        /// The work of <see cref="Render"/> in stages: every MoveNext does a piece (a recipe, a mode of a
        /// landing, a pad note, 16k samples of reverb) and the last one yields the finished clip; the ones
        /// before yield null. With a <paramref name="scratch"/> the caller must run one sound's stages to
        /// the end before starting the next.
        /// </summary>
        public static IEnumerator<float[]> Stages(SoundId id, MusicKey key, int bpm, Scratch scratch = null)
        {
            SoundSpec spec = Sounds.Spec(id);
            bpm = Math.Max(1, bpm);

            float[] dry;
            if (Sounds.IsPad(id))
            {
                dry = PadBuffer(bpm);
                for (int note = 0; note < PadNotes; note++)
                {
                    PadNote(dry, key, id - SoundId.Pad0, note);
                    yield return null;
                }
                PadShape(dry, bpm);
            }
            else if (ModeCount(id) > 0)
            {
                dry = ModalBuffer(id);
                for (int mode = 0; mode < ModeCount(id); mode++)
                {
                    ModalMode(dry, id, mode);
                    yield return null;
                }
                Synth.FadeOut(dry, dry.Length, 0.01f, R);
            }
            else if (id == SoundId.Hold)
            {
                dry = HoldWaves();
                yield return null;
                HoldFilter(dry);
            }
            else if (id == SoundId.LevelComplete)
            {
                dry = LevelCompleteFront(key, bpm);
                yield return null;
                float[] swell = LevelCompleteSwell(key);
                yield return null;
                LevelCompleteBack(dry, swell);
            }
            else
            {
                dry = DryNow(id, key, bpm);
            }
            yield return null;

            var room = new SchroederReverb(spec.Rate);
            if (spec.Loop)
            {
                // The room is fed the loop twice and the second round is kept: the tail of the loop's end
                // is then already under its start, and the seam stays seamless.
                var loop = new float[dry.Length];
                for (int round = 0; round < 2; round++)
                {
                    for (int at = 0; at < loop.Length; at += Chunk)
                    {
                        room.Process(dry, loop, at, Chunk, spec.Wet);
                        yield return null;
                    }
                }
                Synth.Normalise(loop, spec.PeakDb);
                yield return loop;
                yield break;
            }

            if (!(spec.Wet > 0f))
            {
                Synth.Normalise(dry, spec.PeakDb);
                yield return Synth.TrimTail(dry, spec.Rate, Sounds.TailFloorDb);
                yield break;
            }

            int length = dry.Length + Synth.Samples(SchroederReverb.DefaultRt60, spec.Rate);
            float[] output = scratch != null ? scratch.Take(length) : new float[length];
            // The tail is rendered until it is safely under where it will be cut. It is judged in windows
            // longer than the longest comb, so every train of echoes shows in every window.
            int window = Synth.Samples(0.047f, spec.Rate);
            float stopBelow = Synth.Gain(Sounds.TailFloorDb - spec.PeakDb) * 0.5f;
            float peak = 0f;
            int rendered = 0, sinceYield = 0;
            while (rendered < length)
            {
                int end = Math.Min(length, rendered + window);
                room.Process(dry, output, rendered, end - rendered, spec.Wet);
                float windowPeak = 0f;
                for (int i = rendered; i < end; i++)
                {
                    float a = output[i] < 0f ? -output[i] : output[i];
                    if (a > windowPeak) windowPeak = a;
                }
                bool pastDry = rendered >= dry.Length;
                rendered = end;
                if (windowPeak > peak) peak = windowPeak;
                if (pastDry && windowPeak < peak * stopBelow) break;
                sinceYield += window;
                if (sinceYield >= Chunk)
                {
                    sinceYield = 0;
                    yield return null;
                }
            }
            if (peak > 1e-9f) Synth.Scale(output, rendered, Synth.Gain(spec.PeakDb) / peak);
            yield return Synth.TrimTail(output, rendered, spec.Rate, Sounds.TailFloorDb);
        }

        // ---- Recipes -------------------------------------------------------------------------------------------

        static int N(float seconds) => Synth.Samples(seconds, R);

        static float[] DryNow(SoundId id, MusicKey key, int bpm)
        {
            switch (id)
            {
                case SoundId.UiHover: return UiHover();
                case SoundId.UiClick: return UiClick();
                case SoundId.FocusTick: return FocusTick();
                case SoundId.Grab: return Grab();
                case SoundId.Hold: return Hold();
                case SoundId.HoldJump: return HoldJump();
                case SoundId.ReleaseThock: return ReleaseThock();
                case SoundId.ReleaseSub: return ReleaseSub();
                case SoundId.ReleaseBell: return ReleaseBell();
                case SoundId.LandPlastic:
                case SoundId.LandWood:
                case SoundId.LandRubber:
                case SoundId.LandMetal:
                case SoundId.LandGlass: return Modal(id);
                case SoundId.LandFelt: return LandFelt();
                case SoundId.LandCardboard: return LandCardboard();
                case SoundId.LandFeather: return LandFeather();
                case SoundId.PlayerLand: return PlayerLand();
                case SoundId.ButtonPress: return Button(659.26f, 880f);
                case SoundId.ButtonRelease: return Button(880f, 659.26f);
                case SoundId.ExitOpen: return ExitOpen();
                case SoundId.ExitClose: return ExitClose();
                case SoundId.LevelComplete: return LevelComplete(key, bpm);
                case SoundId.KitWood: return KitWood();
                case SoundId.KitShaker: return KitShaker();
                case SoundId.KitKick: return KitKick();
            }
            if (Sounds.IsBox(id)) return Box(id - SoundId.Box0);
            if (Sounds.IsBass(id)) return Bass(key, id - SoundId.Bass0, bpm);
            return new float[1];
        }

        /// <summary>
        /// An FM marimba note (modulator at four times the carrier, index 2 to 0 in 40 ms): the voice of
        /// buttons, UI clicks, exits and the level-complete arpeggio.
        /// </summary>
        static void Marimba(float[] buffer, float at, float hz, float gain, float decay = 0.06f)
        {
            Synth.Add(buffer, N(at), N(decay * 6f), R, new FmTone(hz, 4f, 2f, 0.04f, decay, gain));
        }

        // Sine 2 kHz for 8 ms.
        static float[] UiHover()
        {
            float[] b = Synth.Buffer(0.008f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 2000f, 1f));
            Synth.Window(b, 0, b.Length);
            return b;
        }

        // One marimba C6, 60 ms.
        static float[] UiClick()
        {
            float[] b = Synth.Buffer(0.06f, R);
            Marimba(b, 0f, 1046.5f, 1f, 0.02f);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // Sine 1.8 kHz, 25 ms, decay 6 ms.
        static float[] FocusTick()
        {
            float[] b = Synth.Buffer(0.025f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 1800f, 1f, 0.006f));
            Synth.FadeOut(b, b.Length, 0.003f, R);
            return b;
        }

        // Peel: noise through a band-pass (Q 1.2) swept 800 -> 2400 Hz over 60 ms.
        // Pluck: sine at 523 Hz with a +3 semitone glide over the first 50 ms, decay 40 ms, 120 ms long.
        static float[] Grab()
        {
            float[] b = Synth.Buffer(0.14f, R);

            float[] peel = Synth.Buffer(0.06f, R);
            var rng = new SynthRng(0x9EE1u);
            Synth.AddNoise(peel, 0, peel.Length, R, ref rng, 1f);
            Synth.SweptBandPass(peel, 0, peel.Length, R, 800f, 2400f, 1.2f);
            Synth.Window(peel, 0, peel.Length);
            Synth.Normalise(peel, -5f);
            Synth.Mix(b, 0, peel);

            // The pluck lands as the peel lets go.
            var pluck = new Tone(Wave.Sine, 523.25f, 1f, 0.04f) { GlideSemis = 3f, GlideSeconds = 0.05f };
            Synth.Add(b, N(0.02f), N(0.12f), R, pluck);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // A 2.000 s seamless loop: triangles at 220.5 Hz and 219.5 Hz (441 and 439 whole cycles, so it loops
        // without a click and beats at 1 Hz) through a 900 Hz low-pass.
        static float[] Hold()
        {
            float[] loop = HoldWaves();
            HoldFilter(loop);
            return loop;
        }

        static float[] HoldWaves()
        {
            int n = R * 2;
            var loop = new float[n];
            Synth.Add(loop, 0, n, R, new Tone(Wave.Triangle, 220.5f, 0.5f));
            Synth.Add(loop, 0, n, R, new Tone(Wave.Triangle, 219.5f, 0.5f));
            return loop;
        }

        static void HoldFilter(float[] loop)
        {
            // The filter is run over the loop's end first, so that at the loop's start it is already in the
            // state it will be in when the loop comes round: the filtered loop closes on itself too.
            const int LeadIn = 4096;
            var leadIn = new float[LeadIn];
            Array.Copy(loop, loop.Length - LeadIn, leadIn, 0, LeadIn);
            Biquad filter = Biquad.LowPass(R, 900f, Sqrt2Inv);
            filter.Run(leadIn, 0, LeadIn);
            filter.Run(loop, 0, loop.Length);
        }

        // Sine 3 kHz, 2 ms.
        static float[] HoldJump()
        {
            float[] b = Synth.Buffer(0.002f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 3000f, 1f));
            Synth.Window(b, 0, b.Length);
            return b;
        }

        // Thock: sine from 196 Hz falling 5 semitones over 80 ms, decay 30 ms, plus a 15 ms click of noise
        // high-passed at 2 kHz.
        static float[] ReleaseThock()
        {
            float[] b = Synth.Buffer(0.2f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 196f, 1f, 0.03f) { GlideSemis = -5f, GlideSeconds = 0.08f });

            float[] click = Synth.Buffer(0.015f, R);
            var rng = new SynthRng(0x70C4u);
            Synth.AddNoise(click, 0, click.Length, R, ref rng, 1f, 0.004f);
            Synth.Filter(click, 0, click.Length, Biquad.HighPass(R, 2000f, Sqrt2Inv));
            Synth.FadeOut(click, click.Length, 0.003f, R);
            Synth.Normalise(click, -6f);
            Synth.Mix(b, 0, click);

            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // Sub: 55 Hz sine, 250 ms.
        static float[] ReleaseSub()
        {
            float[] b = Synth.Buffer(0.25f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 55f, 1f, 0.12f) { Attack = 0.01f });
            Synth.FadeOut(b, b.Length, 0.05f, R);
            return b;
        }

        // Bell: FM, carrier 2093 Hz, ratio 1:3.5, index 3 -> 0 in 60 ms, 200 ms.
        static float[] ReleaseBell()
        {
            float[] b = Synth.Buffer(0.2f, R);
            Synth.Add(b, 0, b.Length, R, new FmTone(2093f, 3.5f, 3f, 0.06f, 0.05f));
            Synth.FadeOut(b, b.Length, 0.02f, R);
            return b;
        }

        // Modal landing: a sum of damped sines. Higher modes are quieter and die sooner.
        static float[] Modal(SoundId id)
        {
            float[] b = ModalBuffer(id);
            for (int mode = 0; mode < ModeCount(id); mode++) ModalMode(b, id, mode);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        /// <summary>How many modes a landing clip has; 0 for the noise-based ones and for everything else.</summary>
        public static int ModeCount(SoundId id) => Modes(id, out _, out float[] ratios, out _) ? ratios.Length : 0;

        static float[] ModalBuffer(SoundId id)
        {
            Modes(id, out _, out _, out float[] decaysMs);
            float longest = 0f;
            for (int m = 0; m < decaysMs.Length; m++) longest = Math.Max(longest, decaysMs[m]);
            // 5.5 time constants is 48 dB down: past where the tail is cut.
            return Synth.Buffer(longest * 0.001f * 5.5f, R);
        }

        static void ModalMode(float[] buffer, SoundId id, int mode)
        {
            Modes(id, out float baseHz, out float[] ratios, out float[] decaysMs);
            var tone = new Tone(Wave.Sine, baseHz * ratios[mode], 1f / (1f + 0.8f * mode), decaysMs[mode] * 0.001f);
            if (id == SoundId.LandRubber)
            {
                // Rubber starts two semitones high and falls.
                tone.Hz *= Synth.Ratio(RubberFallSemis);
                tone.GlideSemis = -RubberFallSemis;
                tone.GlideSeconds = RubberFallSeconds;
            }
            // Nothing to add once a mode is 60 dB down.
            Synth.Add(buffer, 0, Synth.Samples(decaysMs[mode] * 0.001f * 7f, R), R, tone);
        }

        public const float RubberFallSemis = 2f, RubberFallSeconds = 0.06f;

        // Felt, sponge: noise, low-pass 500 Hz, decay 60 ms.
        static float[] LandFelt()
        {
            float[] b = Synth.Buffer(0.36f, R);
            var rng = new SynthRng(0xFE17u);
            Synth.AddNoise(b, 0, b.Length, R, ref rng, 1f, 0.06f);
            Synth.Filter(b, 0, b.Length, Biquad.LowPass(R, 500f, Sqrt2Inv));
            Synth.FadeOut(b, b.Length, 0.02f, R);
            return b;
        }

        // Cardboard: a 180 Hz thump under noise through a band-pass at 700 Hz (Q 1.5), decay 50 ms.
        static float[] LandCardboard()
        {
            float[] b = Synth.Buffer(0.3f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 180f, 1f, 0.05f));

            float[] rustle = Synth.Buffer(0.3f, R);
            var rng = new SynthRng(0xCA4Du);
            Synth.AddNoise(rustle, 0, rustle.Length, R, ref rng, 1f, 0.05f);
            Synth.Filter(rustle, 0, rustle.Length, Biquad.BandPass(R, 700f, 1.5f));
            Synth.Normalise(rustle, -4f);
            Synth.Mix(b, 0, rustle);

            Synth.FadeOut(b, b.Length, 0.02f, R);
            return b;
        }

        // Feather: noise, high-pass 5 kHz, decay 30 ms.
        static float[] LandFeather()
        {
            float[] b = Synth.Buffer(0.18f, R);
            var rng = new SynthRng(0xFEA7u);
            Synth.AddNoise(b, 0, b.Length, R, ref rng, 1f, 0.03f);
            Synth.Filter(b, 0, b.Length, Biquad.HighPass(R, 5000f, Sqrt2Inv));
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // Two 5 ms clicks band-passed at 1.2 kHz, plus a 90 Hz sine thump of 60 ms.
        static float[] PlayerLand()
        {
            float[] b = Synth.Buffer(0.09f, R);

            // The filter rings on a little after its 5 ms of noise.
            float[] click = Synth.Buffer(0.02f, R);
            var rng = new SynthRng(0xF007u);
            Synth.AddNoise(click, 0, N(0.005f), R, ref rng, 1f);
            Synth.Window(click, 0, N(0.005f));
            Synth.Filter(click, 0, click.Length, Biquad.BandPass(R, 1200f, 2f));
            Synth.FadeOut(click, click.Length, 0.005f, R);
            Synth.Normalise(click, -8f);
            Synth.Mix(b, 0, click);
            Synth.Mix(b, N(0.03f), click, 0.7f);

            Synth.Add(b, 0, N(0.06f), R, new Tone(Wave.Sine, 90f, 1f, 0.02f));
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // A 2 ms square click, then two marimba notes 70 ms apart (E5 then A5 on press, A5 then E5 on release).
        static float[] Button(float firstHz, float secondHz)
        {
            float[] b = Synth.Buffer(0.45f, R);
            Synth.Add(b, 0, N(0.002f), R, new Tone(Wave.Square, 1000f, 0.4f));
            Marimba(b, 0.004f, firstHz, 0.9f);
            Marimba(b, 0.074f, secondHz, 1f);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        // Not in the art bible's table: an exit unlocking is sol-do-sol on the marimba, rising and ringing
        // a little longer than a button; locking is do-sol, falling. Fifths and octaves only, so it is in
        // tune in every mode once transposed to the key.
        static float[] ExitOpen()
        {
            float[] b = Synth.Buffer(0.9f, R);
            Marimba(b, 0f, 783.99f, 0.8f, 0.12f);
            Marimba(b, 0.09f, 1046.5f, 0.9f, 0.12f);
            Marimba(b, 0.18f, 1567.98f, 1f, 0.12f);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        static float[] ExitClose()
        {
            float[] b = Synth.Buffer(0.6f, R);
            Marimba(b, 0f, 1046.5f, 1f, 0.08f);
            Marimba(b, 0.09f, 783.99f, 0.9f, 0.08f);
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        /// <summary>When the stamp of the level-complete clip lands, seconds (ART_BIBLE 9.7: the "COLLECTED" sticker).</summary>
        public const float StampAt = 0.4f;
        /// <summary>When the arpeggio's first note sounds; the others follow at sixteenth notes.</summary>
        public const float ArpeggioAt = 0.06f;

        // Shutter: two 12 ms noise bursts high-passed at 3 kHz, 40 ms apart. Arpeggio: marimba on scale
        // degrees 1-3-5-6-8 at sixteenth notes. Swell: an add9 pad chord, 1.2 s. Stamp at 400 ms: 90 Hz
        // thump plus 20 ms of noise. In the level's key and tempo.
        static float[] LevelComplete(MusicKey key, int bpm)
        {
            float[] b = LevelCompleteFront(key, bpm);
            float[] swell = LevelCompleteSwell(key);
            LevelCompleteBack(b, swell);
            return b;
        }

        // The shutter and the arpeggio.
        static float[] LevelCompleteFront(MusicKey key, int bpm)
        {
            float sixteenth = 15f / bpm;
            float[] b = Synth.Buffer(Math.Max(1.25f, ArpeggioAt + 4f * sixteenth + 0.75f), R);

            float[] shutter = Synth.Buffer(0.06f, R);
            var rng = new SynthRng(0x5A07u);
            Synth.AddNoise(shutter, 0, N(0.012f), R, ref rng, 1f);
            Synth.Window(shutter, 0, N(0.012f));
            Synth.AddNoise(shutter, N(0.04f), N(0.012f), R, ref rng, 1f);
            Synth.Window(shutter, N(0.04f), N(0.012f));
            Synth.Filter(shutter, 0, shutter.Length, Biquad.HighPass(R, 3000f, Sqrt2Inv));
            Synth.Normalise(shutter, -7f);
            Synth.Mix(b, 0, shutter);

            int tonic = 72 + key.Transpose;
            int[] degrees = key.Arpeggio;
            for (int i = 0; i < degrees.Length; i++)
                Marimba(b, ArpeggioAt + i * sixteenth, Synth.NoteHz(tonic + degrees[i]), 0.5f + 0.05f * i, 0.12f);
            return b;
        }

        // The swell's saws: the tonic's add9 chord, 1.2 s.
        static float[] LevelCompleteSwell(MusicKey key)
        {
            float[] swell = Synth.Buffer(1.2f, R);
            for (int note = 0; note < PadNotes; note++) AddPadNote(swell, R, 60 + key.Transpose, key.Chord(0), note, 1f);
            return swell;
        }

        // The swell shaped and mixed in, and the stamp.
        static void LevelCompleteBack(float[] b, float[] swell)
        {
            Synth.Filter(swell, 0, swell.Length, Biquad.LowPass(R, PadCutoffHz, Sqrt2Inv));
            Synth.Ramp(swell, 0, N(PadAttack), 0f, 1f);
            Synth.Ramp(swell, N(PadAttack), swell.Length - N(PadAttack), 1f, 0f);
            Synth.Normalise(swell, -9f);
            Synth.Mix(b, 0, swell);

            Synth.Add(b, N(StampAt), N(0.16f), R, new Tone(Wave.Sine, 90f, 0.9f, 0.04f));
            float[] slap = Synth.Buffer(0.02f, R);
            var rng = new SynthRng(0x57A9u);
            Synth.AddNoise(slap, 0, slap.Length, R, ref rng, 1f);
            Synth.Window(slap, 0, slap.Length);
            Synth.Filter(slap, 0, slap.Length, Biquad.LowPass(R, 1800f, Sqrt2Inv));
            Synth.Normalise(slap, -10f);
            Synth.Mix(b, N(StampAt), slap);

            Synth.FadeOut(b, b.Length, 0.02f, R);
        }

        // Music box: FM bell, ratio 1:4, index 2 -> 0 over 80 ms, decay 300 ms, 1.2 s.
        static float[] Box(int index)
        {
            float[] b = Synth.Buffer(1.2f, R);
            Synth.Add(b, 0, b.Length, R, new FmTone(Synth.NoteHz(Sounds.BoxLowNote + Sounds.BoxInterval * index), 4f, 2f, 0.08f, 0.3f));
            Synth.FadeOut(b, b.Length, 0.03f, R);
            return b;
        }

        // Woodblock: sine 1200 Hz, 20 ms.
        static float[] KitWood()
        {
            float[] b = Synth.Buffer(0.02f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 1200f, 1f, 0.006f));
            Synth.FadeOut(b, b.Length, 0.003f, R);
            return b;
        }

        // Shaker: noise high-passed at 5 kHz, 40 ms.
        static float[] KitShaker()
        {
            float[] b = Synth.Buffer(0.04f, R);
            var rng = new SynthRng(0x5A4Eu);
            Synth.AddNoise(b, 0, b.Length, R, ref rng, 1f, 0.012f);
            Synth.Filter(b, 0, b.Length, Biquad.HighPass(R, 5000f, Sqrt2Inv));
            Synth.FadeOut(b, b.Length, 0.005f, R);
            return b;
        }

        // Soft kick: sine 110 -> 50 Hz, 90 ms.
        static float[] KitKick()
        {
            float[] b = Synth.Buffer(0.09f, R);
            float fall = (float)(12.0 * Math.Log(50.0 / 110.0) / Math.Log(2.0));
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 110f, 1f, 0.035f) { GlideSemis = fall, GlideSeconds = 0.09f });
            Synth.FadeOut(b, b.Length, 0.01f, R);
            return b;
        }

        /// <summary>The note a chord's bass plays (E2-D#3) and the lowest note of its pad (C3-B3).</summary>
        public static int BassNote(MusicKey key, int chord) => Scales.NoteIn(key.ChordRoot(chord), 40);

        public static int PadRoot(MusicKey key, int chord) => Scales.NoteIn(key.ChordRoot(chord), 48);

        /// <summary>Seconds in a bar of four beats.</summary>
        public static float BarSeconds(int bpm) => 240f / Math.Max(1, bpm);

        // Bass: a sine with a 10 ms attack, one bar of a chord's root. 22,050 Hz.
        static float[] Bass(MusicKey key, int chord, int bpm)
        {
            float bar = BarSeconds(bpm);
            float[] b = Synth.Buffer(bar, Synth.LowRate);
            Synth.Add(b, 0, b.Length, Synth.LowRate, new Tone(Wave.Sine, Synth.NoteHz(BassNote(key, chord)), 1f, bar * 0.6f) { Attack = 0.01f });
            Synth.FadeOut(b, b.Length, 0.06f, Synth.LowRate);
            return b;
        }

        // Pad: three saws detuned +-7 cents through a 600 Hz low-pass, slow attack 400 ms; two bars of an
        // add9 chord (root, fifth, ninth and the third on top). 22,050 Hz.
        static float[] PadBuffer(int bpm) => Synth.Buffer(2f * BarSeconds(bpm) + PadRelease, Synth.LowRate);

        static void PadNote(float[] buffer, MusicKey key, int chord, int note) =>
            AddPadNote(buffer, Synth.LowRate, PadRoot(key, chord), key.Chord(chord), note, 1f);

        static void PadShape(float[] buffer, int bpm)
        {
            int rate = Synth.LowRate;
            Synth.Filter(buffer, 0, buffer.Length, Biquad.LowPass(rate, PadCutoffHz, Sqrt2Inv));
            Synth.Ramp(buffer, 0, Synth.Samples(PadAttack, rate), 0f, 1f);
            Synth.FadeOut(buffer, buffer.Length, PadRelease, rate);
        }

        /// <summary>The MIDI note of voice 0..3 of an add9 chord in open position: root, fifth, ninth, tenth.</summary>
        public static int PadNoteOf(int root, Chord chord, int note)
        {
            switch (note)
            {
                case 0: return root;
                case 1: return root + 7;
                case 2: return root + 14;
                default: return root + 12 + chord.Third;
            }
        }

        static void AddPadNote(float[] buffer, int rate, int root, Chord chord, int note, float gain)
        {
            float hz = Synth.NoteHz(PadNoteOf(root, chord, note));
            // Three saws 7 cents apart, and the notes' phases spread so twelve saws do not all jump at once.
            Synth.AddSaws(buffer, 0, buffer.Length, rate, hz, PadDetuneCents, gain / PadNotes, note * 0.211f);
        }
    }
}
