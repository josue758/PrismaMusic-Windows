using System;
using System.Windows;

namespace PrismaMusic;

public static class MotionSettings
{
    public static Duration Fast => DurationFor(140);
    public static Duration Press => DurationFor(80);
    public static Duration Slow => DurationFor(220);
    public static double PressedScale => SystemParameters.ClientAreaAnimation ? .97 : 1;
    public static double ThumbHoverScale => SystemParameters.ClientAreaAnimation ? 1.2 : 1;

    private static Duration DurationFor(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0));
}
