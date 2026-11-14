using System;
using System.Diagnostics;
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
        if (MessageBox.Show(this, $"Buscar «{artist}» en Wikipedia", LocalTextExtension.Translate("Perfil de artista"),
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
            var source = new Button { Content = LocalTextExtension.Translate("Artículo en Wikipedia"), Margin = new Thickness(0, 12, 0, 0) };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var page = entries[picker.SelectedIndex];
                biography.Text = page.TryGetProperty("extract", out var intro) ? intro.GetString() : "";
                photo.Source = null;
                if (page.TryGetProperty("thumbnail", out var thumbnail) &&
                    Uri.TryCreate(thumbnail.GetProperty("source").GetString(), UriKind.Absolute, out var image) &&
                    image.Scheme == "https" && image.Host == "upload.wikimedia.org")
                    photo.Source = new BitmapImage(image);
                photo.Visibility = photo.Source == null ? Visibility.Collapsed : Visibility.Visible;
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
                biography.Text = LocalTextExtension.Translate("Wikipedia no encontró perfiles.");
            }
            else picker.SelectedIndex = 0;
            panel.Children.Add(picker);
            panel.Children.Add(photo);
            panel.Children.Add(biography);
            panel.Children.Add(source);
            new Window { Owner = this, Title = $"Perfil de {artist}", Width = 580, Height = 510,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("Error al consultar Wikipedia. ") + error.Message; }
    }

    private static async Task<JsonElement[]> SearchArtist(string artist, string language)
    {
        string qualifier = language switch { "es" => "música", "pt" => "música", _ => "music" };
        var url = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" +
            "&generator=search&gsrnamespace=0&gsrlimit=5&prop=extracts%7Cpageimages%7Cinfo" +
            "&exintro=1&explaintext=1&exlimit=5&exchars=1800&inprop=url&piprop=thumbnail&pithumbsize=360" +
            "&gsrsearch=" + Uri.EscapeDataString(artist + " " + qualifier);
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
                string title = page.GetProperty("title").GetString();
                return string.Equals(title, artist, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(artist + " (", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(page => page.TryGetProperty("index", out var index) ? index.GetInt32() : int.MaxValue)
            .Select(page => page.Clone()).ToArray();
    }
}

