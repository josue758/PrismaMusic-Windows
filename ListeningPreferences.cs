using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private int albumSize = 1;
    private bool autoArtwork = true;
    private bool autoLyrics;
    private bool keepAwake;
    private bool awakeRequestActive;
    private double preferredAlbumSize = 150;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private void UpdateAwakeRequest(bool active)
    {
        if (active == awakeRequestActive) return;
        // Both acquisition and release run on the WPF dispatcher thread.
        const uint continuous = 0x80000000;
        const uint systemRequired = 0x00000001;
        if (SetThreadExecutionState(continuous | (active ? systemRequired : 0)) != 0)
            awakeRequestActive = active;
    }

    private void ApplyAlbumSize()
    {
        preferredAlbumSize = albumSize switch { 0 => 116, 2 => 188, _ => 150 };
        UpdateGallerySizing();
    }

    private void GalleryViewportChanged(object sender, SizeChangedEventArgs args) => UpdateGallerySizing();

    private void UpdateGallerySizing()
    {
        // Reserve the scrollbar and list item padding before distributing available space.
        double available = Math.Max(0, Rows.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4);
        double slot = preferredAlbumSize + 32;
        int columns = Math.Max(1, (int)Math.Round(available / slot, MidpointRounding.AwayFromZero));
        if (available >= 272) columns = Math.Max(2, columns);
        double size = available == 0 ? preferredAlbumSize : Math.Clamp(Math.Floor(available / columns) - 32, 64, preferredAlbumSize + 28);
        if (Equals(Application.Current.Resources["GalleryTileSize"], size)) return;
        Application.Current.Resources["GalleryTileSize"] = size;
    }

    private void UpdateEmptyLibrary()
    {
        bool empty = Rows.Items.Count == 0;
        EmptyLibraryView.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty) return;
        bool filtered = Search.Text.Length != 0 || ArtistFilter.SelectedIndex > 0 || Genre.SelectedIndex > 0 || Year.SelectedIndex > 0;
        bool vacant = library.Tracks.Count == 0 && Navigation.SelectedIndex != 4;
        string title = filtered ? "No se encontraron canciones" : Navigation.SelectedIndex == 4 ? "La cola está vacía"
            : vacant ? "Añade tu música" : "Esta vista está vacía";
        string detail = filtered ? "Prueba con otra búsqueda o limpia los filtros." : Navigation.SelectedIndex == 4
            ? "Usa Reproducir a continuación desde el menú de una canción." : vacant ? "Elige una carpeta para empezar tu biblioteca." : "";
        EmptyLibraryTitle.Text = T(title);
        EmptyLibraryDetail.Text = T(detail);
        EmptyLibraryAction.Content = T(filtered ? "Limpiar filtros" : "Añadir carpeta");
        EmptyLibraryAction.Tag = filtered;
        EmptyLibraryAction.Visibility = filtered || vacant ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RunEmptyLibraryAction(object sender, RoutedEventArgs args)
    {
        if (EmptyLibraryAction.Tag is true)
        {
            Search.Clear();
            ArtistFilter.SelectedIndex = Genre.SelectedIndex = Year.SelectedIndex = 0;
            return;
        }
        AddFolder(sender, args);
    }

    private bool IsDemo(Track track) => IsTrackInSource(track.Path, System.IO.Path.Combine(AppContext.BaseDirectory, "Demo"));

    private async Task CompleteTrackPresentation(Track track)
    {
        if (smokeTest || IsDemo(track)) return;
        var artwork = autoArtwork && Cover.Source == null
            ? LoadCollectionArtwork(new LibraryRow(track.Album, track.Artist, new[] { track }), false, 500)
            : Task.FromResult<System.Windows.Media.Imaging.BitmapImage>(null);
        if (autoLyrics && string.IsNullOrWhiteSpace(sourceLyrics))
        {
            try
            {
                string found = await LyricsLookup.Find(Http, track, () => ReferenceEquals(current, track) && autoLyrics);
                if (ReferenceEquals(current, track) && autoLyrics && string.IsNullOrWhiteSpace(sourceLyrics) &&
                    !string.IsNullOrWhiteSpace(found)) SetLyrics(found);
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
        }
        var cover = await artwork;
        if (cover != null && autoArtwork && ReferenceEquals(current, track) && Cover.Source == null)
        {
            Cover.Source = cover;
            RefreshArtworkTheme();
            UpdateMini();
        }
    }
}
