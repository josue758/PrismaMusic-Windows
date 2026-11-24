using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private int settingsPageOnOpen;

    private void FilterSettingsPages(ListBox navigation, ScrollViewer outlet, string input)
    {
        string[] words = input.Split((char[])null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var links = navigation.Items.OfType<ListBoxItem>().ToArray();
        foreach (var link in links)
        {
            if (link.Tag is not StackPanel page) continue;
            bool showAll = words.Length == 0 || Matches(AutomationProperties.GetName(link));
            foreach (UIElement card in page.Children)
                card.Visibility = showAll || Matches(string.Join(" ", SettingsLabels(card))) ? Visibility.Visible : Visibility.Collapsed;
            link.Visibility = page.Children.OfType<UIElement>().Any(card => card.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        var first = links.FirstOrDefault(link => link.Visibility == Visibility.Visible);
        if (navigation.SelectedItem is not ListBoxItem { Visibility: Visibility.Visible })
            navigation.SelectedItem = first;
        if (first == null) outlet.Content = SettingsHint("No se encontraron ajustes.");
        else if (navigation.SelectedItem is ListBoxItem { Tag: StackPanel selected } && !ReferenceEquals(outlet.Content, selected))
            outlet.Content = selected;

        bool Matches(string text) => words.All(word => CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, word,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    private static IEnumerable<string> SettingsLabels(DependencyObject element)
    {
        if (element is TextBlock { Text.Length: > 0 } text) yield return text.Text;
        string label = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(label)) yield return label;
        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is string caption) yield return caption;
            else if (child is DependencyObject nested)
                foreach (var nestedLabel in SettingsLabels(nested)) yield return nestedLabel;
        }
    }

    private async Task ResetSettingsSection(int section)
    {
        switch (section)
        {
            case 0:
                player.StrictDirect = player.Direct = player.Exclusive = false;
                player.OutputId = null;
                player.OutputSampleRate = 0;
                player.Balance = 0;
                player.ReplayMode = ReplayGainMode.Off;
                eqEnabled = eqAutoHeadroom = true;
                eqPreamp = 0;
                Array.Clear(gains);
                Volume.Value = .7;
                ApplyEqualizer();
                UpdateVolumeControls();
                await ReopenOutput();
                break;
            case 1:
                player.Speed = 1;
                continueAfterTrack = gaplessEnabled = rememberPosition = miniAlwaysOnTop = true;
                keepAwake = false;
                if (mini != null) mini.Topmost = true;
                UpdateAwakeRequest(false);
                SetSleepTimer(null);
                SetStopAfterTrack(false);
                await ReopenOutput();
                break;
            case 2:
                scanSubfolders = true;
                scanOnLaunch = discoverNewMusic = false;
                parallelTagReads = 0;
                UpdateLibraryDiscovery();
                break;
            case 3:
                autoArtwork = true;
                autoLyrics = false;
                break;
            case 4:
                interfaceAnimations = pointerEffects = undoNotices = artworkColors = audioBackdrop = artworkMotion = true;
                surfaceTransparency = ambientLight = 1;
                compactRows = false;
                showTrackQuality = true;
                albumSize = 1;
                startupSection = 3;
                accentPreset = "artwork";
                ApplyTheme(1);
                ApplyAppearanceProfile(0);
                ApplyAlbumSize();
                ApplyLibraryPresentation();
                RefreshRows();
                break;
            case 5:
                lyricScale = visualizerSensitivity = 1;
                lyricOffset = 0;
                followLyrics = showRoomTranslation = animateSpectrum = true;
                visualizerStyle = "mirror";
                visualizerFramesPerSecond = 30;
                if (visualizerTimer != null) visualizerTimer.Interval = TimeSpan.FromMilliseconds(1000d / 30);
                preferredLyricSource = "en";
                preferredLyricTarget = "es";
                SpectrumView.Visibility = Visibility.Visible;
                RefreshLyrics();
                UpdateVisualizer();
                break;
        }
    }
}
