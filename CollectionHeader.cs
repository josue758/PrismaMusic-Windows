using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private void UpdateCollectionHeader()
    {
        var page = collection;
        bool detail = page != null;
        ArtistOverviewButton.Visibility = detail && !albumPage && showAllArtistSongs ? Visibility.Visible : Visibility.Collapsed;
        CollectionArtTile.Visibility = CollectionKind.Visibility = CollectionActions.Visibility =
            detail ? Visibility.Visible : Visibility.Collapsed;
        ArtistFilter.Visibility = Genre.Visibility = Year.Visibility = Sort.Visibility = detail ? Visibility.Collapsed : Visibility.Visible;
        OpenRelatedAlbum.Visibility = detail && !albumPage ? Visibility.Visible : Visibility.Collapsed;
        OpenRelatedArtist.Visibility = detail && albumPage ? Visibility.Visible : Visibility.Collapsed;
        EditAlbumButton.Visibility = detail && albumPage ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeader.Background = detail ? CollectionBackdrop(page) : Brushes.Transparent;
        LibraryHeader.Padding = detail ? new Thickness(15) : new Thickness(2, 15, 0, 0);
        if (!detail)
        {
            artRequest = null;
            CollectionArt.Source = null;
            int count = selectedLibrarySource == null
                ? library.Tracks.Count
                : library.Tracks.Count(track => IsTrackInSource(track.Path, selectedLibrarySource));
            if (LibrarySectionIndex == 4)
            {
                CollectionCount.Text = queue.Count == 1 ? LocalTextExtension.Translate("1 canción en cola")
                    : LocalTextExtension.Format("{0:N0} canciones en cola", queue.Count);
                return;
            }
            CollectionCount.Text = selectedLibrarySource == null
                ? string.Format(LocalTextExtension.Translate("{0:N0} canciones en tu biblioteca"), count)
                : LocalTextExtension.Format("{0:N0} pistas en esta fuente", count);
            return;
        }
        CollectionKind.Text = LocalTextExtension.Translate(albumPage ? "Álbum" : "Artista").ToUpper();
        int year = albumPage ? (int)(page.Tracks.FirstOrDefault(track => track.Year > 0)?.Year ?? 0) : 0;
        var duration = TimeSpan.FromSeconds(page.Tracks.Sum(track => (long)track.Seconds));
        var genres = albumPage
            ? string.Join(", ", page.Tracks.Select(track => track.Genre).Where(genre => !string.IsNullOrWhiteSpace(genre))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
            : "";
        CollectionCount.Text = string.Join("  ·  ", new[]
        {
            albumPage ? AlbumOwner(page.Tracks[0]) : "",
            year > 0 ? year.ToString() : "",
            genres,
            page.Tracks.Length == 1 ? LocalTextExtension.Translate("1 pista") : string.Format(LocalTextExtension.Translate("{0:N0} pistas"), page.Tracks.Length),
            duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (detail && !albumPage && !showAllArtistSongs)
        {
            artRequest = null;
            CollectionArt.Source = null;
        }
        else if (artRequest != page)
        {
            artRequest = page;
            CollectionArt.Source = null;
            LoadCollectionArtwork(page);
        }
    }

    private Brush CollectionBackdrop(LibraryRow page)
    {
        Color tone = ArtworkTile.AccentForCaption(page.Name, windowsAccent);
        var baseColor = lightTheme ? Color.FromRgb(249, 249, 250) : Color.FromRgb(38, 39, 43);
        return new LinearGradientBrush(Mix(baseColor, tone, lightTheme ? .12 : .28), baseColor, 28);
    }

    private async void LoadCollectionArtwork(LibraryRow page)
    {
        bool artist = !albumPage;
        var workers = artist ? artistArtworkWorkers : galleryArtworkWorkers;
        await workers.WaitAsync();
        try
        {
            if (!ReferenceEquals(artRequest, page)) return;
            var artwork = await LoadCollectionArtwork(page, artist, 300);
            if (ReferenceEquals(artRequest, page)) CollectionArt.Source = artwork;
        }
        finally { workers.Release(); }
    }

    private void OpenPlayingAlbum(object sender, RoutedEventArgs args)
    {
        if (current is not { } track) return;
        var tracks = IndexedAlbum(track.Album, AlbumOwner(track));
        if (tracks.Length == 0) return;
        OpenCollection(new LibraryRow(track.Album, AlbumOwner(track), tracks), album: true);
        var row = Rows.Items.OfType<LibraryRow>().FirstOrDefault(item => item.Tracks.Length == 1 &&
            string.Equals(item.Tracks[0].Path, track.Path, StringComparison.OrdinalIgnoreCase));
        if (row == null) return;
        Rows.SelectedItem = row;
        Rows.ScrollIntoView(row);
    }

    private void OpenPlayingArtist(object sender, RoutedEventArgs args)
    {
        if (current is { } track) OpenArtistCollection(track.Artist, allSources: true);
    }

    private void PlayCollection(object sender, RoutedEventArgs args) => StartCollection(false);
    private void ShuffleCollection(object sender, RoutedEventArgs args) => StartCollection(true);

    private void StartCollection(bool shuffled)
    {
        if (collection == null || collection.Tracks.Length == 0) return;
        originalContext = collection.Tracks;
        shuffle = shuffled;
        MarkPlaybackToggle(ShuffleButton, shuffle);
        var first = shuffled ? originalContext[Random.Shared.Next(originalContext.Length)] : originalContext[0];
        ReorderPlayback(first);
        Play(first);
    }
}




