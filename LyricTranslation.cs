using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

    private Window lyricTranslationWindow;
    private string preferredLyricSource = "en";
    private string preferredLyricTarget = "es";

    private sealed record LyricLanguage(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly LyricLanguage[] LyricLanguages =
        ("en:English|es:Español|pt:Português|pb:Português (Brasil)|fr:Français|it:Italiano|de:Deutsch|ja:日本語|ko:한국어|" +
         "zh:简体中文|zt:繁體中文|ar:العربية|ru:Русский|uk:Українська|nl:Nederlands|sv:Svenska|da:Dansk|nb:Norsk bokmål|" +
         "fi:Suomi|pl:Polski|cs:Čeština|sk:Slovenčina|ro:Română|hu:Magyar|el:Ελληνικά|tr:Türkçe|he:עברית|hi:हिन्दी|" +
         "bn:বাংলা|ur:اردو|fa:فارسی|id:Bahasa Indonesia|ms:Bahasa Melayu|vi:Tiếng Việt|th:ไทย|tl:Tagalog|ca:Català|" +
         "eu:Euskara|gl:Galego|ga:Gaeilge|sq:Shqip|az:Azərbaycanca|bg:Български|eo:Esperanto|et:Eesti|ky:Кыргызча|" +
         "lv:Latviešu|lt:Lietuvių|sl:Slovenščina|sw:Kiswahili").Split('|')
        .Select(entry => new LyricLanguage(entry[..entry.IndexOf(':')], entry[(entry.IndexOf(':') + 1)..])).ToArray();

    private IEnumerable<string> OriginalLyricLines() => lyricLines.Length > 0
        ? lyricLines.Select(line => line.Text)
        : sourceLyrics.Split('\n').Select(line => line.Trim());

    private void TranslateLyrics(object sender, RoutedEventArgs args)
    {
        if (lyricTranslationWindow is { IsVisible: true })
        {
            lyricTranslationWindow.Activate();
            return;
        }

        int revision = lyricRevision;
        string[] verses = OriginalLyricLines().ToArray();
        string[] lines = verses.Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.Ordinal).ToArray();
        if (lines.Length == 0)
        {
            Status.Text = LocalTextExtension.Translate("Carga una letra antes de traducirla.");
            return;
        }

        var dialog = new Window
        {
            Owner = listeningRoom ?? this, Title = LocalTextExtension.Translate("Traducción de letra"),
            Width = 640, Height = 620, MinWidth = 440, MinHeight = 400,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.SetResourceReference(BackgroundProperty, "SettingsBackground");
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var title = new TextBlock { Text = dialog.Title, FontSize = 28, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        header.Children.Add(title);
        var description = new TextBlock
        {
            Text = LocalTextExtension.Translate("Traducción sin conexión. La primera vez se descarga el motor y el modelo; después funciona sin enviar letras a un servidor."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        header.Children.Add(description);

        var languages = new Grid();
        languages.ColumnDefinitions.Add(new ColumnDefinition());
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        languages.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(languages);
        ComboBox AddLanguage(string label, string selected, int column)
        {
            var group = new StackPanel();
            Grid.SetColumn(group, column);
            languages.Children.Add(group);
            var caption = new TextBlock { Text = LocalTextExtension.Translate(label), Margin = new Thickness(0, 0, 0, 5) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            group.Children.Add(caption);
            var picker = new ComboBox { ItemsSource = LyricLanguages, SelectedValuePath = nameof(LyricLanguage.Code),
                SelectedValue = selected, Style = (Style)FindResource("FilterCombo") };
            System.Windows.Automation.AutomationProperties.SetName(picker, caption.Text);
            group.Children.Add(picker);
            return picker;
        }

        var source = AddLanguage("Idioma original", preferredLyricSource, 0);
        var target = AddLanguage("Idioma de destino", preferredLyricTarget, 2);
        source.SelectionChanged += (_, _) => RememberLanguages();
        target.SelectionChanged += (_, _) => RememberLanguages();

        void RememberLanguages()
        {
            if (source.SelectedValue is string from) preferredLyricSource = from;
            if (target.SelectedValue is string to) preferredLyricTarget = to;
            SaveAudioSettings();
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
        header.Children.Add(actions);
        var translate = new Button { Content = LocalTextExtension.Translate("Traducir"), IsDefault = true };
        translate.SetResourceReference(BackgroundProperty, "AccentBrush");
        translate.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        actions.Children.Add(translate);
        var copy = new Button { Content = LocalTextExtension.Translate("Copiar traducción"),
            IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(copy);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        header.Children.Add(status);
        var result = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 15,
            Padding = new Thickness(12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed };
        result.SetResourceReference(ForegroundProperty, "Ink");
        result.SetResourceReference(BackgroundProperty, "NavigationBackdrop");
        System.Windows.Automation.AutomationProperties.SetName(result, dialog.Title);
        root.Children.Add(result);

        string translatedText = "";
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(translatedText);
                status.Text = LocalTextExtension.Translate("Traducción copiada.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                status.Text = LocalTextExtension.Translate("No se pudo copiar la traducción.");
            }
        };

        translate.Click += async (_, _) =>
        {
            string from = (string)source.SelectedValue;
            string to = (string)target.SelectedValue;
            if (from == to)
            {
                status.Text = LocalTextExtension.Translate("Elige dos idiomas diferentes.");
                return;
            }

            source.IsEnabled = target.IsEnabled = translate.IsEnabled = copy.IsEnabled = false;
            result.Visibility = Visibility.Collapsed;
            status.Text = LocalTextExtension.Translate("Traduciendo…");
            try
            {
                string python = ManagedPython;
                if (!await HasLocalTranslatorModel(python, from, to))
                    python = await InstallLocalTranslator(null, TranslatorDirectory, from,
                        message => status.Text = LocalTextExtension.Translate(message), to);

                status.Text = LocalTextExtension.Translate("Traduciendo…");
                var translations = await TranslateLocalLines(python, from, lines, TranslatorDirectory, to);
                var pairs = lines.Zip(translations).ToDictionary(pair => pair.First, pair => pair.Second);
                if (lyricRevision == revision)
                {
                    translatedLines = pairs;
                    RefreshLyrics();
                }
                translatedText = string.Join(Environment.NewLine, verses.Select(line => pairs.GetValueOrDefault(line, line)));
                result.Text = string.Join(Environment.NewLine + Environment.NewLine,
                    verses.Where(pairs.ContainsKey).Select(line => line + Environment.NewLine + pairs[line]));
                result.Visibility = Visibility.Visible;
                copy.IsEnabled = true;
                status.Text = lyricRevision == revision
                    ? LocalTextExtension.Translate("Traducción disponible en el panel de letras.")
                    : LocalTextExtension.Translate("Cambió la canción. Puedes copiar esta traducción.");
            }
            catch (Exception error)
            {
                Trace.WriteLine(error);
                status.Text = LocalTextExtension.Translate(error.Message.Contains("PRISMA_NO_TRANSLATION_ROUTE", StringComparison.Ordinal)
                    ? "No hay un modelo para estos idiomas."
                    : "No se pudo traducir la letra. Comprueba la conexión si es la primera vez e inténtalo de nuevo.");
            }
            finally { source.IsEnabled = target.IsEnabled = translate.IsEnabled = true; }
        };
        dialog.Content = root;
        ApplyFluentDialog(dialog, root);
        lyricTranslationWindow = dialog;
        dialog.Closed += (_, _) => lyricTranslationWindow = null;
        dialog.Show();
    }

    private static async Task<bool> HasLocalTranslatorModel(string python, string language, string target)
    {
        if (!File.Exists(python)) return false;
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true };
        ConfigureTranslatorEnvironment(start, TranslatorDirectory);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(LocalModelRoute + """
            import sys
            from argostranslate import package
            installed = translation_packages(package.get_installed_packages())
            sys.exit(0 if route(installed, sys.argv[1], sys.argv[2]) else 1)
            """);
        start.ArgumentList.Add(language);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el traductor local.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<string[]> TranslateLocalLines(string python, string language, string[] lines, string managedRoot = null, string target = "es")
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
        start.ArgumentList.Add(LocalModelRoute + """
            import json
            import sys
            from argostranslate import package, tokenizer, translate
            import sentencepiece

            # Load model bytes through Python so Windows profile paths can contain Unicode.
            def load_model(self):
                if self.processor is None:
                    self.processor = sentencepiece.SentencePieceProcessor(model_proto=self.model_file.read_bytes())
                return self.processor

            tokenizer.SentencePieceTokenizer.lazy_processor = load_model
            lines = json.load(sys.stdin)
            packages = translation_packages(package.get_installed_packages())
            selected = route(packages, sys.argv[1], sys.argv[2])
            if not selected:
                raise RuntimeError("PRISMA_NO_TRANSLATION_ROUTE")
            translated = lines
            for pair in selected:
                model = packages[pair]
                source = translate.Language(model.from_code, model.from_name)
                target = translate.Language(model.to_code, model.to_name)
                engine = translate.PackageTranslation(source, target, model)
                translated = [engine.translate(line) for line in translated]
            print(json.dumps(translated, ensure_ascii=False))
            """);
        start.ArgumentList.Add(language);
        start.ArgumentList.Add(target);
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
