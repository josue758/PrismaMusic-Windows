using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private void ShowGroupedSearch(Track[] matches)
    {
        if (matches.Length == 0) return;
        var groups = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        SearchGroups.Children.Add(HomeCard(groups));
        SearchGroups.Visibility = Visibility.Visible;
        var songs = Group("Canciones");
        foreach (var track in matches.Take(3))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var enqueue = ActionButton("+", (_, _) =>
            {
                if (!queue.Any(item => string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    queue.Add(track);
                    RefreshSideQueue();
                }
                Status.Text = LocalTextExtension.Format("Añadida a la cola: {0}", track.Name);
            });
            AutomationProperties.SetName(enqueue, T("Añadir a la cola") + " · " + track.Name);
            DockPanel.SetDock(enqueue, Dock.Right);
            row.Children.Add(enqueue);
            row.Children.Add(ActionButton(track.Name, (_, _) => PlayFromHome(track, matches)));
            songs.Children.Add(row);
        }

        var albums = Group("Álbumes");
        foreach (var album in matches.DistinctBy(track => AlbumKey(track.Album, AlbumOwner(track))).Take(3))
            albums.Children.Add(ActionButton(album.Album, (_, _) => OpenCollection(new LibraryRow(album.Album,
                AlbumOwner(album), IndexedAlbum(album.Album, AlbumOwner(album)).Where(track => !library.HiddenTracks.Contains(track.Path)).ToArray()), album: true)));

        var artists = Group("Artistas");
        foreach (string artist in matches.Select(track => track.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
            artists.Children.Add(ActionButton(artist, (_, _) => OpenArtistCollection(artist, allSources: true)));

        StackPanel Group(string title)
        {
            var group = new StackPanel { Margin = new Thickness(9, 7, 7, 8) };
            group.Children.Add(HomeText(title, 13, heading: true));
            groups.Children.Add(group);
            return group;
        }
    }

}
