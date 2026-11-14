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
    private double lyricOffset;
    private double lyricScale = 1;
    private bool followLyrics = true;

    private void RefreshLyrics()
    {
        lyricDisplayIndex = int.MinValue;
        UpdateLyricDisplay(player.Position.TotalSeconds);
    }

    private FrameworkElement CreateLyricSetting(string title, double minimum, double maximum, double step,
        double current, string format, Action<double> change)
    {
        string label = LocalTextExtension.Translate(title);
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
        var heading = new DockPanel();
        var value = new TextBlock { Text = current.ToString(format), Foreground = (Brush)FindResource("Muted") };
        DockPanel.SetDock(value, Dock.Right);
        heading.Children.Add(value);
        heading.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(heading);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = current, TickFrequency = step,
            IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Style = (Style)FindResource("TransportSlider") };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, args) => { value.Text = args.NewValue.ToString(format); change(args.NewValue); };
        panel.Children.Add(slider);
        return panel;
    }

    private void UpdateLyricDisplay(double position)
    {
        if (roomLyricsLookupButton != null)
            roomLyricsLookupButton.Visibility = lyricLines.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (roomLyricsLiveLabel != null)
            roomLyricsLiveLabel.Visibility = lyricLines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (roomLyricsLiveDot != null)
            roomLyricsLiveDot.Visibility = lyricLines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        int index = followLyrics ? Array.FindLastIndex(lyricLines, line => line.Time <= position + lyricOffset) : -1;
        if (index == lyricDisplayIndex && ReferenceEquals(lyricDisplayTranslation, translatedLines)) return;
        lyricDisplayIndex = index;
        lyricDisplayTranslation = translatedLines;
        RenderLyrics(Lyrics, index);
        if (roomLyrics != null) RenderLyrics(roomLyrics, index);
    }

    private void RenderLyrics(TextBlock view, int index)
    {
        view.Inlines.Clear();
        bool fullscreen = view == roomLyrics;
        view.FontSize = (fullscreen ? 22 : 14) * lyricScale;
        if (fullscreen) view.LineHeight = 35 * lyricScale;
        if (lyricLines.Length == 0)
        {
            view.Text = sourceLyrics.Length == 0 ? LocalTextExtension.Translate("Sin letras disponibles") : sourceLyrics;
            return;
        }
        int start = followLyrics ? Math.Max(0, index - 1) : 0;
        int end = followLyrics ? Math.Min(start + 5, lyricLines.Length) : lyricLines.Length;
        for (int i = start; i < end; i++)
        {
            var line = lyricLines[i];
            bool active = i == index;
            var verse = new Hyperlink(new Run(line.Text))
            {
                Foreground = active ? (Brush)FindResource("Ink") : (Brush)FindResource("Muted"),
                FontSize = (fullscreen ? (active ? 30 : 20) : (active ? 18 : 14)) * lyricScale,
                TextDecorations = null,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
            };
            System.Windows.Automation.AutomationProperties.SetHelpText(verse,
                TimeSpan.FromSeconds(line.Time).ToString(@"m\:ss"));
            verse.Click += (_, args) =>
            {
                args.Handled = true;
                if (!opened) return;
                double duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : double.MaxValue;
                player.Position = TimeSpan.FromSeconds(Math.Clamp(line.Time - lyricOffset, 0, duration));
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
                    FontSize = (fullscreen ? 17 : 13) * lyricScale
                });
            }
            view.Inlines.Add(new LineBreak());
            view.Inlines.Add(new LineBreak());
        }
    }
}
