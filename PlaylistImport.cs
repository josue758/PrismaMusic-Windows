using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PrismaMusic;

internal sealed record PlaylistMatch(string Original, Track Track, bool Remapped);

internal static class PlaylistImport
{
    internal static string[] ReadEntries(string playlist)
    {
        var bytes = File.ReadAllBytes(playlist);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) when (Path.GetExtension(playlist).Equals(".m3u", StringComparison.OrdinalIgnoreCase))
        {
            var legacy = CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            text = (legacy ?? Encoding.Latin1).GetString(bytes);
        }
        return text.TrimStart('\uFEFF').Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#')).ToArray();
    }

    internal static PlaylistMatch[] Match(IEnumerable<string> entries, string folder, IReadOnlyList<Track> tracks,
        string oldRoot = null, string newRoot = null)
    {
        var paths = tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var suffixes = tracks.ToLookup(track => Suffix(track.Path), StringComparer.OrdinalIgnoreCase);
        string previous = oldRoot?.Trim().Replace('/', '\\').TrimEnd('\\');
        string destination = newRoot?.Trim();
        return entries.Select(entry =>
        {
            string path = entry.Replace('/', '\\');
            if (Uri.TryCreate(entry, UriKind.Absolute, out var uri) && entry.Contains("://", StringComparison.Ordinal))
            {
                if (!uri.IsFile && uri.Scheme != "smb") return new PlaylistMatch(entry, null, false);
                path = uri.IsFile ? uri.LocalPath : $@"\\{uri.Host}{Uri.UnescapeDataString(uri.AbsolutePath).Replace('/', '\\')}";
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(previous) && !string.IsNullOrWhiteSpace(destination) &&
                    path.StartsWith(previous + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string mapped = Path.GetFullPath(Path.Combine(destination, path[(previous.Length + 1)..]));
                    if (paths.TryGetValue(mapped, out var relocated)) return new PlaylistMatch(entry, relocated, true);
                }
                string absolute = Path.GetFullPath(path, folder);
                if (paths.TryGetValue(absolute, out var found)) return new PlaylistMatch(entry, found, false);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
            string suffix = Suffix(path);
            var candidates = suffix.Length == 0 ? Array.Empty<Track>() : suffixes[suffix].Take(2).ToArray();
            return candidates.Length == 1 ? new PlaylistMatch(entry, candidates[0], true) : new PlaylistMatch(entry, null, false);
        }).ToArray();
    }

    private static string Suffix(string path)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] + "\\" + parts[^1] : "";
    }
}
