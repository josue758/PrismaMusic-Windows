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
        foreach (var control in new FrameworkElement[] { Navigation, Rows, Search, PlayButton, ShuffleButton, RepeatButton, Seek, Volume })
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
        Console.WriteLine("UI Automation: eight named keyboard controls, seek range and play invoke verified.");
    }

    private async Task VerifySmbPlayback(string directory)
    {
        var scan = await Task.Run(() => ReadFolder(directory));
        if (scan.Errors != 0 || scan.Tracks.Count != 3 || !scan.Tracks.All(track => track.Path.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new InvalidOperationException("SMB demo scan failed.");
        Volume.Value = 0;
        await Play(scan.Tracks[0], false);
        for (int attempt = 0; attempt < 25 && player.Position.TotalMilliseconds < 400; attempt++) await Task.Delay(100);
        if (!opened || current != scan.Tracks[0] || player.Position.TotalMilliseconds < 400)
            throw new InvalidOperationException($"SMB audio did not start. {Status.Text} ({player.Position})");
        await Play(scan.Tracks[1], false);
        if (!opened || current != scan.Tracks[1]) throw new InvalidOperationException("Changing tracks over SMB failed.");
        player.Pause();
        SetPlaying(false);
        Console.WriteLine("SMB loopback: scanned three tracks, played one and switched tracks.");

        string unavailable = Path.Combine(directory, "unavailable-" + Guid.NewGuid().ToString("N"));
        var retained = scan.Tracks[0] with { Path = Path.Combine(unavailable, "retained.m4a") };
        var savedLibrary = library;
        try
        {
            library = new Library { Tracks = scan.Tracks.Append(retained).ToList() };
            await Scan(new[] { unavailable }, false);
            if (!library.Tracks.Contains(retained) || library.Tracks.Count != 4)
                throw new InvalidOperationException("An unavailable SMB folder pruned the library.");
        }
        finally
        {
            library = savedLibrary;
            RefreshFilters();
            RefreshRows();
        }
        Console.WriteLine("SMB rescan: unavailable folder kept existing library references.");
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
}

