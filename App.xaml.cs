using System;
using System.Windows;
using System.Windows.Media;

namespace PrismaMusic;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Respeta "Mostrar animaciones" de Windows: sin movimiento, los cambios son instantáneos.
        if (!SystemParameters.ClientAreaAnimation)
        {
            foreach (var key in new[] { "MotionFast", "MotionPress", "MotionSlow" })
                Resources[key] = new Duration(TimeSpan.Zero);
        }

        // Color de acento del sistema, aclarado para el tema oscuro como hacen las aplicaciones Fluent.
        try
        {
            var system = SystemParameters.WindowGlassColor;
            var accent = Color.FromRgb(Lighten(system.R), Lighten(system.G), Lighten(system.B));
            double luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            Resources["OnAccentBrush"] = new SolidColorBrush(luminance > .5 ? Colors.Black : Colors.White);
        }
        catch (Exception) { }
        base.OnStartup(e);
    }

    private static byte Lighten(byte channel) => (byte)(channel + (255 - channel) * .35);
}
