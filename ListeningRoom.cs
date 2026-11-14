using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

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

        var layout = new DockPanel();
        layout.SetBinding(Panel.BackgroundProperty, new Binding(nameof(Panel.Background)) { Source = Shell });
        var header = new DockPanel { Margin = new Thickness(28, 18, 28, 0) };
        var exit = new Button { Content = LocalTextExtension.Translate("‹ Volver"), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(exit, Dock.Right);
        exit.Click += (_, _) => listeningRoom?.Close();
        header.Children.Add(exit);
        header.Children.Add(new TextBlock { Text = "PRISMA MUSIC  /  " + LocalTextExtension.Translate("Escuchando").ToUpper(),
            FontSize = 13, Foreground = Brushes.Lavender, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var stage = new Grid { Margin = new Thickness(45, 24, 45, 24) };
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Grid { ClipToBounds = true };
        art.Background = new LinearGradientBrush(Color.FromRgb(100, 73, 172), Color.FromRgb(33, 24, 56), 120);
        art.Children.Add(new TextBlock { Text = "♫", FontSize = 90, Foreground = Brushes.Lavender,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var cover = new Image { Stretch = Stretch.Uniform };
        cover.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source)) { Source = Cover });
        art.Children.Add(cover);
        var frame = new Border { Child = art, CornerRadius = new CornerRadius(18), MaxWidth = 550, MaxHeight = 550,
            Margin = new Thickness(0, 0, 26, 0), Background = art.Background };
        stage.Children.Add(frame);

        var details = new DockPanel { Margin = new Thickness(22, 8, 0, 8) };
        Grid.SetColumn(details, 1);
        var title = MirrorText(Now, 38, Brushes.White);
        title.FontWeight = FontWeights.SemiBold;
        var artist = MirrorText(Artist, 17, Brushes.Lavender);
        var format = MirrorText(AudioFormat, 13, new SolidColorBrush(Color.FromRgb(198, 172, 255)));
        foreach (var label in new[] { title, artist, format })
        {
            label.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(label, Dock.Top);
            details.Children.Add(label);
        }
        var lyrics = MirrorText(Lyrics, 21, Brushes.WhiteSmoke);
        lyrics.LineHeight = 30;
        details.Children.Add(new ScrollViewer { Content = lyrics, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 14, 0, 0) });
        stage.Children.Add(details);

        var room = new Window
        {
            Owner = this, Title = LocalTextExtension.Translate("Escuchando"), WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowState = WindowState.Maximized, Content = layout, ShowInTaskbar = false
        };
        listeningRoom = room;
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
            listeningRoom = null;
            if (IsActive && focused != null) Keyboard.Focus(focused);
        };
        Shell.Children.Remove(Transport);
        DockPanel.SetDock(Transport, Dock.Bottom);
        layout.Children.Add(Transport);
        layout.Children.Add(stage);
        try { room.Show(); }
        catch
        {
            layout.Children.Remove(Transport);
            if (Transport.Parent == null) Shell.Children.Add(Transport);
            listeningRoom = null;
            throw;
        }
    }

    private static TextBlock MirrorText(TextBlock source, double size, Brush color)
    {
        var text = new TextBlock { FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = source });
        return text;
    }
}
