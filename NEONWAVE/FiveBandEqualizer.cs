using NAudio.Wave;

namespace NEONWAVE;

/// <summary>
/// Five independent peaking EQ bands applied to interleaved floating-point samples.
/// Gain changes are synchronized with reads so audio callback and UI threads do not
/// modify filter coefficients at the same time.
/// </summary>
public sealed class FiveBandEqualizer : ISampleProvider
{
    private static readonly double[] Frequencies = [80, 250, 1000, 4000, 12000];
    private readonly ISampleProvider source;
    private readonly object gate = new();
    private readonly Biquad[,] filters;
    private readonly float[] gainsDb = new float[5];
    private readonly int channels;
    private readonly int sampleRate;

    public WaveFormat WaveFormat => source.WaveFormat;

    public FiveBandEqualizer(ISampleProvider source, IReadOnlyList<double> initialGainsDb)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        sampleRate = source.WaveFormat.SampleRate;
        filters = new Biquad[5, channels];

        for (int band = 0; band < 5; band++)
        {
            gainsDb[band] = (float)Math.Clamp(initialGainsDb[band], -12, 12);
            for (int channel = 0; channel < channels; channel++)
            {
                filters[band, channel] = new Biquad();
                filters[band, channel].Configure(sampleRate, Frequencies[band], gainsDb[band]);
            }
        }
    }

    public void SetGain(int band, double gainDb)
    {
        if (band < 0 || band >= 5) return;
        lock (gate)
        {
            gainsDb[band] = (float)Math.Clamp(gainDb, -12, 12);
            for (int channel = 0; channel < channels; channel++)
                filters[band, channel].Configure(sampleRate, Frequencies[band], gainsDb[band]);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            int read = source.Read(buffer, offset, count);
            for (int index = offset; index < offset + read; index++)
            {
                int channel = (index - offset) % channels;
                double sample = buffer[index];
                for (int band = 0; band < 5; band++)
                    sample = filters[band, channel].Process(sample);
                // Keep samples in range to prevent clipping from excessive boosts.
                buffer[index] = (float)Math.Clamp(sample, -1, 1);
            }
            return read;
        }
    }

    private sealed class Biquad
    {
        private double b0 = 1, b1, b2, a1, a2, z1, z2;

        public void Configure(int sampleRate, double frequency, double gainDb)
        {
            // RBJ Audio EQ Cookbook: constant-Q peaking filter.
            frequency = Math.Min(frequency, sampleRate * 0.45);
            double amplitude = Math.Pow(10, gainDb / 40);
            double omega = 2 * Math.PI * frequency / sampleRate;
            double alpha = Math.Sin(omega) / (2 * 0.85);
            double cosine = Math.Cos(omega);
            double denominator = 1 + alpha / amplitude;
            b0 = (1 + alpha * amplitude) / denominator;
            b1 = (-2 * cosine) / denominator;
            b2 = (1 - alpha * amplitude) / denominator;
            a1 = (-2 * cosine) / denominator;
            a2 = (1 - alpha / amplitude) / denominator;
        }

        public double Process(double input)
        {
            // Transposed direct-form II; keep state per band and channel.
            double output = b0 * input + z1;
            z1 = b1 * input - a1 * output + z2;
            z2 = b2 * input - a2 * output;
            return output;
        }
    }
}
