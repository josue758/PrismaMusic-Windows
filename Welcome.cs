using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
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

    private async void ShowWelcome(object sender, RoutedEventArgs args)
    {
        string T(string text) => LocalTextExtension.Translate(text);
        int chosenAppearance = appearanceProfile;
        var codes = new[] { "es", "en", "pt" };
        var language = new ComboBox
        {
            ItemsSource = new[] { "Español", "English", "Português" },
            SelectedIndex = Array.IndexOf(codes, LocalTextExtension.Language),
            Style = (Style)FindResource("FilterCombo")
        };
        var accentChoices = new ComboBox
        {
            ItemsSource = new[] { T("Según la carátula"), T("Azul frío"), T("Violeta") },
            SelectedIndex = accentPreset switch { "blue" => 1, "violet" => 2, _ => 0 },
            Style = (Style)FindResource("FilterCombo")
        };
        var animatedSpectrum = new CheckBox
        {
            Style = (Style)FindResource("SettingsToggle"),
            Content = T("Mostrar espectro animado"),
            IsChecked = animateSpectrum,
            Foreground = Brushes.White
        };
        var visualizerStylePicker = new ComboBox
        {
            ItemsSource = new[] { T("Barras"), T("Espejo"), T("Centrado") },
            SelectedIndex = visualizerStyle switch { "bars" => 0, "centered" => 2, _ => 1 },
            Style = (Style)FindResource("FilterCombo")
        };
        var frameRatePicker = new ComboBox
        {
            ItemsSource = new[] { "24 FPS", "30 FPS", "60 FPS" },
            SelectedIndex = visualizerFramesPerSecond switch { 24 => 0, 60 => 2, _ => 1 },
            Style = (Style)FindResource("FilterCombo")
        };
        var sensitivityPicker = new Slider
        {
            Style = (Style)FindResource("TransportSlider"),
            Minimum = .5,
            Maximum = 2,
            Value = visualizerSensitivity,
            TickFrequency = .25,
            IsSnapToTickEnabled = true
        };
        var sensitivityValue = new TextBlock { Foreground = Brushes.Gainsboro };
        sensitivityPicker.ValueChanged += (_, change) => sensitivityValue.Text = $"{change.NewValue:0.00}×";
        sensitivityValue.Text = $"{sensitivityPicker.Value:0.00}×";
        string selectedFolder = null;
        var librarySelection = new TextBlock
        {
            Text = T("Aún no has elegido una biblioteca."),
            Foreground = Brushes.Gainsboro,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var pages = new StackPanel[4];
        pages[0] = new StackPanel();
        pages[0].Children.Add(language);
        pages[0].Children.Add(new TextBlock
        {
            Text = T("Puedes cambiar el idioma más tarde en Configuración. El cambio se aplica al reiniciar."),
            Foreground = Brushes.Gainsboro,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });

        pages[1] = new StackPanel();
        pages[1].Children.Add(new TextBlock
        {
            Text = T("Elige una carpeta local o una carpeta compartida SMB. Para un NAS, conéctalo desde el Explorador de archivos y luego selecciónalo aquí."),
            Foreground = Brushes.Gainsboro,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var chooseFolder = new Button { Content = T("Elegir carpeta o NAS"), Margin = new Thickness(0, 0, 0, 8) };
        chooseFolder.Click += (_, _) =>
        {
            using var picker = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = T("Elige una carpeta local o una carpeta compartida SMB"),
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true
            };
            if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            selectedFolder = picker.SelectedPath;
            librarySelection.Text = LocalTextExtension.Format("Carpeta seleccionada: {0}", selectedFolder);
        };
        var libraryActions = new WrapPanel();
        libraryActions.Children.Add(chooseFolder);
        var chooseDemos = new Button { Content = T("Probar demos"), Margin = new Thickness(0, 0, 0, 6) };
        chooseDemos.Click += (_, _) =>
        {
            selectedFolder = Path.Combine(AppContext.BaseDirectory, "Demo");
            librarySelection.Text = T("Se añadirán las pistas de demostración.");
        };
        libraryActions.Children.Add(chooseDemos);
        pages[1].Children.Add(libraryActions);
        pages[1].Children.Add(new Border
        {
            Background = (Brush)FindResource("SettingsBackground"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), Child = librarySelection
        });
        librarySelection.Margin = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(librarySelection,
            System.Windows.Automation.AutomationLiveSetting.Polite);

        pages[2] = new StackPanel();
        pages[2].Children.Add(CreateAppearanceProfiles(chosenAppearance, profile => chosenAppearance = profile));
        pages[2].Children.Add(new TextBlock { Text = T("Color de la aplicación"), FontWeight = FontWeights.SemiBold });
        pages[2].Children.Add(accentChoices);
        pages[2].Children.Add(animatedSpectrum);
        pages[2].Children.Add(new TextBlock
        {
            Text = T("Estilo del visualizador"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        pages[2].Children.Add(visualizerStylePicker);
        pages[2].Children.Add(new TextBlock
        {
            Text = T("Frecuencia de actualización"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        pages[2].Children.Add(frameRatePicker);
        pages[2].Children.Add(new TextBlock
        {
            Text = T("Sensibilidad"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        pages[2].Children.Add(sensitivityPicker);
        pages[2].Children.Add(sensitivityValue);

        pages[3] = new StackPanel();
        pages[3].Children.Add(new TextBlock
        {
            Text = T("Todo listo"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        pages[3].Children.Add(new TextBlock
        {
            Text = T("Tu biblioteca queda en este equipo. Las letras se pueden traducir localmente: Prisma prepara el runtime y el modelo al solicitar la traducción, sin que tengas que instalar Python."),
            Foreground = Brushes.Gainsboro,
            TextWrapping = TextWrapping.Wrap
        });
        var currentStep = 0;
        bool restartForLanguage = false;
        bool completed = false;
        var pageHost = new ContentControl();
        var stepLabel = new TextBlock { Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11, Margin = new Thickness(0, 7, 0, 0) };
        var progress = new ProgressBar { Minimum = 0, Maximum = 4, Height = 3, Value = 1,
            Background = (Brush)FindResource("SettingsStroke"), BorderThickness = new Thickness(0) };
        progress.SetResourceReference(ForegroundProperty, "AccentBrush");
        var back = new Button { Content = T("Atrás"), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var skip = new Button { Content = T("Ahora no"), Margin = new Thickness(0, 0, 8, 0) };
        var forward = new Button { Content = T("Continuar"), MinWidth = 112, IsDefault = true };
        forward.SetResourceReference(BackgroundProperty, "AccentBrush");
        forward.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 18, 0, 0) };
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
        leftButtons.Children.Add(back);
        leftButtons.Children.Add(skip);
        DockPanel.SetDock(leftButtons, Dock.Left);
        footer.Children.Add(leftButtons);
        DockPanel.SetDock(forward, Dock.Right);
        footer.Children.Add(forward);
        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rail = new Border
        {
            Background = (Brush)FindResource("SettingsCard"),
            BorderBrush = (Brush)FindResource("SettingsStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(17, 18, 14, 14),
            Margin = new Thickness(0, 0, 14, 0)
        };
        var railLayout = new DockPanel();
        var brand = new StackPanel { Margin = new Thickness(2, 0, 0, 25) };
        brand.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x34, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "\uE8D6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 21,
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        brand.Children.Add(new TextBlock { Text = "Prisma Music", FontSize = 21,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        DockPanel.SetDock(brand, Dock.Top);
        railLayout.Children.Add(brand);
        var navigation = new StackPanel();
        var stepButtons = new Button[pages.Length];
        var stepIconTiles = new Border[pages.Length];
        var stageTitle = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold };
        var stageDescription = new TextBlock
        {
            Foreground = Brushes.Gainsboro,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
        string[] stepNames = { T("Idioma"), T("Biblioteca"), T("Apariencia"), T("Listo") };
        string[] stepIcons = { "\uE774", "\uE8B7", "\uE790", "\uE73E" };
        for (int index = 0; index < stepButtons.Length; index++)
        {
            var label = new TextBlock { Text = stepNames[index], VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold };
            var icon = new TextBlock { Text = stepIcons[index], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stepIconTiles[index] = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = icon
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
            text.Children.Add(label);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(stepIconTiles[index]);
            content.Children.Add(text);
            stepButtons[index] = new Button
            {
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(9, 10, 7, 10),
                Margin = new Thickness(0, 2, 0, 3),
                Tag = index
            };
            int step = index;
            System.Windows.Automation.AutomationProperties.SetName(stepButtons[index], stepNames[index]);
            stepButtons[index].Click += (_, _) => SetStep(step);
            navigation.Children.Add(stepButtons[index]);
        }
        DockPanel.SetDock(navigation, Dock.Top);
        railLayout.Children.Add(navigation);
        var railFooter = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        railFooter.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            Margin = new Thickness(0, 10, 0, 12) });
        railFooter.Children.Add(progress);
        railFooter.Children.Add(stepLabel);
        DockPanel.SetDock(railFooter, Dock.Bottom);
        railLayout.Children.Add(railFooter);
        rail.Child = railLayout;
        root.Children.Add(rail);

        var contentLayout = new DockPanel { Margin = new Thickness(3, 5, 6, 5) };
        var header = new Grid { Margin = new Thickness(4, 0, 0, 17) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleBlock.Children.Add(stageTitle);
        titleBlock.Children.Add(stageDescription);
        header.Children.Add(titleBlock);
        Window dialog = null;
        var close = new Button { Content = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Width = 34, Height = 34, Padding = new Thickness(0), ToolTip = T("Cerrar"),
            VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => dialog.Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        System.Windows.Automation.AutomationProperties.SetName(close, T("Cerrar"));
        Grid.SetColumn(close, 1);
        close.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        contentLayout.Children.Add(header);
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Margin = new Thickness(0, 15, 0, 2);
        contentLayout.Children.Add(footer);
        var pageCard = new Border
        {
            Background = (Brush)FindResource("SettingsCard"),
            BorderBrush = (Brush)FindResource("SettingsStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(26, 24, 26, 22),
            Child = new ScrollViewer { Content = pageHost, Style = (Style)FindResource("LyricsScroll"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        contentLayout.Children.Add(pageCard);
        Grid.SetColumn(contentLayout, 1);
        root.Children.Add(contentLayout);
        dialog = new Window
        {
            Owner = this,
            Title = T("Primeros pasos"),
            Width = 900,
            Height = 620,
            MinWidth = 790,
            MinHeight = 550,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            Background = (Brush)FindResource("SettingsBackground"),
            Content = root
        };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_WELCOME_PREVIEW") is string welcomePreview)
        {
            dialog.ContentRendered += (_, _) =>
            {
                int previewStep = 0;
                void CaptureStep()
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth,
                        (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(dialog);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    string path = previewStep == 0 ? welcomePreview :
                        Path.Combine(Path.GetDirectoryName(welcomePreview)!,
                            Path.GetFileNameWithoutExtension(welcomePreview) + $"-step-{previewStep + 1}.png");
                    using (var stream = File.Create(path)) png.Save(stream);
                    if (++previewStep == pages.Length)
                    {
                        dialog.Close();
                        return;
                    }
                    SetStep(previewStep);
                    dialog.Dispatcher.BeginInvoke(CaptureStep, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                CaptureStep();
            };
        }
        void SetStep(int step)
        {
            currentStep = Math.Clamp(step, 0, pages.Length - 1);
            pageHost.Content = pages[currentStep];
            stepLabel.Text = LocalTextExtension.Format("Paso {0} de 4", currentStep + 1);
            progress.Value = currentStep + 1;
            back.IsEnabled = currentStep > 0;
            forward.Content = T(currentStep == pages.Length - 1 ? "Comenzar" : "Continuar");
            string[] titles = { T("Elige tu idioma"), T("Añade tu biblioteca"), T("Personaliza Prisma"), T("Listo para escuchar") };
            string[] descriptions =
            {
                T("Los menús estarán en el idioma que prefieras."),
                T("Elige una carpeta local, de red o prueba las demos."),
                T("Ajusta el color y el visualizador a tu gusto."),
                T("Revisa tus opciones antes de entrar a tu biblioteca.")
            };
            stageTitle.Text = titles[currentStep];
            stageDescription.Text = descriptions[currentStep];
            for (int index = 0; index < stepButtons.Length; index++)
            {
                bool selected = index == currentStep;
                stepButtons[index].Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) : Brushes.Transparent;
                stepButtons[index].Foreground = Brushes.White;
                stepIconTiles[index].Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x32, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255));
                stepIconTiles[index].BorderBrush = selected
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255));
                System.Windows.Automation.AutomationProperties.SetItemStatus(stepButtons[index],
                    selected ? T("Paso actual") : "");
            }
            if (currentStep == pages.Length - 1)
            {
                string librarySummary = selectedFolder == null ? T("Sin carpeta seleccionada") : selectedFolder;
                pages[3].Children.OfType<TextBlock>().Last().Text =
                    T("Tu biblioteca queda en este equipo. Las letras se pueden traducir localmente: Prisma prepara el runtime y el modelo al solicitar la traducción, sin que tengas que instalar Python.") +
                    Environment.NewLine + Environment.NewLine +
                    LocalTextExtension.Format("Carpeta elegida: {0}", librarySummary) + Environment.NewLine +
                    LocalTextExtension.Format("Apariencia elegida: {0}", T(AppearanceProfileNames[chosenAppearance]));
            }
        }
        back.Click += (_, _) => SetStep(currentStep - 1);
        skip.Click += (_, _) =>
        {
            selectedFolder = null;
            completed = true;
            dialog.Close();
        };
        dialog.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != System.Windows.Input.Key.Escape) return;
            key.Handled = true;
            dialog.Close();
        };
        forward.Click += (_, _) =>
        {
            if (currentStep < pages.Length - 1)
            {
                SetStep(currentStep + 1);
                return;
            }
            if (language.SelectedIndex < 0) return;
            restartForLanguage = codes[language.SelectedIndex] != LocalTextExtension.Language;
            if (restartForLanguage)
            {
                try { LocalTextExtension.SaveLanguage(codes[language.SelectedIndex]); }
                catch (Exception error)
                {
                    Status.Text = LocalTextExtension.Translate("No se pudo guardar el idioma. ") + error.Message;
                    restartForLanguage = false;
                    return;
                }
            }
            completed = true;
            dialog.Close();
        };
        SetStep(0);
        dialog.ShowDialog();
        if (smokeTest) return;
        if (!completed) return;
        ApplyAppearanceProfile(chosenAppearance);
        if (accentChoices.SelectedIndex >= 0)
            ApplyAccentPreset(new[] { "artwork", "blue", "violet" }[accentChoices.SelectedIndex]);
        animateSpectrum = animatedSpectrum.IsChecked == true;
        SpectrumView.Visibility = animateSpectrum ? Visibility.Visible : Visibility.Collapsed;
        visualizerStyle = visualizerStylePicker.SelectedIndex switch { 0 => "bars", 2 => "centered", _ => "mirror" };
        visualizerFramesPerSecond = frameRatePicker.SelectedIndex switch { 0 => 24, 2 => 60, _ => 30 };
        visualizerSensitivity = sensitivityPicker.Value;
        SaveAudioSettings();
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(WelcomeFile, "1");
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
        if (!string.IsNullOrEmpty(selectedFolder))
            await Scan(new[] { selectedFolder }, true);
        if (restartForLanguage) RestartForLanguage();
    }

    private void ApplyAccentPreset(string preset)
    {
        accentPreset = preset is "blue" or "violet" ? preset : "artwork";
        artworkColors = accentPreset == "artwork";
        if (current != null) LoadPresentation(current);
        else
        {
            Color accent = AccentPresetColor();
            SetArtworkAccent(accent);
            Shell.Background = ArtworkShellBrush(accent);
        }
        SaveAudioSettings();
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
