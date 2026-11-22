using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PrismaMusic;

internal sealed class LibraryDatabase(string directory)
{
    private static readonly JsonSerializerOptions TrackJson = new() { IgnoreReadOnlyProperties = true };

    private Dictionary<string, Track> savedTracks = new(StringComparer.OrdinalIgnoreCase);

    private string DatabasePath => Path.Combine(directory, "library.sqlite3");
    private string BackupDirectory => Path.Combine(directory, "backups");

    public Library Load()
    {
        RecoverIfCorrupt();
        var library = ReadLibrary();
        CreateBackup();
        return library;
    }

    // Si la base no pasa la verificación de integridad, se conserva aparte (library.corrupt-*.sqlite3)
    // y se restaura la copia de seguridad más reciente. Sin copia, la biblioteca vuelve a escanearse.
    private void RecoverIfCorrupt()
    {
        if (!File.Exists(DatabasePath)) return;
        bool healthy;
        try
        {
            using var db = Open();
            using var check = db.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            healthy = check.ExecuteScalar() as string == "ok";
        }
        catch (SqliteException) { healthy = false; }
        if (healthy) return;

        SqliteConnection.ClearAllPools();
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Move(DatabasePath, Path.Combine(directory, $"library.corrupt-{stamp}.sqlite3"));
        foreach (string side in new[] { "-wal", "-shm" })
            if (File.Exists(DatabasePath + side)) File.Delete(DatabasePath + side);
        string backup = Directory.Exists(BackupDirectory)
            ? Directory.GetFiles(BackupDirectory, "library-*.sqlite3").OrderDescending().FirstOrDefault()
            : null;
        if (backup != null) File.Copy(backup, DatabasePath);
    }

    // Una copia por día de uso, renovada en cada inicio sano; se conservan las cinco más recientes.
    private void CreateBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            string target = Path.Combine(BackupDirectory, $"library-{DateTime.Now:yyyyMMdd}.sqlite3");
            string temporary = target + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
            using (var source = Open())
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = temporary, Pooling = false }.ToString()))
            {
                copy.Open();
                source.BackupDatabase(copy);
            }
            File.Move(temporary, target, true);
            foreach (string old in Directory.GetFiles(BackupDirectory, "library-*.sqlite3").OrderDescending().Skip(5))
                File.Delete(old);
        }
        catch (Exception) { }
    }

    private Library ReadLibrary()
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
            legacy.TagEdits = new Dictionary<string, LibraryTagEdit>(legacy.TagEdits, StringComparer.OrdinalIgnoreCase);
            legacy.PlayCounts = new Dictionary<string, int>(legacy.PlayCounts, StringComparer.OrdinalIgnoreCase);
            Save(legacy, true);
            return legacy;
        }

        var library = JsonSerializer.Deserialize<Library>(payload) ?? throw new InvalidDataException("Biblioteca inválida.");
        library.Favorites = new HashSet<string>(library.Favorites, StringComparer.OrdinalIgnoreCase);
        library.TagEdits = new Dictionary<string, LibraryTagEdit>(library.TagEdits, StringComparer.OrdinalIgnoreCase);
        library.PlayCounts = new Dictionary<string, int>(library.PlayCounts, StringComparer.OrdinalIgnoreCase);
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
            using var unlinkArtwork = db.CreateCommand();
            unlinkArtwork.Transaction = transaction;
            unlinkArtwork.CommandText = "DELETE FROM track_artwork WHERE path = $path";
            var clearPath = unlinkArtwork.Parameters.Add("$path", SqliteType.Text);
            foreach (var (path, track) in next)
            {
                if (savedTracks.TryGetValue(path, out var old) && track == old) continue;
                upsertPath.Value = path.ToUpperInvariant();
                upsertPayload.Value = JsonSerializer.Serialize(track, TrackJson);
                upsert.ExecuteNonQuery();
                if (old != null && (old.FileLength != track.FileLength || old.LastWriteUtcTicks != track.LastWriteUtcTicks))
                {
                    clearPath.Value = upsertPath.Value;
                    unlinkArtwork.ExecuteNonQuery();
                }
            }

            using var removeArtwork = db.CreateCommand();
            removeArtwork.Transaction = transaction;
            removeArtwork.CommandText = """
                DELETE FROM track_artwork WHERE path NOT IN (SELECT path FROM tracks);
                DELETE FROM artwork WHERE hash NOT IN (SELECT hash FROM track_artwork);
                """;
            removeArtwork.ExecuteNonQuery();
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
            library.History,
            library.PlayCounts,
            library.TagEdits
        }));
        saveState.ExecuteNonQuery();
        transaction.Commit();
        if (next != null) savedTracks = next;
    }

    public byte[] ReadArtwork(string path)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT artwork.payload FROM track_artwork
            JOIN artwork ON artwork.hash = track_artwork.hash
            WHERE track_artwork.path = $path
            """;
        command.Parameters.AddWithValue("$path", path.ToUpperInvariant());
        return command.ExecuteScalar() as byte[];
    }

    public void SaveArtwork(string path, byte[] payload)
    {
        if (payload.Length == 0) return;
        string hash = Convert.ToHexString(SHA256.HashData(payload));
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var saveArtwork = db.CreateCommand();
        saveArtwork.Transaction = transaction;
        saveArtwork.CommandText = "INSERT OR IGNORE INTO artwork(hash, payload) VALUES ($hash, $payload)";
        saveArtwork.Parameters.AddWithValue("$hash", hash);
        saveArtwork.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
        saveArtwork.ExecuteNonQuery();

        using var linkArtwork = db.CreateCommand();
        linkArtwork.Transaction = transaction;
        linkArtwork.CommandText = """
            INSERT INTO track_artwork(path, hash) VALUES ($path, $hash)
            ON CONFLICT(path) DO UPDATE SET hash = excluded.hash
            """;
        linkArtwork.Parameters.AddWithValue("$path", path.ToUpperInvariant());
        linkArtwork.Parameters.AddWithValue("$hash", hash);
        linkArtwork.ExecuteNonQuery();
        transaction.Commit();
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
                PRAGMA synchronous = FULL;
                CREATE TABLE IF NOT EXISTS tracks(path TEXT PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS library_state(id INTEGER PRIMARY KEY CHECK(id = 1), payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS artwork(hash TEXT PRIMARY KEY, payload BLOB NOT NULL);
                CREATE TABLE IF NOT EXISTS track_artwork(path TEXT PRIMARY KEY, hash TEXT NOT NULL REFERENCES artwork(hash));
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
