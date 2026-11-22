using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private const int GalleryPageSize = 30;
    private int galleryPage;
    private IEnumerable<Track> galleryExportTracks;
    private readonly SemaphoreSlim artworkWorkers = new(2);
    private readonly SemaphoreSlim galleryArtworkWorkers = new(3);
    private readonly SemaphoreSlim artistArtworkWorkers = new(2);
    private readonly Dictionary<Track, BitmapImage> trackThumbnails = new();

    private void SetGalleryRows(LibraryRow[] rows)
    {
        galleryExportTracks = rows.SelectMany(row => row.Tracks);
        int pages = Math.Max(1, (rows.Length + GalleryPageSize - 1) / GalleryPageSize);
        galleryPage = Math.Clamp(galleryPage, 0, pages - 1);
        Rows.ItemsSource = rows.Skip(galleryPage * GalleryPageSize).Take(GalleryPageSize).ToArray();
        GalleryPager.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        GalleryPageLabel.Text = $"{galleryPage + 1} / {pages}";
        GalleryPrevious.IsEnabled = galleryPage > 0;
        GalleryNext.IsEnabled = galleryPage + 1 < pages;
    }

    private void PreviousGalleryPage(object sender, RoutedEventArgs args) => ChangeGalleryPage(-1);
    private void NextGalleryPage(object sender, RoutedEventArgs args) => ChangeGalleryPage(1);

    private void ChangeGalleryPage(int amount)
    {
        galleryPage += amount;
        RefreshRows();
        if (Rows.Items.Count > 0) Rows.ScrollIntoView(Rows.Items[0]);
    }

    private static string PortraitPath(string artist)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artist.Trim().ToUpperInvariant())));
        return Path.Combine(DataDirectory, "portraits", key + ".img");
    }

    private async void LoadGalleryCard(object sender, RoutedEventArgs args)
    {
        if (sender is Image image) await LoadArtworkCard(image, artist: LibrarySectionIndex == 2);
    }

    private async Task LoadArtworkCard(Image image, bool artist, int decodeWidth = 160)
    {
        if (image.DataContext is not LibraryRow row) return;
        var workers = artist ? artistArtworkWorkers : galleryArtworkWorkers;
        await workers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, row)) return;
            var bitmap = await LoadCollectionArtwork(row, artist, decodeWidth);
            if (image.IsLoaded && ReferenceEquals(image.DataContext, row)) image.Source = bitmap;
        }
        finally { workers.Release(); }
    }

    private void GalleryArtworkChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Image image) return;
        image.Source = null;
        if (image.IsLoaded) LoadGalleryCard(image, new RoutedEventArgs());
    }

    private void RoundGalleryArtwork(object sender, SizeChangedEventArgs args)
    {
        if (sender is FrameworkElement artwork)
            artwork.Clip = new System.Windows.Media.RectangleGeometry(new Rect(args.NewSize), 8, 8);
    }

    private void TrackArtworkChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Image image)
        {
            image.Source = null;
            if (image.IsLoaded) LoadTrackArtwork(image, new RoutedEventArgs());
        }
    }

    private async void LoadTrackArtwork(object sender, RoutedEventArgs args)
    {
        if (sender is not Image image || image.DataContext is not LibraryRow { Tracks.Length: 1 } row) return;
        var track = row.Tracks[0];
        if (trackThumbnails.TryGetValue(track, out var cached))
        {
            image.Source = cached;
            return;
        }
        await artworkWorkers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, row)) return;
            if (!trackThumbnails.TryGetValue(track, out var bitmap))
            {
                bitmap = await Task.Run(() => ReadCollectionImage(row, artist: false, 48));
                if (trackThumbnails.Count >= 384) trackThumbnails.Clear();
                trackThumbnails[track] = bitmap;
            }
            if (image.IsLoaded && ReferenceEquals(image.DataContext, row)) image.Source = bitmap;
        }
        finally { artworkWorkers.Release(); }
    }

    private async Task<BitmapImage> LoadCollectionArtwork(LibraryRow page, bool artist, int size)
    {
        var bitmap = await Task.Run(() => artist ? ReadArtistPortrait(page.Name, size) : ReadCollectionImage(page, false, size));
        if (bitmap != null) return bitmap;
        if (artist)
        {
            if (autoArtwork && !page.Tracks.All(IsDemo))
            {
                try
                {
                    if (await EnsureArtistPortrait(page.Name))
                    {
                        bitmap = await Task.Run(() => ReadArtistPortrait(page.Name, size));
                        if (bitmap != null) return bitmap;
                    }
                }
                catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Online artist portrait lookup failed: {error.Message}"); }
            }
            return await Task.Run(() => ReadCollectionImage(page, false, size));
        }

        if (!autoArtwork || page.Tracks.All(IsDemo)) return null;
        Track track = page.Tracks.FirstOrDefault();
        if (track == null) return null;
        try
        {
            byte[] payload = await OnlineArtwork.FindAlbumCover(track.Album, AlbumOwner(track));
            if (payload == null) return null;
            try { database?.SaveArtwork(track.Path, payload); }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Online album artwork cache write failed: {error.Message}"); }
            using var stream = new MemoryStream(payload, writable: false);
            return await Task.Run(() => DecodeArtwork(stream, size));
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine($"Online album artwork lookup failed: {error.Message}");
            return null;
        }
    }

    private BitmapImage ReadCollectionImage(LibraryRow page, bool artist, int size)
    {
        if (artist && ReadArtistPortrait(page.Name, size) is { } portrait) return portrait;
        foreach (var track in page.Tracks.Take(3))
        {
            try
            {
                var payload = ReadArtworkPayload(track.Path);
                if (payload == null) continue;
                using var bytes = new MemoryStream(payload);
                return DecodeArtwork(bytes, size);
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.Message); }
        }
        return null;
    }

    private BitmapImage ReadArtistPortrait(string artist, int size)
    {
        try
        {
            var path = PortraitPath(artist);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return DecodeArtwork(stream, size);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine(error.Message);
            return null;
        }
    }

    private byte[] ReadArtworkPayload(string path)
    {
        return ReadArtworkPayload(path, () =>
        {
            using var file = AudioMetadata.Open(path);
            return file.Tag.Pictures.FirstOrDefault()?.Data.Data;
        });
    }

    private byte[] ReadArtworkPayload(string path, Func<byte[]> readEmbedded)
    {
        try
        {
            var cached = database?.ReadArtwork(path);
            if (cached != null) return cached;
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Artwork cache read failed: {error.Message}"); }

        try
        {
            byte[] payload = readEmbedded();
            if (payload == null) return ReadFolderArtwork(path);
            try { database?.SaveArtwork(path, payload); }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Artwork cache write failed: {error.Message}"); }
            return payload;
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Artwork read failed: {error.Message}"); return null; }
    }

    private readonly ConcurrentDictionary<string, (string Path, long Expires)> folderArtworkPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] FolderArtworkNames = { "cover.jpg", "folder.jpg", "cover.png", "folder.png", "cover.jpeg", "folder.jpeg" };

    private byte[] ReadFolderArtwork(string trackPath)
    {
        string directory = Path.GetDirectoryName(trackPath);
        if (string.IsNullOrEmpty(directory)) return null;
        if (!folderArtworkPaths.TryGetValue(directory, out var entry) || Environment.TickCount64 >= entry.Expires)
        {
            string path = FolderArtworkNames.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
            entry = (path, Environment.TickCount64 + 60_000);
            if (folderArtworkPaths.Count >= 512) folderArtworkPaths.Clear();
            folderArtworkPaths[directory] = entry;
        }
        if (entry.Path == null) return null;
        using var image = File.OpenRead(entry.Path);
        const int maximumArtworkBytes = 12 * 1024 * 1024;
        if (image.Length is <= 0 or > maximumArtworkBytes) return null;
        var bytes = new byte[image.Length];
        image.ReadExactly(bytes);
        return bytes;
    }

    private static BitmapImage DecodeArtwork(Stream stream, int size)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.DecodePixelWidth = size;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
