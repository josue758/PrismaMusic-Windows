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
        if (style == "aurora" && Peak > 0 && animate)
        {
            phase = (phase + 1.05 / fps) % (Math.PI * 2);
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
        fill = new LinearGradientBrush(light, color, 90);
        reflection = new LinearGradientBrush(Color.FromArgb(58, color.R, color.G, color.B), Colors.Transparent, 90);
        fill.Freeze();
        reflection.Freeze();
        ribbons = new Pen[6];
        for (int i = 0; i < ribbons.Length; i++)
        {
            byte opacity = (byte)(60 + i * 25);
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
        if (style == "aurora")
        {
            DrawAurora(drawing, width, height);
            return;
        }

        double pitch = width / BandCount;
        double barWidth = Math.Min(18, pitch * .62);
        bool centered = style == "centered";
        bool mirrored = style == "mirror";
        double baseline = height * (centered ? .5 : mirrored ? .63 : .86);
        for (int i = 0; i < BandCount; i++)
        {
            double barHeight = 3 + levels[i] * height * (centered ? .72 : mirrored ? .54 : .72);
            double x = (i + .5) * pitch - barWidth / 2;
            double y = baseline - barHeight * (centered ? .5 : 1);
            drawing.DrawRoundedRectangle(fill, null, new Rect(x, y, barWidth, barHeight), 3, 3);
            if (mirrored)
                drawing.DrawRoundedRectangle(reflection, null, new Rect(x, baseline + 5, barWidth, barHeight * .48), 3, 3);
        }
    }

    private void DrawAurora(DrawingContext drawing, double width, double height)
    {
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
            drawing.DrawGeometry(null, ribbons[ribbon], path);
        }
    }

    private double RibbonY(int band, int ribbon, double height)
    {
        double position = band / (BandCount - 1d);
        double envelope = Math.Sin(position * Math.PI);
        double wave = Math.Sin(position * Math.PI * 4 - phase + ribbon * .48);
        return height * (.51 + (ribbon - 2.5) * .026 + envelope * wave * (.06 + levels[band] * .27));
    }
}
