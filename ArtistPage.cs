using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool showAllArtistSongs;
    private LibraryRow renderedArtistPage;
    private Button artistPagePlayButton;
    private Button firstArtistSong;
    private Button firstArtistAlbum;
    private sealed record ArtistBiography(string Text, string Article);
    private readonly Dictionary<string, ArtistBiography> artistBiographies = new(StringComparer.OrdinalIgnoreCase);

    private void ArtistGalleryPointerUp(object sender, MouseButtonEventArgs args)
    {
        if (collection != null || LibrarySectionIndex != 2 || args.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(Rows, source) is not ListBoxItem { Content: LibraryRow artist }) return;
        Rows.SelectedItem = artist;
        OpenArtistCollection(artist.Name);
        args.Handled = true;
    }

    private void OpenArtistCollection(string name)
    {
        var tracks = library.Tracks.Where(track => string.Equals(track.Artist, name, StringComparison.OrdinalIgnoreCase) &&
            (selectedLibrarySource == null || IsTrackInSource(track.Path, selectedLibrarySource)))
            .OrderBy(track => track.Album).ThenBy(track => track.Disc).ThenBy(track => track.Number)
            .ThenBy(track => track.Name).ToArray();
        if (tracks.Length > 0) OpenCollection(new LibraryRow(name, "", tracks), album: false);
    }

    private void ShowArtistOverview(object sender, RoutedEventArgs args)
    {
        showAllArtistSongs = false;
        RefreshRows(refreshQueue: false);
        artistPagePlayButton?.Focus();
    }

    private void RenderArtistPage(LibraryRow page)
    {
        if (ReferenceEquals(renderedArtistPage, page)) return;
        renderedArtistPage = page;
        ArtistContent.Children.Clear();
        ArtistView.ScrollToTop();
        firstArtistSong = firstArtistAlbum = null;
        var albums = page.Tracks.GroupBy(track => AlbumKey(track.Album, AlbumOwner(track)))
            .Select(group =>
            {
                var first = group.First();
                var tracks = IndexedAlbum(first.Album, AlbumOwner(first))
                    .Where(track => selectedLibrarySource == null || IsTrackInSource(track.Path, selectedLibrarySource))
                    .OrderBy(track => track.Disc).ThenBy(track => track.Number).ThenBy(track => track.Name).ToArray();
                if (tracks.Length == 0) tracks = group.ToArray();
                return new LibraryRow(first.Album, AlbumOwner(first), tracks);
            })
            .OrderByDescending(row => row.Tracks.Max(track => track.Year))
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        ArtistContent.Children.Add(ActionButton("Volver", CloseCollection));
        var hero = new DockPanel { Margin = new Thickness(18) };
        var photo = new Image { Stretch = Stretch.UniformToFill };
        AutomationProperties.SetName(photo, LocalTextExtension.Format("Retrato de {0}", page.Name));
        var photoSurface = new Grid { ClipToBounds = true };
        photoSurface.Children.Add(new ArtworkTile { Caption = page.Name });
        photoSurface.Children.Add(photo);
        photoSurface.SizeChanged += (_, args) =>
            photoSurface.Clip = new RectangleGeometry(new Rect(args.NewSize), 9, 9);
        var frame = new Border { Width = 140, Height = 140, Child = photoSurface,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        DockPanel.SetDock(frame, Dock.Left);
        hero.Children.Add(frame);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = HomeText(page.Name, 38, translate: false, heading: true);
        name.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level1);
        identity.Children.Add(name);
        string songCount = page.Tracks.Length == 1 ? T("1 canción") : LocalTextExtension.Format("{0:N0} canciones", page.Tracks.Length);
        string albumCount = albums.Length == 1 ? T("1 álbum") : LocalTextExtension.Format("{0:N0} álbumes", albums.Length);
        var stats = HomeText($"{songCount} · {albumCount}",
            13, translate: false, muted: true);
        stats.TextWrapping = TextWrapping.Wrap;
        stats.Margin = new Thickness(0, 4, 0, 0);
        identity.Children.Add(stats);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        artistPagePlayButton = ActionButton("Reproducir", PlayCollection);
        artistPagePlayButton.SetResourceReference(BackgroundProperty, "AccentBrush");
        ((TextBlock)artistPagePlayButton.Content).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        actions.Children.Add(artistPagePlayButton);
        actions.Children.Add(ActionButton("Aleatorio", ShuffleCollection));
        if (!page.Tracks.All(IsDemo)) actions.Children.Add(ActionButton("Cambiar retrato", (_, _) => ShowArtistProfile(page.Name)));
        identity.Children.Add(actions);
        var photoCredit = ActionButton("Créditos del retrato", (_, _) => OpenArtistSource(photo.Tag as string));
        photoCredit.Visibility = Visibility.Collapsed;
        identity.Children.Add(photoCredit);
        hero.Children.Add(identity);
        var heroCard = HomeCard(hero);
        heroCard.Margin = new Thickness(0, 8, 0, 12);
        heroCard.Background = CollectionBackdrop(page);
        heroCard.SizeChanged += (_, _) =>
        {
            frame.Width = frame.Height = Math.Clamp(heroCard.ActualWidth * .22, 68, 178);
            name.FontSize = Math.Clamp(heroCard.ActualWidth / 17, 24, 46);
        };
        ArtistContent.Children.Add(heroCard);

        var songs = new StackPanel { Margin = new Thickness(12) };
        KeyboardNavigation.SetDirectionalNavigation(songs, KeyboardNavigationMode.Contained);
        var ranked = page.Tracks.Where(track => library.PlayCounts.GetValueOrDefault(track.Path) > 0)
            .OrderByDescending(track => library.PlayCounts.GetValueOrDefault(track.Path))
            .ThenBy(track => track.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var heading = ArtistPageHeading(ranked.Length > 0 ? "Más reproducidas en tu biblioteca" : "Canciones destacadas");
        heading.TextWrapping = TextWrapping.Wrap;
        songs.Children.Add(heading);
        var spotlight = ranked.Length > 0 ? ranked : page.Tracks.OrderByDescending(track => library.Favorites.Contains(track.Path))
            .ThenBy(track => track.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var track in spotlight.Take(6))
        {
            var row = new DockPanel();
            var cover = BuildHomeArtwork(new LibraryRow(track.Album, track.Artist, new[] { track }), 39);
            cover.Width = 39;
            cover.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var duration = HomeText(track.DurationLabel, 11, translate: false, muted: true);
            duration.Margin = new Thickness(6, 0, 2, 0);
            duration.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(duration, Dock.Right);
            row.Children.Add(duration);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(HomeText(track.Name, 13, translate: false, heading: true));
            detail.Children.Add(HomeText(track.Year > 0 ? $"{track.Album} · {track.Year}" : track.Album,
                11, translate: false, muted: true));
            row.Children.Add(detail);
            var play = new Button { Content = row, Style = (Style)FindResource("FluentNavigationButton"),
                Background = Brushes.Transparent, Padding = new Thickness(6, 4, 6, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(play, LocalTextExtension.Format("Reproducir {0}", track.Name));
            play.Click += (_, _) => PlayFromHome(track, page.Tracks);
            songs.Children.Add(play);
            firstArtistSong ??= play;
        }
        songs.Children.Add(ActionButton("Ver todas las canciones", (_, _) =>
        {
            showAllArtistSongs = true;
            RefreshRows(refreshQueue: false);
            if (Rows.Items.Count > 0) Rows.SelectedIndex = 0;
            Rows.Focus();
        }));

        var about = new StackPanel { Margin = new Thickness(14) };
        about.Children.Add(ArtistPageHeading("Biografía"));
        var bio = HomeText("Buscando información del artista…", 13, muted: true);
        bio.TextWrapping = TextWrapping.Wrap;
        bio.TextTrimming = TextTrimming.None;
        bio.LineHeight = 20;
        about.Children.Add(bio);
        var article = ActionButton("Artículo en Wikipedia", (_, _) => OpenArtistSource(bio.Tag as string));
        article.Visibility = Visibility.Collapsed;
        article.Margin = new Thickness(0, 9, 0, 0);
        about.Children.Add(article);
        var songCard = HomeCard(songs);
        var bioCard = HomeCard(about);
        var overview = new Grid();
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bioCard, 1);
        Grid.SetColumnSpan(songCard, 2);
        Grid.SetColumnSpan(bioCard, 2);
        overview.Children.Add(songCard);
        overview.Children.Add(bioCard);
        overview.SizeChanged += (_, args) =>
        {
            bool wide = args.NewSize.Width >= 660;
            Grid.SetColumnSpan(songCard, wide ? 1 : 2);
            Grid.SetColumnSpan(bioCard, wide ? 1 : 2);
            Grid.SetColumn(bioCard, wide ? 1 : 0);
            Grid.SetRow(bioCard, wide ? 0 : 1);
            bioCard.Margin = wide ? new Thickness(10, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        };
        ArtistContent.Children.Add(overview);

        if (albums.Length > 0) AddArtistAlbums(albums);
        _ = PopulateArtistMedia(page, photo, photoCredit, bio, article);

    }

    private void MoveBetweenArtistSections(object sender, KeyEventArgs args)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key is Key.Down or Key.Up) args.Handled = FocusArtistSection(args.Key == Key.Down);
    }

    private bool FocusArtistSection(bool albums)
    {
        var target = albums ? firstArtistAlbum : firstArtistSong;
        if (target == null) return false;
        target.BringIntoView();
        return target.Focus();
    }

    private TextBlock ArtistPageHeading(string title)
    {
        var heading = HomeText(title, 18, heading: true);
        heading.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private void AddArtistAlbums(LibraryRow[] albums)
    {
        var section = ArtistPageHeading("Álbumes en tu biblioteca");
        section.Margin = new Thickness(0, 22, 0, 8);
        ArtistContent.Children.Add(section);
        var cards = new WrapPanel();
        KeyboardNavigation.SetDirectionalNavigation(cards, KeyboardNavigationMode.Contained);
        cards.SizeChanged += (_, _) => FitAlbumCards();
        ArtistContent.Children.Add(cards);
        int next = 0;
        var more = new Button { Content = T("Mostrar más álbumes"), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0) };
        more.Click += (_, _) => AddBatch();
        ArtistContent.Children.Add(more);
        AddBatch();

        void AddBatch()
        {
            int limit = Math.Min(albums.Length, next + 12);
            for (; next < limit; next++)
            {
                var album = albums[next];
                var art = BuildHomeArtwork(album, 144);
                var contents = new StackPanel();
                contents.Children.Add(art);
                var title = HomeText(album.Name, 13, translate: false, heading: true);
                title.Margin = new Thickness(0, 6, 0, 1);
                contents.Children.Add(title);
                uint year = album.Tracks.Max(track => track.Year);
                contents.Children.Add(HomeText(year > 0 ? year.ToString() : album.Detail, 11, translate: false, muted: true));
                var button = new Button { Content = contents, Style = (Style)FindResource("FluentNavigationButton"),
                    Width = 160, Padding = new Thickness(7), Margin = new Thickness(0, 0, 8, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.SetResourceReference(BackgroundProperty, "SettingsCard");
                AutomationProperties.SetName(button, LocalTextExtension.Format("Abrir álbum {0}", album.Name));
                button.Click += (_, _) => OpenCollection(album, album: true);
                cards.Children.Add(button);
                firstArtistAlbum ??= button;
            }
            FitAlbumCards();
            more.Visibility = next < albums.Length ? Visibility.Visible : Visibility.Collapsed;
        }

        void FitAlbumCards()
        {
            double width = cards.ActualWidth;
            if (width <= 0) return;
            int columns = Math.Max(1, Math.Min(cards.Children.Count, (int)(width / 164)));
            double cardWidth = Math.Clamp(width / columns - 8, 132, 228);
            foreach (Button card in cards.Children)
            {
                card.Width = cardWidth;
                ((Border)((StackPanel)card.Content).Children[0]).Height = cardWidth - 16;
            }
        }
    }

    private async Task PopulateArtistMedia(LibraryRow page, Image photo, Button credit, TextBlock bio, Button article)
    {
        var portrait = UpdatePortrait();
        try
        {
            if (!artistBiographies.TryGetValue(page.Name, out var profile) && autoArtwork && !page.Tracks.All(IsDemo))
            {
                profile = await FindArtistBiography(page.Name);
                if (profile != null)
                {
                    if (artistBiographies.Count >= 96) artistBiographies.Clear();
                    artistBiographies[page.Name] = profile;
                }
            }
            if (!ReferenceEquals(renderedArtistPage, page)) return;
            bio.Text = profile?.Text ?? T("Biografía no disponible.");
            bio.Tag = profile?.Article;
            article.Visibility = profile == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
            if (ReferenceEquals(renderedArtistPage, page)) bio.Text = T("Biografía no disponible.");
        }
        finally { await portrait; }

        async Task UpdatePortrait()
        {
            try
            {
                var image = await Task.Run(() => ReadArtistPortrait(page.Name, 420));
                if (image == null && autoArtwork && !page.Tracks.All(IsDemo))
                {
                    if (!ReferenceEquals(renderedArtistPage, page) || !await EnsureArtistPortrait(page.Name)) return;
                    image = await Task.Run(() => ReadArtistPortrait(page.Name, 420));
                }
                if (!ReferenceEquals(renderedArtistPage, page) || image == null) return;
                photo.Source = image;
                string attribution = PortraitPath(page.Name) + ".json";
                if (!File.Exists(attribution)) return;
                var data = JsonSerializer.Deserialize<PortraitCredit>(await File.ReadAllTextAsync(attribution));
                if (data == null || !ReferenceEquals(renderedArtistPage, page)) return;
                photo.Tag = data.Page;
                credit.ToolTip = string.Join(" · ", new[] { data.Creator, data.License }.Where(value => !string.IsNullOrWhiteSpace(value)));
                credit.Visibility = Visibility.Visible;
            }
            catch (Exception error) { Trace.WriteLine(error); }
        }
    }

    private static async Task<ArtistBiography> FindArtistBiography(string artist)
    {
        foreach (string edition in new[] { LocalTextExtension.Language, "en" }.Distinct())
        {
            var entry = (await SearchArtist(artist, edition)).FirstOrDefault(page => MatchesPortraitArtist(page, artist));
            if (entry.ValueKind == JsonValueKind.Undefined) continue;
            string extract = entry.GetProperty("extract").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(extract)) continue;
            int end = extract.Length > 470 ? extract.LastIndexOf(' ', 470) : -1;
            if (end > 0) extract = extract[..end].TrimEnd() + "…";
            return new(extract, entry.TryGetProperty("fullurl", out var url) ? url.GetString() : null);
        }
        return null;
    }

    private static void OpenArtistSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("es.wikipedia.org" or "en.wikipedia.org" or "pt.wikipedia.org" or "commons.wikimedia.org")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { Trace.WriteLine(error); }
    }
}
