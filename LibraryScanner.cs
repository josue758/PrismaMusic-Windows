using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PrismaMusic;

internal sealed record LibraryScanProgress(string Path, int FilesProcessed, int TracksFound, int Errors, Track CurrentTrack, string Error = null);

internal static class LibraryScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg", ".opus" };

    public static (List<Track> Tracks, int Errors) ReadFolder(string folder, CancellationToken cancellationToken = default,
        IProgress<LibraryScanProgress> progress = null, IReadOnlyDictionary<string, Track> cache = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tracks = new List<Track>();
        int errors = 0;
        int processed = 0;
        var gate = new object();
        long lastReport = Environment.TickCount64;
        LibraryScanProgress latest = null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var files = EnumerateFiles(new DirectoryInfo(folder), options, cancellationToken,
            (path, error) => Record(path, null, error));
        Parallel.ForEach(files, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
        }, info =>
        {
            if (!Extensions.Contains(info.Extension)) return;
            Track track = null;
            string error = null;
            try
            {
                long length = info.Length;
                long stamp = info.LastWriteTimeUtc.Ticks;
                track = cache != null && cache.TryGetValue(info.FullName, out var previous) &&
                    previous.FileLength == length && previous.LastWriteUtcTicks == stamp && stamp > 0
                    ? previous : ReadTrack(info.FullName, length, stamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
            {
                error = ex.Message;
            }

            Record(info.FullName, track, error);
        });

        void Record(string path, Track track, string error)
        {
            lock (gate)
            {
                if (track != null) tracks.Add(track);
                else errors++;
                processed++;
                if (progress == null) return;
                latest = new LibraryScanProgress(path, processed, tracks.Count, errors, track, error);
                long now = Environment.TickCount64;
                if (now - lastReport < 150) return;
                lastReport = now;
                progress.Report(latest);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (latest != null) progress.Report(latest);
        tracks.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return (tracks, errors);
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo root, EnumerationOptions options,
        CancellationToken cancellationToken, Action<string, string> reportUnavailableFolder)
    {
        var folders = new Stack<DirectoryInfo>();
        folders.Push(root);
        while (folders.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileSystemInfo[] entries;
            try { entries = folder.GetFileSystemInfos("*", options); }
            catch (Exception error) when (folder != root && error is (IOException or UnauthorizedAccessException))
            {
                reportUnavailableFolder(folder.FullName, error.Message);
                continue;
            }
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is DirectoryInfo child) folders.Push(child);
                else if (entry is FileInfo file) yield return file;
            }
        }
    }

    private static Track ReadTrack(string path, long length, long stamp)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var audio = file.Properties;
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
