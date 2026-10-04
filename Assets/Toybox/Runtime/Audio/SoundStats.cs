using System;

namespace Toybox.Audio
{
    /// <summary>
    /// Measurements on a buffer of samples: level, spectrum, pitch and decay. Nobody can listen to a clip in
    /// a headless run, so this is how the recipes are checked - the tests hold every sound against the
    /// numbers of its recipe with these - and how the bank reports what it made.
    /// </summary>
    public static class SoundStats
    {
        /// <summary>True if no sample is NaN or infinite.</summary>
        public static bool Finite(float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i])) return false;
            return true;
        }

        public static float Peak(float[] buffer) => Synth.Peak(buffer);

        public static float PeakDb(float[] buffer) => Synth.Decibels(Synth.Peak(buffer));

        /// <summary>Root mean square of [start, start + count); the whole buffer by default.</summary>
        public static float Rms(float[] buffer, int start = 0, int count = -1)
        {
            Clip(buffer, ref start, ref count);
            if (count <= 0) return 0f;
            double sum = 0;
            for (int i = start; i < start + count; i++) sum += (double)buffer[i] * buffer[i];
            return (float)Math.Sqrt(sum / count);
        }

        public static float RmsDb(float[] buffer, int start = 0, int count = -1) => Synth.Decibels(Rms(buffer, start, count));

        /// <summary>The largest jump between neighbouring samples in [start, start + count).</summary>
        public static float MaxStep(float[] buffer, int start = 0, int count = -1)
        {
            Clip(buffer, ref start, ref count);
            float largest = 0f;
            for (int i = start + 1; i < start + count; i++) largest = Math.Max(largest, Math.Abs(buffer[i] - buffer[i - 1]));
            return largest;
        }

        /// <summary>
        /// Magnitudes of the discrete Fourier transform of [start, start + count), Hann-windowed and
        /// zero-padded to a power of two of at least <paramref name="minSize"/>. Bin k is at
        /// <c>k x rate / (2 x (length - 1))</c> Hz; the result has size / 2 + 1 bins.
        /// </summary>
        public static double[] Spectrum(float[] buffer, int start = 0, int count = -1, int minSize = 4096)
        {
            Clip(buffer, ref start, ref count);
            int size = 2;
            while (size < count || size < minSize) size <<= 1;
            var re = new double[size];
            var im = new double[size];
            for (int i = 0; i < count; i++)
                re[i] = buffer[start + i] * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (i + 0.5) / count));
            Fft(re, im);
            var magnitude = new double[size / 2 + 1];
            for (int k = 0; k < magnitude.Length; k++) magnitude[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            return magnitude;
        }

        /// <summary>In-place radix-2 FFT; the length must be a power of two.</summary>
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i];
                    re[i] = re[j];
                    re[j] = tr;
                    double ti = im[i];
                    im[i] = im[j];
                    im[j] = ti;
                }
            }
            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2.0 * Math.PI / length;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int i = 0; i < n; i += length)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < length / 2; k++)
                    {
                        int a = i + k, b = a + length / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                        double next = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = next;
                    }
                }
            }
        }

        /// <summary>
        /// The spectral centroid in Hz: the power-weighted mean frequency. Dull sounds sit low, bright and
        /// hissy ones high; white noise sits at a quarter of the sample rate. (Weighted by power, not by
        /// magnitude: the long shallow skirt of a filter would otherwise outweigh its pass band.)
        /// </summary>
        public static float Centroid(float[] buffer, int rate, int start = 0, int count = -1)
        {
            double[] spectrum = Spectrum(buffer, start, count);
            double hzPerBin = (double)rate / (2 * (spectrum.Length - 1));
            double weighted = 0, total = 0;
            for (int k = 1; k < spectrum.Length; k++)
            {
                double power = spectrum[k] * spectrum[k];
                weighted += power * k * hzPerBin;
                total += power;
            }
            return total > 0 ? (float)(weighted / total) : 0f;
        }

        /// <summary>Mean spectral magnitude between two frequencies: how much of a sound lies in a band.</summary>
        public static double Band(float[] buffer, int rate, float lowHz, float highHz, int start = 0, int count = -1)
        {
            double[] spectrum = Spectrum(buffer, start, count, 8192);
            double hzPerBin = (double)rate / (2 * (spectrum.Length - 1));
            int low = Math.Max(0, (int)(lowHz / hzPerBin)), high = Math.Min(spectrum.Length - 1, (int)(highHz / hzPerBin));
            if (high < low) return 0.0;
            double sum = 0;
            for (int k = low; k <= high; k++) sum += spectrum[k];
            return sum / (high - low + 1);
        }

        /// <summary>The frequency of the strongest spectral peak between the limits, interpolated between bins.</summary>
        public static float DominantHz(float[] buffer, int rate, int start = 0, int count = -1, float minHz = 20f, float maxHz = float.MaxValue)
        {
            double[] spectrum = Spectrum(buffer, start, count, 16384);
            double hzPerBin = (double)rate / (2 * (spectrum.Length - 1));
            int low = Math.Max(1, (int)Math.Ceiling(minHz / hzPerBin));
            int high = Math.Min(spectrum.Length - 2, (int)Math.Floor(Math.Min(maxHz, rate * 0.5f) / hzPerBin));
            int best = low;
            for (int k = low; k <= high; k++)
                if (spectrum[k] > spectrum[best]) best = k;
            // A parabola through the peak bin and its neighbours.
            double a = spectrum[best - 1], b = spectrum[best], c = spectrum[best + 1];
            double denominator = a - 2 * b + c;
            double offset = Math.Abs(denominator) > 1e-20 ? 0.5 * (a - c) / denominator : 0.0;
            return (float)((best + offset) * hzPerBin);
        }

        /// <summary>
        /// Amplitude of the component at <paramref name="hz"/> in [start, start + count): a single-frequency
        /// Fourier sum (Goertzel's job), so the frequency need not fall on a bin. A steady sine of amplitude
        /// A measures A.
        /// </summary>
        public static float Level(float[] buffer, int rate, float hz, int start = 0, int count = -1)
        {
            Clip(buffer, ref start, ref count);
            if (count <= 0) return 0f;
            double w = 2.0 * Math.PI * hz / rate;
            double re = 0, im = 0, window = 0;
            for (int i = 0; i < count; i++)
            {
                double h = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (i + 0.5) / count);
                double x = buffer[start + i] * h;
                re += x * Math.Cos(w * i);
                im -= x * Math.Sin(w * i);
                window += h;
            }
            return (float)(2.0 * Math.Sqrt(re * re + im * im) / window);
        }

        /// <summary>
        /// The exponential decay time constant (seconds to fall to 1/e) of the component at
        /// <paramref name="hz"/>, from its level in two windows of <paramref name="window"/> seconds that
        /// start at <paramref name="from"/> and <paramref name="to"/>.
        /// </summary>
        public static float DecayTime(float[] buffer, int rate, float hz, float from, float to, float window)
        {
            int count = Synth.Samples(window, rate);
            float early = Level(buffer, rate, hz, Synth.Samples(from, rate), count);
            float late = Level(buffer, rate, hz, Synth.Samples(to, rate), count);
            if (!(early > 0f) || !(late > 0f) || late >= early) return float.PositiveInfinity;
            return (to - from) / (float)Math.Log(early / late);
        }

        /// <summary>How many times the signal crosses zero going up in [start, start + count): cycles of a clean tone.</summary>
        public static int Cycles(float[] buffer, int start = 0, int count = -1)
        {
            Clip(buffer, ref start, ref count);
            int crossings = 0;
            for (int i = start + 1; i < start + count; i++)
                if (buffer[i - 1] < 0f && buffer[i] >= 0f) crossings++;
            return crossings;
        }

        static void Clip(float[] buffer, ref int start, ref int count)
        {
            if (start < 0) start = 0;
            if (start > buffer.Length) start = buffer.Length;
            if (count < 0 || start + count > buffer.Length) count = buffer.Length - start;
        }
    }
}
