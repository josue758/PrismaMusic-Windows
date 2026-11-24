using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PrismaMusic;

internal enum ReplayGainMode { Off, Track, Album }

internal sealed record ReplayGainTags(double? TrackGain, double? TrackPeak, double? AlbumGain, double? AlbumPeak)
{
    internal static ReplayGainTags Read(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var file = AudioMetadata.Open(path);
            if (file.GetTag(TagLib.TagTypes.Xiph) is not TagLib.Ogg.XiphComment tags) return null;
            return new(Parse(tags.GetFirstField("REPLAYGAIN_TRACK_GAIN")), Parse(tags.GetFirstField("REPLAYGAIN_TRACK_PEAK")),
                Parse(tags.GetFirstField("REPLAYGAIN_ALBUM_GAIN")), Parse(tags.GetFirstField("REPLAYGAIN_ALBUM_PEAK")));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            System.Diagnostics.Trace.WriteLine(error.Message);
            return null;
        }
    }

    internal static double? Parse(string value)
    {
        var text = value.AsSpan().Trim();
        if (text.EndsWith("dB", StringComparison.OrdinalIgnoreCase)) text = text[..^2].TrimEnd();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && double.IsFinite(result)
            ? result : null;
    }

    internal double Decibels(ReplayGainMode mode, double eqBoost)
    {
        if (mode == ReplayGainMode.Off) return 0;
        bool album = mode == ReplayGainMode.Album && AlbumGain.HasValue;
        double? gain = album ? AlbumGain : TrackGain;
        if (gain == null) return 0;
        double peak = (album ? AlbumPeak ?? TrackPeak : TrackPeak) ?? 1;
        double maximum = -20 * Math.Log10(peak > 0 ? peak : 1) - Math.Max(0, eqBoost);
        return Math.Clamp(Math.Min(gain.Value, maximum), -96, 24);
    }
}

public partial class MainWindow
{
    private void AddReplayGainSettings(StackPanel card, Action save)
    {
        card.Children.Add(SettingsHint("Nivela el volumen de los FLAC con etiquetas ReplayGain."));
        var mode = new ComboBox { ItemsSource = new[] { T("Desactivado"), T("Por pista"), T("Por álbum") },
            SelectedIndex = (int)player.ReplayMode, Style = (Style)FindResource("FilterCombo"), MinWidth = 120 };
        AutomationProperties.SetName(mode, "ReplayGain");
        var gain = SettingsHint("");
        card.Children.Add(mode);
        card.Children.Add(gain);
        mode.SelectionChanged += async (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            mode.IsEnabled = false;
            try
            {
                InvalidateGapless();
                player.ReplayMode = (ReplayGainMode)mode.SelectedIndex;
                await ReopenOutput();
                save();
            }
            finally { mode.IsEnabled = true; }
        };
        WatchSettingsCard(gain, 300, () => gain.Text = player.StrictDirect || player.Direct
            ? T("ReplayGain se omite en salida directa.")
            : LocalTextExtension.Format("Ganancia aplicada · {0:+0.0;-0.0;0.0} dB", player.ReplayGainDecibels));
    }
}
