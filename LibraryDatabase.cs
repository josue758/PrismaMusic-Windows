using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PrismaMusic;

internal sealed class LibraryDatabase(string directory)
{
    private Dictionary<string, Track> savedTracks = new(StringComparer.OrdinalIgnoreCase);

    public Library Load()
    {
        using var db = Open();
        using var readState = db.CreateCommand();
        readState.CommandText = "SELECT payload FROM library_state WHERE id = 1";
        if (readState.ExecuteScalar() is not string payload)
        {
            var oldPath = Path.Combine(directory, "library.json");
            var legacy = File.Exists(oldPath)
                ? JsonSerializer.Deserialize<Library>(File.ReadAllText(oldPath)) ?? new Library()
                : new Library();
            legacy.Favorites = new HashSet<string>(legacy.Favorites, StringComparer.OrdinalIgnoreCase);
            Save(legacy, true);
            return legacy;
        }

        var library = JsonSerializer.Deserialize<Library>(payload) ?? throw new InvalidDataException("Biblioteca inválida.");
        library.Favorites = new HashSet<string>(library.Favorites, StringComparer.OrdinalIgnoreCase);
        using var readTracks = db.CreateCommand();
        readTracks.CommandText = "SELECT payload FROM tracks ORDER BY path";
        using var rows = readTracks.ExecuteReader();
        while (rows.Read())
            library.Tracks.Add(JsonSerializer.Deserialize<Track>(rows.GetString(0))
                ?? throw new InvalidDataException("Pista inválida en la biblioteca."));
        savedTracks = Index(library);
        return library;
    }

    public void Save(Library library, bool tracksChanged)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Dictionary<string, Track> next = null;
        if (tracksChanged)
        {
            next = Index(library);
            using var remove = db.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM tracks WHERE path = $path";
            var removePath = remove.Parameters.Add("$path", SqliteType.Text);
            foreach (string path in savedTracks.Keys)
            {
                if (next.ContainsKey(path)) continue;
                removePath.Value = path.ToUpperInvariant();
                remove.ExecuteNonQuery();
            }

            using var upsert = db.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO tracks(path, payload) VALUES ($path, $payload)
                ON CONFLICT(path) DO UPDATE SET payload = excluded.payload
                """;
            var upsertPath = upsert.Parameters.Add("$path", SqliteType.Text);
            var upsertPayload = upsert.Parameters.Add("$payload", SqliteType.Text);
            foreach (var (path, track) in next)
            {
                if (savedTracks.TryGetValue(path, out var old) && track == old) continue;
                upsertPath.Value = path.ToUpperInvariant();
                upsertPayload.Value = JsonSerializer.Serialize(track);
                upsert.ExecuteNonQuery();
            }
        }

        using var saveState = db.CreateCommand();
        saveState.Transaction = transaction;
        saveState.CommandText = """
            INSERT INTO library_state(id, payload) VALUES (1, $payload)
            ON CONFLICT(id) DO UPDATE SET payload = excluded.payload
            """;
        saveState.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(new
        {
            library.Folders,
            library.Favorites,
            library.Playlist,
            library.Playlists,
            library.History
        }));
        saveState.ExecuteNonQuery();
        transaction.Commit();
        if (next != null) savedTracks = next;
    }

    private static Dictionary<string, Track> Index(Library library) =>
        library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "library.sqlite3"),
            DefaultTimeout = 5,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var schema = connection.CreateCommand();
            schema.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS tracks(path TEXT PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS library_state(id INTEGER PRIMARY KEY CHECK(id = 1), payload TEXT NOT NULL);
                """;
            schema.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
