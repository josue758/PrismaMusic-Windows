using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Buffers.Binary;
using System.Text;
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
    public void ScansFlacTagsAfterABigEmbeddedPicture()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "record.flac");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write("fLaC"u8);
            WriteBlockHeader(writer, 0, 34);
            var streamInfo = new byte[34];
            BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10), (96000UL << 44) | (1UL << 41) |
                (23UL << 36) | (96000UL * 185));
            writer.Write(streamInfo);

            const int pictureSize = 2 * 1024 * 1024;
            WriteBlockHeader(writer, 6, pictureSize);
            writer.Write(new byte[pictureSize]);

            using var comments = new MemoryStream();
            using var tags = new BinaryWriter(comments);
            tags.Write(0); // Vendor length.
            var fields = new[] { "title=Río", "ALBUM ARTIST=Prisma", "DATE=2004-05-01", "TRACKNUMBER=7/12", "DISCNUMBER=2/3" };
            tags.Write(fields.Length);
            foreach (string field in fields)
            {
                byte[] value = Encoding.UTF8.GetBytes(field);
                tags.Write(value.Length);
                tags.Write(value);
            }
            WriteBlockHeader(writer, 128 | 4, (int)comments.Length);
            writer.Write(comments.ToArray());
            writer.Write((byte)0xFF);
        }

        var result = LibraryScanner.ReadFolder(directory);
        Assert.Equal(0, result.Errors);
        var track = Assert.Single(result.Tracks);
        Assert.Equal("Río", track.Name);
        Assert.Equal("Prisma", track.Artist);
        Assert.Equal("Prisma", track.AlbumArtist);
        Assert.Equal(2004u, track.Year);
        Assert.Equal(7u, track.Number);
        Assert.Equal(2u, track.Disc);
        Assert.Equal(96000, track.SampleRate);
        Assert.Equal(24, track.BitDepth);
        Assert.Equal(185, track.Seconds);

        static void WriteBlockHeader(BinaryWriter writer, byte type, int size)
        {
            writer.Write(type);
            writer.Write((byte)(size >> 16));
            writer.Write((byte)(size >> 8));
            writer.Write((byte)size);
        }
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
        File.WriteAllText(Path.Combine(directory, "._mac-tagged.flac"), "AppleDouble metadata");
        var metadata = Directory.CreateDirectory(Path.Combine(directory, ".AppleDouble"));
        File.WriteAllText(Path.Combine(metadata.FullName, "mac-tagged.flac"), "AppleDouble metadata");

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
        Assert.Same(first, Assert.Single(LibraryScanner.ReadFolder(directory, cache: cache, onlyNew: true).Tracks));
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

    [Theory]
    [InlineData("fiberhome", "fiberhome")]
    [InlineData(@"\\fiberhome\", "fiberhome")]
    [InlineData("smb://192.168.0.1/", "192.168.0.1")]
    [InlineData("smb://fiberhome/Musica", null)]
    [InlineData(@"Z:\", null)]
    [InlineData("smb://user:password@fiberhome", null)]
    public void NasRecognizesServersSeparatelyFromSharePaths(string input, string expected) =>
        Assert.Equal(expected, NetworkMusicSource.ServerOnly(input));
}
