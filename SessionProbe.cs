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
                Navigation.SelectedIndex = 1;
                await Play(tracks[1], false, 1.5);
                if (!opened) throw new InvalidOperationException("Cannot open demo for session probe.");
                Console.WriteLine("Closing process with paused track, playback order and settings.");
            }
            else
            {
                if (!opened || playing || current != tracks[1] || Math.Abs(player.Position.TotalSeconds - 1.5) > .15 ||
                    !queue.SequenceEqual(new[] { tracks[2] }) || !originalContext.SequenceEqual(tracks) ||
                    !context.SequenceEqual(new[] { tracks[1], tracks[2], tracks[0] }) || !shuffle || repeat != RepeatMode.Track ||
                    startupSection != 2 || Navigation.SelectedIndex != 1)
                    throw new InvalidOperationException("Session did not survive a process restart.");
                Console.WriteLine("Reopened: paused track, position, queue, both playback orders, shuffle and repeat verified.");
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
