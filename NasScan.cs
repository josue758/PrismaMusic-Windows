using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PrismaMusic;

internal sealed record NasScanCheckpoint(bool Subfolders, bool OnlyNew, string[] Pending, string[] Completed)
{
    private static string FilePath(string storage, string source)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())));
        return Path.Combine(storage, "scan-progress", key + ".json");
    }

    internal static NasScanCheckpoint Read(string storage, string source)
    {
        try
        {
            string path = FilePath(storage, source);
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<NasScanCheckpoint>(File.ReadAllText(path));
            return state?.Pending is { Length: > 0 } && state.Pending.Concat(state.Completed ?? Array.Empty<string>()).All(candidate =>
                candidate != null && string.Equals(Path.GetFullPath(candidate), candidate, StringComparison.OrdinalIgnoreCase) &&
                (candidate.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                 candidate.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                ? state : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine(error);
            return null;
        }
    }

    internal static void Write(string storage, string source, NasScanCheckpoint state)
    {
        string path = FilePath(storage, source);
        if (state.Pending.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}

internal sealed class NasScanRun(string source, bool subfolders, bool onlyNew, NasScanCheckpoint checkpoint)
{
    private readonly Queue<string> remaining = new(checkpoint?.Pending ?? new[] { source });
    private readonly HashSet<string> scheduled = new(checkpoint?.Pending ?? new[] { source }, StringComparer.OrdinalIgnoreCase);
    private readonly List<string> failed = new();
    private readonly HashSet<string> completed = new(checkpoint?.Completed ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    internal List<Track> Tracks { get; } = new();
    internal bool Resumed => checkpoint != null;
    internal NasScanCheckpoint Snapshot() => new(subfolders, onlyNew,
        failed.Concat(remaining).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), completed.ToArray());

    internal (List<Track>, int) Read(CancellationToken token, IProgress<LibraryScanProgress> progress,
        IReadOnlyDictionary<string, Track> original, int readers, Action<Track> onNew, Action<string, string> onFailure,
        Func<string, Action<string>, (List<Track> Tracks, int Errors)> readDirectory = null, Action<string> directoryStarting = null)
    {
        int errors = 0, reused = 0, processed = 0;
        while (remaining.TryPeek(out string directory))
        {
            token.ThrowIfCancellationRequested();
            if (completed.Contains(directory)) { remaining.Dequeue(); continue; }
            directoryStarting?.Invoke(directory);
            var children = new List<string>();
            try
            {
                var result = readDirectory != null ? readDirectory(directory, children.Add)
                    : LibraryScanner.ReadFolder(directory, token,
                        progress == null ? null : new DirectoryProgress(value => progress.Report(value with
                        { FilesProcessed = processed + value.FilesProcessed, Errors = errors + value.Errors, Reused = reused + value.Reused })),
                        original, includeSubfolders: false, parallelReads: readers, onlyNew: onlyNew,
                        onNewTrack: onNew, onFailure: onFailure, onChildDirectory: subfolders ? children.Add : null);
                Tracks.AddRange(result.Tracks);
                if (result.Errors > 0) failed.Add(directory);
                else completed.Add(directory);
                errors += result.Errors;
                processed += result.Tracks.Count + result.Errors;
                reused += result.Tracks.Count(track => original.TryGetValue(track.Path, out var old) && ReferenceEquals(old, track));
                remaining.Dequeue();
                // A failed directory is enumerated again on resume, including its children if needed.
                foreach (string child in children)
                    if (scheduled.Add(child) && !completed.Contains(child)) remaining.Enqueue(child);
            }
            catch (Exception failure) when (failure.GetBaseException() is IOException or UnauthorizedAccessException)
            {
                var error = failure.GetBaseException();
                onFailure(directory, error.Message);
                errors++;
                if (NetworkMusicSource.IsDisconnected(error)) break;
                failed.Add(remaining.Dequeue());
            }
        }
        return (Tracks, errors);
    }

    private sealed class DirectoryProgress(Action<LibraryScanProgress> report) : IProgress<LibraryScanProgress>
    {
        public void Report(LibraryScanProgress value) => report(value);
    }
}

public partial class MainWindow
{
    private string ScanCheckpointDirectory => smokeTest ? nasValidationStorage : DataDirectory;
    private string nasValidationStorage;
    private Action<string> nasValidationBeforeDirectory;

    private async Task ResumeNasScan(string source)
    {
        if (ScanCheckpointDirectory == null || !library.Folders.Contains(source, StringComparer.OrdinalIgnoreCase) ||
            NasScanCheckpoint.Read(ScanCheckpointDirectory, source) is not { } checkpoint) return;
        await Scan(new[] { source }, add: false, nasCheckpoint: checkpoint);
    }

    private async void ResumeLastNasScan(object sender, RoutedEventArgs args)
    {
        if (ResumeNasScanButton.Tag is string source) await ResumeNasScan(source);
    }

    private void SaveNasProgress(Dictionary<string, NasScanRun> runs)
    {
        if (ScanCheckpointDirectory == null || database == null || unsavedTracks) return;
        try
        {
            foreach (var (source, run) in runs)
            {
                var state = run.Snapshot();
                NasScanCheckpoint.Write(ScanCheckpointDirectory, source, state);
                if (state.Pending.Length > 0 && library.Folders.Contains(source, StringComparer.OrdinalIgnoreCase))
                {
                    ResumeNasScanButton.Tag = source;
                    ResumeNasScanButton.ToolTip = source;
                    ResumeNasScanButton.Visibility = Visibility.Visible;
                }
                else if (Equals(ResumeNasScanButton.Tag, source))
                {
                    ResumeNasScanButton.Tag = null;
                    ResumeNasScanButton.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status.Text = T("No se pudo guardar el progreso del NAS. ") + error.Message;
        }
    }
}
