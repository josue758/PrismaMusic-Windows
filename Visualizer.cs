using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private static readonly (string Value, string Label)[] VisualizerStyles =
        { ("bars", "Barras"), ("mirror", "Espejo"), ("centered", "Centrado"), ("aurora", "Aurora") };
    private Window visualizerWindow;
    private SpectrumStage visualizerSpectrum;
    private TextBlock visualizerStyleLabel;
    private Image visualizerArtwork;
    private Border visualizerTint;
    private TextBlock visualizerTitle;
    private TextBlock visualizerArtist;
    private Button visualizerPlayButton;
    private Slider visualizerSeek;
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

        var header = new DockPanel { Margin = new Thickness(40, 26, 40, 10) };
        visualizerOptionsButton = CreateVisualizerControl("\uE713", T("Opciones del visualizador"), ShowVisualizerOptions);
        DockPanel.SetDock(visualizerOptionsButton, Dock.Right);
        header.Children.Add(visualizerOptionsButton);
        var back = SettingsButton("Tu biblioteca", (_, _) => visualizerWindow?.Close());
        back.ToolTip = T("Salir del visualizador") + " · Esc";
        back.Margin = new Thickness(0, 0, 20, 0);
        back.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(back, Dock.Left);
        header.Children.Add(back);
        var title = new TextBlock
        {
            Text = T("Visualizador"),
            Foreground = Brushes.White,
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(title);
        root.Children.Add(header);
        var stage = new Grid { Margin = new Thickness(48, 24, 48, 22), MaxWidth = 1500 };
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        var trackCard = new StackPanel { Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center };
        stage.Children.Add(trackCard);

        var display = new Grid { ClipToBounds = true, Margin = new Thickness(22, 0, 22, 20) };
        display.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        display.RowDefinitions.Add(new RowDefinition());
        visualizerStyleLabel = new TextBlock
        {
            FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            Margin = new Thickness(4, 20, 4, 8)
        };
        display.Children.Add(visualizerStyleLabel);
        visualizerSpectrum = new SpectrumStage { IsHitTestVisible = false };
        Grid.SetRow(visualizerSpectrum, 1);
        display.Children.Add(visualizerSpectrum);
        var displayCard = new Border
        {
            CornerRadius = new CornerRadius(16), Child = display,
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(148, 16, 19, 29))
        };
        Grid.SetColumn(displayCard, 1);
        stage.Children.Add(displayCard);
        var artworkFrame = new Border
        {
            Width = 240,
            Height = 240,
            CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };
        var cover = new Grid();
        var placeholder = new ArtworkTile();
        placeholder.SetBinding(ArtworkTile.CaptionProperty, new System.Windows.Data.Binding(nameof(Tag)) { Source = Cover });
        cover.Children.Add(placeholder);
        var coverImage = new Image { Source = Cover.Source, Stretch = Stretch.UniformToFill };
        cover.Children.Add(coverImage);
        artworkFrame.Child = cover;
        trackCard.Children.Add(artworkFrame);
        stage.SizeChanged += (_, _) =>
            artworkFrame.Width = artworkFrame.Height = Math.Max(1, Math.Min(280, Math.Min(stage.ActualWidth / 3 - 28, stage.ActualHeight * .63)));
        cover.SizeChanged += (_, _) =>
            cover.Clip = new RectangleGeometry(new Rect(0, 0, cover.ActualWidth, cover.ActualHeight), 17, 17);
        visualizerArtwork.Tag = coverImage;

        var metadata = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        visualizerTitle = new TextBlock
        {
            Text = Now.Text,
            Foreground = Brushes.White,
            FontSize = 23,
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
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0)
        };
        metadata.Children.Add(visualizerTitle);
        metadata.Children.Add(visualizerArtist);
        trackCard.Children.Add(metadata);
        Grid.SetRow(stage, 1);
        root.Children.Add(stage);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 4)
        };
        var previousButton = CreateVisualizerControl("\uE892", LocalTextExtension.Translate("Anterior"), Previous);
        visualizerPlayButton = CreateVisualizerControl(PlayButton.Content?.ToString() ?? "\uE768",
            LocalTextExtension.Translate("Reproducir o pausar"), Toggle);
        visualizerPlayButton.Background = new SolidColorBrush(color);
        visualizerPlayButton.SetResourceReference(Control.ForegroundProperty, "OnAccentBrush");
        visualizerPlayButton.Width = 58;
        visualizerPlayButton.Height = 54;
        var nextButton = CreateVisualizerControl("\uE893", LocalTextExtension.Translate("Siguiente"), Next);
        controls.Children.Add(previousButton);
        controls.Children.Add(visualizerPlayButton);
        controls.Children.Add(nextButton);
        var playback = new StackPanel { Margin = new Thickness(28, 0, 28, 13) };
        playback.Children.Add(controls);
        visualizerSeek = new Slider
        {
            Style = (Style)FindResource("TransportSlider"), IsMoveToPointEnabled = true,
            Margin = new Thickness(9, 0, 9, 0)
        };
        visualizerSeek.SetBinding(Slider.MaximumProperty, new System.Windows.Data.Binding(nameof(Slider.Maximum)) { Source = Seek });
        visualizerSeek.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(Slider.Value))
            { Source = Seek, Mode = System.Windows.Data.BindingMode.TwoWay });
        System.Windows.Automation.AutomationProperties.SetName(visualizerSeek, T("Posición"));
        var timeline = new Grid { MaxWidth = 880, Margin = new Thickness(0, 3, 0, 3) };
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition());
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        var muted = new SolidColorBrush(Color.FromRgb(194, 198, 210));
        var elapsed = MirrorText(TimeLabel, 11, muted);
        elapsed.TextAlignment = TextAlignment.Right;
        elapsed.VerticalAlignment = VerticalAlignment.Center;
        var remaining = MirrorText(RemainingTimeLabel, 11, muted);
        remaining.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(visualizerSeek, 1);
        Grid.SetColumn(remaining, 2);
        timeline.Children.Add(elapsed);
        timeline.Children.Add(visualizerSeek);
        timeline.Children.Add(remaining);
        playback.Children.Add(timeline);
        var transport = new Border
        {
            Margin = new Thickness(48, 0, 48, 24), MaxWidth = 1500, CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(162, 16, 19, 29)),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            Child = playback
        };
        Grid.SetRow(transport, 2);
        root.Children.Add(transport);

        visualizerWindow = new Window
        {
            Owner = this,
            Title = LocalTextExtension.Translate("Visualizador · Prisma Music"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(7, 9, 15)),
            Content = root
        };
        visualizerWindow.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is Key.Escape or Key.F12)
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
            visualizerSpectrum = null;
            visualizerStyleLabel = null;
            visualizerArtwork = null;
            visualizerTint = null;
            visualizerTitle = null;
            visualizerArtist = null;
            visualizerPlayButton = null;
            visualizerSeek = null;
            visualizerOptionsButton = null;
            visualizerOptionsMenu = null;
            visualizerAccent = default;
        };
        visualizerTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000d / visualizerFramesPerSecond)
        };
        visualizerTimer.Tick += (_, _) => UpdateVisualizer();
        UpdateVisualizer();
        visualizerWindow.IsVisibleChanged += (_, _) =>
        {
            if (visualizerWindow?.IsVisible == true) visualizerTimer.Start();
            else visualizerTimer?.Stop();
        };
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
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
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
        foreach (var style in VisualizerStyles)
            AddChoice(style.Label, style.Value, visualizerStyle, value =>
            {
                visualizerStyle = value;
                UpdateVisualizer();
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

        if (!Equals(visualizerStyleLabel.Tag, visualizerStyle))
        {
            visualizerStyleLabel.Tag = visualizerStyle;
            visualizerStyleLabel.Text = T(VisualizerStyles.First(style => style.Value == visualizerStyle).Label);
        }
        visualizerSpectrum.Update(player.Spectrum, playing, visualizerSensitivity,
            visualizerFramesPerSecond, visualizerStyle, visualizerAccent, interfaceAnimations && SystemParameters.ClientAreaAnimation);
    }
}
