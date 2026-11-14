using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PrismaMusic;

public partial class MainWindow
{
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
