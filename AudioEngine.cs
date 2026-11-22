using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Dmo;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PrismaMusic;

internal sealed class AudioEngine
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private WasapiOut output;
    private WaveStream reader;
    private VolumeSampleProvider volumeStage;
    private WaveStream directReader;
    private WaveStream ActiveReader => directReader ?? reader;
    public bool StrictDirect { get; set; }
    private int outputSampleRate;
    public int OutputSampleRate
    {
        get => outputSampleRate;
        set
        {
            if (!AudioSampleRatePolicy.IsValidRequest(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            outputSampleRate = value;
        }
    }
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
    private double equalizerGainDb;
    public string OutputId { get; set; }
    public string ActiveOutputId => output == null ? null : endpoint?.ID;
    public string ActiveOutputName => output == null ? null : endpoint?.FriendlyName;
    internal bool ActiveOutputUnavailable
    {
        get
        {
            if (output == null || endpoint == null) return false;
            try { return endpoint.State != DeviceState.Active; }
            catch (System.Runtime.InteropServices.COMException) { return true; }
        }
    }
    public bool Exclusive { get; set; }
    public bool IsExclusive { get; private set; }
    public string OutputNotice { get; private set; }
    public WaveFormat OutputFormat => output?.OutputWaveFormat;
    public WaveFormat SourceFormat => ActiveReader?.WaveFormat;
    public double Level => meter?.Level ?? 0;
    public float[] Spectrum => meter?.Spectrum ?? Array.Empty<float>();
    public event EventHandler MediaOpened;
    public event EventHandler MediaEnded;
    public event EventHandler<Exception> MediaFailed;
    public Duration NaturalDuration => ActiveReader == null ? Duration.Automatic : new Duration(ActiveReader.TotalTime);
    public TimeSpan Position
    {
        get => tempo?.Position ?? ActiveReader?.CurrentTime ?? TimeSpan.Zero;
        set { if (tempo != null) tempo.Position = value; else if (ActiveReader != null) ActiveReader.CurrentTime = value; }
    }
    public double Volume { get => volume; set { volume = value; if (volumeStage != null) volumeStage.Volume = (float)value; } }

    public void SetGains(float[] values, double gainDb)
    {
        gains = (float[])values.Clone();
        equalizerGainDb = gainDb;
        meter?.SetGains(gains, equalizerGainDb);
    }

    public void Open(Uri uri)
    {
        try { OpenPrepared(Prepare(uri.LocalPath, StrictDirect), StrictDirect); }
        catch (Exception error)
        {
            Close();
            MediaFailed?.Invoke(this, error);
        }
    }

    internal static WaveStream Prepare(string path, bool strictDirect)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
            path = NetworkMusicSource.Normalize(path) ?? path;
        if (!strictDirect)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".alac" ||
                (path.StartsWith(@"\\", StringComparison.Ordinal) && extension is not (".wav" or ".mp3" or ".aiff" or ".aif")))
                return OpenStreamDecoder(path);
            return new AudioFileReader(path);
        }
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(path),
            ".aiff" or ".aif" => new AiffFileReader(path),
            ".flac" => PrepareDirectFlac(path),
            _ => throw new NotSupportedException("La salida directa admite WAV, AIFF y FLAC compatibles.")
        };
    }

    private static WaveStream PrepareDirectFlac(string path)
    {
        using var metadata = TagLib.File.Create(path);
        if (metadata is not TagLib.Flac.File) throw new NotSupportedException("El archivo no contiene audio FLAC.");
        var original = metadata.Properties;
        var decoded = path.StartsWith(@"\\", StringComparison.Ordinal) ? OpenStreamDecoder(path) : new MediaFoundationReader(path);
        var format = decoded.WaveFormat;
        if (original.BitsPerSample > 0 && format.BitsPerSample == original.BitsPerSample &&
            format.SampleRate == original.AudioSampleRate && format.Channels == original.AudioChannels)
            return decoded;
        decoded.Dispose();
        throw new NotSupportedException("El descodificador de Windows no conservó el formato PCM original del FLAC.");
    }

    private static MediaFoundationReader OpenStreamDecoder(string path)
    {
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { return new OwnedStreamReader(stream); }
        catch { stream.Dispose(); throw; }
    }

    private sealed class OwnedStreamReader : StreamMediaFoundationReader
    {
        private readonly Stream stream;

        public OwnedStreamReader(Stream stream) : base(stream, new MediaFoundationReaderSettings { SingleReaderObject = true })
        {
            this.stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing) stream.Dispose(); }
        }
    }

    internal void OpenPrepared(WaveStream prepared, bool strictDirect)
    {
        Close();
        if (strictDirect) directReader = prepared;
        else reader = prepared;
        OutputNotice = null;
        IsExclusive = false;
        try
        {
            using var devices = new MMDeviceEnumerator();
            bool allowExclusive = true;
            try
            {
                endpoint = OutputId == null ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(OutputId);
                if (endpoint.State != DeviceState.Active && OutputId != null && !strictDirect)
                {
                    endpoint.Dispose();
                    endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    OutputNotice = "La salida seleccionada no está disponible. Se está usando la salida predeterminada.";
                    allowExclusive = false;
                }
            }
            catch (Exception error) when (OutputId != null && !strictDirect &&
                error is System.Runtime.InteropServices.COMException or ArgumentException)
            {
                endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                OutputNotice = "La salida seleccionada no está disponible. Se está usando la salida predeterminada.";
                allowExclusive = false;
            }

            if (strictDirect)
            {
                OpenDirect();
                AttachPlaybackEvents();
                return;
            }
            volumeStage = new VolumeSampleProvider(ToPlaybackSamples(reader)) { Volume = (float)volume };
            if (!direct && Speed != 1) tempo = new TempoProvider(reader, volumeStage, Speed);
            meter = new Meter((ISampleProvider)tempo ?? volumeStage) { Balance = balance, Bypass = direct };
            meter.SetGains(gains, equalizerGainDb);
            int requestedRate = AudioSampleRatePolicy.SelectOutputRate(
                meter.WaveFormat.SampleRate, OutputSampleRate, Exclusive && allowExclusive, strictDirect: false);
            bool usePcm16 = false;
            if (Exclusive && requestedRate != meter.WaveFormat.SampleRate)
            {
                using var client = endpoint.AudioClient;
                if (!AudioSampleRatePolicy.SupportsFloatOutputRate(client, requestedRate, meter.WaveFormat.Channels))
                {
                    if (AudioSampleRatePolicy.SupportsPcm16OutputRate(client, requestedRate, meter.WaveFormat.Channels))
                        usePcm16 = true;
                    else
                    {
                        OutputNotice = $"La salida no admite {requestedRate:N0} Hz en WASAPI exclusivo; se seguirá la frecuencia de la pista.";
                        requestedRate = meter.WaveFormat.SampleRate;
                    }
                }
            }
            ISampleProvider outputProvider = requestedRate == meter.WaveFormat.SampleRate
                ? meter
                : new WdlResamplingSampleProvider(meter, requestedRate);
            IWaveProvider provider = usePcm16
                ? new SampleToWaveProvider16(outputProvider)
                : new SampleToWaveProvider(outputProvider);
            if (Exclusive && allowExclusive)
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
                    provider = new NAudio.Wave.SampleProviders.SampleToWaveProvider(meter);
                }
            }
            if (output == null)
            {
                output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
                output.Init(provider);
            }
            AttachPlaybackEvents();
        }
        catch (Exception error)
        {
            Close();
            MediaFailed?.Invoke(this, error);
        }
    }

    internal static ISampleProvider ToPlaybackSamples(WaveStream source)
    {
        var format = source.WaveFormat;
        WaveFormat standard = null;
        if (format is WaveFormatExtensible extended)
            standard = extended.ToStandardWaveFormat();
        else if (format is WaveFormatExtraData { Encoding: WaveFormatEncoding.Extensible, ExtraSize: >= 22 } extra)
        {
            // Media Foundation can return the extensible format as a serialized WAVEFORMATEX.
            var subtype = new Guid(extra.ExtraData.AsSpan(6, 16));
            if (subtype == AudioMediaSubtypes.MEDIASUBTYPE_PCM)
                standard = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
            else if (subtype == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT && format.BitsPerSample == 32)
                standard = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        }
        if (standard?.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            return new WaveFormatView(source, standard).ToSampleProvider();
        return source as ISampleProvider ?? source.ToSampleProvider();
    }

    private sealed class WaveFormatView(IWaveProvider source, WaveFormat format) : IWaveProvider
    {
        public WaveFormat WaveFormat => format;
        public int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    }

    private void OpenDirect()
    {
        var format = directReader.WaveFormat;
        if (format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible))
            throw new NotSupportedException("La salida directa requiere PCM.");
        using (var client = endpoint.AudioClient)
        {
            if (!client.IsFormatSupported(AudioClientShareMode.Exclusive, format))
                throw new NotSupportedException("El dispositivo no admite el formato original de la pista en modo exclusivo.");
        }
        output = new WasapiOut(endpoint, AudioClientShareMode.Exclusive, false, 100);
        output.Init(directReader);
        if (!format.Equals(output.OutputWaveFormat))
            throw new NotSupportedException("La salida directa requiere que el dispositivo acepte el formato original.");
        IsExclusive = true;
        OutputNotice = $"PCM directo · {format.SampleRate:N0} Hz · {format.BitsPerSample} bits";
    }

    private void AttachPlaybackEvents()
    {
        var session = output;
        session.PlaybackStopped += (_, args) => dispatcher.BeginInvoke(() =>
        {
            if (output != session) return;
            if (args.Exception != null) MediaFailed?.Invoke(this, args.Exception);
            else MediaEnded?.Invoke(this, EventArgs.Empty);
        });
        MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    internal long DevicePositionBytes => output?.GetPosition() ?? 0;

    public void Play() => output?.Play();
    public void Pause() => output?.Pause();
    public void Close()
    {
        var old = output;
        output = null;
        old?.Dispose();
        reader?.Dispose();
        directReader?.Dispose();
        directReader = null;
        endpoint?.Dispose();
        reader = null;
        volumeStage = null;
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

        public void SetGains(float[] values, double gainDb)
        {
            var next = Enumerable.Range(0, WaveFormat.Channels).Select(_ =>
                Frequencies.Select((frequency, index) => (frequency, gain: values[index]))
                    .Where(band => band.gain != 0 && band.frequency < WaveFormat.SampleRate * .48)
                    .Select(band => BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, band.frequency, 1.4f, band.gain))
                    .ToArray()).ToArray();
            lock (gate)
            {
                filters = next;
                headroom = (float)Math.Pow(10, gainDb / 20);
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







