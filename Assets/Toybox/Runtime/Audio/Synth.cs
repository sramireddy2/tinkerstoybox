using System;

namespace Toybox.Audio
{
    public enum Wave
    {
        Sine,
        Triangle,
        Saw,
        Square,
    }

    /// <summary>
    /// The synthesizer's own random numbers (xorshift32 over a scrambled seed): seeded white noise and the
    /// music's dice. It is never the simulation's Rng and never UnityEngine.Random, so generating sound can
    /// not change how a level plays, and the same seed always gives the same samples.
    /// </summary>
    public struct SynthRng
    {
        uint state;

        public SynthRng(uint seed)
        {
            state = Scramble(seed);
        }

        /// <summary>A seed for a second stream that is unrelated to the first.</summary>
        public static uint Mix(uint a, uint b) => Scramble(unchecked(a * 0x9E3779B1u + b + 0x7F4A7C15u));

        static uint Scramble(uint seed)
        {
            uint z = unchecked(seed + 0x9E3779B9u);
            z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
            z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
            z ^= z >> 16;
            return z == 0 ? 0x6D2B79F5u : z;
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Value() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform in [-1, 1): one sample of white noise.</summary>
        public float Signed() => (int)NextUInt() * (1f / 2147483648f);

        /// <summary>Uniform integer in [0, count).</summary>
        public int Range(int count) => count <= 1 ? 0 : (int)(NextUInt() % (uint)count);
    }

    /// <summary>One oscillator voice: a wave at a pitch with an exponential decay and an optional pitch glide.</summary>
    public struct Tone
    {
        public Wave Wave;
        public float Hz;
        /// <summary>Amplitude at the start (before the decay).</summary>
        public float Gain;
        /// <summary>Exponential decay time constant in seconds (amplitude falls to 1/e); 0 for none.</summary>
        public float Decay;
        /// <summary>Linear fade-in, seconds.</summary>
        public float Attack;
        /// <summary>The pitch moves by this many semitones over the first <see cref="GlideSeconds"/>, evenly in semitones, and stays there.</summary>
        public float GlideSemis, GlideSeconds;
        /// <summary>Start phase in cycles.</summary>
        public float Phase;

        public Tone(Wave wave, float hz, float gain, float decay = 0f)
        {
            Wave = wave;
            Hz = hz;
            Gain = gain;
            Decay = decay;
            Attack = 0f;
            GlideSemis = 0f;
            GlideSeconds = 0f;
            Phase = 0f;
        }
    }

    /// <summary>One FM operator pair: a sine carrier whose phase is modulated by a sine at <c>Hz x Ratio</c>.</summary>
    public struct FmTone
    {
        public float Hz;
        /// <summary>Modulator frequency / carrier frequency.</summary>
        public float Ratio;
        /// <summary>The modulation index falls evenly from this to 0 over <see cref="IndexSeconds"/>.</summary>
        public float Index, IndexSeconds;
        public float Gain;
        /// <summary>Exponential decay time constant of the amplitude, seconds.</summary>
        public float Decay;

        public FmTone(float hz, float ratio, float index, float indexSeconds, float decay, float gain = 1f)
        {
            Hz = hz;
            Ratio = ratio;
            Index = index;
            IndexSeconds = indexSeconds;
            Decay = decay;
            Gain = gain;
        }
    }

    /// <summary>A second-order filter section (RBJ cookbook), direct form 1, with its state.</summary>
    public struct Biquad
    {
        double b0, b1, b2, a1, a2;
        double x1, x2, y1, y2;

        public static Biquad LowPass(int rate, float hz, float q)
        {
            Angles(rate, hz, q, out double cos, out double alpha);
            return Make((1 - cos) * 0.5, 1 - cos, (1 - cos) * 0.5, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Biquad HighPass(int rate, float hz, float q)
        {
            Angles(rate, hz, q, out double cos, out double alpha);
            return Make((1 + cos) * 0.5, -(1 + cos), (1 + cos) * 0.5, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>Band-pass with a peak gain of 1 at <paramref name="hz"/>; the band is hz / q wide.</summary>
        public static Biquad BandPass(int rate, float hz, float q)
        {
            Angles(rate, hz, q, out double cos, out double alpha);
            return Make(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
        }

        /// <summary>The same filter retuned, keeping the state: for sweeps.</summary>
        public void Retune(in Biquad coefficients)
        {
            b0 = coefficients.b0;
            b1 = coefficients.b1;
            b2 = coefficients.b2;
            a1 = coefficients.a1;
            a2 = coefficients.a2;
        }

        static void Angles(int rate, float hz, float q, out double cos, out double alpha)
        {
            double w = 2 * Math.PI * Math.Min(hz, rate * 0.49) / rate;
            cos = Math.Cos(w);
            alpha = Math.Sin(w) / (2 * Math.Max(q, 0.05));
        }

        static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            return new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        public float Step(float x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return (float)y;
        }

        public void Run(float[] buffer, int start, int count)
        {
            int end = Math.Min(buffer.Length, start + count);
            double lb0 = b0, lb1 = b1, lb2 = b2, la1 = a1, la2 = a2, lx1 = x1, lx2 = x2, ly1 = y1, ly2 = y2;
            for (int i = Math.Max(0, start); i < end; i++)
            {
                double x = buffer[i];
                double y = lb0 * x + lb1 * lx1 + lb2 * lx2 - la1 * ly1 - la2 * ly2;
                lx2 = lx1;
                lx1 = x;
                ly2 = ly1;
                ly1 = y;
                buffer[i] = (float)y;
            }
            x1 = lx1;
            x2 = lx2;
            y1 = ly1;
            y2 = ly2;
        }
    }

    /// <summary>
    /// The baked room (ART_BIBLE 11.1): a Schroeder reverb - four parallel combs (29.7, 37.1, 41.1 and
    /// 43.7 ms, feedback for the asked RT60, a 4 kHz one-pole low-pass in the feedback path) into two
    /// all-passes in series (5.0 and 1.7 ms, gain 0.7). Linear time, and it can be fed in pieces, so a long
    /// clip need not be reverberated in one frame. An impulse comes out with about unit energy, so the
    /// wet level is the reverberant-to-direct amplitude ratio.
    /// </summary>
    public sealed class SchroederReverb
    {
        public const float DefaultRt60 = 1.4f;
        public const float DampHz = 4000f;
        static readonly float[] CombMs = { 29.7f, 37.1f, 41.1f, 43.7f };
        static readonly float[] AllPassMs = { 5.0f, 1.7f };
        const double AllPassGain = 0.7;
        const int MaxBlock = 256;

        readonly float[][] combs = new float[4][];
        readonly double[] combGain = new double[4];
        readonly double[] combStore = new double[4];
        readonly int[] combAt = new int[4];
        readonly float[][] allPasses = new float[2][];
        readonly int[] allPassAt = new int[2];
        readonly double damp;
        readonly int block;
        // One block of input and of the combs' sum. Doubles: the editor's runtime computes in doubles
        // anyway, and converting at every step costs more than the arithmetic.
        readonly double[] input, mix;

        public SchroederReverb(int rate, float rt60 = DefaultRt60)
        {
            int shortest = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int length = Math.Max(1, (int)Math.Round(CombMs[i] * 0.001 * rate));
                combs[i] = new float[length];
                // A loop of delay D loses 60 dB in RT60 seconds: g = 10^(-3 D / RT60).
                combGain[i] = Math.Pow(10.0, -3.0 * length / rate / Math.Max(rt60, 0.01f));
                shortest = Math.Min(shortest, length);
            }
            for (int i = 0; i < 2; i++)
                allPasses[i] = new float[Math.Max(1, (int)Math.Round(AllPassMs[i] * 0.001 * rate))];
            damp = Math.Exp(-2.0 * Math.PI * DampHz / rate);
            // A comb never reads what it wrote within one block as long as the block is no longer than its delay.
            block = Math.Min(MaxBlock, shortest);
            input = new double[block];
            mix = new double[block];
        }

        /// <summary>
        /// Writes <c>dry + wet x reverb(dry)</c> for the samples [start, start + count) into
        /// <paramref name="output"/>. Samples of <paramref name="dry"/> past its end count as silence, which
        /// is how the tail is rendered. Call with consecutive ranges; the result does not depend on how the
        /// ranges are cut.
        /// </summary>
        public void Process(float[] dry, float[] output, int start, int count, float wet)
        {
            int end = Math.Min(output.Length, start + count);
            int dryLength = dry.Length;
            double keep = 1.0 - damp, lose = damp, wetGain = wet;
            float[] a0 = allPasses[0], a1 = allPasses[1];

            for (int at = Math.Max(0, start); at < end; at += block)
            {
                int n = Math.Min(block, end - at);
                int live = Math.Min(n, dryLength - at);
                for (int i = 0; i < n; i++)
                {
                    input[i] = i < live ? dry[at + i] : 0.0;
                    mix[i] = 0.0;
                }

                for (int c = 0; c < 4; c++)
                {
                    float[] line = combs[c];
                    double gain = combGain[c], store = combStore[c];
                    int p = combAt[c], done = 0;
                    while (done < n)
                    {
                        // Up to the end of the delay line, then around.
                        int run = Math.Min(n - done, line.Length - p);
                        for (int k = 0; k < run; k++)
                        {
                            double echo = line[p + k];
                            store = echo * keep + store * lose;
                            line[p + k] = (float)(input[done + k] + store * gain);
                            mix[done + k] += echo;
                        }
                        done += run;
                        p += run;
                        if (p == line.Length) p = 0;
                    }
                    combStore[c] = store;
                    combAt[c] = p;
                }

                int q0 = allPassAt[0], q1 = allPassAt[1];
                for (int i = 0; i < n; i++)
                {
                    double sum = mix[i] * 0.25;
                    double v0 = a0[q0];
                    double y0 = v0 - AllPassGain * sum;
                    a0[q0] = (float)(sum + AllPassGain * y0);
                    if (++q0 == a0.Length) q0 = 0;

                    double v1 = a1[q1];
                    double y1 = v1 - AllPassGain * y0;
                    a1[q1] = (float)(y0 + AllPassGain * y1);
                    if (++q1 == a1.Length) q1 = 0;

                    output[at + i] = (float)(input[i] + wetGain * y1);
                }
                allPassAt[0] = q0;
                allPassAt[1] = q1;
            }
        }
    }

    /// <summary>
    /// The synthesizer (ART_BIBLE 11.1): oscillators by phase accumulation, seeded white noise, an FM
    /// operator, exponential envelopes, one-pole and biquad filters, pitch glide, the baked reverb and peak
    /// normalising. Pure functions on <c>float[]</c>: no Unity types, no state, no clock - what they write
    /// depends on their arguments alone, so a clip is the same every time it is generated.
    /// Everything ADDS into the buffer unless it says otherwise.
    /// </summary>
    public static class Synth
    {
        /// <summary>Effects and the music box.</summary>
        public const int Rate = 44100;
        /// <summary>Pads and bass.</summary>
        public const int LowRate = 22050;

        const int TableBits = 12;
        const int TableSize = 1 << TableBits;
        static readonly double[] SineTable = BuildSineTable();

        static double[] BuildSineTable()
        {
            var table = new double[TableSize + 1];
            for (int i = 0; i <= TableSize; i++) table[i] = Math.Sin(2.0 * Math.PI * i / TableSize);
            return table;
        }

        // ---- Units ------------------------------------------------------------------------------------

        /// <summary>Decibels to an amplitude factor (-6 dB is about 0.5).</summary>
        public static float Gain(float decibels) => (float)Math.Pow(10.0, decibels / 20.0);

        /// <summary>An amplitude factor in decibels (silence is -200).</summary>
        public static float Decibels(float gain) => gain > 1e-10f ? (float)(20.0 * Math.Log10(gain)) : -200f;

        /// <summary>The frequency ratio of a number of semitones.</summary>
        public static float Ratio(float semitones) => (float)Math.Pow(2.0, semitones / 12.0);

        /// <summary>The frequency of a MIDI note number (69 is A4, 440 Hz; 60 is C4).</summary>
        public static float NoteHz(float midi) => (float)(440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0));

        public static int Samples(float seconds, int rate) => Math.Max(0, (int)Math.Round((double)seconds * rate));

        public static float[] Buffer(float seconds, int rate) => new float[Math.Max(1, Samples(seconds, rate))];

        /// <summary>The sine of a phase given in cycles, from a 4096-point table (error below -120 dB).</summary>
        public static float Sin(double cycles)
        {
            cycles -= (long)cycles;
            if (cycles < 0) cycles += 1.0;
            // A phase a hair below zero rounds up to exactly one.
            if (cycles >= 1.0) cycles = 0.0;
            double x = cycles * TableSize;
            int i = (int)x;
            double a = SineTable[i];
            return (float)(a + (SineTable[i + 1] - a) * (x - i));
        }

        // ---- Oscillators --------------------------------------------------------------------------------

        /// <summary>Adds an oscillator voice to [start, start + count).</summary>
        public static void Add(float[] buffer, int start, int count, int rate, in Tone tone)
        {
            int end = Math.Min(buffer.Length, start + count);
            start = Math.Max(0, start);
            if (end <= start || tone.Gain == 0f || !(tone.Hz > 0f)) return;

            int attack = Samples(tone.Attack, rate);
            double decay = tone.Decay > 0f ? Math.Exp(-1.0 / ((double)tone.Decay * rate)) : 1.0;
            int glide = tone.GlideSemis != 0f ? Samples(tone.GlideSeconds, rate) : 0;

            if (tone.Wave == Wave.Sine && glide == 0)
            {
                // A fixed-pitch decaying sine is a two-pole resonator: two multiplies a sample, no table.
                double w = 2.0 * Math.PI * tone.Hz / rate;
                double phase0 = 2.0 * Math.PI * tone.Phase;
                double k1 = 2.0 * decay * Math.Cos(w), k2 = decay * decay;
                double y2 = Math.Sin(phase0 - w) / decay * tone.Gain;
                double y1 = Math.Sin(phase0) * tone.Gain;
                int at = start;
                if (attack <= 0) buffer[at] += (float)y1;
                at++;
                // The faded-in start, then the bare recurrence.
                int ramped = Math.Min(end, start + attack);
                for (; at < ramped; at++)
                {
                    double y = k1 * y1 - k2 * y2;
                    y2 = y1;
                    y1 = y;
                    buffer[at] += (float)(y * (at - start) / attack);
                }
                for (; at < end; at++)
                {
                    double y = k1 * y1 - k2 * y2;
                    y2 = y1;
                    y1 = y;
                    buffer[at] += (float)y;
                }
                return;
            }

            double phase = tone.Phase - Math.Floor(tone.Phase);
            double step = (double)tone.Hz / rate;
            double glideRatio = glide > 0 ? Math.Pow(2.0, tone.GlideSemis / 12.0 / glide) : 1.0;
            double envelope = tone.Gain;

            // The start, where the attack fades in and the pitch glides: one sample at a time, any wave.
            int shaped = Math.Min(end, start + Math.Max(attack, glide));
            int i = start;
            for (; i < shaped; i++)
            {
                int n = i - start;
                double gain = n < attack ? envelope * n / attack : envelope;
                buffer[i] += (float)(Sample(tone.Wave, phase, step) * gain);
                envelope *= decay;
                phase += step;
                if (phase >= 1.0) phase -= 1.0;
                if (n < glide) step *= glideRatio;
            }

            // The rest at a steady pitch: a tight loop per wave.
            switch (tone.Wave)
            {
                case Wave.Sine:
                    for (; i < end; i++)
                    {
                        double x = phase * TableSize;
                        int index = (int)x;
                        double a = SineTable[index];
                        buffer[i] += (float)((a + (SineTable[index + 1] - a) * (x - index)) * envelope);
                        envelope *= decay;
                        phase += step;
                        if (phase >= 1.0) phase -= 1.0;
                    }
                    break;
                case Wave.Triangle:
                    for (; i < end; i++)
                    {
                        // Starts at zero and rises, like the sine.
                        double p = phase + 0.25;
                        if (p >= 1.0) p -= 1.0;
                        p -= 0.5;
                        buffer[i] += (float)((1.0 - 4.0 * (p < 0.0 ? -p : p)) * envelope);
                        envelope *= decay;
                        phase += step;
                        if (phase >= 1.0) phase -= 1.0;
                    }
                    break;
                case Wave.Saw:
                {
                    double upper = 1.0 - step;
                    for (; i < end; i++)
                    {
                        double value = 2.0 * phase - 1.0;
                        if (phase < step || phase > upper) value -= PolyBlep(phase, step);
                        buffer[i] += (float)(value * envelope);
                        envelope *= decay;
                        phase += step;
                        if (phase >= 1.0) phase -= 1.0;
                    }
                    break;
                }
                default:
                    for (; i < end; i++)
                    {
                        buffer[i] += (float)(Sample(Wave.Square, phase, step) * envelope);
                        envelope *= decay;
                        phase += step;
                        if (phase >= 1.0) phase -= 1.0;
                    }
                    break;
            }
        }

        // One sample of a wave at a phase in [0, 1).
        static double Sample(Wave wave, double phase, double step)
        {
            switch (wave)
            {
                case Wave.Sine:
                {
                    double x = phase * TableSize;
                    int index = (int)x;
                    double a = SineTable[index];
                    return a + (SineTable[index + 1] - a) * (x - index);
                }
                case Wave.Triangle:
                {
                    double p = phase + 0.25;
                    if (p >= 1.0) p -= 1.0;
                    return 1.0 - 4.0 * Math.Abs(p - 0.5);
                }
                case Wave.Saw:
                    return 2.0 * phase - 1.0 - PolyBlep(phase, step);
                default:
                {
                    double half = phase + 0.5;
                    if (half >= 1.0) half -= 1.0;
                    return (phase < 0.5 ? 1.0 : -1.0) + PolyBlep(phase, step) - PolyBlep(half, step);
                }
            }
        }

        // The band-limited step: smooths the jump of a saw or square over the two samples around it.
        static double PolyBlep(double phase, double step)
        {
            if (phase < step)
            {
                double t = phase / step;
                return t + t - t * t - 1.0;
            }
            if (phase > 1.0 - step)
            {
                double t = (phase - 1.0) / step;
                return t * t + t + t + 1.0;
            }
            return 0.0;
        }

        /// <summary>
        /// Adds three saws - one at <paramref name="hz"/>, one <paramref name="cents"/> above and one as far
        /// below - each a third of <paramref name="gain"/>: the pad's voice, in one pass.
        /// </summary>
        public static void AddSaws(float[] buffer, int start, int count, int rate, float hz, float cents, float gain, float phase = 0f)
        {
            int end = Math.Min(buffer.Length, start + count);
            if (!(hz > 0f) || gain == 0f) return;
            double detune = Math.Pow(2.0, cents / 1200.0);
            double s0 = (double)hz / rate, s1 = s0 * detune, s2 = s0 / detune;
            // Spread the three over the cycle so they do not all jump at once.
            double p0 = phase - Math.Floor(phase), p1 = p0 + 0.37, p2 = p0 + 0.71;
            if (p1 >= 1.0) p1 -= 1.0;
            if (p2 >= 1.0) p2 -= 1.0;
            double u0 = 1.0 - s0, u1 = 1.0 - s1, u2 = 1.0 - s2;
            double each = gain / 3.0;
            for (int i = Math.Max(0, start); i < end; i++)
            {
                double value = 2.0 * (p0 + p1 + p2) - 3.0;
                if (p0 < s0 || p0 > u0) value -= PolyBlep(p0, s0);
                if (p1 < s1 || p1 > u1) value -= PolyBlep(p1, s1);
                if (p2 < s2 || p2 > u2) value -= PolyBlep(p2, s2);
                buffer[i] += (float)(value * each);
                p0 += s0;
                if (p0 >= 1.0) p0 -= 1.0;
                p1 += s1;
                if (p1 >= 1.0) p1 -= 1.0;
                p2 += s2;
                if (p2 >= 1.0) p2 -= 1.0;
            }
        }

        /// <summary>Adds an FM voice to [start, start + count).</summary>
        public static void Add(float[] buffer, int start, int count, int rate, in FmTone tone)
        {
            int end = Math.Min(buffer.Length, start + count);
            start = Math.Max(0, start);
            if (end <= start || tone.Gain == 0f || !(tone.Hz > 0f)) return;

            double carrier = 0, modulator = 0;
            double carrierStep = (double)tone.Hz / rate, modulatorStep = carrierStep * tone.Ratio;
            modulatorStep -= Math.Floor(modulatorStep);
            double decay = tone.Decay > 0f ? Math.Exp(-1.0 / ((double)tone.Decay * rate)) : 1.0;
            double envelope = tone.Gain;
            int indexSamples = Math.Max(1, Samples(tone.IndexSeconds, rate));
            // The index is in radians; the table takes cycles.
            double indexCycles = tone.Index / (2.0 * Math.PI);
            double indexStep = indexCycles / indexSamples;
            double index = indexCycles;

            // While the index is up: carrier phase modulated by the modulator.
            int modulated = Math.Min(end, start + indexSamples);
            int i = start;
            for (; i < modulated; i++)
            {
                double mx = modulator * TableSize;
                int mi = (int)mx;
                double ma = SineTable[mi];
                double phase = carrier + index * (ma + (SineTable[mi + 1] - ma) * (mx - mi));
                phase -= (long)phase;
                if (phase < 0.0) phase += 1.0;
                if (phase >= 1.0) phase = 0.0;
                double x = phase * TableSize;
                int xi = (int)x;
                double a = SineTable[xi];
                buffer[i] += (float)((a + (SineTable[xi + 1] - a) * (x - xi)) * envelope);

                envelope *= decay;
                index -= indexStep;
                carrier += carrierStep;
                if (carrier >= 1.0) carrier -= 1.0;
                modulator += modulatorStep;
                if (modulator >= 1.0) modulator -= 1.0;
            }
            // After it: a plain decaying sine, carrying on at the carrier's phase.
            for (; i < end; i++)
            {
                double x = carrier * TableSize;
                int xi = (int)x;
                double a = SineTable[xi];
                buffer[i] += (float)((a + (SineTable[xi + 1] - a) * (x - xi)) * envelope);
                envelope *= decay;
                carrier += carrierStep;
                if (carrier >= 1.0) carrier -= 1.0;
            }
        }

        /// <summary>Adds white noise with an exponential decay (<paramref name="decay"/> 0: none).</summary>
        public static void AddNoise(float[] buffer, int start, int count, int rate, ref SynthRng rng, float gain, float decay = 0f)
        {
            int end = Math.Min(buffer.Length, start + count);
            start = Math.Max(0, start);
            double k = decay > 0f ? Math.Exp(-1.0 / ((double)decay * rate)) : 1.0;
            double envelope = gain;
            for (int i = start; i < end; i++)
            {
                buffer[i] += (float)(rng.Signed() * envelope);
                envelope *= k;
            }
        }

        /// <summary>Adds <paramref name="source"/> into <paramref name="buffer"/> at <paramref name="start"/>, scaled.</summary>
        public static void Mix(float[] buffer, int start, float[] source, float gain = 1f)
        {
            int count = Math.Min(source.Length, buffer.Length - start);
            for (int i = Math.Max(0, -start); i < count; i++) buffer[start + i] += source[i] * gain;
        }

        // ---- Envelopes ------------------------------------------------------------------------------------

        /// <summary>Multiplies [start, start + count) by a ramp from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void Ramp(float[] buffer, int start, int count, float from, float to)
        {
            int end = Math.Min(buffer.Length, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
            {
                float t = count > 1 ? (float)(i - start) / (count - 1) : 1f;
                buffer[i] *= from + (to - from) * t;
            }
        }

        /// <summary>Fades the last <paramref name="seconds"/> of [0, length) to silence and clears what follows.</summary>
        public static void FadeOut(float[] buffer, int length, float seconds, int rate)
        {
            length = Math.Min(length, buffer.Length);
            int fade = Math.Min(length, Samples(seconds, rate));
            Ramp(buffer, length - fade, fade, 1f, 0f);
            for (int i = length; i < buffer.Length; i++) buffer[i] = 0f;
        }

        /// <summary>Multiplies [start, start + count) by a Hann window: a click-free burst.</summary>
        public static void Window(float[] buffer, int start, int count)
        {
            int end = Math.Min(buffer.Length, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
                buffer[i] *= (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (i - start + 0.5) / count));
        }

        /// <summary>Multiplies the whole buffer.</summary>
        public static void Scale(float[] buffer, float gain) => Scale(buffer, buffer.Length, gain);

        /// <summary>Multiplies the first <paramref name="length"/> samples.</summary>
        public static void Scale(float[] buffer, int length, float gain)
        {
            length = Math.Min(length, buffer.Length);
            for (int i = 0; i < length; i++) buffer[i] *= gain;
        }

        // ---- Filters (in place) ----------------------------------------------------------------------------

        /// <summary>One-pole low-pass, 6 dB per octave above <paramref name="hz"/>.</summary>
        public static void LowPass(float[] buffer, int start, int count, int rate, float hz)
        {
            double a = Math.Exp(-2.0 * Math.PI * hz / rate), keep = 1.0 - a;
            double state = 0;
            int end = Math.Min(buffer.Length, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
            {
                state = buffer[i] * keep + state * a;
                buffer[i] = (float)state;
            }
        }

        /// <summary>One-pole high-pass, 6 dB per octave below <paramref name="hz"/>.</summary>
        public static void HighPass(float[] buffer, int start, int count, int rate, float hz)
        {
            double a = Math.Exp(-2.0 * Math.PI * hz / rate), keep = 1.0 - a;
            double state = 0;
            int end = Math.Min(buffer.Length, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
            {
                double x = buffer[i];
                state = x * keep + state * a;
                buffer[i] = (float)(x - state);
            }
        }

        public static void Filter(float[] buffer, int start, int count, Biquad filter) => filter.Run(buffer, start, count);

        /// <summary>
        /// A band-pass whose centre moves from <paramref name="fromHz"/> to <paramref name="toHz"/> over the
        /// range, evenly in pitch (the peel of the grab sound). Retuned every 16 samples.
        /// </summary>
        public static void SweptBandPass(float[] buffer, int start, int count, int rate, float fromHz, float toHz, float q)
        {
            const int Block = 16;
            Biquad filter = Biquad.BandPass(rate, fromHz, q);
            int end = Math.Min(buffer.Length, start + count);
            for (int at = Math.Max(0, start); at < end; at += Block)
            {
                double t = count > 0 ? (double)(at - start) / count : 0.0;
                filter.Retune(Biquad.BandPass(rate, (float)(fromHz * Math.Pow(toHz / fromHz, t)), q));
                filter.Run(buffer, at, Math.Min(Block, end - at));
            }
        }

        // ---- Reverb and mastering ---------------------------------------------------------------------------

        /// <summary>
        /// The dry signal plus <paramref name="wet"/> of the baked room, with <paramref name="tailSeconds"/>
        /// of extra length for the tail. A new buffer.
        /// </summary>
        public static float[] Reverb(float[] dry, int rate, float wet, float tailSeconds = SchroederReverb.DefaultRt60, float rt60 = SchroederReverb.DefaultRt60)
        {
            var output = new float[dry.Length + Samples(tailSeconds, rate)];
            new SchroederReverb(rate, rt60).Process(dry, output, 0, output.Length, wet);
            return output;
        }

        /// <summary>
        /// The same for a seamless loop: the room is fed the loop twice over and the second round is kept,
        /// so the tail of the loop's end is already under its start (what the first round still lacks of
        /// that is 85 dB down).
        /// </summary>
        public static float[] ReverbLoop(float[] loop, int rate, float wet, float rt60 = SchroederReverb.DefaultRt60)
        {
            int n = loop.Length;
            var room = new SchroederReverb(rate, rt60);
            var output = new float[n];
            room.Process(loop, output, 0, n, wet);
            room.Process(loop, output, 0, n, wet);
            return output;
        }

        public static float Peak(float[] buffer)
        {
            float peak = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float a = buffer[i] < 0f ? -buffer[i] : buffer[i];
                if (a > peak) peak = a;
            }
            return peak;
        }

        /// <summary>Scales the buffer so that its largest sample is at <paramref name="peakDb"/> dBFS.</summary>
        public static void Normalise(float[] buffer, float peakDb)
        {
            float peak = Peak(buffer);
            if (peak > 1e-9f) Scale(buffer, Gain(peakDb) / peak);
        }

        /// <summary>
        /// Cuts the tail where the signal has fallen below <paramref name="floorDb"/> dBFS for good, with a
        /// short fade so the cut is not a click. A new buffer of exactly the kept length.
        /// </summary>
        public static float[] TrimTail(float[] buffer, int rate, float floorDb = -50f, float fadeSeconds = 0.008f) =>
            TrimTail(buffer, buffer.Length, rate, floorDb, fadeSeconds);

        /// <summary>The same for the first <paramref name="length"/> samples of a larger (scratch) buffer.</summary>
        public static float[] TrimTail(float[] buffer, int length, int rate, float floorDb = -50f, float fadeSeconds = 0.008f)
        {
            length = Math.Min(length, buffer.Length);
            float floor = Gain(floorDb);
            int last = length - 1;
            while (last > 0 && Math.Abs(buffer[last]) < floor) last--;
            int kept = Math.Max(1, Math.Min(length, last + 1 + Samples(fadeSeconds, rate)));
            var trimmed = new float[kept];
            Array.Copy(buffer, trimmed, kept);
            FadeOut(trimmed, kept, fadeSeconds, rate);
            return trimmed;
        }

        /// <summary>
        /// Dry signal to finished clip: the baked room, the peak level, the cut tail.
        /// </summary>
        public static float[] Master(float[] dry, int rate, float wet, float peakDb, float floorDb = -50f)
        {
            float[] wetted = wet > 0f ? Reverb(dry, rate, wet) : dry;
            Normalise(wetted, peakDb);
            return TrimTail(wetted, rate, floorDb);
        }
    }
}
