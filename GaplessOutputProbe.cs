using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyGaplessOutput()
    {
        var restoreTrack = current;
        var previousContext = context;
        var previousRepeat = repeat;
        var previousReplay = player.ReplayMode;
        bool previousContinue = continueAfterTrack;
        var previousQueue = queue.ToArray();
        using var folder = new GaplessFixture();
        Track first = folder.Create("First", 16384);
        Track second = folder.Create("Second", 8192);
        var seamless = new TaskCompletionSource<Track>(TaskCreationOptions.RunContinuationsAsynchronously);
        void DidTransition(object sender, Track track) => seamless.TrySetResult(track);
        player.ContinuousTransition += DidTransition;
        try
        {
            gaplessProbeRunning = true;
            player.ReplayMode = ReplayGainMode.Off;
            player.Speed = 1;
            repeat = RepeatMode.Off;
            continueAfterTrack = true;
            SetStopAfterTrack(false);
            context = new[] { first };
            queue.Clear();
            queue.Add(second);
            await OpenTrack(first, false, 0, true, false);
            if (!opened) throw new InvalidOperationException("WASAPI could not open the first gapless fixture: " + Status.Text);
            var played = await seamless.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (played != second || current != second || queue.Count != 0 || !playing || !opened)
                throw new InvalidOperationException("The continuous WASAPI transition lost the next song, queue or playback state.");
            Console.WriteLine("Gapless: compatible PCM tracks transitioned through the same WASAPI output and consumed one queue entry.");
        }
        finally
        {
            gaplessProbeRunning = false;
            player.ContinuousTransition -= DidTransition;
            InvalidateGapless();
            context = previousContext;
            repeat = previousRepeat;
            continueAfterTrack = previousContinue;
            player.ReplayMode = previousReplay;
            queue.Clear();
            queue.AddRange(previousQueue);
            if (restoreTrack != null) await Play(restoreTrack, false, 0);
            else player.Close();
        }
    }

    private sealed class GaplessFixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("prisma-gapless-");
        internal Track Create(string name, short amplitude)
        {
            string path = Path.Combine(directory.FullName, name + ".wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 2)))
            {
                for (int frame = 0; frame < 96000; frame++)
                {
                    short sample = frame % 120 < 60 ? amplitude : (short)-amplitude;
                    writer.WriteSample(sample / 32768f);
                    writer.WriteSample(sample / 32768f);
                }
            }
            return LibraryScanner.ReadFile(path);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
