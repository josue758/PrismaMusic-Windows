using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace PrismaMusic;

/// <summary>Feeds one output stream with successive compatible decoders.</summary>
internal sealed class ContinuousAudioSource : ISampleProvider, IDisposable
{
    private sealed record Segment(WaveStream Decoder, ISampleProvider Samples, Track Track, ReplayGainTags Gain);
    private readonly object gate = new();
    private readonly Action<ContinuousAudioSource, Track, ReplayGainTags> transition;
    private readonly List<WaveStream> retired = new();
    private Segment active;
    private Segment staged;
    private bool exhausted;
    private bool disposed;

    public ContinuousAudioSource(WaveStream decoder, ISampleProvider samples,
        Action<ContinuousAudioSource, Track, ReplayGainTags> transition)
    {
        active = new(decoder, samples, null, null);
        WaveFormat = samples.WaveFormat;
        this.transition = transition;
    }

    public WaveFormat WaveFormat { get; }
    public WaveStream Current { get { lock (gate) return active.Decoder; } }
    public TimeSpan Position
    {
        get
        {
            Segment segment;
            lock (gate) segment = active;
            TimeSpan position = segment.Decoder.CurrentTime;
            if (segment.Samples is BufferedPlaybackSamples buffered) position -= buffered.Remaining;
            return position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }
        set
        {
            Segment segment;
            lock (gate) segment = active;
            if (segment.Samples is BufferedPlaybackSamples buffered) buffered.Discard();
            segment.Decoder.CurrentTime = value;
        }
    }

    public bool TryStage(WaveStream decoder, ISampleProvider samples, Track track, ReplayGainTags gain)
    {
        lock (gate)
        {
            if (disposed || exhausted || staged != null || !WaveFormat.Equals(samples.WaveFormat) ||
                !active.Decoder.WaveFormat.Equals(decoder.WaveFormat)) return false;
            staged = new(decoder, samples, track, gain);
            return true;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int produced = 0;
        while (produced < count)
        {
            var segment = active;
            int read = segment.Samples.Read(buffer, offset + produced, count - produced);
            produced += read;
            if (read > 0) continue;
            lock (gate)
            {
                if (staged == null)
                {
                    exhausted = true;
                    break;
                }
                active = staged;
                staged = null;
                retired.Add(segment.Decoder);
            }
            transition(this, active.Track, active.Gain);
        }
        return produced;
    }

    public void ClearStaged()
    {
        Segment removed;
        lock (gate) { removed = staged; staged = null; }
        removed?.Decoder.Dispose();
    }

    public void ReleaseRetired()
    {
        WaveStream[] completed;
        lock (gate) { completed = retired.ToArray(); retired.Clear(); }
        foreach (var decoder in completed) decoder.Dispose();
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        ClearStaged();
        ReleaseRetired();
        active.Decoder.Dispose();
    }
}
