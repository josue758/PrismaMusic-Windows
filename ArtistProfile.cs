using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private Window artistProfileWindow;
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> AutomaticPortraits =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> PortraitRetryAfter =
        new(StringComparer.OrdinalIgnoreCase);

    private static async Task<bool> EnsureArtistPortrait(string artist)
    {
        if (File.Exists(PortraitPath(artist))) return true;
        if (PortraitRetryAfter.TryGetValue(artist, out long retry) && retry > Environment.TickCount64) return false;
        Lazy<Task<bool>> request = AutomaticPortraits.GetOrAdd(artist,
            name => new Lazy<Task<bool>>(() => DownloadArtistPortrait(name)));
        try
        {
            if (await request.Value) return true;
            if (PortraitRetryAfter.Count >= 1024) PortraitRetryAfter.Clear();
            PortraitRetryAfter[artist] = Environment.TickCount64 + (long)TimeSpan.FromMinutes(30).TotalMilliseconds;
            return false;
        }
        finally { AutomaticPortraits.TryRemove(new(artist, request)); }
    }

    private static async Task<bool> DownloadArtistPortrait(string artist, string destination = null)
    {
        destination ??= PortraitPath(artist);
        if (File.Exists(destination)) return true;
        string language = LocalTextExtension.Language;
        foreach (string edition in new[] { language, "en" }.Distinct())
        {
            var pages = await SearchArtist(artist, edition, portraitOnly: true);
            foreach (JsonElement page in pages)
            {
                if (!MatchesPortraitArtist(page, artist) ||
                    !page.TryGetProperty("thumbnail", out JsonElement thumbnail) ||
                    !thumbnail.TryGetProperty("source", out JsonElement source) ||
                    !Uri.TryCreate(source.GetString(), UriKind.Absolute, out Uri image) ||
                    !IsWikimediaPortrait(image)) continue;
                try
                {
                    PortraitCredit attribution = await FindPortraitCredit(page);
                    if (string.IsNullOrWhiteSpace(attribution?.Page) || string.IsNullOrWhiteSpace(attribution.License))
                        continue;
                    if (File.Exists(destination)) return true;
                    await SaveArtistPortrait(artist, image, destination, overwrite: false);
                    await File.WriteAllTextAsync(destination + ".json", JsonSerializer.Serialize(attribution));
                    return true;
                }
                catch (IOException) when (File.Exists(destination)) { return true; }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
                { Trace.WriteLine(error.Message); }
            }
        }
        return false;
    }

    private static bool IsWikimediaPortrait(Uri image) =>
        image.Scheme == "https" && image.Host is "upload.wikimedia.org" or "thumb.wikimedia.org";

    private static bool MatchesPortraitArtist(JsonElement page, string artist)
    {
        string title = page.GetProperty("title").GetString().Split(" (", 2)[0];
        if (NormalizeArtistName(title) != NormalizeArtistName(artist)) return false;
        if (!page.TryGetProperty("extract", out var extract)) return false;
        string biography = extract.GetString() ?? "";
        return new[] { "músic", "music", "cantant", "cantor", "cantaut", "singer", "songwriter", "banda", "band",
            "raper", "rapper", "compositor", "composer", "guitarr", "guitarist", "vocalist", "orquest", "orquestr",
            "duo", "dúo", "disc jockey", "pianist", "discográf", "record producer" }
            .Any(term => biography.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeArtistName(string name) => string.Concat(name.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
            char.IsLetterOrDigit(character))).ToUpperInvariant();

    private void ArtistProfile(object sender, RoutedEventArgs args) => ShowArtistProfile(Selected?.Artist);

    private async void ShowArtistProfile(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return;
        if (artistProfileWindow is { IsVisible: true })
        {
            artistProfileWindow.Activate();
            return;
        }

        try
        {
            JsonElement[] entries = Array.Empty<JsonElement>();
            int selectionRevision = 0;
            Uri selectedPortrait = null;
            PortraitCredit credit = null;
            var localTracks = library.Tracks.Where(track =>
                string.Equals(track.Artist, artist, StringComparison.OrdinalIgnoreCase)).ToArray();
            var photo = new Image { Stretch = Stretch.UniformToFill };
            var photoFallback = new TextBlock
            {
                Text = "\uE8D6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 42,
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var photoFrame = new Border
            {
                Width = 172,
                Height = 172,
                CornerRadius = new CornerRadius(24),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new Grid { Children = { photoFallback, photo } }
            };
            var heading = new TextBlock
            {
                Text = artist,
                FontSize = 32,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            var statistics = new TextBlock
            {
                Text = LocalTextExtension.Format("{0:N0} pistas en la biblioteca · {1:N0} álbumes",
                    localTracks.Length, localTracks.Select(track => track.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            var picker = new ComboBox { Style = (Style)FindResource("FilterCombo"), MinWidth = 240 };
            var biography = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22 };
            var credits = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gainsboro,
                FontSize = 11,
                Margin = new Thickness(0, 9, 0, 0)
            };
            var imageSource = new Button
            {
                Content = LocalTextExtension.Translate("Fuente y licencia de la imagen"),
                Visibility = Visibility.Collapsed
            };
            var source = new Button
            {
                Content = LocalTextExtension.Translate("Artículo en Wikipedia"),
                IsEnabled = false
            };
            var savePortrait = new Button
            {
                Content = LocalTextExtension.Translate("Usar este retrato"),
                IsEnabled = false
            };
            var loading = new TextBlock
            {
                Text = LocalTextExtension.Translate("Buscando información del artista…"),
                Foreground = Brushes.Gainsboro,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            };
            void ShowCredit(PortraitCredit value)
            {
                credit = value;
                credits.Text = value == null ? "" : string.Join(" · ",
                    new[] { value.Creator, value.License }.Where(item => !string.IsNullOrWhiteSpace(item)));
                imageSource.Visibility = string.IsNullOrWhiteSpace(value?.Page) ? Visibility.Collapsed : Visibility.Visible;
            }

            void OpenArticle()
            {
                if (picker.SelectedIndex < 0 ||
                    !entries[picker.SelectedIndex].TryGetProperty("fullurl", out var url) ||
                    !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var article) ||
                    article.Scheme != "https" ||
                    article.Host is not ("en.wikipedia.org" or "es.wikipedia.org" or "pt.wikipedia.org"))
                    return;
                Process.Start(new ProcessStartInfo(article.AbsoluteUri) { UseShellExecute = true });
            }

            imageSource.Click += (_, _) =>
            {
                if (Uri.TryCreate(credit?.Page, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                    uri.Host is "commons.wikimedia.org" or "en.wikipedia.org" or "es.wikipedia.org" or "pt.wikipedia.org")
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            };
            source.Click += (_, _) => OpenArticle();
            picker.SelectionChanged += async (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                int index = picker.SelectedIndex;
                int revision = ++selectionRevision;
                var page = entries[index];
                biography.Text = page.TryGetProperty("extract", out var intro) ? intro.GetString() ?? "" : "";
                photo.Source = null;
                selectedPortrait = null;
                photoFallback.Visibility = Visibility.Visible;
                savePortrait.IsEnabled = false;
                source.IsEnabled = true;
                ShowCredit(null);
                loading.Text = LocalTextExtension.Translate("Cargando foto y créditos…");
                loading.Foreground = Brushes.Gainsboro;
                loading.Visibility = Visibility.Visible;
                try
                {
                    if (page.TryGetProperty("thumbnail", out var thumbnail) &&
                        thumbnail.TryGetProperty("source", out var imageSourceNode) &&
                        Uri.TryCreate(imageSourceNode.GetString(), UriKind.Absolute, out var image) &&
                        IsWikimediaPortrait(image))
                    {
                        BitmapImage bitmap = await LoadArtistImage(image);
                        if (revision != selectionRevision || picker.SelectedIndex != index) return;
                        selectedPortrait = image;
                        photo.Source = bitmap;
                        photoFallback.Visibility = Visibility.Collapsed;
                        savePortrait.IsEnabled = true;
                    }
                    var attribution = await FindPortraitCredit(page);
                    if (revision != selectionRevision || picker.SelectedIndex != index) return;
                    ShowCredit(attribution);
                    loading.Visibility = Visibility.Collapsed;
                }
                catch (Exception error)
                {
                    if (revision != selectionRevision) return;
                    loading.Text = LocalTextExtension.Translate("No se pudo cargar la foto o los créditos. ") + error.Message;
                    loading.Foreground = Brushes.Orange;
                }
            };
            savePortrait.Click += async (_, _) =>
            {
                if (selectedPortrait is not Uri image) return;
                savePortrait.IsEnabled = false;
                try
                {
                    int index = picker.SelectedIndex;
                    var attribution = credit ?? await FindPortraitCredit(entries[index]);
                    await SaveArtistPortrait(artist, image, frameOnFace: true);
                    await File.WriteAllTextAsync(PortraitPath(artist) + ".json", JsonSerializer.Serialize(attribution));
                    savePortrait.Content = LocalTextExtension.Translate("Retrato guardado");
                    artRequest = null;
                    renderedArtistPage = null;
                    RefreshRows();
                }
                catch (Exception error)
                {
                    loading.Text = error.Message;
                    loading.Foreground = Brushes.Orange;
                    loading.Visibility = Visibility.Visible;
                }
                finally { savePortrait.IsEnabled = selectedPortrait != null; }
            };
            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            heroText.Children.Add(new TextBlock
            {
                Text = LocalTextExtension.Translate("PERFIL DE ARTISTA"),
                Foreground = (Brush)FindResource("AccentBrush"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            heroText.Children.Add(heading);
            heroText.Children.Add(statistics);
            var hero = new Border
            {
                Padding = new Thickness(22),
                CornerRadius = new CornerRadius(24),
                Background = new LinearGradientBrush(Color.FromArgb(0x52, 70, 55, 100),
                    Color.FromArgb(0x20, 255, 255, 255), 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { photoFrame, heroText } }
            };
            var biographyCard = new Border
            {
                Padding = new Thickness(20),
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = biography
            };
            var content = new StackPanel { Margin = new Thickness(24, 12, 24, 20) };
            content.Children.Add(hero);
            content.Children.Add(new TextBlock
            {
                Text = LocalTextExtension.Translate("Información y biografía"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 22, 2, 8)
            });
            content.Children.Add(picker);
            content.Children.Add(biographyCard);
            content.Children.Add(credits);
            content.Children.Add(loading);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(source);
            actions.Children.Add(imageSource);
            actions.Children.Add(savePortrait);
            content.Children.Add(actions);
            var back = new Button
            {
                Content = LocalTextExtension.Translate("‹ Volver a artistas"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(20, 14, 0, 2)
            };
            var root = new DockPanel();
            DockPanel.SetDock(back, Dock.Top);
            root.Children.Add(back);
            root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var profile = new Window
            {
                Owner = this,
                Title = LocalTextExtension.Format("Perfil de {0}", artist),
                Width = 860,
                Height = 700,
                MinWidth = 680,
                MinHeight = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(24, 26, 35), 0),
                        new GradientStop(Color.FromRgb(32, 30, 43), 1)
                    },
                    new Point(0, 0),
                    new Point(1, 1)),
                Content = root
            };
            artistProfileWindow = profile;
            profile.Closed += (_, _) => artistProfileWindow = null;
            back.Click += (_, _) => profile.Close();
            profile.Show();

            try
            {
                string language = LocalTextExtension.Language;
                entries = await SearchArtist(artist, language);
                if (entries.Length == 0 && language != "en")
                    entries = await SearchArtist(artist, "en");
                if (!profile.IsVisible) return;
                if (entries.Length == 0)
                {
                    picker.Visibility = Visibility.Collapsed;
                    loading.Text = LocalTextExtension.Translate("Wikipedia no encontró perfiles.");
                    return;
                }
                picker.ItemsSource = entries.Select(page =>
                    page.TryGetProperty("title", out var titleNode) ? titleNode.GetString() : "").ToArray();
                picker.SelectedIndex = 0;
                loading.Text = LocalTextExtension.Translate("Información cargada desde Wikipedia.");
                loading.Foreground = (Brush)FindResource("Muted");
            }
            catch (Exception error)
            {
                if (!profile.IsVisible) return;
                loading.Text = LocalTextExtension.Translate("Error al consultar Wikipedia. ") + error.Message;
                loading.Foreground = Brushes.Orange;
            }
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("Error al consultar Wikipedia. ") + error.Message; }
    }

    private static async Task<BitmapImage> LoadArtistImage(Uri image)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, image);
        request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.34 (https://github.com/josue758/PrismaMusic)");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        const int maxImageSize = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxImageSize)
            throw new InvalidDataException(LocalTextExtension.Translate("La imagen es demasiado grande."));
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0 || bytes.Length > maxImageSize)
            throw new InvalidDataException(LocalTextExtension.Translate("No se pudo cargar la foto del artista."));
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task SaveArtistPortrait(string artist, Uri image, string path = null, bool frameOnFace = false,
        bool overwrite = true)
    {
        const int limit = 2 * 1024 * 1024;
        using var request = new HttpRequestMessage(HttpMethod.Get, image);
        request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.24 (https://github.com/josue758/PrismaMusic)");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException(LocalTextExtension.Translate("La imagen es demasiado grande."));
        using var body = await response.Content.ReadAsStreamAsync();
        using var stream = new MemoryStream();
        var buffer = new byte[16384];
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
        int count;
        while ((count = await body.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
        {
            if (stream.Length + count > limit) throw new InvalidDataException(LocalTextExtension.Translate("La imagen es demasiado grande."));
            stream.Write(buffer, 0, count);
        }
        stream.Position = 0;
        DecodeArtwork(stream, 640);
        byte[] imageBytes = stream.ToArray();
        if (frameOnFace) imageBytes = await PortraitFraming.CenterOnLargestFace(imageBytes);
        path ??= PortraitPath(artist);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(staging, imageBytes);
            File.Move(staging, path, overwrite);
        }
        finally { File.Delete(staging); }
    }

    private static async Task<JsonElement[]> SearchArtist(string artist, string language, bool portraitOnly = false)
    {
        string qualifier = language switch { "es" => "música", "pt" => "música", _ => "music" };
        string root = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" +
            "&prop=extracts%7Cpageimages%7Cinfo&exintro=1&explaintext=1&exlimit=5&exchars=1800" +
            "&inprop=url&piprop=thumbnail%7Cname&pithumbsize=360" + (portraitOnly ? "&pilicense=free" : "");
        var results = await ReadArtistPages(root + "&generator=search&gsrnamespace=0&gsrlimit=5&gsrsearch=" +
            Uri.EscapeDataString(artist + " " + qualifier), artist);
        if (portraitOnly && results.Any(page => MatchesPortraitArtist(page, artist) && page.TryGetProperty("thumbnail", out _)))
            return results;
        if (portraitOnly)
            return await ReadArtistPages(root + "&redirects=1&titles=" + Uri.EscapeDataString(artist), artist);
        return results.Length > 0 ? results : await ReadArtistPages(root + "&titles=" + Uri.EscapeDataString(artist), artist);
    }

    private static async Task<JsonElement[]> ReadArtistPages(string url, string artist)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.34 (https://github.com/josue758/PrismaMusic)");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages)) return Array.Empty<JsonElement>();
        string[] artistWords = artist.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 2).ToArray();
        return pages.EnumerateArray()
            .Where(page =>
            {
                if (page.TryGetProperty("missing", out _) ||
                    !page.TryGetProperty("title", out var titleNode) ||
                    titleNode.ValueKind != JsonValueKind.String) return false;
                string title = titleNode.GetString();
                return NormalizeArtistName(title.Split(" (", 2)[0]) == NormalizeArtistName(artist) ||
                    title.Contains(artist, StringComparison.OrdinalIgnoreCase) ||
                    artistWords.Length > 0 && artistWords.All(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(page => page.GetProperty("title").GetString().Equals(artist, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(page => page.TryGetProperty("index", out var index) ? index.GetInt32() : int.MaxValue)
            .Select(page => page.Clone()).ToArray();
    }
}
