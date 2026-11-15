using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record AccentChoice(string Id, string Label, Color Color);

    private static readonly AccentChoice[] AccentChoices =
    {
        new("violet", "Amatista", Color.FromRgb(190, 145, 255)),
        new("blue", "Océano", Color.FromRgb(96, 188, 255)),
        new("rose", "Rosa", Color.FromRgb(255, 112, 150)),
        new("mint", "Menta", Color.FromRgb(57, 225, 202)),
        new("amber", "Ámbar", Color.FromRgb(255, 176, 73)),
        new("indigo", "Índigo", Color.FromRgb(142, 129, 255)),
        new("emerald", "Esmeralda", Color.FromRgb(80, 220, 130)),
        new("sunset", "Atardecer", Color.FromRgb(255, 109, 106))
    };

    private bool interfaceAnimations = true;
    private bool pointerEffects = true;
    private bool undoNotices = true;
    private bool audioBackdrop = true;
    private bool artworkMotion = true;
    private BitmapSource paletteArtwork;
    private Color artworkTone;
    private DateTime? queueNoticeDeadline;
    private readonly TranslateTransform ambientOffset = new();

    private bool MotionEnabled => interfaceAnimations && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    private void AnimateSettingsPage(FrameworkElement page)
    {
        page.BeginAnimation(OpacityProperty, MotionEnabled
            ? new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop } : null);
    }

    private static string NormalizeAccent(string preset) =>
        AccentChoices.Any(choice => choice.Id == preset) || preset == "system" ? preset : "artwork";

    private Color AccentPresetColor() =>
        AccentChoices.FirstOrDefault(choice => choice.Id == accentPreset)?.Color ?? windowsAccent;

    private void RefreshArtworkTheme()
    {
        var source = Cover.Source as BitmapSource;
        if (!ReferenceEquals(paletteArtwork, source))
        {
            paletteArtwork = source;
            if (source != null) artworkTone = ArtworkPalette.ExtractAccent(source, windowsAccent);
        }

        Color album = paletteArtwork != null ? artworkTone
            : current == null ? windowsAccent : ArtworkTile.AccentForCaption(current.Album, windowsAccent);
        Color accent = accentPreset == "artwork" && artworkColors ? album : AccentPresetColor();
        SetArtworkAccent(accent);
        ApplyAtmosphere(artworkColors ? album : accent);
    }

    private void ApplyAtmosphere(Color tone)
    {
        ApplyArtworkSurfaces(tone);
        AmbientArtwork.Opacity = !artworkColors ? 0 : appearanceProfile switch { 1 => .06, 2 => 0, _ => .14 };
        GlassReflection.Opacity = !pointerEffects ? 0 : ambientLight * (appearanceProfile switch { 1 => .035, 2 => 0, _ => .08 });
        ArtworkTint.Opacity = ambientLight * (appearanceProfile switch { 1 => .22, 2 => 0, _ => .53 });
        Shell.Background = ArtworkShellBrush(tone);
    }

    private void UpdateArtworkMotion()
    {
        bool drift = MotionEnabled && artworkMotion && artworkColors && appearanceProfile == 0 && AmbientArtwork.Source != null;
        double time = Environment.TickCount64 / 4500d;
        ambientOffset.X = drift ? 5 * Math.Sin(time) : 0;
        ambientOffset.Y = drift ? 3 * Math.Cos(time * .8) : 0;
    }

    private FrameworkElement CreateArtworkPalettePreview()
    {
        var layout = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var title = SettingsHint("Paleta de la carátula");
        layout.Children.Add(title);
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        var tiles = new Border[3];
        for (int index = 0; index < tiles.Length; index++)
        {
            var tile = new Border
            {
                Height = 25, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, index == 2 ? 0 : 6, 0),
                BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("WorkspaceStroke")
            };
            row.Children.Add(tile);
            tiles[index] = tile;
        }
        layout.Children.Add(row);

        ImageSource last = null;
        Track lastTrack = null;
        bool rendered = false;
        void Refresh()
        {
            if (rendered && ReferenceEquals(last, Cover.Source) && ReferenceEquals(lastTrack, current)) return;
            last = Cover.Source;
            lastTrack = current;
            rendered = true;
            row.Visibility = paletteArtwork == null && current == null ? Visibility.Collapsed : Visibility.Visible;
            title.Text = T(paletteArtwork != null ? "Paleta de la carátula" : current != null ? "Paleta de la portada generada" : "Reproduce una pista con carátula para ver su paleta.");
            var baseColor = paletteArtwork != null ? artworkTone : current == null ? windowsAccent : ArtworkTile.AccentForCaption(current.Album, windowsAccent);
            var colors = new[] { baseColor, Mix(baseColor, Colors.Black, .58), Mix(baseColor, Colors.White, .34) };
            for (int index = 0; index < tiles.Length; index++)
            {
                Color swatch = colors[index];
                tiles[index].Background = new LinearGradientBrush(swatch, Mix(swatch, Colors.Black, .16), 90);
                tiles[index].ToolTip = $"#{swatch.R:X2}{swatch.G:X2}{swatch.B:X2}";
            }
        }
        WatchSettingsCard(layout, 350, Refresh);
        return layout;
    }

    private FrameworkElement CreateAccentPicker(Action save)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        string group = $"Accent-{Guid.NewGuid():N}";
        AddChoice(new AccentChoice("artwork", "Carátula", artworkTone == default ? windowsAccent : artworkTone), automatic: true);
        AddChoice(new AccentChoice("system", "Windows", windowsAccent), automatic: true);
        foreach (var choice in AccentChoices) AddChoice(choice);

        void AddChoice(AccentChoice choice, bool automatic = false)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("AccentSwatch"), GroupName = group,
                Background = new SolidColorBrush(choice.Color),
                Foreground = new SolidColorBrush(ColorContrast(choice.Color)),
                Content = T(choice.Label), IsChecked = accentPreset == choice.Id,
                ToolTip = T(choice.Label)
            };
            if (automatic) option.SetResourceReference(BorderBrushProperty, "SettingsStroke");
            AutomationProperties.SetName(option, T(choice.Label));
            option.Checked += (_, _) =>
            {
                accentPreset = choice.Id;

                RefreshArtworkTheme();
                save();
            };
            row.Children.Add(option);
            if (choice.Id == "artwork") WatchSettingsCard(option, 350, () =>
            {
                var tone = paletteArtwork != null ? artworkTone : windowsAccent;
                if (option.Background is SolidColorBrush brush && brush.Color == tone) return;
                option.Background = new SolidColorBrush(tone);
                option.Foreground = new SolidColorBrush(ColorContrast(tone));
            });
        }
        return row;
    }

    private static Color ColorContrast(Color color)
    {
        double brightness = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        return brightness > 135 ? Colors.Black : Colors.White;
    }

    private static Color Mix(Color from, Color toward, double ratio) => Color.FromRgb(
        (byte)(from.R + (toward.R - from.R) * ratio),
        (byte)(from.G + (toward.G - from.G) * ratio),
        (byte)(from.B + (toward.B - from.B) * ratio));

    private void ClearQueueNotice()
    {
        queueNoticeDeadline = null;
        QueueRemovalNotice.Visibility = Visibility.Collapsed;
    }
}
