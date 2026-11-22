using System;
using System.IO;
using System.Linq;
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
            var queued = queue.ToArray();
            await OpenTrack(networkTrack, remember: false, position: 4, autoplay: true, recordHistory: false);
            await HandleAudioFailure(new InvalidOperationException("Audio read interrupted.",
                new IOException("Simulated disconnected share.", unchecked((int)0x80070040))));
            if (playbackRetry is not { Interrupted: true, Autoplay: true } interrupted ||
                interrupted.Position < 3.9 || opened || playing || !queue.SequenceEqual(queued))
                throw new InvalidOperationException("An interrupted NAS track lost the playback cursor or queue.");
            prepareAudioFile = (_, _) => throw new IOException("Still disconnected.");
            await ResumeNetworkTrack();
            if (playbackRetry is not { Interrupted: true } waiting || waiting.Position != interrupted.Position)
                throw new InvalidOperationException("Retrying while offline lost the resume position.");
            prepareAudioFile = (_, strict) => prepare(localTrack.Path, strict);
            await ResumeNetworkTrack();
            if (current != networkTrack || !opened || !playing || playbackRetry != null ||
                Math.Abs(player.Position.TotalSeconds - interrupted.Position) > .45 || !queue.SequenceEqual(queued))
                throw new InvalidOperationException("NAS reconnection did not resume the interrupted track and queue.");
            await Play(localTrack, false, 0);
            Console.WriteLine("NAS retry restored pending and interrupted tracks, position, pause and playback state, and queue.");
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
