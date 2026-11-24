using System;
using System.Threading;
using NAudio.Wave;

namespace PrismaMusic;

internal sealed class BufferedPlaybackSamples : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly float[] prefix;
    private readonly int length;
    private int cursor;

    internal BufferedPlaybackSamples(ISampleProvider source)
    {
        this.source = source;
        prefix = new float[Math.Max(1, WaveFormat.SampleRate / 8) * WaveFormat.Channels];
        while (length < prefix.Length)
        {
            int count = source.Read(prefix, length, prefix.Length - length);
            if (count == 0) break;
            length += count;
        }
        if (length == 0) throw new InvalidOperationException("The next decoder did not return audio samples.");
    }

    public WaveFormat WaveFormat => source.WaveFormat;
    internal TimeSpan Remaining => TimeSpan.FromSeconds(
        (length - Volatile.Read(ref cursor)) / ((double)WaveFormat.SampleRate * WaveFormat.Channels));

    internal void Discard() => Interlocked.Exchange(ref cursor, length);

    public int Read(float[] buffer, int offset, int count)
    {
        int start = Volatile.Read(ref cursor);
        int buffered = Math.Min(count, length - start);
        if (buffered > 0)
        {
            Array.Copy(prefix, start, buffer, offset, buffered);
            if (Interlocked.CompareExchange(ref cursor, start + buffered, start) != start) buffered = 0;
        }
        return buffered == count ? buffered : buffered + source.Read(buffer, offset + buffered, count - buffered);
    }
}
