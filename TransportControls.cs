using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PrismaMusic;

public partial class MainWindow
{
    private static readonly int[] SleepTimerPresets = { 15, 30, 60, 90 };
    private double volumeBeforeMute = .7;
    private bool stopAfterTrack;
    private bool continueAfterTrack = true;

    private void ToggleStopAfterTrack(object sender, RoutedEventArgs args) => SetStopAfterTrack(!stopAfterTrack);

    private void SetStopAfterTrack(bool enabled)
    {
        stopAfterTrack = enabled;
        InvalidateGapless();
        StopAfterTrackIndicator.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StopAfterTrackMenuItem.Header = LocalTextExtension.Translate(enabled
            ? "Cancelar pausa al final de la canción" : "Pausar al final de esta canción");
    }

    private void ToggleMute(object sender, RoutedEventArgs args)
    {
        if (Volume.Value > 0)
        {
            volumeBeforeMute = Volume.Value;
            Volume.Value = 0;
        }
        else Volume.Value = volumeBeforeMute;
    }

    private void RefreshMuteButton(double volume)
    {
        if (MuteButton == null) return;
        bool muted = volume == 0;
        MuteButton.Content = muted ? "\uE74F" : "\uE767";
        string label = LocalTextExtension.Translate(muted ? "Restaurar volumen" : "Silenciar");
        MuteButton.ToolTip = label;
        AutomationProperties.SetName(MuteButton, label);
    }

    private void ShowSleepTimerFromMenu(object sender, RoutedEventArgs args) =>
        Dispatcher.BeginInvoke(new Action(() => ShowSleepTimer(ListeningMenuButton, args)));

    private void ShowSleepTimer(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("PlayerMenu"), PlacementTarget = (UIElement)sender, Placement = PlacementMode.Top
        };
        foreach (int minutes in SleepTimerPresets)
        {
            var item = new MenuItem { Header = LocalTextExtension.Format("Pausar en {0} min", minutes),
                Style = (Style)FindResource("PlayerMenuItem") };
            item.Click += (_, _) => SetSleepTimer(minutes);
            menu.Items.Add(item);
        }
        if (sleepAt.HasValue)
        {
            menu.Items.Add(new Separator());
            var cancel = new MenuItem { Header = LocalTextExtension.Translate("Cancelar temporizador"),
                Style = (Style)FindResource("PlayerMenuItem") };
            cancel.Click += (_, _) => SetSleepTimer(null);
            menu.Items.Add(cancel);
        }
        menu.IsOpen = true;
    }

    private void SetSleepTimer(int? minutes)
    {
        sleepAt = minutes.HasValue ? DateTime.UtcNow.AddMinutes(minutes.Value) : null;
        UpdateSleepTimer();
    }

    private void UpdateSleepTimer()
    {
        if (sleepAt is not DateTime deadline)
        {
            SleepCountdown.Visibility = Visibility.Collapsed;
            return;
        }
        double seconds = Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
        if (seconds <= 0)
        {
            sleepAt = null;
            SleepCountdown.Visibility = Visibility.Collapsed;
            CancelPendingOpen();
            player.Pause();
            SetPlaying(false);
            return;
        }
        string countdown = FormatClock(seconds);
        if (!Equals(SleepCountdown.Content, countdown)) SleepCountdown.Content = countdown;
        SleepCountdown.Visibility = Visibility.Visible;
    }

    private static string FormatClock(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
