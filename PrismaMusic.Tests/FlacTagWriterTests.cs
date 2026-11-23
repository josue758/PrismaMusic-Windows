using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class FlacTagWriterTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("prisma-flac-edit-").FullName;

    [Fact]
    public void WritesTagsAndRetainsTheAudioData()
    {
        string path = Path.Combine(directory, "record.flac");
        byte[] audio = [0xFF, 0xF8, 0xE8, 0x18, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD];
        using (var stream = File.Create(path))
        {
            stream.Write("fLaC"u8);
            stream.Write(new byte[] { 128, 0, 0, 34 });
            var streamInfo = new byte[34];
            BinaryPrimitives.WriteUInt16BigEndian(streamInfo.AsSpan(0, 2), 4096);
            BinaryPrimitives.WriteUInt16BigEndian(streamInfo.AsSpan(2, 2), 4096);
            BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10), (44100UL << 44) | (1UL << 41) | (15UL << 36) | 44100UL);
            stream.Write(streamInfo);
            stream.Write(audio);
        }

        FlacTagWriter.Write(path, new LibraryTagEdit(Title: "Río", Album: "Prisma", Genre: "Folk", Year: 2026, Number: 3));

        using var file = TagLib.File.Create(path);
        Assert.Equal("Río", file.Tag.Title);
        Assert.Equal("Prisma", file.Tag.Album);
        Assert.Equal("Folk", Assert.Single(file.Tag.Genres));
        Assert.Equal(2026u, file.Tag.Year);
        Assert.Equal(3u, file.Tag.Track);
        Assert.Equal(audio, File.ReadAllBytes(path)[^audio.Length..]);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void RejectsInvalidFlacWithoutOverwritingIt()
    {
        string path = Path.Combine(directory, "invalid.flac");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(path, original);

        Assert.ThrowsAny<Exception>(() => FlacTagWriter.Write(path, new LibraryTagEdit(Title: "Río")));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
