using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string SessionProbeDirectory()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Contains("--session-probe-write") || args.Contains("--session-probe-read")
            ? Environment.GetEnvironmentVariable("PRISMA_SESSION_PROBE")
                ?? throw new InvalidOperationException("Missing isolated session probe directory.")
            : null;
    }

    private async Task RunSessionProbe()
    {
        try
        {
            var demo = await Task.Run(() => LibraryScanner.ReadFolder(Path.Combine(AppContext.BaseDirectory, "Demo")));
            var tracks = demo.Tracks.OrderBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (tracks.Length != 3 || demo.Errors != 0) throw new InvalidOperationException("Demo library unavailable.");
            if (Environment.GetCommandLineArgs().Contains("--session-probe-write"))
            {
                library.Tracks = tracks.ToList();
                Save(true);
                queue.Add(tracks[2]);
                originalContext = tracks;
                context = new[] { tracks[1], tracks[2], tracks[0] };
                shuffle = true;
                repeat = RepeatMode.Track;
                startupSection = 2;
                LibrarySectionIndex = 1;
                await Play(tracks[1], false, 1.5);
                if (!opened) throw new InvalidOperationException("Cannot open demo for session probe.");
                if (Environment.GetEnvironmentVariable("PRISMA_SESSION_AUTOSAVE_PROBE") == "1")
                {
                    if (!SaveAudioSettings()) throw new InvalidOperationException("Could not write the test settings.");
                    StartSessionAutoSave();
                    for (int attempt = 0; attempt < 40 && !File.Exists(SessionFile); attempt++) await Task.Delay(250);
                    if (!File.Exists(SessionFile)) throw new InvalidOperationException("Autosave did not write the isolated session.");
                    Console.WriteLine("Session checkpoint written. Awaiting interruption.");
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    throw new InvalidOperationException("The session-autosave test process was not interrupted.");
                }
                Console.WriteLine("Closing process with paused track, playback order and settings.");
            }
            else
            {
                var pathComparer = StringComparer.OrdinalIgnoreCase;
                bool songRestored = pathComparer.Equals(current?.Path, tracks[1].Path);
                bool queueRestored = SameOrder(queue, new[] { tracks[2] });
                bool originalOrderRestored = SameOrder(originalContext, tracks);
                bool playbackOrderRestored = SameOrder(context, new[] { tracks[1], tracks[2], tracks[0] });
                double position = player.Position.TotalSeconds;
                if (!opened || playing || !songRestored || Math.Abs(position - 1.5) > .15 ||
                    !queueRestored || !originalOrderRestored || !playbackOrderRestored || !shuffle || repeat != RepeatMode.Track ||
                    startupSection != 2 || LibrarySectionIndex != 1)
                    throw new InvalidOperationException($"Session mismatch: opened={opened}, playing={playing}, track={songRestored}, " +
                        $"position={position:0.00}, queue={queueRestored}, original order={originalOrderRestored}, playback order={playbackOrderRestored}, " +
                        $"shuffle={shuffle}, repeat={repeat}, start={startupSection}, section={LibrarySectionIndex}.");
                Console.WriteLine("Reopened: paused track, position, queue, both playback orders, shuffle and repeat verified.");

                bool SameOrder(System.Collections.Generic.IEnumerable<Track> stored, System.Collections.Generic.IEnumerable<Track> expected) =>
                    stored.Select(track => track.Path).SequenceEqual(expected.Select(track => track.Path), pathComparer);
            }
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            System.Windows.Application.Current.Shutdown(1);
        }
    }
}
