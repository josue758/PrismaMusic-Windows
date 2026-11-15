using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PrismaMusic;

internal sealed record ApplicationUpdate(Version Version, Uri Download, long Size, byte[] Sha256);

internal sealed class ApplicationUpdates(HttpClient client)
{
    private const string Repository = "josue758/PrismaMusic-Windows";
    private const long MaximumInstallerSize = 300L * 1024 * 1024;
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrismaMusic.Windows_is1";
    internal static Version InstalledVersion => typeof(ApplicationUpdates).Assembly.GetName().Version;

    internal static ApplicationUpdates Create()
    {
        var client = new HttpClient { MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrismaMusic/" + InstalledVersion.ToString(3));
        return new ApplicationUpdates(client);
    }

    internal static string InstallationDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || !Path.IsPathFullyQualified(location)) return null;
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            var expectedExe = Path.Combine(directory, "Prisma Music.exe");
            return string.Equals(Environment.ProcessPath, expectedExe, StringComparison.OrdinalIgnoreCase)
                ? directory : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return null;
        }
    }

    internal async Task<ApplicationUpdate> CheckAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=30", timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var releases = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
        return FindUpdate(releases.RootElement, InstalledVersion);
    }

    internal static ApplicationUpdate FindUpdate(JsonElement releases, Version installed)
    {
        ApplicationUpdate best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString();
            if (tag?.StartsWith('v') != true || !Version.TryParse(tag[1..], out var parsed) || parsed.Build < 0 ||
                parsed.Revision >= 0 || tag != $"v{parsed.ToString(3)}") continue;
            var version = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
            if (version <= installed || (best != null && version <= best.Version)) continue;

            string name = $"Prisma-Music-Windows-{parsed}-Setup-x64.exe";
            string url = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name ||
                    asset.GetProperty("state").GetString() != "uploaded" ||
                    asset.GetProperty("browser_download_url").GetString() != url ||
                    !asset.TryGetProperty("digest", out var digestValue)) continue;
                string digest = digestValue.GetString();
                long size = asset.GetProperty("size").GetInt64();
                if (size <= 0 || size > MaximumInstallerSize || digest is not { Length: 71 } ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                    digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0) continue;
                best = new ApplicationUpdate(version, new Uri(url), size, Convert.FromHexString(digest.AsSpan(7)));
                break;
            }
        }
        return best;
    }

    internal static void RemoveExpiredInstallers()
    {
        try
        {
            foreach (var directory in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories("PrismaMusic-Update-*"))
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    directory.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-2))
                    DiscardInstaller(Path.Combine(directory.FullName, "Prisma Music Setup.exe"));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal async Task<string> DownloadAsync(ApplicationUpdate update, IProgress<double> progress, CancellationToken cancellation)
    {
        string directory = Directory.CreateTempSubdirectory("PrismaMusic-Update-").FullName;
        string path = Path.Combine(directory, "Prisma Music Setup.exe");
        try
        {
            await DownloadVerifiedAsync(update, path, progress, cancellation).ConfigureAwait(false);
            return path;
        }
        catch
        {
            DiscardInstaller(path);
            throw;
        }
    }

    internal async Task DownloadVerifiedAsync(ApplicationUpdate update, string path, IProgress<double> progress,
        CancellationToken cancellation)
    {
        using var response = await client.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != update.Size)
            throw new InvalidDataException("Unexpected installer length.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long downloaded = 0;
            int lastPercent = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
            {
                downloaded += read;
                if (downloaded > update.Size) throw new InvalidDataException("Installer exceeded expected length.");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                int percent = (int)(100 * downloaded / update.Size);
                if (percent == lastPercent) continue;
                lastPercent = percent;
                progress?.Report(percent);
            }
            if (downloaded != update.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), update.Sha256))
                throw new InvalidDataException("Installer checksum did not match.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static void LaunchInstaller(string path, string installation, string language)
    {
        var start = new ProcessStartInfo(path) { WorkingDirectory = installation };
        foreach (string argument in new[]
        {
            "/SP-", "/SILENT", "/NORESTART", "/NOCLOSEAPPLICATIONS", $"/DIR={installation}", $"/LANG={language}",
            $"/PRISMAUPDATEPID={Environment.ProcessId}"
        })
            start.ArgumentList.Add(argument);
        using var setup = Process.Start(start) ?? throw new Win32Exception("Could not launch Prisma Music Setup.");
    }

    internal static void DiscardInstaller(string path)
    {
        if (path == null) return;
        try
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
