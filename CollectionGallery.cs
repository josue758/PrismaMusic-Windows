using System;
using System.Collections.Generic;
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
        if (sender is not Image image || image.DataContext is not LibraryRow row) return;
        bool artist = LibrarySectionIndex == 2;
        await artworkWorkers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, row)) return;
            var bitmap = await LoadCollectionArtwork(row, artist, 160);
            if (image.IsLoaded && ReferenceEquals(image.DataContext, row)) image.Source = bitmap;
        }
        finally { artworkWorkers.Release(); }
    }

    private async void LoadTrackArtwork(object sender, RoutedEventArgs args)
    {
        if (sender is not Image image || image.DataContext is not LibraryRow { Tracks.Length: 1 } row) return;
        await artworkWorkers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, row)) return;
            var bitmap = await LoadCollectionArtwork(row, artist: false, 48);
            if (image.IsLoaded && ReferenceEquals(image.DataContext, row)) image.Source = bitmap;
        }
        finally { artworkWorkers.Release(); }
    }

    private async Task<BitmapImage> LoadCollectionArtwork(LibraryRow page, bool artist, int size)
    {
        var bitmap = await Task.Run(() => ReadCollectionImage(page, artist, size));
        if (bitmap != null) return bitmap;
        if (!autoArtwork || page.Tracks.All(IsDemo)) return null;
        if (artist)
        {
            try
            {
                if (await EnsureArtistPortrait(page.Name))
                    return await Task.Run(() => ReadCollectionImage(page, artist: true, size));
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Online artist portrait lookup failed: {error.Message}"); }
            return null;
        }

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
        if (artist)
        {
            try
            {
                var path = PortraitPath(page.Name);
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    return DecodeArtwork(stream, size);
                }
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.Message); }
        }
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

    private byte[] ReadArtworkPayload(string path)
    {
        return ReadArtworkPayload(path, () =>
        {
            using var file = TagLib.File.Create(path);
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
            byte[] embedded = readEmbedded();
            if (embedded == null) return null;
            try { database?.SaveArtwork(path, embedded); }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Artwork cache write failed: {error.Message}"); }
            return embedded;
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Artwork read failed: {error.Message}"); return null; }
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
