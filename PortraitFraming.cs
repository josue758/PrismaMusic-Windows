using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Security.Cryptography;

namespace PrismaMusic;

internal static class PortraitFraming
{
    public static async Task<byte[]> CenterOnLargestFace(byte[] encoded)
    {
        BitmapSource original;
        using (var stream = new MemoryStream(encoded))
        {
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            original = decoder.Frames[0];
            original.Freeze();
        }

        if (!FaceDetector.IsSupported) return encoded;
        double scale = Math.Min(1, 512d / Math.Max(original.PixelWidth, original.PixelHeight));
        var detectionImage = new TransformedBitmap(original, new ScaleTransform(scale, scale));
        var bgra = new FormatConvertedBitmap(detectionImage, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(pixels), BitmapPixelFormat.Bgra8,
            bgra.PixelWidth, bgra.PixelHeight, BitmapAlphaMode.Straight);
        var detector = await FaceDetector.CreateAsync();
        var faces = await detector.DetectFacesAsync(bitmap);
        var face = faces.OrderByDescending(item => item.FaceBox.Width * item.FaceBox.Height).FirstOrDefault();
        if (face == null) return encoded;

        double ratioX = original.PixelWidth / (double)bgra.PixelWidth;
        double ratioY = original.PixelHeight / (double)bgra.PixelHeight;
        double faceWidth = face.FaceBox.Width * ratioX;
        double faceHeight = face.FaceBox.Height * ratioY;
        double side = Math.Min(Math.Min(original.PixelWidth, original.PixelHeight),
            Math.Max(faceWidth, faceHeight) * 2.8);
        int cropSide = Math.Max(1, (int)Math.Round(side));
        double centerX = (face.FaceBox.X + face.FaceBox.Width / 2d) * ratioX;
        double centerY = (face.FaceBox.Y + face.FaceBox.Height / 2d + face.FaceBox.Height * .12) * ratioY;
        int left = Math.Clamp((int)Math.Round(centerX - cropSide / 2d), 0, original.PixelWidth - cropSide);
        int top = Math.Clamp((int)Math.Round(centerY - cropSide / 2d), 0, original.PixelHeight - cropSide);

        var cropped = new CroppedBitmap(original, new Int32Rect(left, top, cropSide, cropSide));
        var output = new PngBitmapEncoder();
        output.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(cropped));
        using var result = new MemoryStream();
        output.Save(result);
        return result.ToArray();
    }
}
