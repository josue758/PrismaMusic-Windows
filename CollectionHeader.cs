using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private static readonly Brush CollectionHeaderBrush = new LinearGradientBrush(
        Color.FromRgb(54, 42, 73), Color.FromRgb(32, 27, 44), 105);

    private void UpdateCollectionHeader()
    {
        var page = collection;
        bool detail = page != null;
        CollectionArtTile.Visibility = CollectionKind.Visibility = CollectionActions.Visibility =
            detail ? Visibility.Visible : Visibility.Collapsed;
        Genre.Visibility = Year.Visibility = Sort.Visibility = detail ? Visibility.Collapsed : Visibility.Visible;
        OpenRelatedAlbum.Visibility = detail && !albumPage ? Visibility.Visible : Visibility.Collapsed;
        OpenRelatedArtist.Visibility = detail && albumPage ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeader.Background = detail ? CollectionHeaderBrush : Brushes.Transparent;
        LibraryHeader.Padding = detail ? new Thickness(15) : new Thickness(2, 15, 0, 0);
        if (!detail)
        {
            artRequest = null;
            CollectionArt.Source = null;
            CollectionCount.Text = string.Format(LocalTextExtension.Translate("{0:N0} canciones en tu biblioteca"), library.Tracks.Count);
            return;
        }
        CollectionKind.Text = LocalTextExtension.Translate(albumPage ? "Álbum" : "Artista").ToUpper();
        int year = albumPage ? (int)(page.Tracks.FirstOrDefault(track => track.Year > 0)?.Year ?? 0) : 0;
        var duration = TimeSpan.FromSeconds(page.Tracks.Sum(track => (long)track.Seconds));
        CollectionCount.Text = string.Join("  ·  ", new[]
        {
            albumPage ? AlbumOwner(page.Tracks[0]) : "",
            year > 0 ? year.ToString() : "",
            page.Tracks.Length == 1 ? LocalTextExtension.Translate("1 pista") : string.Format(LocalTextExtension.Translate("{0:N0} pistas"), page.Tracks.Length),
            duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (artRequest != page)
        {
            artRequest = page;
            CollectionArt.Source = null;
            LoadCollectionArtwork(page);
        }
    }

    private async void LoadCollectionArtwork(LibraryRow page)
    {
        var artwork = await Task.Run(() =>
        {
            foreach (var track in page.Tracks.Take(3))
            {
                try
                {
                    using var file = TagLib.File.Create(track.Path);
                    var cover = file.Tag.Pictures.FirstOrDefault();
                    if (cover == null) continue;
                    using var bytes = new MemoryStream(cover.Data.Data);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = bytes;
                    image.DecodePixelWidth = 300;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.Message); }
            }
            return null;
        });
        if (ReferenceEquals(artRequest, page)) CollectionArt.Source = artwork;
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



