using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PrismaMusic;

public partial class MainWindow
{
    private TextBlock roomLyrics;
    private Ellipse roomLyricsLiveDot;
    private TextBlock roomLyricsLiveLabel;
    private Button roomLyricsLookupButton;
    private int lyricDisplayIndex = int.MinValue;
    private IReadOnlyDictionary<string, string> lyricDisplayTranslation;

    private void UpdateLyricDisplay(double position)
    {
        if (roomLyricsLookupButton != null)
            roomLyricsLookupButton.Visibility = lyricLines.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (roomLyricsLiveLabel != null)
            roomLyricsLiveLabel.Visibility = lyricLines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (roomLyricsLiveDot != null)
            roomLyricsLiveDot.Visibility = lyricLines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        int index = Array.FindLastIndex(lyricLines, line => line.Time <= position);
        if (index == lyricDisplayIndex && ReferenceEquals(lyricDisplayTranslation, translatedLines)) return;
        lyricDisplayIndex = index;
        lyricDisplayTranslation = translatedLines;
        RenderLyrics(Lyrics, index);
        if (roomLyrics != null) RenderLyrics(roomLyrics, index);
    }

    private void RenderLyrics(TextBlock view, int index)
    {
        view.Inlines.Clear();
        if (lyricLines.Length == 0)
        {
            view.Text = sourceLyrics.Length == 0 ? LocalTextExtension.Translate("Sin letras disponibles") : sourceLyrics;
            return;
        }
        int start = Math.Max(0, index - 1);
        for (int i = start; i < Math.Min(start + 5, lyricLines.Length); i++)
        {
            var line = lyricLines[i];
            bool active = i == index;
            bool fullscreen = view == roomLyrics;
            var verse = new Hyperlink(new Run(line.Text))
            {
                Foreground = active ? (Brush)FindResource("Ink") : (Brush)FindResource("Muted"),
                FontSize = fullscreen ? (active ? 30 : 20) : (active ? 18 : 14),
                TextDecorations = null,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
            };
            System.Windows.Automation.AutomationProperties.SetHelpText(verse,
                TimeSpan.FromSeconds(line.Time).ToString(@"m\:ss"));
            verse.Click += (_, args) =>
            {
                args.Handled = true;
                if (!opened) return;
                player.Position = TimeSpan.FromSeconds(line.Time);
                Tick();
            };
            view.Inlines.Add(verse);
            if (translatedLines != null && translatedLines.TryGetValue(line.Text, out var translation) &&
                !string.Equals(line.Text, translation, StringComparison.OrdinalIgnoreCase))
            {
                view.Inlines.Add(new LineBreak());
                view.Inlines.Add(new Run(translation)
                {
                    Foreground = (Brush)FindResource(active ? "AccentBrush" : "Muted"),
                    FontSize = fullscreen ? 17 : 13
                });
            }
            view.Inlines.Add(new LineBreak());
            view.Inlines.Add(new LineBreak());
        }
    }
}
