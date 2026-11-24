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
    private readonly Dictionary<int, Track[]> sortedLibraryTracks = new();
    private List<Track> indexedLibraryTracks;
    private int indexedLibraryCount;
    private Dictionary<string, Track> indexedTracksByPath;
    private ILookup<(string Album, string Owner), Track> indexedAlbums;
    private readonly Dictionary<(string Album, string Owner), Track[]> orderedAlbums = new();

    private void RefreshLibraryIndex()
    {
        if (!ReferenceEquals(indexedLibraryTracks, library.Tracks) || indexedLibraryCount != library.Tracks.Count)
        {
            indexedLibraryTracks = library.Tracks;
            indexedLibraryCount = indexedLibraryTracks.Count;
            sortedLibraryTracks.Clear();
            indexedTracksByPath = null;
            indexedAlbums = null;
            orderedAlbums.Clear();
        }
    }

    private static (string Album, string Owner) AlbumKey(string album, string owner) =>
        ((album ?? "").ToUpperInvariant(), (owner ?? "").ToUpperInvariant());

    private Track[] IndexedAlbum(string album, string owner)
    {
        RefreshLibraryIndex();
        var key = AlbumKey(album, owner);
        if (orderedAlbums.TryGetValue(key, out var tracks)) return VisibleTracks(tracks);
        indexedAlbums ??= library.Tracks.ToLookup(track => AlbumKey(track.Album, AlbumOwner(track)));
        tracks = indexedAlbums[key].OrderBy(track => track.Disc).ThenBy(track => track.Number).ThenBy(track => track.Name).ToArray();
        if (tracks.Length > 0) orderedAlbums.Add(key, tracks);
        return VisibleTracks(tracks);

        Track[] VisibleTracks(Track[] members) => library.HiddenTracks.Count == 0 ? members :
            members.Where(track => !library.HiddenTracks.Contains(track.Path)).ToArray();
    }

    private sealed class LibraryQuery
    {
        private static readonly ConditionalWeakTable<Track, string> SearchText = new();
        private readonly CompareInfo comparison = CultureInfo.CurrentCulture.CompareInfo;
        private readonly string[] words;

        internal LibraryQuery(string text)
        {
            words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
                words = words.OrderByDescending(word => word.All(char.IsAsciiDigit)).ThenByDescending(word => word.Length).ToArray();
        }

        internal bool IsEmpty => words.Length == 0;

        internal bool Matches(Track track)
        {
            string searchable = SearchText.GetValue(track, static item =>
                $"{item.Name} {item.Detail} {item.AlbumArtist} {item.Genre} {item.FormatLabel}");
            foreach (string word in words)
                if (comparison.IndexOf(searchable, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0)
                    return false;
            return true;
        }

        internal IEnumerable<Track> Filter(IEnumerable<Track> source) => source is IReadOnlyList<Track> { Count: >= 4096 } snapshot &&
            Environment.ProcessorCount > 1
            ? snapshot.AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Min(6, Environment.ProcessorCount)).Where(Matches).ToArray()
            : source.Where(Matches);
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
