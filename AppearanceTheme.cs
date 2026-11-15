using System;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private int themePreference = 1;
    private bool lightTheme;
    private double surfaceTransparency = 1;
    private double ambientLight = 1;

    private static readonly (string Key, uint Dark, uint Light)[] ThemeColors =
    {
        ("Ink", 0xFFFFFFFF, 0xFF1B1B1B),
        ("Muted", 0xC5FFFFFF, 0xCF222222),
        ("SettingsBackground", 0xFF202020, 0xFFF4F4F5),
        ("SettingsCard", 0xFF292929, 0xFFFFFFFF),
        ("SettingsStroke", 0x38FFFFFF, 0x3D000000),
        ("WorkspaceStroke", 0x28FFFFFF, 0x23000000),
        ("NavigationBackdrop", 0x89191919, 0xC7F3F3F4),
        ("WorkspaceText", 0xFFF5F5F5, 0xFF1B1B1B),
        ("WorkspaceMuted", 0xC5FFFFFF, 0xC5222222),
        ("TitleSearchSurface", 0x13FFFFFF, 0x0E000000),
        ("ControlFill", 0x10FFFFFF, 0x0B000000),
        ("ControlHover", 0x26FFFFFF, 0x1A000000),
        ("ControlStroke", 0x32FFFFFF, 0x2D000000),
        ("TrackFill", 0x55FFFFFF, 0x58000000),
        ("ButtonOverlay", 0xFFFFFFFF, 0xFF000000),
        ("PopupFill", 0xFF27282E, 0xFFF9F9FA),
        ("ToggleFill", 0xFF22252D, 0xFFF0F0F1),
        ("Panel", 0xA82B2933, 0xC7FFFFFF)
    };

    private void WatchWindowsTheme()
    {
        UserPreferenceChangedEventHandler changed = (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (themePreference == 0 && lightTheme != IsWindowsLightTheme()) ApplyTheme(0);
            });
        };
        SystemEvents.UserPreferenceChanged += changed;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= changed;
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value != 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ApplyTheme(int selection)
    {
        themePreference = Math.Clamp(selection, 0, 2);
        lightTheme = themePreference == 2 || (themePreference == 0 && IsWindowsLightTheme());
        foreach (var (key, dark, light) in ThemeColors)
            SetThemeBrush(key, lightTheme ? light : dark);
        ApplySurfaceMaterial();
        RefreshArtworkTheme();
        foreach (Window window in Application.Current.Windows) UpdateWindowCaptionTheme(window);
    }

    private void ApplySurfaceMaterial()
    {
        byte opacity = (byte)(255 - 36 * surfaceTransparency);
        uint rgb = lightTheme ? 0xFAFAFBu : appearanceProfile == 2 ? 0x1F2127u : 0x202020u;
        SetThemeBrush("WorkspaceSurface", (uint)(appearanceProfile == 2 ? 255 : opacity) << 24 | rgb);
        SetThemeBrush("ListeningSurface", (uint)(appearanceProfile == 2 ? 255 : opacity) << 24 | rgb);
        SetThemeBrush("TransportSurface", (uint)(appearanceProfile == 2 ? 255 : opacity) << 24 | (lightTheme ? 0xFFFFFFu : 0x252525u));
    }

    private static void SetThemeBrush(string key, uint argb)
    {
        var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        var resources = Application.Current.Resources;
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }

    private FrameworkElement CreateThemePicker(Action save)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        string group = $"Theme-{Guid.NewGuid():N}";
        string[] labels = { "Sistema", "Oscuro", "Claro" };
        for (int index = 0; index < labels.Length; index++)
        {
            int selection = index;
            var radio = new RadioButton
            {
                Style = (Style)FindResource("AppearanceChoice"),
                Content = T(labels[index]), GroupName = group, IsChecked = themePreference == index, MinWidth = 82
            };
            AutomationProperties.SetName(radio, T(labels[index]));
            radio.Checked += (_, _) => { ApplyTheme(selection); save(); };
            row.Children.Add(radio);
        }
        return row;
    }
}
