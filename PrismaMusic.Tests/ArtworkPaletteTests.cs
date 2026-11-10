using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class ArtworkPaletteTests
{
    [Fact]
    public void CaptionAccentIsStableAndUsesFallbackWhenCaptionIsMissing()
    {
        Color first = ArtworkTile.AccentForCaption("A", Colors.Blue);
        Color second = ArtworkTile.AccentForCaption("A", Colors.Red);

        Assert.Equal(first, second);
        Assert.NotEqual(first, ArtworkTile.AccentForCaption("B", Colors.Blue));
        Assert.Equal(Colors.Blue, ArtworkTile.AccentForCaption(" ", Colors.Blue));
    }

    [Fact]
    public void ExtractAccentUsesTheDominantSaturatedArtworkColor()
    {
        byte[] pixels =
        {
            0, 0, 220, 255, 0, 0, 220, 255,
            0, 0, 220, 255, 80, 90, 100, 255
        };
        BitmapSource artwork = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        artwork.Freeze();

        Color accent = ArtworkPalette.ExtractAccent(artwork, Colors.Blue);

        Assert.True(accent.R > 200);
        Assert.True(accent.G < 100);
        Assert.True(accent.B < 100);
    }

    [Fact]
    public void ExtractAccentFallsBackForGrayscaleArtwork()
    {
        byte[] pixels = { 70, 70, 70, 255, 120, 120, 120, 255 };
        BitmapSource artwork = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        artwork.Freeze();

        Assert.Equal(Colors.CornflowerBlue, ArtworkPalette.ExtractAccent(artwork, Colors.CornflowerBlue));
    }
}
