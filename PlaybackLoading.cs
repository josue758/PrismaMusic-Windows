using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private readonly SemaphoreSlim fileOpenGate = new(1);
    private CancellationTokenSource pendingOpen;
    private Track pendingTrack;
    private Func<string, bool, WaveStream> prepareAudioFile = AudioEngine.Prepare;
    private bool recoveringAudioOutput;

    private async Task HandleAudioFailure(Exception error)
    {
        bool resume = playing;
        double position = player.Position.TotalSeconds;
        opened = false;
        SetPlaying(false);
        Status.Text = LocalTextExtension.Translate("Windows no pudo reproducir el archivo. ") + error.Message;
        const int deviceInvalidated = unchecked((int)0x88890004);
        const int resourcesInvalidated = unchecked((int)0x88890026);
        if (error.HResult is not (deviceInvalidated or resourcesInvalidated) || player.StrictDirect ||
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

    private async Task OpenTrack(Track track, bool remember, double position, bool autoplay, bool recordHistory)
    {
        CancelPendingOpen();
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
            player.OpenPrepared(prepared, strict);
            prepared = null; // The engine owns the decoder, including on output initialization failure.
            if (!opened) return;

            track = ApplyLibraryTags(track);
            if (remember && current != null && current != track) previous.Push(current);
            bool trackChanged = !ReferenceEquals(current, track);
            current = track;
            UpdateFavoriteToolbarButton();
            player.Volume = Volume.Value;
            if (player.NaturalDuration.HasTimeSpan)
                player.Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, player.NaturalDuration.TimeSpan.TotalSeconds));
            if (autoplay) player.Play();
            SetPlaying(autoplay);
            Now.Text = track.Name;
            Artist.Text = track.Detail;
            UpdateMini();
            if (recordHistory)
            {
                library.History.Remove(track.Path);
                library.History.Insert(0, track.Path);
                if (library.History.Count > 500) library.History.RemoveAt(500);
                Save();
                if (Navigation.SelectedIndex == 5 || ReferenceEquals(Navigation.SelectedItem, HomeNavigation)) RefreshRows();
            }
            if (trackChanged) LoadPresentation(track);
            PublishSystemPlayback();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(pendingOpen, request))
                Status.Text = LocalTextExtension.Translate("Windows no pudo reproducir el archivo. ") + error.Message;
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
