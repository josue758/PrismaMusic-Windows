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
    public void SavesSmartRulesAndTrackImportDates()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        var date = DateTimeOffset.UtcNow;
        var rules = new SmartPlaylistRules(Codec: "FLAC", SampleRate: 96000, Favorites: true, AddedThisMonth: true);
        library.SmartPlaylists["Hi-res"] = rules;
        library.Tracks.Add(Song(@"C:\M\a.flac") with { AddedAt = date });
        database.Save(library, tracksChanged: true);

        var restored = new LibraryDatabase(directory).Load();
        Assert.Equal(rules, restored.SmartPlaylists["HI-RES"]);
        Assert.Equal(date, Assert.Single(restored.Tracks).AddedAt);
    }

    [Fact]
    public void SaveAndLoadKeepTracksFavoritesAndFolders()
    {
        var first = new LibraryDatabase(directory);
        var library = first.Load();
        library.Folders.Add(@"C:\Musica");
        library.Tracks.Add(Song(@"C:\Musica\a.flac"));
        library.Favorites.Add(@"C:\Musica\a.flac");
        library.PlayCounts[@"C:\Musica\a.flac"] = 4;
        first.Save(library, true);

        var loaded = new LibraryDatabase(directory).Load();

        Assert.Single(loaded.Tracks);
        Assert.Equal(@"C:\Musica", loaded.Folders.Single());
        Assert.Contains(@"c:\musica\A.FLAC", loaded.Favorites);
        Assert.Equal(4, loaded.PlayCounts[@"c:\musica\A.FLAC"]);
    }

    [Fact]
    public void TagEditsSurviveReloadAndApplyToFreshlyScannedTrack()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        var scanned = Song(@"C:\M\a.flac");
        var edit = new LibraryTagEdit("Nuevo título", "Nuevo artista", "Nuevo álbum", "Jazz", 2025, "Recopilación", 3, 12);
        library.TagEdits[scanned.Path] = edit;
        library.Tracks.Add(edit.ApplyTo(scanned));
        database.Save(library, true);

        var reloaded = new LibraryDatabase(directory).Load();
        var updated = reloaded.TagEdits[@"c:\m\A.FLAC"].ApplyTo(scanned with { Seconds = 220 });
        Assert.Equal(edit.ApplyTo(scanned), reloaded.Tracks.Single());
        Assert.Equal(edit.Title, updated.Name);
        Assert.Equal(220, updated.Seconds);
        Assert.Equal("Recopilación", updated.AlbumArtist);
        Assert.Equal(3u, updated.Disc);
        Assert.Equal(12u, updated.Number);
        Assert.Same(updated, edit.ApplyTo(updated));
        var legacyEdit = new LibraryTagEdit("Título antiguo", updated.Artist, updated.Album, updated.Genre, updated.Year);
        Assert.Equal(12u, legacyEdit.ApplyTo(updated).Number);
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

        library.Tracks[0] = library.Tracks[0] with { LastWriteUtcTicks = 42 };
        database.Save(library, true);
        Assert.Null(database.ReadArtwork(@"C:\M\a.flac"));
        Assert.Equal(artwork, database.ReadArtwork(@"C:\M\b.flac"));
    }

    [Fact]
    public void RelocationRelinksCachedArtworkToTheNewPath()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        const string first = @"C:\Old\song.flac";
        const string second = @"D:\New\song.flac";
        library.Tracks.Add(Song(first));
        database.Save(library, true);
        byte[] cachedCover = { 1, 2, 3 };
        database.SaveArtwork(first, cachedCover);
        library.Tracks[0] = library.Tracks[0] with { Path = second };

        database.Save(library, true, new System.Collections.Generic.Dictionary<string, string> { [first] = second });

        Assert.Null(database.ReadArtwork(first));
        Assert.Equal(cachedCover, database.ReadArtwork(second));
    }

    [Fact]
    public void PartialTagEditPreservesOtherTagsAfterReload()
    {
        var database = new LibraryDatabase(directory);
        var library = database.Load();
        var track = Song(@"C:\M\song.flac");
        library.Tracks.Add(track);
        library.TagEdits[track.Path] = new LibraryTagEdit(Genre: "Jazz", Year: 0);
        database.Save(library, true);

        var saved = new LibraryDatabase(directory).Load();
        var reread = track with { Name = "Nuevo título en disco", Disc = 2 };
        var merged = saved.TagEdits[track.Path].ApplyTo(reread);
        Assert.Equal(reread.Name, merged.Name);
        Assert.Equal(2u, merged.Disc);
        Assert.Equal("Jazz", merged.Genre);
        Assert.Equal(0u, merged.Year);
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
    public void RecoverySkipsDamagedNewestBackup()
    {
        var db = new LibraryDatabase(directory);
        var library = db.Load();
        library.Tracks.Add(Song(@"C:\M\a.flac"));
        db.Save(library, true);
        db.Load();

        var backups = Path.Combine(directory, "backups");
        string originalBackup = Assert.Single(Directory.GetFiles(backups, "*.sqlite3"));
        File.Copy(originalBackup, Path.Combine(backups, "library-20000101.sqlite3"));
        File.WriteAllText(originalBackup, "damaged backup");
        SqliteConnection.ClearAllPools();
        File.WriteAllText(Path.Combine(directory, "library.sqlite3"), "damaged library");

        Assert.Single(new LibraryDatabase(directory).Load().Tracks);
        Assert.Single(Directory.GetFiles(directory, "library.corrupt-*.sqlite3"));
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
    public void OnlySevenBackupsAreKept()
    {
        Directory.CreateDirectory(Path.Combine(directory, "backups"));
        for (int day = 1; day <= 8; day++)
            File.WriteAllText(Path.Combine(directory, "backups", $"library-2026010{day}.sqlite3"), "x");

        new LibraryDatabase(directory).Load();

        Assert.Equal(7, Directory.GetFiles(Path.Combine(directory, "backups"), "library-*.sqlite3").Length);
    }
}
