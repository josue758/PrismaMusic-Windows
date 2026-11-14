using System;
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
    private readonly SemaphoreSlim artworkWorkers = new(2);

    private void SetGalleryRows(LibraryRow[] rows)
    {
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
        bool artist = Navigation.SelectedIndex == 2;
        await artworkWorkers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, row)) return;
            var bitmap = await Task.Run(() => ReadCollectionImage(row, artist, 160));
            if (image.IsLoaded && ReferenceEquals(image.DataContext, row)) image.Source = bitmap;
        }
        finally { artworkWorkers.Release(); }
    }

    private static BitmapImage ReadCollectionImage(LibraryRow page, bool artist, int size)
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
                using var file = TagLib.File.Create(track.Path);
                var cover = file.Tag.Pictures.FirstOrDefault();
                if (cover == null) continue;
                using var bytes = new MemoryStream(cover.Data.Data);
                return DecodeArtwork(bytes, size);
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.Message); }
        }
        return null;
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
