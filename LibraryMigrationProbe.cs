using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrismaMusic;

public partial class MainWindow
{
    private static void VerifyLibraryMigration(string directory)
    {
        Directory.CreateDirectory(directory);
        var legacy = new Library
        {
            Tracks = Enumerable.Range(0, 10000)
                .Select(i => new Track($@"Z:\Music\{i}.flac", $"Track {i}", "Artist", "Album", "Artist", "Jazz", 2025, 1, 1, 60))
                .ToList()
        };
        string favorite = legacy.Tracks[2].Path;
        legacy.Folders.Add(@"Z:\Music");
        legacy.Favorites.Add(favorite);
        legacy.Playlist.Add(favorite);
        legacy.Playlists["Jazz"] = new() { favorite };
        legacy.History.Add(favorite);
        string source = Path.Combine(directory, "library.json");
        string original = JsonSerializer.Serialize(legacy);
        File.WriteAllText(source, original);
        var database = new LibraryDatabase(directory);
        var migrated = database.Load();
        if (migrated.Tracks.Count != 10000 || !migrated.Favorites.Contains(favorite.ToLowerInvariant()) ||
            migrated.Playlist.Single() != favorite || migrated.Playlists["Jazz"].Single() != favorite ||
            migrated.History.Single() != favorite || migrated.Folders.Single() != legacy.Folders.Single())
            throw new InvalidOperationException("Legacy migration did not preserve the library.");
        migrated.Tracks[0] = migrated.Tracks[0] with { Name = "Edited" };
        migrated.Tracks.RemoveAt(1);
        database.Save(migrated, true);
        migrated.History.Clear();
        database.Save(migrated, false);
        var restored = new LibraryDatabase(directory).Load();
        if (restored.Tracks.Count != 9999 || !restored.Tracks.Any(track => track.Name == "Edited") ||
            restored.History.Count != 0 || File.ReadAllText(source) != original)
            throw new InvalidOperationException("SQLite reopen did not preserve the updates or the original backup.");
        Console.WriteLine("SQLite: 10,000 legacy tracks, incremental updates, and reopen passed.");
    }
}
