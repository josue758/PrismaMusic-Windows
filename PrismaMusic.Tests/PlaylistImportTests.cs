using System;
using System.IO;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class PlaylistImportTests
{
    [Fact]
    public void ResolvesRelativeUrisAndRemappedNasPathsInOriginalOrder()
    {
        string[] lines = [@"Album\First.flac", "file:///C:/Music/Album/Other.flac",
            "smb://old/Music/Second/Final.flac", @"X:\Old\Album\Missing.flac", "https://example.com/a"];
        Track[] library = [Song(@"C:\Music\Album\First.flac"), Song(@"C:\Music\Album\Other.flac"),
            Song(@"\\new\Media\Second\Final.flac"), Song(@"\\another\Media\Second\Final.flac")];
        var found = PlaylistImport.Match(lines, @"C:\Music", library, @"\\old\Music", @"\\new\Media");
        Assert.Equal(library[0], found[0].Track);
        Assert.Equal(library[1], found[1].Track);
        Assert.Equal(library[2], found[2].Track);
        Assert.True(found[2].Remapped);
        Assert.Null(found[3].Track);
        Assert.Null(found[4].Track);
        Assert.Null(Assert.Single(PlaylistImport.Match([lines[2]], @"C:\Music", library)).Track);
    }

    [Fact]
    public void ReadsUtf8MetadataDirectivesAndTrackNamesWithAccents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"prisma-m3u-{Guid.NewGuid():N}.m3u8");
        try
        {
            File.WriteAllText(path, "\uFEFF#EXTM3U\r\n#EXTINF:300,Río\r\nÁlbum/Río.flac\r\n   \r\nÁlbum/Luz.flac");
            Assert.Equal(new[] { "Álbum/Río.flac", "Álbum/Luz.flac" }, PlaylistImport.ReadEntries(path));
        }
        finally { File.Delete(path); }
    }

    private static Track Song(string path) => new(path, "Song", "Artist", "Album", "Artist", "Rock", 2024, 1, 1, 180);
}
