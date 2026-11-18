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
    public void ScansSingleFilesAndHonorsNonRecursiveMode()
    {
        string subfolder = Path.Combine(directory, "Album");
        Directory.CreateDirectory(subfolder);
        string first = Path.Combine(directory, "first.wav");
        string second = Path.Combine(subfolder, "second.wav");
        foreach (string path in new[] { first, second })
        {
            var format = new WaveFormat(44100, 16, 2);
            using var writer = new WaveFileWriter(path, format);
            var silence = new byte[format.AverageBytesPerSecond];
            writer.Write(silence, 0, silence.Length);
        }
        Assert.Equal(first, Assert.Single(LibraryScanner.ReadFolder(directory, includeSubfolders: false, parallelReads: 2).Tracks).Path);
        Assert.Equal(2, LibraryScanner.ReadFolder(directory).Tracks.Count);
        Assert.Equal(second, Assert.Single(LibraryScanner.ReadFolder(second).Tracks).Path);
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

    [Fact]
    public void SuccessfulScanCheckpointsSurviveCancellationAndReload()
    {
        string music = Path.Combine(directory, "Music");
        Directory.CreateDirectory(music);
        var format = new WaveFormat(44100, 16, 2);
        for (int i = 0; i < 260; i++)
        {
            using var writer = new WaveFileWriter(Path.Combine(music, $"{i:D4}.wav"), format);
            var silence = new byte[format.AverageBytesPerSecond];
            writer.Write(silence, 0, silence.Length);
        }

        var database = new LibraryDatabase(Path.Combine(directory, "State"));
        var library = database.Load();
        library.Folders.Add(music);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => LibraryScanner.ReadFolder(music, cancellation.Token,
            parallelReads: 1, checkpoint: batch =>
            {
                library.Tracks.AddRange(batch);
                database.Save(library, true);
                if (library.Tracks.Count >= 256) cancellation.Cancel();
            }));

        var reopened = new LibraryDatabase(Path.Combine(directory, "State")).Load();
        Assert.Equal(music, Assert.Single(reopened.Folders));
        Assert.InRange(reopened.Tracks.Count, 256, 260);
        var cache = reopened.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var resumed = LibraryScanner.ReadFolder(music, cache: cache, parallelReads: 1);
        Assert.Equal(0, resumed.Errors);
        Assert.Equal(260, resumed.Tracks.Count);
        Assert.All(resumed.Tracks.Where(track => cache.ContainsKey(track.Path)),
            track => Assert.Same(cache[track.Path], track));
    }

    [Fact]
    public void ScannerReportsCorruptAudioAndIndexesOtherFiles()
    {
        Directory.CreateDirectory(directory);
        string good = Path.Combine(directory, "good.wav");
        string bad = Path.Combine(directory, "broken.flac");
        var format = new WaveFormat(44100, 16, 2);
        using (var writer = new WaveFileWriter(good, format))
            writer.Write(new byte[format.AverageBytesPerSecond], 0, format.AverageBytesPerSecond);
        File.WriteAllBytes(bad, new byte[64]);

        var failures = new System.Collections.Generic.List<(string Path, string Message)>();
        var checkpoints = new System.Collections.Generic.List<Track>();
        var result = LibraryScanner.ReadFolder(directory, parallelReads: 1,
            checkpoint: batch => checkpoints.AddRange(batch),
            onError: (path, message) => failures.Add((path, message)));

        Assert.Equal(good, Assert.Single(result.Tracks).Path);
        Assert.Equal(good, Assert.Single(checkpoints).Path);
        Assert.Equal(1, result.Errors);
        Assert.Equal(bad, Assert.Single(failures).Path);
        Assert.False(string.IsNullOrWhiteSpace(failures[0].Message));
    }

    [Theory]
    [InlineData("smb://fiberhome/Musica%20FLAC/Album/", @"\\fiberhome\Musica FLAC\Album")]
    [InlineData(@"\\fiberhome\Musica\", @"\\fiberhome\Musica")]
    [InlineData("smb://user:pass@fiberhome/Musica", null)]
    [InlineData(@"\\?\UNC\fiberhome\Musica", null)]
    public void NasNormalizesSupportedPaths(string input, string expected) =>
        Assert.Equal(expected, NetworkMusicSource.Normalize(input));
}
