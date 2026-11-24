using System;
using System.Windows;
using System.Windows.Media;

namespace PrismaMusic;

/// <summary>Draws a three-dimensional particle field from the current spectrum.</summary>
internal sealed class NebulaField
{
    private readonly record struct Particle(double Angle, double Depth, double Orbit, double Size, int Tone, int Band, int Node);
    private readonly Particle[] particles;
    private readonly Brush[] glows = new Brush[6];
    private readonly Pen[] trails = new Pen[6];
    private Brush[] clouds;
    private Brush core;
    private Color accent;
    private double phase = .7;
    private double travel = .08;

    internal NebulaField()
    {
        var random = new Random(2073);
        particles = new Particle[144];
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i] = new Particle(random.NextDouble() * Math.Tau, random.NextDouble(),
                .54 + random.NextDouble() * .82, .46 + Math.Pow(random.NextDouble(), 2) * 1.15,
                random.Next(glows.Length), random.Next(SpectrumStage.BandCount), random.Next(3));
        }
    }

    internal void SetAccent(Color color)
    {
        if (core != null && accent == color) return;
        accent = color;
        Color[] palette =
        {
            Blend(color, Color.FromRgb(255, 81, 101), .59),
            Blend(color, Color.FromRgb(250, 159, 133), .49),
            Blend(color, Color.FromRgb(242, 165, 229), .64),
            Blend(color, Color.FromRgb(178, 242, 175), .73),
            Blend(color, Color.FromRgb(255, 218, 145), .72),
            Blend(color, Color.FromRgb(228, 230, 255), .79)
        };
        for (int i = 0; i < palette.Length; i++)
        {
            var tone = palette[i];
            glows[i] = Glow(Blend(tone, Colors.White, .36), tone, 235, 134);
            trails[i] = new Pen(new SolidColorBrush(Alpha(tone, 40)), 1);
            trails[i].Freeze();
        }
        clouds = new[]
        {
            Glow(Blend(color, Color.FromRgb(182, 72, 99), .42), color, 93, 31),
            Glow(Blend(color, Color.FromRgb(125, 94, 194), .57), color, 60, 19),
            Glow(Blend(color, Color.FromRgb(232, 125, 98), .53), color, 68, 21)
        };
        core = Glow(Colors.White, palette[2], 255, 186);
    }

    internal void Advance(double[] bands, int fps)
    {
        double bass = Bass(bands);
        phase = (phase + (.18 + bass * .46) / fps) % Math.Tau;
        travel = (travel + (.045 + bass * .115) / fps) % 1;
    }

    internal void Draw(DrawingContext drawing, double width, double height, double[] levels, double peak)
    {
        double unit = Math.Min(width, height);
        double bass = Bass(levels);
        // Three points of light drift behind the transport. Small particles converge and then spread outward.
        var first = new Point(width * (.38 + Math.Sin(phase) * .041), height * (.49 + Math.Cos(phase) * .049));
        var second = new Point(width * (.67 + Math.Cos(phase * 1.2) * .055), height * (.35 + Math.Sin(phase) * .07));
        var third = new Point(width * (.58 + Math.Sin(phase + 1) * .089), height * (.67 + Math.Cos(phase) * .04));
        DrawCloud(first, .77, clouds[0]);
        DrawCloud(second, .60, clouds[1]);
        DrawCloud(third, .73, clouds[2]);

        foreach (var particle in particles)
        {
            Point center = particle.Node == 0 ? first : particle.Node == 1 ? second : third;
            double depth = (particle.Depth + travel) % 1;
            double signal = peak > .001 ? .48 + .52 * levels[particle.Band] : .45;
            double distance = (.025 + depth * depth * .74) * particle.Orbit;
            double angle = particle.Angle + Math.Sin(phase + particle.Depth * 5) * .16;
            double dx = Math.Cos(angle) * width * distance;
            double dy = Math.Sin(angle) * height * distance * 1.23;
            var point = new Point(center.X + dx, center.Y + dy);
            if (point.X is < -30 || point.X > width + 30 || point.Y is < -30 || point.Y > height + 30) continue;
            double radius = Math.Max(1, unit * (.0035 + .012 * depth) * particle.Size * (.70 + signal * .65));
            double edge = Math.Min(1, Math.Min(depth * 10, (1 - depth) * 8));
            drawing.PushOpacity(Math.Clamp(edge * (.40 + signal * .55), 0, 1));
            if (depth > .31 && particle.Band % 4 == 0)
            {
                double tail = .17 + bass * .15;
                drawing.DrawLine(trails[particle.Tone], new Point(point.X - dx * tail, point.Y - dy * tail), point);
            }
            drawing.DrawEllipse(glows[particle.Tone], null, point, radius, radius * (.73 + .27 * depth));
            drawing.Pop();
        }

        DrawCore(first, 1.18);
        DrawCore(second, .78);
        DrawCore(third, .93);

        void DrawCloud(Point point, double size, Brush brush) =>
            drawing.DrawEllipse(brush, null, point, width * size, height * (.39 + size * .25));

        void DrawCore(Point point, double size)
        {
            double radius = unit * .048 * size * (.76 + bass * .7);
            drawing.PushOpacity(.64 + peak * .31);
            drawing.DrawEllipse(glows[2], null, point, radius * 1.55, radius * 1.55);
            drawing.DrawEllipse(core, null, point, radius, radius);
            drawing.Pop();
        }
    }

    private static double Bass(double[] levels)
    {
        double total = 0;
        for (int i = 0; i < 10; i++) total += levels[i];
        return Math.Clamp(total / 10, 0, 1);
    }

    private static RadialGradientBrush Glow(Color inner, Color outer, byte center, byte halo)
    {
        var brush = new RadialGradientBrush { GradientStops = new GradientStopCollection
        {
            new GradientStop(Alpha(inner, center), 0),
            new GradientStop(Alpha(inner, (byte)(center * .96)), .21),
            new GradientStop(Alpha(outer, halo), .42),
            new GradientStop(Alpha(outer, (byte)(halo * .28)), .66),
            new GradientStop(Alpha(outer, 0), 1)
        } };
        brush.Freeze();
        return brush;
    }

    private static Color Alpha(Color color, byte value) => Color.FromArgb(value, color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount), (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));
}
