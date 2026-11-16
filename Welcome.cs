using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string WelcomeFile => Path.Combine(DataDirectory, "welcome-complete");

    private void ShowWelcomeOnFirstRun()
    {
        if (library.Tracks.Count == 0 && library.Folders.Count == 0 && !File.Exists(WelcomeFile))
            ShowWelcome(this, new RoutedEventArgs());
    }

    private bool scanSubfolders = true;
    private bool scanOnLaunch;
    private int parallelTagReads;
    private static readonly int[] TagReaderOptions = { 0, 2, 4, 8 };
    private static readonly string[] TagReaderLabels = { "Automático", "2", "4", "8" };

    private async void ShowWelcome(object sender, RoutedEventArgs args)
    {
        string[] codes = { "es", "en", "pt" };
        string[] steps = { "Elige tu idioma", "Tu colección", "Dale tu estilo", "Listo para escuchar" };
        string[] descriptions =
        {
            "Puedes cambiar todo esto después desde Configuración.",
            "Añade carpetas o archivos de música, o conecta tu NAS.",
            "Prueba una apariencia. La vista previa conserva tus ajustes de sonido.",
            "Ajusta lo esencial para tu próxima sesión."
        };
        string[] icons = { "\uE774", "\uE8B7", "\uE790", "\uE7F6" };
        string language = LocalTextExtension.Language;
        string selectedAccent = AccentChoices.Any(choice => choice.Id == accentPreset) ? accentPreset : "violet";
        int selectedTheme = themePreference;
        int selectedProfile = appearanceProfile;
        bool accentChanged = false;
        double previewTransparency = surfaceTransparency;
        int step = 0;
        bool finish = false;
        bool postpone = false;
        var sources = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages = Enumerable.Range(0, steps.Length).Select(_ => new StackPanel { Margin = new Thickness(0, 0, 8, 10) }).ToArray();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.69, GridUnitType.Star), MinWidth = 210, MaxWidth = 260 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var dialog = new Window
        {
            Owner = sender is DependencyObject element ? Window.GetWindow(element) ?? this : this,
            Title = T("Primeros pasos"), Icon = Icon, FontFamily = FontFamily, FontSize = 14,
            Width = 960, Height = 710, MinWidth = 740, MinHeight = 525,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize, UseLayoutRounding = true, Content = root,
            ShowInTaskbar = false
        };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome
        {
            CaptionHeight = 34, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        var rail = new Border
        {
            CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 30, 20, 20), Margin = new Thickness(18, 18, 0, 18)
        };
        rail.SetResourceReference(Border.BackgroundProperty, "WelcomeSurface");
        rail.SetResourceReference(Border.BorderBrushProperty, "SettingsStroke");
        var railContent = new DockPanel();
        rail.Child = railContent;
        root.Children.Add(rail);

        var brand = new StackPanel();
        var wave = new System.Windows.Shapes.Path
        {
            Style = (Style)FindResource("VisualizerIcon"), Width = 64, Height = 55,
            StrokeThickness = 1.6, HorizontalAlignment = HorizontalAlignment.Left
        };
        wave.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        brand.Children.Add(wave);
        var wordmark = Copy("P R I S M A\nM U S I C", 26, false);
        wordmark.FontWeight = FontWeights.SemiBold;
        wordmark.Margin = new Thickness(0, 22, 0, 12);
        brand.Children.Add(wordmark);
        brand.Children.Add(Copy(T("Tu música. Tu espacio.\nA tu manera."), 13));
        DockPanel.SetDock(brand, Dock.Top);
        railContent.Children.Add(brand);
        var sideFooter = Copy("01 — 04  /  PRISMA", 10);
        sideFooter.FontFamily = new FontFamily("Consolas");
        sideFooter.Margin = new Thickness(0, 15, 0, 0);
        DockPanel.SetDock(sideFooter, Dock.Bottom);
        railContent.Children.Add(sideFooter);
        var sideSteps = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        var navigation = new Button[steps.Length];
        Action<int> navigateStep = null;
        for (int i = 0; i < steps.Length; i++)
        {
            int index = i;
            navigation[i] = IconButton(steps[i], icons[i], () => navigateStep(index));
            navigation[i].Style = (Style)FindResource("FluentNavigationButton");
            navigation[i].HorizontalContentAlignment = HorizontalAlignment.Left;
            navigation[i].Margin = new Thickness(-8, 2, 0, 2);
            navigation[i].MinHeight = 38;
            sideSteps.Children.Add(navigation[i]);
        }
        railContent.Children.Add(sideSteps);

        var content = new DockPanel { Margin = new Thickness(28, 23, 26, 18) };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        var stepLabel = Copy("", 12, false);
        stepLabel.FontFamily = new FontFamily("Consolas");
        stepLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var close = IconButton("Cerrar", "\uE8BB", dialog.Close, iconOnly: true);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        var stepHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(close, Dock.Right);
        stepHeader.Children.Add(close);
        stepHeader.Children.Add(stepLabel);
        DockPanel.SetDock(stepHeader, Dock.Top);
        content.Children.Add(stepHeader);

        var back = IconButton("Atrás", "\uE72B", () => navigateStep(step - 1));
        var later = IconButton("Más tarde", null, () => { postpone = true; dialog.Close(); });
        var next = IconButton("Continuar", null, () =>
        {
            if (step < steps.Length - 1) navigateStep(step + 1);
            else { finish = true; dialog.Close(); }
        });
        next.IsDefault = true;
        MarkPrimary(next);
        var footer = new DockPanel { Margin = new Thickness(0, 11, 0, 0), LastChildFill = false };
        var nextActions = new StackPanel { Orientation = Orientation.Horizontal };
        later.Margin = new Thickness(0, 0, 8, 0);
        nextActions.Children.Add(later);
        nextActions.Children.Add(next);
        DockPanel.SetDock(nextActions, Dock.Right);
        footer.Children.Add(nextActions);
        DockPanel.SetDock(back, Dock.Left);
        footer.Children.Add(back);
        var footerLayout = new StackPanel();
        footerLayout.Children.Add(Divider(new Thickness(0, 6, 0, 0)));
        footerLayout.Children.Add(footer);
        DockPanel.SetDock(footerLayout, Dock.Bottom);
        content.Children.Add(footerLayout);

        var title = Copy("", 36, false);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 12);
        var description = Copy("", 15);
        description.Margin = new Thickness(0, 0, 0, 16);
        var pageHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var sheet = new StackPanel();
        sheet.Children.Add(title);
        sheet.Children.Add(description);
        sheet.Children.Add(pageHost);
        var scroll = new ScrollViewer
        {
            Style = (Style)FindResource("LyricsScroll"), Content = sheet,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        content.Children.Add(scroll);

        string[] languageNames = { "Español", "English", "Português" };
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            var radio = Choice(languageNames[i], "WelcomeLanguage", language == code);
            radio.Margin = new Thickness(0, 0, 0, 12);
            radio.MinHeight = 60;
            var label = Copy(languageNames[i], 16, false);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(10, 0, 0, 0);
            label.FontWeight = FontWeights.SemiBold;
            var mark = Copy("○", 18);
            mark.Margin = new Thickness(0, 0, 10, 0);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(mark, Dock.Right);
            row.Children.Add(mark);
            row.Children.Add(label);
            radio.Content = row;
            void MarkLanguage()
            {
                bool selected = radio.IsChecked == true;
                mark.Text = selected ? "✓" : "○";
                mark.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "Muted");
            }
            MarkLanguage();
            radio.Checked += (_, _) => { language = code; MarkLanguage(); };
            radio.Unchecked += (_, _) => MarkLanguage();
            pages[0].Children.Add(radio);
        }

        var selection = Copy("", 13);
        void AddSources(System.Collections.Generic.IEnumerable<string> paths)
        {
            foreach (string path in paths) sources.Add(path);
            selection.Text = LocalTextExtension.Format("{0:N0} canciones · {1:N0} ubicaciones",
                library.Tracks.Count, library.Folders.Concat(sources).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            selection.ToolTip = sources.Count == 0 ? null : string.Join(Environment.NewLine, sources.Take(12));
        }
        AddSources(Array.Empty<string>());
        var libraryActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var add = IconButton("Añadir música", "\uE710");
        var addMenu = new ContextMenu { Style = (Style)FindResource("PlayerMenu") };
        AddImport("Añadir carpetas", () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Multiselect = true, Title = T("Añadir carpetas") };
            if (picker.ShowDialog(dialog) == true) AddSources(picker.FolderNames);
        });
        AddImport("Añadir archivos", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = T("Añadir archivos"),
                Filter = T("Archivos de audio") + "|" + LibraryScanner.AudioFilePattern };
            if (picker.ShowDialog(dialog) == true) AddSources(picker.FileNames);
        });
        add.Click += (_, _) =>
        {
            addMenu.PlacementTarget = add;
            addMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            addMenu.IsOpen = true;
        };
        libraryActions.Children.Add(add);
        var nas = IconButton("Conectar NAS", "\uE968", () =>
        {
            if (AskNasSource(dialog) is string path) AddSources(new[] { path });
        });
        MarkPrimary(add);
        MarkPrimary(nas);
        nas.Margin = new Thickness(8, 0, 8, 0);
        libraryActions.Children.Add(nas);
        libraryActions.Children.Add(IconButton("Probar demos", null, () => AddSources(new[] { Path.Combine(AppContext.BaseDirectory, "Demo") })));
        pages[1].Children.Add(libraryActions);
        pages[1].Children.Add(selection);
        var recurse = Check("Incluir subcarpetas al escanear", scanSubfolders);
        var launch = Check("Leer etiquetas al iniciar", scanOnLaunch);
        pages[1].Children.Add(recurse);
        pages[1].Children.Add(launch);
        pages[1].Children.Add(Divider(new Thickness(0, 13, 0, 13)));
        pages[1].Children.Add(Copy(T("Las etiquetas editadas se guardan en Prisma y se aplican a tu biblioteca."), 13));
        var edit = IconButton("Editar etiquetas de biblioteca", "\uE70F", () =>
            EditLibraryTags(Selected ?? library.Tracks.FirstOrDefault(), dialog));
        edit.IsEnabled = library.Tracks.Count > 0;
        edit.HorizontalAlignment = HorizontalAlignment.Left;
        edit.Margin = new Thickness(0, 10, 0, 1);
        pages[1].Children.Add(edit);
        var online = Check("Completar desde internet", autoArtwork || autoLyrics);
        pages[1].Children.Add(online);
        pages[1].Children.Add(Copy(T("Prisma consulta catálogos de carátulas y letras usando los metadatos de la pista."), 13));

        var profiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 14) };
        string[] profileLabels = { "Cristal vivo", "Calma", "Contraste" };
        string[] profileGlyphs = { "M 5,0 C 5,0 0,6 0,9 A 5,5 0 0 0 10,9 C 10,6 5,0 5,0", "\uE708", "◐" };
        for (int i = 0; i < profileLabels.Length; i++)
        {
            int value = i;
            var profile = Choice(T(profileLabels[i]), "WelcomeProfiles", selectedProfile == i);
            var tile = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            FrameworkElement symbol = i == 0 ? new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(profileGlyphs[i]), StrokeThickness = 1.5,
                Width = 15, Height = 20, Stretch = Stretch.Uniform
            } : i == 1 ? Glyph(profileGlyphs[i], 20) : Copy(profileGlyphs[i], 20, false);
            if (symbol is System.Windows.Shapes.Path path) path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            tile.Children.Add(symbol);
            var label = Copy(T(profileLabels[i]), 13, false);
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(0, 8, 0, 0);
            tile.Children.Add(label);
            profile.Content = tile;
            profile.MinHeight = 76;
            profile.Checked += (_, _) => { selectedProfile = value; UpdatePalette(); };
            profiles.Children.Add(profile);
        }
        pages[2].Children.Add(profiles);
        pages[2].Children.Add(Segments("Tema", new[] { "Sistema", "Oscuro", "Claro" }, selectedTheme,
            index => { selectedTheme = index; UpdatePalette(); }));
        var accents = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var accent in AccentChoices)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("AccentSwatch"), GroupName = "WelcomeAccents",
                Background = new SolidColorBrush(accent.Color), Foreground = new SolidColorBrush(ColorContrast(accent.Color)),
                Width = 49, Content = T(accent.Label), IsChecked = selectedAccent == accent.Id, ToolTip = T(accent.Label)
            };
            AutomationProperties.SetName(option, T(accent.Label));
            option.Checked += (_, _) => { accentChanged = true; selectedAccent = accent.Id; UpdatePalette(); };
            accents.Children.Add(option);
        }
        pages[2].Children.Add(accents);
        var fromCover = Check("Adaptar colores a la carátula", artworkColors);
        pages[2].Children.Add(fromCover);
        var transparencyLabel = Copy(T("Transparencia del cristal"), 13, false);
        transparencyLabel.Margin = new Thickness(0, 10, 0, 8);
        var transparency = new Slider { Style = (Style)FindResource("TransportSlider"),
            Minimum = 0, Maximum = 1, Value = surfaceTransparency, Height = 22, IsMoveToPointEnabled = true };
        AutomationProperties.SetLabeledBy(transparency, transparencyLabel);
        transparency.ValueChanged += (_, _) => { previewTransparency = transparency.Value; UpdatePalette(); };
        pages[2].Children.Add(transparencyLabel);
        pages[2].Children.Add(transparency);
        var reactToMusic = Check("Fondo que responde al audio", audioBackdrop);
        var animations = Check("Animaciones de la interfaz", interfaceAnimations);
        pages[2].Children.Add(reactToMusic);
        pages[2].Children.Add(animations);

        string[] startupNames = { "Inicio", "Canciones", "Álbumes", "Última sección" };
        int[] startupValues = { 3, 0, 1, 2 };
        var startupPicker = new ComboBox
        {
            Style = (Style)FindResource("FilterCombo"), ItemsSource = startupNames.Select(T).ToArray(),
            Width = 198, SelectedIndex = Array.IndexOf(startupValues, startupSection)
        };
        startupPicker.SetResourceReference(ForegroundProperty, "Ink");
        var startupLabel = Copy(T("Pantalla de inicio"), 14, false);
        startupLabel.VerticalAlignment = VerticalAlignment.Center;
        startupLabel.Margin = new Thickness(0, 0, 12, 0);
        AutomationProperties.SetLabeledBy(startupPicker, startupLabel);
        var startupRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        startupRow.Children.Add(startupLabel);
        startupRow.Children.Add(startupPicker);
        pages[3].Children.Add(startupRow);
        var remember = Check("Recordar canción y posición", rememberPosition);
        var covers = Check("Buscar carátulas faltantes", autoArtwork);
        var lyrics = Check("Buscar letras faltantes", autoLyrics);
        online.Click += (_, _) => covers.IsChecked = lyrics.IsChecked = online.IsChecked == true;
        covers.Click += (_, _) => online.IsChecked = covers.IsChecked == true || lyrics.IsChecked == true;
        lyrics.Click += (_, _) => online.IsChecked = covers.IsChecked == true || lyrics.IsChecked == true;
        pages[3].Children.Add(remember);
        pages[3].Children.Add(covers);
        pages[3].Children.Add(lyrics);
        int readers = parallelTagReads;
        var readerChoices = Segments("Lecturas simultáneas", TagReaderLabels, Array.IndexOf(TagReaderOptions, readers),
            index => readers = TagReaderOptions[index]);
        readerChoices.Margin = new Thickness(0, 10, 0, 8);
        pages[3].Children.Add(readerChoices);
        pages[3].Children.Add(Copy(T("Automático ajusta las lecturas a la carpeta. Elige 2, 4 u 8 para fijar un límite."), 13));
        var note = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16),
            Child = Copy(T("Puedes volver a esta bienvenida desde Configuración."), 14, false) };
        note.SetResourceReference(Border.BackgroundProperty, "WelcomeSurface");
        note.SetResourceReference(Border.BorderBrushProperty, "SettingsStroke");
        pages[3].Children.Add(note);

        dialog.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != System.Windows.Input.Key.Escape) return;
            key.Handled = true;
            dialog.Close();
        };
        dialog.SourceInitialized += (_, _) => UpdateWindowCaptionTheme(dialog);
        navigateStep = ShowStep;
        ShowStep(0);
        UpdatePalette();
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_WELCOME_PREVIEW") is string preview)
        {
            dialog.ContentRendered += (_, _) =>
            {
                int index = 0;
                void Capture()
                {
                    string suffix = index == 0 ? "" : $"-step-{index + 1}";
                    string path = Path.Combine(Path.GetDirectoryName(preview)!, Path.GetFileNameWithoutExtension(preview) + suffix + ".png");
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var image = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    image.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(path)) image.Save(stream);
                    if (++index == pages.Length) { dialog.Close(); return; }
                    ShowStep(index);
                    dialog.Dispatcher.BeginInvoke(Capture, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                Capture();
            };
        }
        dialog.ShowDialog();
        if (smokeTest || (!finish && !postpone)) return;
        if (finish)
        {
            if (language != LocalTextExtension.Language)
            {
                try { LocalTextExtension.SaveLanguage(language); }
                catch (Exception error)
                {
                    Status.Text = T("No se pudo guardar el idioma. ") + error.Message;
                    language = LocalTextExtension.Language;
                }
            }
            scanSubfolders = recurse.IsChecked == true;
            scanOnLaunch = launch.IsChecked == true;
            parallelTagReads = readers;
            rememberPosition = remember.IsChecked == true;
            autoArtwork = covers.IsChecked == true;
            autoLyrics = lyrics.IsChecked == true;
            startupSection = startupPicker.SelectedIndex >= 0 ? startupValues[startupPicker.SelectedIndex] : startupSection;
            appearanceProfile = selectedProfile;
            artworkColors = fromCover.IsChecked == true;
            if (accentChanged || (!artworkColors && accentPreset == "artwork")) accentPreset = selectedAccent;
            surfaceTransparency = transparency.Value;
            interfaceAnimations = animations.IsChecked == true;
            audioBackdrop = reactToMusic.IsChecked == true;
            ApplyTheme(selectedTheme);
            SaveAudioSettings();
        }
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(WelcomeFile, "1");
        }
        catch (Exception error) { Trace.WriteLine(error); }
        if (finish && sources.Count != 0) await Scan(sources.ToArray(), add: true);
        if (finish && language != LocalTextExtension.Language) RestartForLanguage();

        TextBlock Copy(string text, double size, bool muted = true)
        {
            var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
            return block;
        }

        TextBlock Glyph(string glyph, double size = 15) => new()
        {
            Text = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center
        };

        Button IconButton(string label, string glyph, Action action = null, bool iconOnly = false)
        {
            var button = new Button { MinHeight = 34, Padding = new Thickness(10, 5, 10, 5) };
            AutomationProperties.SetName(button, T(label));
            if (glyph == null) button.Content = T(label);
            else
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(Glyph(glyph));
                if (!iconOnly) panel.Children.Add(new TextBlock { Text = T(label), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                button.Content = panel;
            }
            if (action != null) button.Click += (_, _) => action();
            return button;
        }

        void MarkPrimary(Button button)
        {
            button.SetResourceReference(BackgroundProperty, "AccentBrush");
            button.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        }

        RadioButton Choice(string caption, string group, bool selected)
        {
            var button = new RadioButton { Content = caption, GroupName = group, IsChecked = selected,
                Style = (Style)FindResource("WelcomeChoice"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, caption);
            return button;
        }

        CheckBox Check(string label, bool enabled) => new()
        {
            Content = T(label), IsChecked = enabled, Style = (Style)FindResource("WelcomeCheck")
        };

        Border Divider(Thickness margin)
        {
            var rule = new Border { Height = 1, Margin = margin };
            rule.SetResourceReference(Border.BackgroundProperty, "SettingsStroke");
            return rule;
        }

        WrapPanel Segments(string label, string[] options, int selected, Action<int> change)
        {
            var row = new WrapPanel();
            var caption = Copy(T(label), 14, false);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(caption);
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var option = Choice(T(options[i]), "Welcome-" + label, selected == i);
                option.Margin = new Thickness(0, 0, 4, 3);
                option.Padding = new Thickness(10, 5, 10, 5);
                option.Checked += (_, _) => change(index);
                row.Children.Add(option);
            }
            return row;
        }

        void AddImport(string name, Action select)
        {
            var item = new MenuItem { Header = T(name), Style = (Style)FindResource("PlayerMenuItem") };
            item.Click += (_, _) => select();
            addMenu.Items.Add(item);
        }

        void ShowStep(int index)
        {
            step = Math.Clamp(index, 0, pages.Length - 1);
            stepLabel.Text = $"{step + 1:00}  /  {steps.Length:00}";
            title.Text = T(steps[step]);
            description.Text = T(descriptions[step]);
            pageHost.Content = pages[step];
            back.Visibility = step == 0 ? Visibility.Hidden : Visibility.Visible;
            string nextLabel = T(step == pages.Length - 1 ? "Empezar a escuchar" : "Continuar");
            next.Content = nextLabel;
            AutomationProperties.SetName(next, nextLabel);
            for (int i = 0; i < navigation.Length; i++)
            {
                bool active = i == step;
                if (active) navigation[i].SetResourceReference(BackgroundProperty, "ControlFill");
                else navigation[i].Background = Brushes.Transparent;
                navigation[i].SetResourceReference(ForegroundProperty, active ? "AccentBrush" : "Muted");
                if (navigation[i].Content is StackPanel row)
                    foreach (var label in row.Children.OfType<TextBlock>())
                        label.SetResourceReference(TextBlock.ForegroundProperty, active ? "AccentBrush" : "Muted");
                AutomationProperties.SetItemStatus(navigation[i], i == step ? T("Paso actual") : "");
            }
            scroll.ScrollToTop();
            if (animations.IsChecked == true && SystemParameters.ClientAreaAnimation)
                pageHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.84, 1,
                    TimeSpan.FromMilliseconds(140)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }

        void UpdatePalette()
        {
            bool isLight = selectedTheme == 2 || (selectedTheme == 0 && IsWindowsLightTheme());
            var accent = AccentChoices.First(choice => choice.Id == selectedAccent).Color;
            var strongAccent = isLight ? Mix(accent, Color.FromRgb(35, 22, 56), .49) : accent;
            foreach (var (key, dark, light) in ThemeColors)
                root.Resources[key] = new SolidColorBrush(FromArgb(isLight ? light : dark));
            root.Resources["AccentBrush"] = new SolidColorBrush(strongAccent);
            root.Resources["OnAccentBrush"] = new SolidColorBrush(ColorContrast(strongAccent));
            Color start = isLight ? Color.FromRgb(248, 241, 243) : Color.FromRgb(37, 22, 21);
            Color end = isLight ? Color.FromRgb(253, 249, 249) : Color.FromRgb(15, 10, 14);
            if (selectedProfile == 1) start = Mix(start, isLight ? Colors.White : Color.FromRgb(24, 28, 36), .58);
            if (selectedProfile == 2) start = end = isLight ? Color.FromRgb(247, 247, 249) : Color.FromRgb(24, 24, 28);
            root.Background = new LinearGradientBrush(Mix(start, accent, selectedProfile == 0 ? .07 : .025), end, 47);
            byte alpha = (byte)(isLight ? 235 - 85 * previewTransparency : 51 - 25 * previewTransparency);
            root.Resources["WelcomeSurface"] = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
        }

        static Color FromArgb(uint value) =>
            Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private void RestartForLanguage()
    {
        try
        {
            SaveAudioSettings();
            if (!smokeTest) SaveSession();

            var arguments = Environment.GetCommandLineArgs();
            string processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("No se pudo determinar el ejecutable de Prisma Music.");
            var start = new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };
            if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(arguments[0]);
            foreach (string argument in arguments.Skip(1)) start.ArgumentList.Add(argument);
            using Process restarted = Process.Start(start)
                ?? throw new InvalidOperationException("No se pudo reiniciar Prisma Music.");
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Status.Text = LocalTextExtension.Translate("No se pudo reiniciar para cambiar el idioma. ") + error.Message;
        }
    }
}
