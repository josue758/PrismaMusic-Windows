using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private DispatcherTimer searchDebounce;
    private readonly ConditionalWeakTable<Track, LibraryRow> cachedSongRows = new();
    private static readonly ConditionalWeakTable<Track, string> cachedSearchText = new();

    private void AnimateLibraryNavigation()
    {
        FrameworkElement view = HomeView.IsVisible ? HomeView : Rows;
        view.BeginAnimation(OpacityProperty, MotionEnabled
            ? new System.Windows.Media.Animation.DoubleAnimation(.80, 1, TimeSpan.FromMilliseconds(145))
                { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop, EasingFunction = new System.Windows.Media.Animation.CubicEase() }
            : null);
    }

    private void SearchTextChanged(object sender, TextChangedEventArgs args)
    {
        if (!ready || restoringLibraryFilters) return;
        if (smokeTest || !Search.IsKeyboardFocusWithin)
        {
            searchDebounce?.Stop();
            Filter(sender, args);
            return;
        }
        if (searchDebounce == null)
        {
            searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(145) };
            searchDebounce.Tick += (_, _) =>
            {
                searchDebounce.Stop();
                Filter(Search, new RoutedEventArgs());
            };
            Closed += (_, _) => searchDebounce.Stop();
        }
        searchDebounce.Stop();
        searchDebounce.Start();
    }

    private static readonly DependencyPropertyKey WideLibraryColumnsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(WideLibraryColumns), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty WideLibraryColumnsProperty = WideLibraryColumnsPropertyKey.DependencyProperty;

    public bool WideLibraryColumns => (bool)GetValue(WideLibraryColumnsProperty);

    private void UpdateWideLibraryColumns() =>
        SetValue(WideLibraryColumnsPropertyKey, Rows.ActualWidth >= 780);

    private IEnumerable<Track> ResolveSavedTracks(IEnumerable<string> paths)
    {
        var byPath = library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        return paths.Where(byPath.ContainsKey).Select(path => byPath[path]);
    }

    private int LibrarySectionIndex
    {
        get => int.TryParse(Navigation.SelectedValue as string, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? index : -1;
        set => Navigation.SelectedValue = value.ToString(CultureInfo.InvariantCulture);
    }

    private static readonly DependencyPropertyKey CurrentTrackPathPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CurrentTrackPath), typeof(string), typeof(MainWindow), new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentTrackPathProperty = CurrentTrackPathPropertyKey.DependencyProperty;

    public string CurrentTrackPath
    {
        get => (string)GetValue(CurrentTrackPathProperty);
        private set => SetValue(CurrentTrackPathPropertyKey, value);
    }
}

public sealed class LibraryTrackMatches : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string path && values[1] is string current &&
        string.Equals(path, current, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
