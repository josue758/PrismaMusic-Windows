using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Casting;

namespace PrismaMusic;

public partial class MainWindow
{
    private static async Task VerifyLyricsAndReceiverDiscovery()
    {
        var track = new Track("demo", "Água (Album Version)", "João", "Demo", "", "", 0, 0, 0, 180);
        var valid = new LyricsLookup.Candidate("Agua", "Joao", "Demo", 180, false, "Verse", "  ");
        var wrong = new[]
        {
            valid with { AlbumName = "Ao vivo" }, valid with { ArtistName = "Other" }, valid with { Duration = 184 }
        };
        if (LyricsLookup.Best(wrong, track) != null ||
            LyricsLookup.Best(new[] { valid }, track)?.Text != "Verse" ||
            LyricsLookup.Key("カ\u3099") != LyricsLookup.Key("ガ") || LyricsLookup.Key("ガ") == LyricsLookup.Key("カ"))
            throw new InvalidOperationException("Lyrics matching failed.");
        Console.WriteLine("Lyrics matching: artist, duration, version, accents and Japanese voicing verified.");
        if (Environment.GetEnvironmentVariable("PRISMA_HARDWARE_PROBE") == "1")
        {
            var selector = CastingDevice.GetDeviceSelector(CastingPlaybackTypes.Audio);
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                var receivers = await DeviceInformation.FindAllAsync(selector).AsTask(timeout.Token);
                Console.WriteLine($"Windows casting audio receivers: {receivers.Count}");
            }
            catch (OperationCanceledException) { Console.WriteLine("Windows casting discovery timed out."); }
        }
        if (Environment.GetEnvironmentVariable("PRISMA_LYRICS_PROBE") == "1")
        {
            var known = new Track("", "Get Lucky", "Daft Punk", "Random Access Memories", "", "", 2013, 0, 0, 369);
            if (string.IsNullOrWhiteSpace(await LyricsLookup.Find(Http, known, () => true)))
                throw new InvalidOperationException("LRCLIB did not return the expected known track.");
            Console.WriteLine("LRCLIB live lookup passed.");
        }
    }
}
