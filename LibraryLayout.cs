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
    private readonly Dictionary<int, Track[]> sortedLibraryTracks = new();
    private List<Track> indexedLibraryTracks;
    private int indexedLibraryCount;
    private Dictionary<string, Track> indexedTracksByPath;
    private ILookup<(string Album, string Owner), Track> indexedAlbums;

    private void RefreshLibraryIndex()
    {
        if (!ReferenceEquals(indexedLibraryTracks, library.Tracks) || indexedLibraryCount != library.Tracks.Count)
        {
            indexedLibraryTracks = library.Tracks;
            indexedLibraryCount = indexedLibraryTracks.Count;
            sortedLibraryTracks.Clear();
            indexedTracksByPath = null;
            indexedAlbums = null;
        }
    }

    private static (string Album, string Owner) AlbumKey(string album, string owner) =>
        ((album ?? "").ToUpperInvariant(), (owner ?? "").ToUpperInvariant());

    private IEnumerable<Track> IndexedAlbum(string album, string owner)
    {
        RefreshLibraryIndex();
        indexedAlbums ??= library.Tracks.ToLookup(track => AlbumKey(track.Album, AlbumOwner(track)));
        return indexedAlbums[AlbumKey(album, owner)];
    }

    private Track[] OrderedLibraryTracks(int order)
    {
        RefreshLibraryIndex();
        if (sortedLibraryTracks.TryGetValue(order, out var existing)) return existing;
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var ordered = order switch
        {
            1 => library.Tracks.OrderBy(track => track.Artist, comparer).ThenBy(track => track.Name, comparer),
            2 => library.Tracks.OrderByDescending(track => track.Year).ThenBy(track => track.Name, comparer),
            3 => library.Tracks.OrderBy(track => track.Album, comparer).ThenBy(track => track.Disc)
                .ThenBy(track => track.Number).ThenBy(track => track.Name, comparer),
            _ => library.Tracks.OrderBy(track => track.Name, comparer)
        };
        return sortedLibraryTracks[order] = ordered.ToArray();
    }

    private void AnimateLibraryNavigation()
    {
        FrameworkElement view = ArtistView.IsVisible ? ArtistView : HomeView.IsVisible ? HomeView : Rows;
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

    private static readonly DependencyPropertyKey ShowAlbumColumnsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ShowAlbumColumns), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowAlbumColumnsProperty = ShowAlbumColumnsPropertyKey.DependencyProperty;
    public bool ShowAlbumColumns => (bool)GetValue(ShowAlbumColumnsProperty);

    private void UpdateWideLibraryColumns()
    {
        SetValue(WideLibraryColumnsPropertyKey, Rows.ActualWidth >= 780);
        SetValue(ShowAlbumColumnsPropertyKey, WideLibraryColumns && (collection == null || !albumPage));
    }

    private IEnumerable<Track> ResolveSavedTracks(IEnumerable<string> paths)
    {
        var byPath = TrackIndex();
        foreach (var path in paths)
            if (byPath.TryGetValue(path, out var track)) yield return track;
    }

    private Dictionary<string, Track> TrackIndex()
    {
        RefreshLibraryIndex();
        return indexedTracksByPath ??= library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
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
