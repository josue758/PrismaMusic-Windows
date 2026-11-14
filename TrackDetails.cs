using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private async void Inspect(object sender, RoutedEventArgs args) => await InspectTrack(Selected);

    private async void InspectPlayingTrack(object sender, RoutedEventArgs args) => await InspectTrack(current);

    private async Task InspectTrack(Track track)
    {
        if (track == null) return;
        try
        {
            var (tags, audio) = await Task.Run(() => ReadTrackDetails(track));
            ShowTrackDetails(track, tags, audio);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, LocalTextExtension.Translate("Información del archivo"));
        }
    }

    private static ((string Label, string Value)[] Tags, (string Label, string Value)[] Audio) ReadTrackDetails(Track track)
    {
        using var file = TagLib.File.Create(track.Path);
        var tag = file.Tag;
        var format = file.Properties;
        string Number(uint index, uint count) => index == 0 ? "" : count == 0 ? index.ToString() : $"{index} / {count}";
        return (new[]
        {
            ("Título", tag.Title ?? track.Name),
            ("Artista", string.Join(", ", tag.Performers)),
            ("Álbum", tag.Album ?? track.Album),
            ("Artista del álbum", string.Join(", ", tag.AlbumArtists)),
            ("Año", tag.Year > 0 ? tag.Year.ToString() : ""),
            ("Género", string.Join(", ", tag.Genres)),
            ("Pista", Number(tag.Track, tag.TrackCount)),
            ("Disco", Number(tag.Disc, tag.DiscCount)),
            ("Carátula integrada", LocalTextExtension.Translate(tag.Pictures.Length > 0 ? "Sí" : "No"))
        }, new[]
        {
            ("Formato de audio", format.Description),
            ("Frecuencia", format.AudioSampleRate > 0 ? $"{format.AudioSampleRate:N0} Hz" : ""),
            ("Resolución", format.BitsPerSample > 0 ? $"{format.BitsPerSample} bit" : ""),
            ("Bitrate", format.AudioBitrate > 0 ? $"{format.AudioBitrate} kb/s" : ""),
            ("Canales", format.AudioChannels > 0 ? format.AudioChannels.ToString() : ""),
            ("Tamaño", $"{new FileInfo(track.Path).Length:N0} B")
        });
    }

    private void ShowTrackDetails(Track track, (string Label, string Value)[] tags, (string Label, string Value)[] audio)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = track.Name, FontSize = 28, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 17) });
        AddSection("Etiquetas", tags);
        AddSection("Formato de audio", audio);
        var path = new TextBox { Text = track.Path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(9),
            Foreground = (Brush)FindResource("Ink"), Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(path, LocalTextExtension.Translate("Ruta del archivo"));
        panel.Children.Add(path);

        var dialog = new Window
        {
            Owner = listeningRoom ?? this, Title = LocalTextExtension.Translate("Información del archivo"),
            Width = 580, Height = 650, MinWidth = 420, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(33, 34, 40)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        var close = new Button { Content = LocalTextExtension.Translate("Cerrar"), IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(close);
        dialog.ShowDialog();

        void AddSection(string title, IEnumerable<(string Label, string Value)> rows)
        {
            panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate(title), FontSize = 15,
                Foreground = (Brush)FindResource("AccentBrush"), FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 6) });
            foreach (var (label, value) in rows.Where(row => !string.IsNullOrWhiteSpace(row.Value)))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = LocalTextExtension.Translate(label),
                    Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
                var content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(content, 1);
                row.Children.Add(content);
                panel.Children.Add(row);
            }
        }
    }
}
