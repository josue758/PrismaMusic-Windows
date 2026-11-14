using System;
using System.Diagnostics;
using System.Windows;
using Windows.Media;
using Windows.Media.Playback;

namespace PrismaMusic;

public partial class MainWindow
{
    private MediaPlayer systemControlHost;
    private SystemMediaTransportControls systemControls;
    private Track publishedTrack;
    private long publishedSecond = -1;

    private void InitializeSystemPlaybackControls()
    {
        try
        {
            systemControlHost = new MediaPlayer();
            systemControlHost.CommandManager.IsEnabled = false;
            systemControls = systemControlHost.SystemMediaTransportControls;
            systemControls.IsEnabled = false;
            systemControls.IsPlayEnabled = true;
            systemControls.IsPauseEnabled = true;
            systemControls.IsPreviousEnabled = true;
            systemControls.IsNextEnabled = true;
            systemControls.ButtonPressed += SystemPlaybackButtonPressed;
            systemControls.PlaybackPositionChangeRequested += SystemPlaybackPositionRequested;
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
            DisposeSystemPlaybackControls();
        }
    }

    private void SystemPlaybackButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        Dispatcher.InvokeAsync(() => ApplySystemPlaybackButton(button));
    }

    private void ApplySystemPlaybackButton(SystemMediaTransportControlsButton button)
    {
        if (current == null || pendingTrack != null || castingConnection != null) return;
        switch (button)
        {
            case SystemMediaTransportControlsButton.Play when !playing:
            case SystemMediaTransportControlsButton.Pause when playing:
                Toggle(this, new RoutedEventArgs());
                break;
            case SystemMediaTransportControlsButton.Next:
                Next(this, new RoutedEventArgs());
                break;
            case SystemMediaTransportControlsButton.Previous:
                Previous(this, new RoutedEventArgs());
                break;
        }
    }

    private void SystemPlaybackPositionRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        var position = args.RequestedPlaybackPosition;
        Dispatcher.InvokeAsync(() =>
        {
            if (!opened || pendingTrack != null || castingConnection != null || !player.NaturalDuration.HasTimeSpan) return;
            player.Position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, player.NaturalDuration.TimeSpan.TotalSeconds));
            publishedSecond = -1;
            Tick();
        });
    }

    private void PublishSystemPlayback()
    {
        if (systemControls == null) return;
        try
        {
            bool isLocalTrack = current != null && castingConnection == null;
            if (systemControls.IsEnabled != isLocalTrack) systemControls.IsEnabled = isLocalTrack;
            if (!isLocalTrack) return;

            if (publishedTrack != current)
            {
                var display = systemControls.DisplayUpdater;
                display.Type = MediaPlaybackType.Music;
                display.MusicProperties.Title = current.Name;
                display.MusicProperties.Artist = current.Artist;
                display.MusicProperties.AlbumTitle = current.Album;
                display.Update();
                publishedTrack = current;
                publishedSecond = -1;
            }
            var status = opened
                ? playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused
                : MediaPlaybackStatus.Stopped;
            if (systemControls.PlaybackStatus != status)
            {
                systemControls.PlaybackStatus = status;
                publishedSecond = -1;
            }
            if (!opened || !player.NaturalDuration.HasTimeSpan) return;
            var duration = player.NaturalDuration.TimeSpan;
            var position = TimeSpan.FromSeconds(Math.Clamp(player.Position.TotalSeconds, 0, duration.TotalSeconds));
            if (publishedSecond == (long)position.TotalSeconds) return;
            systemControls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                EndTime = duration, MaxSeekTime = duration, Position = position
            });
            publishedSecond = (long)position.TotalSeconds;
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
            DisposeSystemPlaybackControls();
        }
    }

    private void DisposeSystemPlaybackControls()
    {
        if (systemControls != null)
        {
            systemControls.ButtonPressed -= SystemPlaybackButtonPressed;
            systemControls.PlaybackPositionChangeRequested -= SystemPlaybackPositionRequested;
            try { systemControls.IsEnabled = false; }
            catch (Exception error) { Trace.WriteLine(error); }
            systemControls = null;
        }
        systemControlHost?.Dispose();
        systemControlHost = null;
    }
}
