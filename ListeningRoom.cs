using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PrismaMusic;

public partial class MainWindow
{
    private Window listeningRoom;

    private void Fullscreen(object sender, RoutedEventArgs args)
    {
        if (listeningRoom != null)
        {
            listeningRoom.Close();
            return;
        }

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Background = new SolidColorBrush(Color.FromRgb(10, 12, 19));

        var backdrop = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = .14,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = 92,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian
            }
        };
        backdrop.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source)) { Source = Cover });
        Grid.SetRowSpan(backdrop, 3);
        layout.Children.Add(backdrop);
        var backdropTint = new Border
        {
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 8, 10, 17))
        };
        Grid.SetRowSpan(backdropTint, 3);
        layout.Children.Add(backdropTint);

        var header = new Grid { Margin = new Thickness(46, 26, 46, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x32, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "\uE8D6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 19,
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var brandText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "PRISMA MUSIC",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        brandText.Children.Add(new TextBlock
        {
            Text = LocalTextExtension.Translate("Tu espacio de escucha"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 255, 255, 255)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        brand.Children.Add(brandText);
        header.Children.Add(brand);

        var exitContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        exitContent.Children.Add(new TextBlock
        {
            Text = "\uE72B",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        exitContent.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Volver al reproductor") });
        var exit = new Button
        {
            Content = exitContent,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(15, 10, 17, 10),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x32, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            ToolTip = LocalTextExtension.Translate("Cerrar pantalla completa · Esc")
        };
        exit.Click += (_, _) => listeningRoom?.Close();
        Grid.SetColumn(exit, 1);
        header.Children.Add(exit);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var stage = new Grid { Margin = new Thickness(72, 10, 72, 18) };
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.04, GridUnitType.Star) });
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.96, GridUnitType.Star) });

        var artGlow = new Border
        {
            Width = 650,
            Height = 650,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                GradientOrigin = new Point(.5, .5),
                Center = new Point(.5, .5),
                RadiusX = .5,
                RadiusY = .5,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x54, 140, 114, 220), 0),
                    new GradientStop(Color.FromArgb(0x20, 84, 69, 150), .55),
                    new GradientStop(Colors.Transparent, 1)
                }
            },
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 74 }
        };
        stage.Children.Add(artGlow);

        var art = new Grid { ClipToBounds = true };
        var fallback = new ArtworkTile();
        fallback.SetBinding(ArtworkTile.CaptionProperty, new Binding(nameof(FrameworkElement.Tag)) { Source = Cover });
        art.Children.Add(fallback);
        var cover = new Image { Stretch = Stretch.Uniform };
        cover.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source)) { Source = Cover });
        art.Children.Add(cover);
        art.Children.Add(new Border
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 102,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 8, 10, 17),
                Color.FromArgb(0x38, 8, 10, 17),
                90)
        });
        var frame = new Border
        {
            Child = art,
            CornerRadius = new CornerRadius(28),
            MaxWidth = 650,
            MaxHeight = 650,
            Margin = new Thickness(0, 0, 38, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 46,
                ShadowDepth = 16,
                Opacity = .48
            }
        };
        stage.Children.Add(frame);
        stage.SizeChanged += (_, _) =>
        {
            double size = Math.Clamp(Math.Min(stage.ActualHeight - 36, stage.ActualWidth * .46), 220, 650);
            frame.Width = size;
            frame.Height = size;
            artGlow.Width = size * 1.3;
            artGlow.Height = size * 1.3;
        };

        var details = new DockPanel { Margin = new Thickness(26, 14, 0, 14), LastChildFill = true };
        Grid.SetColumn(details, 1);
        var metadata = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var nowPlaying = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        nowPlaying.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        nowPlaying.Children.Add(new TextBlock
        {
            Text = LocalTextExtension.Translate("REPRODUCIENDO AHORA"),
            Foreground = (Brush)FindResource("AccentBrush"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        });
        metadata.Children.Add(nowPlaying);
        var title = MirrorText(Now, 48, Brushes.White);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 12, 0, 6);
        title.MaxHeight = 116;
        metadata.Children.Add(title);
        var artist = MirrorText(Artist, 17, new SolidColorBrush(Color.FromArgb(0xCD, 255, 255, 255)));
        artist.Margin = new Thickness(0, 0, 0, 17);
        metadata.Children.Add(artist);
        var qualityText = MirrorText(AudioFormat, 10, (Brush)FindResource("AccentBrush"));
        qualityText.FontFamily = new FontFamily("Consolas, Segoe UI");
        var quality = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(11, 6, 11, 6),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = qualityText
        };
        metadata.Children.Add(quality);
        DockPanel.SetDock(metadata, Dock.Top);
        details.Children.Add(metadata);

        var lyricSection = new Grid();
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lyricHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        lyricHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lyricHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lyricHeader.Children.Add(new TextBlock
        {
            Text = LocalTextExtension.Translate("LETRA SINCRONIZADA"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB5, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var lyricHint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var lyricLiveDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        roomLyricsLiveDot = lyricLiveDot;
        lyricHint.Children.Add(lyricLiveDot);
        roomLyricsLiveLabel = new TextBlock
        {
            Text = LocalTextExtension.Translate("En vivo"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8E, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        lyricHint.Children.Add(roomLyricsLiveLabel);
        roomLyricsLookupButton = new Button
        {
            Content = LocalTextExtension.Translate("Buscar letras"),
            Padding = new Thickness(9, 5, 9, 5),
            FontSize = 10,
            Visibility = lyricLines.Length == 0 ? Visibility.Visible : Visibility.Collapsed
        };
        roomLyricsLookupButton.Click += LookupLyrics;
        lyricHint.Children.Add(roomLyricsLookupButton);
        Grid.SetColumn(lyricHint, 1);
        lyricHeader.Children.Add(lyricHint);
        Grid.SetRow(lyricHeader, 0);
        lyricSection.Children.Add(lyricHeader);

        var lyrics = new TextBlock
        {
            FontSize = 22,
            Foreground = Brushes.WhiteSmoke,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 35,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        roomLyrics = lyrics;
        lyricDisplayIndex = int.MinValue;
        UpdateLyricDisplay(player.Position.TotalSeconds);
        var lyricsContent = new Grid();
        lyricsContent.Children.Add(lyrics);
        var lyricsScroll = new ScrollViewer
        {
            Content = lyricsContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(lyricsScroll, 1);
        lyricSection.Children.Add(lyricsScroll);
        var lyricFooter = new TextBlock
        {
            Text = LocalTextExtension.Translate("Selecciona una línea para escucharla"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8E, 255, 255, 255)),
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(lyricFooter, 2);
        lyricSection.Children.Add(lyricFooter);

        var lyricsCard = new Border
        {
            Padding = new Thickness(22, 18, 22, 16),
            CornerRadius = new CornerRadius(22),
            Background = new LinearGradientBrush(
                Color.FromArgb(0x30, 255, 255, 255),
                Color.FromArgb(0x15, 255, 255, 255),
                90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = lyricSection
        };
        details.Children.Add(lyricsCard);
        stage.Children.Add(details);
        Grid.SetRow(stage, 1);
        layout.Children.Add(stage);

        Visibility artworkButtonVisibility = TransportArtworkButton.Visibility;
        TransportArtworkButton.Visibility = Visibility.Collapsed;
        var room = new Window
        {
            Owner = this,
            Title = LocalTextExtension.Translate("Escuchando"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowState = WindowState.Maximized,
            Content = layout,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(10, 12, 19))
        };
        listeningRoom = room;
        UpdateFullscreenToolbarButton(true);
        var focused = Keyboard.FocusedElement;
        room.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Escape or Key.F11)) return;
            key.Handled = true;
            room.Close();
        };
        room.Closed += (_, _) =>
        {
            layout.Children.Remove(Transport);
            Shell.Children.Add(Transport);
            TransportArtworkButton.Visibility = artworkButtonVisibility;
            listeningRoom = null;
            roomLyrics = null;
            roomLyricsLiveDot = null;
            roomLyricsLiveLabel = null;
            roomLyricsLookupButton = null;
            UpdateFullscreenToolbarButton(false);
            if (IsActive && focused != null) Keyboard.Focus(focused);
        };
        Shell.Children.Remove(Transport);
        Grid.SetRow(Transport, 2);
        Grid.SetColumn(Transport, 0);
        Grid.SetColumnSpan(Transport, 1);
        layout.Children.Add(Transport);
        try { room.Show(); }
        catch
        {
            layout.Children.Remove(Transport);
            if (Transport.Parent == null) Shell.Children.Add(Transport);
            TransportArtworkButton.Visibility = artworkButtonVisibility;
            listeningRoom = null;
            roomLyrics = null;
            roomLyricsLiveDot = null;
            roomLyricsLiveLabel = null;
            roomLyricsLookupButton = null;
            UpdateFullscreenToolbarButton(false);
            throw;
        }
    }

    private void UpdateFullscreenToolbarButton(bool active)
    {
        ((TextBlock)FullScreenMenuItem.Icon).Text = active ? "\uE73F" : "\uE740";
        string label = LocalTextExtension.Translate(active ? "Salir de pantalla completa" : "Pantalla completa");
        FullScreenMenuItem.Header = label;
        System.Windows.Automation.AutomationProperties.SetName(FullScreenMenuItem, label);
    }

    private static TextBlock MirrorText(TextBlock source, double size, Brush color)
    {
        var text = new TextBlock { FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = source });
        return text;
    }
}
