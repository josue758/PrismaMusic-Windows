using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;

namespace PrismaMusic;

public partial class MainWindow
{
    private long nextNasCheck;
    private bool nasCheckRunning;
    private bool lastNasAvailable;

    private void ShowNasRecovery(PlaybackRetry retry, bool available)
    {
        lastNasAvailable = available;
        nextNasCheck = Environment.TickCount64 + 3500;
        NasRecoveryNotice.Visibility = Visibility.Visible;
        NasRecoveryTitle.Text = T(available ? "Listo para reanudar" : "Esperando al NAS");
        NasRecoveryPosition.Text = retry.Interrupted
            ? $"{retry.Track.Name}  ·  {FormatClock(retry.Position)}" : retry.Track.Name;
        RaiseLiveRegion(NasRecoveryTitle);
    }

    private async void CheckNasRecovery()
    {
        if (nasCheckRunning || playbackRetry is not { } retry || Environment.TickCount64 < nextNasCheck) return;
        nasCheckRunning = true;
        nextNasCheck = Environment.TickCount64 + 3500;
        try
        {
            bool available = await Task.Run(() =>
            {
                try
                {
                    using var file = File.Open(retry.Track.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return file.Length > 0;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            });
            if (IsLoaded && ReferenceEquals(retry, playbackRetry) && available != lastNasAvailable)
                ShowNasRecovery(retry, available);
        }
        finally { nasCheckRunning = false; }
    }

    private void AnnouncePlayback(string text)
    {
        if (PlaybackAnnouncement == null || !IsLoaded || string.IsNullOrWhiteSpace(text)) return;
        PlaybackAnnouncement.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(PlaybackAnnouncement, text);
        RaiseLiveRegion(PlaybackAnnouncement);
    }

    private static void RaiseLiveRegion(UIElement element)
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
