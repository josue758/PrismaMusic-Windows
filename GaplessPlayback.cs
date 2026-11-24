using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool gaplessEnabled = true;
    private CancellationTokenSource gaplessPreparation;
    private Track gaplessCandidate;
    private bool gaplessWorkerRunning;
    private bool gaplessProbeRunning;

    private Track CandidateForContinuousPlayback()
    {
        if (!gaplessEnabled || stopAfterTrack || !continueAfterTrack || repeat == RepeatMode.Track ||
            current == null || castingConnection != null) return null;
        if (queue.Count > 0) return queue[0];
        int index = Array.IndexOf(context, current);
        if (index + 1 < context.Length) return context[index + 1];
        return repeat == RepeatMode.Collection && context.Length > 0 ? context[0] : null;
    }

    private void InvalidateGaplessIfCandidateChanged()
    {
        if (gaplessCandidate != null && !ReferenceEquals(gaplessCandidate, CandidateForContinuousPlayback()))
            InvalidateGapless();
    }

    private void InvalidateGapless()
    {
        var request = gaplessPreparation;
        gaplessPreparation = null;
        gaplessCandidate = null;
        request?.Cancel();
        player.ClearStagedNext();
    }

    private async void UpdateGaplessPreparation()
    {
        if (smokeTest && !gaplessProbeRunning || !opened || pendingOpen != null || !player.CanStageNext)
        {
            if (gaplessCandidate != null) InvalidateGapless();
            return;
        }
        InvalidateGaplessIfCandidateChanged();
        if (!playing || gaplessWorkerRunning || gaplessCandidate != null || !player.NaturalDuration.HasTimeSpan || current == null) return;

        var next = CandidateForContinuousPlayback();
        if (next == null || !current.IsLossless || !next.IsLossless || current.SampleRate != next.SampleRate ||
            current.Channels != next.Channels || current.BitDepth != next.BitDepth) return;

        double remaining = (player.NaturalDuration.TimeSpan - player.Position).TotalSeconds;
        double window = NetworkMusicSource.IsSharePath(next.Path) ? 12 : 7;
        if (remaining > window || remaining <= .15) return;
        gaplessCandidate = next;
        using var request = new CancellationTokenSource();
        gaplessPreparation = request;
        gaplessWorkerRunning = true;
        var mode = player.ReplayMode;
        WaveStream decoder = null;
        try
        {
            ReplayGainTags tags;
            ISampleProvider samples;
            (decoder, samples, tags) = await Task.Run(() =>
            {
                var source = prepareAudioFile(next.Path, false);
                try
                {
                    request.Token.ThrowIfCancellationRequested();
                    var firstSamples = new BufferedPlaybackSamples(AudioEngine.ToPlaybackSamples(source));
                    var gain = mode == ReplayGainMode.Off ? null : ReplayGainTags.Read(next.Path);
                    return (source, firstSamples, gain);
                }
                catch { source.Dispose(); throw; }
            }, request.Token);

            if (ReferenceEquals(gaplessPreparation, request) && player.ReplayMode == mode &&
                ReferenceEquals(next, CandidateForContinuousPlayback()) && player.StageNext(decoder, samples, next, tags))
                decoder = null;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Continuous playback preload: {error.Message}"); }
        finally
        {
            decoder?.Dispose();
            gaplessWorkerRunning = false;
            if (ReferenceEquals(gaplessPreparation, request)) gaplessPreparation = null;
        }
    }

    private void AcceptContinuousTrack(Track track)
    {
        InvalidateGapless();
        if (queue.Count > 0 && string.Equals(queue[0].Path, track.Path, StringComparison.OrdinalIgnoreCase))
            queue.RemoveAt(0);
        track = TrackIndex().GetValueOrDefault(track.Path) ?? ApplyLibraryTags(track);
        if (current != null && current != track) previous.Push(current);
        bool changed = current != track;
        current = track;
        CurrentTrackPath = track.Path;
        lastPlaybackPosition = player.Position.TotalSeconds;
        UpdateFavoriteToolbarButton();
        ShowOpenedTrack(track, changed, recordHistory: true, autoplay: playing);
    }
}
