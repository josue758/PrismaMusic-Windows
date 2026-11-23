using System;
using System.Linq;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool discoverNewMusic;
    private DispatcherTimer libraryDiscoveryTimer;

    private void StartLibraryDiscovery()
    {
        libraryDiscoveryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(90) };
        libraryDiscoveryTimer.Tick += async (_, _) =>
        {
            if (scanning || relocatingSource || editingAlbum || library.Folders.Count == 0) return;
            try { await Scan(library.Folders.ToArray(), add: false, onlyNew: true, background: true); }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine($"Background library scan failed: {error.Message}"); }
        };
        Closed += (_, _) => libraryDiscoveryTimer.Stop();
        UpdateLibraryDiscovery();
    }

    private void UpdateLibraryDiscovery()
    {
        if (libraryDiscoveryTimer != null) libraryDiscoveryTimer.IsEnabled = discoverNewMusic;
    }
}
