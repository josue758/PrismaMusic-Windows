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
    private static readonly (string Value, string Label, string Description)[] VisualizerStyles =
    {
        ("nebula", "Nebulosa", "Partículas y halos"),
        ("orbit", "Órbita", "Espectro circular"),
        ("bars", "Barras", "Espectro vertical"),
        ("mirror", "Espejo", "Reflejo del espectro"),
        ("centered", "Centrado", "Barras sobre el eje"),
        ("aurora", "Aurora", "Ondas superpuestas")
    };
    private static readonly int[] VisualizerRates = { 24, 30, 60 };
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
    private Grid visualizerOptionsLayer;
    private Border visualizerOptionsPanel;
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
        visualizerSpectrum = new SpectrumStage { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetRowSpan(visualizerSpectrum, 3);
        root.Children.Add(visualizerSpectrum);

        var header = new DockPanel { Margin = new Thickness(40, 26, 40, 10) };
        visualizerOptionsButton = SettingsButton("Ajustes", ShowVisualizerOptions);
        visualizerOptionsButton.ToolTip = T("Opciones del visualizador");
        System.Windows.Automation.AutomationProperties.SetName(visualizerOptionsButton, T("Opciones del visualizador"));
        visualizerOptionsButton.MinWidth = 104;
        visualizerOptionsButton.VerticalAlignment = VerticalAlignment.Center;
        var optionsLabel = new StackPanel { Orientation = Orientation.Horizontal };
        optionsLabel.Children.Add(new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        optionsLabel.Children.Add(new TextBlock { Text = T("Ajustes"), VerticalAlignment = VerticalAlignment.Center });
        visualizerOptionsButton.Content = optionsLabel;
        visualizerOptionsButton.Foreground = Brushes.White;
        visualizerOptionsButton.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
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
        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(title);
        visualizerStyleLabel = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 3, 0, 0), Foreground = new SolidColorBrush(Color.FromArgb(198, 255, 255, 255)) };
        heading.Children.Add(visualizerStyleLabel);
        header.Children.Add(heading);
        root.Children.Add(header);
        var trackCard = new DockPanel { Margin = new Thickness(48, 0, 48, 21),
            VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 550, HorizontalAlignment = HorizontalAlignment.Left };

        var artworkFrame = new Border
        {
            Width = 60,
            Height = 60,
            CornerRadius = new CornerRadius(8),
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
        DockPanel.SetDock(artworkFrame, Dock.Left);
        trackCard.Children.Add(artworkFrame);
        cover.SizeChanged += (_, _) =>
            cover.Clip = new RectangleGeometry(new Rect(0, 0, cover.ActualWidth, cover.ActualHeight), 8, 8);
        visualizerArtwork.Tag = coverImage;

        var metadata = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        visualizerTitle = new TextBlock
        {
            Text = Now.Text,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        visualizerArtist = new TextBlock
        {
            Text = Artist.Text,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 255, 255, 255)),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0)
        };
        metadata.Children.Add(visualizerTitle);
        metadata.Children.Add(visualizerArtist);
        trackCard.Children.Add(metadata);
        Grid.SetRow(trackCard, 1);
        root.Children.Add(trackCard);

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
            Margin = new Thickness(48, 0, 48, 24), MaxWidth = 850, CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(122, 16, 19, 29)),
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
            if (key.Key is not (Key.Escape or Key.F12)) return;
            key.Handled = true;
            if (key.Key == Key.Escape && visualizerOptionsLayer?.IsVisible == true) CloseVisualizerOptions();
            else visualizerWindow.Close();
        };
        visualizerWindow.Closed += (_, _) =>
        {
            visualizerTimer?.Stop();
            FlushVisualizerSave();
            visualizerSaveTimer = null;
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
            visualizerOptionsLayer = null;
            visualizerOptionsPanel = null;
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
            visualizerFramesPerSecond, visualizerStyle, visualizerAccent, MotionEnabled);
    }
}
