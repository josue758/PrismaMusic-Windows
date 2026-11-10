using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PrismaMusic;

internal static class AudioSampleRatePolicy
{
    public static readonly int[] CommonRates = { 44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000 };

    public static bool IsValidRequest(int sampleRate) => sampleRate == 0 || CommonRates.Contains(sampleRate);

    public static bool SupportsFloatOutputRate(AudioClient client, int sampleRate, int channels) =>
        IsSupported(client, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));

    public static bool SupportsPcm16OutputRate(AudioClient client, int sampleRate, int channels)
    {
        int blockAlign = channels * 2;
        var format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, sampleRate, channels,
            sampleRate * blockAlign, blockAlign, 16);
        return IsSupported(client, format);
    }

    public static bool SupportsOutputRate(AudioClient client, int sampleRate, int channels) =>
        SupportsFloatOutputRate(client, sampleRate, channels) ||
        SupportsPcm16OutputRate(client, sampleRate, channels);

    public static int SelectOutputRate(int trackRate, int requestedRate, bool exclusive, bool strictDirect)
    {
        if (trackRate <= 0) throw new ArgumentOutOfRangeException(nameof(trackRate));
        if (!IsValidRequest(requestedRate)) throw new ArgumentOutOfRangeException(nameof(requestedRate));
        return strictDirect || !exclusive || requestedRate == 0 ? trackRate : requestedRate;
    }

    private static bool IsSupported(AudioClient client, WaveFormat format)
    {
        try { return client.IsFormatSupported(AudioClientShareMode.Exclusive, format); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
