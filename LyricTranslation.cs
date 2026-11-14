using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        if (original.Length == 0) { Status.Text = LocalTextExtension.Translate("Carga una letra antes de traducirla."); return; }
        var panel = new StackPanel { Margin = new Thickness(22) };
        var local = new CheckBox { Content = LocalTextExtension.Translate("Usar Argos Translate local"), IsChecked = true,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(local);
        var localPanel = new StackPanel();
        var python = new TextBox { Text = LoadTranslatorPath(), Margin = new Thickness(0, 4, 0, 6) };
        var choose = new Button { Content = LocalTextExtension.Translate("Elegir Python con Argos"), HorizontalAlignment = HorizontalAlignment.Left };
        var source = new ComboBox { ItemsSource = new[] { "en", "pt", "fr", "de", "it", "ja", "ko", "zh", "ru" }, SelectedIndex = 0 };
        choose.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Python|python.exe;python3.exe|Executable|*.exe" };
            if (picker.ShowDialog() == true) python.Text = picker.FileName;
        };
        localPanel.Children.Add(python);
        localPanel.Children.Add(choose);
        localPanel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Idioma de la letra"),
            Margin = new Thickness(0, 10, 0, 4) });
        localPanel.Children.Add(source);
        panel.Children.Add(localPanel);
        var remotePanel = new StackPanel { Visibility = Visibility.Collapsed };
        remotePanel.Children.Add(new TextBlock { Text = "LibreTranslate", FontSize = 18 });
        remotePanel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("La letra se enviará al servidor que indiques."),
            Margin = new Thickness(0, 4, 0, 8) });
        var server = new TextBox { Text = "http://localhost:5000/translate", Margin = new Thickness(0, 0, 0, 8) };
        var key = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
        var translate = new Button { Content = LocalTextExtension.Translate("Traducir al español"), Margin = new Thickness(0, 10, 0, 0) };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        remotePanel.Children.Add(server);
        remotePanel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Clave API (si el servidor la requiere)") });
        remotePanel.Children.Add(key);
        panel.Children.Add(remotePanel);
        local.Click += (_, _) =>
        {
            localPanel.Visibility = local.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            remotePanel.Visibility = local.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        };
        panel.Children.Add(translate);
        panel.Children.Add(result);
        translate.Click += async (_, _) =>
        {
            translate.IsEnabled = false;
            result.Text = LocalTextExtension.Translate("Traduciendo…");
            try
            {
                string translated;
                if (local.IsChecked == true)
                {
                    translated = await TranslateLocally(python.Text.Trim(), (string)source.SelectedItem, original);
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(Path.Combine(DataDirectory, "translator-path.txt"), python.Text.Trim());
                }
                else
                {
                    if (!Uri.TryCreate(server.Text.Trim(), UriKind.Absolute, out var endpoint) ||
                        (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)))
                        throw new InvalidOperationException(LocalTextExtension.Translate("Usa un enlace HTTPS o un servidor local."));
                    var body = JsonSerializer.Serialize(new { q = original, source = "auto", target = "es",
                        format = "text", api_key = key.Password });
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var response = await Http.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    translated = json.RootElement.GetProperty("translatedText").GetString();
                }
                result.Text = "ORIGINAL\n\n" + original + "\n\nESPAÑOL\n\n" + translated;
            }
            catch (Exception error) { result.Text = LocalTextExtension.Translate("No se pudo traducir la letra.") + " " + error.Message; }
            finally { translate.IsEnabled = true; }
        };
        new Window { Owner = this, Title = LocalTextExtension.Translate("Traducción de letra"), Width = 580, Height = 510,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }

    private static string LoadTranslatorPath()
    {
        try { return File.ReadAllText(Path.Combine(DataDirectory, "translator-path.txt")).Trim(); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    private static async Task<string> TranslateLocally(string python, string language, string original)
    {
        if (!Path.IsPathFullyQualified(python) || !File.Exists(python))
            throw new InvalidOperationException(LocalTextExtension.Translate("Elige un Python que tenga Argos Translate instalado."));
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("""
            import sys
            from argostranslate import tokenizer, translate
            import sentencepiece

            # Load model bytes through Python so Windows profile paths can contain Unicode.
            def load_model(self):
                if self.processor is None:
                    self.processor = sentencepiece.SentencePieceProcessor(model_proto=self.model_file.read_bytes())
                return self.processor

            tokenizer.SentencePieceTokenizer.lazy_processor = load_model
            print(translate.translate(sys.stdin.read(), sys.argv[1], "es"))
            """);
        start.ArgumentList.Add(language);
        start.Environment["ARGOS_CHUNK_TYPE"] = "MINISBD";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(original.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException((await errors).Trim());
            return (await output).Trim();
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
