using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PrismaMusic;

internal static class WindowPlacement
{
    private const int NearestMonitor = 2;
    private const int CalculateClientArea = 0x0083;
    private const int RedrawClientArea = 0x0300;

    internal static void FitMaximizedClient(nint window) =>
        HwndSource.FromHwnd(window)?.AddHook(FitClientToWorkArea);

    private static nint FitClientToWorkArea(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != CalculateClientArea || !IsZoomed(window)) return 0;
        // In both forms of WM_NCCALCSIZE the first RECT is the proposed client area.
        // WindowChrome clips a maximized window to the work area but lays out the whole client.
        // Give WPF only the visible pixels so the playback bar and caption stay inside that clip.
        var proposed = Marshal.PtrToStructure<Bounds>(lParam);
        var monitor = MonitorFromRect(ref proposed, NearestMonitor);
        if (!TryGetMonitorInfo(monitor, out var info)) return 0;
        var work = info.WorkArea;
        var client = new Bounds(Math.Max(proposed.Left, work.Left), Math.Max(proposed.Top, work.Top),
            Math.Min(proposed.Right, work.Right), Math.Min(proposed.Bottom, work.Bottom));
        if (client.Left >= client.Right || client.Top >= client.Bottom) return 0;

        Marshal.StructureToPtr(client, lParam, false);
        handled = true;
        return wParam != 0 ? RedrawClientArea : 0;
    }

    internal static bool TryGetBounds(nint window, bool fullscreen, out Bounds bounds)
    {
        bool found = TryGetMonitorInfo(MonitorFromWindow(window, NearestMonitor), out var info);
        bounds = fullscreen ? info.Monitor : info.WorkArea;
        return found;
    }

    private static bool TryGetMonitorInfo(nint monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal record struct Bounds(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Bounds Monitor;
        internal Bounds WorkArea;
        internal int Flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromRect(ref Bounds bounds, int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
