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
                var audio = file.Properties;
                string codec = ReadCodec(audio);
                string name = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title;
                currentTrack = new Track(path, name, tag.FirstPerformer ?? "Artista desconocido",
                    tag.Album ?? "Sin álbum", tag.FirstAlbumArtist ?? "", tag.FirstGenre ?? "",
                    tag.Year, tag.Disc, tag.Track, (int)audio.Duration.TotalSeconds,
                    codec, audio.AudioSampleRate, audio.BitsPerSample);
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

    private static string ReadCodec(TagLib.Properties audio)
    {
        foreach (var codec in audio.Codecs)
        {
            if (codec is TagLib.Flac.StreamHeader) return "FLAC";
            if (codec is TagLib.Riff.WaveFormatEx wave && (int)wave.FormatTag is 1 or 3) return "PCM";
            if (codec is TagLib.Mpeg4.IsoAudioSampleEntry mp4 && mp4.BoxType.ToString() == "alac") return "ALAC";
        }
        // AIFF-C and WAV can contain compressed audio. Only mark recognized codecs as lossless.
        return null;
    }
}
