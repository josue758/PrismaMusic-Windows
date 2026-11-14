using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed class PressProbe : Button
    {
        public void Press(bool pressed) => IsPressed = pressed;
    }

    private async Task VerifyButtonFeedback()
    {
        foreach (Style style in new[] { (Style)FindResource(typeof(Button)), (Style)FindResource("PrimaryTransport") })
        {
            var button = new PressProbe { Style = style, Content = "▶", Width = 40, Height = 32,
                Opacity = 0, IsHitTestVisible = false };
            Shell.Children.Add(button);
            try
            {
                button.ApplyTemplate();
                var root = (FrameworkElement)button.Template.FindName("Root", button);
                var shade = (FrameworkElement)button.Template.FindName("PressShade", button);
                double Scale() => ((ScaleTransform)root.RenderTransform).ScaleX;
                button.Press(true);
                await Task.Delay(220);
                if (Math.Abs(Scale() - MotionSettings.PressedScale) > .005 || shade.Opacity < .12)
                    throw new InvalidOperationException($"Press feedback did not settle: {Scale():F3}, {shade.Opacity:F3}.");
                button.Press(false);
                await Task.Delay(260);
                if (Math.Abs(Scale() - 1) > .005 || shade.Opacity > .005)
                    throw new InvalidOperationException("Released button retained its pressed appearance.");
            }
            finally { Shell.Children.Remove(button); }
        }
        Console.WriteLine("Button and transport press/release verified.");
    }
}

