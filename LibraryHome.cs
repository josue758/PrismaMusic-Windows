using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private void RenderLibraryHome()
    {
        var tracks = library.Tracks.Where(track => selectedLibrarySource == null ||
            IsTrackInSource(track.Path, selectedLibrarySource)).ToArray();
        var albums = tracks.GroupBy(track => (track.Album, Artist: AlbumOwner(track)))
            .Select(group => new LibraryRow(group.Key.Album, group.Key.Artist,
                group.OrderBy(track => track.Disc).ThenBy(track => track.Number).ThenBy(track => track.Name).ToArray()))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        HomeContent.Children.Clear();
        if (tracks.Length == 0)
        {
            HomeContent.Children.Add(new TextBlock { Text = T("Elige una carpeta para empezar tu biblioteca."),
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            HomeContent.Children.Add(ActionButton("Añadir carpeta", (_, _) => AddFolder(this, new RoutedEventArgs())));
            return;
        }

        var index = tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var recent = library.History.Where(index.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5).Select(path => index[path]).ToArray();
        var spotlight = recent.FirstOrDefault() ?? tracks[0];
        var hero = new StackPanel();
        hero.Children.Add(new TextBlock { Text = T(recent.Length > 0 ? "Volver a escuchar" : "En tu biblioteca"),
            Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
        hero.Children.Add(new TextBlock { Text = spotlight.Name, FontSize = 25, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
        hero.Children.Add(new TextBlock { Text = spotlight.Artist, Foreground = (Brush)FindResource("Muted"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 8) });
        var play = ActionButton("Reproducir ahora", (_, _) => PlayFromHome(spotlight, tracks));
        play.SetResourceReference(BackgroundProperty, "AccentBrush");
        ((TextBlock)play.Content).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        hero.Children.Add(play);
        var heroBody = new DockPanel();
        var heroArt = BuildHomeArtwork(new LibraryRow(spotlight.Album, spotlight.Artist, new[] { spotlight }), 105);
        heroArt.Width = 105;
        heroArt.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(heroArt, Dock.Left);
        heroBody.Children.Add(heroArt);
        heroBody.Children.Add(hero);
        HomeContent.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(15),
            Background = (Brush)FindResource("SettingsCard"), Child = heroBody });

        var facts = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        AddTotal("Canciones", tracks.Length);
        AddTotal("Álbumes", albums.Length);
        AddTotal("Artistas", tracks.Select(track => track.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        HomeContent.Children.Add(facts);
        AddHeading("Álbumes destacados", "Ver álbumes", 1);
        var covers = new UniformGrid { Columns = 3 };
        foreach (var album in albums.Take(3))
        {
            var tile = new StackPanel();
            tile.Children.Add(BuildHomeArtwork(album, 96));
            tile.Children.Add(new TextBlock { Text = album.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
            tile.Children.Add(new TextBlock { Text = album.Detail, Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = tile, Style = (Style)FindResource("FluentNavigationButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = (Brush)FindResource("SettingsCard"),
                Padding = new Thickness(7), Margin = new Thickness(0, 0, 5, 0) };
            AutomationProperties.SetName(button, album.Name);
            button.Click += (_, _) => OpenCollection(album, album: true);
            covers.Children.Add(button);
        }
        HomeContent.Children.Add(covers);
        AddHeading(recent.Length > 0 ? "Escuchadas recientemente" : "Canciones", "Ver canciones", 0);
        foreach (var track in recent.Length > 0 ? recent : tracks.Take(5).ToArray())
        {
            var button = ActionButton($"{track.Name} · {track.Artist}", (_, _) => PlayFromHome(track, tracks), translate: false);
            button.Style = (Style)FindResource("FluentNavigationButton");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Background = Brushes.Transparent;
            button.Margin = new Thickness(0, 0, 0, 3);
            HomeContent.Children.Add(button);
        }

        void AddTotal(string title, int count)
        {
            var cell = new StackPanel { Margin = new Thickness(9, 5, 0, 2) };
            cell.Children.Add(new TextBlock { Text = count.ToString("N0", CultureInfo.CurrentCulture), FontSize = 19, FontWeight = FontWeights.SemiBold });
            cell.Children.Add(new TextBlock { Text = T(title), Foreground = (Brush)FindResource("Muted"), FontSize = 11 });
            facts.Children.Add(cell);
        }

        void AddHeading(string title, string action, int destination)
        {
            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 5) };
            var button = ActionButton(action, (_, _) => Navigation.SelectedIndex = destination);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(new TextBlock { Text = T(title), FontWeight = FontWeights.SemiBold, FontSize = 15,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            HomeContent.Children.Add(row);
        }
    }

    private Border BuildHomeArtwork(LibraryRow album, double height)
    {
        var art = new Grid { Height = height, ClipToBounds = true, DataContext = album };
        art.Children.Add(new ArtworkTile { Caption = album.Name });
        var cover = new Image { Stretch = Stretch.UniformToFill };
        cover.Loaded += LoadGalleryCard;
        art.Children.Add(cover);
        return new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = art };
    }

    private Button ActionButton(string label, RoutedEventHandler action, bool translate = true)
    {
        var button = new Button { Content = new TextBlock { Text = translate ? T(label) : label, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6, 8, 6) };
        AutomationProperties.SetName(button, translate ? T(label) : label);
        button.Click += action;
        return button;
    }

    private void PlayFromHome(Track track, Track[] tracks)
    {
        originalContext = tracks;
        ReorderPlayback(track);
        _ = Play(track);
    }
}
