using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyNetworkRetry(Track localTrack)
    {
        var prepare = prepareAudioFile;
        var previousTrack = current;
        var networkTrack = localTrack with { Path = @"\\127.0.0.1\PrismaValidation\track.m4a" };
        try
        {
            prepareAudioFile = (_, _) => throw new IOException("Simulated disconnected share.");
            await OpenTrack(networkTrack, remember: false, position: 1.25, autoplay: false, recordHistory: false);
            if (playbackRetry?.Track != networkTrack || current != previousTrack || RetryTrackButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("A NAS failure did not retain the intended track.");
            prepareAudioFile = (_, strict) => prepare(localTrack.Path, strict);
            await ResumeNetworkTrack();
            if (current != networkTrack || playbackRetry != null || playing || Math.Abs(player.Position.TotalSeconds - 1.25) > .15)
                throw new InvalidOperationException("NAS retry did not restore the requested position and pause state.");
            await Play(localTrack, false, 0);
            Console.WriteLine("NAS retry restored the pending song, position, and pause state.");
        }
        finally { prepareAudioFile = prepare; }
    }
    private async Task VerifyCancelableOpen(Track slow, Track latest)
    {
        var originalPrepare = prepareAudioFile;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        prepareAudioFile = (path, strict) =>
        {
            if (path == slow.Path)
            {
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Probe gate timed out.");
            }
            return originalPrepare(path, strict);
        };

        var original = current;
        var first = Play(slow, false, 0);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (pendingTrack != slow || current != original) throw new InvalidOperationException("Pending track committed early.");
            Toggle(this, new RoutedEventArgs());
            if (pendingTrack != null) throw new InvalidOperationException("Play did not cancel the pending file.");
            var second = Play(latest, false, 0);
            release.Set();
            await Task.WhenAll(first, second);
            if (!opened || current != latest || playing)
                throw new InvalidOperationException("A stale load replaced the latest selection.");
            Console.WriteLine("Cancelable open: UI responded, stale load discarded, latest track opened.");
        }
        finally
        {
            release.Set();
            await first;
            prepareAudioFile = originalPrepare;
        }
    }
}
