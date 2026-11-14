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
    private TempoProvider tempo;
    public double Speed { get; set; } = 1;
    private Meter meter;
    private MMDevice endpoint;
    private double volume = .7;
    private float balance;
    private bool direct;
    public bool Direct
    {
        get => direct;
        set
        {
            direct = value;
            if (meter != null) meter.Bypass = value;
        }
    }
    public float Balance
    {
        get => balance;
        set { balance = Math.Clamp(value, -1, 1); if (meter != null) meter.Balance = balance; }
    }
    public static readonly int[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private float[] gains = new float[Frequencies.Length];
    public string OutputId { get; set; }
    public bool Exclusive { get; set; }
    public bool IsExclusive { get; private set; }
    public string OutputNotice { get; private set; }
    public double Level => meter?.Level ?? 0;
    public float[] Spectrum => meter?.Spectrum ?? Array.Empty<float>();
    public event EventHandler MediaOpened;
    public event EventHandler MediaEnded;
    public event EventHandler<Exception> MediaFailed;
    public Duration NaturalDuration => reader == null ? Duration.Automatic : new Duration(reader.TotalTime);
    public TimeSpan Position
    {
        get => tempo?.Position ?? reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (tempo != null) tempo.Position = value; else if (reader != null) reader.CurrentTime = value; }
    }
    public double Volume { get => volume; set { volume = value; if (reader != null) reader.Volume = (float)value; } }

    public void SetGains(float[] values)
    {
        gains = (float[])values.Clone();
        meter?.SetGains(gains);
    }

    public void Open(Uri uri)
    {
        Close();
        OutputNotice = null;
        IsExclusive = false;
        try
        {
            reader = new AudioFileReader(uri.LocalPath) { Volume = (float)volume };
            if (!direct && Speed != 1) tempo = new TempoProvider(reader, Speed);
            meter = new Meter((ISampleProvider)tempo ?? reader) { Balance = balance, Bypass = direct };
            meter.SetGains(gains);
            using var devices = new MMDeviceEnumerator();
            try
            {
                endpoint = OutputId == null ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(OutputId);
            }
            catch (System.Runtime.InteropServices.COMException) when (OutputId != null)
            {
                endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                OutputNotice = "La salida seleccionada no está disponible. Se está usando la salida predeterminada.";
            }

            var provider = new NAudio.Wave.SampleProviders.SampleToWaveProvider(meter);
            if (Exclusive && OutputNotice == null)
            {
                try
                {
                    output = new WasapiOut(endpoint, AudioClientShareMode.Exclusive, false, 100);
                    output.Init(provider);
                    IsExclusive = true;
                }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException)
                {
                    output?.Dispose();
                    output = null;
                    OutputNotice = "La salida no aceptó el modo exclusivo. Se está usando WASAPI compartido.";
                }
            }
            if (output == null)
            {
                output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
                output.Init(provider);
            }
            var session = output;
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
        tempo = null;
        endpoint = null;
        meter = null;
    }

    private sealed class Meter(ISampleProvider source) : ISampleProvider
    {
        private BiQuadFilter[][] filters;
        private float headroom = 1;
        private float level;
        public volatile float Balance;
        public volatile bool Bypass;
        private const int FftSize = 2048;
        private readonly Complex[] fft = new Complex[FftSize];
        private readonly float[] window = Enumerable.Range(0, FftSize)
            .Select(index => (float)FastFourierTransform.HannWindow(index, FftSize)).ToArray();
        private int fftIndex;
        private float[] spectrum = Array.Empty<float>();
        public float[] Spectrum => Volatile.Read(ref spectrum);

        private void Analyze(float sample)
        {
            fft[fftIndex] = new Complex { X = sample * window[fftIndex] };
            if (++fftIndex < FftSize) return;
            fftIndex = 0;
            FastFourierTransform.FFT(true, 11, fft);
            var bands = new float[Frequencies.Length];
            for (int band = 0; band < bands.Length; band++)
            {
                double center = Frequencies[band] * FftSize / (double)WaveFormat.SampleRate;
                int first = Math.Max(1, (int)Math.Ceiling(center / Math.Sqrt(2)));
                int last = Math.Min(FftSize / 2 - 1, Math.Max(first, (int)(center * Math.Sqrt(2))));
                float peak = 0;
                for (int bin = first; bin <= last; bin++)
                    peak = Math.Max(peak, fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y);
                bands[band] = Math.Clamp((float)((10 * Math.Log10(Math.Max(peak, 1e-9f)) + 75) / 65), 0, 1);
            }
            Volatile.Write(ref spectrum, bands);
        }
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
                    float sample = buffer[offset + index];
                    int channel = index % WaveFormat.Channels;
                    if (!Bypass)
                    {
                        sample *= headroom;
                        foreach (var filter in filters[channel]) sample = filter.Transform(sample);
                        if (WaveFormat.Channels == 2)
                            sample *= channel == 0 ? 1 - Math.Max(0, Balance) : 1 + Math.Min(0, Balance);
                        buffer[offset + index] = Math.Clamp(sample, -1, 1);
                    }
                    squares += sample * sample;
                    if (index % WaveFormat.Channels == 0) Analyze(sample);
                }
            }
            Volatile.Write(ref level, read == 0 ? 0 : (float)Math.Sqrt(squares / read));
            return read;
        }
    }
}






