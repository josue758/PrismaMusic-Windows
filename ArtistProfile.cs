using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private async void ArtistProfile(object sender, RoutedEventArgs args)
    {
        var artist = Selected?.Artist;
        if (string.IsNullOrWhiteSpace(artist)) return;
        if (MessageBox.Show(this, LocalTextExtension.Format("Buscar «{0}» en Wikipedia", artist), LocalTextExtension.Translate("Perfil de artista"),
            MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;

        try
        {
            string language = LocalTextExtension.Language;
            JsonElement[] entries;
            try
            {
                entries = await SearchArtist(artist, language);
                if (entries.Length == 0 && language != "en") entries = await SearchArtist(artist, "en");
            }
            catch (Exception error)
            {
                Status.Text = LocalTextExtension.Translate("Error al consultar Wikipedia. ") + error.Message;
                entries = Array.Empty<JsonElement>();
            }
            var picker = new ComboBox { Style = (Style)FindResource("FilterCombo"),
                ItemsSource = entries.Select(page => page.GetProperty("title").GetString()).ToArray() };
            var photo = new Image { Height = 165, Margin = new Thickness(0, 10, 0, 8) };
            var biography = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var credits = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Lavender,
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            var imageSource = new Button { Content = LocalTextExtension.Translate("Fuente y licencia de la imagen"),
                Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            var source = new Button { Content = LocalTextExtension.Translate("Artículo en Wikipedia"), Margin = new Thickness(0, 12, 0, 0) };
            var savePortrait = new Button { Content = LocalTextExtension.Translate("Usar este retrato"),
                IsEnabled = false, Margin = new Thickness(0, 10, 0, 0) };
            Uri selectedPortrait = null;
            PortraitCredit credit = null;
            void ShowCredit(PortraitCredit value)
            {
                credit = value;
                credits.Text = value == null ? "" : string.Join(" · ",
                    new[] { value.Creator, value.License }.Where(item => !string.IsNullOrWhiteSpace(item)));
                imageSource.Visibility = string.IsNullOrWhiteSpace(value?.Page) ? Visibility.Collapsed : Visibility.Visible;
            }
            imageSource.Click += (_, _) =>
            {
                if (Uri.TryCreate(credit?.Page, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                    uri.Host is "commons.wikimedia.org" or "en.wikipedia.org" or "es.wikipedia.org" or "pt.wikipedia.org")
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            };
            picker.SelectionChanged += async (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                int index = picker.SelectedIndex;
                var page = entries[index];
                biography.Text = page.TryGetProperty("extract", out var intro) ? intro.GetString() : "";
                photo.Source = null;
                selectedPortrait = null;
                if (page.TryGetProperty("thumbnail", out var thumbnail) &&
                    Uri.TryCreate(thumbnail.GetProperty("source").GetString(), UriKind.Absolute, out var image) &&
                    image.Scheme == "https" && image.Host == "upload.wikimedia.org")
                {
                    selectedPortrait = image;
                    photo.Source = new BitmapImage(image);
                }
                savePortrait.IsEnabled = selectedPortrait != null;
                photo.Visibility = photo.Source == null ? Visibility.Collapsed : Visibility.Visible;
                ShowCredit(null);
                if (selectedPortrait == null) return;
                try
                {
                    var found = await FindPortraitCredit(page);
                    if (picker.SelectedIndex == index) ShowCredit(found);
                }
                catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.Message); }
            };
            savePortrait.Click += async (_, _) =>
            {
                if (selectedPortrait is not Uri image) return;
                savePortrait.IsEnabled = false;
                try
                {
                    int index = picker.SelectedIndex;
                    var attribution = credit ?? await FindPortraitCredit(entries[index]);
                    await SaveArtistPortrait(artist, image);
                    await File.WriteAllTextAsync(PortraitPath(artist) + ".json", JsonSerializer.Serialize(attribution));
                    savePortrait.Content = LocalTextExtension.Translate("Retrato guardado");
                    artRequest = null;
                    RefreshRows();
                }
                catch (Exception error) { Status.Text = error.Message; }
                finally { savePortrait.IsEnabled = selectedPortrait != null; }
            };
            source.Click += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                if (Uri.TryCreate(entries[picker.SelectedIndex].GetProperty("fullurl").GetString(), UriKind.Absolute, out var article)
                    && article.Scheme == "https" && article.Host is "en.wikipedia.org" or "es.wikipedia.org" or "pt.wikipedia.org")
                    Process.Start(new ProcessStartInfo(article.AbsoluteUri) { UseShellExecute = true });
            };
            var panel = new StackPanel { Margin = new Thickness(22) };
            var localTracks = library.Tracks.Where(track => string.Equals(track.Artist, artist, StringComparison.OrdinalIgnoreCase)).ToArray();
            panel.Children.Add(new TextBlock { Text = artist, FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalTextExtension.Translate("{0:N0} pistas en la biblioteca · {1:N0} álbumes"),
                    localTracks.Length, localTracks.Select(track => track.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                Foreground = Brushes.Lavender, Margin = new Thickness(0, 5, 0, 15)
            });
            if (entries.Length == 0)
            {
                picker.Visibility = photo.Visibility = source.Visibility = Visibility.Collapsed;
                savePortrait.Visibility = Visibility.Collapsed;
                biography.Text = LocalTextExtension.Translate("Wikipedia no encontró perfiles.");
            }
            else picker.SelectedIndex = 0;
            panel.Children.Add(picker);
            panel.Children.Add(photo);
            panel.Children.Add(credits);
            panel.Children.Add(biography);
            panel.Children.Add(imageSource);
            panel.Children.Add(source);
            panel.Children.Add(savePortrait);
            new Window { Owner = this, Title = LocalTextExtension.Format("Perfil de {0}", artist), Width = 580, Height = 510,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("Error al consultar Wikipedia. ") + error.Message; }
    }

    private static async Task SaveArtistPortrait(string artist, Uri image, string path = null)
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
        DecodeArtwork(stream, 320);
        path ??= PortraitPath(artist);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(staging, stream.ToArray());
            File.Move(staging, path, true);
        }
        finally { File.Delete(staging); }
    }

    private static async Task<JsonElement[]> SearchArtist(string artist, string language)
    {
        string qualifier = language switch { "es" => "música", "pt" => "música", _ => "music" };
        string root = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" +
            "&prop=extracts%7Cpageimages%7Cinfo&exintro=1&explaintext=1&exlimit=5&exchars=1800" +
            "&inprop=url&piprop=thumbnail%7Cname&pithumbsize=360";
        var results = await ReadArtistPages(root + "&generator=search&gsrnamespace=0&gsrlimit=5&gsrsearch=" +
            Uri.EscapeDataString(artist + " " + qualifier), artist);
        return results.Length > 0 ? results : await ReadArtistPages(root + "&titles=" + Uri.EscapeDataString(artist), artist);
    }

    private static async Task<JsonElement[]> ReadArtistPages(string url, string artist)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.22 (https://github.com/josue758/PrismaMusic)");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages)) return Array.Empty<JsonElement>();
        return pages.EnumerateArray()
            .Where(page =>
            {
                if (page.TryGetProperty("missing", out _)) return false;
                string title = page.GetProperty("title").GetString();
                return string.Equals(title, artist, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(artist + " (", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(page => page.TryGetProperty("index", out var index) ? index.GetInt32() : int.MaxValue)
            .Select(page => page.Clone()).ToArray();
    }
}



