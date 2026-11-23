using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class PlaybackSamplesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertsExtensible24BitPcmToFloatWithoutChangingSampleValues(bool serialized)
    {
        var bytes = new byte[] { 0, 0, 64, 0, 0, 192 };
        WaveFormat format = new WaveFormatExtensible(48000, 24, 2);
        if (serialized)
        {
            using var representation = new MemoryStream();
            using var writer = new BinaryWriter(representation);
            format.Serialize(writer);
            representation.Position = 0;
            using var reader = new BinaryReader(representation);
            format = WaveFormat.FromFormatChunk(reader, reader.ReadInt32());
        }
        using var stream = new RawSourceWaveStream(new MemoryStream(bytes), format);
        var samples = AudioEngine.ToPlaybackSamples(stream);
        var result = new float[2];
        Assert.Equal(2, samples.Read(result, 0, result.Length));
        Assert.Equal(new[] { .5f, -.5f }, result);
        Assert.Equal(48000, samples.WaveFormat.SampleRate);
        Assert.Equal(2, samples.WaveFormat.Channels);
        var tempo = new TempoProvider(stream, samples, .75);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, new SampleToWaveProvider(tempo).WaveFormat.Encoding);
    }
}
