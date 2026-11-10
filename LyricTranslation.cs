using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
    private int lyricRevision;
    private IReadOnlyDictionary<string, string> translatedLines;

    private string BilingualLine(string line) =>
        translatedLines != null && translatedLines.TryGetValue(line, out var translation) &&
        !string.Equals(line, translation, StringComparison.OrdinalIgnoreCase)
            ? line + "\n" + translation : line;

    private void TranslateLyrics(object sender, RoutedEventArgs args)
    {
        string original = Regex.Replace(sourceLyrics, @"\[[^\]\r\n]+\]", "").Trim();
        int revision = lyricRevision;
        string[] lines = lyricLines.Select(line => line.Text)
            .Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.Ordinal).ToArray();
        if (original.Length == 0) { Status.Text = LocalTextExtension.Translate("Carga una letra antes de traducirla."); return; }
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Tus letras. Tu idioma."),
            FontSize = 25, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Traducción sin conexión. La primera vez se descarga el motor y el modelo; después funciona sin enviar letras a un servidor."),
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var source = new ComboBox { ItemsSource = new[] { "en", "pt", "fr", "de", "it", "ja", "ko", "zh", "ru" }, SelectedIndex = 0 };
        source.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("IDIOMA ORIGINAL"), FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 5) });
        panel.Children.Add(source);
        var translate = new Button { Content = LocalTextExtension.Translate("Traducir al español"),
            Background = (Brush)FindResource("AccentBrush"), Foreground = (Brush)FindResource("OnAccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
            Foreground = Brushes.White, FontSize = 14, LineHeight = 24 };
        panel.Children.Add(result);
        panel.Children.Add(translate);
        translate.Click += async (_, _) =>
        {
            translate.IsEnabled = false;
            result.Text = LocalTextExtension.Translate("Traduciendo…");
            try
            {
                string language = (string)source.SelectedItem;
                string python = ManagedPython;
                if (!await HasLocalTranslatorModel(python, language))
                {
                    python = await InstallLocalTranslator(null, TranslatorDirectory, language,
                        status => result.Text = LocalTextExtension.Translate(status));
                }
                if (lines.Length > 0)
                {
                    var translations = await TranslateLocalLines(python, language, lines, TranslatorDirectory);
                    var pairs = lines.Zip(translations).ToDictionary(pair => pair.First, pair => pair.Second);
                    if (lyricRevision == revision)
                    {
                        translatedLines = pairs;
                        Tick();
                    }
                    result.Text = string.Join("\n\n", lines.Select(line => line + "\n" + pairs[line]));
                }
                else result.Text = "ORIGINAL\n\n" + original + "\n\nESPAÑOL\n\n" +
                    await TranslateLocally(python, language, original);
            }
            catch (Exception error) { result.Text = LocalTextExtension.Translate("No se pudo traducir la letra.") + " " + error.Message; }
            finally { translate.IsEnabled = true; }
        };
        new Window { Owner = this, Title = LocalTextExtension.Translate("Traducción de letra"), Width = 620, Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(30, 28, 35)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }

    private static async Task<string> TranslateLocally(string python, string language, string original) =>
        (await TranslateLocalLines(python, language, new[] { original }))[0];

    private static async Task<bool> HasLocalTranslatorModel(string python, string language)
    {
        if (!File.Exists(python)) return false;
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        ConfigureTranslatorEnvironment(start, TranslatorDirectory);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("from argostranslate import package; import sys; sys.exit(0 if any(p.from_code == sys.argv[1] and p.to_code == 'es' for p in package.get_installed_packages()) else 1)");
        start.ArgumentList.Add(language);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el traductor local.");
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static async Task<string[]> TranslateLocalLines(string python, string language, string[] lines, string managedRoot = null)
    {
        if (!Path.IsPathFullyQualified(python) || !File.Exists(python))
            throw new InvalidOperationException(LocalTextExtension.Translate("No se pudo preparar el traductor sin conexión."));
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
            import json
            import sys
            from argostranslate import tokenizer, translate
            import sentencepiece

            # Load model bytes through Python so Windows profile paths can contain Unicode.
            def load_model(self):
                if self.processor is None:
                    self.processor = sentencepiece.SentencePieceProcessor(model_proto=self.model_file.read_bytes())
                return self.processor

            tokenizer.SentencePieceTokenizer.lazy_processor = load_model
            lines = json.load(sys.stdin)
            translated = [translate.translate(line, sys.argv[1], "es") for line in lines]
            print(json.dumps(translated, ensure_ascii=False))
            """);
        start.ArgumentList.Add(language);
        ConfigureTranslatorEnvironment(start, managedRoot ?? TranslatorDirectory);
        start.Environment["ARGOS_CHUNK_TYPE"] = "MINISBD";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(lines).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException((await errors).Trim());
            var result = JsonSerializer.Deserialize<string[]>(await output);
            if (result == null || result.Length != lines.Length || result.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException(LocalTextExtension.Translate("No se pudo traducir la letra."));
            return result;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
