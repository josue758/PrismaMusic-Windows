using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PrismaMusic;

internal sealed record LibraryScanProgress(string Path, int FilesProcessed, int TracksFound, int Errors, Track CurrentTrack, string Error = null, int Reused = 0);

internal static class LibraryScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg", ".opus" };

    internal static string AudioFilePattern { get; } = string.Join(";", System.Linq.Enumerable.Select(Extensions, ext => "*" + ext));

    public static (List<Track> Tracks, int Errors) ReadFolder(string folder, CancellationToken cancellationToken = default,
        IProgress<LibraryScanProgress> progress = null, IReadOnlyDictionary<string, Track> cache = null,
        bool includeSubfolders = true, int parallelReads = 0,
        Action<IReadOnlyList<Track>> checkpoint = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tracks = new List<Track>();
        var pending = new List<Track>(256);
        int errors = 0;
        int processed = 0;
        int reused = 0;
        var gate = new object();
        long lastReport = Environment.TickCount64;
        int reportedCount = 0;
        (string Path, Track Track, string Error) last = default;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        // NAS file opens wait for network I/O. Cache hits stay in the directory enumerator
        // and never consume a worker reserved for reading tags.
        int workers = parallelReads > 0 ? parallelReads : folder.StartsWith(@"\\", StringComparison.Ordinal)
            ? Math.Clamp(Environment.ProcessorCount * 2, 4, 12)
            : Math.Clamp(Environment.ProcessorCount, 1, 4);
        Parallel.ForEach(FilesNeedingTags(), new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = workers
        }, entry =>
        {
            Track track;
            try { track = ReadTrack(entry.Path, entry.Length, entry.Stamp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
            {
                Record(entry.Path, null, ex.Message);
                return;
            }
            Record(entry.Path, track, null);
        });
        cancellationToken.ThrowIfCancellationRequested();
        FlushCheckpoint();
        if (processed != reportedCount && progress != null) Report();
        tracks.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return (tracks, errors);

        IEnumerable<(string Path, long Length, long Stamp)> FilesNeedingTags()
        {
            IEnumerable<FileInfo> files = Extensions.Contains(Path.GetExtension(folder)) && File.Exists(folder)
                ? new[] { new FileInfo(folder) }
                : EnumerateFiles(new DirectoryInfo(folder), options, cancellationToken,
                    (path, error) => Record(path, null, error), includeSubfolders);
            foreach (var info in files)
            {
                if (!Extensions.Contains(info.Extension)) continue;
                long length;
                long stamp;
                try
                {
                    length = info.Length;
                    stamp = info.LastWriteTimeUtc.Ticks;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Record(info.FullName, null, error.Message);
                    continue;
                }

                string path = info.FullName;
                if (cache != null && cache.TryGetValue(path, out var previous) &&
                    previous.FileLength == length && previous.LastWriteUtcTicks == stamp && stamp > 0)
                {
                    Record(path, previous, null, cached: true);
                    continue;
                }
                yield return (path, length, stamp);
            }
        }

        void Record(string path, Track track, string error, bool cached = false)
        {
            lock (gate)
            {
                if (track != null)
                {
                    tracks.Add(track);
                    if (!cached && checkpoint != null)
                    {
                        pending.Add(track);
                        if (pending.Count >= 256) FlushCheckpoint();
                    }
                }
                else errors++;
                if (cached) reused++;
                processed++;
                if (progress == null) return;
                last = (path, track, error);
                long now = Environment.TickCount64;
                if (now - lastReport < 150) return;
                lastReport = now;
                Report();
            }
        }

        void FlushCheckpoint()
        {
            if (pending.Count == 0) return;
            checkpoint?.Invoke(pending.ToArray());
            pending.Clear();
        }

        void Report()
        {
            reportedCount = processed;
            progress.Report(new LibraryScanProgress(last.Path, processed, tracks.Count, errors, last.Track, last.Error, reused));
        }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo root, EnumerationOptions options,
        CancellationToken cancellationToken, Action<string, string> reportUnavailableFolder, bool includeSubfolders)
    {
        var folders = new Stack<DirectoryInfo>();
        folders.Push(root);
        while (folders.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerator<FileSystemInfo> entries;
            try { entries = folder.EnumerateFileSystemInfos("*", options).GetEnumerator(); }
            catch (Exception error) when (folder != root && error is (IOException or UnauthorizedAccessException))
            {
                reportUnavailableFolder(folder.FullName, error.Message);
                continue;
            }
            using (entries)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { if (!entries.MoveNext()) break; }
                    catch (Exception error) when (folder != root && error is (IOException or UnauthorizedAccessException))
                    {
                        reportUnavailableFolder(folder.FullName, error.Message);
                        break;
                    }
                    if (entries.Current is DirectoryInfo child && includeSubfolders) folders.Push(child);
                    else if (entries.Current is FileInfo file) yield return file;
                }
            }
        }
    }

    private static Track ReadTrack(string path, long length, long stamp)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var audio = file.Properties ?? throw new IOException("No se pudieron leer las propiedades de audio.");
        string name = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title;
        return new Track(path, name, tag.FirstPerformer ?? "Artista desconocido",
            tag.Album ?? "Sin álbum", tag.FirstAlbumArtist ?? "", tag.FirstGenre ?? "",
            tag.Year, tag.Disc, tag.Track, (int)audio.Duration.TotalSeconds,
            ReadCodec(audio), audio.AudioSampleRate, audio.BitsPerSample, length, stamp);
    }

    private static string ReadCodec(TagLib.Properties audio)
    {
        foreach (var codec in audio.Codecs)
        {
            if (codec is TagLib.Flac.StreamHeader) return "FLAC";
            if (codec is TagLib.Riff.WaveFormatEx wave && (int)wave.FormatTag is 1 or 3) return "PCM";
            if (codec is TagLib.Mpeg4.IsoAudioSampleEntry mp4 && mp4.BoxType.ToString() == "alac") return "ALAC";
        }
        return null;
    }
}
