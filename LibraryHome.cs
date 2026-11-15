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
        HomeContent.Children.Clear();
        if (tracks.Length == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(18, 28, 18, 24) };
            empty.Children.Add(HomeText("Añade tu música", 24, heading: true));
            empty.Children.Add(SettingsHint("Elige una carpeta para empezar tu biblioteca."));
            empty.Children.Add(ActionButton("Añadir carpeta", AddFolder));
            HomeContent.Children.Add(HomeCard(empty));
            return;
        }

        var albums = tracks.GroupBy(track => (track.Album, Artist: AlbumOwner(track)))
            .Select(group => new LibraryRow(group.Key.Album, group.Key.Artist,
                group.OrderBy(track => track.Disc).ThenBy(track => track.Number).ThenBy(track => track.Name).ToArray()))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var index = tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var recent = library.History.Where(index.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5).Select(path => index[path]).ToArray();
        var spotlight = recent.FirstOrDefault() ?? tracks[0];
        var artwork = new LibraryRow(spotlight.Album, spotlight.Artist, new[] { spotlight });
        var hero = new DockPanel { Margin = new Thickness(15) };
        var heroArt = BuildHomeArtwork(artwork, 126);
        heroArt.Width = 126;
        heroArt.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(heroArt, Dock.Left);
        hero.Children.Add(heroArt);
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(HomeText(recent.Length > 0 ? "Volver a escuchar" : "En tu biblioteca", 11, muted: true));
        var featuredTitle = HomeText(spotlight.Name, 26, translate: false, heading: true);
        featuredTitle.Margin = new Thickness(0, 3, 0, 0);
        heroText.Children.Add(featuredTitle);
        heroText.Children.Add(HomeText(spotlight.Detail, 12, translate: false, muted: true));
        var play = ActionButton("Reproducir ahora", (_, _) => PlayFromHome(spotlight, tracks));
        play.SetResourceReference(BackgroundProperty, "AccentBrush");
        ((TextBlock)play.Content).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        play.Margin = new Thickness(0, 10, 0, 0);
        heroText.Children.Add(play);
        hero.Children.Add(heroText);
        var heroCard = HomeCard(hero);
        var accent = ArtworkTile.AccentForCaption(spotlight.Album, windowsAccent);
        WatchSettingsCard(heroCard, 350, () =>
        {
            Color tint = lightTheme ? Mix(Color.FromRgb(251, 251, 252), accent, .12)
                : Mix(Color.FromRgb(32, 33, 38), accent, .22);
            Color neutral = lightTheme ? Color.FromRgb(251, 251, 252) : Color.FromRgb(37, 37, 41);
            if (heroCard.Background is LinearGradientBrush brush && brush.GradientStops[0].Color == tint) return;
            heroCard.Background = new LinearGradientBrush(tint, neutral, 18);
        });
        HomeContent.Children.Add(heroCard);

        var totals = new TextBlock
        {
            Text = LocalTextExtension.Format("{0:N0} canciones · {1:N0} álbumes · {2:N0} artistas",
                tracks.Length, albums.Length, tracks.Select(track => track.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            Margin = new Thickness(2, 10, 0, 1), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis
        };
        totals.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        HomeContent.Children.Add(totals);

        AddHeading("Álbumes destacados", "Ver álbumes", 1);
        var covers = new UniformGrid { Columns = Math.Min(3, albums.Length) };
        foreach (var album in albums.Take(3))
        {
            var image = BuildHomeArtwork(album, 96);
            var details = new StackPanel();
            details.Children.Add(image);
            var albumTitle = HomeText(album.Name, 13, translate: false, heading: true);
            albumTitle.Margin = new Thickness(0, 7, 0, 0);
            details.Children.Add(albumTitle);
            details.Children.Add(HomeText(album.Detail, 11, translate: false, muted: true));
            var button = new Button
            {
                Content = details, Style = (Style)FindResource("FluentNavigationButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7),
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.SetResourceReference(BackgroundProperty, "SettingsCard");
            AutomationProperties.SetName(button, album.Name);
            button.Click += (_, _) => OpenCollection(album, album: true);
            button.SizeChanged += (_, _) => image.Height = Math.Clamp(button.ActualWidth - 16, 84, 186);
            covers.Children.Add(button);
        }
        HomeContent.Children.Add(covers);

        AddHeading(recent.Length > 0 ? "Escuchadas recientemente" : "Canciones", "Ver canciones", 0);
        var list = new StackPanel { Margin = new Thickness(3) };
        foreach (var track in recent.Length > 0 ? recent : tracks.Take(5).ToArray())
        {
            var row = new DockPanel();
            var cover = BuildHomeArtwork(new LibraryRow(track.Album, track.Artist, new[] { track }), 34);
            cover.Width = 34;
            cover.Margin = new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var duration = HomeText(track.DurationLabel, 11, translate: false, muted: true);
            duration.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(duration, Dock.Right);
            row.Children.Add(duration);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(HomeText(track.Name, 13, translate: false, heading: true));
            detail.Children.Add(HomeText(track.Artist, 11, translate: false, muted: true));
            row.Children.Add(detail);
            var button = new Button
            {
                Content = row, Style = (Style)FindResource("FluentNavigationButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 4, 7, 4)
            };
            AutomationProperties.SetName(button, LocalTextExtension.Format("Reproducir {0}", track.Name));
            button.Click += (_, _) => PlayFromHome(track, tracks);
            list.Children.Add(button);
        }
        HomeContent.Children.Add(HomeCard(list));

        void AddHeading(string title, string action, int destination)
        {
            var row = new DockPanel { Margin = new Thickness(0, 15, 5, 5) };
            var button = ActionButton(action, (_, _) => LibrarySectionIndex = destination);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            var label = HomeText(title, 16, heading: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            HomeContent.Children.Add(row);
        }
    }

    private Border HomeCard(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), Child = child, BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SettingsCard");
        card.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        return card;
    }

    private TextBlock HomeText(string text, double size, bool translate = true, bool muted = false, bool heading = false)
    {
        var label = new TextBlock { Text = translate ? T(text) : text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
        return label;
    }

    private Border BuildHomeArtwork(LibraryRow album, double height)
    {
        var art = new Grid { ClipToBounds = true, DataContext = album };
        art.Children.Add(new ArtworkTile { Caption = album.Name });
        var cover = new Image { Stretch = Stretch.UniformToFill };
        cover.Loaded += LoadGalleryCard;
        art.Children.Add(cover);
        art.SizeChanged += (_, _) =>
            art.Clip = new RectangleGeometry(new Rect(0, 0, art.ActualWidth, art.ActualHeight), 7, 7);
        return new Border { Height = height, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = art };
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
