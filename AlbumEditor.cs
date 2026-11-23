using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool editingAlbum;
    private static string AlbumArtworkDirectory => Path.Combine(DataDirectory, "album-artwork");

    private sealed class AlbumTrackEdit(Track track)
    {
        internal Track Track { get; } = track;
        public string Title { get; set; } = track.Name;
        public string Artist { get; set; } = track.Artist;
        public string Disc { get; set; } = NumberText(track.Disc);
        public string Number { get; set; } = NumberText(track.Number);
    }

    private static string NumberText(uint value) => value == 0 ? "" : value.ToString(CultureInfo.InvariantCulture);

    private static bool ReadTagNumber(string value, out uint number)
    {
        number = 0;
        return string.IsNullOrWhiteSpace(value) ||
            uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number is > 0 and <= 9999;
    }

    private static byte[] ReadCustomArtwork(string key)
    {
        if (key == null || key.Length != 64 || !key.All(Uri.IsHexDigit)) return null;
        try { return File.ReadAllBytes(Path.Combine(AlbumArtworkDirectory, key + ".img")); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void EditAlbum(object sender, RoutedEventArgs args)
    {
        if (!albumPage || collection is not { } page || relocatingSource || editingAlbum) return;
        if (scanning)
        {
            Status.Text = T("Espera a que termine el escaneo para editar etiquetas.");
            return;
        }

        var songs = page.Tracks.Select(track => new AlbumTrackEdit(track)).ToArray();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = T("Editar álbum"), FontSize = 26,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        var name = Field("Álbum", page.Name);
        var initialOwner = Common(track => track.AlbumArtist);
        var initialGenre = Common(track => track.Genre);
        var initialYear = Common(track => NumberText(track.Year));
        var owner = Field("Artista del álbum", initialOwner);
        var genre = Field("Género", initialGenre);
        var year = Field("Año", initialYear);
        panel.Children.Add(SettingsHint("Los campos con varios valores se conservan si los dejas vacíos."));

        byte[] artwork = null;
        var selectArtwork = ButtonFor("Elegir carátula");
        var artworkName = SettingsHint("");
        var artworkActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 16) };
        artworkActions.Children.Add(selectArtwork);
        artworkActions.Children.Add(artworkName);
        panel.Children.Add(artworkActions);
        panel.Children.Add(new TextBlock { Text = T("Canciones del álbum"), FontSize = 16, FontWeight = FontWeights.SemiBold });

        var editor = new DataGrid
        {
            AutoGenerateColumns = false, ItemsSource = songs, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.Cell,
            Margin = new Thickness(0, 8, 0, 8), MaxHeight = 340, RowHeaderWidth = 0, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        editor.SetResourceReference(BackgroundProperty, "SettingsCard");
        editor.SetResourceReference(ForegroundProperty, "Ink");
        editor.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        editor.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "WorkspaceStroke");
        editor.SetResourceReference(DataGrid.RowBackgroundProperty, "SettingsCard");
        editor.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "SettingsBackground");
        editor.AlternationCount = 2;
        var cells = new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("Ink")));
        cells.Setters.Add(new Setter(DataGridCell.MinHeightProperty, 29.0));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
        selectedCell.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("OnAccentBrush")));
        cells.Triggers.Add(selectedCell);
        editor.CellStyle = cells;
        var headers = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headers.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("SettingsBackground")));
        headers.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Ink")));
        headers.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 5, 6, 5)));
        editor.ColumnHeaderStyle = headers;
        Column("Título", nameof(AlbumTrackEdit.Title), new DataGridLength(3, DataGridLengthUnitType.Star));
        Column("Artista", nameof(AlbumTrackEdit.Artist), new DataGridLength(2, DataGridLengthUnitType.Star));
        Column("Disco", nameof(AlbumTrackEdit.Disc), new DataGridLength(54));
        Column("Pista", nameof(AlbumTrackEdit.Number), new DataGridLength(54));
        AutomationProperties.SetName(editor, T("Canciones del álbum"));
        panel.Children.Add(editor);

        var files = new CheckBox { Content = T("Guardar también en los archivos FLAC"),
            Style = (Style)FindResource("SettingsToggle"),
            IsEnabled = songs.Any(song => CanWriteFlac(song.Track)), Margin = new Thickness(0, 6, 0, 8) };
        panel.Children.Add(files);
        var feedback = SettingsHint("");
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        panel.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ButtonFor("Cancelar");
        cancel.IsCancel = true;
        var save = ButtonFor("Guardar");
        save.IsDefault = true;
        save.SetResourceReference(BackgroundProperty, "AccentBrush");
        save.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        panel.Children.Add(actions);

        var surface = new Grid();
        surface.SetResourceReference(BackgroundProperty, "SettingsBackground");
        surface.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var dialog = new Window { Owner = this, Title = T("Editar álbum"), Width = 710,
            MaxWidth = SystemParameters.WorkArea.Width, MaxHeight = SystemParameters.WorkArea.Height,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = surface };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);

        selectArtwork.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "JPEG / PNG|*.jpg;*.jpeg;*.png", CheckFileExists = true };
            if (picker.ShowDialog(dialog) != true) return;
            try
            {
                using var stream = File.OpenRead(picker.FileName);
                if (stream.Length is <= 0 or > 12 * 1024 * 1024) throw new IOException(T("Elige una imagen de hasta 12 MB."));
                if (DecodeArtwork(stream, 280) == null) throw new IOException(T("No se pudo abrir la carátula."));
                stream.Position = 0;
                var bytes = new byte[(int)stream.Length];
                stream.ReadExactly(bytes);
                artwork = bytes;
                artworkName.Text = Path.GetFileName(picker.FileName);
                feedback.Text = "";
            }
            catch (Exception error) { feedback.Text = error.Message; }
        };

        bool writing = false;
        dialog.Closing += (_, args) => args.Cancel = writing;
        save.Click += async (_, _) =>
        {
            editor.CommitEdit(DataGridEditingUnit.Cell, true);
            editor.CommitEdit(DataGridEditingUnit.Row, true);
            if (string.IsNullOrWhiteSpace(name.Text) || songs.Any(song => string.IsNullOrWhiteSpace(song.Title)))
            {
                feedback.Text = T("Escribe un nombre para el álbum y un título para cada canción.");
                return;
            }
            if (!ReadTagNumber(year.Text, out uint albumYear) ||
                songs.Any(song => !ReadTagNumber(song.Disc, out _) || !ReadTagNumber(song.Number, out _)))
            {
                feedback.Text = T("Escribe un número entre 1 y 9999 o deja el campo vacío.");
                return;
            }

            var changes = new List<(Track Track, LibraryTagEdit Edit)>();
            try
            {
                string artworkKey = artwork == null ? null : StoreAlbumArtwork(artwork);
                foreach (var song in songs)
                {
                    var track = song.Track;
                    var existing = library.TagEdits.GetValueOrDefault(track.Path) ?? new LibraryTagEdit();
                    ReadTagNumber(song.Disc, out uint disc);
                    ReadTagNumber(song.Number, out uint number);
                    var edit = existing with
                    {
                        Title = Changed(song.Title, track.Name, existing.Title),
                        Artist = Changed(song.Artist, track.Artist, existing.Artist),
                        Album = Changed(name.Text, track.Album, existing.Album),
                        AlbumArtist = owner.Text.Trim() == initialOwner ? existing.AlbumArtist : Changed(owner.Text, track.AlbumArtist, existing.AlbumArtist),
                        Genre = genre.Text.Trim() == initialGenre ? existing.Genre : Changed(genre.Text, track.Genre, existing.Genre),
                        Year = year.Text.Trim() == initialYear || albumYear == track.Year ? existing.Year : albumYear,
                        Disc = disc == track.Disc ? existing.Disc : disc,
                        Number = number == track.Number ? existing.Number : number,
                        ArtworkOverride = artworkKey ?? existing.ArtworkOverride
                    };
                    if (edit != existing) changes.Add((track, edit));
                }
                if (changes.Count == 0) { feedback.Text = T("Sin cambios"); return; }
                SaveLibraryTagChanges(changes.Select(change => (change.Track.Path, change.Edit, (Track)null)).ToArray());
            }
            catch (Exception error) { feedback.Text = error.Message; return; }

            var paths = page.Tracks.Select(track => track.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var updated = library.Tracks.Where(track => paths.Contains(track.Path)).ToArray();
            var first = updated.FirstOrDefault();
            if (first != null)
            {
                collection = new LibraryRow(first.Album, AlbumOwner(first), IndexedAlbum(first.Album, AlbumOwner(first)));
                UpdateCollectionHeading();
            }
            if (artwork != null && current is { } playingTrack && paths.Contains(playingTrack.Path)) LoadPresentation(playingTrack);
            Status.Text = LocalTextExtension.Format("Etiquetas guardadas en {0:N0} pistas.", changes.Count);

            if (files.IsChecked == true)
            {
                writing = true;
                panel.IsEnabled = false;
                feedback.Text = T("Guardando etiquetas en los archivos FLAC…");
                var failures = await Task.Run(() =>
                {
                    var errors = new List<string>();
                    foreach (var (track, edit) in changes.Where(change => CanWriteFlac(change.Track)))
                    {
                        try { FlacTagWriter.Write(track.Path, edit, artwork); }
                        catch (Exception error) { errors.Add($"{Path.GetFileName(track.Path)} · {error.GetBaseException().Message}"); }
                    }
                    return errors;
                });
                writing = false;
                if (failures.Count > 0)
                    MessageBox.Show(dialog, LocalTextExtension.Format("Los cambios se guardaron en Prisma. No se pudieron actualizar {0:N0} archivos FLAC.", failures.Count) +
                        Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(4)),
                        T("Editar álbum"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            dialog.DialogResult = true;
        };

        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_ALBUM_EDITOR_PREVIEW") != null)
            dialog.ContentRendered += (_, _) =>
            {
                try { CaptureDesignPreview(surface, "PRISMA_ALBUM_EDITOR_PREVIEW"); }
                finally { dialog.Close(); }
            };
        editingAlbum = true;
        try { dialog.ShowDialog(); }
        finally { editingAlbum = false; }

        string Common(Func<Track, string> read) => page.Tracks.Select(track => read(track) ?? "").Distinct().Take(2).ToArray()
            is [var value] ? value : "";

        TextBox Field(string label, string value)
        {
            var caption = new TextBlock { Text = T(label), Margin = new Thickness(0, 0, 0, 4) };
            var input = new TextBox { Text = value, Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 9),
                Style = (Style)FindResource("LibrarySearch") };
            input.SetResourceReference(BackgroundProperty, "ControlFill");
            input.SetResourceReference(ForegroundProperty, "Ink");
            input.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
            input.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
            AutomationProperties.SetLabeledBy(input, caption);
            panel.Children.Add(caption);
            panel.Children.Add(input);
            return input;
        }

        Button ButtonFor(string label)
        {
            var button = new Button { Content = T(label), Style = (Style)FindResource(typeof(Button)), MinWidth = 84 };
            button.SetResourceReference(BackgroundProperty, "ControlFill");
            button.SetResourceReference(ForegroundProperty, "Ink");
            return button;
        }

        void Column(string label, string property, DataGridLength width)
        {
            var display = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
            display.Setters.Add(new Setter(MarginProperty, new Thickness(7, 0, 7, 0)));
            display.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
            editor.Columns.Add(new DataGridTextColumn
            {
                Header = T(label), Width = width, ElementStyle = display, Binding = new System.Windows.Data.Binding(property)
            });
        }
    }

    private static string Changed(string value, string current, string previous) =>
        value?.Trim() == (current ?? "") ? previous : value?.Trim();

    private bool CanWriteFlac(Track track) =>
        !IsDemo(track) && string.Equals(Path.GetExtension(track.Path), ".flac", StringComparison.OrdinalIgnoreCase);

    private static string StoreAlbumArtwork(byte[] bytes)
    {
        Directory.CreateDirectory(AlbumArtworkDirectory);
        string key = Convert.ToHexString(SHA256.HashData(bytes));
        string path = Path.Combine(AlbumArtworkDirectory, key + ".img");
        if (File.Exists(path)) return key;
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return key;
    }
}
