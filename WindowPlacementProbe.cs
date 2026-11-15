using System;
using System.Windows;
using System.Windows.Interop;

namespace PrismaMusic;

public partial class MainWindow
{
    private void VerifyWindowPlacement()
    {
        WindowState previous = WindowState;
        try
        {
            WindowState = WindowState.Maximized;
            UpdateLayout();
            VerifyVisibleBounds(this, Shell, fullscreen: false);
            CaptureDesignPreview(Shell, "PRISMA_MAXIMIZED_PREVIEW");
            Fullscreen(this, new RoutedEventArgs());
            var room = (FrameworkElement)listeningRoom.Content;
            room.UpdateLayout();
            VerifyVisibleBounds(listeningRoom, room, fullscreen: true);
            CaptureDesignPreview(room, "PRISMA_FULLSCREEN_PLACEMENT_PREVIEW");
        }
        finally
        {
            listeningRoom?.Close();
            WindowState = previous;
        }
        if (!ReferenceEquals(Transport.Parent, Shell)) throw new InvalidOperationException("The player bar did not return to the library.");
        Console.WriteLine("Window placement: library inside the work area, listening room inside full monitor, player bar fully visible in both.");
    }

    private void VerifyVisibleBounds(Window window, FrameworkElement root, bool fullscreen)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!WindowPlacement.TryGetBounds(handle, fullscreen, out var bounds))
            throw new InvalidOperationException("Could not read the display bounds.");

        Point start = root.PointToScreen(new Point());
        Point end = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
        Point playerStart = Transport.PointToScreen(new Point());
        Point playerEnd = Transport.PointToScreen(new Point(Transport.ActualWidth, Transport.ActualHeight));
        const double tolerance = 2;
        if (Math.Abs(start.X - bounds.Left) > tolerance || Math.Abs(start.Y - bounds.Top) > tolerance ||
            Math.Abs(end.X - bounds.Right) > tolerance || Math.Abs(end.Y - bounds.Bottom) > tolerance ||
            playerStart.X < bounds.Left - tolerance || playerStart.Y < bounds.Top - tolerance ||
            playerEnd.X > bounds.Right + tolerance || playerEnd.Y > bounds.Bottom + tolerance)
            throw new InvalidOperationException($"Window placement (fullscreen: {fullscreen}) expected {bounds}, got {start} to {end}. Player ends at {playerEnd}.");
    }
}
