using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private const int VisualizerBarCount = 48;
    private Window visualizerWindow;
    private Border[] visualizerBars = Array.Empty<Border>();
    private Border[] visualizerReflections = Array.Empty<Border>();
    private UniformGrid visualizerBarsPanel;
    private UniformGrid visualizerReflectionPanel;
    private Image visualizerArtwork;
    private Border visualizerTint;
    private TextBlock visualizerTitle;
    private TextBlock visualizerArtist;
    private Button visualizerPlayButton;
    private Button visualizerOptionsButton;
    private ContextMenu visualizerOptionsMenu;
    private DispatcherTimer visualizerTimer;
    private Color visualizerAccent;

    private void ShowVisualizer(object sender, RoutedEventArgs args)
    {
        if (visualizerWindow != null)
        {
            visualizerWindow.Activate();
            return;
        }

        var accent = System.Windows.Application.Current.Resources["AccentBrush"] as SolidColorBrush;
        Color color = accent?.Color ?? Color.FromRgb(96, 205, 255);
        visualizerAccent = color;
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(7, 9, 15)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        visualizerArtwork = new Image
        {
            Source = Cover.Source,
            Stretch = Stretch.UniformToFill,
            Opacity = .17,
            Effect = new BlurEffect { Radius = 72, KernelType = KernelType.Gaussian },
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(visualizerArtwork, 3);
        root.Children.Add(visualizerArtwork);
        visualizerTint = new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush(
                Color.FromArgb(0x55, color.R, color.G, color.B),
                Color.FromArgb(0, color.R, color.G, color.B))
        };
        Grid.SetRowSpan(visualizerTint, 3);
        root.Children.Add(visualizerTint);
        var shade = new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x60, 3, 5, 12),
                Color.FromArgb(0xD8, 3, 5, 12),
                90)
        };
        Grid.SetRowSpan(shade, 3);
        root.Children.Add(shade);

        var header = new DockPanel { Margin = new Thickness(38, 28, 38, 12) };
        var close = new Button
        {
            Content = "×",
            FontSize = 23,
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = LocalTextExtension.Translate("Salir del visualizador"),
        };
        System.Windows.Automation.AutomationProperties.SetName(close, LocalTextExtension.Translate("Salir del visualizador"));
        close.Click += (_, _) => visualizerWindow?.Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        visualizerOptionsButton = new Button
        {
            Content = "\uE713",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = LocalTextExtension.Translate("Opciones del visualizador")
        };
        System.Windows.Automation.AutomationProperties.SetName(visualizerOptionsButton,
            LocalTextExtension.Translate("Opciones del visualizador"));
        visualizerOptionsButton.Click += ShowVisualizerOptions;
        DockPanel.SetDock(visualizerOptionsButton, Dock.Right);
        header.Children.Add(visualizerOptionsButton);
        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock
        {
            Text = "PRISMA  /  " + LocalTextExtension.Translate("Visualizador").ToUpperInvariant(),
            Foreground = new SolidColorBrush(color),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        brand.Children.Add(new TextBlock
        {
            Text = LocalTextExtension.Translate("El ritmo de tu música"),
            Foreground = Brushes.White,
            FontSize = 19,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 5, 0, 0)
        });
        header.Children.Add(brand);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var stage = new Grid { Margin = new Thickness(50, 20, 50, 16) };
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        visualizerBarsPanel = new UniformGrid
        {
            Columns = VisualizerBarCount,
            Height = 260,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 16)
        };
        visualizerBars = new Border[VisualizerBarCount];
        visualizerReflections = new Border[VisualizerBarCount];
        var barBrush = new LinearGradientBrush(
            Color.FromRgb((byte)(color.R * .62 + 255 * .38), (byte)(color.G * .62 + 255 * .38),
                (byte)(color.B * .62 + 255 * .38)),
            color,
            90);
        for (int index = 0; index < VisualizerBarCount; index++)
        {
            visualizerBars[index] = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(5),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 0),
                Background = barBrush,
                Opacity = .93
            };
            visualizerBarsPanel.Children.Add(visualizerBars[index]);
        }
        stage.Children.Add(visualizerBarsPanel);

        visualizerReflectionPanel = new UniformGrid
        {
            Columns = VisualizerBarCount,
            Height = 115,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 30, 0, 0),
            Opacity = .11,
            RenderTransform = new ScaleTransform(1, -1),
            RenderTransformOrigin = new Point(.5, .5),
            IsHitTestVisible = false
        };
        for (int index = 0; index < VisualizerBarCount; index++)
        {
            visualizerReflections[index] = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(5),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 0),
                Background = barBrush
            };
            visualizerReflectionPanel.Children.Add(visualizerReflections[index]);
        }
        stage.Children.Add(visualizerReflectionPanel);
        ApplyVisualizerStyle();

        var artworkFrame = new Border
        {
            Width = 220,
            Height = 220,
            CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
            Opacity = .60,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };
        var cover = new Grid();
        cover.Children.Add(new ArtworkTile { Caption = Cover.Tag as string });
        var coverImage = new Image { Source = Cover.Source, Stretch = Stretch.UniformToFill };
        cover.Children.Add(coverImage);
        artworkFrame.Child = cover;
        stage.Children.Add(artworkFrame);
        visualizerArtwork.Tag = coverImage;

        var metadata = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        visualizerTitle = new TextBlock
        {
            Text = Now.Text,
            Foreground = Brushes.White,
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        visualizerArtist = new TextBlock
        {
            Text = Artist.Text,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 255, 255, 255)),
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };
        metadata.Children.Add(visualizerTitle);
        metadata.Children.Add(visualizerArtist);
        Grid.SetRow(metadata, 1);
        stage.Children.Add(metadata);
        Grid.SetRow(stage, 1);
        root.Children.Add(stage);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16, 8, 16, 36)
        };
        var previousButton = CreateVisualizerControl("⏮", LocalTextExtension.Translate("Anterior"), Previous);
        visualizerPlayButton = CreateVisualizerControl(PlayButton.Content?.ToString() ?? "▶",
            LocalTextExtension.Translate("Reproducir o pausar"), Toggle);
        visualizerPlayButton.Background = new SolidColorBrush(color);
        visualizerPlayButton.Foreground = Brushes.Black;
        visualizerPlayButton.Width = 58;
        visualizerPlayButton.Height = 54;
        var nextButton = CreateVisualizerControl("⏭", LocalTextExtension.Translate("Siguiente"), Next);
        controls.Children.Add(previousButton);
        controls.Children.Add(visualizerPlayButton);
        controls.Children.Add(nextButton);
        Grid.SetRow(controls, 2);
        root.Children.Add(controls);

        visualizerWindow = new Window
        {
            Owner = this,
            Title = LocalTextExtension.Translate("Visualizador · Prisma Music"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(7, 9, 15)),
            Content = root
        };
        visualizerWindow.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is Key.Escape or Key.F11)
            {
                key.Handled = true;
                visualizerWindow.Close();
            }
        };
        visualizerWindow.Closed += (_, _) =>
        {
            visualizerTimer?.Stop();
            visualizerTimer = null;
            visualizerWindow = null;
            visualizerBars = Array.Empty<Border>();
            visualizerReflections = Array.Empty<Border>();
            visualizerBarsPanel = null;
            visualizerReflectionPanel = null;
            visualizerArtwork = null;
            visualizerTint = null;
            visualizerTitle = null;
            visualizerArtist = null;
            visualizerPlayButton = null;
            visualizerOptionsButton = null;
            visualizerOptionsMenu = null;
            visualizerAccent = default;
        };
        visualizerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000d / visualizerFramesPerSecond)
        };
        visualizerTimer.Tick += (_, _) => UpdateVisualizer();
        UpdateVisualizer();
        visualizerTimer.Start();
        visualizerWindow.Show();
    }

    private Button CreateVisualizerControl(string content, string label, RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = content,
            Width = 48,
            Height = 48,
            Padding = new Thickness(0),
            Margin = new Thickness(7),
            FontSize = 16,
            ToolTip = label
        };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += action;
        return button;
    }

    private void ShowVisualizerOptions(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 36, 44)),
            Foreground = Brushes.White,
            Padding = new Thickness(6)
        };
        visualizerOptionsMenu = menu;
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate("Estilo"),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        });
        AddChoice("Barras", "bars", visualizerStyle, value =>
        {
            visualizerStyle = value;
            ApplyVisualizerStyle();
        });
        AddChoice("Espejo", "mirror", visualizerStyle, value =>
        {
            visualizerStyle = value;
            ApplyVisualizerStyle();
        });
        AddChoice("Centrado", "centered", visualizerStyle, value =>
        {
            visualizerStyle = value;
            ApplyVisualizerStyle();
        });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate("Sensibilidad"),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        });
        foreach (double sensitivity in new[] { .5, 1, 1.5, 2 })
        {
            double value = sensitivity;
            var item = new MenuItem
            {
                Header = LocalTextExtension.Format("Sensibilidad {0}×", value.ToString("0.#")),
                IsCheckable = true,
                IsChecked = Math.Abs(visualizerSensitivity - value) < .01,
                Tag = "sensitivity"
            };
            item.Click += (_, _) =>
            {
                SelectRadio(item, "sensitivity");
                visualizerSensitivity = value;
                SaveAudioSettings();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate("Frecuencia de actualización"),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        });
        foreach (int framesPerSecond in new[] { 24, 30, 60 })
        {
            int value = framesPerSecond;
            var item = new MenuItem
            {
                Header = $"{value} FPS",
                IsCheckable = true,
                IsChecked = visualizerFramesPerSecond == value,
                Tag = "fps"
            };
            item.Click += (_, _) =>
            {
                SelectRadio(item, "fps");
                visualizerFramesPerSecond = value;
                if (visualizerTimer != null)
                    visualizerTimer.Interval = TimeSpan.FromMilliseconds(1000d / value);
                SaveAudioSettings();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (FrameworkElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;

        void AddChoice(string label, string value, string selected, Action<string> change)
        {
            var item = new MenuItem
            {
                Header = LocalTextExtension.Translate(label),
                IsCheckable = true,
                IsChecked = selected == value,
                Tag = "style"
            };
            item.Click += (_, _) =>
            {
                SelectRadio(item, "style");
                change(value);
                SaveAudioSettings();
            };
            menu.Items.Add(item);
        }

        void SelectRadio(MenuItem selected, string group)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (Equals(item.Tag, group)) item.IsChecked = ReferenceEquals(item, selected);
        }
    }

    private void ApplyVisualizerStyle()
    {
        if (visualizerBarsPanel == null) return;
        visualizerReflectionPanel.Visibility = visualizerStyle == "mirror" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var bar in visualizerBars)
            bar.VerticalAlignment = visualizerStyle == "centered" ? VerticalAlignment.Center : VerticalAlignment.Bottom;
    }

    private void UpdateVisualizer()
    {
        if (visualizerWindow == null) return;
        var accent = System.Windows.Application.Current.Resources["AccentBrush"] as SolidColorBrush;
        if (accent != null && accent.Color != visualizerAccent)
        {
            visualizerAccent = accent.Color;
            var glow = new RadialGradientBrush(
                Color.FromArgb(0x55, visualizerAccent.R, visualizerAccent.G, visualizerAccent.B),
                Color.FromArgb(0, visualizerAccent.R, visualizerAccent.G, visualizerAccent.B));
            visualizerTint.Background = glow;
            var bars = new LinearGradientBrush(
                Color.FromRgb((byte)(visualizerAccent.R * .62 + 255 * .38),
                    (byte)(visualizerAccent.G * .62 + 255 * .38),
                    (byte)(visualizerAccent.B * .62 + 255 * .38)),
                visualizerAccent,
                90);
            foreach (var bar in visualizerBars) bar.Background = bars;
            foreach (var bar in visualizerReflections) bar.Background = bars;
            if (visualizerPlayButton != null) visualizerPlayButton.Background = new SolidColorBrush(visualizerAccent);
        }
        if (visualizerTitle != null) visualizerTitle.Text = Now.Text;
        if (visualizerArtist != null) visualizerArtist.Text = Artist.Text;
        if (visualizerPlayButton != null) visualizerPlayButton.Content = PlayButton.Content;
        if (visualizerArtwork != null)
        {
            visualizerArtwork.Source = Cover.Source;
            if (visualizerArtwork.Tag is Image centerCover) centerCover.Source = Cover.Source;
        }

        var spectrum = player.Spectrum;
        double height = Math.Max(1, visualizerWindow.ActualHeight * .28);
        for (int index = 0; index < visualizerBars.Length; index++)
        {
            double value = 0;
            if (spectrum.Length > 0)
            {
                double position = index * (spectrum.Length - 1d) / Math.Max(1, visualizerBars.Length - 1);
                int lower = (int)position;
                int upper = Math.Min(spectrum.Length - 1, lower + 1);
                value = spectrum[lower] + (spectrum[upper] - spectrum[lower]) * (position - lower);
            }
            double scaledValue = Math.Clamp(value * visualizerSensitivity, 0, 1);
            double barHeight = playing ? 5 + height * scaledValue : 3;
            if (visualizerStyle == "centered") barHeight = playing ? 5 + height * .38 * scaledValue : 3;
            visualizerBars[index].Height = barHeight;
            visualizerReflections[index].Height = Math.Max(2, barHeight * .72);
        }
    }
}
