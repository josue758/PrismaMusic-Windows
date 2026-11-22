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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyCompactWorkspace()
    {
        double previousWidth = Width, previousHeight = Height;
        Width = 1340;
        Height = 780;
        UpdateLayout();
        SetListeningPanelHidden(false);
        UpdateLayout();
        if (listeningIsOverlay || ListeningColumn.Width.Value == 0 || ListeningScrim.IsVisible)
            throw new InvalidOperationException("A wide window did not dock the playback panel.");
        Width = MinWidth;
        Height = MinHeight;
        LibrarySectionIndex = 0;
        UpdateLayout();
        double libraryWidth = Rows.ActualWidth;
        if (!listeningIsOverlay || Listening.IsVisible || ListeningColumn.Width.Value != 0 || libraryWidth < 550 ||
            !QueueHeaderButton.IsVisible || !AudioOutputButton.IsVisible || !PipToolbarButton.IsVisible ||
            CompactEqualizerMenuItem.Visibility != Visibility.Visible)
            throw new InvalidOperationException("The compact workspace did not free space or retain its primary commands.");
        await Task.Delay(220);
        CaptureDesignPreview(Shell, "PRISMA_COMPACT_WORKSPACE_PREVIEW");
        QueueHeaderButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        UpdateLayout();
        if (!Listening.IsVisible || !ListeningScrim.IsVisible || ListeningTabs.SelectedIndex != 1 ||
            ListeningTrackSummary.IsVisible || Math.Abs(libraryWidth - Rows.ActualWidth) > 1 ||
            ListeningColumn.Width.Value != 0)
            throw new InvalidOperationException("Opening the compact queue resized or obstructed the library layout incorrectly.");
        await Task.Delay(220);
        CaptureDesignPreview(Shell, "PRISMA_COMPACT_DRAWER_PREVIEW");
        ListeningScrim.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (Listening.IsVisible || ListeningScrim.IsVisible)
            throw new InvalidOperationException("The backdrop did not close the compact drawer.");
        QueueHeaderButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        ListeningCloseButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(ListeningCloseButton),
            Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        if (Listening.IsVisible || !QueueHeaderButton.IsKeyboardFocused)
            throw new InvalidOperationException("Escape did not close the compact drawer and return focus.");
        AudioOutputButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        for (int attempt = 0; attempt < 50 && audioDevicePicker == null; attempt++) await Task.Delay(50);
        if (audioOutputPopup?.IsOpen != true || audioDevicePicker == null)
            throw new InvalidOperationException("Compact audio output did not open.");
        audioOutputPopup.IsOpen = false;
        PipToolbarButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (mini?.IsVisible != true) throw new InvalidOperationException("Compact mini player did not open.");
        mini.Close();
        TransportArtworkButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (listeningRoom?.IsVisible != true || !EqualizerToolbarButton.IsVisible || !FullscreenToolbarButton.IsVisible)
            throw new InvalidOperationException("The listening room inherited the compact toolbar.");
        listeningRoom.Close();
        Width = 1340;
        UpdateLayout();
        if (listeningIsOverlay || !Listening.IsVisible || ListeningColumn.Width.Value == 0 || ListeningScrim.IsVisible)
            throw new InvalidOperationException("Expanding did not restore the docked layout.");
        Width = previousWidth;
        Height = previousHeight;
        Console.WriteLine($"Compact workspace: {libraryWidth:0} DIP for music, overlay queue, audio output, PiP, and wide layout restored.");
    }

    private async Task VerifyTagReview(Track fixture)
    {
        var originalLibrary = library;
        var originalDatabase = database;
        var originalUndo = undoBatchTags;
        string folder = Path.Combine(Path.GetTempPath(), "prisma-review-" + Guid.NewGuid().ToString("N"));
        var first = fixture with { Path = Path.Combine(folder, "one.flac"), Name = "Ola", Artist = "Prisma", Album = "Río" };
        var second = first with { Path = Path.Combine(folder, "two.flac"), Name = "Marea", Album = " RÍO " };
        try
        {
            library = new Library { Tracks = new() { first, second } };
            database = new LibraryDatabase(folder);
            database.Load();
            database.Save(library, true);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                Window review = null;
                try
                {
                    review = OwnedWindows.OfType<Window>().Single(window => window.Title == T("Revisión de etiquetas") && window.IsVisible);
                    var surface = (Grid)review.Content;
                    var root = (DockPanel)((Border)surface.Children[0]).Child;
                    var lists = root.Children.OfType<Grid>().Single().Children.OfType<ListBox>().ToArray();
                    for (int attempt = 0; attempt < 60 && lists[1].Items.Count != 2; attempt++) await Task.Delay(50);
                    if (lists[0].Items.Count != 1 || lists[1].Items.Count != 2)
                        throw new InvalidOperationException("The review did not show the album variants.");
                    var buttons = root.Children.OfType<StackPanel>().Last().Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
                    Button Action(string label) => buttons.Single(button => Equals(button.Content, T(label)));
                    Action("Seleccionar grupo").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    await Task.Delay(180);
                    CaptureDesignPreview(surface, "PRISMA_TAG_REVIEW_PREVIEW");
                    var edited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        Window batch = null;
                        try
                        {
                            batch = review.OwnedWindows.OfType<Window>().Single(window => window.IsVisible);
                            var body = (StackPanel)((ScrollViewer)((Grid)batch.Content).Children[0]).Content;
                            var field = body.Children.OfType<Grid>().Single(grid =>
                                grid.Children.OfType<CheckBox>().Any(box => Equals(box.Content, T("Álbum"))));
                            field.Children.OfType<CheckBox>().Single().IsChecked = true;
                            field.Children.OfType<TextBox>().Single().Text = "Río";
                            if (library.Tracks[1].Album != " RÍO " ||
                                !body.Children.OfType<ListBox>().Single().Items.Cast<string>().Any(text => text.Contains("→ Río")))
                                throw new InvalidOperationException("The review preview changed data early or omitted the album change.");
                            body.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                                .Single(button => Equals(button.Content, T("Aplicar cambios"))).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            edited.SetResult();
                        }
                        catch (Exception error) { edited.SetException(error); }
                        finally { if (batch?.IsVisible == true) batch.Close(); }
                    }));
                    Action("Vista previa de edición").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    await edited.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    for (int attempt = 0; attempt < 60 && !Action("Deshacer edición por lotes").IsEnabled; attempt++) await Task.Delay(50);
                    if (library.Tracks.Any(track => track.Album != "Río") || lists[0].Items.Count != 0 ||
                        library.TagEdits.ContainsKey(first.Path) || !library.TagEdits.ContainsKey(second.Path))
                        throw new InvalidOperationException("Applying the reviewed changes did not resolve the album group.");
                    Action("Deshacer edición por lotes").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if (library.Tracks[1].Album != " RÍO ")
                        throw new InvalidOperationException("Undo did not restore the album variant.");
                    finished.SetResult();
                }
                catch (Exception error) { finished.SetException(error); }
                finally { if (review?.IsVisible == true) review.Close(); }
            }));
            ShowTagReview(this, new RoutedEventArgs());
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine("Tag review: selected album group, previewed and applied a correction, and restored it with Undo.");
        }
        finally
        {
            library = originalLibrary;
            database = originalDatabase;
            undoBatchTags = originalUndo;
        }
    }

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
            database = new LibraryDatabase(root);
            database.Load();
            database.Save(library, true);
            await VerifyRelocationDialog(source, destination);
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
            var snapshot = library.Tracks.ToArray();
            var query = new LibraryQuery("cancion 009");
            var matches = query.Filter(snapshot).ToArray();
            long search = watch.ElapsedMilliseconds;
            var culture = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
            const System.Globalization.CompareOptions options = System.Globalization.CompareOptions.IgnoreCase |
                System.Globalization.CompareOptions.IgnoreNonSpace;
            var exact = snapshot.Where(item =>
            {
                string searchable = $"{item.Name} {item.Detail} {item.AlbumArtist} {item.Genre} {item.FormatLabel}";
                return culture.IndexOf(searchable, "cancion", options) >= 0 && culture.IndexOf(searchable, "009", options) >= 0;
            }).ToArray();
            if (matches.Length == 0 || !matches.SequenceEqual(exact))
                throw new InvalidOperationException("Large-library search changed the results or their order.");
            var keys = library.Tracks.Take(600).Select(track => (track.Album, Owner: AlbumOwner(track))).Distinct().ToArray();
            watch.Restart();
            int expected = keys.Sum(key => library.Tracks.Count(track =>
                string.Equals(track.Album, key.Album, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AlbumOwner(track), key.Owner, StringComparison.OrdinalIgnoreCase)));
            double linear = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            int indexed = keys.Sum(key => IndexedAlbum(key.Album, key.Owner).Length);
            double indexedTime = watch.Elapsed.TotalMilliseconds;
            if (indexed != expected) throw new InvalidOperationException("Indexed album lookups disagreed with the baseline.");
            watch.Restart();
            for (int pass = 0; pass < 100; pass++)
                foreach (var key in keys) _ = IndexedAlbum(key.Album, key.Owner);
            double cachedAlbumTime = watch.Elapsed.TotalMilliseconds / 100;

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
                string otherCorrupt = Path.Combine(scan, "other.wav");
                File.WriteAllText(otherCorrupt, "bad audio");
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
                return (firstTime, secondTime, corrupt, otherCorrupt, seed);
            });
            var reportProbe = await Scan(new[] { Path.GetDirectoryName(scanTimes.corrupt) }, add: false);
            if (reportProbe?.Complete != false || lastScanReport.Added != files || lastScanReport.Failures.Length != 2)
                throw new InvalidOperationException("The UI scan report missed a failed route or its import count.");
            int beforeRetry = library.Tracks.Count;
            var retry = await Scan(new[] { scanTimes.corrupt }, add: false, allSubfolders: true, retryFailures: true);
            if (retry?.Complete != false || library.Tracks.Count != beforeRetry || lastScanReport.Failures.Length != 2)
                throw new InvalidOperationException("Retry did not stay within the failed route.");
            File.Copy(scanTimes.seed, scanTimes.corrupt, overwrite: true);
            // Repair both files so the remaining error proves that the report retried exactly one path.
            File.Copy(scanTimes.seed, scanTimes.otherCorrupt, overwrite: true);
            await VerifyScanReportDialog(scanTimes.corrupt);
            if (lastScanReport.Added != files + 1 || lastScanReport.Failures.Single().Path != scanTimes.otherCorrupt ||
                library.Tracks.Count != beforeRetry + 1)
                throw new InvalidOperationException("The repaired route did not import on retry.");
            retry = await Scan(new[] { scanTimes.otherCorrupt }, add: false, allSubfolders: true, retryFailures: true);
            if (retry?.Complete != true || !lastScanReport.Complete || lastScanReport.Added != files + 2)
                throw new InvalidOperationException("Resolving the remaining path did not complete the original report.");
            File.SetLastWriteTimeUtc(scanTimes.seed, DateTime.UtcNow.AddMinutes(-2));
            await Scan(new[] { scanTimes.seed }, add: false);
            if (lastScanReport.UpdatedPaths.Single() != scanTimes.seed)
                throw new InvalidOperationException("The report did not list the path with refreshed tags.");
            var cancelled = Scan(new[] { Path.GetDirectoryName(scanTimes.seed) }, add: false);
            scanCancellation.Cancel();
            if (await cancelled != null || !lastScanReport.Cancelled || lastScanReport.Complete)
                throw new InvalidOperationException("The report did not distinguish cancellation from a completed scan.");
            Console.WriteLine($"Scale check: {librarySize:N0} tracks, SQLite load {open} ms, search {search} ms, " +
                $"{keys.Length} album lookups {linear:0.0} → {indexedTime:0.0} ms, repeated {cachedAlbumTime:0.00} ms; " +
                $"{files} WAV scan {scanTimes.firstTime} ms, incremental {scanTimes.secondTime} ms.");
            Console.WriteLine("Relocation, bulk numbering and undo, scan report and selective retries passed.");
        }
        finally
        {
            library = originalLibrary;
            database = originalDatabase;
        }
    }

    private async Task VerifyScanReportDialog(string path)
    {
        var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            Window dialog = null;
            try
            {
                dialog = OwnedWindows.OfType<Window>().Single(window => window.Title == T("Informe del escaneo") && window.IsVisible);
                var surface = (Grid)dialog.Content;
                var root = (DockPanel)((Border)surface.Children[0]).Child;
                var toolbar = root.Children.OfType<StackPanel>().First().Children.OfType<DockPanel>().Single();
                var search = toolbar.Children.OfType<TextBox>().Single();
                var paths = root.Children.OfType<ListBox>().Single();
                var actions = root.Children.OfType<StackPanel>().Last().Children.OfType<WrapPanel>().Single();
                var retry = actions.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Reintentar ruta"));
                if (retry.IsEnabled || paths.Items.Count != 2)
                    throw new InvalidOperationException($"Expected two unselected failures, got {paths.Items.Count} with retry enabled: {retry.IsEnabled}.");
                paths.SelectedIndex = 0;
                await Task.Delay(250);
                CaptureDesignPreview(surface, "PRISMA_SCAN_REPORT_PREVIEW");
                search.Text = "no-such-path";
                if (paths.Items.Count != 0 || retry.IsEnabled)
                    throw new InvalidOperationException("The scan report retained a hidden selection.");
                search.Text = Path.GetFileName(path).ToUpperInvariant();
                if (paths.Items.Count != 1 || paths.Items[0] is not ScanFailure item || item.Path != path)
                    throw new InvalidOperationException("The scan report did not filter paths case-insensitively.");
                paths.SelectedIndex = 0;
                if (!retry.IsEnabled) throw new InvalidOperationException("The selected failure could not be retried.");
                retry.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if (dialog.IsVisible) throw new InvalidOperationException("Retry did not return to scan progress.");
                clicked.SetResult();
            }
            catch (Exception error) { clicked.SetException(error); }
            finally { if (dialog?.IsVisible == true) dialog.Close(); }
        }));
        ShowScanReport(this, new RoutedEventArgs());
        await clicked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int attempt = 0; attempt < 120 && scanning; attempt++) await Task.Delay(50);
        if (scanning) throw new InvalidOperationException("The scan report retry did not finish.");
    }

    private async Task VerifyRelocationDialog(string source, string destination)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            Window dialog = null;
            try
            {
                dialog = OwnedWindows.OfType<Window>().Single(window => window.Title == T("Reubicar fuente") && window.IsVisible);
                var surface = (Grid)dialog.Content;
                var root = (StackPanel)((Border)surface.Children[0]).Child;
                var path = root.Children.OfType<DockPanel>().Single().Children.OfType<TextBox>().Single();
                var buttons = root.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
                Button Button(string label) => buttons.Single(button => Equals(button.Content, T(label)));
                var apply = Button("Aplicar reubicación");
                if (apply.IsEnabled) throw new InvalidOperationException("Relocation was available before verification.");
                path.Text = destination;
                Button("Verificar ubicación").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                for (int attempt = 0; attempt < 80 && !apply.IsEnabled; attempt++) await Task.Delay(50);
                if (!apply.IsEnabled || !library.Folders.Contains(source) || library.Folders.Contains(destination))
                    throw new InvalidOperationException("Verification failed or relocated the source before confirmation.");
                path.Text = source;
                if (apply.IsEnabled) throw new InvalidOperationException("An edited path retained its previous verification.");
                path.Text = destination;
                Button("Verificar ubicación").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                for (int attempt = 0; attempt < 80 && !apply.IsEnabled; attempt++) await Task.Delay(50);
                if (!apply.IsEnabled) throw new InvalidOperationException("The destination could not be verified again.");
                CaptureDesignPreview(surface, "PRISMA_RELOCATION_PREVIEW");
                apply.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if (!library.Folders.Contains(destination) || library.Folders.Contains(source))
                    throw new InvalidOperationException("Applying the verified move did not relocate the source.");
                completed.SetResult();
                Console.WriteLine("Relocation dialog verified the destination, rejected an edited path, and saved only on Apply.");
            }
            catch (Exception error) { completed.SetException(error); }
            finally { if (dialog?.IsVisible == true) dialog.Close(); }
        }));
        RelocateLibrarySource(source);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(12));
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
        if (firstArtistAlbum?.ActualWidth is not (> 0 and <= 260) ||
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

    private async Task VerifyResponsiveArtistPage(IReadOnlyList<Track> demos)
    {
        const string artist = "Prisma Sessions";
        var tracks = Enumerable.Range(0, 14).Select(index => demos[index % demos.Count] with
        {
            Artist = artist, AlbumArtist = artist, Album = $"Prisma Sessions · {index + 1:D2}", Year = (uint)(2026 - index)
        }).ToList();
        library.Tracks = tracks;
        Search.Clear();
        Width = MinWidth;
        Height = 690;
        LibrarySectionIndex = 2;
        RefreshRows();
        Rows.SelectedIndex = 0;
        ActivateSelection();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        int narrowColumns = VerifyCards();
        CaptureDesignPreview(Shell, "PRISMA_ARTIST_COMPACT_PREVIEW");

        Width = 1540;
        Height = 970;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        int wideColumns = VerifyCards();
        if (wideColumns <= narrowColumns)
            throw new InvalidOperationException($"The album gallery did not expand from {narrowColumns} columns.");
        int previousSize = albumSize;
        albumSize = 0;
        ApplyAlbumSize();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        int smallCoverColumns = VerifyCards();
        albumSize = 2;
        ApplyAlbumSize();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (VerifyCards() >= smallCoverColumns)
            throw new InvalidOperationException("The artist gallery did not use the preferred cover size.");
        albumSize = previousSize;
        ApplyAlbumSize();

        if (!TryArtistShortcut(Key.Down, ModifierKeys.Control) || !firstArtistAlbum.IsKeyboardFocused ||
            !TryArtistShortcut(Key.Up, ModifierKeys.Control) || !firstArtistSong.IsKeyboardFocused ||
            TryArtistShortcut(Key.Down, ModifierKeys.Control | ModifierKeys.Shift))
            throw new InvalidOperationException("Artist keyboard shortcuts did not focus the requested sections.");
        ArtistView.ScrollToTop();
        await Task.Delay(250);
        CaptureDesignPreview(Shell, "PRISMA_ARTIST_LAYOUT_PREVIEW");
        TryArtistShortcut(Key.Down, ModifierKeys.Control);
        await Task.Delay(250);
        CaptureDesignPreview(Shell, "PRISMA_ARTIST_ALBUMS_PREVIEW");
        var more = ArtistContent.Children.OfType<Button>().Single(button => Equals(button.Content, T("Mostrar más álbumes")));
        more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (artistAlbumButtons.Count != 14 || !artistAlbumButtons[12].IsKeyboardFocused)
            throw new InvalidOperationException("Loading more albums did not focus the first newly shown card.");
        var open = artistAlbumButtons[12];
        var album = open.Tag;
        open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        CloseCollection(this, new RoutedEventArgs());
        if (!ArtistView.IsVisible || artistAlbumButtons.Count != 14 || !Equals(Keyboard.FocusedElement, artistAlbumButtons[12]) ||
            !Equals(artistAlbumButtons[12].Tag, album))
            throw new InvalidOperationException("Back did not restore focus and pagination on the same album.");
        Console.WriteLine($"Artist profile: {narrowColumns} compact and {wideColumns} wide columns, square covers, cover size, shortcuts, and album return passed.");

        int VerifyCards()
        {
            UpdateLayout();
            if (!ArtistView.IsVisible || artistAlbumButtons.Count != 12)
                throw new InvalidOperationException("The artist gallery did not render its first album batch.");
            var grid = (UniformGrid)VisualTreeHelper.GetParent(firstArtistAlbum);
            var host = (Grid)VisualTreeHelper.GetParent(grid);
            if (grid.ActualWidth > host.ActualWidth + 1)
                throw new InvalidOperationException("The artist album grid overflowed its container.");
            foreach (var card in artistAlbumButtons)
            {
                var artwork = (Border)((StackPanel)card.Content).Children[0];
                if (artwork.ActualWidth < 30 || Math.Abs(artwork.ActualWidth - artwork.ActualHeight) > 1)
                    throw new InvalidOperationException($"An album cover was not square: {artwork.ActualWidth} × {artwork.ActualHeight}.");
            }
            return grid.Columns;
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

    private async Task VerifyNasPhysicalRecovery(string storage)
    {
        var first = Environment.GetEnvironmentVariable("PRISMA_NAS_FIRST_ALBUM")
            ?? throw new InvalidOperationException("First NAS fixture missing.");
        var second = Environment.GetEnvironmentVariable("PRISMA_NAS_SECOND_ALBUM")
            ?? throw new InvalidOperationException("Second NAS fixture missing.");
        string source = Path.GetDirectoryName(first);
        if (!NetworkMusicSource.IsSharePath(source) || !IsTrackInSource(second, source))
            throw new InvalidOperationException("The NAS test albums must be within the same source.");
        string host = source[2..].Split('\\')[0];
        nasValidationStorage = storage;
        database = new LibraryDatabase(storage);
        library = database.Load();
        Volume.Value = 0;
        try
        {
            if (Environment.GetEnvironmentVariable("PRISMA_NAS_PLAY_ONLY") != "1")
            {
                library.Folders.Add(source);
                database.Save(library, true);
                nasValidationBeforeDirectory = directory =>
                {
                    if (!string.Equals(directory, second, StringComparison.OrdinalIgnoreCase)) return;
                    Console.WriteLine("NAS_SCAN_READY: Suspend only SMB service, keeping this computer online.");
                    WaitForSmb(available: false).GetAwaiter().GetResult();
                };
                try
                {
                    await Scan(new[] { source }, add: false,
                        nasCheckpoint: new NasScanCheckpoint(false, false, new[] { first, second }, Array.Empty<string>()));
                }
                finally { nasValidationBeforeDirectory = null; }
                var checkpoint = NasScanCheckpoint.Read(storage, source);
                int kept = library.Tracks.Count;
                if (kept < 2 || checkpoint?.Pending.SingleOrDefault() != second ||
                    !checkpoint.Completed.Contains(first, StringComparer.OrdinalIgnoreCase) ||
                    new LibraryDatabase(storage).Load().Tracks.Count != kept)
                    throw new InvalidOperationException("SMB interruption did not retain the completed album and pending directory.");
                Console.WriteLine($"NAS_SCAN_OFFLINE: {kept} tracks retained. Resume SMB service.");
                await WaitForSmb(available: true);
                await ResumeNasScan(source);
                if (NasScanCheckpoint.Read(storage, source) != null || library.Tracks.Count <= kept)
                    throw new InvalidOperationException("SMB reconnection did not finish the remaining album.");
                Console.WriteLine($"NAS_SCAN_PASS: resumed the pending directory; {library.Tracks.Count} tracks indexed.");
            }

            Track track = library.Tracks.Where(item => IsTrackInSource(item.Path, first))
                .OrderByDescending(item => item.Seconds).First();
            queue.Clear();
            queue.Add(library.Tracks.First(item => item.Path != track.Path));
            var expectedQueue = queue.ToArray();
            originalContext = context = new[] { track };
            await OpenTrack(track, remember: false, position: 0, autoplay: true, recordHistory: false);
            for (int attempt = 0; attempt < 35 && player.Position.TotalSeconds < 1; attempt++) await Task.Delay(100);
            if (!opened || !playing || player.Position.TotalSeconds < 1)
                throw new InvalidOperationException($"The NAS track did not start (opened={opened}, playing={playing}): {Status.Text}");
            Console.WriteLine("NAS_PLAY_READY: Suspend only SMB service again.");
            await WaitForSmb(available: false);
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (playbackRetry == null && DateTime.UtcNow < deadline) await Task.Delay(200);
            if (playbackRetry is not { Interrupted: true, Autoplay: true } retry || retry.Position < 1 ||
                !queue.SequenceEqual(expectedQueue))
                throw new InvalidOperationException("The interrupted stream did not expose its resume position and queue.");
            Console.WriteLine($"NAS_PLAY_OFFLINE: position {retry.Position:0.00} s retained. Resume SMB service.");
            await WaitForSmb(available: true);
            await ResumeNetworkTrack();
            if (!opened || !playing || current?.Path != track.Path || Math.Abs(player.Position.TotalSeconds - retry.Position) > 1 ||
                !queue.SequenceEqual(expectedQueue))
                throw new InvalidOperationException("SMB reconnection did not restore the song at its saved position with the same queue.");
            Console.WriteLine("NAS_PLAY_PASS: sound recovered at the saved position with the queue intact.");
        }
        finally { player.Close(); }

        async Task WaitForSmb(bool available)
        {
            var timeout = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < timeout)
            {
                using var connection = new System.Net.Sockets.TcpClient();
                bool connected = false;
                try
                {
                    await connection.ConnectAsync(host, 445).WaitAsync(TimeSpan.FromSeconds(2));
                    connected = connection.Connected;
                }
                catch (Exception error) when (error is System.Net.Sockets.SocketException or TimeoutException) { }
                if (connected == available) return;
                await Task.Delay(350);
            }
            throw new TimeoutException(available ? "SMB did not reconnect" : "SMB did not go offline");
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
                if (Environment.GetEnvironmentVariable("PRISMA_USB_REQUIRE_RECONNECT") == "1")
                    await VerifyReconnectedDac(track, id);
                return;
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException("USB playback did not recover before the deadline: " + Status.Text);

        async Task VerifyReconnectedDac(Track playingTrack, string requestedId)
        {
            Console.WriteLine("DAC_RECONNECT_READY: Reconnect the DAC.");
            var timeout = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < timeout)
            {
                bool available = false;
                try
                {
                    using var requested = devices.GetDevice(requestedId);
                    available = requested.State == DeviceState.Active;
                }
                catch (System.Runtime.InteropServices.COMException) { }
                if (available)
                {
                    double position = player.Position.TotalSeconds;
                    await ReopenOutput();
                    if (!opened || !playing || current?.Path != playingTrack.Path || player.ActiveOutputId != requestedId ||
                        !player.IsExclusive || Math.Abs(player.Position.TotalSeconds - position) > .75)
                        throw new InvalidOperationException("The reconnected DAC did not reopen at the current song position.");
                    Console.WriteLine($"DAC_RECONNECT_PASS: exclusive playback reopened at {player.Position.TotalSeconds:0.00} s.");
                    return;
                }
                await Task.Delay(250);
            }
            throw new TimeoutException("The DAC did not reconnect.");
        }
    }
}
