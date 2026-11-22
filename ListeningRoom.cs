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
        layout.SetResourceReference(Panel.BackgroundProperty, "SettingsBackground");

        var backdrop = new Image
        {
            Stretch = Stretch.UniformToFill, Opacity = artworkColors ? .23 * ambientLight : 0,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 92 }
        };
        backdrop.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source)) { Source = Cover });
        Grid.SetRowSpan(backdrop, 3);
        layout.Children.Add(backdrop);
        var tint = new Border { IsHitTestVisible = false };
        Grid.SetRowSpan(tint, 3);
        layout.Children.Add(tint);
        WatchSettingsCard(tint, 350, () =>
        {
            Color tone = artworkColors && paletteArtwork != null ? artworkTone : ((SolidColorBrush)FindResource("AccentBrush")).Color;
            byte strength = (byte)(ambientLight * (lightTheme ? 44 : 72));
            if (tint.Background is LinearGradientBrush last && last.GradientStops[0].Color == Color.FromArgb(strength, tone.R, tone.G, tone.B)) return;
            tint.Background = new LinearGradientBrush(Color.FromArgb(strength, tone.R, tone.G, tone.B),
                lightTheme ? Color.FromArgb(88, 255, 255, 255) : Color.FromArgb(138, 10, 15, 23), 23);
        });

        var header = new Grid { Margin = new Thickness(46, 20, 46, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var exit = SettingsButton("Tu biblioteca", (_, _) => listeningRoom?.Close());
        exit.ToolTip = T("Cerrar pantalla completa · Esc");
        header.Children.Add(exit);
        var heading = new TextBlock
        {
            Text = T("Sala de escucha"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        bool showLyrics = true;
        var lyricsToggle = new Button { Content = T("Letras"), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(lyricsToggle, 2);
        header.Children.Add(lyricsToggle);
        layout.Children.Add(header);

        var stage = new Grid { Margin = new Thickness(46, 8, 46, 18), ClipToBounds = true };
        stage.ColumnDefinitions.Add(new ColumnDefinition());
        stage.ColumnDefinitions.Add(new ColumnDefinition());
        var album = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        album.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        album.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stage.Children.Add(album);

        var art = new Grid { ClipToBounds = true };
        var fallback = new ArtworkTile();
        fallback.SetBinding(ArtworkTile.CaptionProperty, new Binding(nameof(FrameworkElement.Tag)) { Source = Cover });
        art.Children.Add(fallback);
        var cover = new Image { Stretch = Stretch.UniformToFill };
        cover.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source)) { Source = Cover });
        art.Children.Add(cover);
        var frame = new Border
        {
            Child = art, CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = .24 }
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        art.SizeChanged += (_, _) => art.Clip = new RectangleGeometry(new Rect(0, 0, art.ActualWidth, art.ActualHeight), 11, 11);
        album.Children.Add(frame);

        var metadata = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(metadata, 1);
        album.Children.Add(metadata);
        var titleRow = new DockPanel();
        var favorite = new Button
        {
            Width = 40, Height = 38, Style = (Style)FindResource("TransportIcon")
        };
        favorite.Click += Favorite;
        favorite.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = FavoriteToolbarButton });
        favorite.SetBinding(ToolTipProperty, new Binding(nameof(ToolTip)) { Source = FavoriteToolbarButton });
        favorite.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
            new Binding { Source = FavoriteToolbarButton, Path = new PropertyPath(System.Windows.Automation.AutomationProperties.NameProperty) });
        DockPanel.SetDock(favorite, Dock.Right);
        titleRow.Children.Add(favorite);
        var title = MirrorText(Now, 30, (Brush)FindResource("Ink"));
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        title.FontWeight = FontWeights.SemiBold;
        title.MaxHeight = 72;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        titleRow.Children.Add(title);
        metadata.Children.Add(titleRow);
        var artist = MirrorText(Artist, 15, (Brush)FindResource("Muted"));
        artist.Margin = new Thickness(0, 3, 0, 8);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.MaxHeight = 38;
        metadata.Children.Add(artist);
        var qualityText = MirrorText(AudioFormat, 11, (Brush)FindResource("Muted"));
        qualityText.FontFamily = new FontFamily("Consolas, Segoe UI");
        var quality = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(5), Child = qualityText
        };
        quality.SetResourceReference(Border.BackgroundProperty, "ControlFill");
        metadata.Children.Add(quality);

        var lyricSection = new Grid { Margin = new Thickness(24, 4, 0, 8) };
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        lyricSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(lyricSection, 1);
        stage.Children.Add(lyricSection);

        var lyricHeader = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var translate = SettingsButton("Traducir letra", TranslateLyrics);
        lyricHeader.Children.Add(translate);
        var translation = new CheckBox { Content = T("Español junto al original"), Style = (Style)FindResource("SettingsToggle"),
            IsChecked = showRoomTranslation, Margin = new Thickness(6, 0, 0, 0) };
        translation.Click += (_, _) =>
        {
            showRoomTranslation = translation.IsChecked == true;
            RefreshLyrics();
        };
        lyricHeader.Children.Add(translation);
        WatchSettingsCard(translation, 350, () =>
            translation.Visibility = translatedLines?.Count > 0 ? Visibility.Visible : Visibility.Collapsed);
        WatchSettingsCard(translate, 350, () => translate.IsEnabled = !string.IsNullOrWhiteSpace(sourceLyrics));
        lyricSection.Children.Add(lyricHeader);

        var lyrics = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 9, 0) };
        roomLyrics = lyrics;
        lyrics.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var lyricsScroll = new ScrollViewer
        {
            Style = (Style)FindResource("LyricsScroll"), Content = lyrics,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(lyricsScroll, 1);
        lyricSection.Children.Add(lyricsScroll);

        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        roomLyricsLookupButton = SettingsButton("Buscar letras", LookupLyrics);
        footer.Children.Add(roomLyricsLookupButton);
        roomLyricsLiveLabel = SettingsHint("Selecciona una línea para escucharla");
        roomLyricsLiveLabel.Margin = new Thickness(0, 7, 0, 0);
        footer.Children.Add(roomLyricsLiveLabel);
        Grid.SetRow(footer, 2);
        lyricSection.Children.Add(footer);

        void FitArtwork()
        {
            double width = stage.ActualWidth * (showLyrics ? .46 : .78);
            double size = Math.Clamp(Math.Min(stage.ActualHeight - 128, width), 112, 720);
            album.Width = frame.Height = size;
        }
        stage.SizeChanged += (_, _) => FitArtwork();
        lyricsToggle.Click += (_, _) =>
        {
            showLyrics = !showLyrics;
            lyricSection.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(album, showLyrics ? 1 : 2);
            System.Windows.Automation.AutomationProperties.SetName(lyricsToggle, T(showLyrics ? "Ocultar letras" : "Letras"));
            FitArtwork();
        };
        System.Windows.Automation.AutomationProperties.SetName(lyricsToggle, T("Ocultar letras"));
        Grid.SetRow(stage, 1);
        layout.Children.Add(stage);
        RefreshLyrics();

        Visibility artworkButtonVisibility = TransportArtworkButton.Visibility;
        GridLength trackColumn = TransportMetadataColumn.Width;
        TransportMetadata.Visibility = Visibility.Collapsed;
        TransportMetadataColumn.Width = new GridLength(0);
        ListeningPanelMenuItem.Visibility = Visibility.Collapsed;
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
        UpdateTransportViewport();
        room.FontFamily = FontFamily;
        room.FontSize = 13;
        room.UseLayoutRounding = true;
        UpdateFullscreenToolbarButton(true);
        var focused = Keyboard.FocusedElement;
        room.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Escape or Key.F11)) return;
            key.Handled = true;
            room.Close();
        };
        Thickness transportMargin = Transport.Margin;
        room.Closed += (_, _) =>
        {
            layout.Children.Remove(Transport);
            Shell.Children.Add(Transport);
            Transport.Margin = transportMargin;
            TransportArtworkButton.Visibility = artworkButtonVisibility;
            TransportMetadata.Visibility = Visibility.Visible;
            TransportMetadataColumn.Width = trackColumn;
            ListeningPanelMenuItem.Visibility = Visibility.Visible;
            listeningRoom = null;
            UpdateTransportViewport();
            roomLyrics = null;
            roomLyricsLiveDot = null;
            roomLyricsLiveLabel = null;
            roomLyricsLookupButton = null;
            UpdateFullscreenToolbarButton(false);
            if (IsActive && focused != null) Keyboard.Focus(focused);
        };
        Transport.Margin = new Thickness(24, 0, 24, 16);
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
            Transport.Margin = transportMargin;
            TransportArtworkButton.Visibility = artworkButtonVisibility;
            TransportMetadata.Visibility = Visibility.Visible;
            TransportMetadataColumn.Width = trackColumn;
            ListeningPanelMenuItem.Visibility = Visibility.Visible;
            listeningRoom = null;
            UpdateTransportViewport();
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
        string label = T(active ? "Salir de la Sala de escucha" : "Sala de escucha");
        FullScreenMenuItem.Header = label;
        System.Windows.Automation.AutomationProperties.SetName(FullScreenMenuItem, label);
        FullscreenToolbarButton.Content = active ? "\uE73F" : "\uE740";
        FullscreenToolbarButton.ToolTip = label + " · F11";
        System.Windows.Automation.AutomationProperties.SetName(FullscreenToolbarButton, label);
    }

    private static TextBlock MirrorText(TextBlock source, double size, Brush color)
    {
        var text = new TextBlock { FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = source });
        return text;
    }
}
