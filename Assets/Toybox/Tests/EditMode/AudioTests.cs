using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Audio;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Toys;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Toybox.Tests
{
    /// <summary>What the audio fixtures share: one generated bank, dry recipes, small signal helpers.</summary>
    static class AudioLab
    {
        public const int R = Synth.Rate;
        public const int Bpm = SoundRecipes.DefaultBpm;

        static SoundBank bank;
        static readonly Dictionary<SoundId, float[]> DryCache = new Dictionary<SoundId, float[]>();

        /// <summary>Every clip, generated once for the whole run, in C major at 84 BPM. Samples kept, no AudioClips.</summary>
        public static SoundBank Bank
        {
            get
            {
                if (bank == null)
                {
                    bank = new SoundBank { KeepSamples = true };
                    bank.SetKey(MusicKey.CMajor, Bpm);
                    bank.GenerateAll();
                }
                return bank;
            }
        }

        public static float[] Clip(SoundId id) => Bank.Samples(id);

        public static float[] Dry(SoundId id)
        {
            if (!DryCache.TryGetValue(id, out float[] dry)) DryCache[id] = dry = SoundRecipes.Dry(id, MusicKey.CMajor, Bpm);
            return dry;
        }

        public static int N(float seconds, int rate = R) => Synth.Samples(seconds, rate);

        public static float[] Sine(float hz, float seconds, float gain = 1f)
        {
            float[] b = Synth.Buffer(seconds, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, hz, gain));
            return b;
        }

        public static float[] Noise(float seconds, uint seed = 7u)
        {
            float[] b = Synth.Buffer(seconds, R);
            var rng = new SynthRng(seed);
            Synth.AddNoise(b, 0, b.Length, R, ref rng, 1f);
            return b;
        }

        /// <summary>The gain a filter leaves of a sine: its level in the second half of a second.</summary>
        public static float Through(Action<float[]> filter, float hz)
        {
            float[] b = Sine(hz, 1f);
            filter(b);
            return SoundStats.Level(b, R, hz, R / 2, R / 2);
        }

        public static float Level(float[] b, float hz, float from, float to, int rate = R) => SoundStats.Level(b, rate, hz, N(from, rate), N(to - from, rate));

        public static float Dominant(float[] b, float from, float to, float minHz = 20f, float maxHz = float.MaxValue, int rate = R) =>
            SoundStats.DominantHz(b, rate, N(from, rate), N(to - from, rate), minHz, maxHz);

        public static float Centroid(float[] b, float from, float to, int rate = R) => SoundStats.Centroid(b, rate, N(from, rate), N(to - from, rate));

        public static IEnumerable<SoundId> All()
        {
            for (int i = 1; i < Sounds.Count; i++) yield return (SoundId)i;
        }

        public static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// The synthesizer's building blocks, each held against the number it is supposed to produce: pitch by
    /// counting cycles, level by a single-frequency Fourier sum, brightness by the spectral centroid of a
    /// small FFT, decay by the level in two windows.
    /// </summary>
    public class AudioSynthTests
    {
        const int R = AudioLab.R;

        [Test]
        public void ASine_HasItsFrequencyItsLevelAndItsDecay()
        {
            float[] b = AudioLab.Sine(1000f, 1f, 0.5f);
            Assert.AreEqual(1000, SoundStats.Cycles(b), 1, "a 1 kHz sine has a thousand cycles a second");
            Assert.AreEqual(0.5f, SoundStats.Peak(b), 0.002f);
            Assert.AreEqual(0.5f, SoundStats.Level(b, R, 1000f), 0.005f);
            Assert.AreEqual(0.5f / Mathf.Sqrt(2f), SoundStats.Rms(b), 0.002f);
            Assert.AreEqual(1000f, SoundStats.DominantHz(b, R), 1f);
            Assert.AreEqual(1000f, SoundStats.Centroid(b, R), 30f, "a pure tone's centroid is its frequency");
            Assert.Less(Mathf.Abs(b[0]), 1e-6f, "it starts at zero: no click");

            float[] decaying = Synth.Buffer(0.5f, R);
            Synth.Add(decaying, 0, decaying.Length, R, new Tone(Wave.Sine, 880f, 1f, 0.1f));
            Assert.AreEqual(0.1f, SoundStats.DecayTime(decaying, R, 880f, 0.05f, 0.25f, 0.05f), 0.003f, "decay tau 100 ms");

            // The table sine (used wherever the pitch moves) agrees with the resonator (fixed pitch).
            float[] glided = Synth.Buffer(0.5f, R);
            Synth.Add(glided, 0, glided.Length, R, new Tone(Wave.Sine, 880f, 1f, 0.1f) { GlideSemis = 1e-6f, GlideSeconds = 0.01f });
            for (int i = 0; i < glided.Length; i += 97) Assert.AreEqual(decaying[i], glided[i], 2e-3f, "sample " + i);

            // An attack fades the start in.
            float[] soft = Synth.Buffer(0.2f, R);
            Synth.Add(soft, 0, soft.Length, R, new Tone(Wave.Sine, 880f, 1f) { Attack = 0.05f });
            Assert.Less(SoundStats.Rms(soft, 0, AudioLab.N(0.01f)), 0.15f);
            Assert.AreEqual(1f / Mathf.Sqrt(2f), SoundStats.Rms(soft, AudioLab.N(0.1f), AudioLab.N(0.1f)), 0.01f);
        }

        [Test]
        public void AGlide_MovesThePitchBySemitones()
        {
            // The release thock's body: 196 Hz falling five semitones over 80 ms.
            float[] b = Synth.Buffer(0.4f, R);
            Synth.Add(b, 0, b.Length, R, new Tone(Wave.Sine, 196f, 1f) { GlideSemis = -5f, GlideSeconds = 0.08f });
            float landed = 196f * Mathf.Pow(2f, -5f / 12f);
            Assert.AreEqual(146.83f, landed, 0.01f);
            Assert.AreEqual(landed, AudioLab.Dominant(b, 0.1f, 0.4f), 1.5f, "after the glide it stays five semitones down");
            // Through the glide the frequency falls evenly in pitch: 13.6 cycles in the 80 ms, not 15.7 or 11.7.
            Assert.That(SoundStats.Cycles(b, 0, AudioLab.N(0.08f)), Is.InRange(13, 14));

            // The soft kick: 110 -> 50 Hz over 90 ms is 6.85 cycles.
            float[] kick = AudioLab.Dry(SoundId.KitKick);
            Assert.AreEqual(AudioLab.N(0.09f), kick.Length);
            Assert.That(SoundStats.Cycles(kick), Is.InRange(6, 7));
        }

        [Test]
        public void TheWaves_HaveTheirHarmonics()
        {
            const float Hz = 220.5f;
            float[] triangle = Synth.Buffer(0.5f, R), saw = Synth.Buffer(0.5f, R), square = Synth.Buffer(0.5f, R);
            Synth.Add(triangle, 0, triangle.Length, R, new Tone(Wave.Triangle, Hz, 1f));
            Synth.Add(saw, 0, saw.Length, R, new Tone(Wave.Saw, Hz, 1f));
            Synth.Add(square, 0, square.Length, R, new Tone(Wave.Square, Hz, 1f));

            // Triangle: odd harmonics falling with the square of their number.
            Assert.AreEqual(8f / (Mathf.PI * Mathf.PI), SoundStats.Level(triangle, R, Hz), 0.01f);
            Assert.AreEqual(8f / (Mathf.PI * Mathf.PI) / 9f, SoundStats.Level(triangle, R, 3f * Hz), 0.005f);
            Assert.Less(SoundStats.Level(triangle, R, 2f * Hz), 0.003f);
            Assert.AreEqual(1f, SoundStats.Peak(triangle), 0.01f);
            Assert.Less(Mathf.Abs(triangle[0]), 1e-6f, "the triangle starts at zero");

            // Saw: every harmonic, falling with its number.
            Assert.AreEqual(2f / Mathf.PI, SoundStats.Level(saw, R, Hz), 0.01f);
            Assert.AreEqual(1f / Mathf.PI, SoundStats.Level(saw, R, 2f * Hz), 0.01f);
            Assert.AreEqual(2f / Mathf.PI / 3f, SoundStats.Level(saw, R, 3f * Hz), 0.01f);

            // Square: odd harmonics, falling with their number.
            Assert.AreEqual(4f / Mathf.PI, SoundStats.Level(square, R, Hz), 0.02f);
            Assert.AreEqual(4f / Mathf.PI / 3f, SoundStats.Level(square, R, 3f * Hz), 0.01f);
            Assert.Less(SoundStats.Level(square, R, 2f * Hz), 0.01f);

            foreach (float[] wave in new[] { triangle, saw, square })
            {
                Assert.AreEqual(110, SoundStats.Cycles(wave), 1, "220.5 Hz for half a second");
                Assert.Less(SoundStats.Peak(wave), 1.2f, "band-limiting overshoots a little, never much");
            }
        }

        [Test]
        public void Noise_IsSeededWhiteAndBounded()
        {
            float[] a = AudioLab.Noise(1f, 42u), again = AudioLab.Noise(1f, 42u), other = AudioLab.Noise(1f, 43u);
            Assert.IsTrue(AudioLab.Same(a, again), "the same seed gives the same noise");
            Assert.IsFalse(AudioLab.Same(a, other));
            Assert.LessOrEqual(SoundStats.Peak(a), 1f);
            Assert.AreEqual(1f / Mathf.Sqrt(3f), SoundStats.Rms(a), 0.01f, "uniform noise in [-1, 1)");
            double mean = 0;
            foreach (float sample in a) mean += sample;
            Assert.Less(Math.Abs(mean / a.Length), 0.01);
            Assert.AreEqual(R / 4f, SoundStats.Centroid(a, R, 0, 16384), R / 4f * 0.06f, "white: the centroid is half way up the spectrum");

            // The generator is a value: a copy continues the same stream, the original is not disturbed.
            var rng = new SynthRng(5u);
            SynthRng copy = rng;
            Assert.AreEqual(rng.NextUInt(), copy.NextUInt());
            Assert.That(new SynthRng(1u).Value(), Is.InRange(0f, 1f));
            Assert.AreNotEqual(SynthRng.Mix(1u, 2u), SynthRng.Mix(2u, 1u));
            for (int i = 0; i < 200; i++) Assert.That(rng.Range(5), Is.InRange(0, 4));
        }

        [Test]
        public void Filters_PassAndStopWhatTheyShould()
        {
            // One pole: 6 dB per octave, so a decade away is a tenth.
            Assert.Greater(AudioLab.Through(b => Synth.LowPass(b, 0, b.Length, R, 1000f), 100f), 0.97f);
            Assert.That(AudioLab.Through(b => Synth.LowPass(b, 0, b.Length, R, 1000f), 10000f), Is.InRange(0.06f, 0.14f));
            Assert.That(AudioLab.Through(b => Synth.HighPass(b, 0, b.Length, R, 1000f), 100f), Is.InRange(0.07f, 0.14f));
            Assert.Greater(AudioLab.Through(b => Synth.HighPass(b, 0, b.Length, R, 1000f), 10000f), 0.9f);

            // Biquads: 12 dB per octave, so two octaves away is a sixteenth.
            Action<float[]> low = b => Synth.Filter(b, 0, b.Length, Biquad.LowPass(R, 900f, 0.7071f));
            Assert.Greater(AudioLab.Through(low, 225f), 0.98f);
            Assert.AreEqual(0.707f, AudioLab.Through(low, 900f), 0.03f, "3 dB down at the cutoff");
            Assert.Less(AudioLab.Through(low, 3600f), 0.07f);

            Action<float[]> high = b => Synth.Filter(b, 0, b.Length, Biquad.HighPass(R, 2000f, 0.7071f));
            Assert.Less(AudioLab.Through(high, 500f), 0.07f);
            Assert.AreEqual(0.707f, AudioLab.Through(high, 2000f), 0.03f);
            Assert.Greater(AudioLab.Through(high, 8000f), 0.97f);

            Action<float[]> band = b => Synth.Filter(b, 0, b.Length, Biquad.BandPass(R, 700f, 1.5f));
            Assert.AreEqual(1f, AudioLab.Through(band, 700f), 0.03f, "unit gain at the centre");
            Assert.Less(AudioLab.Through(band, 175f), 0.25f);
            Assert.Less(AudioLab.Through(band, 2800f), 0.25f);

            // The peel of the grab: the band moves up through the noise.
            float[] noise = AudioLab.Noise(0.5f);
            Synth.SweptBandPass(noise, 0, noise.Length, R, 800f, 2400f, 1.2f);
            float early = AudioLab.Centroid(noise, 0f, 0.1f), late = AudioLab.Centroid(noise, 0.4f, 0.5f);
            Assert.That(early, Is.InRange(800f, 2600f));
            Assert.That(late, Is.InRange(2400f, 7500f));
            Assert.Less(early, late * 0.65f, "swept upward");
            Assert.IsTrue(SoundStats.Finite(noise));
        }

        [Test]
        public void Fm_PutsSidebandsAroundTheCarrier()
        {
            // Carrier 1 kHz, modulator 4 kHz, index 2 (held for the measurement): Bessel says J0 = 0.224 at
            // the carrier, J1 = 0.577 at 1 +- 4 kHz (the lower one folds to 3 kHz), J2 = 0.353 at 1 +- 8 kHz.
            float[] b = Synth.Buffer(0.2f, R);
            Synth.Add(b, 0, b.Length, R, new FmTone(1000f, 4f, 2f, 1000f, 0f));
            Assert.AreEqual(0.224f, SoundStats.Level(b, R, 1000f), 0.02f);
            Assert.AreEqual(0.577f, SoundStats.Level(b, R, 3000f), 0.02f);
            Assert.AreEqual(0.577f, SoundStats.Level(b, R, 5000f), 0.02f);
            Assert.AreEqual(0.353f, SoundStats.Level(b, R, 7000f), 0.02f);
            Assert.AreEqual(0.353f, SoundStats.Level(b, R, 9000f), 0.02f);
            Assert.Less(SoundStats.Level(b, R, 2000f), 0.01f, "nothing between the sidebands");
            Assert.LessOrEqual(SoundStats.Peak(b), 1.0001f, "phase modulation never exceeds the carrier's amplitude");

            // Once the index has fallen to zero it is a plain decaying sine.
            float[] bell = Synth.Buffer(0.5f, R);
            Synth.Add(bell, 0, bell.Length, R, new FmTone(1000f, 4f, 2f, 0.08f, 0.3f));
            Assert.AreEqual(1000f, AudioLab.Dominant(bell, 0.1f, 0.5f), 2f);
            Assert.Less(AudioLab.Level(bell, 5000f, 0.1f, 0.5f), 0.005f);
            Assert.Greater(AudioLab.Level(bell, 5000f, 0f, 0.04f), 0.2f, "bright at the strike");
            Assert.AreEqual(0.3f, SoundStats.DecayTime(bell, R, 1000f, 0.1f, 0.35f, 0.1f), 0.01f);
        }

        [Test]
        public void TheRoom_DecaysOverItsRt60_AndIsAsLoudAsItsWetLevel()
        {
            // An impulse into the room, three seconds of tail.
            var impulse = new float[1];
            impulse[0] = 1f;
            float[] wet = Synth.Reverb(impulse, R, 1f, 3f);
            Assert.AreEqual(1 + 3 * R, wet.Length);
            Assert.AreEqual(1f, wet[0], 1e-6f, "the dry signal passes untouched");
            Assert.IsTrue(SoundStats.Finite(wet));

            // The first echo arrives after the shortest comb (29.7 ms) - nothing but the dry sample before it.
            Assert.Less(SoundStats.Peak(Slice(wet, 1, AudioLab.N(0.029f))), 1e-6f);
            Assert.Greater(SoundStats.Peak(Slice(wet, AudioLab.N(0.029f), AudioLab.N(0.02f))), 0.05f);

            wet[0] = 0f;
            double energy = 0;
            foreach (float sample in wet) energy += (double)sample * sample;
            Assert.That(energy, Is.InRange(0.2, 1.6), "an impulse comes back with about unit energy, so 'wet 0.18' is a level");

            // Reverberation time, measured where the 4 kHz damping does not shorten it: below 500 Hz.
            Synth.Filter(wet, 0, wet.Length, Biquad.LowPass(R, 500f, 0.7071f));
            Assert.AreEqual(1.4f, Rt60(wet), 0.25f, "RT60 of 1.4 s");

            // Above the damping it dies sooner: "a 4 kHz low-pass in the feedback".
            float[] bright = Synth.Reverb(impulse, R, 1f, 3f);
            bright[0] = 0f;
            Synth.Filter(bright, 0, bright.Length, Biquad.HighPass(R, 6000f, 0.7071f));
            Assert.Less(Rt60(bright), 0.7f);

            // In pieces it is the same as in one go (the bank reverberates 16k samples per stage).
            float[] dry = AudioLab.Dry(SoundId.ReleaseThock);
            float[] whole = Synth.Reverb(dry, R, 0.18f);
            var pieces = new float[whole.Length];
            var room = new SchroederReverb(R);
            for (int at = 0; at < pieces.Length; at += 1000) room.Process(dry, pieces, at, 1000, 0.18f);
            Assert.IsTrue(AudioLab.Same(whole, pieces));

            // At 22,050 Hz (pads, bass) the delays are the same milliseconds.
            float[] lowRate = Synth.Reverb(impulse, Synth.LowRate, 1f, 3f);
            lowRate[0] = 0f;
            Synth.Filter(lowRate, 0, lowRate.Length, Biquad.LowPass(Synth.LowRate, 500f, 0.7071f));
            Assert.AreEqual(1.4f, Rt60(lowRate, Synth.LowRate), 0.25f);
        }

        static float[] Slice(float[] source, int start, int count)
        {
            var slice = new float[count];
            Array.Copy(source, start, slice, 0, count);
            return slice;
        }

        // Schroeder's backward integration: twice the time the energy still to come takes from -5 to -35 dB.
        static float Rt60(float[] response, int rate = R)
        {
            var remaining = new double[response.Length + 1];
            for (int i = response.Length - 1; i >= 0; i--) remaining[i] = remaining[i + 1] + (double)response[i] * response[i];
            int at5 = -1, at35 = -1;
            for (int i = 0; i < response.Length; i++)
            {
                double db = 10.0 * Math.Log10(remaining[i] / remaining[0] + 1e-30);
                if (at5 < 0 && db <= -5.0) at5 = i;
                if (at35 < 0 && db <= -35.0)
                {
                    at35 = i;
                    break;
                }
            }
            Assert.Greater(at35, at5, "the response decays by 35 dB within its length");
            return 2f * (at35 - at5) / rate;
        }

        [Test]
        public void Mastering_SetsThePeakAndCutsTheTail()
        {
            float[] dry = Synth.Buffer(0.1f, R);
            Synth.Add(dry, 0, dry.Length, R, new Tone(Wave.Sine, 440f, 0.3f, 0.02f));
            float[] clip = Synth.Master(dry, R, 0.18f, -9f);
            Assert.AreEqual(-9f, SoundStats.PeakDb(clip), 0.01f);
            Assert.Greater(clip.Length, dry.Length, "the room's tail is kept");
            Assert.Less(clip.Length, dry.Length + AudioLab.N(1.4f), "and cut where it has died away");
            Assert.AreEqual(0f, clip[clip.Length - 1], 1e-6f, "the cut is faded, not a click");

            // -50 dBFS is where it is cut: just before the cut the tail is still about that loud, never much louder.
            float floor = Synth.Gain(-50f);
            int fade = AudioLab.N(0.008f);
            Assert.Less(SoundStats.Peak(Slice(clip, clip.Length - fade, fade)), floor * 1.01f);
            Assert.Greater(SoundStats.Peak(Slice(clip, clip.Length - fade - 400, 400)), floor * 0.5f);

            // A quieter clip is cut sooner: its tail reaches the floor earlier.
            Assert.Less(Synth.Master(dry, R, 0.18f, -26f).Length, clip.Length);

            Assert.AreEqual(0.5012f, Synth.Gain(-6f), 0.0005f);
            Assert.AreEqual(-6.0206f, Synth.Decibels(0.5f), 0.001f);
            Assert.AreEqual(2f, Synth.Ratio(12f), 1e-5f);
            Assert.AreEqual(261.63f, Synth.NoteHz(60), 0.01f);
            Assert.AreEqual(440f, Synth.NoteHz(69), 1e-3f);
            Assert.AreEqual(1f, Synth.Sin(0.25), 1e-6f);
            Assert.AreEqual(-1f, Synth.Sin(-0.25), 1e-6f);
            for (double phase = -3.0; phase < 3.0; phase += 0.0371) Assert.AreEqual(Math.Sin(2.0 * Math.PI * phase), Synth.Sin(phase), 1e-6, "sine table at " + phase);
        }

        [Test]
        public void TheFft_FindsATone()
        {
            float[] b = AudioLab.Sine(1234.5f, 0.3f, 0.3f);
            Assert.AreEqual(1234.5f, SoundStats.DominantHz(b, R), 1.5f);
            Assert.AreEqual(0.3f, SoundStats.Level(b, R, 1234.5f), 0.003f);
            Assert.Less(SoundStats.Level(b, R, 2469f), 0.001f);

            // Against the definition: a direct Fourier sum of a short signal.
            var re = new double[64];
            var im = new double[64];
            var expected = new double[64];
            for (int i = 0; i < 64; i++) re[i] = Math.Sin(i * 0.7) + 0.25 * Math.Cos(i * 2.1);
            for (int k = 0; k < 64; k++)
            {
                double sumRe = 0, sumIm = 0;
                for (int i = 0; i < 64; i++)
                {
                    sumRe += re[i] * Math.Cos(2 * Math.PI * k * i / 64);
                    sumIm -= re[i] * Math.Sin(2 * Math.PI * k * i / 64);
                }
                expected[k] = Math.Sqrt(sumRe * sumRe + sumIm * sumIm);
            }
            SoundStats.Fft(re, im);
            for (int k = 0; k < 64; k++) Assert.AreEqual(expected[k], Math.Sqrt(re[k] * re[k] + im[k] * im[k]), 1e-9, "bin " + k);

            Assert.IsFalse(SoundStats.Finite(new[] { 0f, float.NaN }));
            Assert.IsFalse(SoundStats.Finite(new[] { float.PositiveInfinity }));
        }
    }

    /// <summary>
    /// Every clip of ART_BIBLE 11.3 and 11.4 against its recipe - numerically, since nobody can listen to
    /// it here: lengths, peak levels, pitches, mode frequencies and decays, brightness, the loop's seam -
    /// and the bank that generates them: order, slices, cost, memory, determinism.
    /// </summary>
    public class AudioClipTests
    {
        const int R = AudioLab.R;

        [Test]
        public void EveryClip_IsFinite_Unclipped_AndPeaksWhereItsSpecSays()
        {
            SoundBank bank = AudioLab.Bank;
            Assert.IsTrue(bank.Complete);
            Assert.IsTrue(bank.StaticReady);
            Assert.IsTrue(bank.KeyReady);
            float floor = Synth.Gain(Sounds.TailFloorDb);
            foreach (SoundId id in AudioLab.All())
            {
                float[] clip = bank.Samples(id);
                SoundSpec spec = Sounds.Spec(id);
                Assert.IsNotNull(clip, id + " was generated");
                Assert.Greater(clip.Length, 16, id + " has samples");
                Assert.IsTrue(SoundStats.Finite(clip), id + " has no NaN");
                Assert.Less(SoundStats.Peak(clip), 1f, id + " does not clip");
                Assert.AreEqual(spec.PeakDb, SoundStats.PeakDb(clip), 0.02f, id + " peaks at its level");
                // Neither a single spike in silence nor a brick: the loudness sits a sensible way under the peak.
                Assert.That(SoundStats.PeakDb(clip) - SoundStats.RmsDb(clip), Is.InRange(3f, 26f), id + " crest factor");
                Assert.AreEqual(clip.Length / (float)spec.Rate, bank.Seconds(id), 1e-5f);
                if (spec.Loop) continue;

                // The baked room: longer than the recipe, and cut at -50 dBFS with a fade to nothing.
                float[] dry = AudioLab.Dry(id);
                Assert.LessOrEqual(clip.Length, dry.Length + Synth.Samples(1.4f, spec.Rate), id + " tail");
                Assert.AreEqual(0f, clip[clip.Length - 1], 1e-6f, id + " ends in silence");
                int last = Synth.Samples(0.008f, spec.Rate);
                for (int i = clip.Length - last; i < clip.Length; i++) Assert.Less(Mathf.Abs(clip[i]), floor * 1.01f, id + " tail is below the floor");
                Assert.Less(Mathf.Abs(Mean(clip)), 0.02f, id + " has no DC offset");
            }

            // Tonal clips begin at (next to) zero: a clip that started mid-wave would click every time it is played.
            foreach (SoundId id in AudioLab.All())
            {
                bool tonal = Sounds.IsBox(id) || Sounds.IsBass(id) || Sounds.IsPad(id) || SoundRecipes.ModeCount(id) > 0 || id == SoundId.FocusTick ||
                             id == SoundId.HoldJump || id == SoundId.UiHover || id == SoundId.UiClick || id == SoundId.Grab || id == SoundId.ReleaseSub ||
                             id == SoundId.ReleaseBell || id == SoundId.KitWood || id == SoundId.KitKick || id == SoundId.ExitOpen || id == SoundId.ExitClose;
                if (!tonal) continue;
                float[] clip = bank.Samples(id);
                Assert.Less(Mathf.Abs(clip[0]), 0.05f * SoundStats.Peak(clip), id + " starts at zero");
            }

            // The gain staging: effects -9 dBFS, music voices -18, the hold loop -29.
            Assert.AreEqual(-9f, SoundStats.PeakDb(bank.Samples(SoundId.Grab)), 0.02f);
            Assert.AreEqual(-18f, SoundStats.PeakDb(bank.Samples(SoundId.Box4)), 0.02f);
            Assert.AreEqual(-18f, SoundStats.PeakDb(bank.Samples(SoundId.Pad0)), 0.02f);
            Assert.AreEqual(-29f, SoundStats.PeakDb(bank.Samples(SoundId.Hold)), 0.02f);
            Assert.AreEqual(-26f, SoundStats.PeakDb(bank.Samples(SoundId.FocusTick)), 0.02f);
            Assert.AreEqual(-19f, SoundStats.PeakDb(bank.Samples(SoundId.LandFelt)), 0.02f, "felt is 10 dB under the effect peak");
            Assert.AreEqual(-35f, SoundStats.PeakDb(bank.Samples(SoundId.LandFeather)), 0.02f, "a feather 26 dB under it");

            // Sample rates: 44.1 kHz for effects and the music box, 22.05 kHz for pads and bass.
            foreach (SoundId id in AudioLab.All())
                Assert.AreEqual(Sounds.IsPad(id) || Sounds.IsBass(id) ? 22050 : 44100, Sounds.Spec(id).Rate, id.ToString());

            // The release sub carries more room than anything else (wet 0.6): its tail is the longest relative to its body.
            Assert.Greater(bank.Samples(SoundId.ReleaseSub).Length, AudioLab.Dry(SoundId.ReleaseSub).Length + AudioLab.N(0.5f));
            Assert.AreEqual(0.6f, Sounds.Spec(SoundId.ReleaseSub).Wet);
            Assert.AreEqual(0.18f, Sounds.Spec(SoundId.Grab).Wet);
            Assert.AreEqual(0.30f, Sounds.Spec(SoundId.Box0).Wet);
        }

        static float Mean(float[] buffer)
        {
            double sum = 0;
            foreach (float sample in buffer) sum += sample;
            return (float)(sum / buffer.Length);
        }

        [Test]
        public void Clips_AreTheSameEveryTime()
        {
            SoundBank bank = AudioLab.Bank;
            foreach (SoundId id in AudioLab.All())
            {
                float[] again = SoundRecipes.Render(id, MusicKey.CMajor, AudioLab.Bpm);
                Assert.IsTrue(AudioLab.Same(bank.Samples(id), again), id + " is generated bit for bit the same, sliced or in one go");
            }
            // The per-key clips do depend on key and tempo.
            Assert.IsFalse(AudioLab.Same(SoundRecipes.Render(SoundId.Pad0, new MusicKey(9, MusicScale.MinorPentatonic), 76), bank.Samples(SoundId.Pad0)));
            Assert.IsFalse(AudioLab.Same(SoundRecipes.Render(SoundId.Bass0, MusicKey.CMajor, 92), bank.Samples(SoundId.Bass0)));
            // The others do not.
            Assert.IsTrue(AudioLab.Same(SoundRecipes.Render(SoundId.Grab, new MusicKey(9, MusicScale.MinorPentatonic), 76), bank.Samples(SoundId.Grab)));
        }

        [Test]
        public void Ticks_AndUiSounds_MatchTheirRecipes()
        {
            // Focus tick: sine 1.8 kHz, 25 ms, decay 6 ms, -26 dBFS.
            float[] tick = AudioLab.Dry(SoundId.FocusTick);
            Assert.AreEqual(AudioLab.N(0.025f), tick.Length);
            Assert.AreEqual(1800f, SoundStats.DominantHz(tick, R), 30f);
            Assert.AreEqual(45, SoundStats.Cycles(tick), 1, "1.8 kHz for 25 ms");
            Assert.AreEqual(0.006f, SoundStats.DecayTime(tick, R, 1800f, 0f, 0.008f, 0.008f), 0.0006f);

            // Hold jump tick: sine 3 kHz, 2 ms.
            float[] jump = AudioLab.Dry(SoundId.HoldJump);
            Assert.AreEqual(AudioLab.N(0.002f), jump.Length);
            Assert.AreEqual(6, SoundStats.Cycles(jump), 1, "3 kHz for 2 ms");
            Assert.AreEqual(3000f, SoundStats.DominantHz(jump, R), 150f);

            // UI hover: sine 2 kHz for 8 ms. UI click: one marimba C6, 60 ms.
            float[] hover = AudioLab.Dry(SoundId.UiHover);
            Assert.AreEqual(AudioLab.N(0.008f), hover.Length);
            Assert.AreEqual(16, SoundStats.Cycles(hover), 1);
            Assert.AreEqual(2000f, SoundStats.DominantHz(hover, R), 60f);
            float[] click = AudioLab.Dry(SoundId.UiClick);
            Assert.AreEqual(AudioLab.N(0.06f), click.Length);
            Assert.AreEqual(1046.5f, AudioLab.Dominant(click, 0.04f, 0.06f, 500f, 2000f), 40f, "C6 once the strike's brightness is gone");

            // All four are short and quiet in the bank.
            Assert.Less(AudioLab.Bank.Seconds(SoundId.FocusTick), 0.5f);
            Assert.Less(AudioLab.Bank.Seconds(SoundId.HoldJump), 0.5f);
            Assert.Less(SoundStats.PeakDb(AudioLab.Clip(SoundId.UiHover)), -20f);
        }

        [Test]
        public void Grab_IsAPeelAndAPluck()
        {
            float[] grab = AudioLab.Dry(SoundId.Grab);
            Assert.AreEqual(AudioLab.N(0.14f), grab.Length);
            // The first 20 ms are the peel alone: noise in a band that starts at 800 Hz.
            float peel = AudioLab.Centroid(grab, 0f, 0.02f);
            Assert.That(peel, Is.InRange(700f, 3000f));
            Assert.Greater(SoundStats.Band(grab, R, 600f, 2400f, 0, AudioLab.N(0.02f)), 4.0 * SoundStats.Band(grab, R, 8000f, 12000f, 0, AudioLab.N(0.02f)));
            // The pluck: 523 Hz with a +3 semitone glide over its first 50 ms, so it settles at 622 Hz.
            Assert.AreEqual(523.25f * Mathf.Pow(2f, 3f / 12f), AudioLab.Dominant(grab, 0.075f, 0.14f), 12f);
            // Decay 40 ms: 30 ms further on it has fallen to e^-0.75.
            float early = AudioLab.Level(grab, 622.25f, 0.075f, 0.1f), late = AudioLab.Level(grab, 622.25f, 0.105f, 0.13f);
            Assert.AreEqual(Mathf.Exp(-0.75f), late / early, 0.08f);
        }

        [Test]
        public void HoldLoop_IsTwoSecondsSeamless_AndBeatsOnceASecond()
        {
            float[] hold = AudioLab.Clip(SoundId.Hold);
            Assert.AreEqual(2 * R, hold.Length, "a 2.000 s loop");
            Assert.IsTrue(Sounds.Spec(SoundId.Hold).Loop);

            // The recipe (before the room): triangles at 220.5 and 219.5 Hz - 441 and 439 whole cycles - equally loud.
            float[] dry = AudioLab.Dry(SoundId.Hold);
            Assert.AreEqual(2 * R, dry.Length);
            float upper = SoundStats.Level(dry, R, 220.5f), lower = SoundStats.Level(dry, R, 219.5f);
            Assert.AreEqual(1f, upper / lower, 0.02f);
            Assert.Greater(upper, 30f * SoundStats.Level(dry, R, 200f));
            // A triangle's third harmonic is a ninth, less what the 900 Hz low-pass takes; the eleventh is gone.
            float third = SoundStats.Level(dry, R, 661.5f) / upper;
            Assert.That(third, Is.InRange(0.08f, 0.115f));
            Assert.Less(SoundStats.Level(dry, R, 2425.5f) / upper, 0.003f, "low-passed at 900 Hz");
            Assert.Less(SoundStats.Level(dry, R, 441f) / upper, 0.003f, "a triangle has no even harmonics");

            // Beating at 1 Hz: loud at 0 s, 1 s and 2 s, cancelled at 0.5 s and 1.5 s.
            float loud = SoundStats.Rms(dry, 0, AudioLab.N(0.1f)), quiet = SoundStats.Rms(dry, AudioLab.N(0.45f), AudioLab.N(0.1f));
            Assert.Greater(loud, 5f * quiet);
            Assert.AreEqual(loud, SoundStats.Rms(dry, AudioLab.N(0.95f), AudioLab.N(0.1f)), loud * 0.1f);
            Assert.Less(SoundStats.Rms(dry, AudioLab.N(1.45f), AudioLab.N(0.1f)), loud * 0.2f);
            Assert.Less(Mathf.Abs(dry[0] - dry[dry.Length - 1]), SoundStats.MaxStep(dry) * 1.01f, "the dry loop closes on itself");

            // With the room baked in, both tones are still there and it still beats (the room's combs colour them a little).
            float wetUpper = SoundStats.Level(hold, R, 220.5f), wetLower = SoundStats.Level(hold, R, 219.5f);
            Assert.That(wetUpper / wetLower, Is.InRange(0.5f, 2f));
            Assert.Greater(wetUpper, 20f * SoundStats.Level(hold, R, 200f));
            float strongest = 0f, weakest = float.MaxValue;
            for (float at = 0f; at < 1.95f; at += 0.05f)
            {
                float rms = SoundStats.Rms(hold, AudioLab.N(at), AudioLab.N(0.05f));
                strongest = Mathf.Max(strongest, rms);
                weakest = Mathf.Min(weakest, rms);
            }
            Assert.Greater(strongest, 1.5f * weakest, "the beat is audible");

            // The seam: played end to start, the step across it is no larger than the steps beside it, and
            // the slope carries on.
            float seam = Mathf.Abs(hold[0] - hold[hold.Length - 1]);
            float before = Mathf.Abs(hold[hold.Length - 1] - hold[hold.Length - 2]), after = Mathf.Abs(hold[1] - hold[0]);
            Assert.Less(seam, SoundStats.MaxStep(hold) * 1.01f, "no click at the loop point");
            Assert.AreEqual((before + after) * 0.5f, seam, Mathf.Max(before, after) * 0.5f + 1e-5f, "the wave runs smoothly through the seam");
        }

        [Test]
        public void Release_IsAThock_ASub_AndABell()
        {
            // Thock: 196 Hz falling five semitones over 80 ms, decay 30 ms, plus 15 ms of noise above 2 kHz.
            float[] thock = AudioLab.Dry(SoundId.ReleaseThock);
            Assert.AreEqual(146.8f, AudioLab.Dominant(thock, 0.085f, 0.2f), 12f, "the body lands five semitones under 196 Hz");
            Assert.That(AudioLab.Dominant(thock, 0f, 0.05f, 60f, 400f), Is.InRange(160f, 200f), "and starts near 196 Hz");
            Assert.Greater(SoundStats.Band(thock, R, 3000f, 9000f, 0, AudioLab.N(0.015f)), 8.0 * SoundStats.Band(thock, R, 3000f, 9000f, AudioLab.N(0.03f), AudioLab.N(0.015f)), "the click is at the front only");
            Assert.Greater(AudioLab.Centroid(thock, 0f, 0.015f), AudioLab.Centroid(thock, 0.04f, 0.055f), "and brightens it");
            Assert.AreEqual(Mathf.Exp(-1f), SoundStats.Rms(thock, AudioLab.N(0.06f), AudioLab.N(0.03f)) / SoundStats.Rms(thock, AudioLab.N(0.03f), AudioLab.N(0.03f)), 0.08f, "decay 30 ms");

            // Sub: 55 Hz sine, 250 ms.
            float[] sub = AudioLab.Dry(SoundId.ReleaseSub);
            Assert.AreEqual(AudioLab.N(0.25f), sub.Length);
            Assert.AreEqual(55f, SoundStats.DominantHz(sub, R), 3f);
            Assert.Less(SoundStats.Centroid(sub, R), 110f);
            Assert.That(SoundStats.Cycles(sub), Is.InRange(13, 14), "55 Hz for a quarter second");

            // Bell: FM, carrier 2093 Hz, ratio 1:3.5, index 3 -> 0 in 60 ms, 200 ms.
            float[] bell = AudioLab.Dry(SoundId.ReleaseBell);
            Assert.AreEqual(AudioLab.N(0.2f), bell.Length);
            Assert.AreEqual(2093f, AudioLab.Dominant(bell, 0.07f, 0.2f), 20f, "the carrier is what is left");
            // Sidebands at 2093 +- 7325.5 Hz while the index is up: 9418 Hz, and 5232 Hz folded.
            Assert.Greater(AudioLab.Level(bell, 9418.5f, 0f, 0.03f), 0.15f);
            Assert.Greater(AudioLab.Level(bell, 5232.5f, 0f, 0.03f), 0.15f);
            Assert.Less(AudioLab.Level(bell, 9418.5f, 0.07f, 0.1f), 0.01f);
            Assert.Greater(AudioLab.Centroid(bell, 0f, 0.03f), 1.5f * AudioLab.Centroid(bell, 0.07f, 0.1f));
        }

        [Test]
        public void ModalLandings_HaveTheirModesAndDecays()
        {
            foreach (SoundId id in new[] { SoundId.LandPlastic, SoundId.LandWood, SoundId.LandRubber, SoundId.LandMetal, SoundId.LandGlass })
            {
                Assert.IsTrue(SoundRecipes.Modes(id, out float baseHz, out float[] ratios, out float[] decaysMs), id.ToString());
                float[] dry = AudioLab.Dry(id);
                // Rubber starts two semitones high and falls for 60 ms; measure it once it has landed.
                float settle = id == SoundId.LandRubber ? 0.07f : 0.002f;
                for (int mode = 0; mode < ratios.Length; mode++)
                {
                    float hz = baseHz * ratios[mode], tau = decaysMs[mode] * 0.001f;
                    // A window of one time constant, long enough to tell the mode from its neighbours.
                    float window = Mathf.Max(tau, 12f / (baseHz * 0.4f));
                    float level = AudioLab.Level(dry, hz, settle, settle + window);
                    Assert.Greater(level, 0.02f, id + " mode " + mode + " at " + hz + " Hz is there");
                    Assert.AreEqual(hz, AudioLab.Dominant(dry, settle, settle + window, hz * 0.85f, hz * 1.15f), hz * 0.02f + 3f, id + " mode " + mode);
                    float measured = SoundStats.DecayTime(dry, R, hz, settle, settle + tau, window);
                    Assert.AreEqual(tau, measured, tau * 0.12f, id + " mode " + mode + " decays in " + decaysMs[mode] + " ms");
                }
                // The base mode is the loudest thing in it.
                Assert.AreEqual(baseHz, AudioLab.Dominant(dry, settle, settle + decaysMs[0] * 0.002f), baseHz * 0.03f + 3f, id + " rings at its base");
                Assert.AreEqual(-9f, SoundStats.PeakDb(AudioLab.Clip(id)), 0.02f);
            }

            // The table's numbers.
            SoundRecipes.Modes(SoundId.LandPlastic, out float plastic, out float[] plasticRatios, out float[] plasticDecays);
            Assert.AreEqual(420f, plastic);
            CollectionAssert.AreEqual(new[] { 1f, 2.3f, 3.9f }, plasticRatios);
            CollectionAssert.AreEqual(new[] { 70f, 45f, 30f }, plasticDecays);
            SoundRecipes.Modes(SoundId.LandMetal, out float metal, out float[] metalRatios, out float[] metalDecays);
            Assert.AreEqual(900f, metal);
            CollectionAssert.AreEqual(new[] { 1f, 2.76f, 5.40f, 8.93f }, metalRatios);
            CollectionAssert.AreEqual(new[] { 900f, 600f, 400f, 250f }, metalDecays);
            SoundRecipes.Modes(SoundId.LandWood, out float wood, out _, out _);
            SoundRecipes.Modes(SoundId.LandRubber, out float rubber, out _, out _);
            SoundRecipes.Modes(SoundId.LandGlass, out float glass, out _, out _);
            Assert.AreEqual(300f, wood);
            Assert.AreEqual(140f, rubber);
            Assert.AreEqual(1600f, glass);
            Assert.IsFalse(SoundRecipes.Modes(SoundId.LandFelt, out _, out _, out _));

            // Rubber's fall: two semitones above its base at the strike.
            float[] rubberDry = AudioLab.Dry(SoundId.LandRubber);
            Assert.Greater(AudioLab.Dominant(rubberDry, 0f, 0.03f, 100f, 190f), 143f);

            // Metal rings longest, then glass; plastic is a tock.
            Assert.Greater(AudioLab.Bank.Seconds(SoundId.LandMetal), AudioLab.Bank.Seconds(SoundId.LandGlass));
            Assert.Greater(AudioLab.Bank.Seconds(SoundId.LandGlass), 2f * AudioLab.Bank.Seconds(SoundId.LandWood));
            Assert.Less(AudioLab.Bank.Seconds(SoundId.LandPlastic), 1f);
        }

        [Test]
        public void NoiseLandings_AndThePlayersOwn_MatchTheirRecipes()
        {
            // Felt, sponge: noise, low-pass 500 Hz, decay 60 ms, -10 dB.
            float[] felt = AudioLab.Dry(SoundId.LandFelt);
            Assert.Less(SoundStats.Centroid(felt, R, 0, 8192), 500f, "dull");
            Assert.Greater(SoundStats.Band(felt, R, 50f, 400f), 10.0 * SoundStats.Band(felt, R, 2000f, 2500f));
            float feltDecay = SoundStats.Rms(felt, AudioLab.N(0.12f), AudioLab.N(0.06f)) / SoundStats.Rms(felt, AudioLab.N(0.06f), AudioLab.N(0.06f));
            Assert.AreEqual(Mathf.Exp(-1f), feltDecay, 0.15f, "decay 60 ms");

            // Cardboard: a 180 Hz thump under noise band-passed at 700 Hz (Q 1.5), decay 50 ms.
            float[] cardboard = AudioLab.Dry(SoundId.LandCardboard);
            Assert.AreEqual(180f, AudioLab.Dominant(cardboard, 0f, 0.15f), 12f, "the thump");
            Assert.AreEqual(0.05f, SoundStats.DecayTime(cardboard, R, 180f, 0.01f, 0.06f, 0.05f), 0.008f);
            Assert.Greater(SoundStats.Band(cardboard, R, 600f, 820f), 3.5 * SoundStats.Band(cardboard, R, 2800f, 3400f), "the rustle sits around 700 Hz");
            Assert.Greater(SoundStats.Band(cardboard, R, 600f, 820f), 20.0 * SoundStats.Band(cardboard, R, 12000f, 16000f));

            // Feather: noise, high-pass 5 kHz, decay 30 ms, -26 dB.
            float[] feather = AudioLab.Dry(SoundId.LandFeather);
            Assert.Greater(SoundStats.Centroid(feather, R, 0, 4096), 5000f, "all air");
            Assert.Greater(SoundStats.Band(feather, R, 6000f, 16000f), 10.0 * SoundStats.Band(feather, R, 200f, 1200f));
            float featherDecay = SoundStats.Rms(feather, AudioLab.N(0.06f), AudioLab.N(0.03f)) / SoundStats.Rms(feather, AudioLab.N(0.03f), AudioLab.N(0.03f));
            Assert.AreEqual(Mathf.Exp(-1f), featherDecay, 0.15f, "decay 30 ms");

            // Brightness sorts the materials the way an ear would.
            float Brightness(SoundId id) => SoundStats.Centroid(AudioLab.Dry(id), R, 0, 4096);
            Assert.Less(Brightness(SoundId.LandFelt), Brightness(SoundId.LandPlastic));
            Assert.Less(Brightness(SoundId.LandRubber), Brightness(SoundId.LandWood));
            Assert.Less(Brightness(SoundId.LandWood), Brightness(SoundId.LandGlass));
            Assert.Less(Brightness(SoundId.LandPlastic), Brightness(SoundId.LandGlass));
            Assert.Less(Brightness(SoundId.LandGlass), Brightness(SoundId.LandFeather));

            // Player land: two 5 ms clicks band-passed at 1.2 kHz (30 ms apart), plus a 90 Hz thump of 60 ms.
            float[] land = AudioLab.Dry(SoundId.PlayerLand);
            Assert.That(SoundStats.DominantHz(land, R), Is.InRange(60f, 125f), "the thump");
            Assert.Greater(SoundStats.Band(land, R, 1000f, 1400f), 3.0 * SoundStats.Band(land, R, 5000f, 7000f), "the clicks sit at 1.2 kHz");
            double firstClick = SoundStats.Band(land, R, 1000f, 1400f, 0, AudioLab.N(0.012f));
            double between = SoundStats.Band(land, R, 1000f, 1400f, AudioLab.N(0.018f), AudioLab.N(0.012f));
            double secondClick = SoundStats.Band(land, R, 1000f, 1400f, AudioLab.N(0.03f), AudioLab.N(0.012f));
            Assert.Greater(firstClick, 1.5 * between, "two clicks, not one");
            Assert.Greater(secondClick, 1.5 * between);
        }

        [Test]
        public void Buttons_Exits_AndTheKit_MatchTheirRecipes()
        {
            const float E5 = 659.26f, A5 = 880f, G5 = 783.99f, C6 = 1046.5f, G6 = 1567.98f;
            // Press: E5 then A5, 70 ms apart, after a 2 ms square click. Release: A5 then E5.
            float[] press = AudioLab.Dry(SoundId.ButtonPress), release = AudioLab.Dry(SoundId.ButtonRelease);
            Assert.AreEqual(E5, AudioLab.Dominant(press, 0.044f, 0.074f, 400f, 1200f), 25f, "the first note alone");
            Assert.AreEqual(A5, AudioLab.Dominant(press, 0.13f, 0.25f, 400f, 1200f), 10f, "then the second");
            Assert.AreEqual(A5, AudioLab.Dominant(release, 0.044f, 0.074f, 400f, 1200f), 25f);
            Assert.AreEqual(E5, AudioLab.Dominant(release, 0.13f, 0.25f, 400f, 1200f), 10f);
            // Marimba: decay 60 ms.
            Assert.AreEqual(0.06f, SoundStats.DecayTime(press, R, A5, 0.13f, 0.19f, 0.06f), 0.006f);
            // The click: a square wave has harmonics a sine would not; they are there only at the very start.
            Assert.Greater(AudioLab.Level(press, 1000f, 0f, 0.002f), 0.2f);
            Assert.AreEqual(0.4f, SoundStats.Peak(Slice(press, 0, AudioLab.N(0.0015f))), 0.1f);

            // Exit: sol-do-sol rising to open, do-sol falling to lock.
            float[] open = AudioLab.Dry(SoundId.ExitOpen), close = AudioLab.Dry(SoundId.ExitClose);
            Assert.AreEqual(G5, AudioLab.Dominant(open, 0.045f, 0.09f, 400f, 2500f), 25f);
            Assert.AreEqual(G6, AudioLab.Dominant(open, 0.25f, 0.5f, 400f, 2500f), 15f);
            Assert.AreEqual(C6, AudioLab.Dominant(close, 0.045f, 0.09f, 400f, 2500f), 25f);
            Assert.AreEqual(G5, AudioLab.Dominant(close, 0.16f, 0.4f, 400f, 2500f), 15f);

            // Toy kit: woodblock (sine 1200 Hz, 20 ms), shaker (noise high-passed 5 kHz, 40 ms), soft kick (110 -> 50 Hz, 90 ms).
            float[] wood = AudioLab.Dry(SoundId.KitWood), shaker = AudioLab.Dry(SoundId.KitShaker), kick = AudioLab.Dry(SoundId.KitKick);
            Assert.AreEqual(AudioLab.N(0.02f), wood.Length);
            Assert.AreEqual(24, SoundStats.Cycles(wood), 1, "1200 Hz for 20 ms");
            Assert.AreEqual(1200f, SoundStats.DominantHz(wood, R), 40f);
            Assert.AreEqual(AudioLab.N(0.04f), shaker.Length);
            Assert.Greater(SoundStats.Centroid(shaker, R), 5000f);
            Assert.AreEqual(AudioLab.N(0.09f), kick.Length);
            Assert.That(SoundStats.DominantHz(kick, R), Is.InRange(45f, 115f));
            Assert.Less(SoundStats.Centroid(kick, R), 200f);
        }

        static float[] Slice(float[] source, int start, int count)
        {
            var slice = new float[count];
            Array.Copy(source, start, slice, 0, count);
            return slice;
        }

        [Test]
        public void TheMusicBox_IsNineBells_AMinorThirdApart()
        {
            Assert.AreEqual(9, Sounds.BoxCount);
            for (int i = 0; i < Sounds.BoxCount; i++)
            {
                SoundId id = SoundId.Box0 + i;
                float hz = Synth.NoteHz(60 + 3 * i);
                float[] dry = AudioLab.Dry(id);
                Assert.AreEqual(AudioLab.N(1.2f), dry.Length, id + " is 1.2 s");
                Assert.AreEqual(hz, AudioLab.Dominant(dry, 0.15f, 0.6f), hz * 0.004f, id + " rings at its note");
                Assert.AreEqual(0.3f, SoundStats.DecayTime(dry, R, hz, 0.15f, 0.45f, 0.15f), 0.02f, id + " decays in 300 ms");
                // FM ratio 1:4, index 2 -> 0 over 80 ms: sidebands at 3x and 5x the note while it is struck.
                Assert.Greater(AudioLab.Level(dry, 5f * hz, 0f, 0.04f), 0.15f, id + " is bright at the strike");
                Assert.Less(AudioLab.Level(dry, 5f * hz, 0.1f, 0.14f), 0.01f, id + " and pure after 80 ms");
            }
            Assert.AreEqual(261.63f, Synth.NoteHz(Sounds.BoxLowNote), 0.01f, "C4");
            Assert.AreEqual(1046.5f, Synth.NoteHz(Sounds.BoxLowNote + Sounds.BoxInterval * 8), 0.01f, "C6");

            // Any note from C4 to C6 is at most a semitone from a clip.
            for (int note = 59; note <= 85; note++)
            {
                SoundId box = Sounds.Box(note, out float pitch);
                Assert.IsTrue(Sounds.IsBox(box));
                Assert.That(pitch, Is.InRange(Mathf.Pow(2f, -1.001f / 12f), Mathf.Pow(2f, 1.001f / 12f)), "note " + note);
                Assert.AreEqual(Synth.NoteHz(note), Synth.NoteHz(60 + 3 * (box - SoundId.Box0)) * pitch, 0.05f, "note " + note);
            }
        }

        [Test]
        public void PadAndBass_AreRenderedInTheKeyAndTempo()
        {
            MusicKey key = MusicKey.CMajor;
            int rate = Synth.LowRate;
            float bar = 240f / AudioLab.Bpm;
            Assert.AreEqual(2.857f, bar, 0.001f);

            // Bass: a sine with a 10 ms attack; one bar of each chord's root: C, A, F, G.
            int[] bassNotes = { 48, 45, 41, 43 };
            for (int chord = 0; chord < 4; chord++)
            {
                float[] bass = SoundRecipes.Dry(SoundId.Bass0 + chord, key, AudioLab.Bpm);
                Assert.AreEqual(Synth.Samples(bar, rate), bass.Length, "one bar");
                Assert.AreEqual(bassNotes[chord], SoundRecipes.BassNote(key, chord));
                float hz = Synth.NoteHz(bassNotes[chord]);
                Assert.AreEqual(hz, AudioLab.Dominant(bass, 0.1f, 1.1f, 20f, 2000f, rate), 1f, "bass " + chord);
                Assert.Less(SoundStats.Level(bass, rate, 2f * hz, Synth.Samples(0.1f, rate), Synth.Samples(1f, rate)), 0.01f, "a sine: no second harmonic");
                Assert.Less(SoundStats.Rms(bass, 0, Synth.Samples(0.003f, rate)), 0.5f * SoundStats.Rms(bass, Synth.Samples(0.012f, rate), Synth.Samples(0.02f, rate)), "10 ms attack");
            }

            // Pad: three saws detuned +-7 cents through a 600 Hz low-pass, slow attack 400 ms; two bars of an add9 chord.
            for (int chord = 0; chord < 4; chord++)
            {
                float[] pad = SoundRecipes.Dry(SoundId.Pad0 + chord, key, AudioLab.Bpm);
                Assert.AreEqual(Synth.Samples(2f * bar + SoundRecipes.PadRelease, rate), pad.Length, "two bars and the release");
                int root = SoundRecipes.PadRoot(key, chord);
                Assert.That(root, Is.InRange(48, 59));
                Chord c = key.Chord(chord);
                int from = Synth.Samples(1f, rate), count = Synth.Samples(3f, rate);
                for (int note = 0; note < SoundRecipes.PadNotes; note++)
                {
                    float hz = Synth.NoteHz(SoundRecipes.PadNoteOf(root, c, note));
                    // Three saws 7 cents apart beat against each other; over three seconds the note is plainly there.
                    float level = Mathf.Max(SoundStats.Level(pad, rate, hz, from, count), Mathf.Max(SoundStats.Level(pad, rate, hz * Synth.Ratio(0.07f), from, count), SoundStats.Level(pad, rate, hz * Synth.Ratio(-0.07f), from, count)));
                    Assert.Greater(level, 0.01f, "pad " + chord + " note " + note);
                }
                // The other third is not in the chord.
                float wrong = Synth.NoteHz(root + 12 + (c.Minor ? 4 : 3));
                float right = Synth.NoteHz(root + 12 + c.Third);
                Assert.Greater(SoundStats.Level(pad, rate, right, from, count), 5f * SoundStats.Level(pad, rate, wrong, from, count), "pad " + chord + " is " + (c.Minor ? "minor" : "major"));
                // Slow attack, and dark: two octaves above the cutoff there is next to nothing.
                Assert.Less(SoundStats.Rms(pad, 0, Synth.Samples(0.05f, rate)), 0.2f * SoundStats.Rms(pad, Synth.Samples(0.45f, rate), Synth.Samples(0.2f, rate)), "400 ms attack");
                Assert.Greater(SoundStats.Band(pad, rate, 100f, 600f, from, 16384), 25.0 * SoundStats.Band(pad, rate, 2400f, 4000f, from, 16384), "600 Hz low-pass");
            }
            Assert.IsTrue(key.Chord(1).Minor, "vi is minor");
            Assert.IsFalse(key.Chord(0).Minor);

            // Detune: saws 7 cents either side of the note.
            float[] one = SoundRecipes.Dry(SoundId.Pad0, key, AudioLab.Bpm);
            float c3 = Synth.NoteHz(48);
            Assert.AreEqual(1.004f, Synth.Ratio(0.07f), 0.0002f, "7 cents");
            Assert.Greater(SoundStats.Level(one, rate, c3 * 2f, Synth.Samples(1f, rate), Synth.Samples(3f, rate)), 0.003f, "a saw has even harmonics");

            // Another tempo is another length; another key another pitch.
            Assert.AreEqual(Synth.Samples(240f / 76f, rate), SoundRecipes.Dry(SoundId.Bass0, key, 76).Length);
            var aMinor = new MusicKey(9, MusicScale.MinorPentatonic);
            Assert.AreEqual(45, SoundRecipes.BassNote(aMinor, 0), "A2");
            Assert.AreEqual(110f, AudioLab.Dominant(SoundRecipes.Dry(SoundId.Bass0, aMinor, 76), 0.1f, 1.1f, 20f, 2000f, rate), 1f);
        }

        [Test]
        public void LevelComplete_IsShutterArpeggioSwellAndStamp_InTheLevelsKey()
        {
            float sixteenth = 15f / AudioLab.Bpm;
            float[] stinger = SoundRecipes.Dry(SoundId.LevelComplete, MusicKey.CMajor, AudioLab.Bpm);
            Assert.GreaterOrEqual(stinger.Length, AudioLab.N(1.2f), "the swell alone is 1.2 s");

            // Shutter: two 12 ms noise bursts high-passed at 3 kHz, 40 ms apart.
            Assert.Greater(AudioLab.Centroid(stinger, 0f, 0.012f), 3000f);
            Assert.Greater(AudioLab.Centroid(stinger, 0.04f, 0.052f), 3000f);
            float burst = SoundStats.Rms(stinger, 0, AudioLab.N(0.012f)), gap = SoundStats.Rms(stinger, AudioLab.N(0.018f), AudioLab.N(0.018f));
            Assert.Greater(burst, 3f * gap, "two bursts with a gap between them");
            Assert.Greater(SoundStats.Rms(stinger, AudioLab.N(0.04f), AudioLab.N(0.012f)), 3f * gap);

            // Arpeggio: marimba on scale degrees 1-3-5-6-8 at sixteenth notes. In C: C5 E5 G5 A5 C6.
            int[] notes = { 72, 76, 79, 81, 84 };
            for (int i = 0; i < notes.Length; i++)
            {
                float at = SoundRecipes.ArpeggioAt + i * sixteenth;
                float hz = Synth.NoteHz(notes[i]);
                Assert.AreEqual(hz, AudioLab.Dominant(stinger, at + 0.045f, at + sixteenth, 480f, 1150f), hz * 0.03f, "arpeggio note " + i);
            }

            // Stamp at 400 ms: a 90 Hz thump.
            Assert.AreEqual(0.4f, SoundRecipes.StampAt);
            Assert.Greater(AudioLab.Level(stinger, 90f, 0.4f, 0.5f), 4f * AudioLab.Level(stinger, 90f, 0.28f, 0.38f));

            // Swell: an add9 pad chord (C E G D) under it all, gone by 1.2 s.
            Assert.Greater(AudioLab.Level(stinger, Synth.NoteHz(60), 0.3f, 0.9f), 0.01f);
            Assert.Greater(AudioLab.Level(stinger, Synth.NoteHz(67), 0.3f, 0.9f), 0.01f);
            Assert.Greater(AudioLab.Level(stinger, Synth.NoteHz(74), 0.3f, 0.9f), 0.005f);

            // In A minor at 76 BPM the tonic is A4 and the degrees are 1 b3 5 b6 8: A4 C5 E5 F5 A5.
            var aMinor = new MusicKey(9, MusicScale.MinorPentatonic);
            float slow = 15f / 76f;
            float[] night = SoundRecipes.Dry(SoundId.LevelComplete, aMinor, 76);
            int[] nightNotes = { 69, 72, 76, 77, 81 };
            for (int i = 0; i < nightNotes.Length; i++)
            {
                float at = SoundRecipes.ArpeggioAt + i * slow;
                float hz = Synth.NoteHz(nightNotes[i]);
                Assert.AreEqual(hz, AudioLab.Dominant(night, at + 0.045f, at + slow, 400f, 1000f), hz * 0.03f, "night arpeggio note " + i);
            }
            CollectionAssert.AreEqual(new[] { 0, 3, 7, 8, 12 }, aMinor.Arpeggio);
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 9, 12 }, MusicKey.CMajor.Arpeggio);
            CollectionAssert.AreEqual(new[] { 0, 3, 7, 9, 12 }, new MusicKey(2, MusicScale.DorianPentatonic).Arpeggio);
        }

        [Test]
        public void TheBank_GeneratesInOrder_InSlices_WithinItsBudgets()
        {
            var bank = new SoundBank { KeepSamples = true };
            Assert.IsFalse(bank.Complete);
            Assert.IsFalse(bank.Ready(SoundId.UiHover));
            Assert.IsNull(bank.Samples(SoundId.Grab));
            Assert.AreEqual(0f, bank.Seconds(SoundId.Grab));
            Assert.AreEqual(0, bank.SampleBytes);

            // One stage at a time: the order in which sounds appear is the art bible's - UI, grab, hold,
            // release, land, button, level complete, music bank.
            bank.SetKey(MusicKey.CMajor, AudioLab.Bpm);
            var order = new List<SoundId>();
            var seen = new HashSet<SoundId>();
            int guard = 0;
            while (!bank.Complete && guard++ < 100000)
            {
                bank.Pump(0.0);
                foreach (SoundId id in AudioLab.All())
                    if (bank.Ready(id) && seen.Add(id)) order.Add(id);
            }
            Assert.IsTrue(bank.Complete);
            Assert.AreEqual(Sounds.Count - 1, order.Count, "every sound was generated");
            for (int i = 1; i < order.Count; i++) Assert.Less(order[i - 1], order[i], "generated in the order of the enum");
            Assert.AreEqual(SoundId.UiHover, order[0]);
            Assert.Less(order.IndexOf(SoundId.Grab), order.IndexOf(SoundId.Hold));
            Assert.Less(order.IndexOf(SoundId.Hold), order.IndexOf(SoundId.ReleaseThock));
            Assert.Less(order.IndexOf(SoundId.ReleaseBell), order.IndexOf(SoundId.LandPlastic));
            Assert.Less(order.IndexOf(SoundId.PlayerLand), order.IndexOf(SoundId.ButtonPress));
            Assert.Less(order.IndexOf(SoundId.ExitClose), order.IndexOf(SoundId.LevelComplete));
            Assert.Less(order.IndexOf(SoundId.LevelComplete), order.IndexOf(SoundId.Box0));
            Assert.Less(order.IndexOf(SoundId.KitKick), order.IndexOf(SoundId.Pad0));
            Assert.AreEqual(guard, bank.StageCount, "a pump with no budget does exactly one stage");

            // It made what the shared bank made.
            foreach (SoundId id in AudioLab.All()) Assert.IsTrue(AudioLab.Same(AudioLab.Clip(id), bank.Samples(id)), id.ToString());

            // Sliced at 4 ms a frame, as behind the title screen: measure it.
            var sliced = new SoundBank { KeepSamples = true };
            sliced.SetKey(MusicKey.CMajor, AudioLab.Bpm);
            var watch = new Stopwatch();
            int frames = 0;
            double longestFrame = 0;
            while (!sliced.Complete && frames < 100000)
            {
                watch.Restart();
                sliced.Pump(AudioPresenter.GenerationBudgetMs);
                longestFrame = Math.Max(longestFrame, watch.Elapsed.TotalMilliseconds);
                frames++;
            }
            double megabytes = sliced.SampleBytes / (1024.0 * 1024.0);
            string summary = "[Toybox] audio generation: " + sliced.SynthMilliseconds.ToString("0.0") + " ms of synthesis over " + frames + " frames of " +
                             AudioPresenter.GenerationBudgetMs + " ms (longest frame " + longestFrame.ToString("0.00") + " ms, longest stage " +
                             sliced.LongestStageMilliseconds.ToString("0.00") + " ms, " + sliced.StageCount + " stages of which " + sliced.LongStages + " over 4 ms, " + sliced.LongStagesWithCollection + " of those with a GC inside), " +
                             megabytes.ToString("0.00") + " MB of samples in " + sliced.Generated + " clips";
            Debug.Log(summary);
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxAudio");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.txt"), summary + "\n\n" + sliced.Report() + "\n");

            // The budgets of ART_BIBLE 11.1 / 12.2: under 12 MB of samples, under 400 ms of synthesis. The
            // time is measured on a shared machine, so the assertion leaves it room; the report has the number.
            // Memory is exact. Time is measured on a shared machine with a garbage collector that may run in
            // any stage, so it is reported (Temp/ToyboxAudio/report.txt) and only held against a bound that
            // nothing but a real regression would cross.
            Assert.Less(megabytes, 12.0, "sample memory");
            Assert.Less(sliced.SynthMilliseconds, 4000.0, "synthesis time (budget 400 ms; see Temp/ToyboxAudio/report.txt for the measurement)");
            Assert.Greater(frames, 5, "it really was spread over frames");
            Assert.Greater(sliced.StageCount, 150, "in small stages: no clip is synthesised in one go");
            Assert.AreEqual(Sounds.Count - 1, sliced.Generated);
            StringAssert.Contains("LandMetal", sliced.Report());
        }

        [Test]
        public void TheBank_RendersThePerKeyClipsAgain_WhenTheKeyChanges()
        {
            var bank = new SoundBank { KeepSamples = true };
            bank.GenerateAll();
            Assert.IsTrue(bank.StaticReady);
            Assert.IsFalse(bank.KeyReady, "no key yet: no stinger, no pad, no bass");
            Assert.IsFalse(bank.Ready(SoundId.Pad0));
            Assert.IsFalse(bank.Ready(SoundId.LevelComplete));
            float[] grab = bank.Samples(SoundId.Grab);
            long staticBytes = bank.SampleBytes;

            Assert.IsTrue(bank.SetKey(MusicKey.CMajor, 84));
            Assert.IsFalse(bank.SetKey(MusicKey.CMajor, 84), "the same key again changes nothing");
            Assert.IsFalse(bank.Complete);
            Assert.AreEqual(9, bank.Pending, "stinger, four bass roots, four pad chords");
            bank.GenerateAll();
            Assert.IsTrue(bank.KeyReady);
            float[] pad = bank.Samples(SoundId.Pad0);
            long withKey = bank.SampleBytes;
            Assert.Greater(withKey, staticBytes);

            var night = new MusicKey(9, MusicScale.MinorPentatonic);
            Assert.IsTrue(bank.SetKey(night, 76));
            Assert.IsFalse(bank.KeyReady, "the old key's clips are dropped at once");
            Assert.IsFalse(bank.Ready(SoundId.Pad0));
            Assert.IsTrue(bank.Ready(SoundId.Grab));
            // Interrupted half way and changed again: no clip of a stale key survives.
            bank.Pump(0.0);
            bank.Pump(0.0);
            bank.SetKey(new MusicKey(7, MusicScale.LydianPentatonic), 92);
            bank.SetKey(night, 76);
            bank.GenerateAll();
            Assert.IsTrue(bank.KeyReady);
            Assert.AreEqual(night, bank.Key);
            Assert.AreEqual(76, bank.Bpm);
            Assert.AreSame(grab, bank.Samples(SoundId.Grab), "the rest is made once");
            Assert.IsTrue(AudioLab.Same(SoundRecipes.Render(SoundId.Pad0, night, 76), bank.Samples(SoundId.Pad0)));
            Assert.IsTrue(AudioLab.Same(SoundRecipes.Render(SoundId.LevelComplete, night, 76), bank.Samples(SoundId.LevelComplete)));
            Assert.IsFalse(AudioLab.Same(pad, bank.Samples(SoundId.Pad0)));
            // Slower tempo, longer bars; the memory is replaced, not added to.
            Assert.Greater(bank.SampleBytes, withKey);
            Assert.Less(bank.SampleBytes, withKey * 1.2);
            Assert.Less(bank.SampleBytes / (1024.0 * 1024.0), 12.0, "under the budget at the slowest tempo too");

            // Every preset's key and tempo: rendered, sound, at its level, and inside the memory budget.
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                bank.SetKey(MusicKey.Of(preset), preset.Bpm);
                bank.GenerateAll();
                Assert.IsTrue(bank.KeyReady, preset.Key);
                Assert.Less(bank.SampleBytes / (1024.0 * 1024.0), 12.0, preset.Key + " sample memory");
                foreach (SoundId id in AudioLab.All())
                {
                    SoundSpec spec = Sounds.Spec(id);
                    if (!spec.PerKey) continue;
                    float[] clip = bank.Samples(id);
                    Assert.IsTrue(SoundStats.Finite(clip), preset.Key + " " + id);
                    Assert.AreEqual(spec.PeakDb, SoundStats.PeakDb(clip), 0.02f, preset.Key + " " + id);
                }
                // The bass is on the chord's root, in this key.
                for (int chord = 0; chord < MusicKey.ChordCount; chord++)
                {
                    float hz = Synth.NoteHz(SoundRecipes.BassNote(MusicKey.Of(preset), chord));
                    float[] bass = bank.Samples(SoundId.Bass0 + chord);
                    Assert.AreEqual(hz, SoundStats.DominantHz(bass, Synth.LowRate, 2205, 22050), 1.5f, preset.Key + " bass " + chord);
                    Assert.AreEqual(0, (SoundRecipes.BassNote(MusicKey.Of(preset), chord) - MusicKey.Of(preset).ChordRoot(chord)) % 12);
                }
            }

            bank.Dispose();
            Assert.IsFalse(bank.Ready(SoundId.Grab));
            Assert.AreEqual(0, bank.SampleBytes);
        }

        [Test]
        public void TheBank_WrapsItsSamplesInAudioClips()
        {
            var bank = new SoundBank { MakeClips = true, KeepSamples = true };
            try
            {
                // The first few sounds are enough to see the wrapping.
                int guard = 0;
                while (!bank.Ready(SoundId.Hold) && guard++ < 10000) bank.Pump(0.0);
                AudioClip grab = bank.Clip(SoundId.Grab);
                Assume.That(grab != null, "this editor has no audio (AudioClip.Create failed); the bank stays silent");
                float[] samples = bank.Samples(SoundId.Grab);
                Assert.AreEqual(samples.Length, grab.samples);
                Assert.AreEqual(1, grab.channels);
                Assert.AreEqual(44100, grab.frequency);
                Assert.AreEqual("Grab", grab.name);
                var read = new float[samples.Length];
                Assert.IsTrue(grab.GetData(read, 0));
                for (int i = 0; i < read.Length; i += 7) Assert.AreEqual(samples[i], read[i], 1e-4f, "sample " + i);
                AudioClip hold = bank.Clip(SoundId.Hold);
                Assert.AreEqual(88200, hold.samples);
                Assert.AreEqual(2f, hold.length, 1e-4f);
                Assert.IsNull(bank.Clip(SoundId.Box0), "not generated yet");

                bank.Dispose();
                Assert.IsTrue(grab == null, "disposing the bank destroys its clips");
            }
            finally
            {
                bank.Dispose();
            }
        }
    }

    /// <summary>
    /// The musical side: the size-to-pitch law, keys and chords per preset, the composition (a pure function
    /// of the level id) and the scheduler's note times.
    /// </summary>
    public class AudioMusicTests
    {
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 }, Lydian = { 0, 2, 4, 6, 7, 9, 11 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 }, Minor = { 0, 2, 3, 5, 7, 8, 10 };

        static int[] Mode(MusicScale scale)
        {
            switch (scale)
            {
                case MusicScale.LydianPentatonic: return Lydian;
                case MusicScale.DorianPentatonic: return Dorian;
                case MusicScale.MinorPentatonic: return Minor;
                default: return Major;
            }
        }

        [Test]
        public void SizeToPitch_BigIsLow_FourTimesTheSizeIsAnOctaveDown()
        {
            Assert.AreEqual(0f, PitchLaw.Semis(1f), 1e-5f, "a toy of one unit plays the clip as written");
            Assert.AreEqual(-12f, PitchLaw.Semis(4f), 1e-4f, "four times the size is an octave down");
            Assert.AreEqual(12f, PitchLaw.Semis(0.25f), 1e-4f);
            Assert.AreEqual(-6f, PitchLaw.Semis(2f), 1e-4f);
            Assert.AreEqual(-24f, PitchLaw.Semis(16f), 1e-4f);
            Assert.AreEqual(-24f, PitchLaw.Semis(1000f), "clamped");
            Assert.AreEqual(24f, PitchLaw.Semis(1f / 16f), 1e-4f);
            Assert.AreEqual(24f, PitchLaw.Semis(0.001f), "clamped");
            Assert.AreEqual(24f, PitchLaw.Semis(0f));
            Assert.AreEqual(24f, PitchLaw.Semis(float.NaN), "nonsense is the smallest toy, not an exception");
            Assert.AreEqual(0.5f, PitchLaw.Pitch(-12f), 1e-5f);
            Assert.AreEqual(2f, PitchLaw.Pitch(12f), 1e-5f);
            Assert.AreEqual(0.25f, PitchLaw.Pitch(-24f), 1e-5f);
            Assert.AreEqual(4f, PitchLaw.Pitch(24f), 1e-5f, "two octaves up");
            Assert.AreEqual(4f, PitchLaw.MaxPitch);

            // Monotonic: a larger toy never sounds higher - unsnapped strictly, snapped never the wrong way.
            float previous = float.MaxValue;
            for (float size = 0.01f; size < 200f; size *= 1.013f)
            {
                float semis = PitchLaw.Semis(size);
                Assert.LessOrEqual(semis, previous, "size " + size);
                if (size > 1f / 16f * 1.02f && size < 16f / 1.02f) Assert.Less(semis, previous, "strictly inside the clamp, at size " + size);
                previous = semis;
            }

            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                MusicKey key = MusicKey.Of(preset);
                foreach (int clipNote in new[] { PitchLaw.GrabNote, PitchLaw.HoldNote, PitchLaw.ThockNote })
                {
                    int last = int.MaxValue;
                    var heard = new HashSet<int>();
                    for (float size = 0.01f; size < 200f; size *= 1.013f)
                    {
                        int semis = PitchLaw.SnappedSemis(size, clipNote, key);
                        Assert.LessOrEqual(semis, last, preset.Key + " size " + size);
                        last = semis;
                        heard.Add(semis);
                        Assert.IsTrue(key.Contains(clipNote + semis), preset.Key + ": what sounds is a note of the scale");
                        Assert.LessOrEqual(PitchLaw.Pitch(semis), PitchLaw.MaxPitch);
                        Assert.AreEqual(Mathf.Pow(2f, semis / 12f), PitchLaw.Pitch(semis), 1e-4f, "within what the source can do, no clamping after the snap");
                        // Snapping moves a note by half the scale's widest gap at most (lydian's is five semitones)
                        // - except at the two ends, where the last scale note inside the law's range is taken.
                        if (Mathf.Abs(PitchLaw.Semis(size)) < 21f) Assert.LessOrEqual(Mathf.Abs(semis - PitchLaw.Semis(size)), 2.51f, preset.Key + " size " + size);
                        Assert.That(semis, Is.InRange(-24, 24), "never outside the law's two octaves");
                    }
                    Assert.Greater(heard.Count, 12, "sizes spread over many notes of the scale");
                }

                // While holding, the tone is on the pentatonic of the chord that sounds; the thock on its root or fifth.
                for (int chord = 0; chord < MusicKey.ChordCount; chord++)
                {
                    Chord c = key.Chord(chord);
                    int lastHold = int.MaxValue, lastThock = int.MaxValue;
                    for (float size = 0.05f; size < 40f; size *= 1.05f)
                    {
                        int hold = PitchLaw.HoldSemis(size, key, chord);
                        Assert.IsTrue(Scales.Contains(PitchLaw.HoldNote + hold, key.Root + c.Root, c.Pentatonic));
                        Assert.LessOrEqual(hold, lastHold);
                        lastHold = hold;
                        int thock = PitchLaw.ThockSemis(size, key, chord);
                        int degree = (((PitchLaw.ThockNote + thock - key.Root - c.Root) % 12) + 12) % 12;
                        Assert.IsTrue(degree == 0 || degree == 7, "the thock is tuned to the chord's root (or its fifth)");
                        Assert.LessOrEqual(thock, lastThock);
                        lastThock = thock;
                    }
                }
            }

            // A one-unit block (bounding diameter 1.73) in C major: 4.75 semitones down, snapped to G4.
            Assert.AreEqual(-5, PitchLaw.SnappedSemis(Mathf.Sqrt(3f), PitchLaw.GrabNote, MusicKey.CMajor));
            Assert.AreEqual(0, PitchLaw.SnappedSemis(1f, PitchLaw.GrabNote, MusicKey.CMajor));
            Assert.AreEqual(-12, PitchLaw.SnappedSemis(4f, PitchLaw.GrabNote, MusicKey.CMajor));
        }

        [Test]
        public void Landing_PitchFallsWithSize_AndVolumeRisesWithSpeedAndMass()
        {
            Assert.AreEqual(1f, PitchLaw.LandPitch(1f), 1e-5f);
            Assert.AreEqual(Mathf.Pow(4f, -0.7f), PitchLaw.LandPitch(4f), 1e-5f);
            Assert.AreEqual(0.379f, PitchLaw.LandPitch(4f), 0.001f);
            Assert.AreEqual(0.25f, PitchLaw.LandPitch(100f), "clamped below");
            Assert.AreEqual(4f, PitchLaw.LandPitch(0.01f), "clamped above");
            float previous = float.MaxValue;
            for (float size = 0.02f; size < 100f; size *= 1.1f)
            {
                Assert.LessOrEqual(PitchLaw.LandPitch(size), previous);
                previous = PitchLaw.LandPitch(size);
            }

            // volume = clamp(speed / 12, 0, 1) at mass 1.
            Assert.AreEqual(0.5f, PitchLaw.LandVolume(6f, 1f), 1e-5f);
            Assert.AreEqual(1f, PitchLaw.LandVolume(12f, 1f), 1e-5f);
            Assert.AreEqual(1f, PitchLaw.LandVolume(40f, 1f));
            Assert.AreEqual(0f, PitchLaw.LandVolume(0f, 1f));
            Assert.AreEqual(0f, PitchLaw.LandVolume(-3f, 1f));
            // The mass leans on it.
            Assert.Greater(PitchLaw.LandVolume(6f, 100f), PitchLaw.LandVolume(6f, 1f));
            Assert.Less(PitchLaw.LandVolume(6f, 0.01f), PitchLaw.LandVolume(6f, 1f));
            Assert.AreEqual(0.5f * 1.3f, PitchLaw.LandVolume(6f, 100f), 1e-4f);
            Assert.AreEqual(0.5f * 0.7f, PitchLaw.LandVolume(6f, 0.01f), 1e-4f);
            for (float mass = 0.001f; mass < 1e6f; mass *= 3f)
                for (float speed = 0f; speed < 50f; speed += 1.7f)
                    Assert.That(PitchLaw.LandVolume(speed, mass), Is.InRange(0f, 1f));

            // The floor answers only something heavy that really fell.
            Assert.AreEqual(0f, PitchLaw.LandSubVolume(10f, 5f));
            Assert.AreEqual(0f, PitchLaw.LandSubVolume(2f, 5000f));
            Assert.Greater(PitchLaw.LandSubVolume(10f, 1000f), PitchLaw.LandSubVolume(10f, 100f));
            Assert.LessOrEqual(PitchLaw.LandSubVolume(100f, 1e9f), 0.8f);
        }

        [Test]
        public void EveryPreset_HasItsKeyItsScaleAndItsChords()
        {
            // ART_BIBLE 6.4: C major, F major, G lydian, E-flat major, D dorian, A minor pentatonic.
            Check(EnvironmentPreset.SunnyRug, 0, MusicScale.MajorPentatonic, 84, 0, "C");
            Check(EnvironmentPreset.BlockHall, 5, MusicScale.MajorPentatonic, 88, 5, "F");
            Check(EnvironmentPreset.PegboardWorkbench, 7, MusicScale.LydianPentatonic, 92, -5, "G");
            Check(EnvironmentPreset.CardboardBox, 3, MusicScale.MajorPentatonic, 80, 3, "Eb");
            Check(EnvironmentPreset.HighShelf, 2, MusicScale.DorianPentatonic, 92, 2, "D");
            Check(EnvironmentPreset.NightLight, 9, MusicScale.MinorPentatonic, 76, -3, "A");
            Assert.AreEqual(MusicKey.CMajor, MusicKey.Of(null));
            Assert.AreEqual(MusicKey.CMajor, MusicKey.Of(EnvironmentPreset.None));

            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                MusicKey key = MusicKey.Of(preset);
                int[] mode = Mode(key.Scale);
                Assert.AreEqual(5, key.Pentatonic.Length);
                foreach (int degree in key.Pentatonic) CollectionAssert.Contains(mode, degree, preset.Key + ": the pentatonic is part of its mode");
                CollectionAssert.Contains(key.Pentatonic, 0);
                CollectionAssert.Contains(key.Pentatonic, 7, "tonic and fifth are in every scale (buttons and exits play them)");
                foreach (int degree in key.Arpeggio) CollectionAssert.Contains(mode, degree % 12);

                // Four add9 chords, every note of each in the mode: nothing the lead plays can clash by more than colour.
                Assert.AreEqual(0, key.Chord(0).Root, "the first chord is the tonic's");
                for (int chord = 0; chord < MusicKey.ChordCount; chord++)
                {
                    Chord c = key.Chord(chord);
                    foreach (int interval in new[] { 0, c.Third, 7, 14 })
                        CollectionAssert.Contains(mode, (c.Root + interval) % 12, preset.Key + " chord " + chord + " interval " + interval);
                    foreach (int degree in c.Pentatonic)
                        CollectionAssert.Contains(mode, (c.Root + degree) % 12, preset.Key + " chord " + chord + ": the hold tone's notes are in the mode");
                    Assert.AreEqual((key.Root + c.Root) % 12, key.ChordRoot(chord));
                    Assert.AreEqual(key.Chord(chord).Root, key.Chord(chord + 4).Root, "the progression goes round");
                }
            }
            // Major is I-vi-IV-V as written.
            CollectionAssert.AreEqual(new[] { 0, 9, 5, 7 }, new[] { MusicKey.CMajor.Chord(0).Root, MusicKey.CMajor.Chord(1).Root, MusicKey.CMajor.Chord(2).Root, MusicKey.CMajor.Chord(3).Root });
            CollectionAssert.AreEqual(new[] { false, true, false, false }, new[] { MusicKey.CMajor.Chord(0).Minor, MusicKey.CMajor.Chord(1).Minor, MusicKey.CMajor.Chord(2).Minor, MusicKey.CMajor.Chord(3).Minor });

            // Snapping: to the nearest scale note, the lower of two equally near, never backwards.
            Assert.AreEqual(60, MusicKey.CMajor.Snap(60.4f));
            Assert.AreEqual(62, MusicKey.CMajor.Snap(61.2f));
            Assert.AreEqual(60, MusicKey.CMajor.Snap(61f), "C# is as near to C as to D: the lower");
            Assert.AreEqual(64, MusicKey.CMajor.Snap(65.4f));
            Assert.AreEqual(67, MusicKey.CMajor.Snap(65.6f));
            Assert.AreEqual(57, MusicKey.CMajor.Snap(58f), "between A and C");
            int snapped = int.MinValue;
            for (float note = 30f; note < 100f; note += 0.07f)
            {
                int now = MusicKey.CMajor.Snap(note);
                Assert.GreaterOrEqual(now, snapped);
                Assert.IsTrue(MusicKey.CMajor.Contains(now));
                snapped = now;
            }
            Assert.AreEqual(48, Scales.NoteIn(0, 48));
            Assert.AreEqual(57, Scales.NoteIn(9, 48));
            Assert.AreEqual(45, Scales.NoteIn(9, 40));
            Assert.AreEqual("Eb", Scales.NoteName(63));
        }

        static void Check(EnvironmentPreset preset, int root, MusicScale scale, int bpm, int transpose, string name)
        {
            MusicKey key = MusicKey.Of(preset);
            Assert.AreEqual(root, key.Root, preset.Key);
            Assert.AreEqual(scale, key.Scale, preset.Key);
            Assert.AreEqual(bpm, preset.Bpm, preset.Key);
            Assert.AreEqual(transpose, key.Transpose, preset.Key + " transposes the short way");
            Assert.AreEqual(name, Scales.NoteName(key.Root));
            StringAssert.StartsWith(name.Replace("Eb", "E-flat"), preset.MusicKey);
        }

        [Test]
        public void TheTune_IsAPureFunctionOfTheLevelId()
        {
            MusicKey key = MusicKey.CMajor;
            var score = new MusicScore(3, key, 84);
            var again = new MusicScore(3, key, 84);
            var other = new MusicScore(4, key, 84);
            bool differs = false;
            int sounding = 0, slots = 0;
            for (int step = 0; step < MusicScore.StepsPerCycle * 6; step++)
            {
                int note = score.LeadAt(step);
                Assert.AreEqual(note, again.LeadAt(step), "the same seed plays the same tune, step " + step);
                differs |= note != other.LeadAt(step);
                if ((step & 1) != 0)
                {
                    Assert.AreEqual(-1, note, "the lead moves in eighth notes");
                    continue;
                }
                slots++;
                if (note < 0) continue;
                sounding++;
                Assert.IsTrue(key.Contains(note), "note " + note + " at step " + step + " is in the scale");
                Assert.That(note, Is.InRange(MusicScore.LowestNote, MusicScore.HighestNote), "within the music box's clips");
            }
            Assert.IsTrue(differs, "another level, another tune");
            Assert.That(sounding / (float)slots, Is.InRange(0.25f, 0.7f), "an eighth note sounds with probability 0.45");
            Assert.GreaterOrEqual(score.LeadAt(0), 0, "the downbeat always sounds");
            Assert.AreEqual(-1, score.LeadAt(-2));

            // The seed is the level's id, the key and tempo the preset's.
            MusicScore night = MusicScore.For(null, EnvironmentPreset.NightLight);
            Assert.AreEqual(76, night.Bpm);
            Assert.IsTrue(night.Night);
            Assert.AreEqual(new MusicKey(9, MusicScale.MinorPentatonic), night.Key);
            Assert.AreEqual(15.0 / 76.0, night.StepSeconds, 1e-12);
            Assert.AreEqual(15.0 / 84.0, score.StepSeconds, 1e-12, "a sixteenth note at 84 BPM");

            // Random access: asking for a later round and then an earlier one gives the same notes again.
            var jumping = new MusicScore(3, key, 84);
            int far = MusicScore.StepsPerCycle * 5 + 6, near = MusicScore.StepsPerCycle * 2 + 10;
            int farNote = jumping.LeadAt(far), nearNote = jumping.LeadAt(near);
            Assert.AreEqual(score.LeadAt(far), farNote);
            Assert.AreEqual(score.LeadAt(near), nearNote);
            Assert.AreEqual(farNote, jumping.LeadAt(far));

            // The walk: neighbouring notes are at most a leap (four rungs of the ladder) apart within a motif.
            var ladder = new List<int>(score.Ladder);
            Assert.That(ladder.Count, Is.InRange(10, 13), "the scale's notes between B3 and C#6");
            int last = -1;
            for (int step = 0; step < MusicScore.StepsPerChord; step += 2)
            {
                int note = score.LeadAt(step);
                if (note < 0) continue;
                if (last >= 0) Assert.LessOrEqual(Mathf.Abs(ladder.IndexOf(note) - ladder.IndexOf(last)), 4);
                last = note;
            }

            // Every key keeps its tune inside its scale and range.
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                MusicScore s = MusicScore.For(null, preset);
                for (int step = 0; step < MusicScore.StepsPerCycle * 2; step += 2)
                {
                    int note = s.LeadAt(step);
                    if (note >= 0) Assert.IsTrue(s.Key.Contains(note) && note >= MusicScore.LowestNote && note <= MusicScore.HighestNote, preset.Key + " step " + step);
                }
            }
        }

        [Test]
        public void TheTune_IsAnAABAForm_ThatMutatesTwoStepsEveryEightBars()
        {
            Assert.AreEqual(16, MusicScore.StepsPerBar);
            Assert.AreEqual(32, MusicScore.StepsPerChord, "two bars to a chord");
            Assert.AreEqual(128, MusicScore.StepsPerCycle, "eight bars to a round");

            int formsWithB = 0, mutated = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var score = new MusicScore(seed, MusicKey.CMajor, 84);
                // A A' B A': the second and fourth two-bar sections repeat the first except for its last half bar.
                bool bDiffers = false;
                for (int step = 0; step < 32; step += 2)
                {
                    int a = score.LeadAt(step), a1 = score.LeadAt(32 + step), b = score.LeadAt(64 + step), a2 = score.LeadAt(96 + step);
                    Assert.AreEqual(a1, a2, "seed " + seed + ": the fourth section is A' again");
                    if (step < 24) Assert.AreEqual(a, a1, "seed " + seed + ": A' begins as A, step " + step);
                    bDiffers |= a != b;
                }
                if (bDiffers) formsWithB++;
                Assert.GreaterOrEqual(score.LeadAt(64), 0, "B has its downbeat too");

                // From one round to the next, at most two eighth-note slots of the material (A and B) change.
                for (int cycle = 0; cycle < 6; cycle++)
                {
                    int changed = 0;
                    for (int step = 0; step < 32; step += 2)
                    {
                        if (score.LeadAt(cycle * 128 + step) != score.LeadAt((cycle + 1) * 128 + step)) changed++;
                        if (score.LeadAt(cycle * 128 + 64 + step) != score.LeadAt((cycle + 1) * 128 + 64 + step)) changed++;
                    }
                    Assert.LessOrEqual(changed, 2, "seed " + seed + " round " + cycle);
                    mutated += changed;
                    Assert.AreEqual(score.LeadAt(cycle * 128), score.LeadAt(0), "the downbeat is never mutated");
                }
            }
            Assert.GreaterOrEqual(formsWithB, 11, "B is its own motif");
            Assert.Greater(mutated, 20, "the tune does change over time");
        }

        [Test]
        public void EachStep_StartsItsLayers()
        {
            var score = new MusicScore(1, MusicKey.CMajor, 84);
            var events = new List<MusicEvent>();

            // Step 0: the pad's first chord, the bass's first root, the lead's downbeat. No kit before the first grab.
            score.Events(0, 5.0, false, false, events);
            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(MusicVoice.Pad, events[0].Voice);
            Assert.AreEqual(SoundId.Pad0, events[0].Sound);
            Assert.AreEqual(MusicVoice.Bass, events[1].Voice);
            Assert.AreEqual(SoundId.Bass0, events[1].Sound);
            Assert.AreEqual(48, events[1].Note, "C3");
            Assert.AreEqual(MusicVoice.Lead, events[2].Voice);
            Assert.IsTrue(Sounds.IsBox(events[2].Sound));
            foreach (MusicEvent e in events)
            {
                Assert.AreEqual(5.0, e.Time);
                Assert.AreEqual(0, e.Step);
                Assert.Greater(e.Volume, 0f);
            }

            // Pads every two bars, one chord after the other; bass every bar.
            for (int step = 0; step < 512; step++)
            {
                events.Clear();
                score.Events(step, 0.0, false, false, events);
                int pads = events.FindAll(e => e.Voice == MusicVoice.Pad).Count, basses = events.FindAll(e => e.Voice == MusicVoice.Bass).Count;
                Assert.AreEqual(step % 32 == 0 ? 1 : 0, pads, "step " + step);
                Assert.AreEqual(step % 16 == 0 ? 1 : 0, basses, "step " + step);
                Assert.AreEqual(0, events.FindAll(e => e.Voice == MusicVoice.Kit).Count, "no kit before the first grab");
                int chord = step / 32 % 4;
                Assert.AreEqual(chord, MusicScore.ChordAt(step));
                if (pads == 1) Assert.AreEqual(SoundId.Pad0 + chord, events.Find(e => e.Voice == MusicVoice.Pad).Sound);
                if (basses == 1) Assert.AreEqual(SoundId.Bass0 + chord, events.Find(e => e.Voice == MusicVoice.Bass).Sound);
                MusicEvent lead = events.Find(e => e.Voice == MusicVoice.Lead);
                Assert.AreEqual(score.LeadAt(step) >= 0, lead.Sound != SoundId.None);
                if (lead.Sound != SoundId.None)
                {
                    // The clip and its rate give the note.
                    float hz = Synth.NoteHz(60 + 3 * (lead.Sound - SoundId.Box0)) * lead.Pitch;
                    Assert.AreEqual(Synth.NoteHz(lead.Note), hz, 0.05f);
                    Assert.AreEqual(score.LeadAt(step), lead.Note);
                }
            }

            // After the first grab the toy kit plays: kick on one and three, woodblock on two and four, shaker in eighths.
            int kicks = 0, woods = 0, shakers = 0;
            for (int step = 0; step < 16; step++)
            {
                events.Clear();
                score.Events(step, 0.0, true, false, events);
                bool kick = events.Exists(e => e.Sound == SoundId.KitKick), wood = events.Exists(e => e.Sound == SoundId.KitWood), shaker = events.Exists(e => e.Sound == SoundId.KitShaker);
                Assert.AreEqual(step == 0 || step == 8, kick, "kick at step " + step);
                Assert.AreEqual(step == 4 || step == 12, wood, "woodblock at step " + step);
                Assert.AreEqual(step % 2 == 0, shaker, "shaker at step " + step);
                if (kick) kicks++;
                if (wood) woods++;
                if (shaker) shakers++;
            }
            Assert.AreEqual(2, kicks);
            Assert.AreEqual(2, woods);
            Assert.AreEqual(8, shakers);

            // While holding, the music box rests - the hold tone is the lead - and everything else carries on.
            for (int step = 0; step < 128; step++)
            {
                events.Clear();
                score.Events(step, 0.0, true, true, events);
                Assert.IsFalse(events.Exists(e => e.Voice == MusicVoice.Lead), "step " + step);
                Assert.AreEqual(step % 32 == 0, events.Exists(e => e.Voice == MusicVoice.Pad));
            }

            // Night drops the kit to shaker only.
            var night = new MusicScore(14, new MusicKey(9, MusicScale.MinorPentatonic), 76, true);
            for (int step = 0; step < 64; step++)
            {
                events.Clear();
                night.Events(step, 0.0, true, false, events);
                foreach (MusicEvent e in events)
                    if (e.Voice == MusicVoice.Kit) Assert.AreEqual(SoundId.KitShaker, e.Sound, "step " + step);
            }
            events.Clear();
            night.Events(0, 0.0, true, false, events);
            Assert.IsTrue(events.Exists(e => e.Sound == SoundId.KitShaker));
            Assert.AreEqual(45, events.Find(e => e.Voice == MusicVoice.Bass).Note, "A2 under A minor");
        }

        [Test]
        public void TheScheduler_HandsOutEveryStepOnceAtItsExactTime_HoweverTheFramesFall()
        {
            // sunny-rug: C major pentatonic at 84 BPM. A sixteenth is 15 / 84 s.
            MusicScore score = MusicScore.For(null, EnvironmentPreset.SunnyRug);
            double stepSeconds = 15.0 / 84.0;
            var box = new MusicBox();
            var events = new List<MusicEvent>();
            Assert.AreEqual(0, box.Advance(0.0, events), "not started: nothing");

            double start = 10.0;
            box.Start(score, start);
            Assert.IsTrue(box.Running);
            Assert.AreEqual(start + MusicBox.StartDelay, box.NextTime, 1e-12);

            // Twenty seconds of uneven frames: 60 Hz, some at 20 Hz, a hitch of 150 ms now and then.
            var rng = new SynthRng(99u);
            double now = start;
            var all = new List<MusicEvent>();
            int steps = 0;
            while (now < start + 20.0)
            {
                events.Clear();
                steps += box.Advance(now, events);
                foreach (MusicEvent e in events)
                {
                    Assert.GreaterOrEqual(e.Time, now - 1e-9, "never scheduled in the past");
                    Assert.Less(e.Time, now + MusicBox.LookAhead + 1e-9, "never further ahead than the look-ahead");
                    all.Add(e);
                }
                float dice = rng.Value();
                now += dice < 0.8f ? 1.0 / 60.0 : dice < 0.97f ? 0.05 : 0.15;
            }
            Assert.AreEqual(0, box.Resyncs, "a 200 ms look-ahead survives 150 ms hitches");
            Assert.AreEqual(box.NextStep, steps);
            Assert.That(steps, Is.InRange(111, 114), "20 s at 84 BPM is 112 sixteenths (and the look-ahead)");

            // The expected note times: step n at start + 0.1 + n x 15/84, exactly, in order, each step once.
            int previousStep = -1;
            var expected = new List<MusicEvent>();
            for (int step = 0; step < steps; step++) score.Events(step, start + MusicBox.StartDelay + step * stepSeconds, false, false, expected);
            Assert.AreEqual(expected.Count, all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                Assert.AreEqual(expected[i].Step, all[i].Step);
                Assert.AreEqual(expected[i].Sound, all[i].Sound);
                Assert.AreEqual(expected[i].Pitch, all[i].Pitch);
                Assert.AreEqual(start + 0.1 + all[i].Step * stepSeconds, all[i].Time, 1e-9, "event " + i + " is on the grid");
                Assert.GreaterOrEqual(all[i].Step, previousStep);
                previousStep = all[i].Step;
            }
            // Pads at 0, 32, 64, 96: 10.1 s, then every 5.714 s.
            List<MusicEvent> pads = all.FindAll(e => e.Voice == MusicVoice.Pad);
            Assert.AreEqual(4, pads.Count);
            Assert.AreEqual(10.1, pads[0].Time, 1e-9);
            Assert.AreEqual(10.1 + 32 * stepSeconds, pads[1].Time, 1e-9);
            Assert.AreEqual(15.814, pads[1].Time, 0.001);
            CollectionAssert.AreEqual(new[] { SoundId.Pad0, SoundId.Pad1, SoundId.Pad2, SoundId.Pad3 }, pads.ConvertAll(e => e.Sound));
            Assert.AreEqual((steps + 15) / 16, all.FindAll(e => e.Voice == MusicVoice.Bass).Count, "a bass note a bar");
            Assert.AreEqual(0, box.ChordAt(start + 1.0));
            Assert.AreEqual(1, box.ChordAt(start + 0.1 + 33 * stepSeconds));
            Assert.AreEqual(0, box.ChordAt(start - 5.0), "before the tune starts");

            // The same frames again give the same notes: nothing depends on anything but the clock.
            var second = new MusicBox();
            second.Start(MusicScore.For(null, EnvironmentPreset.SunnyRug), start);
            var replay = new List<MusicEvent>();
            for (double t = start; t < start + 20.0; t += 1.0 / 144.0) second.Advance(t, replay);
            for (int i = 0; i < Math.Min(replay.Count, all.Count); i++)
            {
                Assert.AreEqual(all[i].Time, replay[i].Time, 1e-9);
                Assert.AreEqual(all[i].Sound, replay[i].Sound);
            }

            // Every preset's tempo is its grid.
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                var b = new MusicBox();
                b.Start(MusicScore.For(null, preset), 0.0);
                Assert.AreEqual(MusicBox.StartDelay + 16 * 15.0 / preset.Bpm, b.TimeOf(16), 1e-12, preset.Key + ": a bar is 240 / bpm seconds");
            }
        }

        [Test]
        public void TheScheduler_StopsResumesAndSurvivesARunawayClock()
        {
            MusicScore score = MusicScore.For(null, EnvironmentPreset.BlockHall);
            double step = 15.0 / 88.0;
            var box = new MusicBox();
            var events = new List<MusicEvent>();
            box.Start(score, 0.0);
            for (double now = 0.0; now < 9.0; now += 1.0 / 60.0) box.Advance(now, events);
            int reached = box.NextStep;
            Assert.Greater(reached, 32, "into the second chord");

            // Stopped (the game is paused): nothing more.
            box.Stop();
            events.Clear();
            Assert.AreEqual(0, box.Advance(30.0, events));
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(reached, box.NextStep);

            // Resumed: from the start of the two-bar section it was in, so pad and bass come back in together.
            box.Resume(40.0);
            Assert.IsTrue(box.Running);
            Assert.AreEqual(reached - reached % 32, box.NextStep);
            Assert.AreEqual(40.0 + MusicBox.StartDelay, box.NextTime, 1e-9);
            box.Advance(40.0, events);
            Assert.IsTrue(events.Exists(e => e.Voice == MusicVoice.Pad), "the section's chord sounds again");
            Assert.AreEqual(40.1, events[0].Time, 1e-9);
            Assert.AreEqual(0, box.Resyncs);

            // The clock runs away (a hidden tab, a hitch longer than the look-ahead): no burst of late notes.
            events.Clear();
            box.Advance(40.02, events);
            int before = box.NextStep;
            events.Clear();
            box.Advance(43.0, events);
            Assert.AreEqual(1, box.Resyncs);
            foreach (MusicEvent e in events) Assert.GreaterOrEqual(e.Time, 43.0, "nothing is scheduled in the past");
            Assert.AreEqual(43.0 + MusicBox.StartDelay, events[0].Time, 1e-9);
            Assert.LessOrEqual(box.NextStep - before, 2 + (int)(MusicBox.LookAhead / step) + 32);

            // The clock starts over (the audio device changed): the tune finds it again instead of waiting for hours.
            events.Clear();
            box.Advance(1.0, events);
            Assert.AreEqual(2, box.Resyncs);
            Assert.AreEqual(1.0 + MusicBox.StartDelay, events[0].Time, 1e-9);

            // The kit and the hold are the scheduler's to pass on.
            box.Kit = true;
            box.Holding = true;
            events.Clear();
            for (double now = 1.0; now < 8.0; now += 1.0 / 60.0) box.Advance(now, events);
            Assert.IsTrue(events.Exists(e => e.Voice == MusicVoice.Kit));
            Assert.IsFalse(events.Exists(e => e.Voice == MusicVoice.Lead));
        }

        [Test]
        public void TheVoices_NeverExceedTwelve_AndEachLayerKeepsItsOwn()
        {
            Assert.AreEqual(12, AudioOutput.MusicVoices);
            Assert.AreEqual(12, AudioOutput.EffectVoices);
            foreach (EnvironmentPreset preset in new[] { EnvironmentPreset.SunnyRug, EnvironmentPreset.PegboardWorkbench, EnvironmentPreset.NightLight })
            {
                // A bank in the preset's key, so the voices know how long each clip really rings.
                var bank = new SoundBank { KeepSamples = true };
                bank.SetKey(MusicKey.Of(preset), preset.Bpm);
                bank.GenerateAll();
                var output = new AudioOutput(bank, null, false);
                var box = new MusicBox { Kit = true };
                box.Start(MusicScore.For(null, preset), 0.0);
                var events = new List<MusicEvent>();
                int most = 0;
                for (double now = 0.0; now < 60.0; now += 1.0 / 60.0)
                {
                    output.Now = now;
                    events.Clear();
                    box.Advance(now, events);
                    foreach (MusicEvent e in events) Assert.GreaterOrEqual(output.Schedule(e), 0);
                    most = Math.Max(most, output.MusicSounding);
                    Assert.LessOrEqual(output.MusicSounding, 12, preset.Key + ": at most 12 voices sound at once");
                }
                Assert.GreaterOrEqual(most, 5, preset.Key + ": pad, bass, lead and kit do overlap");
                Assert.AreEqual(0, output.Skipped);
                Assert.AreEqual(0, output.Cuts, preset.Key + ": no note cuts another of its layer short - each layer has voices enough");
                // A pad chord rings for its two bars and a little more; the next one starts on another voice.
                Assert.Greater(bank.Seconds(SoundId.Pad0), 2f * 240f / preset.Bpm);
                Assert.Less(bank.Seconds(SoundId.Pad0), 2f * 240f / preset.Bpm + 2f);
                Assert.Greater(output.Count(SoundId.Pad0), 2);
                Assert.Greater(output.Count(SoundId.KitShaker), 100);
                output.Dispose();
                bank.Dispose();
            }
        }
    }

    /// <summary>A level for the audio presenter's tests: a build lambda, and the environment (the key) it asks for.</summary>
    public sealed class SoundLevel : LevelDefinition
    {
        readonly Action<LevelContext> build;
        readonly string environment;

        public SoundLevel(Action<LevelContext> build, string environment = null)
        {
            this.build = build;
            this.environment = environment;
        }

        public override string Environment => environment ?? base.Environment;

        public override void Build(LevelContext ctx) => build(ctx);
    }

    /// <summary>
    /// The presenter: which sound each event of the game makes, at what pitch and volume; that nothing
    /// sounds before the first click; that the game's sounds pause with the game; volumes from Settings.
    /// All of it runs silently - the voices keep an account of what they would play - except one test that
    /// creates the real (muted) AudioSources.
    /// </summary>
    public class AudioPresenterTests
    {
        Game game;
        GameFlow flow;
        Presentation presentation;
        AudioPresenter audio;
        ScriptedInput input;
        double clock;
        Prop block;
        Exit exit;

        [SetUp]
        public void Init()
        {
            clock = 100.0;
            Settings.Use(new MemoryStore());
        }

        [TearDown]
        public void Dispose()
        {
            presentation?.Dispose();
            presentation = null;
            flow?.Dispose();
            flow = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            AudioPresenter.Overrides = null;
            Settings.Use(null);
            Materials.Tier = QualityTier.Medium;
            Materials.Plain = false;
        }

        static List<PresenterRegistry.Entry> OnlyAudio()
        {
            var entries = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                if (entry.Type == typeof(AudioPresenter)) entries.Add(entry);
            return entries;
        }

        // A room 40 across with a one-unit block three units in front of the player.
        void Room(LevelContext ctx)
        {
            TestHelpers.Room(ctx, 20f, 12f);
            block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f));
            exit = ctx.AddExit(new Vector3(8f, 1f, 8f), new Vector3(2f, 2f, 2f)).Lock();
            ctx.SetSpawn(Vector3.zero, 0f);
        }

        AudioPresenter Attach(Action<LevelContext> build = null, string environment = null, bool unlocked = true, bool withFlow = false, AudioPresenter.Options options = null)
        {
            build ??= Room;
            input = new ScriptedInput();
            game = Game.Create(new GameOptions { Input = input });
            options ??= new AudioPresenter.Options { Audible = false };
            options.Clock ??= () => clock;
            options.Unlocked |= unlocked;
            AudioPresenter.Overrides = options;
            if (withFlow) flow = new GameFlow(game, new LevelList(new[] { 0, 1 }, id => new SoundLevel(build, environment)), new Progress(new MemoryStore()));
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = OnlyAudio(), Flow = flow });
            AudioPresenter.Overrides = null;
            if (withFlow) flow.StartLevel(0);
            else game.LoadLevel(new SoundLevel(build, environment));
            audio = presentation.Get<AudioPresenter>();
            Assert.IsNotNull(audio, "the audio presenter attached");
            return audio;
        }

        void Present(float dt = Sim.Dt)
        {
            clock += dt;
            presentation.Frame(dt, 1f);
        }

        void Tick(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                game.Tick();
                Present();
            }
        }

        void Wait(float seconds)
        {
            for (int i = Mathf.CeilToInt(seconds / Sim.Dt); i > 0; i--) Present();
        }

        void Click()
        {
            input.Once.GrabPressed = true;
            Tick();
        }

        void LookAt(Vector3 target) => TestHelpers.LookAt(game.Player, target);

        PlayedSound Last(SoundId id)
        {
            Assert.IsTrue(audio.Output.Last(id, out PlayedSound sound), id + " was played");
            return sound;
        }

        [Test]
        public void ThePresenter_IsFoundByTheGame_AndRunsWithThePlainLookToo()
        {
            List<PresenterRegistry.Entry> entries = OnlyAudio();
            Assert.AreEqual(1, entries.Count, "AudioPresenter carries [Presenter]");
            Assert.That(entries[0].Order, Is.InRange(300, 399), "audio's range");
            Assert.IsFalse(entries[0].ProvidesLook || entries[0].ProvidesHud || entries[0].Fallback);
            CollectionAssert.Contains(PresenterRegistry.Select(PresenterRegistry.All, true), entries[0], "it provides neither look nor HUD, so plain keeps it");
            CollectionAssert.Contains(PresenterRegistry.Select(PresenterRegistry.All, false), entries[0]);

            // Outside Play Mode it is silent by default: no AudioSources, no clips, nothing sounding.
            game = Game.Create();
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = entries });
            audio = presentation.Get<AudioPresenter>();
            Assert.IsNotNull(audio);
            Assert.AreSame(audio, AudioPresenter.Active);
            Assert.IsFalse(audio.Output.Audible);
            Assert.IsNull(audio.Bank);
            Assert.IsFalse(audio.Unlocked);
            Assert.AreEqual(0, game.Root.GetComponentsInChildren<AudioSource>(true).Length);
            presentation.Frame(Sim.Dt, 1f);
            game.LoadLevel(new SoundLevel(Room));
            presentation.Frame(Sim.Dt, 1f);
            game.Tick();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0, audio.Output.Played);
            presentation.Dispose();
            Assert.IsNull(AudioPresenter.Active);

            // It comes and goes without a trace, with a level already standing (the screenshot tool's order).
            int objects = game.Root.GetComponentsInChildren<Transform>(true).Length;
            AudioPresenter.Overrides = new AudioPresenter.Options { Audible = false, Unlocked = true };
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = entries });
            AudioPresenter.Overrides = null;
            audio = presentation.Get<AudioPresenter>();
            Assert.IsNotNull(audio.Score, "attached to a loaded level: as if it had just loaded");
            for (int i = 0; i < 30; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            Assert.IsTrue(audio.Music.Running);
            Assert.Greater(audio.Output.Played, 0);
            presentation.Dispose();
            presentation = null;
            Assert.AreEqual(objects, game.Root.GetComponentsInChildren<Transform>(true).Length, "nothing is left under the game's root");
            // Disposed: settings and events no longer reach it.
            Assert.DoesNotThrow(() => Settings.SfxVolume = 0.3f);
            int played = audio.Output.Played;
            game.Events.RaisePlayerLanded(new PlayerLandEvent { ImpactSpeed = 9f });
            Assert.AreEqual(played, audio.Output.Played);
        }

        [Test]
        public void NothingSounds_BeforeThePlayersFirstClick()
        {
            Attach(unlocked: false);
            Assert.IsFalse(audio.Unlocked);
            LookAt(block.Center);
            Tick(5);
            Click();
            Assert.IsTrue(game.Grabber.IsHolding);
            Tick(10);
            Click();
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = block, Speed = 8f, Mass = 1f, Point = block.Center });
            game.Events.RaisePlayerLanded(new PlayerLandEvent { ImpactSpeed = 9f });
            exit.Unlock();
            audio.PlayUi(UiSound.Click);
            Assert.AreEqual(-1, audio.Play(SoundId.Grab));
            Wait(1f);
            Assert.AreEqual(0, audio.Output.Played, "a browser starts no audio before a gesture; neither do we");
            Assert.IsFalse(audio.Music.Running);
            Assert.IsFalse(audio.Output.HoldPlaying);

            // The click that captures the pointer is the gesture.
            presentation.Context.PointerLocked = true;
            Present();
            Assert.IsTrue(audio.Unlocked);
            Present();
            Assert.IsTrue(audio.Music.Running, "the music starts with the first frame after it");
            Assert.Greater(audio.Output.Played, 0);
            audio.PlayUi(UiSound.Click);
            Assert.AreEqual(1, audio.Output.Count(SoundId.UiClick));

            // The right-button look is a gesture as well; so is a menu calling Unlock.
            presentation.Dispose();
            game.Dispose();
            Attach(unlocked: false);
            presentation.Context.LookHeld = true;
            Present();
            Assert.IsTrue(audio.Unlocked);
            presentation.Dispose();
            game.Dispose();
            Attach(unlocked: false);
            audio.Unlock();
            Assert.IsTrue(audio.Unlocked);
        }

        [Test]
        public void Focus_TicksOnceWhenTheAimComesToRestOnAToy()
        {
            Attach();
            LookAt(new Vector3(10f, 5f, 10f));
            Tick(3);
            Assert.AreEqual(0, audio.Output.Count(SoundId.FocusTick));
            LookAt(block.Center);
            Tick(1);
            Assert.AreSame(block, game.Grabber.Focus);
            Assert.AreEqual(1, audio.Output.Count(SoundId.FocusTick));
            PlayedSound tick = Last(SoundId.FocusTick);
            Assert.IsFalse(tick.Spatial, "2D");
            Assert.AreEqual(1f, tick.Pitch);
            Tick(20);
            Assert.AreEqual(1, audio.Output.Count(SoundId.FocusTick), "once, not every frame");
            LookAt(new Vector3(10f, 5f, 10f));
            Tick(2);
            LookAt(block.Center);
            Tick(2);
            Assert.AreEqual(2, audio.Output.Count(SoundId.FocusTick), "again when the aim comes back");
            // Not while holding: the reticle is hidden then.
            Click();
            Tick(5);
            Assert.AreEqual(2, audio.Output.Count(SoundId.FocusTick));
        }

        [Test]
        public void GrabHoldRelease_SoundTheToysTrueSize()
        {
            Attach();
            MusicKey key = audio.Key;
            Assert.AreEqual(MusicKey.CMajor, key, "an ad-hoc level has no room: C major");
            LookAt(block.Center);
            Tick(2);
            float diameter = 2f * block.Radius;
            Assert.AreEqual(Mathf.Sqrt(3f), diameter, 0.01f, "a one-unit block is 1.73 across its corners");

            // Grab: 2D, pitched by the law - a 1.73-unit toy in C major is five semitones down, on G4.
            Click();
            Assert.IsTrue(game.Grabber.IsHolding);
            Assert.AreEqual(1, audio.Output.Count(SoundId.Grab));
            PlayedSound grab = Last(SoundId.Grab);
            Assert.IsFalse(grab.Spatial);
            Assert.AreEqual(Mathf.Pow(2f, -5f / 12f), grab.Pitch, 1e-4f);
            Assert.AreEqual(1f, grab.Volume);

            // Hold: the loop fades in over 40 ms, at the law's pitch snapped to the chord's pentatonic.
            Assert.IsTrue(audio.Output.HoldPlaying, "the hold loop starts with the grab");
            Tick(4);
            Assert.AreEqual(1f, audio.Output.HoldGain, 1e-4f);
            float small = PitchLaw.Pitch(PitchLaw.HoldSemis(2f * block.Radius, key, 0));
            Assert.AreEqual(small, audio.Output.HoldPitch, small * 0.01f);
            Assert.IsTrue(audio.Music.Holding, "the music box rests while the hold tone is the lead");
            Assert.IsTrue(audio.Music.Kit, "the toy kit enters after the first grab");
            Assert.AreEqual(0, audio.Output.Count(SoundId.HoldJump));

            // Look at the far wall: the toy jumps there and grows. One tick, and the tone glides down.
            LookAt(new Vector3(0f, 6f, 20f));
            Tick(1);
            float grown = block.Scale;
            Assert.Greater(grown, 3f, "the block is on the far wall now, several times its size");
            Assert.AreEqual(1, audio.Output.Count(SoundId.HoldJump), "a scale jump of more than 5% in one tick ticks");
            float large = PitchLaw.Pitch(PitchLaw.HoldSemis(2f * block.Radius, key, audio.CurrentChord));
            Assert.Less(large, small * 0.6f, "bigger is lower");
            Assert.Greater(audio.Output.HoldPitch, large * 1.02f, "60 ms of portamento: not there yet after one frame");
            Assert.Less(audio.Output.HoldPitch, small);
            Tick(8);
            Assert.AreEqual(large, audio.Output.HoldPitch, large * 0.02f, "and there after a tenth of a second");
            Assert.AreEqual(1, audio.Output.Count(SoundId.HoldJump), "standing still is not a jump");

            // Release: the thock at the true size, tuned to the chord's root or fifth; the sub because it grew past x2.
            float released = 2f * block.Radius;
            int chord = audio.CurrentChord;
            Click();
            Assert.IsFalse(game.Grabber.IsHolding);
            Assert.AreEqual(1, audio.Output.Count(SoundId.ReleaseThock));
            PlayedSound thock = Last(SoundId.ReleaseThock);
            Assert.AreEqual(PitchLaw.Pitch(PitchLaw.ThockSemis(released, key, chord)), thock.Pitch, 1e-4f);
            Assert.Less(thock.Pitch, 0.6f, "a toy ten units across thocks more than an octave down");
            Assert.IsFalse(thock.Spatial);
            Assert.AreEqual(1, audio.Output.Count(SoundId.ReleaseSub), "grew by more than x2: the sub");
            Assert.AreEqual(0, audio.Output.Count(SoundId.ReleaseBell));
            Assert.IsFalse(audio.Music.Holding);
            Tick(4);
            Assert.IsFalse(audio.Output.HoldPlaying, "the loop fades out within 40 ms of the release");
            Assert.AreEqual(1, audio.Output.Count(SoundId.Hold), "one hold, one loop start");
        }

        [Test]
        public void AToyThatShrank_RingsTheBell()
        {
            Prop far = null;
            Attach(ctx =>
            {
                TestHelpers.Room(ctx, 20f, 12f);
                far = ctx.AddProp(BasicToys.Block(3f), new Vector3(0f, 1.5f, 16f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            LookAt(far.Center);
            Tick(2);
            Click();
            Assert.IsTrue(game.Grabber.IsHolding);
            float grabPitch = Last(SoundId.Grab).Pitch;
            Assert.Less(grabPitch, 0.5f, "a block 5.2 units across is more than an octave below a one-unit toy");
            // Down at the floor in front of the feet: the toy comes close and shrinks.
            LookAt(new Vector3(0f, 0f, 2f));
            Tick(12);
            Assert.Less(far.Scale, 0.4f);
            float holdPitch = audio.Output.HoldPitch;
            Click();
            Assert.AreEqual(1, audio.Output.Count(SoundId.ReleaseBell), "shrank below x0.5: the bell");
            Assert.AreEqual(0, audio.Output.Count(SoundId.ReleaseSub));
            Assert.Greater(Last(SoundId.ReleaseThock).Pitch, 0.9f, "the small toy thocks high");
            Assert.Greater(holdPitch, 2f * PitchLaw.Pitch(PitchLaw.HoldSemis(3f * Mathf.Sqrt(3f), audio.Key, 0)), "and its hold tone had risen");
            Assert.AreEqual(1f, Last(SoundId.ReleaseBell).Pitch, "the bell is unpitched");
        }

        [Test]
        public void AnImpact_SoundsLikeItsMaterial_AtItsSizeSpeedAndPlace()
        {
            Prop wood = null, glass = null, giant = null, feather = null, plain = null;
            Attach(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                GameObject Toy(float size, ToyRecipe recipe)
                {
                    GameObject toy = BasicToys.Block(size);
                    ToyInfo.Tag(toy, recipe, Palette.Cherry);
                    return toy;
                }
                wood = ctx.AddProp(Toy(1f, ToyRecipe.PaintedWood), new Vector3(-6f, 0.5f, 6f));
                glass = ctx.AddProp(Toy(0.5f, ToyRecipe.Glass), new Vector3(-3f, 0.25f, 6f));
                giant = ctx.AddProp(Toy(6f, ToyRecipe.BrushedMetal), new Vector3(8f, 3f, 14f));
                feather = ctx.AddProp(Toy(0.3f, ToyRecipe.Feather), new Vector3(0f, 0.15f, 6f));
                GameObject bare = BasicToys.Block(1f);
                if (bare.GetComponent<ToyInfo>() != null) Object.DestroyImmediate(bare.GetComponent<ToyInfo>());
                plain = ctx.AddProp(bare, new Vector3(3f, 0.5f, 6f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Tick(30);
            int before = audio.Output.Played;

            // Land: 3D at the point of impact, pitch = clamp(S^-0.7), volume = clamp(speed / 12) leaned on by the mass.
            var point = new Vector3(-6f, 0f, 6f);
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = wood, Speed = 6f, Mass = 1f, Point = point, Normal = Vector3.up });
            PlayedSound land = Last(SoundId.LandWood);
            Assert.IsTrue(land.Spatial, "3D");
            Assert.AreEqual(point, land.Position);
            Assert.AreEqual(0.5f, land.Volume, 1e-4f, "speed 6 of 12");
            Assert.AreEqual(Mathf.Pow(2f * wood.Radius, -0.7f), land.Pitch, 1e-4f);
            Assert.AreEqual(0.681f, land.Pitch, 0.002f, "a 1.73-unit toy");
            Assert.AreEqual(before + 1, audio.Output.Played, "one sound, no sub for a light toy");
            Present();

            game.Events.RaisePropImpact(new PropImpactEvent { Prop = glass, Speed = 12f, Mass = 0.2f, Point = glass.Center });
            PlayedSound tink = Last(SoundId.LandGlass);
            Assert.Greater(tink.Pitch, land.Pitch, "smaller is higher");
            Assert.AreEqual(PitchLaw.LandVolume(12f, 0.2f), tink.Volume, 1e-4f);
            Assert.Less(tink.Volume, 1f, "a light toy is a little quieter at the same speed");
            Present();

            // A giant: low, loud, and the floor answers with the sub.
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = giant, Speed = 10f, Mass = giant.Mass, Point = giant.Center });
            Assert.Greater(giant.Mass, 100f);
            PlayedSound clang = Last(SoundId.LandMetal);
            Assert.AreEqual(Mathf.Clamp(Mathf.Pow(2f * giant.Radius, -0.7f), 0.25f, 4f), clang.Pitch, 1e-4f);
            Assert.Less(clang.Pitch, 0.3f);
            Assert.AreEqual(1f, clang.Volume);
            PlayedSound sub = Last(SoundId.ReleaseSub);
            Assert.IsTrue(sub.Spatial);
            Assert.AreEqual(PitchLaw.LandSubVolume(10f, giant.Mass), sub.Volume, 1e-4f);
            Present();

            game.Events.RaisePropImpact(new PropImpactEvent { Prop = feather, Speed = 4f, Mass = 0.02f, Point = feather.Center });
            Assert.AreEqual(1, audio.Output.Count(SoundId.LandFeather));
            // A toy without a tag is plastic.
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = plain, Speed = 4f, Mass = 1f, Point = plain.Center });
            Assert.AreEqual(1, audio.Output.Count(SoundId.LandPlastic));
            Present();

            // Too slow to hear: nothing. No prop, a removed prop, a held prop: nothing.
            int played = audio.Output.Played;
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = wood, Speed = 0.2f, Mass = 1f, Point = point });
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = null, Speed = 9f, Mass = 1f, Point = point });
            Assert.AreEqual(played, audio.Output.Played);

            // A pile coming down: at most four landings start in one frame, each quieter than the one before.
            for (int i = 0; i < 9; i++) game.Events.RaisePropImpact(new PropImpactEvent { Prop = wood, Speed = 9f, Mass = 1f, Point = point });
            Assert.AreEqual(played + AudioPresenter.MaxLandsPerFrame, audio.Output.Played);
            float pile = 0f;
            for (int back = 0; back < AudioPresenter.MaxLandsPerFrame; back++)
            {
                Assert.IsTrue(audio.Output.Last(back, out PlayedSound one));
                Assert.AreEqual(0.75f / (1f + 0.5f * (AudioPresenter.MaxLandsPerFrame - 1 - back)), one.Volume, 1e-4f);
                pile += one.Volume;
            }
            Assert.Less(pile, 2f, "four at once are less than twice one, not four times");
            Present();
            game.Events.RaisePropImpact(new PropImpactEvent { Prop = wood, Speed = 9f, Mass = 1f, Point = point });
            Assert.AreEqual(played + AudioPresenter.MaxLandsPerFrame + 1, audio.Output.Played, "and the next frame has room again");
            Assert.LessOrEqual(audio.Output.EffectsSounding, AudioOutput.EffectVoices);
            Assert.AreEqual(0, audio.Output.Stolen, "twelve voices were enough for all of this");
            for (int i = 0; i < 14; i++)
            {
                Present();
                game.Events.RaisePropImpact(new PropImpactEvent { Prop = giant, Speed = 9f, Mass = 1f, Point = point });
            }
            Assert.AreEqual(AudioOutput.EffectVoices, audio.Output.EffectsSounding, "a thirteenth long ring takes the oldest voice");
            Assert.Greater(audio.Output.Stolen, 0);

            // Every material of the art bible has its clip.
            Assert.AreEqual(SoundId.LandPlastic, Sounds.Land(ToyRecipe.GlossyPlastic.Sound));
            Assert.AreEqual(SoundId.LandWood, Sounds.Land(ToyRecipe.PaintedWood.Sound));
            Assert.AreEqual(SoundId.LandRubber, Sounds.Land(ToyRecipe.Rubber.Sound));
            Assert.AreEqual(SoundId.LandMetal, Sounds.Land(ToyRecipe.BrushedMetal.Sound));
            Assert.AreEqual(SoundId.LandGlass, Sounds.Land(ToyRecipe.Glass.Sound));
            Assert.AreEqual(SoundId.LandFelt, Sounds.Land(ToyRecipe.Felt.Sound));
            Assert.AreEqual(SoundId.LandFelt, Sounds.Land(ToyRecipe.Sponge.Sound), "felt and sponge share a row");
            Assert.AreEqual(SoundId.LandCardboard, Sounds.Land(ToyRecipe.Cardboard.Sound));
            Assert.AreEqual(SoundId.LandFeather, Sounds.Land(ToyRecipe.Feather.Sound));
        }

        [Test]
        public void ARealFall_IsHeardThroughTheEnginesImpactEvent()
        {
            Prop dropped = null;
            Attach(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                dropped = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 6f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            int impacts = 0;
            game.Events.PropImpact += e => impacts++;
            Tick(90);
            Assert.Greater(impacts, 0, "the block hit the floor");
            Assert.GreaterOrEqual(audio.Output.Count(SoundId.LandPlastic), 1);
            Assert.LessOrEqual(audio.Output.Count(SoundId.LandPlastic), impacts);
            PlayedSound land = Last(SoundId.LandPlastic);
            Assert.IsTrue(land.Spatial);
            Assert.Less(Vector3.Distance(land.Position, dropped.Center), 2.5f, "it sounds where the block is");

            // The player's own landing: 2D, louder the harder it is.
            game.Events.RaisePlayerLanded(new PlayerLandEvent { ImpactSpeed = 7f });
            PlayedSound own = Last(SoundId.PlayerLand);
            Assert.IsFalse(own.Spatial);
            Assert.AreEqual(0.5f, own.Volume, 1e-4f);
            game.Events.RaisePlayerLanded(new PlayerLandEvent { ImpactSpeed = 1f });
            Assert.AreEqual(1, audio.Output.Count(SoundId.PlayerLand), "a step down is not a landing");
            game.Events.RaisePlayerLanded(new PlayerLandEvent { ImpactSpeed = 30f });
            Assert.AreEqual(1f, Last(SoundId.PlayerLand).Volume);
        }

        [Test]
        public void Exits_AndGadgetButtons_SoundInTheLevelsKey()
        {
            Attach(environment: "night-light");
            Assert.AreEqual(new MusicKey(9, MusicScale.MinorPentatonic), audio.Key, "night-light is A minor pentatonic");
            Assert.AreEqual(0, audio.Output.Count(SoundId.ExitClose), "an exit locked while the level is built is not news");
            Tick(5);
            exit.Unlock();
            PlayedSound open = Last(SoundId.ExitOpen);
            Assert.IsTrue(open.Spatial);
            Assert.AreEqual(exit.Position, open.Position);
            Assert.AreEqual(Mathf.Pow(2f, -3f / 12f), open.Pitch, 1e-4f, "written with do on C; A is three semitones down");
            Assert.AreEqual(open.Pitch, audio.KeyPitch(), 1e-6f);
            exit.Lock();
            Assert.AreEqual(1, audio.Output.Count(SoundId.ExitClose));

            // A menu's click is on the tonic too; its hover is not pitched.
            audio.PlayUi(UiSound.Click);
            audio.PlayUi(UiSound.Hover);
            Assert.AreEqual(open.Pitch, Last(SoundId.UiClick).Pitch, 1e-6f);
            Assert.AreEqual(1f, Last(SoundId.UiHover).Pitch);
            Assert.AreEqual(1f, audio.KeyPitch(9), 1e-6f, "the button is written with do on A: at home in A minor");

            // Gadget events are found by name on whatever carries them.
            var plates = new FakeGadgetEvents();
            var heard = new List<string>();
            GadgetSounds bound = GadgetSounds.Bind(plates, (sound, placed, position) => heard.Add(sound + (placed ? " at " + position : " nowhere")));
            // "...Pressed" / "...Released" by their endings, PlateRejected by the table of named mirrors.
            CollectionAssert.AreEquivalent(new[] { "PlatePressed", "PlateReleased", "PlateRejected", "LeverPressed", "PedalReleased" }, new List<string>(bound.EventNames));
            plates.Press(new FakePlateEvent { Position = new Vector3(1f, 2f, 3f) });
            plates.Release(new FakePlateEvent { Position = new Vector3(4f, 5f, 6f) });
            plates.Lever(new FakeLeverEvent { Prop = block });
            plates.Pedal(new FakePedalEvent { Gadget = new FakeGadget { Position = new Vector3(7f, 8f, 9f) } });
            plates.Pedal(new FakePedalEvent());
            plates.Other(new FakePlateEvent { Position = new Vector3(2f, 0f, 2f) });
            plates.Number(3);
            CollectionAssert.AreEqual(new[]
            {
                "ButtonPress at " + new Vector3(1f, 2f, 3f), "ButtonRelease at " + new Vector3(4f, 5f, 6f), "ButtonPress at " + block.Center,
                "ButtonRelease at " + new Vector3(7f, 8f, 9f), "ButtonRelease nowhere", "ExitClose at " + new Vector3(2f, 0f, 2f),
            }, heard);
            bound.Unbind();
            plates.Press(new FakePlateEvent());
            Assert.AreEqual(6, heard.Count, "unbound");
            Assert.AreEqual(0, bound.Count);
            Assert.AreEqual(SoundId.ButtonPress, GadgetSounds.SoundFor("ButtonPressed"));
            Assert.AreEqual(SoundId.ButtonRelease, GadgetSounds.SoundFor("PlateReleased"));
            Assert.AreEqual(SoundId.None, GadgetSounds.SoundFor("PropDropped"));
            Assert.AreEqual(SoundId.None, GadgetSounds.SoundFor(null));
            Assert.AreEqual(SoundId.ExitClose, GadgetSounds.SoundFor("PlateRejected"), "a named mirror");
            // The game's own events: the plate's press and release, and every mirror in the table.
            GadgetSounds real = GadgetSounds.Bind(game.Events, (sound, placed, position) => { });
            Assert.AreEqual(GadgetSounds.Named.Count + 2, real.Count);
            real.Unbind();
        }

        public struct FakePlateEvent
        {
            public Vector3 Position;
        }

        public struct FakeLeverEvent
        {
            public Prop Prop;
        }

        public sealed class FakeGadget
        {
            public Vector3 Position { get; set; }
        }

        public struct FakePedalEvent
        {
            public FakeGadget Gadget;
        }

        public sealed class FakeGadgetEvents
        {
            public event Action<FakePlateEvent> PlatePressed, PlateReleased, PlateRejected;
            public event Action<FakeLeverEvent> LeverPressed;
            public event Action<FakePedalEvent> PedalReleased;
            public event Action<int> Counted;
            public event Action Pressed;

            public void Press(FakePlateEvent e) => PlatePressed?.Invoke(e);
            public void Release(FakePlateEvent e) => PlateReleased?.Invoke(e);
            public void Other(FakePlateEvent e) => PlateRejected?.Invoke(e);
            public void Lever(FakeLeverEvent e) => LeverPressed?.Invoke(e);
            public void Pedal(FakePedalEvent e) => PedalReleased?.Invoke(e);
            public void Number(int n)
            {
                Counted?.Invoke(n);
                Pressed?.Invoke();
            }
        }

        [Test]
        public void Music_PlaysInThePresetsKeyAndTempo_AndFollowsTheGame()
        {
            Attach(environment: "night-light");
            LookAt(new Vector3(10f, 5f, 10f));
            MusicScore score = audio.Score;
            Assert.AreEqual(76, score.Bpm, "night is slower");
            Assert.IsTrue(score.Night);
            Assert.AreEqual(-1, score.Seed, "an ad-hoc level's id");
            Assert.IsFalse(audio.Music.Running);

            double started = clock + Sim.Dt;
            Present();
            Assert.IsTrue(audio.Music.Running);
            Wait(12f);
            // The first notes: pad and bass on the first step, 100 ms after the start.
            Assert.IsTrue(audio.Output.Last(audio.Output.Played - 1, out PlayedSound first));
            Assert.AreEqual(SoundId.Pad0, first.Id);
            Assert.IsTrue(first.Scheduled);
            Assert.AreEqual(started + MusicBox.StartDelay, first.Time, 1e-6);
            Assert.AreEqual(0.8f, first.Volume, 1e-5f);

            // Every note is on the grid of 15 / 76 s and every bell is a note of A minor pentatonic.
            double step = 15.0 / 76.0;
            int bells = 0;
            for (int back = 0; audio.Output.Last(back, out PlayedSound note); back++)
            {
                Assert.IsTrue(note.Scheduled, note.Id + " is music: scheduled on the audio clock");
                double steps = (note.Time - started - MusicBox.StartDelay) / step;
                Assert.AreEqual(Math.Round(steps), steps, 1e-6, note.Id + " is on the grid");
                Assert.IsFalse(note.Id == SoundId.KitKick || note.Id == SoundId.KitWood || note.Id == SoundId.KitShaker, "no kit before the first grab");
                if (!Sounds.IsBox(note.Id)) continue;
                bells++;
                int midi = Mathf.RoundToInt(60 + 3 * (note.Id - SoundId.Box0) + 12f * Mathf.Log(note.Pitch, 2f));
                Assert.IsTrue(audio.Key.Contains(midi), "the music box plays " + Scales.NoteName(midi));
            }
            Assert.Greater(bells, 10);
            Assert.LessOrEqual(audio.Output.MusicSounding, 12);
            Assert.AreEqual(1, audio.CurrentChord, "12 s in at 76 BPM (6.3 s to a chord): the second chord");
        }

        [Test]
        public void Music_RestsWhileHolding_GetsItsKitAfterTheFirstGrab_AndEndsWithTheLevel()
        {
            Attach(environment: "block-hall");
            Assert.AreEqual(new MusicKey(5, MusicScale.MajorPentatonic), audio.Key);
            Wait(3f);
            Assert.AreEqual(0, audio.Output.Count(SoundId.KitKick) + audio.Output.Count(SoundId.KitShaker));
            int bellsBefore = Bells();
            Assert.Greater(bellsBefore, 0);

            LookAt(block.Center);
            Tick(1);
            Click();
            Assert.IsTrue(game.Grabber.IsHolding);
            Tick(1);
            int bellsAtGrab = Bells();
            Tick(300);
            Assert.LessOrEqual(Bells() - bellsAtGrab, 1, "while holding the music box rests (a note already scheduled may still sound)");
            Assert.Greater(audio.Output.Count(SoundId.KitKick), 2, "the kit came in with the grab");
            Assert.Greater(audio.Output.Count(SoundId.KitWood), 2, "daytime: the whole kit");
            Assert.Greater(audio.Output.Count(SoundId.KitShaker), 8);
            // The hold tone is the lead: it sits on the pentatonic of whichever chord sounds.
            int chord = audio.CurrentChord;
            Chord c = audio.Key.Chord(chord);
            int semis = Mathf.RoundToInt(12f * Mathf.Log(audio.Output.HoldPitch, 2f));
            Assert.IsTrue(Scales.Contains(PitchLaw.HoldNote + semis, audio.Key.Root + c.Root, c.Pentatonic));

            Click();
            Tick(360);
            Assert.Greater(Bells(), bellsAtGrab + 1, "released: the music box plays again");

            // Level complete: the stinger, and the music fades out and stops.
            game.CompleteLevel();
            Assert.AreEqual(1, audio.Output.Count(SoundId.LevelComplete));
            PlayedSound stinger = Last(SoundId.LevelComplete);
            Assert.IsFalse(stinger.Spatial);
            Assert.AreEqual(1f, stinger.Pitch, "rendered in the key, not transposed");
            Assert.IsFalse(audio.Music.Running);
            int played = audio.Output.Played;
            Wait(0.5f);
            Assert.AreEqual(0f, audio.Output.MusicFade, "faded out in a quarter second");
            Assert.AreEqual(0, audio.Output.MusicSounding);
            Assert.AreEqual(played, audio.Output.Played, "and nothing more is scheduled");

            // The next level: its own tune from the top, the kit waiting for a grab again.
            game.LoadLevel(new SoundLevel(Room, "sunny-rug"));
            Assert.AreEqual(MusicKey.CMajor, audio.Key);
            Present();
            Assert.IsTrue(audio.Music.Running);
            Assert.IsFalse(audio.Music.Kit);
            Assert.AreEqual(1f, audio.Output.MusicFade);
            Assert.AreEqual(84, audio.Score.Bpm);
            Wait(1f);
            Assert.Less(audio.Music.NextStep, 10, "from the top");

            // A restart of the same level keeps its tune going.
            Wait(8f);
            int step = audio.Music.NextStep;
            game.RestartLevel();
            Present();
            Assert.IsTrue(audio.Music.Running);
            Assert.GreaterOrEqual(audio.Music.NextStep, step, "the tune carries on through a restart");
        }

        int Bells()
        {
            int bells = 0;
            for (int i = 0; i < Sounds.BoxCount; i++) bells += audio.Output.Count(SoundId.Box0 + i);
            return bells;
        }

        [Test]
        public void TheGamesSounds_PauseWithTheGame_AndMenuSoundsDoNot()
        {
            Attach(withFlow: true, options: new AudioPresenter.Options { Audible = false, Bank = AudioLab.Bank });
            Assert.AreEqual(FlowState.Playing, flow.State);
            Wait(2f);
            Assert.IsTrue(audio.Music.Running);
            int sectionStep = audio.Music.NextStep - audio.Music.NextStep % 32;

            // A long metal ring and a held toy, then Esc.
            LookAt(block.Center);
            Tick(1);
            Click();
            Tick(5);
            Assert.IsTrue(audio.Output.HoldPlaying);
            audio.Output.PlayAt(SoundId.LandMetal, Vector3.zero, 1f, 1f);
            int sounding = audio.Output.EffectsSounding;
            Assert.Greater(sounding, 0);

            Assert.IsTrue(flow.Pause());
            Assert.IsTrue(audio.Output.EffectsPaused, "the game's sounds wait");
            Assert.IsFalse(audio.Music.Running, "the music stops being scheduled");
            int played = audio.Output.Played;
            Wait(0.5f);
            Assert.AreEqual(0f, audio.Output.MusicFade, "and has faded out");
            Assert.AreEqual(0, audio.Output.MusicSounding);
            Wait(10f);
            Assert.AreEqual(sounding, audio.Output.EffectsSounding, "a paused sound does not run out while the game stands still");
            Assert.IsTrue(audio.Output.HoldPlaying, "the hold loop waits with them");
            Assert.AreEqual(played, audio.Output.Played);

            // Menus still click, and their sounds do not steal a waiting voice while others are free.
            audio.PlayUi(UiSound.Hover);
            audio.PlayUi(UiSound.Click);
            Assert.AreEqual(played + 2, audio.Output.Played);
            Assert.AreEqual(sounding + 2, audio.Output.EffectsSounding);
            Wait(1f);
            Assert.AreEqual(sounding, audio.Output.EffectsSounding, "the menu sounds ran out; the game's still wait");

            // Resume: everything picks up, the music from the start of its two-bar section.
            Assert.IsTrue(flow.Resume());
            Assert.IsFalse(audio.Output.EffectsPaused);
            Present();
            Assert.IsTrue(audio.Music.Running);
            Assert.AreEqual(1f, audio.Output.MusicFade);
            Assert.That(audio.Music.NextStep, Is.InRange(sectionStep, sectionStep + 4));
            Wait(8f);
            Assert.AreEqual(0, audio.Output.EffectsSounding, "the metal ring has rung out by now");

            // The title and the level select pause the game's sounds just the same.
            flow.Pause();
            flow.ReturnToTitle();
            Assert.IsTrue(audio.Output.EffectsPaused);
            Wait(0.5f);
            Assert.AreEqual(0, audio.Output.MusicSounding);
            // Starting a level from there drops what was waiting: it belonged to the level that is gone.
            flow.StartLevel(1);
            Assert.IsFalse(audio.Output.EffectsPaused);
            Present();
            Assert.IsTrue(audio.Music.Running);
            Wait(0.1f);
            Assert.IsFalse(audio.Output.HoldPlaying, "the hold loop of the level that is gone has stopped");
            Assert.AreEqual(0, audio.Output.EffectsSounding);
        }

        [Test]
        public void AFrame_AllocatesNothing()
        {
            // ART_BIBLE 12.2: zero managed allocation per frame in Audio. Music running, a toy held (the hold
            // tone gliding with the chords), then released (the focus query, the music box playing again).
            Attach(options: new AudioPresenter.Options { Audible = false, Bank = AudioLab.Bank });
            LookAt(block.Center);
            Tick(2);
            Click();
            Tick(30);
            Assert.IsTrue(audio.Output.HoldPlaying);
            Action frames = () =>
            {
                for (int i = 0; i < 900; i++)
                {
                    clock += Sim.Dt;
                    audio.Frame(Sim.Dt, 1f);
                }
            };
            // Once to warm up (lists at their capacity, every path compiled), then measured.
            frames();
            int played = audio.Output.Played;
            Assert.That(() => frames(), Is.Not.AllocatingGCMemory());
            Assert.Greater(audio.Output.Played, played + 50, "and it was not idle: fifteen seconds of music were scheduled");

            Click();
            Tick(30);
            frames();
            Assert.That(() => frames(), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Volumes_ComeFromTheSettings()
        {
            Attach();
            Assert.AreEqual(Settings.DefaultSfxVolume, audio.Output.SfxVolume);
            Assert.AreEqual(Settings.DefaultMusicVolume, audio.Output.MusicVolume);
            Settings.SfxVolume = 0.25f;
            Settings.MusicVolume = 0.5f;
            Assert.AreEqual(0.25f, audio.Output.SfxVolume);
            Assert.AreEqual(0.5f, audio.Output.MusicVolume);
            Settings.ResetToDefaults();
            Assert.AreEqual(1f, audio.Output.SfxVolume);
            Assert.AreEqual(0.7f, audio.Output.MusicVolume);
            float listener = AudioListener.volume;
            Settings.MasterVolume = 0.33f;
            Assert.AreEqual(listener, AudioListener.volume, "a silent run leaves the listener alone");
        }

        [Test]
        public void RealSources_AreSetUpAsTheArtBibleSays()
        {
            var bank = new SoundBank { MakeClips = true };
            bank.SetKey(MusicKey.CMajor, 84);
            bank.GenerateAll();
            try
            {
                Assume.That(bank.Clip(SoundId.Grab) != null, "this editor has no audio (AudioClip.Create failed)");
                float listener = AudioListener.volume;
                Attach(options: new AudioPresenter.Options { Audible = true, Muted = true, Bank = bank });
                Assert.IsTrue(audio.Output.Audible);
                Assert.AreEqual(Settings.MasterVolume, AudioListener.volume, 1e-6f, "the master volume is the listener's");
                Assert.AreEqual(0.8f, AudioListener.volume, 1e-6f, "AudioListener.volume = 0.8 by default");

                // Sources: 12 pooled for effects, 12 for music, 1 for the hold loop - all under the presentation's root.
                AudioSource[] sources = game.Root.GetComponentsInChildren<AudioSource>(true);
                Assert.AreEqual(25, sources.Length);
                foreach (AudioSource source in sources)
                {
                    Assert.IsTrue(source.transform.IsChildOf(presentation.Context.Root));
                    Assert.IsFalse(source.playOnAwake);
                    Assert.IsTrue(source.mute, "this test's sources are muted");
                    Assert.AreEqual(0f, source.dopplerLevel);
                    Assert.AreEqual(AudioRolloffMode.Linear, source.rolloffMode);
                    Assert.AreEqual(4f, source.minDistance);
                    Assert.AreEqual(160f, source.maxDistance);
                }
                AudioSource hold = audio.Output.Source(-1);
                Assert.IsTrue(hold.loop);
                Assert.AreEqual(0f, hold.spatialBlend, "the hold loop is never spatialised");

                // A landing: 3D at its point, the bank's clip, the law's pitch, the gain times the effect volume.
                Settings.SfxVolume = 0.5f;
                var point = new Vector3(3f, 0f, 7f);
                int voice = audio.PlayAt(SoundId.LandWood, point, 0.8f, 0.7f);
                AudioSource land = audio.Output.Source(voice);
                Assert.AreSame(bank.Clip(SoundId.LandWood), land.clip);
                Assert.AreEqual(1f, land.spatialBlend);
                Assert.AreEqual(point, land.transform.position);
                Assert.AreEqual(0.7f, land.pitch, 1e-5f);
                Assert.AreEqual(0.4f, land.volume, 1e-5f);
                Assert.IsFalse(land.loop);
                int flat = audio.Play(SoundId.Grab, 1f, 2f);
                Assert.AreNotEqual(voice, flat);
                Assert.AreEqual(0f, audio.Output.Source(flat).spatialBlend);
                Assert.AreSame(bank.Clip(SoundId.Grab), audio.Output.Source(flat).clip);
                Settings.SfxVolume = 1f;
                Assert.AreEqual(0.8f, land.volume, 1e-5f, "a volume change reaches sounds that are already playing");

                // The law's top is two octaves up: an AudioSource set to 4 from code keeps it (-3..3 is the inspector's range).
                land.pitch = PitchLaw.MaxPitch;
                Assert.AreEqual(4f, land.pitch, 1e-5f);

                // Music: scheduled on the music voices, at the music volume.
                Present();
                Wait(0.5f);
                Assert.IsTrue(audio.Music.Running);
                AudioSource pad = null;
                for (int i = 0; i < AudioOutput.MusicVoices; i++)
                    if (audio.Output.Source(100 + i).clip == bank.Clip(SoundId.Pad0)) pad = audio.Output.Source(100 + i);
                Assert.IsNotNull(pad, "the first chord's pad is on a music voice");
                Assert.AreEqual(0.8f * Settings.MusicVolume, pad.volume, 1e-5f);
                Assert.AreEqual(0f, pad.spatialBlend);

                // The hold loop, through the game.
                LookAt(block.Center);
                Tick(1);
                Click();
                Tick(4);
                Assert.AreSame(bank.Clip(SoundId.Hold), hold.clip);
                Assert.AreEqual(1f, hold.volume, 1e-4f);
                Assert.AreEqual(audio.Output.HoldPitch, hold.pitch, 1e-5f);

                presentation.Dispose();
                presentation = null;
                Assert.AreEqual(listener, AudioListener.volume, 1e-6f, "the listener's volume is put back");
                Assert.AreEqual(0, game.Root.GetComponentsInChildren<AudioSource>(true).Length, "and the sources are gone");
            }
            finally
            {
                presentation?.Dispose();
                presentation = null;
                bank.Dispose();
            }
        }

        [Test]
        public void Sound_NeverTouchesTheSimulation()
        {
            // The same two seconds of a falling stack with the full audio path running and without it.
            Vector3[] Fall(bool withAudio)
            {
                var props = new List<Prop>();
                Action<LevelContext> build = ctx =>
                {
                    TestHelpers.Floor(ctx, 40f);
                    for (int i = 0; i < 5; i++) props.Add(ctx.AddProp(BasicToys.Block(1f), new Vector3(0.3f * i, 2f + 1.6f * i, 5f)));
                    ctx.SetSpawn(Vector3.zero, 0f);
                };
                UnityEngine.Random.State before = UnityEngine.Random.state;
                if (withAudio)
                {
                    Attach(build, "pegboard-workbench", options: new AudioPresenter.Options { Audible = false, Bank = new SoundBank { KeepSamples = true } });
                }
                else
                {
                    input = new ScriptedInput();
                    game = Game.Create(new GameOptions { Input = input });
                    game.LoadLevel(new SoundLevel(build, "pegboard-workbench"));
                }
                uint dice = game.Rng.NextUInt();
                var reference = new Rng(game.Rng.Seed);
                Assert.AreEqual(reference.NextUInt(), dice, "building the level and attaching audio drew nothing from the game's dice");
                for (int i = 0; i < 120; i++)
                {
                    game.Tick();
                    if (withAudio) Present();
                }
                Assert.AreEqual(reference.NextUInt(), game.Rng.NextUInt(), "nor did two seconds of sound (generation included)");
                Assert.AreEqual(JsonUtility.ToJson(before), JsonUtility.ToJson(UnityEngine.Random.state), "UnityEngine.Random is not used either");
                var poses = new Vector3[props.Count];
                for (int i = 0; i < poses.Length; i++) poses[i] = props[i].Position;
                if (withAudio)
                {
                    Assert.Greater(audio.Output.Played, 0, "there was sound");
                    Assert.Greater(audio.Bank.Generated, 0, "and the bank was generating in slices meanwhile");
                    audio.Bank.Dispose();
                }
                presentation?.Dispose();
                presentation = null;
                game.Dispose();
                game = null;
                return poses;
            }

            Vector3[] silent = Fall(false), loud = Fall(true);
            for (int i = 0; i < silent.Length; i++)
                Assert.IsTrue(silent[i] == loud[i] && silent[i].x.Equals(loud[i].x) && silent[i].y.Equals(loud[i].y) && silent[i].z.Equals(loud[i].z), "block " + i + " ends where it ends without sound, bit for bit");
        }
    }
}
