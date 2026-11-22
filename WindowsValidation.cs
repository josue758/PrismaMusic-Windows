using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyLibraryOperations(string root, Track fixture)
    {
        const int librarySize = 12000;
        const int files = 1200;
        string source = Path.Combine(root, "old"), destination = Path.Combine(root, "new");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        var originalLibrary = library;
        var originalDatabase = database;
        try
        {
            var sourceFile = Path.Combine(source, "one.m4a");
            var destinationFile = Path.Combine(destination, "one.m4a");
            File.Copy(fixture.Path, sourceFile);
            File.Copy(fixture.Path, destinationFile);
            var sourceTrack = LibraryScanner.ReadFile(sourceFile);
            library = new Library
            {
                Folders = new() { source },
                Tracks = Enumerable.Range(0, librarySize).Select(index => fixture with
                {
                    Path = Path.Combine(root, "synthetic", $"track-{index:D5}.flac"),
                    Name = $"Canción {index:D5}", Album = $"Álbum {index / 12:D4}", Artist = $"Artista {index % 57:D2}", AlbumArtist = "Prisma"
                }).Append(sourceTrack).ToList()
            };
            library.Favorites.Add(sourceFile);
            library.Playlist.Add(sourceFile);
            library.Playlists["Prueba"] = new() { sourceFile };
            library.History.Add(sourceFile);
            library.PlayCounts[sourceFile] = 6;
            var originalEdit = new LibraryTagEdit(sourceTrack.Name, sourceTrack.Artist, sourceTrack.Album, sourceTrack.Genre, sourceTrack.Year);
            library.TagEdits[sourceFile] = originalEdit;
            try
            {
                LibraryRelocation.Verify(source, destination, library.Tracks.Select(track => track.Path), _ => false);
                throw new InvalidOperationException("Relocation accepted a missing file.");
            }
            catch (IOException) { }
            var mapping = LibraryRelocation.Verify(source, destination, library.Tracks.Select(track => track.Path), File.Exists);
            library = LibraryRelocation.Apply(library, source, destination, mapping);
            database = new LibraryDatabase(root);
            database.Load();
            database.Save(library, true);
            var watch = Stopwatch.StartNew();
            library = new LibraryDatabase(root).Load();
            long open = watch.ElapsedMilliseconds;
            if (library.Folders.Single() != destination || library.Favorites.Single() != destinationFile ||
                library.Playlist.Single() != destinationFile || library.Playlists["Prueba"].Single() != destinationFile ||
                library.History.Single() != destinationFile || library.PlayCounts[destinationFile] != 6 ||
                library.TagEdits[destinationFile] != originalEdit)
                throw new InvalidOperationException("Relocation did not preserve the library references.");

            var batch = library.Tracks.Take(3).Select((track, index) =>
            {
                var edit = new LibraryTagEdit(track.Name, track.Artist, track.Album, "Jazz", 2020, "Prisma", track.Disc, (uint)(index + 4));
                return new BatchUndo(track, library.TagEdits.GetValueOrDefault(track.Path), edit, edit.ApplyTo(track));
            }).ToArray();
            SaveLibraryTagChanges(batch.Select(item => (item.Before.Path, item.Applied, (Track)null)).ToArray());
            if (!new LibraryDatabase(root).Load().Tracks.Take(3).Select(track => track.Number).SequenceEqual(new uint[] { 4, 5, 6 }))
                throw new InvalidOperationException("Bulk numbering did not persist in its requested order.");
            undoBatchTags = batch;
            UndoBatchTags(this, new RoutedEventArgs());
            if (undoBatchTags != null || !library.Tracks.Take(3).SequenceEqual(batch.Select(item => item.Before)))
                throw new InvalidOperationException("Undo bulk edit did not restore the initial tracks.");

            watch.Restart();
            int searchMatches = library.Tracks.Count(track => MatchesLibraryQuery(track, new[] { "cancion", "009" }));
            long search = watch.ElapsedMilliseconds;
            if (searchMatches == 0) throw new InvalidOperationException("Large-library search returned no matches.");
            var keys = library.Tracks.Take(600).Select(track => (track.Album, Owner: AlbumOwner(track))).Distinct().ToArray();
            watch.Restart();
            int expected = keys.Sum(key => library.Tracks.Count(track =>
                string.Equals(track.Album, key.Album, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AlbumOwner(track), key.Owner, StringComparison.OrdinalIgnoreCase)));
            double linear = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            int indexed = keys.Sum(key => IndexedAlbum(key.Album, key.Owner).Count());
            double indexedTime = watch.Elapsed.TotalMilliseconds;
            if (indexed != expected) throw new InvalidOperationException("Indexed album lookups disagreed with the baseline.");

            var scanTimes = await Task.Run(() =>
            {
                string scan = Path.Combine(root, "scan");
                Directory.CreateDirectory(scan);
                string seed = Path.Combine(scan, "seed.wav");
                using (var writer = new NAudio.Wave.WaveFileWriter(seed, new NAudio.Wave.WaveFormat(8000, 16, 1)))
                    writer.Write(new byte[1600], 0, 1600);
                for (int i = 1; i < files; i++) File.Copy(seed, Path.Combine(scan, $"track-{i:D4}.wav"));
                string corrupt = Path.Combine(scan, "bad.wav");
                File.WriteAllText(corrupt, "bad audio");
                var failures = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
                var timer = Stopwatch.StartNew();
                var first = LibraryScanner.ReadFolder(scan, onFailure: (path, error) => failures[path] = error);
                long firstTime = timer.ElapsedMilliseconds;
                if (first.Tracks.Count != files || !failures.ContainsKey(corrupt))
                    throw new InvalidOperationException("Scan did not record the corrupt route.");
                var cache = first.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
                timer.Restart();
                var second = LibraryScanner.ReadFolder(scan, cache: cache);
                long secondTime = timer.ElapsedMilliseconds;
                if (!second.Tracks.All(track => ReferenceEquals(track, cache[track.Path])))
                    throw new InvalidOperationException("Incremental scan reread an unchanged track.");
                return (firstTime, secondTime, corrupt, seed);
            });
            var reportProbe = await Scan(new[] { Path.GetDirectoryName(scanTimes.corrupt) }, add: false);
            if (reportProbe?.Complete != false || lastScanReport.Added != files ||
                !lastScanReport.Failures.Any(item => item.Path == scanTimes.corrupt))
                throw new InvalidOperationException("The UI scan report missed a failed route or its import count.");
            int beforeRetry = library.Tracks.Count;
            var retry = await Scan(new[] { scanTimes.corrupt }, add: false, allSubfolders: true, retryFailures: true);
            if (retry?.Complete != false || library.Tracks.Count != beforeRetry || lastScanReport.Failures.Single().Path != scanTimes.corrupt)
                throw new InvalidOperationException("Retry did not stay within the failed route.");
            File.Copy(scanTimes.seed, scanTimes.corrupt, overwrite: true);
            retry = await Scan(new[] { scanTimes.corrupt }, add: false, allSubfolders: true, retryFailures: true);
            if (retry?.Complete != true || lastScanReport.Added != 1 || lastScanReport.Failures.Length != 0 ||
                library.Tracks.Count != beforeRetry + 1)
                throw new InvalidOperationException("The repaired route did not import on retry.");
            File.SetLastWriteTimeUtc(scanTimes.seed, DateTime.UtcNow.AddMinutes(-2));
            await Scan(new[] { scanTimes.seed }, add: false);
            if (lastScanReport.UpdatedPaths.Single() != scanTimes.seed)
                throw new InvalidOperationException("The report did not list the path with refreshed tags.");
            Console.WriteLine($"Scale check: {librarySize:N0} tracks, SQLite load {open} ms, search {search} ms, " +
                $"{keys.Length} album lookups {linear:0.0} → {indexedTime:0.0} ms; {files} WAV scan {scanTimes.firstTime} ms, incremental {scanTimes.secondTime} ms.");
            Console.WriteLine("Relocation, bulk numbering and undo, scan report and selective retries passed.");
        }
        finally
        {
            library = originalLibrary;
            database = originalDatabase;
        }
    }
    private async Task VerifyTagEditing(string root, Track fixture)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, Path.GetFileName(fixture.Path));
        File.Copy(fixture.Path, path);
        byte[] before = File.ReadAllBytes(path);
        using (var cover = File.Create(Path.Combine(root, "folder.png")))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)));
            encoder.Save(cover);
        }
        if (ReadArtworkPayload(path, () => null) is not { Length: > 0 } bytes)
            throw new InvalidOperationException("The album cover beside the music was not loaded.");
        using (var stream = new MemoryStream(bytes))
            if (DecodeArtwork(stream, 32).PixelWidth == 0) throw new InvalidOperationException("The folder cover did not decode.");

        var oldDatabase = database;
        var oldLibrary = library;
        var oldQueue = queue.ToArray();
        try
        {
            var original = LibraryScanner.ReadFile(path);
            database = new LibraryDatabase(root);
            library = database.Load();
            library.Tracks.Add(original);
            database.Save(library, true);
            var edit = new LibraryTagEdit(original.Name, original.Artist, original.Album, original.Genre, original.Year,
                "Prisma Validation", 3, 9);
            SaveLibraryTagEdit(original, edit);
            var saved = new LibraryDatabase(root).Load();
            var track = saved.Tracks.Single();
            if (track.AlbumArtist != edit.AlbumArtist || track.Disc != 3 || track.Number != 9)
                throw new InvalidOperationException("Album artist, disc or track number did not persist.");
            SaveLibraryTagEdit(track, null, LibraryScanner.ReadFile(path));
            var restored = new LibraryDatabase(root).Load();
            if (restored.TagEdits.Count != 0 || restored.Tracks.Single() != original || !File.ReadAllBytes(path).SequenceEqual(before))
                throw new InvalidOperationException("Restoring tags did not preserve the original audio file and metadata.");
            SaveLibraryTagEdit(original, new LibraryTagEdit(Genre: "Jazz"));
            queue.Clear();
            queue.Add(library.Tracks.Single());
            using (var media = AudioMetadata.Open(path))
            {
                media.Tag.Title = "Title changed on disk";
                media.Save();
            }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-3));
            var result = await Scan(new[] { path }, add: false);
            var imported = library.Tracks.Single();
            if (result?.Complete != true || imported.Name != "Title changed on disk" || imported.Genre != "Jazz" ||
                queue.Single() != imported || lastScanReport.Updated != 1)
                throw new InvalidOperationException("Rescan did not merge external and local tags or refresh the queue.");
            Console.WriteLine("Tag restoration, partial tag merges, refreshed queue, and local cover passed.");
        }
        finally
        {
            library = oldLibrary;
            database = oldDatabase;
            queue.Clear();
            queue.AddRange(oldQueue);
        }
    }
    private async Task VerifyArtistPage(string fixture)
    {
        var result = await Task.Run(() => LibraryScanner.ReadFolder(fixture));
        var tracks = result.Tracks.Where(track => track.Artist.Equals("Diana Krall", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (tracks.Length < 2) throw new InvalidOperationException("The artist fixture needs at least two Diana Krall tracks.");
        library.Tracks = tracks.ToList();
        library.PlayCounts[tracks[0].Path] = 4;
        library.PlayCounts[tracks[1].Path] = 7;
        Search.Clear();
        Width = 1630;
        Height = 1050;
        LibrarySectionIndex = 2;
        RefreshRows();
        Rows.SelectedIndex = 0;
        ActivateSelection();
        if (collection?.Name != tracks[0].Artist || albumPage || !ArtistView.IsVisible || Rows.IsVisible)
            throw new InvalidOperationException("The artist gallery did not open the profile.");

        Button GetButton(string label) => Descendants(ArtistContent).OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == label);
        bool ready = false;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            UpdateLayout();
            bool portrait = Descendants(ArtistContent).OfType<Image>().Any(image =>
                AutomationProperties.GetName(image) == LocalTextExtension.Format("Retrato de {0}", collection.Name) && image.Source != null);
            if (portrait && GetButton(T("Artículo en Wikipedia"))?.IsVisible == true)
            {
                ready = true;
                break;
            }
            await Task.Delay(250);
        }
        if (!ready) throw new InvalidOperationException("The profile did not contain the artist portrait and biography.");
        if (firstArtistAlbum?.Width is not (>= 132 and <= 228) ||
            !FocusArtistSection(albums: true) || !FocusArtistSection(albums: false))
            throw new InvalidOperationException("Artist album sizing or keyboard section focus failed.");
        ArtistView.ScrollToTop();
        UpdateLayout();
        var firstSong = Descendants(ArtistContent).OfType<Button>()
            .FirstOrDefault(button => tracks.Any(track => AutomationProperties.GetName(button) ==
                LocalTextExtension.Format("Reproducir {0}", track.Name)));
        if (firstSong == null || AutomationProperties.GetName(firstSong) != LocalTextExtension.Format("Reproducir {0}", tracks[1].Name))
            throw new InvalidOperationException("The profile did not order songs by local plays.");
        CaptureDesignPreview(Shell, "PRISMA_ARTIST_PAGE_PREVIEW");
        var album = GetButton(LocalTextExtension.Format("Abrir álbum {0}", tracks[0].Album)) ??
            throw new InvalidOperationException("The artist's album did not appear.");
        album.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!albumPage || collection?.Name != tracks[0].Album || ArtistView.IsVisible)
            throw new InvalidOperationException("Opening an album from the artist profile failed.");
        CloseCollection(this, new RoutedEventArgs());
        UpdateLayout();
        var all = GetButton(T("Ver todas las canciones")) ?? throw new InvalidOperationException("All songs is missing.");
        all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!Rows.IsVisible || Rows.Items.Count != tracks.Length || !ArtistOverviewButton.IsVisible)
            throw new InvalidOperationException("Opening the artist's complete track list failed.");
        ShowArtistOverview(this, new RoutedEventArgs());
        if (!ArtistView.IsVisible) throw new InvalidOperationException("Return to artist profile failed.");
        Console.WriteLine("Artist gallery: live portrait and biography, album navigation, and ranked and full song lists passed.");

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
    private static void VerifyAudioFormats(string directory)
    {
        Track ReadFixture(string name) => LibraryScanner.ReadFolder(Path.Combine(directory, name)).Tracks.Single();
        var alac = ReadFixture("alac-24-96.m4a");
        var renamed = ReadFixture("alac-24-96.alac");
        var flac = ReadFixture("flac-24-96.flac");
        var aac = ReadFixture("aac-48.m4a");
        var wav = ReadFixture("wav-16-48.wav");
        var mp3 = ReadFixture("mp3-48.mp3");
        int expected = (int)Math.Round(new FileInfo(Path.Combine(directory, "alac-payload.bin")).Length * 8 / 3000d);
        if (alac.Codec != "ALAC" || alac.SampleRate != 96000 || alac.BitDepth != 24 || alac.Channels != 2 ||
            alac.BitrateKbps != expected || renamed.FormatLabel != alac.FormatLabel ||
            ReadTrackDetails(alac).Audio.Single(entry => entry.Label == "Bitrate").Value != $"{expected:N0} kb/s")
            throw new InvalidOperationException($"ALAC metadata mismatch. Expected {expected} kb/s, got {alac.FormatLabel}.");
        if (flac.Codec != "FLAC" || flac.BitDepth != 24 || flac.BitrateKbps is not > 0 ||
            wav.Codec != "PCM" || wav.BitrateKbps != 1536 || aac.Codec != "AAC" || aac.BitrateKbps is not (> 100 and < 160) ||
            mp3.BitrateKbps != 192)
            throw new InvalidOperationException($"Other audio formats mismatched: {flac.FormatLabel} / {wav.FormatLabel} / {aac.FormatLabel} / {mp3.FormatLabel}.");

        foreach (var track in new[] { alac, renamed })
        {
            using var decoder = AudioEngine.Prepare(track.Path, strictDirect: false);
            if (decoder.Read(new byte[32768], 0, 32768) == 0 || decoder.WaveFormat.SampleRate != track.SampleRate)
                throw new InvalidOperationException($"ALAC decoder failed: {track.Path}");
        }
        Console.WriteLine($"ALAC reading and decoding passed ({alac.FormatLabel}); FLAC, AAC, MP3, and PCM bitrates passed.");
    }
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
            string path = Environment.GetEnvironmentVariable("PRISMA_NAS_DIALOG_PROBE") == null ? source : AskNasSource(this);
            if (path != source) throw new InvalidOperationException("The NAS dialog did not verify and accept the share.");
            bool testOwnNas = Environment.GetEnvironmentVariable("PRISMA_SMB_LIVE_SOURCE") == "1";
            if (testOwnNas)
            {
                string[] components = source[2..].Split('\\');
                var listed = await Task.Run(() => NetworkMusicSource.ListShares(components[0]));
                if (!listed.Contains($@"\\{components[0]}\{components[1]}", StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The NAS did not list the selected SMB share.");
            }
            await ImportNasSource(path);
            int expected = testOwnNas ? library.Tracks.Count : 3;
            if (expected < 2 || library.Tracks.Count != expected || library.Folders.Single() != source)
                throw new InvalidOperationException($"SMB scan did not index the demo source. {Status.Text}");
            if (selectedLibrarySource != source)
                throw new InvalidOperationException("The NAS import did not open the source and report its indexed tracks.");

            var directoryForDatabase = Environment.GetEnvironmentVariable("PRISMA_SMB_TEST_DATABASE");
            if (!string.IsNullOrEmpty(directoryForDatabase))
            {
                var probeDatabase = new LibraryDatabase(directoryForDatabase);
                probeDatabase.Save(library, tracksChanged: true);
                library = probeDatabase.Load();
                if (library.Tracks.Count != expected || library.Folders.Single() != source)
                    throw new InvalidOperationException("Reloading the SMB library from SQLite lost the source or tracks.");
            }

            ShowLibrarySource(source);
            var rows = Rows.Items.OfType<LibraryRow>().ToArray();
            if (rows.Length != expected || !rows.All(row => IsTrackInSource(row.Tracks.Single().Path, source)))
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
            Console.WriteLine($"SMB: indexed, persisted and displayed {expected} tracks, played one and switched tracks.");

            var before = library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
            await Scan(new[] { source }, add: false);
            if (library.Tracks.Count != expected || !library.Tracks.All(track => ReferenceEquals(before[track.Path], track)))
                throw new InvalidOperationException("SMB rescan reread unchanged tracks.");
            string unavailable = Path.Combine(source, "unavailable-" + Guid.NewGuid().ToString("N"));
            var retained = first with { Path = Path.Combine(unavailable, "retained.m4a") };
            library.Tracks.Add(retained);
            await Scan(new[] { unavailable }, false);
            if (!library.Tracks.Contains(retained) || library.Tracks.Count != expected + 1)
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
