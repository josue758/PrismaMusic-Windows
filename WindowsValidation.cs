using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using NAudio.CoreAudioApi;

namespace PrismaMusic;

public partial class MainWindow
{
    private void VerifyAutomationPeers()
    {
        foreach (var control in new FrameworkElement[]
        {
            Navigation, Rows, Search, PlayButton, ShuffleButton, RepeatButton, Seek, Volume,
            TransportArtworkButton, NewPlaylistButton, AudioOutputButton, ListeningMenuButton,
            FavoriteToolbarButton, MuteButton
        })
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(control);
            if (peer == null || string.IsNullOrWhiteSpace(peer.GetName()) || !peer.IsKeyboardFocusable())
                throw new InvalidOperationException($"Automation control is unnamed or unavailable by keyboard: {control.Name}");
        }
        var seekPeer = UIElementAutomationPeer.CreatePeerForElement(Seek);
        if (seekPeer.GetPattern(PatternInterface.RangeValue) == null)
            throw new InvalidOperationException("Seek does not expose the range-value automation pattern.");
        var playPeer = UIElementAutomationPeer.CreatePeerForElement(PlayButton);
        if (playPeer.GetPattern(PatternInterface.Invoke) == null)
            throw new InvalidOperationException("Play does not expose the invoke automation pattern.");
        Console.WriteLine("UI Automation: fourteen named keyboard controls, seek range and play invoke verified.");
    }

    private async Task VerifySmbPlayback(string directory)
    {
        string source = NetworkMusicSource.Normalize(directory)
            ?? throw new InvalidOperationException("Windows did not resolve the test path to a network share.");
        var originalLibrary = library;
        string originalSource = selectedLibrarySource;
        try
        {
            library = new Library();
            await Scan(new[] { source }, add: true);
            if (library.Tracks.Count != 3 || library.Folders.Single() != source)
                throw new InvalidOperationException($"SMB scan did not index the demo source. {Status.Text}");

            var directoryForDatabase = Environment.GetEnvironmentVariable("PRISMA_SMB_TEST_DATABASE");
            if (!string.IsNullOrEmpty(directoryForDatabase))
            {
                var probeDatabase = new LibraryDatabase(directoryForDatabase);
                probeDatabase.Save(library, tracksChanged: true);
                library = probeDatabase.Load();
                if (library.Tracks.Count != 3 || library.Folders.Single() != source)
                    throw new InvalidOperationException("Reloading the SMB library from SQLite lost the source or tracks.");
            }

            ShowLibrarySource(source);
            var rows = Rows.Items.OfType<LibraryRow>().ToArray();
            if (rows.Length != 3 || !rows.All(row => IsTrackInSource(row.Tracks.Single().Path, source)))
                throw new InvalidOperationException("The NAS source did not display its tracks.");
            Volume.Value = 0;
            Track first = rows[0].Tracks.Single();
            await Play(first, false);
            for (int attempt = 0; attempt < 25 && player.Position.TotalMilliseconds < 400; attempt++) await Task.Delay(100);
            if (!opened || current != first || player.Position.TotalMilliseconds < 400)
                throw new InvalidOperationException($"SMB audio did not start. {Status.Text} ({player.Position})");
            Track second = rows[1].Tracks.Single();
            await Play(second, false);
            if (!opened || current != second) throw new InvalidOperationException("Changing tracks over SMB failed.");
            player.Pause();
            SetPlaying(false);
            Console.WriteLine("SMB: indexed, persisted and displayed three tracks, played one and switched tracks.");

            var before = library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
            await Scan(new[] { source }, add: false);
            if (library.Tracks.Count != 3 || !library.Tracks.All(track => ReferenceEquals(before[track.Path], track)))
                throw new InvalidOperationException("SMB rescan reread unchanged tracks.");
            string unavailable = Path.Combine(source, "unavailable-" + Guid.NewGuid().ToString("N"));
            var retained = first with { Path = Path.Combine(unavailable, "retained.m4a") };
            library.Tracks.Add(retained);
            await Scan(new[] { unavailable }, false);
            if (!library.Tracks.Contains(retained) || library.Tracks.Count != 4)
                throw new InvalidOperationException("An unavailable SMB folder pruned the library.");
            Console.WriteLine("SMB: incremental rescan reused unchanged tracks and retained unavailable files.");
        }
        finally
        {
            library = originalLibrary;
            selectedLibrarySource = originalSource;
            RefreshLibrarySources();
            RefreshFilters();
            RefreshRows();
        }
    }

    private async Task VerifyUsbPcm(string deviceName, string path)
    {
        using var devices = new MMDeviceEnumerator();
        string id = null;
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
                if (device.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    id = device.ID;
        }
        if (id == null) throw new InvalidOperationException("USB audio endpoint unavailable.");
        var previousOutput = player.OutputId;
        try
        {
            player.OutputId = id;
            player.StrictDirect = true;
            opened = false;
            player.Open(new Uri(path));
            if (!opened || !player.IsExclusive || player.OutputNotice?.Contains("PCM directo") != true)
                throw new InvalidOperationException("The USB endpoint did not accept the original PCM format.");
            player.Play();
            for (int attempt = 0; attempt < 20 && player.DevicePositionBytes <= 0; attempt++) await Task.Delay(50);
            if (player.DevicePositionBytes <= 0) throw new InvalidOperationException("The USB audio clock did not advance.");
            Console.WriteLine($"USB {deviceName}: {player.OutputNotice}; device clock advanced.");
        }
        finally
        {
            player.Close();
            opened = false;
            player.StrictDirect = false;
            player.OutputId = previousOutput;
            SetPlaying(false);
        }
    }

    private async Task VerifyUsbDisconnect(string deviceName, string path)
    {
        string id = null;
        using var devices = new MMDeviceEnumerator();
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device)
                if (device.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    id = device.ID;
        if (id == null) throw new InvalidOperationException("USB audio endpoint unavailable.");
        Volume.Value = 0;
        player.OutputId = id;
        player.Exclusive = true;
        var track = new Track(path, "USB audio validation", "", "", "", "", 0, 0, 0, 0);
        await Play(track);
        for (int attempt = 0; attempt < 40 && player.DevicePositionBytes <= 0; attempt++) await Task.Delay(50);
        if (!opened || !playing || player.ActiveOutputId != id || !player.IsExclusive || player.DevicePositionBytes <= 0)
            throw new InvalidOperationException("USB WASAPI exclusive playback did not start: " + Status.Text);
        Console.WriteLine("READY: USB WASAPI exclusive; device clock advanced. Disconnect the DAC.");
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            using var fallback = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (fallback.ID != id && opened && playing && player.OutputId == id &&
                player.ActiveOutputId == fallback.ID && player.DevicePositionBytes > 0)
            {
                Console.WriteLine($"PASS: playback recovered on {fallback.FriendlyName} at {player.Position.TotalSeconds:0.00} s; requested DAC was retained.");
                return;
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException("USB playback did not recover before the deadline: " + Status.Text);
    }
}
