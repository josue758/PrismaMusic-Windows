using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PrismaMusic.Tests;

public class ApplicationUpdatesTests
{
    [Fact]
    public void FindsNewestPublishedVerifiedWindowsInstallerIncludingPreReleases()
    {
        using var releases = JsonDocument.Parse(JsonSerializer.Serialize(new[]
        {
            Release("0.57.0"),
            Release("0.59.0", draft: true),
            Release("0.60.0", trustedUrl: false),
            Release("0.61.0", digest: "sha256:unverified"),
            Release("0.58.0"),
            Release("0.62.0", state: "new")
        }));

        var update = ApplicationUpdates.FindUpdate(releases.RootElement, new Version(0, 56, 0, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 58, 0, 0), update.Version);
        Assert.Equal(32, update.Sha256.Length);
        Assert.Null(ApplicationUpdates.FindUpdate(releases.RootElement, update.Version));
    }

    [Theory]
    [InlineData("Prisma test installer", true)]
    [InlineData("Prisma fake installer", false)]
    [InlineData("Prisma truncated", false)]
    public async Task ChecksInstallerLengthAndDigest(string payload, bool verified)
    {
        var expected = System.Text.Encoding.UTF8.GetBytes("Prisma test installer");
        using var client = new HttpClient(new InstallerResponse(System.Text.Encoding.UTF8.GetBytes(payload)));
        var updater = new ApplicationUpdates(client);
        var update = new ApplicationUpdate(new Version(0, 57, 0, 0), new Uri("https://github.com/test"),
            expected.Length, SHA256.HashData(expected));
        if (!verified)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(update, null, CancellationToken.None));
            return;
        }

        string path = await updater.DownloadAsync(update, null, CancellationToken.None);
        try { Assert.Equal(expected, await File.ReadAllBytesAsync(path)); }
        finally { ApplicationUpdates.DiscardInstaller(path); }
    }

    private static object Release(string version, bool draft = false, bool trustedUrl = true, string digest = null,
        string state = "uploaded")
    {
        string tag = $"v{version}";
        string name = $"Prisma-Music-Windows-{version}-Setup-x64.exe";
        string root = trustedUrl ? "https://github.com" : "https://example.com";
        return new
        {
            draft,
            prerelease = true,
            tag_name = tag,
            assets = new[]
            {
                new
                {
                    name, state, size = 100L,
                    browser_download_url = $"{root}/josue758/PrismaMusic-Windows/releases/download/{tag}/{name}",
                    digest = digest ?? ("sha256:" + new string('a', 64))
                }
            }
        };
    }

    private sealed class InstallerResponse(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
