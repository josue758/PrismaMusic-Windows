#define TRACE
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TagLib;
using WinRT.Interop;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Casting;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Popups;
using Point = System.Windows.Point;
using Rect = Windows.Foundation.Rect;

namespace PrismaMusic;


public sealed class Library
{
	public List<string> Folders { get; set; } = new List<string>();

	public List<Track> Tracks { get; set; } = new List<Track>();

	public HashSet<string> Favorites { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public List<string> Playlist { get; set; } = new List<string>();

	public Dictionary<string, List<string>> Playlists { get; set; } = new Dictionary<string, List<string>>();

	public List<string> History { get; set; } = new List<string>();

	public Dictionary<string, LibraryTagEdit> TagEdits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public record LibraryRow(string Name, string Detail, Track[] Tracks, string Footnote = null)
{
	public string TileCaption
	{
		get
		{
			if (Tracks.Length != 1)
			{
				return Name;
			}
			return Tracks[0].Album;
		}
	}

	public string DurationLabel
	{
		get
		{
			if (Tracks.Length != 1 || Tracks[0].Seconds <= 0)
			{
				return "";
			}
			return TimeSpan.FromSeconds(Tracks[0].Seconds).ToString("m\\:ss");
		}
	}

	public string FormatLabel => Tracks.Length == 1 ? Tracks[0].FormatLabel : "";
}

public record Track(string Path, string Name, string Artist, string Album, string AlbumArtist, string Genre, uint Year, uint Disc, uint Number, int Seconds,
	string Codec = null, int SampleRate = 0, int BitDepth = 0)
{
	public bool IsLossless => Codec is "FLAC" or "ALAC" or "PCM";
	public string FormatLabel => IsLossless && SampleRate > 0 && BitDepth > 0
		? $"{Codec} · {BitDepth} bit / {SampleRate / 1000d:0.###} kHz"
		: Codec ?? System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

	public string Detail => string.Join("  ·  ", new string[3]
	{
		Artist,
		Album,
		(Year == 0) ? "" : Year.ToString()
	}.Where((string x) => x.Length > 0));
}
public partial class MainWindow : Window
{

	private sealed record AudioSettings(double Volume, string OutputId, bool Exclusive, float[] Gains, float Balance = 0f, bool EqEnabled = true, bool Direct = false, bool ArtworkColors = true, bool AnimateSpectrum = true, double Speed = 1.0, bool StrictDirect = false, bool Shuffle = false, RepeatMode Repeat = RepeatMode.Off, bool RememberPosition = true, string AccentPreset = "artwork", string VisualizerStyle = "mirror", double VisualizerSensitivity = 1.0, int VisualizerFramesPerSecond = 30, int OutputSampleRate = 0, bool ContinueAfterTrack = true, double LyricOffset = 0, double LyricScale = 1, int AppearanceProfile = 0, bool CompactRows = false, bool ShowTrackQuality = true, bool MiniAlwaysOnTop = true, bool FollowLyrics = true, int AlbumSize = 1, bool AutoArtwork = true, bool AutoLyrics = false, bool KeepAwake = false);

	private enum RepeatMode
	{
		Off,
		Collection,
		Track
	}

	private struct FrameMargins
	{
		public int Left;

		public int Right;

		public int Top;

		public int Bottom;
	}

	private static readonly string DataDirectory = SessionProbeDirectory() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrismaMusic.Windows");

	private LibraryDatabase database;

	private bool unsavedTracks;

	private CancellationTokenSource scanCancellation;

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(12.0)
	};

	private static readonly string AudioSettingsFile = Path.Combine(DataDirectory, "audio-settings.json");

	private readonly bool smokeTest = Enumerable.Contains(Environment.GetCommandLineArgs(), "--smoke-test");

	private readonly AudioEngine player = new AudioEngine();

	private readonly List<Track> queue = new List<Track>();

	private readonly Stack<Track> previous = new Stack<Track>();

	private readonly DispatcherTimer timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(200.0)
	};

	private Library library = new Library();

	private Track current;

	private string selectedLibrarySource;

	private LibraryRow collection;

	private bool albumPage;

	private LibraryRow artRequest;

	private readonly Stack<(LibraryRow Collection, int Selection, bool Album)> collectionHistory = new Stack<(LibraryRow, int, bool)>();

	private string playlistName = "Mi lista";

	private readonly Border[] spectrumBars = new Border[AudioEngine.Frequencies.Length];

	private bool shuffle;

	private bool rememberPosition = true;

	private RepeatMode repeat;

	private Track[] originalContext = Array.Empty<Track>();

	private Track[] context = Array.Empty<Track>();

	private bool ready;

	private bool scanning;

	private bool playing;

	private bool opened;

	private bool updatingSeek;

	private Color windowsAccent;

	private string visualizerStyle = "mirror";

	private double visualizerSensitivity = 1.0;

	private int visualizerFramesPerSecond = 30;

	private (double Time, string Text)[] lyricLines = Array.Empty<(double, string)>();

	private bool eqEnabled = true;

	private bool artworkColors = true;

	private bool animateSpectrum = true;

	private string accentPreset = "artwork";

	private readonly float[] gains = new float[AudioEngine.Frequencies.Length];

	private DateTime? sleepAt;

	private Window mini;

	private TextBlock miniTitle;

	private TextBlock miniArtist;

	private TextBlock miniFormat;

	private Image miniCover;

	private System.Windows.Controls.Button miniToggle;

	private Slider miniProgress;

	private bool micaActive;

	private static readonly float[] FlatGains = new float[AudioEngine.Frequencies.Length];

	private List<string> ActivePlaylist
	{
		get
		{
			if (!(playlistName == "Mi lista"))
			{
				return library.Playlists[playlistName];
			}
			return library.Playlist;
		}
	}

	private Track Selected => (Rows.SelectedItem as LibraryRow)?.Tracks.FirstOrDefault() ?? current;

	private Color AccentPresetColor()
	{
		string text = accentPreset;
		if (!(text == "blue"))
		{
			if (text == "violet")
			{
				return Color.FromRgb(190, 145, byte.MaxValue);
			}
			return windowsAccent;
		}
		return Color.FromRgb(96, 188, byte.MaxValue);
	}

	public MainWindow()
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Expected Obj, but got Unknown
		InitializeComponent();
		windowsAccent = ((SolidColorBrush)System.Windows.Application.Current.Resources["AccentBrush"]).Color;
		TransportTitle.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Text")
		{
			Source = Now
		});
		for (int i = 0; i < spectrumBars.Length; i++)
		{
			Border border = new Border
			{
				Height = 2.0,
				CornerRadius = new CornerRadius(2.0),
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(2.0, 0.0, 2.0, 0.0),
				Background = ((System.Windows.Application.Current.Resources["AccentBrush"] as Brush) ?? new SolidColorBrush(Color.FromRgb(175, 196, byte.MaxValue)))
			};
			spectrumBars[i] = border;
			SpectrumView.Children.Add(border);
		}
		try
		{
			if (!smokeTest)
			{
				LibraryDatabase libraryDatabase = new LibraryDatabase(DataDirectory);
				library = libraryDatabase.Load();
				database = libraryDatabase;
			}
		}
		catch (Exception ex)
		{
			Status.Text = LocalTextExtension.Translate("No se pudo leer la biblioteca. ") + ex.Message;
		}
		player.Volume = Volume.Value;
		if (!smokeTest)
		{
			LoadAudioSettings();
		}
		UpdateAudioOutputTooltip();
		player.MediaOpened += (object? _, EventArgs _) =>
		{
			opened = true;
			UpdateAudioOutputTooltip();
			if (player.OutputNotice != null)
			{
				Status.Text = player.OutputNotice;
			}
			Tick();
		};
		player.MediaEnded += (object? _, EventArgs _) =>
		{
			if (pendingTrack == null)
			{
				Advance(endOfTrack: true);
			}
		};
		player.MediaFailed += async (_, error) => await HandleAudioFailure(error);
		timer.Tick += (object? _, EventArgs _) =>
		{
			Tick();
		};
		timer.Start();
		Closed += (object? _, EventArgs _) =>
		{
			mini?.Close();
			UpdateAwakeRequest(false);
			scanCancellation?.Cancel();
			timer.Stop();
			DisposeSystemPlaybackControls();
			CancelPendingOpen();
			if (!smokeTest)
			{
				SaveSession();
				SaveAudioSettings();
			}
			player.Close();
		};
		SourceInitialized += (object? _, EventArgs _) =>
		{
			EnableDarkTitleBar();
			ApplyFluentBackdrop();
			if (!smokeTest || Environment.GetEnvironmentVariable("PRISMA_SYSTEM_CONTROLS_PROBE") == "1")
				InitializeSystemPlaybackControls();
		};
		StateChanged += (object? _, EventArgs _) =>
		{
			UpdateMainWindowState();
		};
		SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			bool flag = ActualWidth < 1120.0;
			NavigationColumn.Width = new GridLength(flag ? 168 : 210);
			ListeningColumn.Width = new GridLength(flag ? 280 : 330);
			BrandTitle.FontSize = (flag ? 14 : 17);
			Heading.FontSize = (flag ? 32 : 38);
			System.Windows.Controls.ComboBox genre = Genre;
			System.Windows.Controls.ComboBox year = Year;
			double num = (Sort.FontSize = (flag ? 12 : 14));
			double fontSize = (year.FontSize = num);
			genre.FontSize = fontSize;
			foreach (System.Windows.Controls.Button item in LibraryCommands.Children.OfType<System.Windows.Controls.Button>())
			{
				item.Padding = (flag ? new Thickness(8.0, 6.0, 8.0, 6.0) : new Thickness(13.0, 8.0, 13.0, 8.0));
			}
			foreach (System.Windows.Controls.Button item2 in ListeningActions.Children.OfType<System.Windows.Controls.Button>())
			{
				if (flag)
				{
					item2.Padding = new Thickness(7.0, 6.0, 7.0, 6.0);
				}
				else
				{
					((DependencyObject)item2).ClearValue(System.Windows.Controls.Control.PaddingProperty);
				}
			}
			ArtPanel.Height = Math.Clamp(ActualHeight - 490.0, 114.0, 254.0);
		};
		UpdatePlaylistPicker();
		Navigation.SelectedIndex = 0;
		Sort.SelectedIndex = 0;
		ready = true;
		ApplyLibraryPresentation();
		ApplyAlbumSize();
		Shell.Background = DefaultShellBrush();
		if (smokeTest)
		{
			Loaded += async (object _, RoutedEventArgs _) =>
			{
				await SmokeTest();
			};
		}
		RefreshFilters();
		RefreshLibrarySources();
		RefreshRows();
		UpdateFavoriteToolbarButton();
		if (smokeTest)
		{
			return;
		}
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await RestoreSession();
			if (SessionProbeDirectory() != null)
			{
				await RunSessionProbe();
			}
			else
			{
				ShowWelcomeOnFirstRun();
			}
		};
	}

	private void MinimizeMainWindow(object sender, RoutedEventArgs args)
	{
		WindowState = WindowState.Minimized;
	}

	private void ToggleMainWindowState(object sender, RoutedEventArgs args)
	{
		WindowState = ((WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void CloseMainWindow(object sender, RoutedEventArgs args)
	{
		Close();
	}

	private void UpdateMainWindowState()
	{
		bool flag = WindowState == WindowState.Maximized;
		MaximizeWindowGlyph.Text = (flag ? "\ue923" : "\ue922");
		string text = LocalTextExtension.Translate(flag ? "Restaurar" : "Maximizar");
		MaximizeWindowButton.ToolTip = text;
		AutomationProperties.SetName((DependencyObject)(object)MaximizeWindowButton, text);
	}

	private async Task SmokeTest()
	{
		_ = 27;
		try
		{
			await VerifyButtonFeedback();
			string language = LocalTextExtension.Language;
			string text;
			if (language == "en")
			{
				text = "Songs";
			}
			else
			{
				text = ((!(language == "pt")) ? "Canciones" : "Músicas");
			}
			string text2 = text;
			if (Heading.Text != text2)
			{
				throw new InvalidOperationException("Unexpected heading: " + Heading.Text);
			}
			TransportArtworkButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			Window window = listeningRoom;
			if (window == null || !window.IsVisible || (object)Transport.Parent == Shell)
			{
				throw new InvalidOperationException("The player artwork did not open the listening room.");
			}
			TransportArtworkButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			if (listeningRoom != null || (object)Transport.Parent != Shell)
			{
				throw new InvalidOperationException("The player artwork did not close the listening room.");
			}
			player.StrictDirect = true;
			UpdateVolumeControls();
			if (EnableVolumeButton.Visibility != Visibility.Visible || VolumeControls.Visibility != Visibility.Collapsed)
			{
				throw new InvalidOperationException("Direct mode did not expose the volume action.");
			}
			EnableAdjustableVolume(this, new RoutedEventArgs());
			if (player.StrictDirect || VolumeControls.Visibility != Visibility.Visible)
			{
				throw new InvalidOperationException("Adjustable volume was not restored.");
			}
			Console.WriteLine("Fullscreen and direct-volume controls passed.");
			double width = Width;
			double height = Height;
			Width = MinWidth;
			Height = MinHeight;
			UpdateLayout();
			if (Rows.ActualWidth < 200.0 || ArtPanel.ActualHeight > 160.0 || Transport.ActualHeight < 45.0 || string.IsNullOrWhiteSpace(AutomationProperties.GetName((DependencyObject)(object)PlayButton)))
			{
				throw new InvalidOperationException("Compact layout or accessible playback label failed.");
			}
			string environmentVariable = Environment.GetEnvironmentVariable("PRISMA_COMPACT_PREVIEW");
			if (environmentVariable != null)
			{
				RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap((int)Math.Ceiling(Shell.ActualWidth * 1.25), (int)Math.Ceiling(Shell.ActualHeight * 1.25), 120.0, 120.0, PixelFormats.Pbgra32);
				renderTargetBitmap.Render(Shell);
				PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
				pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
				using FileStream stream = System.IO.File.Create(environmentVariable);
				pngBitmapEncoder.Save(stream);
			}
			Width = width;
			Height = height;
			VerifyAutomationPeers();
			var originalProfile = appearanceProfile;
			ApplyAppearanceProfile(2);
			if (ArtworkTint.Opacity != 0 || AmbientArtwork.Opacity != 0 || ArtworkShellBrush(Colors.Gold) is not SolidColorBrush)
				throw new InvalidOperationException("Contrast appearance did not remove the artwork backdrop.");
			ApplyAppearanceProfile(1);
			if (ArtworkTint.Opacity <= 0 || ArtworkTint.Opacity >= .76)
				throw new InvalidOperationException("Calm appearance did not soften the artwork backdrop.");
			ApplyAppearanceProfile(originalProfile);
			Console.WriteLine("Appearance profiles passed.");
			Console.WriteLine("Compact layout and accessible transport label passed.");
			Console.WriteLine($"Interface: {LocalTextExtension.Language} ({Heading.Text})");
			if (Environment.GetEnvironmentVariable("PRISMA_WELCOME_PREVIEW") != null)
			{
				ShowWelcome(this, new RoutedEventArgs());
			}
			await VerifyLyricsAndReceiverDiscovery();
			string environmentVariable2 = Environment.GetEnvironmentVariable("PRISMA_TRANSLATOR_SETUP_PROBE");
			if (environmentVariable2 != null)
			{
				string root = Path.Combine(Environment.GetEnvironmentVariable("PRISMA_TRANSLATOR_SETUP_ROOT"), "PrismaTranslatorProbe-" + Guid.NewGuid().ToString("N"));
				if (!(await TranslateLocalLines(await InstallLocalTranslator(environmentVariable2, root, "en", Console.WriteLine), "en", new string[1] { "Good morning." }, root))[0].Contains("días", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Managed translation did not produce Spanish.");
				}
				Console.WriteLine("Managed Argos setup and translation passed.");
			}
			string environmentVariable3 = Environment.GetEnvironmentVariable("PRISMA_ARGOS_PROBE");
			if (environmentVariable3 != null)
			{
				string[] originals = new string[2] { "Good morning.", "Good night." };
				string[] array = await TranslateLocalLines(environmentVariable3, "en", originals);
				SetLyrics("[00:01]Good morning.\n[00:03]Good night.");
				translatedLines = originals.Zip(array).ToDictionary(((string First, string Second) pair) => pair.First, ((string First, string Second) pair) => pair.Second);
				if (!BilingualLine(lyricLines[1].Text).Contains("noches", StringComparison.OrdinalIgnoreCase) || lyricLines[1].Time != 3.0)
				{
					throw new InvalidOperationException("Bilingual lyrics lost their alignment.");
				}
				string text3 = array[0];
				if (!text3.Contains("días", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Unexpected Argos translation: " + text3);
				}
				Console.WriteLine("Argos: English-to-Spanish local translation passed.");
			}
			string dbProbe = Environment.GetEnvironmentVariable("PRISMA_DATABASE_PROBE");
			if (dbProbe != null)
			{
				await Task.Run(() =>
				{
					VerifyLibraryMigration(dbProbe);
				});
			}
			string environmentVariable4 = Environment.GetEnvironmentVariable("PRISMA_SMB_PROBE");
			if (environmentVariable4 != null)
			{
				await VerifySmbPlayback(environmentVariable4);
			}
			string environmentVariable5 = Environment.GetEnvironmentVariable("PRISMA_USB_PCM_PROBE");
			if (environmentVariable5 != null)
			{
				string environmentVariable6 = Environment.GetEnvironmentVariable("PRISMA_USB_ENDPOINT");
				if (environmentVariable6 != null)
				{
					await VerifyUsbPcm(environmentVariable6, environmentVariable5);
				}
			}
			(List<Track> Tracks, int Errors) demo = await Task.Run(() => LibraryScanner.ReadFolder(Path.Combine(AppContext.BaseDirectory, "Demo")));
			if (demo.Errors != 0 || demo.Tracks.Count != 3)
			{
				throw new InvalidOperationException("Se esperaban tres demos legibles.");
			}
			Track track = demo.Tracks[0];
			var verifiedLossless = track with { Codec = "FLAC", SampleRate = 96000, BitDepth = 24 };
			library.Tracks = demo.Tracks.Select(item => item == track ? verifiedLossless : item).ToList();
			RefreshFilters();
			Navigation.SelectedItem = LosslessNavigation;
			if (Rows.Items.Count != 1 || (Rows.Items[0] as LibraryRow)?.Tracks[0] != verifiedLossless)
				throw new InvalidOperationException("Lossless view did not filter out compressed tracks.");
			Search.Text = "orbita prisma";
			if (Rows.Items.Count != 1) throw new InvalidOperationException("Library search did not match multiple accent-insensitive terms.");
			Search.Text = "orbita incorrect";
			if (Rows.Items.Count != 0) throw new InvalidOperationException("Library search did not require all terms.");
			Search.Clear();
			ArtistFilter.SelectedItem = verifiedLossless.Artist;
			if (Rows.Items.Count != 1) throw new InvalidOperationException("Artist filter did not retain the matching track.");
			ArtistFilter.SelectedItem = demo.Tracks[1].Artist;
			if (Rows.Items.Count != 0) throw new InvalidOperationException("Artist filter did not combine with Lossless.");
			ArtistFilter.SelectedIndex = 0;
			if (Environment.GetEnvironmentVariable("PRISMA_LOSSLESS_PREVIEW") != null &&
				Environment.GetEnvironmentVariable("PRISMA_LOSSLESS_FIXTURE") is string losslessFixture)
			{
				var scannedLossless = LibraryScanner.ReadFolder(losslessFixture).Tracks.Single(item => item.Codec == "FLAC");
				library.Tracks = demo.Tracks.Append(scannedLossless).ToList();
				RefreshFilters();
				RefreshRows();
				await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
				CaptureDesignPreview(Shell, "PRISMA_LOSSLESS_PREVIEW");
			}
			Navigation.SelectedIndex = 0;
			library.Tracks = demo.Tracks;
			RefreshFilters();
			Console.WriteLine("Lossless navigation, format metadata and combined artist/search filters passed.");
			string environmentVariable7 = Environment.GetEnvironmentVariable("PRISMA_PREVIEW_PATH");
			if (!string.IsNullOrEmpty(environmentVariable7))
			{
				library.Tracks = demo.Tracks;
				RefreshFilters();
				RefreshRows();
				Now.Text = track.Name;
				Cover.Tag = track.Album;
				Artist.Text = track.Detail;
				AudioFormat.Text = track.FormatLabel;
				SetArtworkAccent(ArtworkTile.AccentForCaption(track.Album, windowsAccent));
				UpdateLayout();
				RenderTargetBitmap renderTargetBitmap2 = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap2.Render(this);
				PngBitmapEncoder pngBitmapEncoder2 = new PngBitmapEncoder();
				pngBitmapEncoder2.Frames.Add(BitmapFrame.Create(renderTargetBitmap2));
				using FileStream stream2 = System.IO.File.Create(environmentVariable7);
				pngBitmapEncoder2.Save(stream2);
			}
			if (Environment.GetEnvironmentVariable("PRISMA_SETTINGS_PREVIEW") != null)
			{
				Settings(this, new RoutedEventArgs());
			}
			library.Tracks = demo.Tracks;
			Navigation.SelectedIndex = 1;
			Rows.SelectedIndex = 0;
			LibraryRow libraryRow = (LibraryRow)Rows.SelectedItem;
			string environmentVariable8 = Environment.GetEnvironmentVariable("PRISMA_GALLERY_PREVIEW");
			if (environmentVariable8 != null)
			{
				UpdateLayout();
				RenderTargetBitmap renderTargetBitmap3 = new RenderTargetBitmap((int)Shell.ActualWidth, (int)Shell.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap3.Render(Shell);
				PngBitmapEncoder pngBitmapEncoder3 = new PngBitmapEncoder();
				pngBitmapEncoder3.Frames.Add(BitmapFrame.Create(renderTargetBitmap3));
				using FileStream stream3 = System.IO.File.Create(environmentVariable8);
				pngBitmapEncoder3.Save(stream3);
			}
			if (Rows.ItemTemplate != Rows.FindResource("GalleryCard"))
			{
				throw new InvalidOperationException("Album gallery did not use cards.");
			}
			double galleryWidth = Width;
			int originalAlbumSize = albumSize;
			albumSize = 2;
			ApplyAlbumSize();
			Width = MinWidth;
			UpdateLayout();
			await Task.Delay(250);
			var firstTile = Rows.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
			var secondTile = Rows.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
			if (firstTile == null || secondTile == null ||
				Math.Abs(firstTile.TranslatePoint(new Point(), Rows).Y - secondTile.TranslatePoint(new Point(), Rows).Y) > 1)
				throw new InvalidOperationException("Compact gallery did not fit two large album cards on the same row.");
			CaptureDesignPreview(Shell, "PRISMA_COMPACT_GALLERY_PREVIEW");
			Width = galleryWidth;
			albumSize = originalAlbumSize;
			ApplyAlbumSize();
			UpdateLayout();
			Console.WriteLine("Compact gallery fit two album cards with the largest cover preference.");
			SetGalleryRows(Enumerable.Repeat(libraryRow, 31).ToArray());
			if (Rows.Items.Count != 30 || !GalleryNext.IsEnabled)
			{
				throw new InvalidOperationException("Gallery did not limit the first page.");
			}
			RefreshRows();
			Rows.SelectedIndex = 0;
			ActivateSelection();
			LibraryRow libraryRow2 = collection;
			if (string.IsNullOrWhiteSpace(CollectionCount.Text) || CollectionArtTile.Visibility != Visibility.Visible)
			{
				throw new InvalidOperationException("Album header did not show its summary.");
			}
			string environmentVariable9 = Environment.GetEnvironmentVariable("PRISMA_ALBUM_PREVIEW");
			if (environmentVariable9 != null)
			{
				UpdateLayout();
				RenderTargetBitmap renderTargetBitmap4 = new RenderTargetBitmap((int)Shell.ActualWidth, (int)Shell.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap4.Render(Shell);
				PngBitmapEncoder pngBitmapEncoder4 = new PngBitmapEncoder();
				pngBitmapEncoder4.Frames.Add(BitmapFrame.Create(renderTargetBitmap4));
				using FileStream stream4 = System.IO.File.Create(environmentVariable9);
				pngBitmapEncoder4.Save(stream4);
			}
			BrowseRelatedArtist(this, new RoutedEventArgs());
			if (collection?.Name != libraryRow.Tracks[0].Artist)
			{
				throw new InvalidOperationException("Album did not open artist.");
			}
			BrowseRelatedAlbum(this, new RoutedEventArgs());
			if (collection?.Name != libraryRow.Name)
			{
				throw new InvalidOperationException("Artist did not open album.");
			}
			CloseCollection(this, new RoutedEventArgs());
			CloseCollection(this, new RoutedEventArgs());
			if (collection != libraryRow2 || Rows.SelectedIndex != 0)
			{
				throw new InvalidOperationException("Back did not restore album.");
			}
			CloseCollection(this, new RoutedEventArgs());
			if (collection != null || Rows.SelectedIndex != 0)
			{
				throw new InvalidOperationException("Back did not restore library.");
			}
			Console.WriteLine("Album/artist navigation and back selection passed.");
			if (Environment.GetEnvironmentVariable("PRISMA_PROFILE_PROBE") == "1")
			{
				string[] originals = new string[3] { "es", "pt", "en" };
				foreach (string root in originals)
				{
					JsonElement[] array2 = await SearchArtist("Daft Punk", root);
					if (array2.Length == 0)
					{
						throw new InvalidOperationException("No profile for " + root + ".");
					}
					if (root == "en")
					{
						PortraitCredit portraitCredit = await FindPortraitCredit(array2.First((JsonElement item) => item.TryGetProperty("pageimage", out var _)));
						if (string.IsNullOrWhiteSpace(portraitCredit?.Page) || string.IsNullOrWhiteSpace(portraitCredit.License))
						{
							throw new InvalidOperationException("Portrait attribution is incomplete.");
						}
						Console.WriteLine("Portrait license: " + portraitCredit.License);
					}
					Console.WriteLine("Wikipedia profile: " + root);
				}
			}
			Navigation.SelectedIndex = 4;
			queue.Clear();
			queue.AddRange(demo.Tracks);
			RefreshRows();
			Rows.SelectedIndex = 1;
			EditQueue(-1);
			if (queue[0] != demo.Tracks[1])
			{
				throw new InvalidOperationException("Queue reorder failed.");
			}
			EditQueue(0);
			if (queue.Count != 2 || queue.Contains(demo.Tracks[1]))
			{
				throw new InvalidOperationException("Queue removal failed.");
			}
			var nextTrack = queue[1];
			Rows.SelectedIndex = 1;
			PlayNextInQueue(this, new RoutedEventArgs());
			PlayNextInQueue(this, new RoutedEventArgs());
			if (queue.Count != 2 || queue[0] != nextTrack)
				throw new InvalidOperationException("Play next did not prioritize the selected track without duplicating it.");
			queue.Clear();
			player.Speed = 1.5;
			await Play(track, remember: false, 1.0);
			if (!opened || playing || Math.Abs(player.Position.TotalSeconds - 1.0) > 0.15)
			{
				throw new InvalidOperationException("Paused session restore failed.");
			}
			bool flag = library.Favorites.Contains(track.Path);
			FavoriteToolbarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			if (library.Favorites.Contains(track.Path) == flag || !object.Equals(FavoriteToolbarButton.Content, "\uEB52"))
			{
				throw new InvalidOperationException("The transport favorite button did not favorite the playing track.");
			}
			FavoriteToolbarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			if (library.Favorites.Contains(track.Path) != flag)
			{
				throw new InvalidOperationException("The transport favorite button did not toggle off.");
			}
			Console.WriteLine("Transport favorite toggles the playing track.");
			double testVolume = Volume.Value;
			Volume.Value = .42;
			ToggleMute(this, new RoutedEventArgs());
			if (Volume.Value != 0 || MuteButton.Content.ToString() != "\uE74F")
				throw new InvalidOperationException("Transport mute did not update volume and icon.");
			ToggleMute(this, new RoutedEventArgs());
			if (Volume.Value != .42) throw new InvalidOperationException("Transport mute did not restore volume.");
			SetSleepTimer(15);
			if (SleepCountdown.Visibility != Visibility.Visible || sleepAt <= DateTime.UtcNow)
				throw new InvalidOperationException("Sleep timer did not show the active countdown.");
			CaptureDesignPreview(Shell, "PRISMA_TRANSPORT_PREVIEW", Transport);
			player.Play();
			SetPlaying(true);
			sleepAt = DateTime.UtcNow.AddSeconds(-1);
			Tick();
			if (playing || sleepAt.HasValue || SleepCountdown.Visibility != Visibility.Collapsed)
				throw new InvalidOperationException("Sleep timer did not pause playback and clear the countdown.");
			Volume.Value = testVolume;
			Console.WriteLine("Transport mute, restore and sleep timer passed.");
			originalContext = demo.Tracks.ToArray();
			shuffle = true;
			ReorderPlayback(track);
			if (context[0] != track || !context.ToHashSet().SetEquals(demo.Tracks))
			{
				throw new InvalidOperationException("Shuffle dropped or duplicated tracks.");
			}
			shuffle = false;
			ReorderPlayback(track);
			if (!context.SequenceEqual(demo.Tracks))
			{
				throw new InvalidOperationException("Disabling shuffle lost the original order.");
			}
			Console.WriteLine("Queue edit, shuffle and paused resume passed.");
			await VerifyCancelableOpen(demo.Tracks[1], demo.Tracks[2]);
			player.Speed = 1.0;
			Volume.Value = 0.0;
			context = demo.Tracks.ToArray();
			repeat = RepeatMode.Track;
			await Play(track);
			player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(180.0);
			for (int num = 0; num < 35; num++)
			{
				if (!(player.Position.TotalSeconds > 2.0))
				{
					break;
				}
				await Task.Delay(100);
			}
			if (current != track || !playing || player.Position.TotalSeconds > 2.0)
			{
				throw new InvalidOperationException("Repeat track did not restart at the end.");
			}
			repeat = RepeatMode.Collection;
			await Play(context[^1]);
			player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(180.0);
			for (int num = 0; num < 35; num++)
			{
				if (!(current != track))
				{
					break;
				}
				await Task.Delay(100);
			}
			if (current != track || !playing)
			{
				throw new InvalidOperationException("Repeat collection did not wrap at the end.");
			}
			repeat = RepeatMode.Off;
			Console.WriteLine("Repeat: track restart and collection wrap passed.");
			await Play(track);
			queue.Clear();
			queue.Add(demo.Tracks[1]);
			repeat = RepeatMode.Track;
			SetStopAfterTrack(true);
			player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(180);
			for (int attempt = 0; attempt < 35 && playing; attempt++) await Task.Delay(100);
			if (playing || stopAfterTrack || current != track || queue.Count != 1 ||
				StopAfterTrackIndicator.Visibility != Visibility.Collapsed)
				throw new InvalidOperationException("Pause after track did not override repeat and preserve the queue.");
			continueAfterTrack = false;
			Advance(endOfTrack: true);
			if (queue.Count != 1 || playing) throw new InvalidOperationException("Disabled autoplay advanced the queue.");
			continueAfterTrack = true;
			Next(this, new RoutedEventArgs());
			for (int attempt = 0; attempt < 35 && current != demo.Tracks[1]; attempt++) await Task.Delay(100);
			if (current != demo.Tracks[1] || queue.Count != 0) throw new InvalidOperationException("Manual Next did not consume the queue.");
			repeat = RepeatMode.Off;
			await Play(track);
			Console.WriteLine("Pause after track, autoplay and manual Next passed.");
			player.Speed = 1.5;
			await Play(track, remember: false, 1.0);
			var details = ReadTrackDetails(track);
			if (!details.Audio.Any(row => row.Label == "Frecuencia" && row.Value.EndsWith(" Hz")))
				throw new InvalidOperationException("File details did not expose the sample rate.");
			var listeningMenu = ListeningMenuButton.ContextMenu;
			listeningMenu.StaysOpen = true;
			ShowListeningMenu(ListeningMenuButton, new RoutedEventArgs());
			if (!listeningMenu.IsOpen || listeningMenu.Items.OfType<MenuItem>().Count() != 11)
				throw new InvalidOperationException("Listening menu did not expose all commands.");
			CaptureDesignPreview(listeningMenu, "PRISMA_LISTENING_MENU_PREVIEW");
			listeningMenu.IsOpen = false;
			listeningMenu.ClearValue(ContextMenu.StaysOpenProperty);
			Console.WriteLine("Listening menu and file details passed.");
			if (Environment.GetEnvironmentVariable("PRISMA_TAG_EDITOR_PREVIEW") != null)
				EditLibraryTags(this, new RoutedEventArgs());
			Mini(this, new RoutedEventArgs());
			for (int num = 0; num < 20; num++)
			{
				if (miniFormat.Text.Contains("kHz"))
				{
					break;
				}
				await Task.Delay(100);
			}
			if (miniTitle.Text != track.Name || miniArtist.Text != track.Artist || !miniFormat.Text.Contains("kHz") || miniFormat.Text != AudioFormat.Text)
			{
				throw new InvalidOperationException("Mini player did not show the current track and format.");
			}
			SetPlaying(value: true);
			if (!object.Equals(miniToggle.Content, PlayButton.Content))
			{
				throw new InvalidOperationException("Mini player transport is out of sync.");
			}
			SetPlaying(value: false);
			CaptureDesignPreview((FrameworkElement)mini.Content, "PRISMA_PIP_PREVIEW");
			var mainState = WindowState;
			WindowState = WindowState.Minimized;
			await Task.Delay(100);
			if (!mini.IsVisible || !mini.Topmost)
				throw new InvalidOperationException("Mini player disappeared when minimizing the main window.");
			WindowState = mainState;
			mini.Close();
			Console.WriteLine("Mini player: track, format and playback state passed.");
			SetLyrics("[00:02]First verse\n[00:05]Second verse");
			Lyrics.Inlines.OfType<Hyperlink>().Last().RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
			if (Math.Abs(player.Position.TotalSeconds - 5.0) > 0.2)
			{
				throw new InvalidOperationException("Selecting a verse did not seek.");
			}
			Console.WriteLine("Clickable synchronized verse passed.");
			lyricOffset = 1.5;
			lyricScale = 1.3;
			RefreshLyrics();
			var lastVerse = Lyrics.Inlines.OfType<Hyperlink>().Last();
			lastVerse.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
			if (Math.Abs(player.Position.TotalSeconds - 3.5) > .2 || lyricDisplayIndex != 1 ||
				Math.Abs(Lyrics.Inlines.OfType<Hyperlink>().Last().FontSize - 23.4) > .01)
				throw new InvalidOperationException("Lyrics size, synchronization or offset-aware seek failed.");
			if (Environment.GetEnvironmentVariable("PRISMA_LYRIC_PANEL_PREVIEW") != null)
			{
				Navigation.SelectedIndex = 0;
				SetLyrics("[00:00]Las luces de la ciudad\n[00:04]Se reflejan sobre el agua\n[00:08]Y nos marcan el camino");
				CaptureDesignPreview(Shell, "PRISMA_LYRIC_PANEL_PREVIEW");
			}
			lyricOffset = 0;
			lyricScale = 1;
			RefreshLyrics();
			Console.WriteLine("Lyric scaling, sync offset and offset-aware seek passed.");
			followLyrics = false;
			SetLyrics("[00:01]One\n[00:02]Two\n[00:03]Three\n[00:04]Four\n[00:05]Five\n[00:06]Six");
			if (Lyrics.Inlines.OfType<Hyperlink>().Count() != 6 || lyricDisplayIndex != -1)
				throw new InvalidOperationException("Lyrics manual mode did not show the full text.");
			followLyrics = true;
			RefreshLyrics();
			if (Lyrics.Inlines.OfType<Hyperlink>().Count() >= 6)
				throw new InvalidOperationException("Lyrics follow mode did not restore the listening window.");
			Console.WriteLine("Full lyrics and automatic verse modes passed.");
			if (Environment.GetEnvironmentVariable("PRISMA_SYSTEM_CONTROLS_PROBE") == "1")
			{
				PublishSystemPlayback();
				if (systemControls?.IsEnabled != true || systemControls.DisplayUpdater.MusicProperties.Title != current.Name)
					throw new InvalidOperationException("Windows media controls did not receive the track metadata.");
				ApplySystemPlaybackButton(SystemMediaTransportControlsButton.Play);
				if (!playing || systemControls.PlaybackStatus != MediaPlaybackStatus.Playing)
					throw new InvalidOperationException("Windows media play did not start local playback.");
				ApplySystemPlaybackButton(SystemMediaTransportControlsButton.Pause);
				if (playing || systemControls.PlaybackStatus != MediaPlaybackStatus.Paused)
					throw new InvalidOperationException("Windows media pause did not pause local playback.");
				var originalTrack = current;
				queue.Insert(0, demo.Tracks[1]);
				ApplySystemPlaybackButton(SystemMediaTransportControlsButton.Next);
				for (int attempt = 0; attempt < 30 && pendingTrack != null; attempt++) await Task.Delay(50);
				if (current != demo.Tracks[1] || systemControls.DisplayUpdater.MusicProperties.Title != current.Name)
					throw new InvalidOperationException("Windows media next did not advance the queue and update the title.");
				ApplySystemPlaybackButton(SystemMediaTransportControlsButton.Previous);
				for (int attempt = 0; attempt < 30 && pendingTrack != null; attempt++) await Task.Delay(50);
				if (current != originalTrack)
					throw new InvalidOperationException("Windows media previous did not return to the previous track.");
				Console.WriteLine("Windows media controls published metadata and verified play, pause, next and previous.");
			}
			string portrait = Environment.GetEnvironmentVariable("PRISMA_PORTRAIT_PROBE");
			if (portrait != null)
			{
				await SaveArtistPortrait("Daft Punk", new Uri((await SearchArtist("Daft Punk", "en")).First((JsonElement page) => page.TryGetProperty("thumbnail", out var _)).GetProperty("thumbnail").GetProperty("source")
					.GetString()), portrait);
				using FileStream stream5 = System.IO.File.OpenRead(portrait);
				if (DecodeArtwork(stream5, 160).PixelWidth < 1)
				{
					throw new InvalidOperationException("Portrait cache unreadable.");
				}
				Console.WriteLine("Wikipedia portrait downloaded, persisted and decoded.");
			}
			player.Volume = 0.0;
			player.Exclusive = Enumerable.Contains(Environment.GetCommandLineArgs(), "--exclusive");
			player.Open(new Uri(track.Path));
			player.Play();
			for (int num = 0; num < 40; num++)
			{
				if (!(player.Position.TotalMilliseconds < 300.0))
				{
					break;
				}
				await Task.Delay(250);
			}
			if (!opened || player.Position.TotalMilliseconds < 300.0)
			{
				throw new InvalidOperationException("No avanzó la reproducción de la demo.");
			}
			player.Pause();
			await Task.Delay(300);
			double pausedAt = player.Position.TotalSeconds;
			await Task.Delay(250);
			if (Math.Abs(player.Position.TotalSeconds - pausedAt) > 0.15)
			{
				throw new InvalidOperationException("Tempo playback did not pause.");
			}
			player.Position = TimeSpan.FromSeconds(1.0);
			if (Math.Abs(player.Position.TotalSeconds - 1.0) > 0.15)
			{
				throw new InvalidOperationException("Tempo seek did not reset the processing buffer.");
			}
			playing = true;
			player.Speed = 0.75;
			await ReopenOutput();
			await Task.Delay(500);
			if (!opened || player.Position.TotalSeconds <= 1.1)
			{
				throw new InvalidOperationException("Tempo playback did not resume after reconfiguration.");
			}
			Console.WriteLine("Tempo: playback, pause, seek and output reopen passed.");
			string visualizerPreview = Environment.GetEnvironmentVariable("PRISMA_VISUALIZER_PREVIEW");
			double previewVolume = player.Volume;
			if (!string.IsNullOrEmpty(visualizerPreview))
			{
				player.Volume = 0.7;
			}
			ShowVisualizer(this, new RoutedEventArgs());
			await Task.Delay(string.IsNullOrEmpty(visualizerPreview) ? 150 : 1200);
			Window window2 = visualizerWindow;
			if (window2 == null || !window2.IsVisible || visualizerBars.Length != 48 || visualizerTitle?.Text != Now.Text || !visualizerBars.Any((Border bar) => bar.Height > 3.0))
			{
				throw new InvalidOperationException("The full-screen visualizer did not reflect the current track.");
			}
			ShowVisualizerOptions(visualizerOptionsButton, new RoutedEventArgs());
			ContextMenu contextMenu = visualizerOptionsMenu;
			if (contextMenu == null || contextMenu.Items.OfType<MenuItem>().Count((MenuItem item) => object.Equals(item.Tag, "style")) != 3 || visualizerOptionsMenu.Items.OfType<MenuItem>().Count((MenuItem item) => object.Equals(item.Tag, "sensitivity")) != 4 || visualizerOptionsMenu.Items.OfType<MenuItem>().Count((MenuItem item) => object.Equals(item.Tag, "fps")) != 3)
			{
				throw new InvalidOperationException("Visualizer style, sensitivity or frame-rate options are missing.");
			}
			visualizerOptionsMenu.IsOpen = false;
			if (!string.IsNullOrEmpty(visualizerPreview))
			{
				FrameworkElement frameworkElement = (FrameworkElement)visualizerWindow.Content;
				RenderTargetBitmap renderTargetBitmap5 = new RenderTargetBitmap((int)frameworkElement.ActualWidth, (int)frameworkElement.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap5.Render(frameworkElement);
				PngBitmapEncoder pngBitmapEncoder5 = new PngBitmapEncoder();
				pngBitmapEncoder5.Frames.Add(BitmapFrame.Create(renderTargetBitmap5));
				using FileStream stream6 = System.IO.File.Create(visualizerPreview);
				pngBitmapEncoder5.Save(stream6);
			}
			player.Volume = previewVolume;
			visualizerWindow.Close();
			if (visualizerWindow != null)
			{
				throw new InvalidOperationException("The visualizer did not close cleanly.");
			}
			Console.WriteLine("Full-screen live-spectrum visualizer passed.");
			IEnumerable libraryRows = Rows.ItemsSource;
			double beforeRoom = player.Position.TotalSeconds;
			if (Environment.GetEnvironmentVariable("PRISMA_ROOM_PREVIEW") != null)
				SetLyrics("[00:00]Las luces de la ciudad\n[00:08]Se reflejan sobre el agua\n[00:16]Y nos marcan el camino");
			Fullscreen(this, new RoutedEventArgs());
			var room = listeningRoom ?? throw new InvalidOperationException("The listening room did not open.");
			await Task.Delay(400);
			if (!room.IsVisible || !playing || player.Position.TotalSeconds <= beforeRoom || libraryRows != Rows.ItemsSource)
			{
				throw new InvalidOperationException("Listening room disturbed playback or the library.");
			}
			string environmentVariable10 = Environment.GetEnvironmentVariable("PRISMA_ROOM_PREVIEW");
			if (environmentVariable10 != null)
			{
				FrameworkElement frameworkElement2 = (FrameworkElement)room.Content;
				RenderTargetBitmap renderTargetBitmap6 = new RenderTargetBitmap((int)frameworkElement2.ActualWidth, (int)frameworkElement2.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap6.Render(frameworkElement2);
				PngBitmapEncoder pngBitmapEncoder6 = new PngBitmapEncoder();
				pngBitmapEncoder6.Frames.Add(BitmapFrame.Create(renderTargetBitmap6));
				using FileStream stream7 = System.IO.File.Create(environmentVariable10);
				pngBitmapEncoder6.Save(stream7);
			}
			room.Close();
			if (!playing || (object)Transport.Parent != Shell || TransportTitle.Text != Now.Text || libraryRows != Rows.ItemsSource)
			{
				throw new InvalidOperationException("Closing the listening room disturbed playback or the library.");
			}
			Console.WriteLine("Independent listening room preserved playback and library state.");
			string environmentVariable11 = Environment.GetEnvironmentVariable("PRISMA_PCM_PROBE");
			if (environmentVariable11 != null)
			{
				player.StrictDirect = true;
				opened = false;
				player.Open(new Uri(environmentVariable11));
				if (opened && player.IsExclusive)
				{
					string outputNotice = player.OutputNotice;
					if (outputNotice != null && outputNotice.Contains("PCM directo"))
					{
						player.Play();
						await Task.Delay(500);
						if (player.Position.TotalMilliseconds < 200.0)
						{
							throw new InvalidOperationException("Direct PCM playback did not advance.");
						}
						Console.WriteLine(player.OutputNotice);
						goto IL_23f4;
					}
				}
				throw new InvalidOperationException("Direct PCM open failed.");
			}
			goto IL_23f4;
			IL_23f4:
			using Windows.Media.Playback.MediaPlayer mediaPlayer = new Windows.Media.Playback.MediaPlayer();
			CastingDevicePicker castingDevicePicker = new CastingDevicePicker();
			InitializeWithWindow.Initialize(castingDevicePicker, new WindowInteropHelper(this).Handle);
			castingDevicePicker.Filter.SupportsAudio = true;
			if (mediaPlayer.GetAsCastingSource() == null)
			{
				throw new InvalidOperationException("Windows no creó la fuente de transmisión.");
			}
			Console.WriteLine("Casting: selector y fuente inicializados");
            ShowAudioOutputMenu(AudioOutputButton, new RoutedEventArgs());
            for (int attempt = 0; attempt < 50 && audioDevicePicker == null; attempt++) await Task.Delay(50);
            if (audioOutputPopup?.IsOpen != true || audioOutputSurface == null || audioDevicePicker is not { Items.Count: > 0 } ||
                (!player.StrictDirect && (audioRatePicker is not { Items.Count: > 0 } || audioRatePicker.Items[0]?.ToString() !=
                    LocalTextExtension.Translate("Automática · seguir la frecuencia de la pista"))))
                throw new InvalidOperationException("The audio output panel did not expose device and exclusive-rate choices.");
            CaptureDesignPreview(audioOutputSurface, "PRISMA_AUDIO_MENU_PREVIEW");
            audioOutputPopup.IsOpen = false;
            if (Environment.GetEnvironmentVariable("PRISMA_OUTPUT_FALLBACK_PROBE") != null)
            {
                player.OutputId = "{0.0.0.00000000}." + Guid.NewGuid().ToString("B");
                string requestedOutput = player.OutputId;
                player.Position = TimeSpan.FromSeconds(2);
                SetPlaying(true);
                await HandleAudioFailure(new COMException("Simulated endpoint invalidation", unchecked((int)0x88890004)));
                if (!opened || !playing || player.OutputId != requestedOutput || player.ActiveOutputId == null ||
                    player.ActiveOutputId == requestedOutput || Math.Abs(player.Position.TotalSeconds - 2) > .35)
                    throw new InvalidOperationException($"Unavailable output did not fall back to the system device: {Status.Text}");
                Console.WriteLine("Unavailable device fallback preserved the requested ID and exposed the actual endpoint.");
            }
            Console.WriteLine("Audio output panel opened and exposed device and sample-rate choices.");
			Console.WriteLine(player.OutputNotice ?? (player.IsExclusive ? "WASAPI exclusive" : "WASAPI shared"));
			Console.WriteLine($"OK: {demo.Tracks.Count} demos y reproducción de {track.Name}");
			System.Windows.Application.Current.Shutdown(0);
		}
		catch (Exception value)
		{
			Console.Error.WriteLine(value);
			System.Windows.Application.Current.Shutdown(1);
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

	[DllImport("dwmapi.dll")]
	private static extern int DwmExtendFrameIntoClientArea(nint window, ref FrameMargins margins);

	private Brush DefaultShellBrush()
	{
		if (appearanceProfile == 2) return new SolidColorBrush(Color.FromRgb(19, 21, 27));
		if (!micaActive)
		{
			return new SolidColorBrush(Color.FromRgb(32, 32, 32));
		}
		return new SolidColorBrush(Color.FromArgb(48, 0, 0, 0));
	}

	private Brush ArtworkShellBrush(Color accent)
	{
		if (appearanceProfile == 2) return DefaultShellBrush();
		double strength = appearanceProfile == 1 ? .14 : .35;
		if (!micaActive)
		{
			return new LinearGradientBrush(Color.FromRgb((byte)(accent.R * strength + 20.8),
				(byte)(accent.G * strength + 20.8), (byte)(accent.B * strength + 20.8)), Color.FromRgb(32, 32, 32), 120.0);
		}
		return new LinearGradientBrush(Color.FromArgb((byte)(strength * 243), accent.R, accent.G, accent.B), Color.FromArgb(32, 0, 0, 0), 120.0);
	}

	private void ShellMouseMove(object sender, System.Windows.Input.MouseEventArgs args)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		if (appearanceProfile == 0 && SystemParameters.ClientAreaAnimation && Shell.ActualWidth > 0 && Shell.ActualHeight > 0)
		{
			Point position = args.GetPosition(Shell);
			Point val = new Point(position.X / Shell.ActualWidth, position.Y / Shell.ActualHeight);
			if (GlassReflection.Background is RadialGradientBrush radialGradientBrush)
			{
				Point center = (radialGradientBrush.GradientOrigin = val);
				radialGradientBrush.Center = center;
			}
			if (AudioGlow.Background is RadialGradientBrush radialGradientBrush2)
			{
				Point center = (radialGradientBrush2.GradientOrigin = val);
				radialGradientBrush2.Center = center;
			}
		}
	}

	private void SetArtworkAccent(Color accent)
	{
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		double num = (0.2126 * (double)(int)accent.R + 0.7152 * (double)(int)accent.G + 0.0722 * (double)(int)accent.B) / 255.0;
		System.Windows.Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
		System.Windows.Application.Current.Resources["OnAccentBrush"] = new SolidColorBrush((num > 0.58) ? Colors.Black : Colors.White);
		Border[] array = spectrumBars;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Background = new SolidColorBrush(accent);
		}
		AudioGlow.Background = new RadialGradientBrush(Color.FromArgb(85, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
		ArtworkTint.Background = new RadialGradientBrush
		{
			Center = new Point(0.68, 0.28),
			GradientOrigin = new Point(0.68, 0.28),
			RadiusX = 0.78,
			RadiusY = 0.72,
			GradientStops = new GradientStopCollection
			{
				new GradientStop(Color.FromArgb(112, accent.R, accent.G, accent.B), 0.0),
				new GradientStop(Color.FromArgb(54, accent.R, accent.G, accent.B), 0.45),
				new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1.0)
			}
		};
		GlassReflection.Background = new RadialGradientBrush
		{
			RadiusX = 0.42,
			RadiusY = 0.48,
			GradientStops = new GradientStopCollection
			{
				new GradientStop(Color.FromArgb(58, accent.R, accent.G, accent.B), 0.0),
				new GradientStop(Color.FromArgb(20, accent.R, accent.G, accent.B), 0.35),
				new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1.0)
			}
		};
	}

	private void ApplyFluentBackdrop()
	{
		try
		{
			if (Environment.OSVersion.Version.Build < 22621)
			{
				return;
			}
			nint handle = new WindowInteropHelper(this).Handle;
			HwndSource hwndSource = HwndSource.FromHwnd(handle);
			if (hwndSource?.CompositionTarget == null)
			{
				return;
			}
			int value = 2;
			if (DwmSetWindowAttribute(handle, 38, ref value, 4) == 0)
			{
				FrameMargins margins = new FrameMargins
				{
					Left = -1,
					Right = -1,
					Top = -1,
					Bottom = -1
				};
				if (DwmExtendFrameIntoClientArea(handle, ref margins) == 0)
				{
					hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
					Background = Brushes.Transparent;
					micaActive = true;
					Shell.Background = DefaultShellBrush();
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void EnableDarkTitleBar()
	{
		int value = 1;
		DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref value, 4);
	}

	private void Save(bool tracksChanged = false)
	{
		try
		{
			unsavedTracks |= tracksChanged;
			if (database != null)
			{
				database.Save(library, unsavedTracks);
				unsavedTracks = false;
			}
		}
		catch (Exception ex)
		{
			Status.Text = LocalTextExtension.Translate("No se guardó la biblioteca. ") + ex.Message;
		}
	}

	private async void AddFolder(object sender, RoutedEventArgs args)
	{
		if (scanning)
		{
			return;
		}
		using FolderBrowserDialog picker = new FolderBrowserDialog
		{
			Description = LocalTextExtension.Translate("Elige una carpeta local o una carpeta compartida SMB"),
			ShowNewFolderButton = false,
			UseDescriptionForTitle = true
		};
		if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
		{
			await Scan(new string[1] { picker.SelectedPath }, add: true);
		}
	}

	private async void ConnectNas(object sender, RoutedEventArgs args)
	{
		if (scanning)
		{
			return;
		}

		Window dialog = new Window
		{
			Title = LocalTextExtension.Translate("Conectar NAS"),
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Width = 500,
			SizeToContent = SizeToContent.Height,
			ResizeMode = ResizeMode.NoResize,
			Background = new SolidColorBrush(Color.FromRgb(26, 29, 37)),
			Foreground = Brushes.White,
			ShowInTaskbar = false
		};
		Grid content = new Grid { Margin = new Thickness(24) };
		content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		TextBlock title = new TextBlock
		{
			Text = LocalTextExtension.Translate("Conecta una carpeta compartida"),
			FontSize = 21,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 8)
		};
		content.Children.Add(title);

		TextBlock instructions = new TextBlock
		{
			Text = LocalTextExtension.Translate("Escribe la ruta UNC del NAS o busca una carpeta compartida. Debe estar disponible en el Explorador de archivos; Prisma Music no almacena tus credenciales."),
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(180, 187, 200)),
			Margin = new Thickness(0, 0, 0, 16)
		};
		Grid.SetRow(instructions, 1);
		content.Children.Add(instructions);

		System.Windows.Controls.TextBox pathInput = new System.Windows.Controls.TextBox
		{
			Height = 38,
			FontSize = 14,
			Padding = new Thickness(9, 7, 9, 7),
			VerticalContentAlignment = VerticalAlignment.Center,
			ToolTip = @"\\servidor\compartida\Musica"
		};
		Grid.SetRow(pathInput, 2);
		content.Children.Add(pathInput);

		TextBlock error = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 150)),
			Margin = new Thickness(0, 8, 0, 0)
		};
		Grid.SetRow(error, 3);
		content.Children.Add(error);

		StackPanel actions = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(0, 18, 0, 0)
		};
		System.Windows.Controls.Button browse = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Buscar carpeta…"),
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(12, 7, 12, 7)
		};
		browse.Click += (_, _) =>
		{
			using FolderBrowserDialog picker = new FolderBrowserDialog
			{
				Description = LocalTextExtension.Translate("Elige la carpeta compartida de tu NAS"),
				ShowNewFolderButton = false,
				UseDescriptionForTitle = true
			};
			if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				pathInput.Text = picker.SelectedPath;
			}
		};
		System.Windows.Controls.Button connect = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Conectar"),
			IsDefault = true,
			Padding = new Thickness(16, 7, 16, 7)
		};
		connect.Click += (_, _) =>
		{
			string path = pathInput.Text.Trim();
			if (!path.StartsWith(@"\\", StringComparison.Ordinal))
			{
				error.Text = LocalTextExtension.Translate("Usa una ruta de red UNC que comience con \\\\. ");
				pathInput.Focus();
				return;
			}
			if (!Directory.Exists(path))
			{
				error.Text = LocalTextExtension.Translate("No se puede acceder a esa ruta. Comprueba la conexión y los permisos en el Explorador de archivos.");
				pathInput.Focus();
				return;
			}
			pathInput.Text = Path.TrimEndingDirectorySeparator(path);
			dialog.DialogResult = true;
		};
		actions.Children.Add(browse);
		actions.Children.Add(connect);
		Grid.SetRow(actions, 4);
		content.Children.Add(actions);
		dialog.Content = content;

		if (dialog.ShowDialog() == true)
		{
			await Scan(new[] { pathInput.Text }, add: true);
		}
	}

	private void RefreshLibrarySources()
	{
		LibrarySources.Items.Clear();
		if (library.Folders.Count == 0)
		{
			LibrarySources.Items.Add(new TextBlock
			{
				Text = LocalTextExtension.Translate("Aún no hay carpetas"),
				FontSize = 10,
				Foreground = new SolidColorBrush(Color.FromRgb(145, 153, 167)),
				Margin = new Thickness(8, 2, 4, 5)
			});
			return;
		}

		foreach (string folder in library.Folders.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
		{
			string sourcePath = folder;
			string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
			if (string.IsNullOrWhiteSpace(name))
			{
				name = sourcePath;
			}
			StackPanel label = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
			label.Children.Add(new TextBlock
			{
				Text = sourcePath.StartsWith(@"\\", StringComparison.Ordinal) ? "\uE968" : "\uE8B7",
				FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
				Foreground = (Brush)System.Windows.Application.Current.Resources["AccentBrush"],
				FontSize = 13,
				Margin = new Thickness(0, 0, 8, 0),
				VerticalAlignment = VerticalAlignment.Center
			});
			label.Children.Add(new TextBlock
			{
				Text = name,
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center
			});

			System.Windows.Controls.Button sourceButton = new System.Windows.Controls.Button
			{
				Content = label,
				Tag = sourcePath,
				ToolTip = sourcePath,
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
				Background = string.Equals(selectedLibrarySource, sourcePath, StringComparison.OrdinalIgnoreCase)
					? new SolidColorBrush(Color.FromArgb(34, 255, 255, 255))
					: Brushes.Transparent,
				Foreground = Brushes.White,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(8, 5, 6, 5),
				Margin = new Thickness(0, 1, 0, 1)
			};
			AutomationProperties.SetName(sourceButton, LocalTextExtension.Format("Abrir fuente {0}", name));
			sourceButton.Click += SelectLibrarySource;
			MenuItem reveal = new MenuItem { Header = LocalTextExtension.Translate("Abrir en el Explorador de archivos") };
			reveal.Click += (_, _) => RevealLibrarySource(sourcePath);
			MenuItem remove = new MenuItem { Header = LocalTextExtension.Translate("Quitar fuente"), StaysOpenOnClick = false };
			remove.Click += (_, _) => RemoveLibrarySource(sourcePath);
			sourceButton.ContextMenu = new ContextMenu();
			sourceButton.ContextMenu.Items.Add(reveal);
			sourceButton.ContextMenu.Items.Add(remove);
			LibrarySources.Items.Add(sourceButton);
		}
	}

	private void SelectLibrarySource(object sender, RoutedEventArgs args)
	{
		if (sender is not System.Windows.Controls.Button { Tag: string path })
		{
			return;
		}
		selectedLibrarySource = path;
		collection = null;
		collectionHistory.Clear();
		galleryPage = 0;
		RefreshLibrarySources();
		if (Navigation.SelectedIndex != 0)
		{
			Navigation.SelectedIndex = 0;
		}
		else
		{
			UpdateCollectionHeading();
		}
	}

	private void ClearLibrarySource(object sender, RoutedEventArgs args)
	{
		selectedLibrarySource = null;
		collection = null;
		collectionHistory.Clear();
		RefreshLibrarySources();
		UpdateCollectionHeading();
	}

	private void RevealLibrarySource(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			Status.Text = LocalTextExtension.Translate("No se pudo abrir la carpeta. ") + ex.Message;
		}
	}

	private void RemoveLibrarySource(string path)
	{
		if (scanning)
		{
			return;
		}
		MessageBoxResult result = System.Windows.MessageBox.Show(
			this,
			LocalTextExtension.Translate("Se quitará esta fuente y sus pistas de la biblioteca. Los archivos del disco no se borrarán."),
			LocalTextExtension.Translate("Quitar fuente"),
			MessageBoxButton.OKCancel,
			MessageBoxImage.Warning);
		if (result != MessageBoxResult.OK)
		{
			return;
		}

		string normalized = Path.TrimEndingDirectorySeparator(path);
		library.Folders.RemoveAll(folder => string.Equals(Path.TrimEndingDirectorySeparator(folder), normalized, StringComparison.OrdinalIgnoreCase));
		HashSet<string> removedPaths = library.Tracks
			.Where(track => IsTrackInSource(track.Path, normalized)
				&& !library.Folders.Any(folder => IsTrackInSource(track.Path, folder)))
			.Select(track => track.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		library.Tracks.RemoveAll(track => removedPaths.Contains(track.Path));
		library.Favorites.RemoveWhere(removedPaths.Contains);
		library.Playlist.RemoveAll(removedPaths.Contains);
		library.History.RemoveAll(removedPaths.Contains);
		foreach (List<string> tracks in library.Playlists.Values)
		{
			tracks.RemoveAll(removedPaths.Contains);
		}
		queue.RemoveAll(track => removedPaths.Contains(track.Path));
		context = context.Where(track => !removedPaths.Contains(track.Path)).ToArray();
		originalContext = originalContext.Where(track => !removedPaths.Contains(track.Path)).ToArray();
		if (string.Equals(selectedLibrarySource, path, StringComparison.OrdinalIgnoreCase))
		{
			selectedLibrarySource = null;
			collection = null;
			collectionHistory.Clear();
			UpdateCollectionHeading();
		}
		RefreshLibrarySources();
		RefreshFilters();
		RefreshRows();
		UpdatePlaylistPicker();
		Status.Text = LocalTextExtension.Translate("Se quitó la fuente. Los archivos originales siguen intactos.");
		Save(tracksChanged: true);
	}

	private static bool IsTrackInSource(string trackPath, string sourcePath)
	{
		string source = Path.TrimEndingDirectorySeparator(sourcePath);
		string prefix = Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar;
		return string.Equals(trackPath, source, StringComparison.OrdinalIgnoreCase)
			|| trackPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	private async void AddDemos(object sender, RoutedEventArgs args)
	{
		await Scan(new string[1] { Path.Combine(AppContext.BaseDirectory, "Demo") }, add: true);
	}

	private async void Rescan(object sender, RoutedEventArgs args)
	{
		await Scan(library.Folders.ToArray(), add: false);
	}

	private void CancelScan(object sender, RoutedEventArgs args)
	{
		scanCancellation?.Cancel();
	}

	private async Task Scan(string[] folders, bool add)
	{
		if (scanning || folders.Length == 0)
		{
			return;
		}
		CancellationTokenSource cancellation = (scanCancellation = new CancellationTokenSource());
		CancellationToken token = cancellation.Token;
		scanning = true;
		CancelScanButton.Visibility = Visibility.Visible;
		ScanProgressPanel.Visibility = Visibility.Visible;
		ScanProgressText.Text = LocalTextExtension.Translate("Preparando el escaneo…");
		Status.Text = "";
		Dictionary<string, Track> indexed = library.Tracks.ToDictionary((Track t) => t.Path, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Track> originalTracks = new Dictionary<string, Track>(indexed, StringComparer.OrdinalIgnoreCase);
		List<string> errors = new List<string>();
		List<string> addedFolders = new List<string>();
		int filesProcessed = 0;
		int tracksProcessed = 0;
		int errorsProcessed = 0;
		try
		{
			foreach (string folder in folders)
			{
				token.ThrowIfCancellationRequested();
				try
				{
					ScanProgressText.Text = LocalTextExtension.Format("Escaneando carpeta: {0}", folder);
					int folderFileOffset = filesProcessed;
					int folderTrackOffset = tracksProcessed;
					int folderErrorOffset = errorsProcessed;
					IProgress<LibraryScanProgress> progress = new Progress<LibraryScanProgress>(value =>
					{
						if (!scanning || !ReferenceEquals(scanCancellation, cancellation))
						{
							return;
						}
						string item = value.CurrentTrack == null
							? LocalTextExtension.Format("No se pudieron leer las etiquetas de {0}: {1}", Path.GetFileName(value.Path), value.Error)
							: LocalTextExtension.Format("Leyendo etiquetas: {0} · {1} · {2}{3}",
								value.CurrentTrack.Name,
								value.CurrentTrack.Artist,
								value.CurrentTrack.Album,
								value.CurrentTrack.Year > 0 ? $" · {value.CurrentTrack.Year}" : "");
						ScanProgressText.Text = LocalTextExtension.Format("Escaneando {0:N0} archivos de audio · {1:N0} pistas reconocidas · {2:N0} errores\n{3}",
							folderFileOffset + value.FilesProcessed, folderTrackOffset + value.TracksFound,
							folderErrorOffset + value.Errors, item);
					});
					(List<Track>, int) tuple = await Task.Run(() => LibraryScanner.ReadFolder(folder, token, progress), token);
					filesProcessed += tuple.Item1.Count + tuple.Item2;
					tracksProcessed += tuple.Item1.Count;
					errorsProcessed += tuple.Item2;
					foreach (Track item in tuple.Item1)
					{
						indexed[item.Path] = item;
					}
					if (tuple.Item2 == 0)
					{
						HashSet<string> seen = tuple.Item1.Select((Track t) => t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
						string prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
						string[] array = indexed.Keys.Where((string path) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !seen.Contains(path)).ToArray();
						foreach (string key in array)
						{
							indexed.Remove(key);
						}
					}
					else
					{
						errors.Add($"{tuple.Item2} archivos con errores en {folder}");
					}
					if (add && !library.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
					{
						addedFolders.Add(folder);
					}
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex2)
				{
					errors.Add(ex2.Message);
				}
			}
			token.ThrowIfCancellationRequested();
			library.Tracks = indexed.Values.Select(ApplyLibraryTags).ToList();
			library.Folders.AddRange(addedFolders);
			int newTracks = library.Tracks.Count(track => !originalTracks.ContainsKey(track.Path));
			int updatedTracks = library.Tracks.Count(track =>
				originalTracks.TryGetValue(track.Path, out Track previous) && previous != track);
			RefreshLibrarySources();
			RefreshFilters();
			RefreshRows();
			Status.Text = errors.Count == 0
				? LocalTextExtension.Format("Escaneo terminado: {0:N0} pistas nuevas, {1:N0} etiquetas actualizadas. Las etiquetas se leyeron de los archivos; no se modificaron.", newTracks, updatedTracks)
				: LocalTextExtension.Format("Escaneo parcial: {0:N0} pistas nuevas, {1:N0} etiquetas actualizadas. {2}", newTracks, updatedTracks, errors[0]);
			Save(tracksChanged: true);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			Status.Text = LocalTextExtension.Translate("Escaneo cancelado. La biblioteca anterior se conservó.");
		}
		finally
		{
			scanning = false;
			CancelScanButton.Visibility = Visibility.Collapsed;
			ScanProgressPanel.Visibility = Visibility.Collapsed;
			scanCancellation = null;
			cancellation.Dispose();
		}
	}

	private void RefreshFilters()
	{
		string artist = ArtistFilter.SelectedItem as string;
		ArtistFilter.ItemsSource = new[] { LocalTextExtension.Translate("Todos los artistas") }.Concat(library.Tracks
			.Select(track => track.Artist).Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name)).ToArray();
		ArtistFilter.SelectedItem = artist;
		if (ArtistFilter.SelectedIndex < 0) ArtistFilter.SelectedIndex = 0;
		string selectedItem = Genre.SelectedItem as string;
		string selectedItem2 = Year.SelectedItem as string;
		Genre.ItemsSource = new string[1] { LocalTextExtension.Translate("Todos los géneros") }.Concat(from g in (from t in library.Tracks
				select t.Genre into g
				where g.Length > 0
				select g).Distinct(StringComparer.CurrentCultureIgnoreCase)
			orderby g
			select g).ToArray();
		Year.ItemsSource = new string[1] { LocalTextExtension.Translate("Todos los años") }.Concat(from y in (from t in library.Tracks
				select t.Year into y
				where y != 0
				select y).Distinct()
			orderby y descending
			select y.ToString()).ToArray();
		Genre.SelectedItem = selectedItem;
		Year.SelectedItem = selectedItem2;
		if (Genre.SelectedIndex < 0)
		{
			Genre.SelectedIndex = 0;
		}
		if (Year.SelectedIndex < 0)
		{
			Year.SelectedIndex = 0;
		}
	}

	private void Navigate(object sender, SelectionChangedEventArgs args)
	{
		if (ready)
		{
			collection = null;
			collectionHistory.Clear();
			galleryPage = 0;
			UpdateCollectionHeading();
		}
	}

	private void Filter(object sender, RoutedEventArgs args)
	{
		if (!ready)
		{
			return;
		}
		galleryPage = 0;
		if (sender == Search && collection != null && !string.IsNullOrWhiteSpace(Search.Text))
		{
			if (Navigation.SelectedIndex != 0)
			{
				Navigation.SelectedIndex = 0;
				return;
			}
			collection = null;
			collectionHistory.Clear();
			UpdateCollectionHeading();
		}
		else
		{
			RefreshRows();
		}
	}

	private void RefreshRows()
	{
		galleryExportTracks = null;
		UpdateCollectionHeader();
		System.Windows.Controls.ListBox rows = Rows;
		bool flag = collection == null;
		if (flag)
		{
			int selectedIndex = Navigation.SelectedIndex;
			bool flag2 = (uint)(selectedIndex - 1) <= 1u;
			flag = flag2;
		}
		rows.Tag = (flag ? "Gallery" : null);
		GalleryPager.Visibility = Visibility.Collapsed;
		QueueTools.Visibility = ((Navigation.SelectedIndex != 4) ? Visibility.Collapsed : Visibility.Visible);
		PlaylistPicker.Visibility = ((Navigation.SelectedIndex != 6) ? Visibility.Collapsed : Visibility.Visible);
		LosslessNotice.Visibility = Visibility.Collapsed;
		BackToCollection.Visibility = ((collection == null) ? Visibility.Collapsed : Visibility.Visible);
		if (collection != null)
		{
			Rows.ItemsSource = collection.Tracks.Select((Track track, int index) => new LibraryRow(albumPage ? $"{index + 1:00}  {track.Name}" : track.Name, track.Detail, new Track[1] { track })).ToArray();
			UpdateEmptyLibrary();
			return;
		}
		Dictionary<string, Track> lookup = library.Tracks.ToDictionary((Track t) => t.Path, StringComparer.OrdinalIgnoreCase);
		bool losslessView = ReferenceEquals(Navigation.SelectedItem, LosslessNavigation);
		IEnumerable<Track> source = Navigation.SelectedIndex switch
		{
			3 => library.Tracks.Where((Track t) => library.Favorites.Contains(t.Path)), 
			4 => queue, 
			5 => from path in library.History.Where(lookup.ContainsKey)
				select lookup[path], 
			6 => from path in ActivePlaylist.Where(lookup.ContainsKey)
				select lookup[path], 
			_ => library.Tracks, 
		};
		if (losslessView) source = source.Where(track => track.IsLossless);
		if (selectedLibrarySource != null)
		{
			source = source.Where(track => IsTrackInSource(track.Path, selectedLibrarySource));
		}
		string[] query = Search.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		source = source.Where(t => (Genre.SelectedIndex <= 0 || t.Genre.Equals((string)Genre.SelectedItem, StringComparison.CurrentCultureIgnoreCase))
			&& (Year.SelectedIndex <= 0 || t.Year.ToString() == (string)Year.SelectedItem)
			&& (ArtistFilter.SelectedIndex <= 0 || string.Equals(t.Artist, ArtistFilter.SelectedItem as string, StringComparison.CurrentCultureIgnoreCase))
			&& MatchesLibraryQuery(t, query));
		if (Navigation.SelectedIndex == 1)
		{
			var source2 = from t in source
				group t by (Album: t.Album, Artist: (t.AlbumArtist.Length > 0) ? t.AlbumArtist : t.Artist) into g
				select new
				{
					Key = g.Key,
					Tracks = (from t in g
						orderby t.Disc, t.Number, t.Name
						select t).ToArray()
				};
			var orderedAlbums = Sort.SelectedIndex switch
			{
				1 => source2.OrderBy(g => g.Key.Artist).ThenBy(g => g.Key.Album),
				2 => source2.OrderByDescending(g => g.Tracks[0].Year).ThenBy(g => g.Key.Album),
				_ => source2.OrderBy(g => g.Key.Album).ThenBy(g => g.Key.Artist)
			};
			SetGalleryRows(orderedAlbums.Select(g => new LibraryRow(g.Key.Album, g.Key.Artist, g.Tracks, string.Join(" · ", new string[2]
			{
				(g.Tracks[0].Year != 0) ? g.Tracks[0].Year.ToString() : "",
				(g.Tracks.Length == 1) ? LocalTextExtension.Translate("1 pista") : LocalTextExtension.Format("{0:N0} pistas", g.Tracks.Length)
			}.Where((string item) => item.Length > 0)))).ToArray());
		}
		else if (Navigation.SelectedIndex == 2)
		{
			SetGalleryRows((from t in source
				group t by t.Artist into g
				orderby g.Key
				select new LibraryRow(g.Key, (g.Count() == 1) ? LocalTextExtension.Translate("1 pista") : LocalTextExtension.Format("{0:N0} pistas", g.Count()), (from t in g
					orderby t.Album, t.Number
					select t).ToArray())).ToArray());
		}
		else
		{
			if (Navigation.SelectedIndex < 4 || losslessView)
			{
				source = Sort.SelectedIndex switch
				{
					1 => (IEnumerable<Track>)(from t in source
						orderby t.Artist, t.Name
						select t), 
					2 => from t in source
						orderby t.Year descending, t.Name
						select t, 
					3 => source.OrderBy(t => t.Album).ThenBy(t => t.Disc).ThenBy(t => t.Number).ThenBy(t => t.Name),
					_ => source.OrderBy((Track t) => t.Name), 
				};
			}
			Rows.ItemsSource = source.Select((Track t) => new LibraryRow(t.Name, t.Detail, new Track[1] { t })).ToArray();
			if (losslessView)
			{
				CollectionCount.Text = Rows.Items.Count == 1
					? LocalTextExtension.Translate("1 pista sin pérdida")
					: LocalTextExtension.Format("{0:N0} pistas sin pérdida", Rows.Items.Count);
				if (Rows.Items.Count == 0)
				{
					LosslessNotice.Text = LocalTextExtension.Translate("No hay pistas sin pérdida en esta vista. Actualiza la biblioteca para identificar el formato de los archivos importados anteriormente.");
					LosslessNotice.Visibility = Visibility.Visible;
				}
			}
		}
		UpdateEmptyLibrary();
	}

	private static bool MatchesLibraryQuery(Track track, string[] query)
	{
		if (query.Length == 0) return true;
		string searchable = $"{track.Name} {track.Detail} {track.AlbumArtist} {track.Genre} {track.FormatLabel}";
		return query.All(word => CultureInfo.CurrentCulture.CompareInfo.IndexOf(searchable, word,
			CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
	}

	private void Activate(object sender, MouseButtonEventArgs args)
	{
		ActivateSelection();
	}

	private void CloseCollection(object sender, RoutedEventArgs args)
	{
		if (collectionHistory.TryPop(out (LibraryRow, int, bool) result))
		{
			(collection, _, albumPage) = result;
			UpdateCollectionHeading();
			if (result.Item2 < Rows.Items.Count)
			{
				Rows.SelectedIndex = result.Item2;
			}
		}
		else
		{
			collection = null;
			UpdateCollectionHeading();
		}
	}

	private void UpdateCollectionHeading()
	{
		string section = (Navigation.SelectedItem as ListBoxItem)?.Content?.ToString() ?? LocalTextExtension.Translate("Canciones");
		string sourceName = selectedLibrarySource == null ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedLibrarySource));
		if (string.IsNullOrWhiteSpace(sourceName))
		{
			sourceName = selectedLibrarySource;
		}
		Heading.Text = collection?.Name ?? (sourceName == null ? section : sourceName + " · " + section);
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
		if (Rows.SelectedItem is LibraryRow libraryRow)
		{
			Track track = libraryRow.Tracks[0];
			Track[] array = (from item in library.Tracks
				where string.Equals(item.Artist, track.Artist, StringComparison.OrdinalIgnoreCase)
				orderby item.Album, item.Disc, item.Number
				select item).ToArray();
			if (array.Length != 0)
			{
				OpenCollection(new LibraryRow(track.Artist, "", array), album: false);
			}
		}
	}

	private static string AlbumOwner(Track track)
	{
		if (!string.IsNullOrEmpty(track.AlbumArtist))
		{
			return track.AlbumArtist;
		}
		return track.Artist;
	}

	private void BrowseRelatedAlbum(object sender, RoutedEventArgs args)
	{
		if (Rows.SelectedItem is LibraryRow libraryRow)
		{
			Track track = libraryRow.Tracks[0];
			Track[] array = (from item in library.Tracks
				where string.Equals(item.Album, track.Album, StringComparison.OrdinalIgnoreCase) && string.Equals(AlbumOwner(item), AlbumOwner(track), StringComparison.OrdinalIgnoreCase)
				orderby item.Disc, item.Number, item.Name
				select item).ToArray();
			if (array.Length != 0)
			{
				OpenCollection(new LibraryRow(track.Album, AlbumOwner(track), array), album: true);
			}
		}
	}

	private void ActivateSelection()
	{
		if (!(Rows.SelectedItem is LibraryRow libraryRow))
		{
			return;
		}
		bool flag = collection == null;
		if (flag)
		{
			int selectedIndex = Navigation.SelectedIndex;
			bool flag2 = (uint)(selectedIndex - 1) <= 1u;
			flag = flag2;
		}
		if (flag)
		{
			OpenCollection(libraryRow, Navigation.SelectedIndex == 1);
			return;
		}
		context = ((libraryRow.Tracks.Length > 1) ? libraryRow.Tracks : ((IEnumerable<LibraryRow>)Rows.ItemsSource).SelectMany((LibraryRow r) => r.Tracks).ToArray());
		originalContext = context;
		ReorderPlayback(libraryRow.Tracks[0]);
		Play(libraryRow.Tracks[0]);
	}

	private async void LoadPresentation(Track track)
	{
		Cover.Tag = track.Album;
		Cover.Source = null;
		AudioFormat.Text = Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant();
		UpdateMini();
		Shell.Background = DefaultShellBrush();
		SetLyrics("");
		(string, BitmapImage, string) tuple = await Task.Run(() =>
		{
			string text = "";
			BitmapImage bitmapImage = null;
			List<string> list = new List<string> { Path.GetExtension(track.Path).TrimStart('.').ToUpperInvariant() };
			try
			{
				string path = Path.ChangeExtension(track.Path, ".lrc");
				if (System.IO.File.Exists(path))
				{
					text = System.IO.File.ReadAllText(path);
				}
				if (string.IsNullOrWhiteSpace(text))
				{
					string plainPath = Path.ChangeExtension(track.Path, ".txt");
					if (System.IO.File.Exists(plainPath)) text = System.IO.File.ReadAllText(plainPath);
				}
				TagLib.File file = TagLib.File.Create(track.Path);
				try
				{
					if (text.Length == 0)
					{
						text = file.Tag.Lyrics ?? "";
					}
					Properties properties = file.Properties;
					if (properties.BitsPerSample > 0)
					{
						list.Add($"{properties.BitsPerSample} bit");
					}
					if (properties.AudioSampleRate > 0)
					{
						list.Add($"{(double)properties.AudioSampleRate / 1000.0:0.##} kHz");
					}
					if (properties.AudioBitrate > 0)
					{
						list.Add($"{properties.AudioBitrate} kb/s");
					}
					byte[] array = ReadArtworkPayload(track.Path, () => file.Tag.Pictures.FirstOrDefault()?.Data.Data);
					if (array != null && array.Length > 0)
					{
						using MemoryStream streamSource = new MemoryStream(array);
						bitmapImage = new BitmapImage();
						bitmapImage.BeginInit();
						bitmapImage.StreamSource = streamSource;
						bitmapImage.DecodePixelWidth = 500;
						bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage.EndInit();
						((Freezable)bitmapImage).Freeze();
					}
				}
				finally
				{
					if (file != null)
					{
						((IDisposable)file).Dispose();
					}
				}
			}
			catch (Exception)
			{
			}
			return (lyrics: text, image: bitmapImage, format: string.Join(" · ", list));
		});
		if (current != track)
		{
			return;
		}
		Cover.Source = tuple.Item2;
		if (tuple.Item2 != null && SystemParameters.ClientAreaAnimation)
		{
			Cover.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(260.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		AudioFormat.Text = tuple.Item3;
		UpdateMini();
		if (artworkColors)
		{
			BitmapImage item = tuple.Item2;
			if (item != null && ((Freezable)item).IsFrozen)
			{
				Color color = ArtworkPalette.ExtractAccent(tuple.Item2, windowsAccent);
				SetArtworkAccent(color);
				Shell.Background = ArtworkShellBrush(color);
				goto IL_0274;
			}
		}
		if (artworkColors)
		{
			Color color2 = ArtworkTile.AccentForCaption(track.Album, windowsAccent);
			SetArtworkAccent(color2);
			Shell.Background = ArtworkShellBrush(color2);
		}
		else
		{
			Color color3 = AccentPresetColor();
			SetArtworkAccent(color3);
			Shell.Background = ArtworkShellBrush(color3);
		}
		goto IL_0274;
		IL_0274:
		SetLyrics(tuple.Item1);
		await CompleteTrackPresentation(track);
	}

	private void SetLyrics(string text)
	{
		sourceLyrics = text;
		lyricRevision++;
		translatedLines = null;
		lyricLines = (from line in text.Split('\n').SelectMany((string line) =>
			{
				MatchCollection source = Regex.Matches(line, "\\[(\\d+):(\\d+(?:[.:]\\d+)?)\\]");
				string words = Regex.Replace(line, "\\[[^\\]]+\\]", "").Trim();
				return from Match match in source
					select ((double)(int.Parse(match.Groups[1].Value) * 60) + double.Parse(match.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture), words: words);
			})
			orderby line.Item1
			select line).ToArray();
		lyricDisplayIndex = int.MinValue;
		UpdateLyricDisplay(player.Position.TotalSeconds);
	}

	private async void LookupLyrics(object sender, RoutedEventArgs args)
	{
		Track track = current;
		if (track == null || System.Windows.MessageBox.Show(this, LocalTextExtension.Translate("Se enviarán el título, artista y álbum a LRCLIB para buscar la letra."), LocalTextExtension.Translate("Buscar letras"), MessageBoxButton.OKCancel) != MessageBoxResult.OK)
		{
			return;
		}
		try
		{
			string text = await LyricsLookup.Find(Http, track, () => (object)current == track);
			if ((object)current == track)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					SetLyrics(text);
				}
				else
				{
					Status.Text = LocalTextExtension.Translate("No se encontró una letra para esta pista.");
				}
			}
		}
		catch (Exception ex)
		{
			if ((object)current == track)
			{
				Status.Text = LocalTextExtension.Translate("Error al buscar letras. ") + ex.Message;
			}
		}
	}

	private void Tick()
	{
		UpdateSleepTimer();
		float[] spectrum = player.Spectrum;
		int num = 0;
		while (animateSpectrum && SystemParameters.ClientAreaAnimation && num < spectrumBars.Length)
		{
			double val = ((playing && num < spectrum.Length) ? (2f + 26f * spectrum[num]) : 2f);
			spectrumBars[num].Height = Math.Max(val, spectrumBars[num].Height * 0.65);
			num++;
		}
		AudioGlow.Opacity = ((appearanceProfile == 0 && animateSpectrum && playing && SystemParameters.ClientAreaAnimation) ? Math.Clamp(player.Level * 0.42, 0.025, 0.2) : 0.0);
		UpdateVisualizer();
		if (opened)
		{
			double totalSeconds = player.Position.TotalSeconds;
			double num2 = (player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0.0);
			updatingSeek = true;
			Seek.Maximum = num2;
			if (!Seek.IsMouseCaptureWithin)
			{
				Seek.Value = totalSeconds;
			}
			updatingSeek = false;
			TimeLabel.Text = FormatClock(totalSeconds);
			RemainingTimeLabel.Text = "−" + FormatClock(num2 - totalSeconds);
			UpdateLyricDisplay(totalSeconds);
		}
		PublishSystemPlayback();
	}

	private void SetPlaying(bool value)
	{
		playing = value;
		PublishSystemPlayback();
		UpdateAwakeRequest(value && keepAwake);
		PlayButton.Content = (value ? "\uE769" : "\uE768");
		string value2 = LocalTextExtension.Translate(value ? "Pausar" : "Reproducir");
		AutomationProperties.SetName((DependencyObject)(object)PlayButton, value2);
		if (miniToggle != null)
		{
			AutomationProperties.SetName((DependencyObject)(object)miniToggle, value2);
		}
		if (miniToggle != null)
		{
			miniToggle.Content = PlayButton.Content;
		}
	}

	private void Toggle(object sender, RoutedEventArgs args)
	{
		if (pendingTrack != null)
		{
			CancelPendingOpen();
			return;
		}
		if (current == null)
		{
			ActivateSelection();
			return;
		}
		if (!opened)
		{
			Play(current);
			return;
		}
		if (playing)
		{
			player.Pause();
		}
		else
		{
			player.Play();
		}
		SetPlaying(!playing);
	}

	private void Advance(bool endOfTrack = false)
	{
		if (endOfTrack && (stopAfterTrack || !continueAfterTrack))
		{
			SetStopAfterTrack(false);
			player.Pause();
			if (opened) player.Position = TimeSpan.Zero;
			SetPlaying(false);
			Tick();
			return;
		}
		if (endOfTrack && repeat == RepeatMode.Track && current != null)
		{
			Play(current, remember: false);
			return;
		}
		if (queue.Count > 0)
		{
			Track track = queue[0];
			queue.RemoveAt(0);
			Play(track);
			if (Navigation.SelectedIndex == 4)
			{
				RefreshRows();
			}
			return;
		}
		int num = Array.IndexOf(context, pendingTrack ?? current);
		if (num + 1 < context.Length)
		{
			Play(context[num + 1]);
			return;
		}
		if (repeat == RepeatMode.Collection && context.Length != 0)
		{
			Play(context[0]);
			return;
		}
		CancelPendingOpen();
		player.Pause();
		SetPlaying(value: false);
	}

	private void ShowListeningMenu(object sender, RoutedEventArgs args)
	{
		if (sender is System.Windows.Controls.Button { ContextMenu: { } contextMenu } button)
		{
			contextMenu.PlacementTarget = button;
			contextMenu.Placement = PlacementMode.Top;
			StopAfterTrackMenuItem.IsEnabled = current != null;
			contextMenu.IsOpen = true;
		}
	}

	private void ReorderPlayback(Track first)
	{
		if (!shuffle || first == null)
		{
			context = originalContext;
			return;
		}
		Track[] array = originalContext.Where((Track track) => track != first).ToArray();
		Random.Shared.Shuffle(array);
		context = new Track[1] { first }.Concat(array).ToArray();
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
			_ => RepeatMode.Off, 
		};
		UpdateRepeatButton();
	}

	private void UpdateRepeatButton()
	{
		string text = LocalTextExtension.Translate(repeat switch
		{
			RepeatMode.Track => "Repetir pista", 
			RepeatMode.Collection => "Repetir colección", 
			_ => "Repetición desactivada", 
		});
		RepeatButton.Content = ((repeat == RepeatMode.Track) ? "\ue8ed" : "\ue8ee");
		RepeatButton.ToolTip = text;
		AutomationProperties.SetName((DependencyObject)(object)RepeatButton, text);
		MarkPlaybackToggle(RepeatButton, repeat != RepeatMode.Off);
	}

	private static void MarkPlaybackToggle(System.Windows.Controls.Button button, bool active)
	{
		button.Background = active ? new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)) : Brushes.Transparent;
		button.SetResourceReference(ForegroundProperty, active ? "AccentBrush" : "Ink");
		AutomationProperties.SetItemStatus((DependencyObject)(object)button, active ? "On" : "Off");
	}

	private void Next(object sender, RoutedEventArgs args)
	{
		Advance();
	}

	private void Previous(object sender, RoutedEventArgs args)
	{
		Track result;
		if (opened && player.Position.TotalSeconds > 3.0)
		{
			player.Position = TimeSpan.Zero;
		}
		else if (previous.TryPop(out result))
		{
			Play(result, remember: false);
		}
	}

	private void UpdateVolumeControls()
	{
		VolumeControls.Visibility = (player.StrictDirect ? Visibility.Collapsed : Visibility.Visible);
		EnableVolumeButton.Visibility = ((!player.StrictDirect) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void EnableAdjustableVolume(object sender, RoutedEventArgs args)
	{
		player.StrictDirect = false;
		UpdateVolumeControls();
		ReopenOutput();
		if (!smokeTest)
		{
			SaveAudioSettings();
		}
	}

	private void ChangeVolume(object sender, RoutedPropertyChangedEventArgs<double> args)
	{
		player.Volume = args.NewValue;
		RefreshMuteButton(args.NewValue);
	}

	private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
	{
		if (opened && !updatingSeek)
		{
			player.Position = TimeSpan.FromSeconds(args.NewValue);
		}
	}

	private void Favorite(object sender, RoutedEventArgs args)
	{
		Track track = current ?? Selected;
		if (!(track == null))
		{
			bool flag = library.Favorites.Add(track.Path);
			if (!flag)
			{
				library.Favorites.Remove(track.Path);
			}
			Status.Text = (flag ? LocalTextExtension.Format("Añadida a favoritos: {0}", track.Name) : LocalTextExtension.Format("Retirada de favoritos: {0}", track.Name));
			UpdateFavoriteToolbarButton();
			Save();
			if (Navigation.SelectedIndex == 3)
			{
				RefreshRows();
			}
		}
	}

	private void RowsSelectionChanged(object sender, SelectionChangedEventArgs args)
	{
		UpdateFavoriteToolbarButton();
	}

	private void UpdateFavoriteToolbarButton()
	{
		if (FavoriteToolbarButton != null)
		{
			Track track = current ?? Selected;
			bool flag = track != null && library.Favorites.Contains(track.Path);
			FavoriteToolbarButton.Content = (flag ? "\uEB52" : "\uEB51");
			FavoriteToolbarButton.Foreground = (flag ? ((System.Windows.Application.Current.Resources["AccentBrush"] as Brush) ?? Brushes.White) : Brushes.White);
			string text = LocalTextExtension.Translate(flag ? "Quitar de favoritos" : "Favorito");
			FavoriteToolbarButton.ToolTip = text;
			AutomationProperties.SetName((DependencyObject)(object)FavoriteToolbarButton, text);
		}
	}

	private void UpdatePlaylistPicker()
	{
		PlaylistPicker.ItemsSource = new string[1] { "Mi lista" }.Concat(library.Playlists.Keys.OrderBy((string name) => name)).ToArray();
		PlaylistPicker.SelectedItem = playlistName;
	}

	private void SelectPlaylist(object sender, SelectionChangedEventArgs args)
	{
		if (PlaylistPicker.SelectedItem is string text)
		{
			playlistName = text;
			if (ready)
			{
				RefreshRows();
			}
		}
	}

	private void NewPlaylist(object sender, RoutedEventArgs args)
	{
		System.Windows.Controls.TextBox input = new System.Windows.Controls.TextBox
		{
			Width = 290.0,
			Margin = new Thickness(0.0, 6.0, 0.0, 12.0),
			MaxLength = 80
		};
		System.Windows.Controls.Button button = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Crear"),
			IsDefault = true,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Nombre de la lista")
		});
		stackPanel.Children.Add(input);
		stackPanel.Children.Add(button);
		Window dialog = new Window
		{
			Title = LocalTextExtension.Translate("Nueva lista"),
			Owner = this,
			Content = stackPanel,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			SizeToContent = SizeToContent.WidthAndHeight,
			ResizeMode = ResizeMode.NoResize,
			Background = new SolidColorBrush(Color.FromRgb(38, 38, 38))
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			string text = input.Text.Trim();
			if (text.Length == 0 || PlaylistPicker.Items.Cast<string>().Contains(text, StringComparer.CurrentCultureIgnoreCase))
			{
				System.Windows.MessageBox.Show(dialog, LocalTextExtension.Translate("Escribe un nombre distinto para la lista."), LocalTextExtension.Translate("Nueva lista"));
			}
			else
			{
				library.Playlists.Add(text, new List<string>());
				playlistName = text;
				UpdatePlaylistPicker();
				Save();
				dialog.Close();
			}
		};
		dialog.Loaded += (object _, RoutedEventArgs _) =>
		{
			input.Focus();
		};
		dialog.ShowDialog();
	}

	private void Playlist(object sender, RoutedEventArgs args)
	{
		Track selected = Selected;
		if ((object)selected != null)
		{
			bool flag = ActivePlaylist.Remove(selected.Path);
			if (!flag)
			{
				ActivePlaylist.Add(selected.Path);
			}
			Status.Text = (flag ? LocalTextExtension.Format("Retirada de {0}: {1}", playlistName, selected.Name) : LocalTextExtension.Format("Añadida a {0}: {1}", playlistName, selected.Name));
			Save();
			if (Navigation.SelectedIndex == 6)
			{
				RefreshRows();
			}
		}
	}

	private void Enqueue(object sender, RoutedEventArgs args)
	{
		Track track = Selected;
		if ((object)track != null && !queue.Any((Track item) => string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
		{
			queue.Add(track);
			Status.Text = LocalTextExtension.Format("Añadida a la cola: {0}", track.Name);
			if (Navigation.SelectedIndex == 4)
			{
				RefreshRows();
			}
		}
	}

	private void PlayNextInQueue(object sender, RoutedEventArgs args)
	{
		if (Selected is not Track track) return;
		int existing = queue.FindIndex(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, track.Path));
		if (existing >= 0) queue.RemoveAt(existing);
		queue.Insert(0, track);
		Status.Text = LocalTextExtension.Format("Siguiente en la cola: {0}", track.Name);
		if (Navigation.SelectedIndex == 4)
		{
			RefreshRows();
			Rows.SelectedItem = Rows.Items.OfType<LibraryRow>().FirstOrDefault(row => row.Tracks[0] == track);
		}
	}

	private void LoadAudioSettings()
	{
		try
		{
			if (!System.IO.File.Exists(AudioSettingsFile))
			{
				return;
			}
			AudioSettings audioSettings = JsonSerializer.Deserialize<AudioSettings>(System.IO.File.ReadAllText(AudioSettingsFile));
			if (audioSettings == null)
			{
				return;
			}
			Volume.Value = Math.Clamp(audioSettings.Volume, 0.0, 1.0);
			player.OutputId = audioSettings.OutputId;
			player.Exclusive = audioSettings.Exclusive;
			player.OutputSampleRate = (AudioSampleRatePolicy.IsValidRequest(audioSettings.OutputSampleRate) ? audioSettings.OutputSampleRate : 0);
			player.Balance = audioSettings.Balance;
			eqEnabled = audioSettings.EqEnabled;
			player.Direct = audioSettings.Direct;
			player.StrictDirect = audioSettings.StrictDirect;
			shuffle = audioSettings.Shuffle;
			rememberPosition = audioSettings.RememberPosition;
			continueAfterTrack = audioSettings.ContinueAfterTrack;
			lyricOffset = Math.Clamp(audioSettings.LyricOffset, -10, 10);
			lyricScale = Math.Clamp(audioSettings.LyricScale, .85, 1.35);
			ApplyAppearanceProfile(audioSettings.AppearanceProfile);
			compactRows = audioSettings.CompactRows;
			showTrackQuality = audioSettings.ShowTrackQuality;
			miniAlwaysOnTop = audioSettings.MiniAlwaysOnTop;
			followLyrics = audioSettings.FollowLyrics;
			albumSize = Math.Clamp(audioSettings.AlbumSize, 0, 2);
			autoArtwork = audioSettings.AutoArtwork;
			autoLyrics = audioSettings.AutoLyrics;
			keepAwake = audioSettings.KeepAwake;
			repeat = (Enum.IsDefined(audioSettings.Repeat) ? audioSettings.Repeat : RepeatMode.Off);
			MarkPlaybackToggle(ShuffleButton, shuffle);
			UpdateRepeatButton();
			UpdateVolumeControls();
			player.Speed = Math.Clamp(audioSettings.Speed, 0.75, 2.0);
			artworkColors = audioSettings.ArtworkColors;
			string text = audioSettings.AccentPreset;
			bool flag = ((text == "blue" || text == "violet") ? true : false);
			accentPreset = (flag ? audioSettings.AccentPreset : "artwork");
			animateSpectrum = audioSettings.AnimateSpectrum;
			switch (audioSettings.VisualizerStyle)
			{
			case "bars":
			case "mirror":
			case "centered":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			visualizerStyle = (flag ? audioSettings.VisualizerStyle : "mirror");
			visualizerSensitivity = (double.IsFinite(audioSettings.VisualizerSensitivity) ? Math.Clamp(audioSettings.VisualizerSensitivity, 0.5, 2.0) : 1.0);
			int num = audioSettings.VisualizerFramesPerSecond;
			flag = ((num == 24 || num == 30 || num == 60) ? true : false);
			visualizerFramesPerSecond = (flag ? audioSettings.VisualizerFramesPerSecond : 30);
			SpectrumView.Visibility = ((!animateSpectrum) ? Visibility.Collapsed : Visibility.Visible);
			if (!artworkColors)
			{
				SetArtworkAccent(AccentPresetColor());
			}
			if (audioSettings.Gains?.Length == gains.Length)
			{
				for (int i = 0; i < gains.Length; i++)
				{
					gains[i] = Math.Clamp(audioSettings.Gains[i], -12f, 12f);
				}
				ApplyEqualizer();
			}
		}
		catch (Exception ex)
		{
			Status.Text = LocalTextExtension.Translate("No se pudieron leer los ajustes de audio. ") + ex.Message;
		}
	}

	private bool SaveAudioSettings()
	{
		try
		{
			Directory.CreateDirectory(DataDirectory);
			System.IO.File.WriteAllText(AudioSettingsFile + ".tmp", JsonSerializer.Serialize(new AudioSettings(Volume.Value, player.OutputId, player.Exclusive, gains, player.Balance, eqEnabled, player.Direct, artworkColors, animateSpectrum, player.Speed, player.StrictDirect, shuffle, repeat, rememberPosition, accentPreset, visualizerStyle, visualizerSensitivity, visualizerFramesPerSecond, player.OutputSampleRate, continueAfterTrack, lyricOffset, lyricScale, appearanceProfile, compactRows, showTrackQuality, miniAlwaysOnTop, followLyrics, albumSize, autoArtwork, autoLyrics, keepAwake)));
			System.IO.File.Move(AudioSettingsFile + ".tmp", AudioSettingsFile, overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			Status.Text = LocalTextExtension.Translate("No se pudieron guardar los ajustes de audio. ") + ex.Message;
			return false;
		}
	}

	private void ApplyEqualizer()
	{
		player.SetGains(eqEnabled ? gains : FlatGains);
	}

	private void Settings(object sender, RoutedEventArgs args)
	{
		TextBlock settingsSaveStatus = new TextBlock
		{
			Text = LocalTextExtension.Translate("Tus ajustes están guardados."),
			Foreground = (Brush)FindResource("Muted"),
			FontSize = 10
		};
		DispatcherTimer settingsSaveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(350)
		};
		settingsSaveTimer.Tick += (_, _) =>
		{
			settingsSaveTimer.Stop();
			if (!smokeTest)
			{
				settingsSaveStatus.Text = SaveAudioSettings()
					? LocalTextExtension.Translate("Ajustes guardados.")
					: Status.Text;
			}
		};
		void SaveSettingsSoon()
		{
			if (smokeTest)
			{
				return;
			}
			settingsSaveTimer.Stop();
			settingsSaveTimer.Start();
			settingsSaveStatus.Text = LocalTextExtension.Translate("Guardando ajustes…");
		}

		StackPanel NewSettingsPage() => new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
		var soundPage = NewSettingsPage();
		var playbackPage = NewSettingsPage();
		var lyricsPage = NewSettingsPage();
		var appearancePage = NewSettingsPage();
		var languagePage = NewSettingsPage();
		var libraryPage = NewSettingsPage();
		var visualizerPage = NewSettingsPage();
		var panel = soundPage;
		var soundAnchor = new TextBlock
		{
			Text = LocalTextExtension.Translate("Ecualizador"),
			FontSize = 26.0,
			FontWeight = FontWeights.SemiBold
		};
		panel.Children.Add(SettingsSection(soundAnchor, "Ajusta ecualización, balance y salida de audio."));
		panel.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Diez bandas · −12 a +12 dB"),
			Margin = new Thickness(0.0, 4.0, 0.0, 14.0)
		});
		(string Name, float[] Values)[] presets = new (string, float[])[6]
		{
			("Plano", FlatGains),
			("Rock", new float[10] { 4f, 3f, 2f, 0f, -1f, -1f, 1f, 3f, 4f, 4f }),
			("Jazz", new float[10] { 2f, 2f, 1f, 1f, -1f, -1f, 0f, 2f, 3f, 3f }),
			("Clásica", new float[10] { 3f, 2f, 1f, 0f, -1f, 0f, 0f, 1f, 2f, 3f }),
			("Electrónica", new float[10] { 5f, 4f, 2f, 0f, -2f, 0f, 2f, 3f, 4f, 5f }),
			("Vocal", new float[10] { -2f, -2f, -1f, 0f, 2f, 3f, 3f, 2f, 0f, -1f })
		};
		System.Windows.Controls.CheckBox eqToggle = new System.Windows.Controls.CheckBox
		{
			Content = LocalTextExtension.Translate("Activar ecualizador"),
			IsChecked = eqEnabled,
			Foreground = Brushes.White,
			Margin = new Thickness(0.0, 0.0, 20.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Controls.ComboBox preset = new System.Windows.Controls.ComboBox
		{
			Width = 165.0,
			ItemsSource = presets.Select(((string Name, float[] Values) item) => LocalTextExtension.Translate(item.Name)).ToArray(),
			SelectedIndex = Array.FindIndex(presets, ((string Name, float[] Values) item) => Enumerable.SequenceEqual(item.Values, gains)),
			Style = (Style)FindResource("FilterCombo")
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
		stackPanel.Children.Add(eqToggle);
		stackPanel.Children.Add(preset);
		panel.Children.Add(stackPanel);
		Slider[] sliders = new Slider[gains.Length];
		StackPanel bands = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			IsEnabled = eqEnabled
		};
		bool applyingPreset = false;
		eqToggle.Click += (object _, RoutedEventArgs _) =>
		{
			eqEnabled = eqToggle.IsChecked == true;
			bands.IsEnabled = eqEnabled;
			ApplyEqualizer();
			SaveSettingsSoon();
		};
		preset.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
		{
			if (preset.SelectedIndex >= 0)
			{
				applyingPreset = true;
				float[] item = presets[preset.SelectedIndex].Values;
				for (int i = 0; i < item.Length; i++)
				{
					sliders[i].Value = item[i];
				}
				applyingPreset = false;
				ApplyEqualizer();
				SaveSettingsSoon();
			}
		};
		for (int num = 0; num < gains.Length; num++)
		{
			int band = num;
			int num2 = AudioEngine.Frequencies[band];
			StackPanel stackPanel2 = new StackPanel
			{
				Width = 52.0
			};
			Slider slider = new Slider
			{
				Orientation = System.Windows.Controls.Orientation.Vertical,
				Height = 135.0,
				Minimum = -12.0,
				Maximum = 12.0,
				Value = gains[band],
				TickFrequency = 1.0,
				IsSnapToTickEnabled = true,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			};
			sliders[num] = slider;
			TextBlock value = new TextBlock
			{
				Text = $"{gains[band]:0} dB",
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			};
			slider.ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> change) =>
			{
				gains[band] = (float)change.NewValue;
				if (!applyingPreset)
				{
					preset.SelectedIndex = -1;
					ApplyEqualizer();
					SaveSettingsSoon();
				}
				value.Text = $"{change.NewValue:0} dB";
			};
			stackPanel2.Children.Add(slider);
			stackPanel2.Children.Add(new TextBlock
			{
				Text = ((num2 >= 1000) ? $"{num2 / 1000}k" : num2.ToString()),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			});
			stackPanel2.Children.Add(value);
			bands.Children.Add(stackPanel2);
		}
		panel.Children.Add(new Border
		{
			Child = bands,
			Padding = new Thickness(8.0, 12.0, 8.0, 12.0),
			CornerRadius = new CornerRadius(10.0),
			Background = new SolidColorBrush(Color.FromRgb(33, 46, 66))
		});
		TextBlock element = new TextBlock
		{
			Text = LocalTextExtension.Translate("Balance estéreo"),
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 18.0, 0.0, 4.0)
		};
		Slider balanceSlider = new Slider
		{
			Minimum = -1.0,
			Maximum = 1.0,
			Value = player.Balance,
			TickFrequency = 0.05,
			IsSnapToTickEnabled = true
		};
		System.Windows.Controls.Button button = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Centrar"),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		balanceSlider.ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> change) =>
		{
			player.Balance = (float)change.NewValue;
			SaveSettingsSoon();
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			balanceSlider.Value = 0.0;
		};
		panel.Children.Add(element);
		panel.Children.Add(balanceSlider);
		panel.Children.Add(button);
		panel.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Salida de audio · WASAPI"),
			Margin = new Thickness(0.0, 20.0, 0.0, 5.0)
		});
		System.Windows.Controls.ComboBox choices = new System.Windows.Controls.ComboBox
		{
			Style = (Style)FindResource("FilterCombo"),
			ItemTemplate = (DataTemplate)FindResource("AudioDeviceLabel"),
			SelectedValuePath = "Id"
		};
		try
		{
			using MMDeviceEnumerator mMDeviceEnumerator = new MMDeviceEnumerator();
			var list = mMDeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select((MMDevice device) =>
			{
				using (device)
				{
					return new
					{
						Name = device.FriendlyName,
						Id = device.ID
					};
				}
			}).ToList();
			list.Insert(0, new
			{
				Name = LocalTextExtension.Translate("Predeterminada del sistema"),
				Id = (string)null
			});
			choices.ItemsSource = list;
			choices.SelectedIndex = Math.Max(0, list.FindIndex(item => item.Id == player.OutputId));
			choices.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
			{
				player.OutputId = choices.SelectedValue as string;
				ReopenOutput();
				SaveSettingsSoon();
			};
		}
		catch (Exception ex)
		{
			choices.Items.Add(ex.Message);
			choices.SelectedIndex = 0;
			choices.IsEnabled = false;
		}
		panel.Children.Add(choices);
		System.Windows.Controls.CheckBox exclusive = new System.Windows.Controls.CheckBox
		{
			Content = LocalTextExtension.Translate("Solicitar acceso exclusivo"),
			IsChecked = player.Exclusive,
			Foreground = Brushes.White,
			Margin = new Thickness(0.0, 12.0, 0.0, 4.0)
		};
		TextBlock mode = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = 520.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		exclusive.Click += (object _, RoutedEventArgs _) =>
		{
			player.Exclusive = exclusive.IsChecked == true;
			ReopenOutput();
			UpdateMode();
			SaveSettingsSoon();
		};
		choices.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
		{
			UpdateMode();
		};
		UpdateMode();
		panel.Children.Add(exclusive);
		panel.Children.Add(mode);
		panel = playbackPage;
		var playbackAnchor = new TextBlock
		{
			Text = LocalTextExtension.Translate("Reproducción"),
			FontSize = 21.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 20.0, 0.0, 4.0)
		};
		panel.Children.Add(SettingsSection(playbackAnchor, "Controla la velocidad y el procesamiento de audio."));
		double[] speeds = new double[5] { 0.75, 1.0, 1.25, 1.5, 2.0 };
		System.Windows.Controls.ComboBox speedPicker = new System.Windows.Controls.ComboBox
		{
			Style = (Style)FindResource("FilterCombo"),
			ItemsSource = speeds.Select((double speed) => $"{speed:0.##}×").ToArray(),
			SelectedIndex = Array.IndexOf(speeds, player.Speed),
			IsEnabled = (!player.Direct && !player.StrictDirect)
		};
		panel.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Velocidad de reproducción"),
			Margin = new Thickness(0.0, 14.0, 0.0, 4.0)
		});
		panel.Children.Add(speedPicker);
		speedPicker.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
		{
			if (speedPicker.SelectedIndex >= 0)
			{
				player.Speed = speeds[speedPicker.SelectedIndex];
				ReopenOutput();
				SaveSettingsSoon();
			}
		};
		AddPreference(LocalTextExtension.Translate("PCM directo exclusivo (WAV, AIFF y FLAC)"), player.StrictDirect, (bool enabled) =>
		{
			player.StrictDirect = enabled;
			UpdateVolumeControls();
			speedPicker.IsEnabled = !enabled && !player.Direct;
			ReopenOutput();
			UpdateMode();
			SaveSettingsSoon();
		});
		AddPreference("Omitir ecualizador, balance y velocidad", player.Direct, (bool enabled) =>
		{
			player.Direct = enabled;
			speedPicker.IsEnabled = !enabled && !player.StrictDirect;
			ReopenOutput();
			SaveSettingsSoon();
		});
		AddPreference("Continuar con la siguiente canción", continueAfterTrack, enabled => continueAfterTrack = enabled);
		AddPreference("Evitar el reposo al reproducir", keepAwake, enabled =>
		{
			keepAwake = enabled;
			UpdateAwakeRequest(playing && enabled);
		});
		AddPreference("Mantener el minirreproductor encima", miniAlwaysOnTop, enabled =>
		{
			miniAlwaysOnTop = enabled;
			if (mini != null) mini.Topmost = enabled;
		});
		AddPreference("Recordar posición de la pista", rememberPosition, enabled => rememberPosition = enabled);
		var sleep = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Temporizador de sueño"),
			Margin = new Thickness(0, 14, 0, 0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		sleep.Click += ShowSleepTimer;
		panel.Children.Add(sleep);
		panel = lyricsPage;
		var lyricsAnchor = new TextBlock { Text = LocalTextExtension.Translate("Letras") };
		panel.Children.Add(SettingsSection(lyricsAnchor, "Ajusta el tamaño y la sincronización de las letras."));
		panel.Children.Add(CreateLyricSetting("Tamaño de las letras", .85, 1.35, .05, lyricScale, "P0",
			value => { lyricScale = value; RefreshLyrics(); SaveSettingsSoon(); }));
		panel.Children.Add(CreateLyricSetting("Ajuste de sincronización", -10, 10, .1, lyricOffset, "+0.0 s;-0.0 s;0.0 s",
			value => { lyricOffset = value; RefreshLyrics(); SaveSettingsSoon(); }));
		AddPreference("Seguir el verso activo", followLyrics, enabled => { followLyrics = enabled; RefreshLyrics(); });
		panel = appearancePage;
		var appearanceAnchor = new TextBlock
		{
			Text = LocalTextExtension.Translate("Apariencia"),
			FontSize = 21.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 20.0, 0.0, 4.0)
		};
		panel.Children.Add(SettingsSection(appearanceAnchor, "Personaliza el aspecto del reproductor."));
		panel.Children.Add(CreateAppearanceProfiles(appearanceProfile, profile =>
		{
			ApplyAppearanceProfile(profile);
			SaveSettingsSoon();
		}));
		AddPreference("Filas compactas en la biblioteca", compactRows, enabled => { compactRows = enabled; ApplyLibraryPresentation(); });
		AddPreference("Mostrar formato en la biblioteca y la barra", showTrackQuality, enabled => { showTrackQuality = enabled; ApplyLibraryPresentation(); });
		AddOption(appearancePage, "Tamaño de las portadas", new[] { "Pequeño", "Mediano", "Grande" }, albumSize,
			index => { albumSize = index; ApplyAlbumSize(); });
		panel.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Color de la aplicación"),
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		});
		System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox();
		comboBox.Style = (Style)FindResource("FilterCombo");
		comboBox.ItemsSource = new string[3]
		{
			LocalTextExtension.Translate("Según la carátula"),
			LocalTextExtension.Translate("Azul frío"),
			LocalTextExtension.Translate("Violeta")
		};
		System.Windows.Controls.ComboBox comboBox2 = comboBox;
		string text = accentPreset;
		int selectedIndex = ((text == "blue") ? 1 : ((text == "violet") ? 2 : 0));
		comboBox2.SelectedIndex = selectedIndex;
		System.Windows.Controls.ComboBox accentChoices = comboBox;
		accentChoices.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
		{
			if (accentChoices.SelectedIndex >= 0)
			{
				ApplyAccentPreset((new string[3] { "artwork", "blue", "violet" })[accentChoices.SelectedIndex]);
				SaveSettingsSoon();
			}
		};
		panel.Children.Add(accentChoices);
		AddPreference("Espectro animado", animateSpectrum, (bool enabled) =>
		{
			animateSpectrum = enabled;
			SpectrumView.Visibility = ((!enabled) ? Visibility.Collapsed : Visibility.Visible);
		});
		panel = languagePage;
		var languageAnchor = new TextBlock
		{
			Text = LocalTextExtension.Translate("Idioma de interfaz"),
			Margin = new Thickness(0.0, 18.0, 0.0, 5.0)
		};
		panel.Children.Add(SettingsSection(languageAnchor, "El idioma se aplica al reiniciar Prisma Music."));
		string[] codes = new string[3] { "es", "en", "pt" };
		System.Windows.Controls.ComboBox language = new System.Windows.Controls.ComboBox
		{
			Style = (Style)FindResource("FilterCombo"),
			ItemsSource = new string[3] { "Español", "English", "Português" },
			SelectedIndex = Array.IndexOf(codes, LocalTextExtension.Language)
		};
		TextBlock languageNotice = new TextBlock
		{
			Margin = new Thickness(0.0, 5.0, 0.0, 0.0)
		};
		language.SelectionChanged += (object _, SelectionChangedEventArgs _) =>
		{
			if (language.SelectedIndex >= 0)
			{
				languageNotice.Text = ((codes[language.SelectedIndex] == LocalTextExtension.Language) ? LocalTextExtension.Translate("Este idioma ya está aplicado.") : LocalTextExtension.Translate("Guarda el idioma y reinicia Prisma Music para cambiar la interfaz."));
			}
		};
		panel.Children.Add(language);
		System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Aplicar idioma y reiniciar"),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		button2.Click += (object _, RoutedEventArgs _) =>
		{
			if (language.SelectedIndex >= 0)
			{
				if (!(codes[language.SelectedIndex] == LocalTextExtension.Language))
				{
					try
					{
						LocalTextExtension.SaveLanguage(codes[language.SelectedIndex]);
						RestartForLanguage();
						return;
					}
					catch (Exception ex2)
					{
						languageNotice.Text = LocalTextExtension.Translate("No se pudo guardar el idioma. ") + ex2.Message;
						return;
					}
				}
				languageNotice.Text = LocalTextExtension.Translate("Este idioma ya está aplicado.");
			}
		};
		panel.Children.Add(button2);
		panel.Children.Add(languageNotice);
		System.Windows.Controls.Button button3 = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("Primeros pasos"),
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		panel.Children.Add(button3);
		libraryPage.Children.Add(SettingsSection(new TextBlock { Text = LocalTextExtension.Translate("Biblioteca") },
			"Exporta tu colección o las pistas de la vista actual."));
		var exportLibrary = new System.Windows.Controls.Button { Content = LocalTextExtension.Translate("Exportar biblioteca") };
		exportLibrary.Click += ExportLibrary;
		var exportPlaylist = new System.Windows.Controls.Button { Content = LocalTextExtension.Translate("Exportar lista") };
		exportPlaylist.Click += ExportCurrentPlaylist;
		AddLibraryAction("Biblioteca JSON", "Incluye las pistas, fuentes, listas y etiquetas editadas.", exportLibrary);
		AddLibraryAction("Lista M3U8", "Incluye las pistas de la vista actual en el orden mostrado.", exportPlaylist);
		panel = libraryPage;
		AddPreference("Buscar carátulas automáticamente", autoArtwork, enabled => autoArtwork = enabled);
		AddPreference("Buscar letras automáticamente", autoLyrics, enabled => autoLyrics = enabled);
		libraryPage.Children.Add(new TextBlock { Text = T("La búsqueda automática usa el título y el artista para consultar los catálogos en línea."),
			Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(9, 4, 0, 0) });
		visualizerPage.Children.Add(SettingsSection(new TextBlock { Text = T("Visualizador") }, "Ajusta el movimiento del visualizador."));
		string[] styles = { "bars", "mirror", "centered" };
		AddOption(visualizerPage, "Estilo del visualizador", new[] { "Barras", "Espejo", "Centrado" },
			Array.IndexOf(styles, visualizerStyle), index => { visualizerStyle = styles[index]; });
		int[] rates = { 24, 30, 60 };
		AddOption(visualizerPage, "Frecuencia de actualización", new[] { "24 FPS", "30 FPS", "60 FPS" },
			Array.IndexOf(rates, visualizerFramesPerSecond), index =>
			{
				visualizerFramesPerSecond = rates[index];
				if (visualizerTimer != null) visualizerTimer.Interval = TimeSpan.FromMilliseconds(1000d / rates[index]);
			});
		visualizerPage.Children.Add(CreateLyricSetting("Sensibilidad", .5, 2, .25, visualizerSensitivity, "0.00×",
			value => { visualizerSensitivity = value; SaveSettingsSoon(); }));
		panel = visualizerPage;
		AddPreference("Espectro animado", animateSpectrum, enabled =>
		{
			animateSpectrum = enabled;
			SpectrumView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
		});

		void AddOption(StackPanel page, string label, string[] values, int index, Action<int> change)
		{
			page.Children.Add(new TextBlock { Text = T(label), Margin = new Thickness(0, 10, 0, 4) });
			var selector = new System.Windows.Controls.ComboBox { Style = (Style)FindResource("FilterCombo"),
				ItemsSource = values.Select(T).ToArray(), SelectedIndex = index };
			AutomationProperties.SetName(selector, T(label));
			selector.SelectionChanged += (_, _) =>
			{
				if (selector.SelectedIndex < 0) return;
				change(selector.SelectedIndex);
				SaveSettingsSoon();
			};
			page.Children.Add(selector);
		}

		void AddLibraryAction(string heading, string description, System.Windows.Controls.Button button)
		{
			var row = new Grid();
			row.ColumnDefinitions.Add(new ColumnDefinition());
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
			text.Children.Add(new TextBlock { Text = LocalTextExtension.Translate(heading), FontWeight = FontWeights.SemiBold });
			text.Children.Add(new TextBlock { Text = LocalTextExtension.Translate(description), TextWrapping = TextWrapping.Wrap,
				Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
			row.Children.Add(text);
			Grid.SetColumn(button, 1);
			button.VerticalAlignment = VerticalAlignment.Center;
			row.Children.Add(button);
			libraryPage.Children.Add(new Border { Background = (Brush)FindResource("SettingsBackground"),
				BorderBrush = (Brush)FindResource("SettingsStroke"), BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8), Child = row });
		}
		var settingsScroll = new ScrollViewer
		{
			Style = (Style)FindResource("LyricsScroll"),
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		var settingsCard = new Border { Background = (Brush)FindResource("SettingsCard"),
			BorderBrush = (Brush)FindResource("SettingsStroke"), BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(12), Child = settingsScroll };
		var settingsBackground = (Brush)FindResource("SettingsBackground");
		var settingsRoot = new Grid { Background = settingsBackground };
		settingsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		settingsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		settingsRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
		settingsRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		var settingsHeader = new Grid { Margin = new Thickness(24, 20, 24, 16) };
		settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var title = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		title.Children.Add(new Border
		{
			Width = 42,
			Height = 42,
			CornerRadius = new CornerRadius(14),
			Background = new SolidColorBrush(Color.FromArgb(0x28, 255, 255, 255)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)),
			BorderThickness = new Thickness(1),
			Child = new TextBlock
			{
				Text = "\uE713",
				FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
				FontSize = 18,
				Foreground = (Brush)FindResource("AccentBrush"),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		});
		var titleText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
		titleText.Children.Add(new TextBlock
		{
			Text = LocalTextExtension.Translate("Configuración"),
			FontSize = 24,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 2, 0, 0)
		});
		settingsSaveStatus.Margin = new Thickness(0, 2, 0, 0);
		titleText.Children.Add(settingsSaveStatus);
		title.Children.Add(titleText);
		settingsHeader.Children.Add(title);
		Window settingsWindow = null;
		var backToPlayer = new System.Windows.Controls.Button
		{
			Content = LocalTextExtension.Translate("‹ Volver al reproductor"),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(12, 8, 12, 8),
			ToolTip = LocalTextExtension.Translate("Volver al reproductor")
		};
		AutomationProperties.SetName(backToPlayer, LocalTextExtension.Translate("Volver al reproductor"));
		backToPlayer.Click += (_, _) => settingsWindow.Close();
		WindowChrome.SetIsHitTestVisibleInChrome(backToPlayer, true);
		Grid.SetColumn(backToPlayer, 1);
		settingsHeader.Children.Add(backToPlayer);
		var closeSettings = new System.Windows.Controls.Button
		{
			Content = "\uE8BB",
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			Width = 38,
			Height = 38,
			Padding = new Thickness(0),
			ToolTip = LocalTextExtension.Translate("Cerrar"),
			VerticalAlignment = VerticalAlignment.Top
		};
		closeSettings.Click += (_, _) => settingsWindow.Close();
		AutomationProperties.SetName(closeSettings, LocalTextExtension.Translate("Cerrar"));
		WindowChrome.SetIsHitTestVisibleInChrome(closeSettings, true);
		Grid.SetColumn(closeSettings, 2);
		settingsHeader.Children.Add(closeSettings);
		Grid.SetColumnSpan(settingsHeader, 2);
		settingsRoot.Children.Add(settingsHeader);

		var navigation = new System.Windows.Controls.ListBox { Margin = new Thickness(18, 0, 14, 16),
			Background = Brushes.Transparent, BorderThickness = new Thickness(0),
			Foreground = (Brush)FindResource("Ink") };
		AutomationProperties.SetName(navigation, LocalTextExtension.Translate("Configuración"));
		void AddSettingsLink(string label, string glyph, StackPanel page)
		{
			var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
			content.Children.Add(new TextBlock
			{
				Text = glyph,
				FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
				FontSize = 15,
				Width = 27,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = (Brush)FindResource("AccentBrush")
			});
			content.Children.Add(new TextBlock { Text = LocalTextExtension.Translate(label), VerticalAlignment = VerticalAlignment.Center });
			var link = new ListBoxItem { Content = content, Tag = page };
			AutomationProperties.SetName(link, LocalTextExtension.Translate(label));
			navigation.Items.Add(link);
		}
		AddSettingsLink("Sonido", "\uE767", soundPage);
		AddSettingsLink("Reproducción", "\uE768", playbackPage);
		AddSettingsLink("Letras", "\uE8BD", lyricsPage);
		AddSettingsLink("Apariencia", "\uE790", appearancePage);
		AddSettingsLink("Idioma", "\uE774", languagePage);
		AddSettingsLink("Biblioteca", "\uE8B7", libraryPage);
		AddSettingsLink("Visualizador", "\uE9E9", visualizerPage);
		navigation.SelectionChanged += (_, _) =>
		{
			settingsScroll.Content = (navigation.SelectedItem as ListBoxItem)?.Tag;
			settingsScroll.ScrollToTop();
		};
		navigation.SelectedIndex = 0;
		Grid.SetRow(navigation, 1);
		settingsRoot.Children.Add(navigation);
		Grid.SetColumn(settingsCard, 1);
		Grid.SetRow(settingsCard, 1);
		settingsCard.Margin = new Thickness(0, 0, 18, 18);
		settingsRoot.Children.Add(settingsCard);
		settingsWindow = new Window
		{
			Owner = this,
			Title = LocalTextExtension.Translate("Configuración · Prisma Music"),
			Width = 980,
			Height = 780,
			MinWidth = 760,
			MinHeight = 580,
			ResizeMode = ResizeMode.CanResize,
			WindowStyle = WindowStyle.None,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Content = settingsRoot,
			Background = settingsBackground
		};
		settingsWindow.Closed += (_, _) =>
		{
			settingsSaveTimer.Stop();
			if (!smokeTest)
			{
				settingsSaveStatus.Text = SaveAudioSettings()
					? LocalTextExtension.Translate("Ajustes guardados.")
					: Status.Text;
			}
		};
		settingsWindow.PreviewKeyDown += (_, key) =>
		{
			if (key.Key == Key.Escape)
			{
				key.Handled = true;
				settingsWindow.Close();
			}
		};
		WindowChrome.SetWindowChrome(settingsWindow, new WindowChrome
		{
			CaptionHeight = 48,
			ResizeBorderThickness = new Thickness(6),
			GlassFrameThickness = new Thickness(0),
			UseAeroCaptionButtons = false
		});
		if (smokeTest)
		{
			string preview = Environment.GetEnvironmentVariable("PRISMA_SETTINGS_PREVIEW");
			if (preview != null)
			{
				settingsWindow.ContentRendered += async (object? _, EventArgs _) =>
				{
					FrameworkElement frameworkElement = (FrameworkElement)settingsWindow.Content;
					RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap((int)frameworkElement.ActualWidth, (int)frameworkElement.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
					renderTargetBitmap.Render(frameworkElement);
					PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder
					{
						Frames = { BitmapFrame.Create(renderTargetBitmap) }
					};
					using FileStream stream = System.IO.File.Create(preview);
					pngBitmapEncoder.Save(stream);
					if (settingsScroll != null)
					{
						navigation.SelectedIndex = 5;
						if (!ReferenceEquals(settingsScroll.Content, libraryPage))
							throw new InvalidOperationException("Settings navigation did not switch to Library.");
						await Task.Delay(250);
						settingsWindow.UpdateLayout();
						RenderTargetBitmap renderTargetBitmap2 = new RenderTargetBitmap((int)frameworkElement.ActualWidth, (int)frameworkElement.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
						renderTargetBitmap2.Render(frameworkElement);
						PngBitmapEncoder pngBitmapEncoder2 = new PngBitmapEncoder
						{
							Frames = { BitmapFrame.Create(renderTargetBitmap2) }
						};
						using FileStream stream2 = System.IO.File.Create(Path.Combine(Path.GetDirectoryName(preview), Path.GetFileNameWithoutExtension(preview) + "-biblioteca.png"));
						pngBitmapEncoder2.Save(stream2);
					}
					settingsWindow.Close();
				};
			}
		}
		settingsWindow.SourceInitialized += (object? _, EventArgs _) =>
		{
			int value2 = 1;
			DwmSetWindowAttribute(new WindowInteropHelper(settingsWindow).Handle, 20, ref value2, 4);
		};
		button3.Click += (object _, RoutedEventArgs _) =>
		{
			settingsWindow.Close();
			ShowWelcome(this, new RoutedEventArgs());
		};
		settingsWindow.ShowDialog();
		settingsSaveTimer.Stop();
		if (!smokeTest)
		{
			settingsSaveStatus.Text = SaveAudioSettings()
				? LocalTextExtension.Translate("Ajustes guardados.")
				: Status.Text;
		}
		void AddPreference(string label, bool value2, Action<bool> change)
		{
			System.Windows.Controls.CheckBox control = new System.Windows.Controls.CheckBox
			{
				Content = LocalTextExtension.Translate(label),
				Style = (Style)FindResource("SettingsToggle"),
				IsChecked = value2,
				Foreground = Brushes.White,
				Margin = new Thickness(0, 4, 0, 1)
			};
			control.Click += (object _, RoutedEventArgs _) =>
			{
				change(control.IsChecked == true);
				SaveSettingsSoon();
			};
			panel.Children.Add(control);
		}
		Border SettingsSection(TextBlock heading, string description)
		{
			heading.FontSize = 27;
			heading.FontWeight = FontWeights.SemiBold;
			heading.Margin = new Thickness(0);
			StackPanel content = new StackPanel();
			content.Children.Add(heading);
			content.Children.Add(new TextBlock
			{
				Text = LocalTextExtension.Translate(description),
				Foreground = (Brush)FindResource("Muted"),
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 3, 0, 0)
			});
			return new Border
			{
				Margin = new Thickness(0, 0, 0, 14),
				Padding = new Thickness(0, 2, 0, 10),
				BorderBrush = (Brush)FindResource("SettingsStroke"),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Child = content
			};
		}
		void UpdateMode()
		{
			mode.Text = player.OutputNotice ?? ((!opened) ? LocalTextExtension.Translate("El modo exclusivo se solicita al abrir una pista.") : (player.IsExclusive ? LocalTextExtension.Translate("Salida activa en modo exclusivo.") : LocalTextExtension.Translate("Salida activa en modo compartido.")));
		}
	}

	private void Mini(object sender, RoutedEventArgs args)
	{
		if (mini != null)
		{
			mini.Activate();
			return;
		}
		var grid = new Grid();
		var fallbackArt = new ArtworkTile();
		fallbackArt.SetBinding(ArtworkTile.CaptionProperty, new System.Windows.Data.Binding(nameof(Tag)) { Source = Cover });
		grid.Children.Add(fallbackArt);
		miniCover = new Image
		{
			Stretch = Stretch.UniformToFill,
			ClipToBounds = true
		};
		grid.Children.Add(miniCover);
		var coverFrame = new Border
		{
			Width = 100,
			Height = 100,
			CornerRadius = new CornerRadius(14),
			Background = new SolidColorBrush(Color.FromRgb(33, 35, 45)),
			Child = grid,
			ClipToBounds = true,
			Margin = new Thickness(0, 0, 14, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		miniTitle = new TextBlock
		{
			FontWeight = FontWeights.SemiBold,
			FontSize = 15,
			TextTrimming = TextTrimming.CharacterEllipsis,
			ToolTip = current?.Name
		};
		miniArtist = new TextBlock
		{
			Foreground = (Brush)FindResource("Muted"),
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		miniFormat = new TextBlock
		{
			Foreground = (Brush)FindResource("AccentBrush"),
			FontSize = 10,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		miniProgress = new Slider
		{
			Style = (Style)FindResource("TransportSlider"),
			Minimum = 0,
			Margin = new Thickness(0, 4, 0, 0),
			IsMoveToPointEnabled = true
		};
		AutomationProperties.SetName(miniProgress, LocalTextExtension.Translate("Posición"));
		miniProgress.SetBinding(Slider.MaximumProperty,
			new System.Windows.Data.Binding(nameof(Slider.Maximum)) { Source = Seek, Mode = BindingMode.OneWay });
		miniProgress.SetBinding(Slider.ValueProperty,
			new System.Windows.Data.Binding(nameof(Slider.Value)) { Source = Seek, Mode = BindingMode.TwoWay });
		var controls = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0, 5, 0, 0)
		};
		var previousButton = new System.Windows.Controls.Button
		{
			Content = "\uE892",
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			Style = (Style)FindResource("CompactButton"),
			ToolTip = LocalTextExtension.Translate("Anterior")
		};
		miniToggle = new System.Windows.Controls.Button
		{
			Content = PlayButton.Content,
			Style = (Style)FindResource("PrimaryTransport"),
			Width = 38,
			Height = 36,
			Padding = new Thickness(0),
			ToolTip = LocalTextExtension.Translate("Reproducir o pausar")
		};
		AutomationProperties.SetName(miniToggle, LocalTextExtension.Translate("Reproducir o pausar"));
		var nextButton = new System.Windows.Controls.Button
		{
			Content = "\uE893",
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			Style = (Style)FindResource("CompactButton"),
			ToolTip = LocalTextExtension.Translate("Siguiente")
		};
		var returnButton = new System.Windows.Controls.Button
		{
			Content = "\uE8A7",
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			Style = (Style)FindResource("CompactButton"),
			ToolTip = LocalTextExtension.Translate("Volver a Prisma Music"),
		};
		AutomationProperties.SetName(returnButton, LocalTextExtension.Translate("Volver a Prisma Music"));
		var closeMini = new System.Windows.Controls.Button
		{
			Content = "\uE711",
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			Style = (Style)FindResource("CompactButton"),
			ToolTip = LocalTextExtension.Translate("Cerrar")
		};
		AutomationProperties.SetName(closeMini, LocalTextExtension.Translate("Cerrar"));
		closeMini.Click += (_, _) => mini?.Close();
		AutomationProperties.SetName(previousButton, LocalTextExtension.Translate("Anterior"));
		AutomationProperties.SetName(nextButton, LocalTextExtension.Translate("Siguiente"));
		returnButton.Click += (_, _) =>
		{
			if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
			Activate();
			mini?.Close();
		};
		previousButton.Click += Previous;
		miniToggle.Click += Toggle;
		nextButton.Click += Next;
		controls.Children.Add(previousButton);
		controls.Children.Add(miniToggle);
		controls.Children.Add(nextButton);
		controls.Children.Add(returnButton);
		controls.Children.Add(closeMini);
		var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		details.Children.Add(miniTitle);
		details.Children.Add(miniArtist);
		details.Children.Add(miniFormat);
		details.Children.Add(miniProgress);
		var miniTime = new TextBlock { FontSize = 10, Foreground = (Brush)FindResource("Muted") };
		miniTime.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = TimeLabel });
		details.Children.Add(miniTime);
		details.Children.Add(controls);
		var card = new Border
		{
			Padding = new Thickness(16),
			CornerRadius = new CornerRadius(20),
			BorderThickness = new Thickness(1),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)),
			Background = new SolidColorBrush(Color.FromArgb(0xF2, 24, 26, 35)),
			Effect = new System.Windows.Media.Effects.DropShadowEffect
			{
				Color = Colors.Black,
				BlurRadius = 28,
				ShadowDepth = 8,
				Opacity = .42
			}
		};
		var content = new DockPanel();
		DockPanel.SetDock(coverFrame, Dock.Left);
		content.Children.Add(coverFrame);
		content.Children.Add(details);
		card.Child = content;
		mini = new Window
		{
			Title = "Prisma Music · PiP",
			Width = 400,
			Height = 184,
			ResizeMode = ResizeMode.NoResize,
			Topmost = miniAlwaysOnTop,
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Background = Brushes.Transparent,
			Content = card
		};
		System.Windows.Rect workArea = SystemParameters.WorkArea;
		mini.Left = Math.Max(workArea.Left, workArea.Right - mini.Width - 24);
		mini.Top = Math.Max(workArea.Top, workArea.Bottom - mini.Height - 24);
		mini.Closed += (object? _, EventArgs _) =>
		{
			mini = null;
			miniTitle = null;
			miniArtist = null;
			miniFormat = null;
			miniCover = null;
			miniToggle = null;
			miniProgress = null;
		};
		UpdateMini();
		card.MouseLeftButtonDown += (_, e) =>
		{
			if (e.LeftButton == MouseButtonState.Pressed) mini?.DragMove();
		};
		mini.PreviewKeyDown += (_, e) =>
		{
			if (e.Key != Key.Escape) return;
			e.Handled = true;
			mini?.Close();
		};
		mini.Show();
	}

	private void UpdateMini()
	{
		if (miniTitle != null)
		{
			miniTitle.Text = current?.Name ?? "Prisma Music";
			miniTitle.ToolTip = current?.Name;
			miniArtist.Text = current?.Artist ?? "";
			miniFormat.Text = AudioFormat.Text;
			miniCover.Source = Cover.Source;
		}
	}

	private static void CaptureDesignPreview(FrameworkElement view, string environmentVariable, FrameworkElement crop = null)
	{
		if (Environment.GetEnvironmentVariable(environmentVariable) is not string path) return;
		view.UpdateLayout();
		var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight),
			96, 96, PixelFormats.Pbgra32);
		bitmap.Render(view);
		var encoder = new PngBitmapEncoder();
		BitmapSource frame = bitmap;
		if (crop != null)
		{
			var bounds = crop.TransformToAncestor(view).TransformBounds(new System.Windows.Rect(crop.RenderSize));
			frame = new CroppedBitmap(bitmap, new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
		}
		encoder.Frames.Add(BitmapFrame.Create(frame));
		using var stream = System.IO.File.Create(path);
		encoder.Save(stream);
	}

	private void Shortcuts(object sender, System.Windows.Input.KeyEventArgs args)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Invalid comparison between Unknown and I4
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Invalid comparison between Unknown and I4
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Invalid comparison between Unknown and I4
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Invalid comparison between Unknown and I4
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Invalid comparison between Unknown and I4
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Invalid comparison between Unknown and I4
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Invalid comparison between Unknown and I4
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Invalid comparison between Unknown and I4
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Invalid comparison between Unknown and I4
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Invalid comparison between Unknown and I4
		if ((int)Keyboard.Modifiers == 2 && (int)args.Key == 49)
		{
			Search.Focus();
			Search.SelectAll();
			args.Handled = true;
		}
		else if ((int)Keyboard.Modifiers == 2 && (int)args.Key == 52)
		{
			Inspect(sender, args);
		}
		else if ((int)args.Key == 100 || ((int)args.Key == 13 && listeningRoom != null))
		{
			Fullscreen(sender, args);
		}
		else if ((int)args.Key == 13 && pendingTrack != null)
		{
			CancelPendingOpen();
			args.Handled = true;
		}
		else if ((int)args.Key == 13 && Search.IsKeyboardFocusWithin)
		{
			Rows.Focus();
			args.Handled = true;
		}
		else if ((int)args.Key == 13 && collection != null)
		{
			CloseCollection(sender, args);
			Rows.Focus();
			args.Handled = true;
		}
		else if ((int)args.Key == 6 && Rows.IsKeyboardFocusWithin)
		{
			ActivateSelection();
		}
	}
}
