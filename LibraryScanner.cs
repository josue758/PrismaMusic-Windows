using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PrismaMusic;

internal sealed record LibraryScanProgress(string Path, int FilesProcessed, int TracksFound, int Errors, Track CurrentTrack, string Error = null);

internal static class LibraryScanner
{
    private static readonly HashSet<string> Extensions = new(System.StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg", ".opus" };

    public static (List<Track> Tracks, int Errors) ReadFolder(string folder, CancellationToken cancellationToken = default,
        IProgress<LibraryScanProgress> progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tracks = new List<Track>();
        int errors = 0;
        int filesProcessed = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(path))) continue;
            Track currentTrack = null;
            string error = null;
            try
            {
                using var file = TagLib.File.Create(path);
                var tag = file.Tag;
                string name = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title;
                currentTrack = new Track(path, name, tag.FirstPerformer ?? "Artista desconocido",
                    tag.Album ?? "Sin álbum", tag.FirstAlbumArtist ?? "", tag.FirstGenre ?? "",
                    tag.Year, tag.Disc, tag.Track, (int)file.Properties.Duration.TotalSeconds);
                tracks.Add(currentTrack);
            }
            catch (System.Exception exception) { errors++; error = exception.Message; }
            finally
            {
                filesProcessed++;
                progress?.Report(new LibraryScanProgress(path, filesProcessed, tracks.Count, errors, currentTrack, error));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return (tracks, errors);
    }
}
