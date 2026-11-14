using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        if (MessageBox.Show(this, $"Buscar «{artist}» en Wikipedia", "Perfil de artista",
            MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;

        try
        {
            var url = "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" +
                "&generator=search&gsrnamespace=0&gsrlimit=5&prop=extracts%7Cpageimages%7Cinfo" +
                "&exintro=1&explaintext=1&exlimit=5&exchars=1800&inprop=url&piprop=thumbnail&pithumbsize=360" +
                "&gsrsearch=" + Uri.EscapeDataString(artist + " musician");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PrismaMusicWindows/0.5 (https://github.com/josue758/PrismaMusic)");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("pages", out var pages) || pages.GetArrayLength() == 0)
            {
                Status.Text = "Wikipedia no encontró perfiles.";
                return;
            }
            var entries = pages.EnumerateArray().OrderBy(page => page.GetProperty("index").GetInt32()).ToArray();
            var picker = new ComboBox { ItemsSource = entries.Select(page => page.GetProperty("title").GetString()).ToArray() };
            var photo = new Image { Height = 165, Margin = new Thickness(0, 10, 0, 8) };
            var biography = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var source = new Button { Content = "Artículo en Wikipedia", Margin = new Thickness(0, 12, 0, 0) };
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
                    && article.Scheme == "https" && article.Host == "en.wikipedia.org")
                    Process.Start(new ProcessStartInfo(article.AbsoluteUri) { UseShellExecute = true });
            };
            picker.SelectedIndex = 0;
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(picker);
            panel.Children.Add(photo);
            panel.Children.Add(biography);
            panel.Children.Add(source);
            new Window { Owner = this, Title = $"Perfil de {artist}", Width = 580, Height = 510,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
        }
        catch (Exception error) { Status.Text = "Error al consultar Wikipedia. " + error.Message; }
    }
}
