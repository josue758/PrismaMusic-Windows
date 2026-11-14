using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace PrismaMusic;

internal sealed class AudioEngine
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private WasapiOut output;
    private AudioFileReader reader;
    private Meter meter;
    private MMDevice endpoint;
    private double volume = .7;
    public static readonly int[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private float[] gains = new float[Frequencies.Length];
    public string OutputId { get; set; }
    public double Level => meter?.Level ?? 0;
    public event EventHandler MediaOpened;
    public event EventHandler MediaEnded;
    public event EventHandler<Exception> MediaFailed;
    public Duration NaturalDuration => reader == null ? Duration.Automatic : new Duration(reader.TotalTime);
    public TimeSpan Position { get => reader?.CurrentTime ?? TimeSpan.Zero; set { if (reader != null) reader.CurrentTime = value; } }
    public double Volume { get => volume; set { volume = value; if (reader != null) reader.Volume = (float)value; } }

    public void SetGains(float[] values)
    {
        gains = (float[])values.Clone();
        meter?.SetGains(gains);
    }

    public void Open(Uri uri)
    {
        Close();
        try
        {
            reader = new AudioFileReader(uri.LocalPath) { Volume = (float)volume };
            meter = new Meter(reader);
            meter.SetGains(gains);
            using var devices = new MMDeviceEnumerator();
            endpoint = OutputId == null ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(OutputId);
            var session = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
            output = session;
            session.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(meter));
            session.PlaybackStopped += (_, args) => dispatcher.BeginInvoke(() =>
            {
                if (output != session) return;
                if (args.Exception != null) MediaFailed?.Invoke(this, args.Exception);
                else MediaEnded?.Invoke(this, EventArgs.Empty);
            });
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            Close();
            dispatcher.BeginInvoke(() => MediaFailed?.Invoke(this, error));
        }
    }

    public void Play() => output?.Play();
    public void Pause() => output?.Pause();
    public void Close()
    {
        var old = output;
        output = null;
        old?.Dispose();
        reader?.Dispose();
        endpoint?.Dispose();
        reader = null;
        endpoint = null;
        meter = null;
    }

    private sealed class Meter(ISampleProvider source) : ISampleProvider
    {
        private BiQuadFilter[][] filters;
        private float headroom = 1;
        private float level;
        private readonly object gate = new();
        public float Level => Volatile.Read(ref level);
        public WaveFormat WaveFormat => source.WaveFormat;

        public void SetGains(float[] values)
        {
            var next = Enumerable.Range(0, WaveFormat.Channels).Select(_ =>
                Frequencies.Select((frequency, index) => (frequency, gain: values[index]))
                    .Where(band => band.gain != 0 && band.frequency < WaveFormat.SampleRate * .48)
                    .Select(band => BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, band.frequency, 1.4f, band.gain))
                    .ToArray()).ToArray();
            lock (gate)
            {
                filters = next;
                headroom = (float)Math.Pow(10, -Math.Max(0, values.Max()) / 20);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            double squares = 0;
            lock (gate)
            {
                for (int index = 0; index < read; index++)
                {
                    float sample = buffer[offset + index] * headroom;
                    foreach (var filter in filters[index % WaveFormat.Channels]) sample = filter.Transform(sample);
                    buffer[offset + index] = Math.Clamp(sample, -1, 1);
                    squares += sample * sample;
                }
            }
            Volatile.Write(ref level, read == 0 ? 0 : (float)Math.Sqrt(squares / read));
            return read;
        }
    }
}
