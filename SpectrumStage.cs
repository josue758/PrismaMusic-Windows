using System;
using System.Windows;
using System.Windows.Media;

namespace PrismaMusic;

internal sealed class SpectrumStage : FrameworkElement
{
    internal const int BandCount = 48;
    private readonly double[] levels = new double[BandCount];
    private Brush fill;
    private Brush reflection;
    private Pen[] ribbons;
    private Pen ribbonGlow;
    private Pen guide;
    private Pen orbitSpoke;
    private Pen orbitTrail;
    private Brush fieldGlow;
    private Brush star;
    private Color accent;
    private string style;
    private double phase;
    private NebulaField nebula;

    internal double Peak { get; private set; }

    internal void Update(float[] bands, bool playing, double sensitivity, int fps, string nextStyle, Color nextAccent, bool animate)
    {
        bool changed = style != nextStyle || fill == null || accent != nextAccent;
        if (fill == null || accent != nextAccent) SetPalette(nextAccent);
        style = nextStyle;
        Peak = 0;
        double decay = Math.Pow(.72, 30d / fps);
        for (int i = 0; i < BandCount; i++)
        {
            double target = 0;
            if (playing && bands.Length > 0)
            {
                double position = i * (bands.Length - 1d) / (BandCount - 1);
                int lower = (int)position;
                double value = bands[lower] + (bands[Math.Min(lower + 1, bands.Length - 1)] - bands[lower]) * (position - lower);
                target = Math.Clamp(value * sensitivity, 0, 1);
            }
            double level = target >= levels[i] ? target : target + (levels[i] - target) * decay;
            if (level < .002) level = 0;
            changed |= Math.Abs(level - levels[i]) > .001;
            levels[i] = level;
            Peak = Math.Max(Peak, level);
        }
        if ((style == "aurora" || style == "orbit") && Peak > 0 && animate)
        {
            phase = (phase + (style == "orbit" ? .29 + Peak * .42 : 1.05) / fps) % Math.Tau;
            changed = true;
        }
        if (style == "nebula")
        {
            nebula ??= new NebulaField();
            nebula.SetAccent(nextAccent);
            if (playing && Peak > 0 && animate)
            {
                nebula.Advance(levels, fps);
                changed = true;
            }
        }
        if (changed) InvalidateVisual();
    }

    private void SetPalette(Color color)
    {
        accent = color;
        var light = Color.FromRgb((byte)((color.R + 255) / 2), (byte)((color.G + 255) / 2), (byte)((color.B + 255) / 2));
        fill = new LinearGradientBrush(light, Color.FromArgb(150, color.R, color.G, color.B), 90);
        reflection = new LinearGradientBrush(Color.FromArgb(102, color.R, color.G, color.B), Colors.Transparent, 90);
        fieldGlow = new RadialGradientBrush(Color.FromArgb(98, color.R, color.G, color.B),
            Color.FromArgb(0, color.R, color.G, color.B));
        star = new RadialGradientBrush(light, Color.FromArgb(0, color.R, color.G, color.B));
        fill.Freeze();
        reflection.Freeze();
        fieldGlow.Freeze();
        star.Freeze();
        guide = CreatePen(color, 36, 1);
        ribbonGlow = CreatePen(color, 33, 11);
        orbitSpoke = CreatePen(light, 230, 2.5);
        orbitTrail = CreatePen(color, 45, 7);
        ribbons = new Pen[6];
        for (int i = 0; i < ribbons.Length; i++)
        {
            byte opacity = (byte)(92 + i * 28);
            Color tone = i % 2 == 0 ? light : color;
            ribbons[i] = new Pen(new SolidColorBrush(Color.FromArgb(opacity, tone.R, tone.G, tone.B)), i == 5 ? 2.2 : 1.25)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            ribbons[i].Freeze();
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double width = ActualWidth;
        double height = ActualHeight;
        if (fill == null || width < BandCount || height < 16) return;
        if (style == "nebula")
        {
            nebula.Draw(drawing, width, height, levels, Peak);
            return;
        }
        if (style == "orbit")
        {
            DrawOrbit(drawing, width, height);
            return;
        }
        if (style == "aurora")
        {
            DrawAurora(drawing, width, height);
            return;
        }

        bool centered = style == "centered";
        bool mirrored = style == "mirror";
        double left = width * .05;
        double span = width - left * 2;
        double pitch = span / BandCount;
        double barWidth = Math.Min(27, pitch * .66);
        double baseline = height * (centered ? .47 : mirrored ? .54 : .66);
        double corner = Math.Min(5, barWidth * .29);
        drawing.DrawEllipse(fieldGlow, null, new Point(width * .5, baseline - height * .08), width * .58, height * .42);
        drawing.DrawLine(guide, new Point(left, baseline), new Point(width - left, baseline));
        for (int i = 0; i < BandCount; i++)
        {
            double barHeight = 4 + levels[i] * height * (centered ? .51 : mirrored ? .38 : .47);
            double x = left + (i + .5) * pitch - barWidth / 2;
            double y = baseline - barHeight * (centered ? .5 : 1);
            drawing.DrawRoundedRectangle(fill, null, new Rect(x, y, barWidth, barHeight), corner, corner);
            if (mirrored)
                drawing.DrawRoundedRectangle(reflection, null, new Rect(x, baseline + 5, barWidth, barHeight * .52), corner, corner);
        }
    }

    private static Pen CreatePen(Color color, byte opacity, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B)), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private void DrawOrbit(DrawingContext drawing, double width, double height)
    {
        double scale = Math.Min(width, height);
        double radius = scale * .198;
        var center = new Point(width * .51, height * .46);
        for (int ring = 1; ring <= 3; ring++)
        {
            double distance = radius * (.42 + ring * .22);
            drawing.DrawEllipse(null, guide, center, distance, distance);
        }
        drawing.DrawEllipse(fieldGlow, null, center, radius * 2.45, radius * 1.95);
        for (int i = 0; i < BandCount; i++)
        {
            double angle = i * Math.Tau / BandCount + phase;
            double length = scale * (.014 + levels[i] * .118);
            var spoke = new Vector(Math.Cos(angle), Math.Sin(angle));
            var inner = center + spoke * radius;
            var outer = center + spoke * (radius + length);
            drawing.DrawLine(orbitTrail, inner, outer);
            drawing.DrawLine(orbitSpoke, inner, outer);
            if (i % 4 == 0)
            {
                double halo = scale * (.004 + levels[i] * .009);
                drawing.DrawEllipse(star, null, outer, halo, halo);
            }
        }
        for (int i = 0; i < 3; i++)
        {
            double angle = i * Math.Tau / 3 - phase * 1.2;
            var point = center + new Vector(Math.Cos(angle), Math.Sin(angle)) * (radius * .71);
            double halo = scale * (.013 + levels[i * 8] * .010);
            drawing.DrawEllipse(star, null, point, halo, halo);
        }
    }

    private void DrawAurora(DrawingContext drawing, double width, double height)
    {
        drawing.DrawEllipse(fieldGlow, null, new Point(width * .51, height * .47), width * .61, height * .40);
        double pitch = width / (BandCount - 1);
        for (int ribbon = 0; ribbon < ribbons.Length; ribbon++)
        {
            var path = new StreamGeometry();
            using (var line = path.Open())
            {
                double previous = RibbonY(0, ribbon, height);
                line.BeginFigure(new Point(0, previous), isFilled: false, isClosed: false);
                for (int i = 1; i < BandCount; i++)
                {
                    double y = RibbonY(i, ribbon, height);
                    double middle = (i - .5) * pitch;
                    line.BezierTo(new Point(middle, previous), new Point(middle, y), new Point(i * pitch, y), true, false);
                    previous = y;
                }
            }
            path.Freeze();
            drawing.DrawGeometry(null, ribbonGlow, path);
            drawing.DrawGeometry(null, ribbons[ribbon], path);
        }
    }

    private double RibbonY(int band, int ribbon, double height)
    {
        double position = band / (BandCount - 1d);
        double envelope = Math.Sin(position * Math.PI);
        double wave = Math.Sin(position * Math.PI * 4 - phase + ribbon * .48);
        return height * (.47 + (ribbon - 2.5) * .033 + envelope * wave * (.065 + levels[band] * .245));
    }
}
