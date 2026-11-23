using System;
using NAudio.Wave;
using SoundTouch;

namespace PrismaMusic;

internal sealed class TempoProvider : ISampleProvider
{
    private readonly WaveStream source;
    private readonly ISampleProvider samples;
    private readonly SoundTouchProcessor processor;
    private readonly float[] input;
    private readonly object gate = new();
    private readonly double speed;
    private double position;
    private bool exhausted;

    public TempoProvider(WaveStream source, ISampleProvider samples, double speed)
    {
        this.source = source;
        this.samples = samples;
        this.speed = speed;
        input = new float[2048 * WaveFormat.Channels];
        processor = new SoundTouchProcessor
        {
            Channels = WaveFormat.Channels,
            SampleRate = WaveFormat.SampleRate,
            Tempo = speed
        };
    }

    public WaveFormat WaveFormat => samples.WaveFormat;
    public TimeSpan Position
    {
        get { lock (gate) return TimeSpan.FromSeconds(Math.Min(position, source.TotalTime.TotalSeconds)); }
        set
        {
            lock (gate)
            {
                source.CurrentTime = value;
                processor.Clear();
                exhausted = false;
                position = source.CurrentTime.TotalSeconds;
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            int channels = WaveFormat.Channels;
            int written = 0;
            while (written + channels <= count)
            {
                int frames = processor.ReceiveSamples(buffer.AsSpan(offset + written, count - written),
                    (count - written) / channels);
                written += frames * channels;
                if (frames > 0) continue;
                if (exhausted) break;
                int read = samples.Read(input, 0, input.Length);
                if (read == 0)
                {
                    exhausted = true;
                    processor.Flush();
                }
                else processor.PutSamples(input, read / channels);
            }
            position += written / (double)(channels * WaveFormat.SampleRate) * speed;
            return written;
        }
    }
}
