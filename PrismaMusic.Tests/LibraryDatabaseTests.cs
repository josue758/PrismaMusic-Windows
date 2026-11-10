using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryDatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "prisma-tests-" + Guid.NewGuid().ToString("N"));

    private static Track Song(string path, string name = "Canción") =>
        new(path, name, "Artista", "Álbum", "Artista", "Rock", 2020, 1, 1, 180);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public void SaveAndLoadKeepTracksFavoritesAndFolders()
    {
        var first = new LibraryDatabase(directory);
        var library = first.Load();
        library.Folders.Add(@"C:\Musica");
        library.Tracks.Add(Song(@"C:\Musica\a.flac"));
        library.Favorites.Add(@"C:\Musica\a.flac");
        first.Save(library, true);

        var loaded = new LibraryDatabase(directory).Load();

        Assert.Single(loaded.Tracks);
        Assert.Equal(@"C:\Musica", loaded.Folders.Single());
        Assert.Contains(@"c:\musica\A.FLAC", loaded.Favorites);
    }

    [Fact]
    public void RemovedTracksDisappearAfterSaving()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        library.Tracks.AddRange(new[] { Song(@"C:\M\a.mp3", "A"), Song(@"C:\M\b.mp3", "B") });
        database.Save(library, true);

        library.Tracks.RemoveAll(track => track.Name == "A");
        database.Save(library, true);

        var loaded = new LibraryDatabase(directory).Load();
        Assert.Equal("B", loaded.Tracks.Single().Name);
    }

    [Fact]
    public void ChangingOnlyFavoritesDoesNotRewriteTracks()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        library.Tracks.Add(Song(@"C:\M\a.mp3"));
        database.Save(library, true);

        library.Favorites.Add(@"C:\M\a.mp3");
        database.Save(library, false);

        var loaded = new LibraryDatabase(directory).Load();
        Assert.Single(loaded.Tracks);
        Assert.Single(loaded.Favorites);
    }

    [Fact]
    public void ArtworkIsSharedAcrossTracksAndSurvivesReload()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        library.Tracks.AddRange(new[] { Song(@"C:\M\a.flac"), Song(@"C:\M\b.flac", "Otra") });
        database.Save(library, true);
        byte[] artwork = { 1, 2, 3, 4, 5 };

        database.SaveArtwork(@"C:\M\a.flac", artwork);
        database.SaveArtwork(@"C:\M\b.flac", artwork);

        Assert.Equal(artwork, database.ReadArtwork(@"c:\m\A.FLAC"));
        Assert.Equal(artwork, database.ReadArtwork(@"C:\M\b.flac"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(directory, "library.sqlite3") }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM artwork";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void RemovingTrackPrunesItsArtworkReferenceAndUnusedImage()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        library.Tracks.Add(Song(@"C:\M\a.flac"));
        database.Save(library, true);
        database.SaveArtwork(@"C:\M\a.flac", new byte[] { 1, 2, 3 });

        library.Tracks.Clear();
        database.Save(library, true);

        Assert.Null(database.ReadArtwork(@"C:\M\a.flac"));
    }

    [Fact]
    public void CorruptDatabaseIsKeptAndRestoredFromBackup()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        library.Tracks.Add(Song(@"C:\M\a.mp3"));
        database.Save(library, true);
        new LibraryDatabase(directory).Load();            // un inicio sano renueva la copia de seguridad

        SqliteConnection.ClearAllPools();
        string file = Path.Combine(directory, "library.sqlite3");
        File.WriteAllBytes(file, Enumerable.Repeat((byte)0x7F, 4096).ToArray());
        foreach (string side in new[] { "-wal", "-shm" })
            if (File.Exists(file + side)) File.Delete(file + side);

        var recovered = new LibraryDatabase(directory).Load();

        Assert.Single(recovered.Tracks);
        Assert.NotEmpty(Directory.GetFiles(directory, "library.corrupt-*.sqlite3"));
    }

    [Fact]
    public void OnlyFiveBackupsAreKept()
    {
        Directory.CreateDirectory(Path.Combine(directory, "backups"));
        for (int day = 1; day <= 8; day++)
            File.WriteAllText(Path.Combine(directory, "backups", $"library-2026010{day}.sqlite3"), "x");

        new LibraryDatabase(directory).Load();

        Assert.True(Directory.GetFiles(Path.Combine(directory, "backups"), "library-*.sqlite3").Length <= 5);
    }
}
