using System;
using System.Windows;
using System.Windows.Media;

namespace PrismaMusic;

public sealed class ArtworkTile : FrameworkElement
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(ArtworkTile),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    private static readonly (Color Light, Color Dark)[] Palettes =
    {
        (Color.FromRgb(97, 101, 196), Color.FromRgb(29, 30, 65)),
        (Color.FromRgb(199, 107, 133), Color.FromRgb(65, 26, 52)),
        (Color.FromRgb(55, 151, 145), Color.FromRgb(16, 48, 58)),
        (Color.FromRgb(190, 132, 72), Color.FromRgb(65, 36, 38))
    };

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        int key = 0;
        foreach (char letter in Caption ?? "") key = unchecked(key * 31 + char.ToUpperInvariant(letter));
        var palette = Palettes[(int)((uint)key % Palettes.Length)];
        var bounds = new Rect(RenderSize);
        double radius = Math.Min(ActualWidth, ActualHeight) * .07;
        var clip = new RectangleGeometry(bounds, radius, radius);
        context.PushClip(clip);
        context.DrawRectangle(new LinearGradientBrush(palette.Light, palette.Dark, 138), null, bounds);
        var center = new Point(ActualWidth * .69, ActualHeight * .55);
        double disc = Math.Min(ActualWidth, ActualHeight) * .43;
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 12, 12, 26)), null, center, disc, disc);
        var ring = new Pen(new SolidColorBrush(Color.FromArgb(76, 239, 239, 255)), Math.Max(.45, disc * .008));
        for (int groove = 0; groove < 6; groove++)
        {
            double size = disc * (1 - groove * .115);
            context.DrawEllipse(null, ring, center, size, size);
        }
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 229, 223, 251)), null, center, disc * .21, disc * .21);
        context.DrawEllipse(new SolidColorBrush(palette.Dark), null, center, disc * .05, disc * .05);
        context.Pop();
    }
}
