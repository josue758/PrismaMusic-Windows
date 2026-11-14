using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PrismaMusic;

public record Track(string Path, string Name, string Artist, string Album, string AlbumArtist,
    string Genre, uint Year, uint Disc, uint Number, int Seconds)
{
    public string Detail => string.Join("  ·  ", new[] { Artist, Album, Year == 0 ? "" : Year.ToString() }.Where(x => x.Length > 0));
}

public record LibraryRow(string Name, string Detail, Track[] Tracks);

public sealed class Library
{
    public List<string> Folders { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
    public HashSet<string> Favorites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Playlist { get; set; } = new();
    public Dictionary<string, List<string>> Playlists { get; set; } = new();
    public List<string> History { get; set; } = new();
}

public partial class MainWindow : Window
{
    private static readonly string DataDirectory = SessionProbeDirectory() ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrismaMusic.Windows");
    private LibraryDatabase database;
    private bool unsavedTracks;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg", ".opus" };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly string AudioSettingsFile = Path.Combine(DataDirectory, "audio-settings.json");
    private sealed record AudioSettings(double Volume, string OutputId, bool Exclusive, float[] Gains, float Balance = 0, bool EqEnabled = true, bool Direct = false, bool ArtworkColors = true, bool AnimateSpectrum = true, double Speed = 1, bool StrictDirect = false, bool Shuffle = false, RepeatMode Repeat = RepeatMode.Off, bool RememberPosition = true);
    private readonly bool smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");
    private readonly AudioEngine player = new();
    private readonly List<Track> queue = new();
    private readonly Stack<Track> previous = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Library library = new();
    private Track current;
    private LibraryRow collection;
    private bool albumPage;
    private LibraryRow artRequest;
    private readonly Stack<(LibraryRow Collection, int Selection, bool Album)> collectionHistory = new();
    private string playlistName = "Mi lista";
    private List<string> ActivePlaylist => playlistName == "Mi lista" ? library.Playlist : library.Playlists[playlistName];
    private readonly Border[] spectrumBars = new Border[AudioEngine.Frequencies.Length];
    private enum RepeatMode { Off, Collection, Track }
    private bool shuffle;
    private bool rememberPosition = true;
    private RepeatMode repeat;
    private Track[] originalContext = Array.Empty<Track>();
    private Track[] context = Array.Empty<Track>();
    private bool ready, scanning, playing, opened, updatingSeek;

    private (double Time, string Text)[] lyricLines = Array.Empty<(double, string)>();
    private bool eqEnabled = true;
    private bool artworkColors = true, animateSpectrum = true;
    private readonly float[] gains = new float[AudioEngine.Frequencies.Length];
    private DateTime? sleepAt;
    private Window mini;
    private TextBlock miniTitle;
    private TextBlock miniArtist;
    private TextBlock miniFormat;
    private Image miniCover;
    private Button miniToggle;

    public MainWindow()
    {
        InitializeComponent();
        for (int index = 0; index < spectrumBars.Length; index++)
        {
            var bar = new Border { Height = 2, CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(198, 172, 255)) };
            spectrumBars[index] = bar;
            SpectrumView.Children.Add(bar);
        }
        try
        {
            if (!smokeTest)
            {
                var storage = new LibraryDatabase(DataDirectory);
                library = storage.Load();
                database = storage;
            }
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("No se pudo leer la biblioteca. ") + error.Message; }

        player.Volume = Volume.Value;
        if (!smokeTest) LoadAudioSettings();
        player.MediaOpened += (_, _) =>
        {
            opened = true;
            if (player.OutputNotice != null) Status.Text = player.OutputNotice;
            Tick();
        };
        player.MediaEnded += (_, _) => { if (pendingTrack == null) Advance(endOfTrack: true); };
        player.MediaFailed += (_, e) =>
        {
            SetPlaying(false);
            Status.Text = LocalTextExtension.Translate("Windows no pudo reproducir el archivo. ") + e.Message;
        };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            CancelPendingOpen();
            if (!smokeTest) { SaveSession(); SaveAudioSettings(); }
            player.Close();
        };
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        SizeChanged += (_, _) =>
        {
            bool compact = ActualWidth < 1000;
            NavigationColumn.Width = new GridLength(compact ? 156 : 180);
            ListeningColumn.Width = new GridLength(compact ? 248 : 320);
            Genre.Width = compact ? 132 : 167;
            Year.Width = compact ? 112 : 142;
            Sort.Width = compact ? 90 : 115;
            BrandTitle.FontSize = compact ? 14 : 17;
            Genre.FontSize = Year.FontSize = Sort.FontSize = compact ? 12 : 14;
            foreach (var button in LibraryCommands.Children.OfType<Button>())
                button.Padding = compact ? new Thickness(8, 6, 8, 6) : new Thickness(13, 8, 13, 8);
            foreach (var button in ListeningActions.Children.OfType<Button>().Concat(ListeningExtras.Children.OfType<Button>()))
            {
                if (compact) button.Padding = new Thickness(7, 6, 7, 6);
                else button.ClearValue(Control.PaddingProperty);
            }
            ArtPanel.Height = Math.Clamp(ActualHeight - 490, 114, 254);
        };
        UpdatePlaylistPicker();
        Navigation.SelectedIndex = 0;
        Sort.SelectedIndex = 0;
        ready = true;
        Shell.Background = new LinearGradientBrush(Color.FromRgb(30, 25, 40), Color.FromRgb(17, 16, 23), 115);
        if (smokeTest)
            Loaded += async (_, _) => await SmokeTest();
        RefreshFilters();
        RefreshRows();
        if (!smokeTest) Loaded += async (_, _) =>
        {
            await RestoreSession();
            if (SessionProbeDirectory() != null) await RunSessionProbe();
            else ShowWelcomeOnFirstRun();
        };
    }

    private async Task SmokeTest()
    {
        try
        {
            string expectedHeading = LocalTextExtension.Language switch { "en" => "Songs", "pt" => "Músicas", _ => "Canciones" };
            if (Heading.Text != expectedHeading)
                throw new InvalidOperationException($"Unexpected heading: {Heading.Text}");

            Fullscreen(this, new RoutedEventArgs());
            if (listeningRoom?.IsVisible != true || Transport.Parent == Shell)
                throw new InvalidOperationException("Listening view did not expand.");
            Fullscreen(this, new RoutedEventArgs());
            if (listeningRoom != null || Transport.Parent != Shell)
                throw new InvalidOperationException("Library layout was not restored.");
            player.StrictDirect = true;
            UpdateVolumeControls();
            if (EnableVolumeButton.Visibility != Visibility.Visible || VolumeControls.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Direct mode did not expose the volume action.");
            EnableAdjustableVolume(this, new RoutedEventArgs());
            if (player.StrictDirect || VolumeControls.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Adjustable volume was not restored.");
            Console.WriteLine("Fullscreen and direct-volume controls passed.");
            double normalWidth = Width, normalHeight = Height;
            Width = MinWidth;
            Height = MinHeight;
            UpdateLayout();
            if (Rows.ActualWidth < 200 || ArtPanel.ActualHeight > 160 || Transport.ActualHeight < 45 ||
                string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(PlayButton)))
                throw new InvalidOperationException("Compact layout or accessible playback label failed.");
            if (Environment.GetEnvironmentVariable("PRISMA_COMPACT_PREVIEW") is string compactPreview)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Shell.ActualWidth * 1.25),
                    (int)Math.Ceiling(Shell.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
                bitmap.Render(Shell);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(compactPreview);
                png.Save(stream);
            }
            Width = normalWidth;
            Height = normalHeight;
            Console.WriteLine("Compact layout and accessible transport label passed.");
            Console.WriteLine($"Interface: {LocalTextExtension.Language} ({Heading.Text})");
            await VerifyLyricsAndReceiverDiscovery();
            string basePython = Environment.GetEnvironmentVariable("PRISMA_TRANSLATOR_SETUP_PROBE");
            if (basePython != null)
            {
                string root = Path.Combine(Environment.GetEnvironmentVariable("PRISMA_TRANSLATOR_SETUP_ROOT"), "PrismaTranslatorProbe-" + Guid.NewGuid().ToString("N"));
                string python = await InstallLocalTranslator(basePython, root, "en", Console.WriteLine);
                var result = await TranslateLocalLines(python, "en", new[] { "Good morning." }, root);
                if (!result[0].Contains("días", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Managed translation did not produce Spanish.");
                Console.WriteLine("Managed Argos setup and translation passed.");
            }
            string argosProbe = Environment.GetEnvironmentVariable("PRISMA_ARGOS_PROBE");
            if (argosProbe != null)
            {
                var originals = new[] { "Good morning.", "Good night." };
                var translations = await TranslateLocalLines(argosProbe, "en", originals);
                SetLyrics("[00:01]Good morning.\n[00:03]Good night.");
                translatedLines = originals.Zip(translations).ToDictionary(pair => pair.First, pair => pair.Second);
                if (!BilingualLine(lyricLines[1].Text).Contains("noches", StringComparison.OrdinalIgnoreCase) ||
                    lyricLines[1].Time != 3)
                    throw new InvalidOperationException("Bilingual lyrics lost their alignment.");
                string translated = translations[0];
                if (!translated.Contains("días", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Unexpected Argos translation: {translated}");
                Console.WriteLine("Argos: English-to-Spanish local translation passed.");
            }
            string dbProbe = Environment.GetEnvironmentVariable("PRISMA_DATABASE_PROBE");
            if (dbProbe != null) await Task.Run(() => VerifyLibraryMigration(dbProbe));
            var demo = await Task.Run(() => ReadFolder(Path.Combine(AppContext.BaseDirectory, "Demo")));
            if (demo.Errors != 0 || demo.Tracks.Count != 3)
                throw new InvalidOperationException("Se esperaban tres demos legibles.");
            var track = demo.Tracks[0];
            string preview = Environment.GetEnvironmentVariable("PRISMA_PREVIEW_PATH");
            if (!string.IsNullOrEmpty(preview))
            {
                library.Tracks = demo.Tracks;
                RefreshFilters();
                RefreshRows();
                Now.Text = track.Name;
                Artist.Text = track.Detail;
                UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(this);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(preview);
                png.Save(stream);
            }
            if (Environment.GetEnvironmentVariable("PRISMA_SETTINGS_PREVIEW") != null) Settings(this, new RoutedEventArgs());
            library.Tracks = demo.Tracks;
            Navigation.SelectedIndex = 1;
            Rows.SelectedIndex = 0;
            var albumRow = (LibraryRow)Rows.SelectedItem;
            if (Environment.GetEnvironmentVariable("PRISMA_GALLERY_PREVIEW") is string galleryPreview)
            {
                UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Shell.ActualWidth, (int)Shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(Shell);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(galleryPreview);
                png.Save(stream);
            }
            if (!ReferenceEquals(Rows.ItemTemplate, Rows.FindResource("GalleryCard")))
                throw new InvalidOperationException("Album gallery did not use cards.");
            SetGalleryRows(Enumerable.Repeat(albumRow, GalleryPageSize + 1).ToArray());
            if (Rows.Items.Count != GalleryPageSize || !GalleryNext.IsEnabled)
                throw new InvalidOperationException("Gallery did not limit the first page.");
            RefreshRows();
            Rows.SelectedIndex = 0;
            ActivateSelection();
            var albumPage = collection;
            if (string.IsNullOrWhiteSpace(CollectionCount.Text) || CollectionArtTile.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Album header did not show its summary.");
            if (Environment.GetEnvironmentVariable("PRISMA_ALBUM_PREVIEW") is string albumPreview)
            {
                UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Shell.ActualWidth, (int)Shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(Shell);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(albumPreview);
                png.Save(stream);
            }
            BrowseRelatedArtist(this, new RoutedEventArgs());
            if (collection?.Name != albumRow.Tracks[0].Artist) throw new InvalidOperationException("Album did not open artist.");
            BrowseRelatedAlbum(this, new RoutedEventArgs());
            if (collection?.Name != albumRow.Name) throw new InvalidOperationException("Artist did not open album.");
            CloseCollection(this, new RoutedEventArgs());
            CloseCollection(this, new RoutedEventArgs());
            if (collection != albumPage || Rows.SelectedIndex != 0) throw new InvalidOperationException("Back did not restore album.");
            CloseCollection(this, new RoutedEventArgs());
            if (collection != null || Rows.SelectedIndex != 0) throw new InvalidOperationException("Back did not restore library.");
            Console.WriteLine("Album/artist navigation and back selection passed.");
            if (Environment.GetEnvironmentVariable("PRISMA_PROFILE_PROBE") == "1")
            {
                foreach (string language in new[] { "es", "pt", "en" })
                {
                    var profiles = await SearchArtist("Daft Punk", language);
                    if (profiles.Length == 0) throw new InvalidOperationException($"No profile for {language}.");
                    if (language == "en")
                    {
                        var page = profiles.First(item => item.TryGetProperty("pageimage", out _));
                        var credits = await FindPortraitCredit(page);
                        if (string.IsNullOrWhiteSpace(credits?.Page) || string.IsNullOrWhiteSpace(credits.License))
                            throw new InvalidOperationException("Portrait attribution is incomplete.");
                        Console.WriteLine($"Portrait license: {credits.License}");
                    }
                    Console.WriteLine($"Wikipedia profile: {language}");
                }
            }
            Navigation.SelectedIndex = 4;
            queue.Clear();
            queue.AddRange(demo.Tracks);
            RefreshRows();
            Rows.SelectedIndex = 1;
            EditQueue(-1);
            if (queue[0] != demo.Tracks[1]) throw new InvalidOperationException("Queue reorder failed.");
            EditQueue(0);
            if (queue.Count != 2 || queue.Contains(demo.Tracks[1])) throw new InvalidOperationException("Queue removal failed.");
            queue.Clear();
            player.Speed = 1.5;
            await Play(track, false, 1);
            if (!opened || playing || Math.Abs(player.Position.TotalSeconds - 1) > .15)
                throw new InvalidOperationException("Paused session restore failed.");
            originalContext = demo.Tracks.ToArray();
            shuffle = true;
            ReorderPlayback(track);
            if (context[0] != track || !context.ToHashSet().SetEquals(demo.Tracks))
                throw new InvalidOperationException("Shuffle dropped or duplicated tracks.");
            shuffle = false;
            ReorderPlayback(track);
            if (!context.SequenceEqual(demo.Tracks))
                throw new InvalidOperationException("Disabling shuffle lost the original order.");
            Console.WriteLine("Queue edit, shuffle and paused resume passed.");
            await VerifyCancelableOpen(demo.Tracks[1], demo.Tracks[2]);
            player.Speed = 1;
            Volume.Value = 0;
            context = demo.Tracks.ToArray();
            repeat = RepeatMode.Track;
            await Play(track);
            player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(180);
            for (int retry = 0; retry < 35 && player.Position.TotalSeconds > 2; retry++)
                await Task.Delay(100);
            if (current != track || !playing || player.Position.TotalSeconds > 2)
                throw new InvalidOperationException("Repeat track did not restart at the end.");
            repeat = RepeatMode.Collection;
            await Play(context[^1]);
            player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(180);
            for (int retry = 0; retry < 35 && current != track; retry++)
                await Task.Delay(100);
            if (current != track || !playing)
                throw new InvalidOperationException("Repeat collection did not wrap at the end.");
            repeat = RepeatMode.Off;
            Console.WriteLine("Repeat: track restart and collection wrap passed.");
            player.Speed = 1.5;
            await Play(track, false, 1);
            Mini(this, new RoutedEventArgs());
            for (int retry = 0; retry < 20 && !miniFormat.Text.Contains("kHz"); retry++)
                await Task.Delay(100);
            if (miniTitle.Text != track.Name || miniArtist.Text != track.Artist ||
                !miniFormat.Text.Contains("kHz") || miniFormat.Text != AudioFormat.Text)
                throw new InvalidOperationException("Mini player did not show the current track and format.");
            SetPlaying(true);
            if (!Equals(miniToggle.Content, PlayButton.Content))
                throw new InvalidOperationException("Mini player transport is out of sync.");
            SetPlaying(false);
            mini.Close();
            Console.WriteLine("Mini player: track, format and playback state passed.");
            SetLyrics("[00:02]First verse\n[00:05]Second verse");
            var verse = Lyrics.Inlines.OfType<System.Windows.Documents.Hyperlink>().Last();
            verse.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            if (Math.Abs(player.Position.TotalSeconds - 5) > .2)
                throw new InvalidOperationException("Selecting a verse did not seek.");
            Console.WriteLine("Clickable synchronized verse passed.");
            if (Environment.GetEnvironmentVariable("PRISMA_PORTRAIT_PROBE") is string portrait)
            {
                var profiles = await SearchArtist("Daft Punk", "en");
                var thumbnail = profiles.First(page => page.TryGetProperty("thumbnail", out _)).GetProperty("thumbnail");
                await SaveArtistPortrait("Daft Punk", new Uri(thumbnail.GetProperty("source").GetString()), portrait);
                using var saved = File.OpenRead(portrait);
                if (DecodeArtwork(saved, 160).PixelWidth < 1) throw new InvalidOperationException("Portrait cache unreadable.");
                Console.WriteLine("Wikipedia portrait downloaded, persisted and decoded.");
            }
            player.Volume = 0;
            player.Exclusive = Environment.GetCommandLineArgs().Contains("--exclusive");
            player.Open(new Uri(track.Path));
            player.Play();
            for (int retry = 0; retry < 40 && player.Position.TotalMilliseconds < 300; retry++)
                await Task.Delay(250);
            if (!opened || player.Position.TotalMilliseconds < 300)
                throw new InvalidOperationException("No avanzó la reproducción de la demo.");
            player.Pause();
            await Task.Delay(300);
            double pausedAt = player.Position.TotalSeconds;
            await Task.Delay(250);
            if (Math.Abs(player.Position.TotalSeconds - pausedAt) > .15)
                throw new InvalidOperationException("Tempo playback did not pause.");
            player.Position = TimeSpan.FromSeconds(1);
            if (Math.Abs(player.Position.TotalSeconds - 1) > .15)
                throw new InvalidOperationException("Tempo seek did not reset the processing buffer.");
            playing = true;
            player.Speed = .75;
            await ReopenOutput();
            await Task.Delay(500);
            if (!opened || player.Position.TotalSeconds <= 1.1)
                throw new InvalidOperationException("Tempo playback did not resume after reconfiguration.");
            Console.WriteLine("Tempo: playback, pause, seek and output reopen passed.");
            var libraryRows = Rows.ItemsSource;
            double beforeRoom = player.Position.TotalSeconds;
            Fullscreen(this, new RoutedEventArgs());
            await Task.Delay(400);
            if (!playing || player.Position.TotalSeconds <= beforeRoom || !ReferenceEquals(libraryRows, Rows.ItemsSource))
                throw new InvalidOperationException("Listening room disturbed playback or the library.");
            if (Environment.GetEnvironmentVariable("PRISMA_ROOM_PREVIEW") is string roomPreview)
            {
                var content = (FrameworkElement)listeningRoom.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(roomPreview);
                png.Save(stream);
            }
            listeningRoom.Close();
            if (!playing || Transport.Parent != Shell || !ReferenceEquals(libraryRows, Rows.ItemsSource))
                throw new InvalidOperationException("Closing the listening room disturbed playback or the library.");
            Console.WriteLine("Independent listening room preserved playback and library state.");
            string pcmProbe = Environment.GetEnvironmentVariable("PRISMA_PCM_PROBE");
            if (pcmProbe != null)
            {
                player.StrictDirect = true;
                opened = false;
                player.Open(new Uri(pcmProbe));
                if (!opened || !player.IsExclusive || player.OutputNotice?.Contains("PCM directo") != true)
                    throw new InvalidOperationException("Direct PCM open failed.");
                player.Play();
                await Task.Delay(500);
                if (player.Position.TotalMilliseconds < 200)
                    throw new InvalidOperationException("Direct PCM playback did not advance.");
                Console.WriteLine(player.OutputNotice);
            }
            using var castProbe = new Windows.Media.Playback.MediaPlayer();
            var pickerProbe = new Windows.Media.Casting.CastingDevicePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(pickerProbe, new WindowInteropHelper(this).Handle);
            pickerProbe.Filter.SupportsAudio = true;
            if (castProbe.GetAsCastingSource() == null)
                throw new InvalidOperationException("Windows no creó la fuente de transmisión.");
            Console.WriteLine("Casting: selector y fuente inicializados");
            Console.WriteLine(player.OutputNotice ?? (player.IsExclusive ? "WASAPI exclusive" : "WASAPI shared"));
            Console.WriteLine($"OK: {demo.Tracks.Count} demos y reproducción de {track.Name}");
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void EnableDarkTitleBar()
    {
        int enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
    }

    private void Save(bool tracksChanged = false)
    {
        try
        {
            unsavedTracks |= tracksChanged;
            if (database == null) return;
            database.Save(library, unsavedTracks);
            unsavedTracks = false;
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("No se guardó la biblioteca. ") + error.Message; }
    }

    private async void AddFolder(object sender, RoutedEventArgs args)
    {
        if (scanning) return;
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalTextExtension.Translate("Elige una carpeta local o una carpeta compartida SMB"),
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            await Scan(new[] { picker.SelectedPath }, true);
    }

    private async void AddDemos(object sender, RoutedEventArgs args) =>
        await Scan(new[] { Path.Combine(AppContext.BaseDirectory, "Demo") }, true);

    private async void Rescan(object sender, RoutedEventArgs args) =>
        await Scan(library.Folders.ToArray(), false);

    private async Task Scan(string[] folders, bool add)
    {
        if (scanning || folders.Length == 0) return;
        scanning = true;
        Status.Text = LocalTextExtension.Translate("Leyendo carpetas…");
        var indexed = library.Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        try
        {
            foreach (string folder in folders)
            {
                try
                {
                    var result = await Task.Run(() => ReadFolder(folder));
                    foreach (var track in result.Tracks) indexed[track.Path] = track;
                    if (result.Errors == 0)
                    {
                        var seen = result.Tracks.Select(t => t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        string prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
                        foreach (string path in indexed.Keys.Where(path =>
                            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !seen.Contains(path)).ToArray())
                            indexed.Remove(path);
                    }
                    else errors.Add($"{result.Errors} archivos con errores en {folder}");
                    if (add && !library.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        library.Folders.Add(folder);
                }
                catch (Exception error) { errors.Add(error.Message); }
            }
            library.Tracks = indexed.Values.ToList();
            RefreshFilters();
            RefreshRows();
            Status.Text = errors.Count == 0
                ? LocalTextExtension.Format("{0:N0} canciones en la biblioteca", indexed.Count)
                : LocalTextExtension.Format("Escaneo parcial. Se conservaron las referencias de las carpetas afectadas. {0}", errors[0]);
            Save(true);
        }
        finally { scanning = false; }
    }

    private static (List<Track> Tracks, int Errors) ReadFolder(string folder)
    {
        var tracks = new List<Track>();
        int errors = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            if (!Extensions.Contains(Path.GetExtension(path))) continue;
            try
            {
                using var file = TagLib.File.Create(path);
                var tag = file.Tag;
                string name = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title;
                tracks.Add(new Track(path, name, tag.FirstPerformer ?? "Artista desconocido",
                    tag.Album ?? "Sin álbum", tag.FirstAlbumArtist ?? "", tag.FirstGenre ?? "",
                    tag.Year, tag.Disc, tag.Track, (int)file.Properties.Duration.TotalSeconds));
            }
            catch (Exception) { errors++; }
        }
        return (tracks, errors);
    }

    private void RefreshFilters()
    {
        string genre = Genre.SelectedItem as string;
        string year = Year.SelectedItem as string;
        Genre.ItemsSource = new[] { LocalTextExtension.Translate("Todos los géneros") }.Concat(library.Tracks.Select(t => t.Genre)
            .Where(g => g.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(g => g)).ToArray();
        Year.ItemsSource = new[] { LocalTextExtension.Translate("Todos los años") }.Concat(library.Tracks.Select(t => t.Year)
            .Where(y => y > 0).Distinct().OrderByDescending(y => y).Select(y => y.ToString())).ToArray();
        Genre.SelectedItem = genre;
        Year.SelectedItem = year;
        if (Genre.SelectedIndex < 0) Genre.SelectedIndex = 0;
        if (Year.SelectedIndex < 0) Year.SelectedIndex = 0;
    }

    private void Navigate(object sender, SelectionChangedEventArgs args)
    {
        if (!ready) return;
        collection = null;
        collectionHistory.Clear();
        galleryPage = 0;
        Heading.Text = (Navigation.SelectedItem as ListBoxItem)?.Content?.ToString() ?? LocalTextExtension.Translate("Canciones");
        RefreshRows();
    }

    private void Filter(object sender, RoutedEventArgs args)
    {
        if (!ready) return;
        galleryPage = 0;
        if (ReferenceEquals(sender, Search) && collection != null && !string.IsNullOrWhiteSpace(Search.Text))
        {
            if (Navigation.SelectedIndex != 0) Navigation.SelectedIndex = 0;
            else { collection = null; collectionHistory.Clear(); UpdateCollectionHeading(); }
            return;
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        UpdateCollectionHeader();
        Rows.Tag = collection == null && Navigation.SelectedIndex is 1 or 2 ? "Gallery" : null;
        GalleryPager.Visibility = Visibility.Collapsed;
        QueueTools.Visibility = Navigation.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPicker.Visibility = NewPlaylistButton.Visibility = Navigation.SelectedIndex == 6
            ? Visibility.Visible : Visibility.Collapsed;
        BackToCollection.Visibility = collection == null ? Visibility.Collapsed : Visibility.Visible;
        if (collection != null)
        {
            Rows.ItemsSource = collection.Tracks.Select((track, index) => new LibraryRow(
                albumPage ? $"{index + 1:00}  {track.Name}" : track.Name,
                track.Seconds > 0 ? $"{track.Detail}  ·  {TimeSpan.FromSeconds(track.Seconds):m\\:ss}" : track.Detail,
                new[] { track })).ToArray();
            return;
        }
        var lookup = library.Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        IEnumerable<Track> tracks = Navigation.SelectedIndex switch
        {
            3 => library.Tracks.Where(t => library.Favorites.Contains(t.Path)),
            4 => queue,
            5 => library.History.Where(lookup.ContainsKey).Select(path => lookup[path]),
            6 => ActivePlaylist.Where(lookup.ContainsKey).Select(path => lookup[path]),
            _ => library.Tracks
        };
        string query = Search.Text.Trim();
        tracks = tracks.Where(t =>
            (Genre.SelectedIndex <= 0 || t.Genre.Equals((string)Genre.SelectedItem, StringComparison.CurrentCultureIgnoreCase)) &&
            (Year.SelectedIndex <= 0 || t.Year.ToString() == (string)Year.SelectedItem) &&
            (query.Length == 0 || (t.Name + " " + t.Detail + " " + t.Genre).Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        if (Navigation.SelectedIndex == 1)
        {
            var albums = tracks.GroupBy(t => (t.Album, Artist: t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist))
                .Select(g => new { g.Key, Tracks = g.OrderBy(t => t.Disc).ThenBy(t => t.Number).ThenBy(t => t.Name).ToArray() });
            var sorted = Sort.SelectedIndex switch
            {
                1 => albums.OrderBy(g => g.Key.Artist).ThenBy(g => g.Key.Album),
                2 => albums.OrderByDescending(g => g.Tracks[0].Year).ThenBy(g => g.Key.Album),
                _ => albums.OrderBy(g => g.Key.Album).ThenBy(g => g.Key.Artist)
            };
            SetGalleryRows(sorted.Select(g => new LibraryRow(g.Key.Album,
                $"{g.Key.Artist}  ·  {g.Tracks.Length} pistas  ·  {g.Tracks[0].Year}", g.Tracks)).ToArray());
            return;
        }

        if (Navigation.SelectedIndex == 2)
        {
            SetGalleryRows(tracks.GroupBy(t => t.Artist).OrderBy(g => g.Key)
                .Select(g => new LibraryRow(g.Key, $"{g.Count()} pistas", g.OrderBy(t => t.Album).ThenBy(t => t.Number).ToArray())).ToArray());
            return;
        }

        if (Navigation.SelectedIndex < 4)
            tracks = Sort.SelectedIndex switch
            {
                1 => tracks.OrderBy(t => t.Artist).ThenBy(t => t.Name),
                2 => tracks.OrderByDescending(t => t.Year).ThenBy(t => t.Name),
                _ => tracks.OrderBy(t => t.Name)
            };
        Rows.ItemsSource = tracks.Select(t => new LibraryRow(t.Name, t.Detail, new[] { t })).ToArray();
    }

    private Track Selected => (Rows.SelectedItem as LibraryRow)?.Tracks.FirstOrDefault() ?? current;

    private void Activate(object sender, MouseButtonEventArgs args) => ActivateSelection();

    private void CloseCollection(object sender, RoutedEventArgs args)
    {
        if (collectionHistory.TryPop(out var previousPage))
        {
            collection = previousPage.Collection;
            albumPage = previousPage.Album;
            UpdateCollectionHeading();
            if (previousPage.Selection < Rows.Items.Count) Rows.SelectedIndex = previousPage.Selection;
        }
        else
        {
            collection = null;
            UpdateCollectionHeading();
        }
    }

    private void UpdateCollectionHeading()
    {
        Heading.Text = collection?.Name ??
            (Navigation.SelectedItem as ListBoxItem)?.Content?.ToString() ?? LocalTextExtension.Translate("Canciones");
        RefreshRows();
    }

    private void OpenCollection(LibraryRow row, bool album)
    {
        collectionHistory.Push((collection, Rows.SelectedIndex, albumPage));
        albumPage = album;
        collection = row;
        UpdateCollectionHeading();
        Rows.SelectedIndex = 0;
        Rows.Focus();
    }

    private void BrowseRelatedArtist(object sender, RoutedEventArgs args)
    {
        if (Rows.SelectedItem is not LibraryRow row) return;
        var track = row.Tracks[0];
        var tracks = library.Tracks.Where(item => string.Equals(item.Artist, track.Artist, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Album).ThenBy(item => item.Disc).ThenBy(item => item.Number).ToArray();
        if (tracks.Length > 0) OpenCollection(new LibraryRow(track.Artist, "", tracks), false);
    }

    private static string AlbumOwner(Track track) => string.IsNullOrEmpty(track.AlbumArtist) ? track.Artist : track.AlbumArtist;

    private void BrowseRelatedAlbum(object sender, RoutedEventArgs args)
    {
        if (Rows.SelectedItem is not LibraryRow row) return;
        var track = row.Tracks[0];
        var tracks = library.Tracks.Where(item => string.Equals(item.Album, track.Album, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AlbumOwner(item), AlbumOwner(track), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Disc).ThenBy(item => item.Number).ThenBy(item => item.Name).ToArray();
        if (tracks.Length > 0) OpenCollection(new LibraryRow(track.Album, AlbumOwner(track), tracks), true);
    }
    private void ActivateSelection()
    {
        if (Rows.SelectedItem is not LibraryRow row) return;
        if (collection == null && Navigation.SelectedIndex is 1 or 2)
        {
            OpenCollection(row, Navigation.SelectedIndex == 1);
            return;
        }
        context = row.Tracks.Length > 1
            ? row.Tracks
            : ((IEnumerable<LibraryRow>)Rows.ItemsSource).SelectMany(r => r.Tracks).ToArray();
        originalContext = context;
        ReorderPlayback(row.Tracks[0]);
        Play(row.Tracks[0]);
    }

    private async void LoadPresentation(Track track)
    {
        Cover.Source = null;
        AudioFormat.Text = Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant();
        UpdateMini();
        Shell.Background = new LinearGradientBrush(Color.FromRgb(30, 25, 40), Color.FromRgb(17, 16, 23), 115);
        SetLyrics("");
        var result = await Task.Run(() =>
        {
            string lyrics = "";
            BitmapImage image = null;
            var format = new List<string> { Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant() };
            try
            {
                string lrc = Path.ChangeExtension(track.Path, ".lrc");
                if (File.Exists(lrc)) lyrics = File.ReadAllText(lrc);
                using var file = TagLib.File.Create(track.Path);
                if (lyrics.Length == 0) lyrics = file.Tag.Lyrics ?? "";
                var audio = file.Properties;
                if (audio.BitsPerSample > 0) format.Add($"{audio.BitsPerSample} bit");
                if (audio.AudioSampleRate > 0) format.Add($"{audio.AudioSampleRate / 1000d:0.##} kHz");
                if (audio.AudioBitrate > 0) format.Add($"{audio.AudioBitrate} kb/s");
                var picture = file.Tag.Pictures.FirstOrDefault();
                if (picture != null)
                {
                    using var stream = new MemoryStream(picture.Data.Data);
                    image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.DecodePixelWidth = 500;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                }
            }
            catch (Exception) { }
            return (lyrics, image, format: string.Join(" · ", format));
        });
        if (current != track) return;
        Cover.Source = result.image;
        AudioFormat.Text = result.format;
        UpdateMini();
        if (artworkColors && result.image is { IsFrozen: true })
        {
            var pixel = new byte[4];
            var sample = result.image;
            var rgb = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
            rgb.CopyPixels(new Int32Rect(rgb.PixelWidth / 2, rgb.PixelHeight / 2, 1, 1), pixel, 4, 0);
            var accent = Color.FromRgb((byte)(24 + pixel[2] / 4), (byte)(24 + pixel[1] / 4), (byte)(32 + pixel[0] / 4));
            Shell.Background = new LinearGradientBrush(accent, Color.FromRgb(19, 19, 29), 120);
        }
        SetLyrics(result.lyrics);
    }

    private void SetLyrics(string text)
    {
        sourceLyrics = text;
        lyricRevision++;
        translatedLines = null;

        lyricLines = text.Split('\n').SelectMany(line =>
        {
            var stamps = Regex.Matches(line, @"\[(\d+):(\d+(?:[.:]\d+)?)\]");
            string words = Regex.Replace(line, @"\[[^\]]+\]", "").Trim();
            return stamps.Cast<Match>().Select(match =>
                (int.Parse(match.Groups[1].Value) * 60 + double.Parse(match.Groups[2].Value.Replace(':', '.'),
                    System.Globalization.CultureInfo.InvariantCulture), words));
        }).OrderBy(line => line.Item1).ToArray();
        lyricDisplayIndex = int.MinValue;
        UpdateLyricDisplay(player.Position.TotalSeconds);
    }

    private async void LookupLyrics(object sender, RoutedEventArgs args)
    {
        Track track = current;
        if (track == null) return;
        if (MessageBox.Show(this, LocalTextExtension.Translate("Se enviarán el título, artista y álbum a LRCLIB para buscar la letra."),
            LocalTextExtension.Translate("Buscar letras"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            string text = await LyricsLookup.Find(Http, track, () => ReferenceEquals(current, track));
            if (!ReferenceEquals(current, track)) return;
            if (!string.IsNullOrWhiteSpace(text)) SetLyrics(text);
            else Status.Text = LocalTextExtension.Translate("No se encontró una letra para esta pista.");
        }
        catch (Exception error)
        {
            if (ReferenceEquals(current, track)) Status.Text = LocalTextExtension.Translate("Error al buscar letras. ") + error.Message;
        }
    }

    private void Tick()
    {
        if (sleepAt is DateTime deadline && DateTime.UtcNow >= deadline)
        {
            sleepAt = null;
            player.Pause();
            SetPlaying(false);
        }
        var spectrum = player.Spectrum;
        for (int band = 0; animateSpectrum && SystemParameters.ClientAreaAnimation && band < spectrumBars.Length; band++)
        {
            double target = playing && band < spectrum.Length ? 2 + 26 * spectrum[band] : 2;
            spectrumBars[band].Height = Math.Max(target, spectrumBars[band].Height * .65);
        }
        if (!opened) return;
        double position = player.Position.TotalSeconds;
        double duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
        updatingSeek = true;
        Seek.Maximum = duration;
        if (!Seek.IsMouseCaptureWithin) Seek.Value = position;
        updatingSeek = false;
        TimeLabel.Text = $"{TimeSpan.FromSeconds(position):m\\:ss} / {TimeSpan.FromSeconds(duration):m\\:ss}";
        UpdateLyricDisplay(position);
    }

    private void SetPlaying(bool value)
    {
        playing = value;
        PlayButton.Content = value ? "⏸" : "▶";
        if (miniToggle != null) miniToggle.Content = PlayButton.Content;
    }

    private void Toggle(object sender, RoutedEventArgs args)
    {
        if (pendingTrack != null) { CancelPendingOpen(); return; }
        if (current == null) { ActivateSelection(); return; }
        if (!opened) { Play(current); return; }
        if (playing) player.Pause(); else player.Play();
        SetPlaying(!playing);
    }

    private void Advance(bool endOfTrack = false)
    {
        if (endOfTrack && repeat == RepeatMode.Track && current != null)
        {
            Play(current, false);
            return;
        }
        if (queue.Count > 0)
        {
            var next = queue[0];
            queue.RemoveAt(0);
            Play(next);
            if (Navigation.SelectedIndex == 4) RefreshRows();
            return;
        }
        int index = Array.IndexOf(context, pendingTrack ?? current);
        if (index + 1 < context.Length) Play(context[index + 1]);
        else if (repeat == RepeatMode.Collection && context.Length > 0) Play(context[0]);
        else { CancelPendingOpen(); player.Pause(); SetPlaying(false); }
    }

    private void ShowListeningMenu(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ReorderPlayback(Track first)
    {
        if (!shuffle || first == null)
        {
            context = originalContext;
            return;
        }
        var remaining = originalContext.Where(track => track != first).ToArray();
        Random.Shared.Shuffle(remaining);
        context = new[] { first }.Concat(remaining).ToArray();
    }

    private void ToggleShuffle(object sender, RoutedEventArgs args)
    {
        shuffle = !shuffle;
        ReorderPlayback(current);
        MarkPlaybackToggle(ShuffleButton, shuffle);
    }

    private void ToggleRepeat(object sender, RoutedEventArgs args)
    {
        repeat = repeat switch
        {
            RepeatMode.Off => RepeatMode.Collection,
            RepeatMode.Collection => RepeatMode.Track,
            _ => RepeatMode.Off
        };
        UpdateRepeatButton();
    }

    private void UpdateRepeatButton()
    {
        string label = LocalTextExtension.Translate(repeat switch
        {
            RepeatMode.Track => "Repetir pista",
            RepeatMode.Collection => "Repetir colección",
            _ => "Repetición desactivada"
        });
        RepeatButton.Content = repeat == RepeatMode.Track ? "\uE8ED" : "\uE8EE";
        RepeatButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(RepeatButton, label);
        MarkPlaybackToggle(RepeatButton, repeat != RepeatMode.Off);
    }

    private static void MarkPlaybackToggle(Button button, bool active)
    {
        button.Background = active ? new SolidColorBrush(Color.FromRgb(74, 58, 99)) : Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "On" : "Off");
    }

    private void Next(object sender, RoutedEventArgs args) => Advance();

    private void Previous(object sender, RoutedEventArgs args)
    {
        if (opened && player.Position.TotalSeconds > 3) player.Position = TimeSpan.Zero;
        else if (previous.TryPop(out Track track)) Play(track, false);
    }

    private void UpdateVolumeControls()
    {
        VolumeControls.Visibility = player.StrictDirect ? Visibility.Collapsed : Visibility.Visible;
        EnableVolumeButton.Visibility = player.StrictDirect ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EnableAdjustableVolume(object sender, RoutedEventArgs args)
    {
        player.StrictDirect = false;
        UpdateVolumeControls();
        _ = ReopenOutput();
        if (!smokeTest) SaveAudioSettings();
    }

    private void ChangeVolume(object sender, RoutedPropertyChangedEventArgs<double> args) => player.Volume = args.NewValue;

    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (opened && !updatingSeek) player.Position = TimeSpan.FromSeconds(args.NewValue);
    }

    private void Favorite(object sender, RoutedEventArgs args)
    {
        if (Selected is not Track track) return;
        bool added = library.Favorites.Add(track.Path);
        if (!added) library.Favorites.Remove(track.Path);
        Status.Text = added ? LocalTextExtension.Format("Añadida a favoritos: {0}", track.Name) : LocalTextExtension.Format("Retirada de favoritos: {0}", track.Name);
        Save();
        if (Navigation.SelectedIndex == 3) RefreshRows();
    }

    private void UpdatePlaylistPicker()
    {
        PlaylistPicker.ItemsSource = new[] { "Mi lista" }.Concat(library.Playlists.Keys.OrderBy(name => name)).ToArray();
        PlaylistPicker.SelectedItem = playlistName;
    }

    private void SelectPlaylist(object sender, SelectionChangedEventArgs args)
    {
        if (PlaylistPicker.SelectedItem is not string name) return;
        playlistName = name;
        if (ready) RefreshRows();
    }

    private void NewPlaylist(object sender, RoutedEventArgs args)
    {
        var input = new TextBox { Width = 290, Margin = new Thickness(0, 6, 0, 12), MaxLength = 80 };
        var create = new Button { Content = LocalTextExtension.Translate("Crear"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Nombre de la lista") });
        content.Children.Add(input);
        content.Children.Add(create);
        var dialog = new Window { Title = LocalTextExtension.Translate("Nueva lista"), Owner = this, Content = content,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)) };
        create.Click += (_, _) =>
        {
            string name = input.Text.Trim();
            if (name.Length == 0 || PlaylistPicker.Items.Cast<string>().Contains(name, StringComparer.CurrentCultureIgnoreCase))
            {
                MessageBox.Show(dialog, LocalTextExtension.Translate("Escribe un nombre distinto para la lista."), LocalTextExtension.Translate("Nueva lista"));
                return;
            }
            library.Playlists.Add(name, new List<string>());
            playlistName = name;
            UpdatePlaylistPicker();
            Save();
            dialog.Close();
        };
        dialog.Loaded += (_, _) => input.Focus();
        dialog.ShowDialog();
    }

    private void Playlist(object sender, RoutedEventArgs args)
    {
        if (Selected is not Track track) return;
        bool removed = ActivePlaylist.Remove(track.Path);
        if (!removed) ActivePlaylist.Add(track.Path);
        Status.Text = removed ? LocalTextExtension.Format("Retirada de {0}: {1}", playlistName, track.Name) : LocalTextExtension.Format("Añadida a {0}: {1}", playlistName, track.Name);
        Save();
        if (Navigation.SelectedIndex == 6) RefreshRows();
    }

    private void Enqueue(object sender, RoutedEventArgs args)
    {
        if (Selected is not Track track) return;
        if (queue.Any(item => string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase))) return;
        queue.Add(track);
        Status.Text = LocalTextExtension.Format("Añadida a la cola: {0}", track.Name);
        if (Navigation.SelectedIndex == 4) RefreshRows();
    }

    private async void Inspect(object sender, RoutedEventArgs args)
    {
        if (Selected is not Track track) return;
        string information = await Task.Run(() =>
        {
            try
            {
                using var file = TagLib.File.Create(track.Path);
                var audio = file.Properties;
                return $"{Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant()} · {audio.Description}\n" +
                    $"{audio.AudioBitrate} kb/s · {audio.AudioSampleRate:N0} Hz · {audio.AudioChannels} canales\n" +
                    (audio.BitsPerSample > 0 ? $"{audio.BitsPerSample} bits\n" : "") +
                    $"{new FileInfo(track.Path).Length:N0} bytes\n\n{track.Path}";
            }
            catch (Exception error) { return error.Message; }
        });
        MessageBox.Show(this, information, track.Name);
    }

    private void LoadAudioSettings()
    {
        try
        {
            if (!File.Exists(AudioSettingsFile)) return;
            var settings = JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(AudioSettingsFile));
            if (settings == null) return;
            Volume.Value = Math.Clamp(settings.Volume, 0, 1);
            player.OutputId = settings.OutputId;
            player.Exclusive = settings.Exclusive;
            player.Balance = settings.Balance;
            eqEnabled = settings.EqEnabled;
            player.Direct = settings.Direct;
            player.StrictDirect = settings.StrictDirect;
            shuffle = settings.Shuffle;
            rememberPosition = settings.RememberPosition;
            repeat = Enum.IsDefined(settings.Repeat) ? settings.Repeat : RepeatMode.Off;
            MarkPlaybackToggle(ShuffleButton, shuffle);
            UpdateRepeatButton();
            UpdateVolumeControls();
            player.Speed = Math.Clamp(settings.Speed, .75, 2);
            artworkColors = settings.ArtworkColors;
            animateSpectrum = settings.AnimateSpectrum;
            SpectrumView.Visibility = animateSpectrum ? Visibility.Visible : Visibility.Collapsed;
            if (settings.Gains?.Length == gains.Length)
            {
                for (int i = 0; i < gains.Length; i++) gains[i] = Math.Clamp(settings.Gains[i], -12, 12);
                ApplyEqualizer();
            }
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("No se pudieron leer los ajustes de audio. ") + error.Message; }
    }

    private void SaveAudioSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(AudioSettingsFile + ".tmp", JsonSerializer.Serialize(
                new AudioSettings(Volume.Value, player.OutputId, player.Exclusive, gains, player.Balance, eqEnabled, player.Direct, artworkColors, animateSpectrum, player.Speed, player.StrictDirect, shuffle, repeat, rememberPosition)));
            File.Move(AudioSettingsFile + ".tmp", AudioSettingsFile, true);
        }
        catch (Exception error) { Status.Text = LocalTextExtension.Translate("No se pudieron guardar los ajustes de audio. ") + error.Message; }
    }

    private static readonly float[] FlatGains = new float[AudioEngine.Frequencies.Length];
    private void ApplyEqualizer() => player.SetGains(eqEnabled ? gains : FlatGains);

    private void Settings(object sender, RoutedEventArgs args)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Ecualizador"), FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Diez bandas · −12 a +12 dB"), Margin = new Thickness(0, 4, 0, 14) });
        var presets = new (string Name, float[] Values)[]
        {
            ("Plano", FlatGains),
            ("Rock", new float[] { 4, 3, 2, 0, -1, -1, 1, 3, 4, 4 }),
            ("Jazz", new float[] { 2, 2, 1, 1, -1, -1, 0, 2, 3, 3 }),
            ("Clásica", new float[] { 3, 2, 1, 0, -1, 0, 0, 1, 2, 3 }),
            ("Electrónica", new float[] { 5, 4, 2, 0, -2, 0, 2, 3, 4, 5 }),
            ("Vocal", new float[] { -2, -2, -1, 0, 2, 3, 3, 2, 0, -1 })
        };
        var eqToggle = new CheckBox { Content = LocalTextExtension.Translate("Activar ecualizador"),
            IsChecked = eqEnabled, Foreground = Brushes.White, Margin = new Thickness(0, 0, 20, 0),
            VerticalAlignment = VerticalAlignment.Center };
        var preset = new ComboBox { Width = 165, ItemsSource = presets.Select(item => LocalTextExtension.Translate(item.Name)).ToArray(),
            SelectedIndex = Array.FindIndex(presets, item => item.Values.SequenceEqual(gains)),
            Style = (Style)FindResource("FilterCombo") };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        toolbar.Children.Add(eqToggle);
        toolbar.Children.Add(preset);
        panel.Children.Add(toolbar);
        var sliders = new Slider[gains.Length];
        var bands = new StackPanel { Orientation = Orientation.Horizontal, IsEnabled = eqEnabled };
        bool applyingPreset = false;
        eqToggle.Click += (_, _) =>
        {
            eqEnabled = eqToggle.IsChecked == true;
            bands.IsEnabled = eqEnabled;
            ApplyEqualizer();
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedIndex < 0) return;
            applyingPreset = true;
            var values = presets[preset.SelectedIndex].Values;
            for (int i = 0; i < values.Length; i++) sliders[i].Value = values[i];
            applyingPreset = false;
            ApplyEqualizer();
        };
        for (int index = 0; index < gains.Length; index++)
        {
            int band = index;
            int frequency = AudioEngine.Frequencies[band];
            var column = new StackPanel { Width = 52 };
            var slider = new Slider { Orientation = Orientation.Vertical, Height = 135, Minimum = -12, Maximum = 12,
                Value = gains[band], TickFrequency = 1, IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Center };
            sliders[index] = slider;
            var value = new TextBlock { Text = $"{gains[band]:0} dB", HorizontalAlignment = HorizontalAlignment.Center };
            slider.ValueChanged += (_, change) =>
            {
                gains[band] = (float)change.NewValue;
                if (!applyingPreset) { preset.SelectedIndex = -1; ApplyEqualizer(); }
                value.Text = $"{change.NewValue:0} dB";
            };
            column.Children.Add(slider);
            column.Children.Add(new TextBlock { Text = frequency >= 1000 ? $"{frequency / 1000}k" : frequency.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center });
            column.Children.Add(value);
            bands.Children.Add(column);
        }
        panel.Children.Add(new Border { Child = bands, Padding = new Thickness(8, 12, 8, 12),
            CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(49, 44, 51)) });
        var balanceLabel = new TextBlock { Text = LocalTextExtension.Translate("Balance estéreo"),
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 4) };
        var balanceSlider = new Slider { Minimum = -1, Maximum = 1, Value = player.Balance,
            TickFrequency = .05, IsSnapToTickEnabled = true };
        var center = new Button { Content = LocalTextExtension.Translate("Centrar"), HorizontalAlignment = HorizontalAlignment.Left };
        balanceSlider.ValueChanged += (_, change) => player.Balance = (float)change.NewValue;
        center.Click += (_, _) => balanceSlider.Value = 0;
        panel.Children.Add(balanceLabel);
        panel.Children.Add(balanceSlider);
        panel.Children.Add(center);
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Salida de audio · WASAPI"), Margin = new Thickness(0, 20, 0, 5) });
        var choices = new ComboBox { Style = (Style)FindResource("FilterCombo"), ItemTemplate = (DataTemplate)FindResource("AudioDeviceLabel"), SelectedValuePath = "Id" };
        try
        {
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var outputs = devices.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active)
                .Select(device =>
                {
                    using (device) return new { Name = device.FriendlyName, Id = device.ID };
                }).ToList();
            outputs.Insert(0, new { Name = LocalTextExtension.Translate("Predeterminada del sistema"), Id = (string)null });
            choices.ItemsSource = outputs;
            choices.SelectedIndex = Math.Max(0, outputs.FindIndex(item => item.Id == player.OutputId));
            choices.SelectionChanged += (_, _) =>
            {
                player.OutputId = choices.SelectedValue as string;
                ReopenOutput();
            };
        }
        catch (Exception error) { choices.Items.Add(error.Message); choices.SelectedIndex = 0; choices.IsEnabled = false; }
        panel.Children.Add(choices);
        var exclusive = new CheckBox { Content = LocalTextExtension.Translate("Solicitar acceso exclusivo"), IsChecked = player.Exclusive,
            Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 4) };
        var mode = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        void UpdateMode() => mode.Text = player.OutputNotice ?? (opened
            ? (player.IsExclusive ? LocalTextExtension.Translate("Salida activa en modo exclusivo.") : LocalTextExtension.Translate("Salida activa en modo compartido."))
            : LocalTextExtension.Translate("El modo exclusivo se solicita al abrir una pista."));
        exclusive.Click += (_, _) =>
        {
            player.Exclusive = exclusive.IsChecked == true;
            ReopenOutput();
            UpdateMode();
        };
        choices.SelectionChanged += (_, _) => UpdateMode();
        UpdateMode();
        panel.Children.Add(exclusive);
        panel.Children.Add(mode);
        var speeds = new[] { .75, 1, 1.25, 1.5, 2 };
        var speedPicker = new ComboBox { Style = (Style)FindResource("FilterCombo"),
            ItemsSource = speeds.Select(speed => $"{speed:0.##}×").ToArray(),
            SelectedIndex = Array.IndexOf(speeds, player.Speed), IsEnabled = !player.Direct && !player.StrictDirect };
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Velocidad de reproducción"),
            Margin = new Thickness(0, 14, 0, 4) });
        panel.Children.Add(speedPicker);
        speedPicker.SelectionChanged += (_, _) =>
        {
            if (speedPicker.SelectedIndex < 0) return;
            player.Speed = speeds[speedPicker.SelectedIndex];
            ReopenOutput();
        };
        AddPreference(LocalTextExtension.Translate("PCM directo exclusivo (WAV, AIFF y FLAC)"), player.StrictDirect, enabled =>
        {
            player.StrictDirect = enabled;
            UpdateVolumeControls();
            speedPicker.IsEnabled = !enabled && !player.Direct;
            ReopenOutput();
            UpdateMode();
        });
        AddPreference("Omitir ecualizador, balance y velocidad", player.Direct, enabled =>
        {
            player.Direct = enabled;
            speedPicker.IsEnabled = !enabled && !player.StrictDirect;
            ReopenOutput();
        });
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Apariencia"),
            FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 4) });
        AddPreference("Colores de la carátula", artworkColors, enabled =>
        {
            artworkColors = enabled;
            if (current != null) LoadPresentation(current);
        });
        AddPreference("Espectro animado", animateSpectrum, enabled =>
        {
            animateSpectrum = enabled;
            SpectrumView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        void AddPreference(string label, bool value, Action<bool> change)
        {
            var control = new CheckBox { Content = LocalTextExtension.Translate(label), IsChecked = value,
                Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 2) };
            control.Click += (_, _) => change(control.IsChecked == true);
            panel.Children.Add(control);
        }
        var sleep = new Button { Content = LocalTextExtension.Translate("Pausar en 30 minutos"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        sleep.Content = sleepAt.HasValue ? LocalTextExtension.Translate("Cancelar temporizador") : LocalTextExtension.Translate("Pausar en 30 minutos");
        sleep.Click += (_, _) =>
        {
            sleepAt = sleepAt.HasValue ? null : DateTime.UtcNow.AddMinutes(30);
            sleep.Content = sleepAt.HasValue ? LocalTextExtension.Translate("Cancelar temporizador") : LocalTextExtension.Translate("Pausar en 30 minutos");
        };
        panel.Children.Add(sleep);
        AddPreference("Recordar posición de la pista", rememberPosition, enabled => rememberPosition = enabled);
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Idioma de interfaz"), Margin = new Thickness(0, 18, 0, 5) });
        var codes = new[] { "es", "en", "pt" };
        var language = new ComboBox { Style = (Style)FindResource("FilterCombo"), ItemsSource = new[] { "Español", "English", "Português" },
            SelectedIndex = Array.IndexOf(codes, LocalTextExtension.Language) };
        var languageNotice = new TextBlock { Margin = new Thickness(0, 5, 0, 0) };
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex < 0) return;
            try
            {
                LocalTextExtension.SaveLanguage(codes[language.SelectedIndex]);
                languageNotice.Text = LocalTextExtension.Translate("El idioma se aplicará al volver a abrir Prisma Music.");
            }
            catch (Exception error) { languageNotice.Text = error.Message; }
        };
        panel.Children.Add(language);
        panel.Children.Add(languageNotice);
        var welcome = new Button { Content = LocalTextExtension.Translate("Primeros pasos"),
            Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(welcome);
        var settingsWindow = new Window { Owner = this, Title = LocalTextExtension.Translate("Configuración · Prisma Music"), Width = 630, Height = 710, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel, Background = new SolidColorBrush(Color.FromRgb(38, 36, 41)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)) };
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_SETTINGS_PREVIEW") is string preview)
        {
            settingsWindow.ContentRendered += (_, _) =>
            {
                var content = (FrameworkElement)settingsWindow.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(preview);
                png.Save(stream);
                settingsWindow.Close();
            };
        }
        settingsWindow.SourceInitialized += (_, _) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(settingsWindow).Handle, 20, ref dark, sizeof(int)); };
        welcome.Click += (_, _) => { settingsWindow.Close(); ShowWelcome(this, new RoutedEventArgs()); };
        settingsWindow.ShowDialog();
        if (!smokeTest) SaveAudioSettings();
    }

    private void Mini(object sender, RoutedEventArgs args)
    {
        if (mini != null) { mini.Activate(); return; }
        var coverPanel = new Grid();
        coverPanel.Children.Add(new TextBlock { Text = "♫", FontSize = 32, Foreground = Brushes.PeachPuff,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        miniCover = new Image { Stretch = Stretch.UniformToFill };
        coverPanel.Children.Add(miniCover);
        var tile = new Border { Width = 86, Height = 86, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(62, 43, 57)), Child = coverPanel, ClipToBounds = true,
            Margin = new Thickness(0, 0, 14, 0) };

        miniTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        miniArtist = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        miniFormat = new TextBlock { Foreground = Brushes.PeachPuff, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var back = new Button { Content = "⏮", Style = (Style)FindResource("CompactButton"), ToolTip = LocalTextExtension.Translate("Anterior") };
        miniToggle = new Button { Content = playing ? "⏸" : "▶", Background = Brushes.PeachPuff,
            Foreground = Brushes.Black, Padding = new Thickness(14, 6, 14, 6), ToolTip = LocalTextExtension.Translate("Reproducir o pausar") };
        var forward = new Button { Content = "⏭", Style = (Style)FindResource("CompactButton"), ToolTip = LocalTextExtension.Translate("Siguiente") };
        back.Click += Previous;
        miniToggle.Click += Toggle;
        forward.Click += Next;
        buttons.Children.Add(back);
        buttons.Children.Add(miniToggle);
        buttons.Children.Add(forward);
        var text = new StackPanel();
        text.Children.Add(miniTitle);
        text.Children.Add(miniArtist);
        text.Children.Add(miniFormat);
        text.Children.Add(buttons);
        var panel = new DockPanel { Margin = new Thickness(13) };
        DockPanel.SetDock(tile, Dock.Left);
        panel.Children.Add(tile);
        panel.Children.Add(text);
        mini = new Window { Owner = this, Title = "Prisma Music · Mini", Width = 370, Height = 155,
            ResizeMode = ResizeMode.NoResize, Topmost = true, Content = panel,
            Background = new SolidColorBrush(Color.FromRgb(38, 36, 41)) };
        mini.Closed += (_, _) => { mini = null; miniTitle = null; miniArtist = null; miniFormat = null; miniCover = null; miniToggle = null; };
        UpdateMini();
        mini.Show();
    }

    private void UpdateMini()
    {
        if (miniTitle == null) return;
        miniTitle.Text = current?.Name ?? "Prisma Music";
        miniArtist.Text = current?.Artist ?? "";
        miniFormat.Text = AudioFormat.Text;
        miniCover.Source = Cover.Source;
    }
    private void Shortcuts(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.F)
        {
            Search.Focus();
            Search.SelectAll();
            args.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.I) Inspect(sender, args);
        else if (args.Key == Key.F11 || (args.Key == Key.Escape && listeningRoom != null)) Fullscreen(sender, args);
        else if (args.Key == Key.Escape && pendingTrack != null) { CancelPendingOpen(); args.Handled = true; }
        else if (args.Key == Key.Escape && Search.IsKeyboardFocusWithin)
        {
            Rows.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && collection != null)
        {
            CloseCollection(sender, args);
            Rows.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && Rows.IsKeyboardFocusWithin) ActivateSelection();
    }
}
























































