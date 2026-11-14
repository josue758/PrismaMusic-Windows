using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PrismaMusic;

internal static class LyricsLookup
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex AlbumEdition = new(@"\s*[\(\[]album\s+(version|edit)[\)\]]\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex[] Versions =
    {
        new(@"\blive\b|\bao vivo\b|\ben vivo\b|ライブ", RegexOptions.IgnoreCase),
        new(@"\bremix(?:ed)?\b", RegexOptions.IgnoreCase),
        new(@"\bacoustic\b|\bunplugged\b|\bacústico\b", RegexOptions.IgnoreCase),
        new(@"\bkaraoke\b|\binstrumental\b", RegexOptions.IgnoreCase)
    };

    internal record Candidate(string TrackName, string ArtistName, string AlbumName, double Duration,
        bool Instrumental, string PlainLyrics, string SyncedLyrics)
    {
        public string Text => !string.IsNullOrWhiteSpace(SyncedLyrics) ? SyncedLyrics : PlainLyrics;
    }

    internal static string Key(string text)
    {
        var key = new StringBuilder();
        foreach (Rune rune in (text ?? "").Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune)) continue;
            if (rune.Value <= 0x024F)
            {
                foreach (char value in rune.ToString().Normalize(NormalizationForm.FormD))
                    if (char.GetUnicodeCategory(value) != UnicodeCategory.NonSpacingMark)
                        key.Append(char.ToUpperInvariant(value));
            }
            else key.Append(Rune.ToUpperInvariant(rune).ToString());
        }
        return key.ToString();
    }

    private static string BaseTitle(string title) => AlbumEdition.Replace(title ?? "", "");
    private static int VersionFlags(string title, string album) =>
        Versions.Select((marker, index) => marker.IsMatch(title + " " + album) ? 1 << index : 0).Sum();

    internal static Candidate Best(Candidate[] candidates, Track track)
    {
        string title = Key(BaseTitle(track.Name));
        string artist = Key(track.Artist);
        if (track.Seconds <= 0 || title.Length == 0 || artist.Length == 0) return null;
        string album = Key(track.Album);
        int version = VersionFlags(track.Name, track.Album);
        return candidates
            .Where(item => item != null && !item.Instrumental && !string.IsNullOrWhiteSpace(item.Text) &&
                Key(BaseTitle(item.TrackName)) == title && Key(item.ArtistName) == artist &&
                Math.Abs(item.Duration - track.Seconds) <= 2 && VersionFlags(item.TrackName, item.AlbumName) == version)
            .OrderByDescending(item => (album.Length > 0 && Key(item.AlbumName) == album ? 10 : 0) +
                (!string.IsNullOrWhiteSpace(item.SyncedLyrics) ? 4 : 0) - Math.Abs(item.Duration - track.Seconds))
            .FirstOrDefault();
    }

    internal static async Task<string> Find(HttpClient client, Track track, Func<bool> stillCurrent)
    {
        if (track.Seconds <= 0) return null;
        string Args(string title) => $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(track.Artist)}";
        string exact = "get?" + Args(track.Name) + $"&album_name={Uri.EscapeDataString(track.Album)}&duration={track.Seconds}";
        foreach (string endpoint in new[] { exact, "search?" + Args(track.Name), "search?" + Args(BaseTitle(track.Name)) }.Distinct())
        {
            if (!stillCurrent()) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://lrclib.net/api/" + endpoint);
            request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.25 (https://github.com/josue758/PrismaMusic)");
            using var response = await client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            if (!stillCurrent()) return null;
            string body = await response.Content.ReadAsStringAsync();
            var candidates = endpoint.StartsWith("get?", StringComparison.Ordinal)
                ? new[] { JsonSerializer.Deserialize<Candidate>(body, Json) }
                : JsonSerializer.Deserialize<Candidate[]>(body, Json) ?? Array.Empty<Candidate>();
            if (Best(candidates, track) is Candidate found) return found.Text;
        }
        return null;
    }
}
