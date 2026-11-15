using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private static readonly string[] AppearanceProfileNames = { "Cristal", "Sereno", "Contraste" };
    private static readonly string[] AppearanceProfileDescriptions =
    {
        "Color ambiental y reflejos animados.",
        "Color suave y reflejos fijos.",
        "Fondo neutro con menos transparencias."
    };
    private static readonly Color[] AppearanceProfileColors =
    {
        Color.FromRgb(112, 98, 175), Color.FromRgb(66, 119, 128), Color.FromRgb(50, 53, 64)
    };
    private int appearanceProfile;
    private bool compactRows;
    private bool showTrackQuality = true;
    private bool miniAlwaysOnTop = true;

    private void ApplyLibraryPresentation()
    {
        var resources = Application.Current.Resources;
        resources["SongThumbnailSize"] = compactRows ? 30d : 42d;
        resources["TrackQualityVisibility"] = showTrackQuality ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyAppearanceProfile(int profile)
    {
        appearanceProfile = Math.Clamp(profile, 0, AppearanceProfileNames.Length - 1);
        ApplySurfaceMaterial();
        RefreshArtworkTheme();
    }


    private FrameworkElement CreateAppearanceProfiles(int selected, Action<int> change)
    {
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 12) };
        var group = "Appearance-" + Guid.NewGuid().ToString("N");
        for (int index = 0; index < AppearanceProfileNames.Length; index++)
        {
            int profile = index;
            var title = LocalTextExtension.Translate(AppearanceProfileNames[index]);
            var description = LocalTextExtension.Translate(AppearanceProfileDescriptions[index]);
            var sample = new Grid { Height = 31, Margin = new Thickness(0, 0, 0, 8) };
            sample.Children.Add(new Border { CornerRadius = new CornerRadius(5),
                Background = new LinearGradientBrush(AppearanceProfileColors[index], Color.FromRgb(30, 32, 42), 20) });
            sample.Children.Add(new Border { CornerRadius = new CornerRadius(3), Width = 28, Height = 15,
                Margin = new Thickness(7, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255)) });
            var content = new StackPanel();
            content.Children.Add(sample);
            content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            var detail = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
                FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(detail);
            var button = new RadioButton { Content = content, GroupName = group, IsChecked = selected == index,
                Style = (Style)FindResource("AppearanceChoice") };
            AutomationProperties.SetName(button, title);
            AutomationProperties.SetHelpText(button, description);
            button.Checked += (_, _) => change(profile);
            cards.Children.Add(button);
        }
        return cards;
    }
}
