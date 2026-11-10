using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

internal static class ArtworkPalette
{
    public static Color ExtractAccent(BitmapSource source, Color fallback)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var buckets = new Dictionary<int, (double Score, double Red, double Green, double Blue)>();
        int stepX = Math.Max(1, bitmap.PixelWidth / 48);
        int stepY = Math.Max(1, bitmap.PixelHeight / 48);
        for (int y = 0; y < bitmap.PixelHeight; y += stepY)
        for (int x = 0; x < bitmap.PixelWidth; x += stepX)
        {
            int offset = y * stride + x * 4;
            if (pixels[offset + 3] < 128) continue;

            byte blue = pixels[offset];
            byte green = pixels[offset + 1];
            byte red = pixels[offset + 2];
            int maximum = Math.Max(red, Math.Max(green, blue));
            int minimum = Math.Min(red, Math.Min(green, blue));
            if (maximum < 42 || maximum > 248 || maximum - minimum < 28) continue;

            double score = (maximum - minimum) / (double)maximum;
            int key = (red >> 4) << 8 | (green >> 4) << 4 | (blue >> 4);
            buckets.TryGetValue(key, out var bucket);
            buckets[key] = (bucket.Score + score, bucket.Red + red * score,
                bucket.Green + green * score, bucket.Blue + blue * score);
        }

        if (buckets.Count == 0) return fallback;
        var dominant = buckets.Values.MaxBy(bucket => bucket.Score);
        byte Lift(double channel) => (byte)Math.Clamp(channel / dominant.Score * .82 + 255 * .18, 0, 255);
        return Color.FromRgb(Lift(dominant.Red), Lift(dominant.Green), Lift(dominant.Blue));
    }
}
