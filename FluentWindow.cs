using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private void UpdateWindowCaptionTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        int dark = lightTheme ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private bool ApplyWindowMaterial(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        UpdateWindowCaptionTheme(window);
        if (Environment.OSVersion.Version.Build < 22621 || SystemParameters.HighContrast ||
            HwndSource.FromHwnd(handle)?.CompositionTarget is not HwndTarget target) return false;

        int round = 2;
        DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        int mica = 2;
        if (DwmSetWindowAttribute(handle, 38, ref mica, sizeof(int)) != 0) return false;
        var margins = new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0) return false;

        target.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
        return true;
    }

    private void ApplyFluentDialog(Window window, System.Windows.Controls.Panel root)
    {
        window.Icon = Icon;
        window.FontFamily = FontFamily;
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SourceInitialized += (_, _) =>
        {
            if (ApplyWindowMaterial(window))
                root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "NavigationBackdrop");
        };
    }
}
