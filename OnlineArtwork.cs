using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrismaMusic;

internal static class OnlineArtwork
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> AlbumCovers =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim MusicBrainzGate = new(1, 1);
    private static DateTime nextMusicBrainzRequest;

    internal static async Task<byte[]> FindAlbumCover(string album, string artist)
    {
        if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist)) return null;
        string key = album.Trim() + "\n" + artist.Trim();
        Lazy<Task<byte[]>> request = AlbumCovers.GetOrAdd(key,
            _ => new Lazy<Task<byte[]>>(() => FetchAlbumCover(album, artist)));
        try { return await request.Value; }
        catch
        {
            if (AlbumCovers.TryGetValue(key, out Lazy<Task<byte[]>> current) && ReferenceEquals(current, request))
                AlbumCovers.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<byte[]> FetchAlbumCover(string album, string artist)
    {
        string query = $"release:\"{album}\" AND artist:\"{artist}\"";
        string releaseId = null;
        await MusicBrainzGate.WaitAsync();
        try
        {
            TimeSpan delay = nextMusicBrainzRequest - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            nextMusicBrainzRequest = DateTime.UtcNow.AddSeconds(1);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://musicbrainz.org/ws/2/release/?fmt=json&limit=5&query=" + Uri.EscapeDataString(query));
            request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.34 (https://github.com/josue758/PrismaMusic)");
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (!document.RootElement.TryGetProperty("releases", out JsonElement releases)) return null;
            foreach (JsonElement release in releases.EnumerateArray())
            {
                string title = release.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() : null;
                if (!string.Equals(title, album, StringComparison.OrdinalIgnoreCase)) continue;
                string creditedArtist = release.TryGetProperty("artist-credit", out JsonElement credits)
                    ? string.Concat(credits.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
                        .Select(item => item.TryGetProperty("name", out JsonElement name) ? name.GetString() : ""))
                    : null;
                if (!string.IsNullOrWhiteSpace(creditedArtist)
                    && !creditedArtist.Contains(artist, StringComparison.OrdinalIgnoreCase)) continue;
                if (release.TryGetProperty("id", out JsonElement id)) releaseId = id.GetString();
                if (!string.IsNullOrWhiteSpace(releaseId)) break;
            }
        }
        finally { MusicBrainzGate.Release(); }

        if (!Guid.TryParse(releaseId, out _)) return null;
        using var coverRequest = new HttpRequestMessage(HttpMethod.Get, $"https://coverartarchive.org/release/{releaseId}/front-500");
        coverRequest.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.34 (https://github.com/josue758/PrismaMusic)");
        using var coverResponse = await Http.SendAsync(coverRequest, HttpCompletionOption.ResponseHeadersRead);
        if (coverResponse.StatusCode == HttpStatusCode.NotFound) return null;
        if (!coverResponse.IsSuccessStatusCode) return null;
        if (coverResponse.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        const int maximumArtworkBytes = 6 * 1024 * 1024;
        if (coverResponse.Content.Headers.ContentLength > maximumArtworkBytes) return null;
        await using Stream input = await coverResponse.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory())) > 0)
        {
            if (output.Length + read > maximumArtworkBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.Length == 0 ? null : output.ToArray();
    }
}
