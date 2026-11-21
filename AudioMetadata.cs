using System;
using System.IO;
using System.Linq;

namespace PrismaMusic;

internal readonly record struct AudioMetadata(string Codec, int SampleRate, int BitDepth, int Channels, int BitrateKbps)
{
    internal static TagLib.File Open(string path) =>
        Path.GetExtension(path).Equals(".alac", StringComparison.OrdinalIgnoreCase)
            ? TagLib.File.Create(path, "taglib/m4a", TagLib.ReadStyle.Average)
            : TagLib.File.Create(path);

    internal static AudioMetadata Read(string path, TagLib.Properties properties)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var mp4 = extension is ".m4a" or ".alac" ? Mp4AudioMetadata.Read(path) : default;
        string codec = mp4.Codec;
        int depth = properties.BitsPerSample;
        foreach (var entry in properties.Codecs)
        {
            if (entry is TagLib.Flac.StreamHeader) codec = "FLAC";
            if (entry is TagLib.Riff.WaveFormatEx wave && (int)wave.FormatTag is 1 or 3) codec = "PCM";
            if (entry is TagLib.Mpeg4.IsoAudioSampleEntry sample)
            {
                if (sample.BoxType.ToString() == "alac") codec = "ALAC";
                if (sample.GetChildRecursively("esds") is TagLib.Mpeg4.AppleElementaryStreamDescriptor descriptor &&
                    descriptor.ObjectTypeId is 0x40 or 0x66 or 0x67 or 0x68) codec ??= "AAC";
            }
        }
        if (codec == "PCM" && properties.AudioSampleRate > 0 && depth > 0 && properties.AudioChannels > 0)
            return new(codec, properties.AudioSampleRate, depth, properties.AudioChannels,
                (int)Math.Round(properties.AudioSampleRate * (double)depth * properties.AudioChannels / 1000));
        return new(codec, mp4.SampleRate > 0 ? mp4.SampleRate : properties.AudioSampleRate,
            mp4.BitDepth > 0 ? mp4.BitDepth : depth,
            mp4.Channels > 0 ? mp4.Channels : properties.AudioChannels,
            mp4.BitrateKbps > 0 ? mp4.BitrateKbps : properties.AudioBitrate);
    }

    internal static AudioMetadata ForTrack(Track track, TagLib.Properties properties)
    {
        if (track.BitrateKbps is int bitrate)
            return new(track.Codec, track.SampleRate, track.BitDepth, track.Channels, bitrate);
        if (Path.GetExtension(track.Path).Equals(".flac", StringComparison.OrdinalIgnoreCase) &&
            FlacTagReader.Read(track.Path, new FileInfo(track.Path).Length, 0) is { } flac)
            return new(flac.Codec, flac.SampleRate, flac.BitDepth, flac.Channels, flac.BitrateKbps.GetValueOrDefault());
        return Read(track.Path, properties);
    }

    internal static int Kilobits(long audioBytes, double seconds)
    {
        double bitrate = seconds > 0 ? audioBytes * 0.008 / seconds : 0;
        return bitrate > 0 && bitrate <= int.MaxValue ? (int)Math.Max(1, Math.Round(bitrate)) : 0;
    }

    internal string Label(string path) => string.Join(" · ", new[]
    {
        Codec ?? Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
        BitDepth > 0 ? $"{BitDepth} bit" : null,
        SampleRate > 0 ? $"{SampleRate / 1000d:0.###} kHz" : null,
        BitrateKbps > 0 ? $"{BitrateKbps:N0} kb/s" : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
