using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryExportTests
{
    private static readonly Track Song = new(@"\\NAS\Música\Órbita.flac", "Órbita", "Prisma", "Demo", "", "", 2026, 1, 1, 24,
        "FLAC", 48000, 16);

    [Fact]
    public void PlaylistPreservesOrderAndUnicodePaths()
    {
        var second = Song with { Path = @"C:\Música\Luz.wav", Name = "Luz\r\nagua" };
        Assert.Equal("#EXTM3U\n#EXTINF:24,Prisma - Luz  agua\n" + second.Path +
            "\n#EXTINF:24,Prisma - Órbita\n" + Song.Path + "\n", LibraryExport.PlaylistText(new[] { second, Song }));
    }

    [Fact]
    public async Task JsonExportOverwritesDestinationAndRetainsMetadata()
    {
        string path = Path.GetTempFileName();
        try
        {
            var library = new Library();
            library.Tracks.Add(Song);
            library.TagEdits[Song.Path] = new LibraryTagEdit("Órbita", "Prisma", "Demo", "Ambient", 2026);
            await LibraryExport.WriteAsync(path, LibraryExport.LibraryJson(library));
            var exported = JsonSerializer.Deserialize<Library>(await File.ReadAllTextAsync(path));
            Assert.Equal(Song, Assert.Single(exported.Tracks));
            Assert.Equal("Ambient", exported.TagEdits[Song.Path].Genre);
        }
        finally { File.Delete(path); }
    }
}
