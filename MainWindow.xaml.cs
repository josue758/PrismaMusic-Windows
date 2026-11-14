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
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrismaMusic.Windows");
    private static readonly string LibraryFile = Path.Combine(DataDirectory, "library.json");
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg", ".opus" };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly string AudioSettingsFile = Path.Combine(DataDirectory, "audio-settings.json");
    private sealed record AudioSettings(double Volume, string OutputId, bool Exclusive, float[] Gains, float Balance = 0, bool EqEnabled = true, bool Direct = false, bool ArtworkColors = true, bool AnimateSpectrum = true, double Speed = 1);
    private readonly bool smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");
    private readonly AudioEngine player = new();
    private readonly List<Track> queue = new();
    private readonly Stack<Track> previous = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Library library = new();
    private Track current;
    private LibraryRow collection;
    private string playlistName = "Mi lista";
    private List<string> ActivePlaylist => playlistName == "Mi lista" ? library.Playlist : library.Playlists[playlistName];
    private readonly Border[] spectrumBars = new Border[AudioEngine.Frequencies.Length];
    private Track[] context = Array.Empty<Track>();
    private bool ready, scanning, playing, opened, updatingSeek;
    private WindowState previousWindowState;
    private (double Time, string Text)[] lyricLines = Array.Empty<(double, string)>();
    private bool eqEnabled = true;
    private bool artworkColors = true, animateSpectrum = true;
    private readonly float[] gains = new float[AudioEngine.Frequencies.Length];
    private DateTime? sleepAt;
    private Window mini;
    private TextBlock miniTitle;

    public MainWindow()
    {
        InitializeComponent();
        for (int index = 0; index < spectrumBars.Length; index++)
        {
            var bar = new Border { Height = 2, CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(255, 178, 143)) };
            spectrumBars[index] = bar;
            SpectrumView.Children.Add(bar);
        }
        try
        {
            if (File.Exists(LibraryFile))
                library = JsonSerializer.Deserialize<Library>(File.ReadAllText(LibraryFile)) ?? new Library();
        }
        catch (Exception error) { Status.Text = "No se pudo leer la biblioteca. " + error.Message; }

        player.Volume = Volume.Value;
        if (!smokeTest) LoadAudioSettings();
        player.MediaOpened += (_, _) =>
        {
            opened = true;
            if (player.OutputNotice != null) Status.Text = player.OutputNotice;
            Tick();
        };
        player.MediaEnded += (_, _) => Advance();
        player.MediaFailed += (_, e) =>
        {
            SetPlaying(false);
            Status.Text = "Windows no pudo reproducir el archivo. " + e.Message;
        };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            if (!smokeTest) { SaveSession(); SaveAudioSettings(); }
            player.Close();
        };
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        UpdatePlaylistPicker();
        Navigation.SelectedIndex = 0;
        Sort.SelectedIndex = 0;
        ready = true;
        Shell.Background = new LinearGradientBrush(Color.FromRgb(37, 37, 37), Color.FromRgb(23, 23, 23), 115);
        if (smokeTest)
            Loaded += async (_, _) => await SmokeTest();
        RefreshFilters();
        RefreshRows();
        if (!smokeTest) Loaded += (_, _) => RestoreSession();
    }

    private async Task SmokeTest()
    {
        try
        {
            string expectedHeading = LocalTextExtension.Language switch { "en" => "Songs", "pt" => "Músicas", _ => "Canciones" };
            if (Heading.Text != expectedHeading)
                throw new InvalidOperationException($"Unexpected heading: {Heading.Text}");

            Console.WriteLine($"Interface: {LocalTextExtension.Language} ({Heading.Text})");
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
                Rows.SelectedIndex = 0;
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
            Play(track, false, 1);
            if (!opened || playing || Math.Abs(player.Position.TotalSeconds - 1) > .15)
                throw new InvalidOperationException("Paused session restore failed.");
            Console.WriteLine("Queue edit and paused resume passed.");
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
            ReopenOutput();
            await Task.Delay(500);
            if (!opened || player.Position.TotalSeconds <= 1.1)
                throw new InvalidOperationException("Tempo playback did not resume after reconfiguration.");
            Console.WriteLine("Tempo: playback, pause, seek and output reopen passed.");
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

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(LibraryFile + ".tmp", JsonSerializer.Serialize(library));
            File.Move(LibraryFile + ".tmp", LibraryFile, true);
        }
        catch (Exception error) { Status.Text = "No se guardó la biblioteca. " + error.Message; }
    }

    private async void AddFolder(object sender, RoutedEventArgs args)
    {
        if (scanning) return;
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Elige una carpeta local o una carpeta compartida SMB",
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
                ? $"{indexed.Count:N0} canciones en la biblioteca"
                : $"Escaneo parcial. Se conservaron las referencias de las carpetas afectadas. {errors[0]}";
            Save();
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
        Heading.Text = (Navigation.SelectedItem as ListBoxItem)?.Content?.ToString() ?? LocalTextExtension.Translate("Canciones");
        RefreshRows();
    }

    private void Filter(object sender, RoutedEventArgs args) { if (ready) RefreshRows(); }

    private void RefreshRows()
    {
        CollectionCount.Text = string.Format(LocalTextExtension.Translate("{0:N0} canciones en tu biblioteca"), library.Tracks.Count);
        QueueTools.Visibility = Navigation.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPicker.Visibility = NewPlaylistButton.Visibility = Navigation.SelectedIndex == 6
            ? Visibility.Visible : Visibility.Collapsed;
        BackToCollection.Visibility = collection == null ? Visibility.Collapsed : Visibility.Visible;
        if (collection != null)
        {
            Rows.ItemsSource = collection.Tracks.Select(t => new LibraryRow(t.Name, t.Detail, new[] { t })).ToArray();
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
            Rows.ItemsSource = sorted.Select(g => new LibraryRow(g.Key.Album,
                $"{g.Key.Artist}  ·  {g.Tracks.Length} pistas  ·  {g.Tracks[0].Year}", g.Tracks)).ToArray();
            return;
        }

        if (Navigation.SelectedIndex == 2)
        {
            Rows.ItemsSource = tracks.GroupBy(t => t.Artist).OrderBy(g => g.Key)
                .Select(g => new LibraryRow(g.Key, $"{g.Count()} pistas", g.OrderBy(t => t.Album).ThenBy(t => t.Number).ToArray())).ToArray();
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
        collection = null;
        Heading.Text = (Navigation.SelectedItem as ListBoxItem)?.Content?.ToString() ?? LocalTextExtension.Translate("Canciones");
        RefreshRows();
    }

    private void ActivateSelection()
    {
        if (Rows.SelectedItem is not LibraryRow row) return;
        if (collection == null && Navigation.SelectedIndex is 1 or 2)
        {
            collection = row;
            Heading.Text = row.Name;
            RefreshRows();
            Rows.SelectedIndex = 0;
            Rows.Focus();
            return;
        }
        context = row.Tracks.Length > 1
            ? row.Tracks
            : ((IEnumerable<LibraryRow>)Rows.ItemsSource).SelectMany(r => r.Tracks).ToArray();
        Play(row.Tracks[0]);
    }

    private void Play(Track track, bool remember = true, double? restorePosition = null)
    {
        if (remember && current != null && current != track) previous.Push(current);
        current = track;
        opened = false;
        player.Close();
        player.Open(new Uri(track.Path));
        player.Volume = Volume.Value;
        if (opened && restorePosition.HasValue && player.NaturalDuration.HasTimeSpan)
            player.Position = TimeSpan.FromSeconds(Math.Clamp(restorePosition.Value, 0, player.NaturalDuration.TimeSpan.TotalSeconds));
        if (opened && !restorePosition.HasValue) player.Play();
        SetPlaying(opened && !restorePosition.HasValue);
        Now.Text = track.Name;
        Artist.Text = track.Detail;
        if (miniTitle != null) miniTitle.Text = track.Name;
        if (restorePosition.HasValue || !opened) { LoadPresentation(track); return; }
        library.History.Remove(track.Path);
        library.History.Insert(0, track.Path);
        if (library.History.Count > 500) library.History.RemoveAt(500);
        Save();
        if (Navigation.SelectedIndex == 5) RefreshRows();
        LoadPresentation(track);
    }

    private async void LoadPresentation(Track track)
    {
        Cover.Source = null;
        Shell.Background = new LinearGradientBrush(Color.FromRgb(37, 37, 37), Color.FromRgb(23, 23, 23), 115);
        SetLyrics("");
        var result = await Task.Run(() =>
        {
            string lyrics = "";
            BitmapImage image = null;
            try
            {
                string lrc = Path.ChangeExtension(track.Path, ".lrc");
                if (File.Exists(lrc)) lyrics = File.ReadAllText(lrc);
                using var file = TagLib.File.Create(track.Path);
                if (lyrics.Length == 0) lyrics = file.Tag.Lyrics ?? "";
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
            return (lyrics, image);
        });
        if (current != track) return;
        Cover.Source = result.image;
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

        lyricLines = text.Split('\n').SelectMany(line =>
        {
            var stamps = Regex.Matches(line, @"\[(\d+):(\d+(?:[.:]\d+)?)\]");
            string words = Regex.Replace(line, @"\[[^\]]+\]", "").Trim();
            return stamps.Cast<Match>().Select(match =>
                (int.Parse(match.Groups[1].Value) * 60 + double.Parse(match.Groups[2].Value.Replace(':', '.'),
                    System.Globalization.CultureInfo.InvariantCulture), words));
        }).OrderBy(line => line.Item1).ToArray();
        Lyrics.Text = text.Length == 0 ? LocalTextExtension.Translate("Sin letras disponibles") : text;
    }

    private async void LookupLyrics(object sender, RoutedEventArgs args)
    {
        Track track = current;
        if (track == null) return;
        if (MessageBox.Show(this, "Se enviarán el título, artista y álbum a LRCLIB para buscar la letra.",
            "Buscar letras", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(track.Name)}&artist_name={Uri.EscapeDataString(track.Artist)}&album_name={Uri.EscapeDataString(track.Album)}&duration={track.Seconds}";
            using var response = await Http.GetAsync(url);
            if (current != track) return;
            if (!response.IsSuccessStatusCode) { Status.Text = "No se encontró una letra para esta pista."; return; }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (current != track) return;
            var root = doc.RootElement;
            string text = root.TryGetProperty("syncedLyrics", out var sync) ? sync.GetString() : null;
            text ??= root.TryGetProperty("plainLyrics", out var plain) ? plain.GetString() : null;
            if (!string.IsNullOrWhiteSpace(text)) SetLyrics(text);
            else Status.Text = "No se encontró una letra para esta pista.";
        }
        catch (Exception error) { Status.Text = "Error al buscar letras. " + error.Message; }
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
        for (int band = 0; animateSpectrum && band < spectrumBars.Length; band++)
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
        if (lyricLines.Length > 0)
        {
            int index = Array.FindLastIndex(lyricLines, line => line.Time <= position);
            Lyrics.Text = index < 0 ? "…" : string.Join("\n\n", lyricLines.Skip(index).Take(5).Select(line => line.Text));
        }
    }

    private void SetPlaying(bool value)
    {
        playing = value;
        PlayButton.Content = value ? "⏸" : "▶";
    }

    private void Toggle(object sender, RoutedEventArgs args)
    {
        if (current == null) { ActivateSelection(); return; }
        if (playing) player.Pause(); else player.Play();
        SetPlaying(!playing);
    }

    private void Advance()
    {
        if (queue.Count > 0)
        {
            var next = queue[0];
            queue.RemoveAt(0);
            Play(next);
            if (Navigation.SelectedIndex == 4) RefreshRows();
            return;
        }
        int index = Array.IndexOf(context, current);
        if (index + 1 < context.Length) Play(context[index + 1]);
        else { player.Pause(); SetPlaying(false); }
    }

    private void Next(object sender, RoutedEventArgs args) => Advance();

    private void Previous(object sender, RoutedEventArgs args)
    {
        if (opened && player.Position.TotalSeconds > 3) player.Position = TimeSpan.Zero;
        else if (previous.TryPop(out Track track)) Play(track, false);
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
        Status.Text = added ? $"Añadida a favoritos: {track.Name}" : $"Retirada de favoritos: {track.Name}";
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
                MessageBox.Show(dialog, "Escribe un nombre distinto para la lista.", LocalTextExtension.Translate("Nueva lista"));
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
        Status.Text = removed ? $"Retirada de {playlistName}: {track.Name}" : $"Añadida a {playlistName}: {track.Name}";
        Save();
        if (Navigation.SelectedIndex == 6) RefreshRows();
    }

    private void Enqueue(object sender, RoutedEventArgs args)
    {
        if (Selected is not Track track) return;
        if (queue.Any(item => string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase))) return;
        queue.Add(track);
        Status.Text = $"Añadida a la cola: {track.Name}";
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

    private void Fullscreen(object sender, RoutedEventArgs args)
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            Grid.SetColumn(Listening, 2);
            Grid.SetColumnSpan(Listening, 1);
            ArtPanel.Height = 242;
            WindowState = previousWindowState;
        }
        else
        {
            previousWindowState = WindowState;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            Grid.SetColumn(Listening, 0);
            Grid.SetColumnSpan(Listening, 3);
            ArtPanel.Height = Math.Clamp(SystemParameters.PrimaryScreenHeight * .45, 200, 480);
            WindowState = WindowState.Maximized;
        }
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
        catch (Exception error) { Status.Text = "No se pudieron leer los ajustes de audio. " + error.Message; }
    }

    private void SaveAudioSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(AudioSettingsFile + ".tmp", JsonSerializer.Serialize(
                new AudioSettings(Volume.Value, player.OutputId, player.Exclusive, gains, player.Balance, eqEnabled, player.Direct, artworkColors, animateSpectrum, player.Speed)));
            File.Move(AudioSettingsFile + ".tmp", AudioSettingsFile, true);
        }
        catch (Exception error) { Status.Text = "No se pudieron guardar los ajustes de audio. " + error.Message; }
    }

    private void ReopenOutput()
    {
        if (current == null) return;
        var position = player.Position;
        bool resume = playing;
        opened = false;
        player.Open(new Uri(current.Path));
        if (!opened) return;
        player.Position = position;
        if (resume) player.Play();
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
            ? (player.IsExclusive ? "Salida activa en modo exclusivo." : "Salida activa en modo compartido.")
            : "El modo exclusivo se solicita al abrir una pista.");
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
            SelectedIndex = Array.IndexOf(speeds, player.Speed), IsEnabled = !player.Direct };
        panel.Children.Add(new TextBlock { Text = LocalTextExtension.Translate("Velocidad de reproducción"),
            Margin = new Thickness(0, 14, 0, 4) });
        panel.Children.Add(speedPicker);
        speedPicker.SelectionChanged += (_, _) =>
        {
            if (speedPicker.SelectedIndex < 0) return;
            player.Speed = speeds[speedPicker.SelectedIndex];
            ReopenOutput();
        };
        AddPreference("Omitir ecualizador, balance y velocidad", player.Direct, enabled =>
        {
            player.Direct = enabled;
            speedPicker.IsEnabled = !enabled;
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
        settingsWindow.ShowDialog();
        if (!smokeTest) SaveAudioSettings();
    }

    private void Mini(object sender, RoutedEventArgs args)
    {
        if (mini != null) { mini.Activate(); return; }
        miniTitle = new TextBlock { Text = current?.Name ?? "Prisma Music", Margin = new Thickness(0, 0, 0, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var back = new Button { Content = "⏮" };
        var toggle = new Button { Content = "▶ / ⏸" };
        var forward = new Button { Content = "⏭" };
        back.Click += Previous;
        toggle.Click += Toggle;
        forward.Click += Next;
        buttons.Children.Add(back);
        buttons.Children.Add(toggle);
        buttons.Children.Add(forward);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(miniTitle);
        panel.Children.Add(buttons);
        mini = new Window { Owner = this, Title = "Prisma Music · Mini", Width = 290, Height = 125,
            Topmost = true, Content = panel, Background = new SolidColorBrush(Color.FromRgb(34, 34, 48)) };
        mini.Closed += (_, _) => { mini = null; miniTitle = null; };
        mini.Show();
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
        else if (args.Key == Key.F11 || (args.Key == Key.Escape && WindowStyle == WindowStyle.None)) Fullscreen(sender, args);
        else if (args.Key == Key.Enter && Rows.IsKeyboardFocusWithin) ActivateSelection();
    }
}






















