using System;
using System.Linq;

namespace PrismaMusic;

public partial class MainWindow
{
    private void RebindOpenCollections(Func<Track, Track> replace, Func<Track, bool> changed)
    {
        collection = Update(collection, albumPage);
        for (int index = 0; index < libraryHistory.Count; index++)
        {
            var visit = libraryHistory[index];
            libraryHistory[index] = visit with { Collection = Update(visit.Collection, visit.Album) };
        }

        LibraryRow Update(LibraryRow page, bool isAlbum)
        {
            if (page?.Tracks.Any(changed) != true) return page;
            var members = page.Tracks.Select(replace).Where(track => isAlbum
                ? string.Equals(track.Album, page.Name, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(AlbumOwner(track), AlbumOwner(page.Tracks[0]), StringComparison.OrdinalIgnoreCase)
                : string.Equals(track.Artist, page.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(track => track.Album).ThenBy(track => track.Disc)
                .ThenBy(track => track.Number).ThenBy(track => track.Name).ToArray();
            return members.Length == 0 ? null : page with { Tracks = members };
        }
    }

    private void RebindPlaybackTracks(Func<Track, Track> replace)
    {
        context = context.Select(replace).ToArray();
        originalContext = originalContext.Select(replace).ToArray();
        for (int index = 0; index < queue.Count; index++) queue[index] = replace(queue[index]);
        if (previous.Count > 0)
        {
            var earlier = previous.Reverse().Select(replace).ToArray();
            previous.Clear();
            foreach (var track in earlier) previous.Push(track);
        }
        if (removedQueueTrack != null) removedQueueTrack = replace(removedQueueTrack);
        if (playbackRetry is { } retry)
        {
            var retryTrack = replace(retry.Track);
            if (retryTrack != retry.Track)
            {
                playbackRetry = retry with { Track = retryTrack };
                RetryTrackButton.ToolTip = retryTrack.Name;
            }
        }

        if (current == null) return;
        var updated = replace(current);
        if (updated == current) return;
        var oldTrack = current;
        current = updated;
        CurrentTrackPath = current.Path;
        Now.Text = updated.Name;
        TransportArtist.Text = updated.Artist;
        Artist.Text = updated.Detail;
        if (oldTrack.FormatLabel != updated.FormatLabel) AudioFormat.Text = updated.FormatLabel;
        UpdateFavoriteToolbarButton();
        UpdateMini();
        PublishSystemPlayback();
    }
}
