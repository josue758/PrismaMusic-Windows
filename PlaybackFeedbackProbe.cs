using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyPlaybackFeedback(Track track)
    {
        string fixture = Path.Combine(Path.GetTempPath(), $"prisma-availability-{Guid.NewGuid():N}.flac");
        var hiddenTrack = track with { Path = fixture };
        var retry = new PlaybackRetry(hiddenTrack, false, 6, true, false, Interrupted: true);
        try
        {
            playbackRetry = retry;
            RetryTrackButton.Visibility = Visibility.Visible;
            ShowNasRecovery(retry, available: false);
            if (NasRecoveryTitle.Text != T("Esperando al NAS"))
                throw new InvalidOperationException("A disconnected song did not display the waiting state.");
            File.WriteAllBytes(fixture, new byte[] { 1 });
            nextNasCheck = 0;
            CheckNasRecovery();
            for (int attempt = 0; attempt < 30 && nasCheckRunning; attempt++) await Task.Delay(25);
            if (NasRecoveryTitle.Text != T("Listo para reanudar") || !ReferenceEquals(retry, playbackRetry) ||
                !NasRecoveryPosition.Text.Contains("0:06"))
                throw new InvalidOperationException("The NAS availability notice lost the original resume position.");
            AnnouncePlayback("Prisma");
            if (AutomationProperties.GetName(PlaybackAnnouncement) != "Prisma" ||
                AutomationProperties.GetLiveSetting(PlaybackAnnouncement) != AutomationLiveSetting.Polite)
                throw new InvalidOperationException("Playback announcements lack an accessible live region.");
            Console.WriteLine("NAS readiness, preserved playback position and accessibility live region passed.");
        }
        finally
        {
            playbackRetry = null;
            NasRecoveryNotice.Visibility = Visibility.Collapsed;
            RetryTrackButton.Visibility = Visibility.Collapsed;
            File.Delete(fixture);
        }
    }
}
