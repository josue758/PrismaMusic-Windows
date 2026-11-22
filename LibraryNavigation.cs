using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record LibraryVisit(int Section, string Source, LibraryRow Collection, bool Album,
        int GalleryPage, LibraryRow Selection, bool GallerySelection, bool AllArtistSongs, LibraryFilters Filters, ArtistPosition Artist);

    private readonly List<LibraryVisit> libraryHistory = new();
    private bool restoringLibraryVisit;

    private void RememberLibraryView(int section)
    {
        if (section < 0 || restoringLibraryVisit) return;
        if (libraryHistory.Count == 48) libraryHistory.RemoveAt(0);
        libraryHistory.Add(new LibraryVisit(section, selectedLibrarySource, collection, albumPage, galleryPage,
            Rows.SelectedItem as LibraryRow, Equals(Rows.Tag, "Gallery"), showAllArtistSongs, new LibraryFilters(Search.Text, ArtistFilter.SelectedItem as string,
                Genre.SelectedItem as string, Year.SelectedItem as string, Sort.SelectedIndex), RememberArtistPosition()));
        LibraryBackButton.IsEnabled = true;
    }

    private void GoBackInLibrary(object sender, RoutedEventArgs args)
    {
        if (libraryHistory.Count == 0) return;
        var visit = libraryHistory[^1];
        libraryHistory.RemoveAt(libraryHistory.Count - 1);
        LibraryBackButton.IsEnabled = libraryHistory.Count != 0;
        restoringLibraryVisit = true;
        try
        {
            selectedLibrarySource = library.Folders.Contains(visit.Source, StringComparer.OrdinalIgnoreCase)
                ? visit.Source : null;
            LibrarySectionIndex = visit.Section;
            collection = visit.Collection;
            albumPage = visit.Album;
            showAllArtistSongs = visit.AllArtistSongs;
            galleryPage = visit.GalleryPage;
            RestoreLibraryFilters(visit.Filters);
            RefreshLibrarySources();
            UpdateCollectionHeading();
            if (visit.Selection is { } selection)
            {
                var restored = Rows.Items.OfType<LibraryRow>().FirstOrDefault(row => visit.GallerySelection
                    ? string.Equals(row.Name, selection.Name, StringComparison.OrdinalIgnoreCase) &&
                      (visit.Section != 1 || string.Equals(row.Detail, selection.Detail, StringComparison.OrdinalIgnoreCase))
                    : string.Equals(row.Tracks[0].Path, selection.Tracks[0].Path, StringComparison.OrdinalIgnoreCase));
                Rows.SelectedItem = restored;
                if (restored != null) Rows.ScrollIntoView(restored);
            }
            if (Rows.IsVisible) Rows.Focus();
            else if (ArtistView.IsVisible)
            {
                artistPagePlayButton?.Focus();
                RestoreArtistPosition(visit.Artist);
            }
            else Navigation.Focus();
        }
        finally { restoringLibraryVisit = false; }
    }
}
