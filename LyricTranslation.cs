using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private string sourceLyrics = "";

    private void TranslateLyrics(object sender, RoutedEventArgs args)
    {
        string original = Regex.Replace(sourceLyrics, @"\[[^\]\r\n]+\]", "").Trim();
        if (original.Length == 0) { Status.Text = "Carga una letra antes de traducirla."; return; }
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Servidor LibreTranslate", FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "La letra se enviará al servidor que indiques.", Margin = new Thickness(0, 4, 0, 8) });
        var server = new TextBox { Text = "http://localhost:5000/translate", Margin = new Thickness(0, 0, 0, 8) };
        var key = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
        var translate = new Button { Content = "Traducir al español" };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(server);
        panel.Children.Add(new TextBlock { Text = "Clave API (si el servidor la requiere)" });
        panel.Children.Add(key);
        panel.Children.Add(translate);
        panel.Children.Add(result);
        translate.Click += async (_, _) =>
        {
            if (!Uri.TryCreate(server.Text.Trim(), UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)))
            {
                result.Text = "Usa un enlace HTTPS o un servidor local.";
                return;
            }
            translate.IsEnabled = false;
            result.Text = "Traduciendo…";
            try
            {
                var body = JsonSerializer.Serialize(new { q = original, source = "auto", target = "es",
                    format = "text", api_key = key.Password });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                result.Text = "ORIGINAL\n\n" + original + "\n\nESPAÑOL\n\n" +
                    json.RootElement.GetProperty("translatedText").GetString();
            }
            catch (Exception error) { result.Text = "No se pudo traducir la letra. " + error.Message; }
            finally { translate.IsEnabled = true; }
        };
        new Window { Owner = this, Title = "Traducción de letra", Width = 580, Height = 510,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }
}
