using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record PortraitCredit(string Creator, string License, string Page);

    private static async Task<PortraitCredit> FindPortraitCredit(JsonElement page)
    {
        if (!page.TryGetProperty("pageimage", out var name) ||
            !Uri.TryCreate(page.GetProperty("fullurl").GetString(), UriKind.Absolute, out var article) ||
            article.Host is not ("en.wikipedia.org" or "es.wikipedia.org" or "pt.wikipedia.org"))
            return null;
        string url = $"https://{article.Host}/w/api.php?action=query&format=json&formatversion=2" +
            "&prop=imageinfo&iiprop=url%7Cextmetadata&iiextmetadatafilter=Artist%7CLicenseShortName" +
            "&titles=" + Uri.EscapeDataString("File:" + name.GetString());
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.27 (https://github.com/josue758/PrismaMusic)");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) || pages.GetArrayLength() == 0 ||
            !pages[0].TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0)
            return null;
        var image = info[0];
        string description = image.TryGetProperty("descriptionurl", out var link) ? link.GetString() : null;
        if (!image.TryGetProperty("extmetadata", out var metadata)) return new PortraitCredit("", "", description);
        string Value(string key) => metadata.TryGetProperty(key, out var node) && node.TryGetProperty("value", out var raw)
            ? WebUtility.HtmlDecode(Regex.Replace(raw.GetString() ?? "", "<[^>]+>", " ")).Trim() : "";
        return new PortraitCredit(Value("Artist"), Value("LicenseShortName"), description);
    }
}
