using System;
using System.IO;
using System.Threading;
using System.Linq;
using NAudio.Wave;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "prisma-scan-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public void ScannerStopsBeforeEnumeratingWhenCancelled()
    {
        Directory.CreateDirectory(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => LibraryScanner.ReadFolder(directory, cancellation.Token));
    }

    [Fact]
    public void ScannerDistinguishesPcmFromCompressedAudioInTheSameContainer()
    {
        Directory.CreateDirectory(directory);
        var pcm = new WaveFormat(44100, 16, 2);
        var compressed = WaveFormat.CreateMuLawFormat(8000, 1);
        WriteWave("linear.wav", pcm);
        WriteWave("compressed.wav", compressed);

        var result = LibraryScanner.ReadFolder(directory);
        Assert.Equal(0, result.Errors);
        var linear = Assert.Single(result.Tracks.Where(track => track.IsLossless));
        Assert.Equal("PCM", linear.Codec);
        Assert.Equal(44100, linear.SampleRate);
        Assert.Equal(16, linear.BitDepth);
        Assert.Equal("compressed", Assert.Single(result.Tracks.Where(track => !track.IsLossless)).Name);

        void WriteWave(string name, WaveFormat format)
        {
            using var writer = new WaveFileWriter(Path.Combine(directory, name), format);
            var silence = new byte[format.AverageBytesPerSecond];
            writer.Write(silence, 0, silence.Length);
        }
    }

    [Fact]
    public void ScannerIgnoresUnsupportedFiles()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "not music");

        var result = LibraryScanner.ReadFolder(directory);

        Assert.Empty(result.Tracks);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public void ScannerReusesUnchangedFilesAndRefreshesChangedMetadata()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "cache.wav");
        Write(1);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-3));
        var first = Assert.Single(LibraryScanner.ReadFolder(directory).Tracks);
        var cache = new System.Collections.Generic.Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase) { [path] = first };

        Assert.Same(first, Assert.Single(LibraryScanner.ReadFolder(directory, cache: cache).Tracks));

        Write(2);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        var second = Assert.Single(LibraryScanner.ReadFolder(directory, cache: cache).Tracks);
        Assert.NotSame(first, second);
        Assert.Equal(2, second.Seconds);

        void Write(int seconds)
        {
            var format = new WaveFormat(44100, 16, 2);
            using var writer = new WaveFileWriter(path, format);
            var bytes = new byte[format.AverageBytesPerSecond * seconds];
            writer.Write(bytes, 0, bytes.Length);
        }
    }

    [Theory]
    [InlineData("smb://fiberhome/Musica%20FLAC/Album/", @"\\fiberhome\Musica FLAC\Album")]
    [InlineData(@"\\fiberhome\Musica\", @"\\fiberhome\Musica")]
    [InlineData("smb://user:pass@fiberhome/Musica", null)]
    [InlineData(@"\\?\UNC\fiberhome\Musica", null)]
    public void NasNormalizesSupportedPaths(string input, string expected) =>
        Assert.Equal(expected, NetworkMusicSource.Normalize(input));
}
