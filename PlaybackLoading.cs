using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private readonly SemaphoreSlim fileOpenGate = new(1);
    private CancellationTokenSource pendingOpen;
    private Track pendingTrack;
    private Func<string, bool, WaveStream> prepareAudioFile = AudioEngine.Prepare;
    private bool recoveringAudioOutput;
    private Exception openingAudioError;
    private double lastPlaybackPosition;
    private sealed record PlaybackRetry(Track Track, bool Remember, double Position, bool Autoplay, bool RecordHistory, bool Interrupted = false);
    private PlaybackRetry playbackRetry;

    private void UpdatePlaybackRetry(Exception error, PlaybackRetry retry)
    {
        if (smokeTest && nasValidationStorage != null) Console.WriteLine($"NAS_ERROR: {error}");
        if (IsNetworkPlaybackFailure(retry.Track, error))
        {
            playbackRetry = retry;
            RetryTrackButton.Content = T(retry.Interrupted ? "Reanudar pista" : "Reintentar pista");
            RetryTrackButton.ToolTip = retry.Track.Name;
            RetryTrackButton.Visibility = Visibility.Visible;
            Status.Text = retry.Interrupted
                ? LocalTextExtension.Format("Se interrumpió «{0}» en el NAS. Al recuperar la conexión puedes reanudar desde {1}.",
                    retry.Track.Name, FormatClock(retry.Position))
                : LocalTextExtension.Format("No se pudo abrir «{0}» en el NAS. Comprueba la conexión y vuelve a intentarlo.", retry.Track.Name);
        }
        else Status.Text = LocalTextExtension.Translate("Windows no pudo reproducir el archivo. ") + error.Message;
    }

    private static bool IsNetworkPlaybackFailure(Track track, Exception error)
    {
        if (!NetworkMusicSource.IsSharePath(track.Path)) return false;
        for (Exception cause = error; cause != null; cause = cause.InnerException)
            if (cause is IOException or UnauthorizedAccessException || NetworkMusicSource.IsDisconnected(cause))
                return true;
        return false;
    }

    private async void RetryNetworkTrack(object sender, RoutedEventArgs args) => await ResumeNetworkTrack();

    private Task ResumeNetworkTrack() => playbackRetry is { } retry
        ? OpenTrack(retry.Track, retry.Remember, retry.Position, retry.Autoplay, retry.RecordHistory, retry.Interrupted)
        : Task.CompletedTask;

    private async Task HandleAudioFailure(Exception error)
    {
        if (pendingOpen != null && !opened)
        {
            openingAudioError = error;
            return;
        }
        bool resume = playing;
        double position = lastPlaybackPosition;
        try { if (opened) position = player.Position.TotalSeconds; }
        catch (Exception failure) when (current != null && IsNetworkPlaybackFailure(current, failure)) { }
        opened = false;
        SetPlaying(false);
        if (current != null && pendingOpen == null && IsNetworkPlaybackFailure(current, error))
        {
            player.Close();
            UpdatePlaybackRetry(error, new(current, false, position, resume, false, Interrupted: true));
            return;
        }
        else Status.Text = LocalTextExtension.Translate("Windows no pudo reproducir el archivo. ") + error.Message;
        const int deviceInvalidated = unchecked((int)0x88890004);
        const int resourcesInvalidated = unchecked((int)0x88890026);
        const int deviceNotReady = unchecked((int)0x80070015);
        bool lostOutput = error.HResult is deviceInvalidated or resourcesInvalidated ||
            (error.HResult == deviceNotReady && player.ActiveOutputUnavailable);
        if (!lostOutput || player.StrictDirect ||
            !resume || current == null || pendingOpen != null || recoveringAudioOutput) return;

        recoveringAudioOutput = true;
        var track = current;
        try
        {
            await OpenTrack(track, remember: false, position, autoplay: true, recordHistory: false);
        }
        finally { recoveringAudioOutput = false; }
    }

    private Task Play(Track track, bool remember = true, double? restorePosition = null) =>
        OpenTrack(track, remember, restorePosition ?? 0, autoplay: !restorePosition.HasValue,
            recordHistory: !restorePosition.HasValue);

    private Task ReopenOutput()
    {
        if (current == null && pendingTrack == null) return Task.CompletedTask;
        if (pendingTrack != null && pendingTrack != current) return Play(pendingTrack);
        return OpenTrack(current ?? pendingTrack, false, player.Position.TotalSeconds, playing, false);
    }

    private async Task OpenTrack(Track track, bool remember, double position, bool autoplay, bool recordHistory, bool interrupted = false)
    {
        CancelPendingOpen();
        playbackRetry = null;
        RetryTrackButton.Visibility = Visibility.Collapsed;
        using var request = new CancellationTokenSource();
        pendingOpen = request;
        pendingTrack = track;
        player.Pause();
        SetPlaying(false);
        PlayButton.Content = "\uE711";
        var cancelLabel = LocalTextExtension.Translate("Cancelar apertura");
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, cancelLabel);
        if (miniToggle != null) System.Windows.Automation.AutomationProperties.SetName(miniToggle, cancelLabel);
        if (miniToggle != null) miniToggle.Content = PlayButton.Content;
        string loading = LocalTextExtension.Translate("Abriendo archivo de audio…");
        Status.Text = loading;
        bool strict = player.StrictDirect;
        WaveStream prepared = null;

        try
        {
            await fileOpenGate.WaitAsync(request.Token);
            try
            {
                request.Token.ThrowIfCancellationRequested();
                prepared = await Task.Run(() => prepareAudioFile(track.Path, strict), request.Token);
            }
            finally { fileOpenGate.Release(); }

            if (!ReferenceEquals(pendingOpen, request)) return;
            if (Status.Text == loading) Status.Text = "";
            opened = false;
            openingAudioError = null;
            player.OpenPrepared(prepared, strict);
            prepared = null; // The engine owns the decoder, including on output initialization failure.
            if (!opened && openingAudioError is { } failure)
            {
                openingAudioError = null;
                UpdatePlaybackRetry(failure, new(track, remember, position, autoplay, recordHistory, interrupted));
            }
            if (!opened) return;

            track = TrackIndex().GetValueOrDefault(track.Path) ?? ApplyLibraryTags(track);
            if (remember && current != null && current != track) previous.Push(current);
            bool trackChanged = !ReferenceEquals(current, track);
            current = track;
            CurrentTrackPath = track.Path;
            UpdateFavoriteToolbarButton();
            player.Volume = Volume.Value;
            if (player.NaturalDuration.HasTimeSpan)
                player.Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, player.NaturalDuration.TimeSpan.TotalSeconds));
            lastPlaybackPosition = player.Position.TotalSeconds;
            if (autoplay) player.Play();
            SetPlaying(autoplay);
            Now.Text = track.Name;
            TransportArtist.Text = track.Artist;
            Artist.Text = track.Detail;
            UpdateMini();
            if (recordHistory)
            {
                int plays = library.PlayCounts.GetValueOrDefault(track.Path);
                if (opened && autoplay && plays < int.MaxValue) library.PlayCounts[track.Path] = plays + 1;
                library.History.Remove(track.Path);
                library.History.Insert(0, track.Path);
                if (library.History.Count > 500) library.History.RemoveAt(500);
                Save();
                if (LibrarySectionIndex == 5 || ReferenceEquals(Navigation.SelectedItem, HomeNavigation)) RefreshRows();
            }
            if (trackChanged) LoadPresentation(track);
            PublishSystemPlayback();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(pendingOpen, request))
                UpdatePlaybackRetry(error, new(track, remember, position, autoplay, recordHistory, interrupted));
        }
        finally
        {
            prepared?.Dispose();
            if (ReferenceEquals(pendingOpen, request))
            {
                pendingOpen = null;
                pendingTrack = null;
                SetPlaying(playing);
            }
        }
    }

    private void CancelPendingOpen()
    {
        if (pendingOpen == null) return;
        var request = pendingOpen;
        pendingOpen = null;
        pendingTrack = null;
        request.Cancel();
        if (Status.Text == LocalTextExtension.Translate("Abriendo archivo de audio…")) Status.Text = "";
        SetPlaying(playing);
    }
}
