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
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
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
        var surface = new Grid();
        surface.SetResourceReference(BackgroundProperty, "SettingsBackground");
        var layout = new Grid { Margin = new Thickness(22, 18, 22, 0) };
        surface.Children.Add(layout);
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var heading = new TextBlock { Text = T("Editar álbum"), FontSize = 27,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(heading);
        var metadata = new Grid();
        metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metadata.ColumnDefinitions.Add(new ColumnDefinition());
        var metadataCard = Card(metadata, new Thickness(14));
        header.Children.Add(metadataCard);
        layout.Children.Add(header);

        byte[] artwork = null;
        var artworkImage = new Image { Source = CollectionArt.Source, Stretch = Stretch.UniformToFill };
        var artworkPreview = new Grid { Width = 100, Height = 100 };
        artworkPreview.Children.Add(new ArtworkTile { Caption = page.Name });
        artworkPreview.Children.Add(artworkImage);
        var selectArtwork = ButtonFor("Elegir carátula");
        selectArtwork.Margin = new Thickness(0, 7, 0, 0);
        selectArtwork.Padding = new Thickness(9, 5, 9, 5);
        var cover = new StackPanel { Width = 122, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        var artworkFrame = new Border { CornerRadius = new CornerRadius(7), Child = artworkPreview,
            ClipToBounds = true, Width = 100, HorizontalAlignment = HorizontalAlignment.Center };
        cover.Children.Add(artworkFrame);
        cover.Children.Add(selectArtwork);
        metadata.Children.Add(cover);

        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        for (int index = 0; index < 3; index++) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(fields, 1);
        metadata.Children.Add(fields);

        var (initialOwner, mixedOwners) = Common(track => track.AlbumArtist);
        var (initialGenre, mixedGenres) = Common(track => track.Genre);
        var (initialYear, mixedYears) = Common(track => NumberText(track.Year));
        var name = Field("Álbum", page.Name, 0, 0);
        var owner = Field("Artista del álbum", initialOwner, 0, 1);
        var genre = Field("Género", initialGenre, 1, 0);
        var year = Field("Año", initialYear, 1, 1);
        var mixed = SettingsHint("Los campos con varios valores se conservan si los dejas vacíos.");
        mixed.Margin = new Thickness(0, 3, 0, 0);
        Grid.SetRow(mixed, 2);
        Grid.SetColumnSpan(mixed, 2);
        if (mixedOwners || mixedGenres || mixedYears) fields.Children.Add(mixed);

        var tracks = new DockPanel();
        Grid.SetRow(tracks, 1);
        layout.Children.Add(tracks);
        var tracksHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        DockPanel.SetDock(tracksHeader, Dock.Top);
        tracks.Children.Add(tracksHeader);
        string trackCount = songs.Length == 1 ? T("1 pista") : LocalTextExtension.Format("{0:N0} pistas", songs.Length);
        var count = SettingsHint(trackCount + "  ·  " + T("Doble clic o F2 para editar"));
        count.Margin = new Thickness(0);
        count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(count, Dock.Right);
        tracksHeader.Children.Add(count);
        tracksHeader.Children.Add(new TextBlock { Text = T("Canciones del álbum"), FontSize = 17, FontWeight = FontWeights.SemiBold });

        var editor = new DataGrid
        {
            AutoGenerateColumns = false, ItemsSource = songs, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.Cell,
            RowHeaderWidth = 0, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new Thickness(0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanUserResizeRows = false
        };
        editor.SetResourceReference(BackgroundProperty, "SettingsCard");
        editor.SetResourceReference(ForegroundProperty, "Ink");
        editor.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        editor.Resources.Add(typeof(ScrollBar), new Style(typeof(ScrollBar), (Style)FindResource("ThinVerticalScrollBar")));
        editor.SetResourceReference(DataGrid.RowBackgroundProperty, "SettingsCard");
        editor.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "ControlFill");
        editor.AlternationCount = 2;
        var cells = new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("Ink")));
        cells.Setters.Add(new Setter(DataGridCell.MinHeightProperty, 34.0));
        cells.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
        selectedCell.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("OnAccentBrush")));
        cells.Triggers.Add(selectedCell);
        editor.CellStyle = cells;
        var headers = new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("SettingsBackground")));
        headers.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Muted")));
        headers.Setters.Add(new Setter(PaddingProperty, new Thickness(11, 7, 7, 7)));
        headers.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        headers.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headers.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("WorkspaceStroke")));
        editor.ColumnHeaderStyle = headers;
        Column("Título", nameof(AlbumTrackEdit.Title), new DataGridLength(3, DataGridLengthUnitType.Star));
        Column("Artista", nameof(AlbumTrackEdit.Artist), new DataGridLength(2, DataGridLengthUnitType.Star));
        Column("Disco", nameof(AlbumTrackEdit.Disc), new DataGridLength(54));
        Column("Pista", nameof(AlbumTrackEdit.Number), new DataGridLength(54));
        AutomationProperties.SetName(editor, T("Canciones del álbum"));
        tracks.Children.Add(Card(editor, new Thickness(1)));

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var divider = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        divider.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        DockPanel.SetDock(divider, Dock.Top);
        footer.Children.Add(divider);
        var feedback = SettingsHint("");
        feedback.Margin = new Thickness(0, 8, 0, 0);
        feedback.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        DockPanel.SetDock(feedback, Dock.Top);
        footer.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        var cancel = ButtonFor("Cancelar");
        cancel.IsCancel = true;
        var save = ButtonFor("Guardar");
        save.IsDefault = true;
        save.SetResourceReference(BackgroundProperty, "AccentBrush");
        save.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        save.Margin = new Thickness(0);
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        var commands = new DockPanel { Margin = new Thickness(0, 11, 0, 12), LastChildFill = true };
        var files = new CheckBox { Content = T("Guardar también en los archivos FLAC"), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("Guardar también en los archivos FLAC"), Style = (Style)FindResource("SettingsToggle"),
            IsEnabled = songs.Any(song => CanWriteFlac(song.Track)), MaxWidth = 356, HorizontalAlignment = HorizontalAlignment.Left };
        commands.Children.Add(actions);
        commands.Children.Add(files);
        footer.Children.Add(commands);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        var workArea = SystemParameters.WorkArea.Size;
        if (WindowPlacement.TryGetBounds(new WindowInteropHelper(this).Handle, fullscreen: false, out var bounds))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            workArea = new Size((bounds.Right - bounds.Left) / dpi.DpiScaleX, (bounds.Bottom - bounds.Top) / dpi.DpiScaleY);
        }
        double maxWidth = Math.Max(1, workArea.Width - 24);
        double maxHeight = Math.Max(1, workArea.Height - 24);
        var dialog = new Window { Owner = this, Title = T("Editar álbum"),
            Width = Math.Min(850, maxWidth), Height = Math.Min(660, maxHeight),
            MinWidth = Math.Min(500, maxWidth), MinHeight = Math.Min(440, maxHeight),
            MaxWidth = maxWidth, MaxHeight = maxHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false, Content = surface };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);
        dialog.SizeChanged += (_, _) =>
        {
            bool compact = dialog.ActualHeight < 510;
            layout.Margin = compact ? new Thickness(16, 10, 16, 0) : new Thickness(22, 18, 22, 0);
            header.Margin = new Thickness(0, 0, 0, compact ? 8 : 16);
            heading.FontSize = compact ? 22 : 27;
            heading.Margin = new Thickness(0, 0, 0, compact ? 7 : 12);
            metadataCard.Padding = new Thickness(compact ? 9 : 14);
            commands.Margin = compact ? new Thickness(0, 7, 0, 8) : new Thickness(0, 11, 0, 12);
            artworkPreview.Width = artworkPreview.Height = compact ? 76 : 100;
            artworkFrame.Width = artworkPreview.Width;
        };
        dialog.Loaded += async (_, _) =>
        {
            if (artworkImage.Source != null) return;
            var image = await Task.Run(() => ReadCollectionImage(page, artist: false, 200));
            if (dialog.IsVisible && artwork == null && image != null) artworkImage.Source = image;
        };

        selectArtwork.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "JPEG / PNG|*.jpg;*.jpeg;*.png", CheckFileExists = true };
            if (picker.ShowDialog(dialog) != true) return;
            try
            {
                using var stream = File.OpenRead(picker.FileName);
                if (stream.Length is <= 0 or > 12 * 1024 * 1024) throw new IOException(T("Elige una imagen de hasta 12 MB."));
                var image = DecodeArtwork(stream, 280);
                if (image == null) throw new IOException(T("No se pudo abrir la carátula."));
                stream.Position = 0;
                var bytes = new byte[(int)stream.Length];
                stream.ReadExactly(bytes);
                artwork = bytes;
                artworkImage.Source = image;
                selectArtwork.ToolTip = Path.GetFileName(picker.FileName);
                SetFeedback("");
            }
            catch (Exception error) { SetFeedback(error.Message); }
        };

        bool writing = false;
        dialog.Closing += (_, args) => args.Cancel = writing;
        save.Click += async (_, _) =>
        {
            editor.CommitEdit(DataGridEditingUnit.Cell, true);
            editor.CommitEdit(DataGridEditingUnit.Row, true);
            if (string.IsNullOrWhiteSpace(name.Text) || songs.Any(song => string.IsNullOrWhiteSpace(song.Title)))
            {
                SetFeedback(T("Escribe un nombre para el álbum y un título para cada canción."));
                return;
            }
            if (!ReadTagNumber(year.Text, out uint albumYear) ||
                songs.Any(song => !ReadTagNumber(song.Disc, out _) || !ReadTagNumber(song.Number, out _)))
            {
                SetFeedback(T("Escribe un número entre 1 y 9999 o deja el campo vacío."));
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
                if (changes.Count == 0) { SetFeedback(T("Sin cambios")); return; }
                SaveLibraryTagChanges(changes.Select(change => (change.Track.Path, change.Edit, (Track)null)).ToArray());
            }
            catch (Exception error) { SetFeedback(error.Message); return; }

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
                header.IsEnabled = tracks.IsEnabled = commands.IsEnabled = false;
                SetFeedback(T("Guardando etiquetas en los archivos FLAC…"));
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
            dialog.ContentRendered += async (_, _) =>
            {
                try
                {
                    CaptureDesignPreview(surface, "PRISMA_ALBUM_EDITOR_PREVIEW");
                    if (Environment.GetEnvironmentVariable("PRISMA_ALBUM_EDITOR_COMPACT_PREVIEW") != null)
                    {
                        dialog.Width = dialog.MinWidth;
                        dialog.Height = dialog.MinHeight;
                        await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout, System.Windows.Threading.DispatcherPriority.Loaded);
                        CaptureDesignPreview(surface, "PRISMA_ALBUM_EDITOR_COMPACT_PREVIEW");
                        double saveBottom = save.TranslatePoint(new Point(0, save.ActualHeight), surface).Y;
                        if (editor.ActualHeight < 80 || saveBottom > surface.ActualHeight)
                            throw new InvalidOperationException($"Compact album editor height {editor.ActualHeight}, save bottom {saveBottom}, surface {surface.ActualHeight}.");
                    }
                }
                finally { dialog.Close(); }
            };
        editingAlbum = true;
        try { dialog.ShowDialog(); }
        finally { editingAlbum = false; }

        (string Value, bool IsMixed) Common(Func<Track, string> read)
        {
            string first = read(page.Tracks[0]) ?? "";
            bool mixedValues = page.Tracks.Skip(1).Any(track => (read(track) ?? "") != first);
            return (mixedValues ? "" : first, mixedValues);
        }

        void SetFeedback(string message)
        {
            feedback.Text = message;
            feedback.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        Border Card(FrameworkElement content, Thickness padding)
        {
            var card = new Border { Child = content, Padding = padding, CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1), ClipToBounds = true };
            card.SetResourceReference(Border.BackgroundProperty, "SettingsCard");
            card.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
            return card;
        }

        TextBox Field(string label, string value, int row, int column)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, column == 0 ? 12 : 0, 8) };
            var caption = new TextBlock { Text = T(label), Margin = new Thickness(0, 0, 0, 3) };
            var input = new TextBox { Text = value, Padding = new Thickness(9, 3, 9, 3), MinHeight = 31,
                Style = (Style)FindResource("LibrarySearch") };
            input.SetResourceReference(BackgroundProperty, "ControlFill");
            input.SetResourceReference(ForegroundProperty, "Ink");
            input.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
            input.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
            AutomationProperties.SetLabeledBy(input, caption);
            group.Children.Add(caption);
            group.Children.Add(input);
            Grid.SetRow(group, row);
            Grid.SetColumn(group, column);
            fields.Children.Add(group);
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
            display.Setters.Add(new Setter(MarginProperty, new Thickness(11, 0, 7, 0)));
            display.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            var editStyle = new Style(typeof(TextBox), (Style)FindResource("LibrarySearch"));
            editStyle.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("SettingsBackground")));
            editStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Ink")));
            editStyle.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
            editStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, new DynamicResourceExtension("Ink")));
            editStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(9, 0, 6, 0)));
            display.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
            editor.Columns.Add(new DataGridTextColumn
            {
                Header = T(label), Width = width, ElementStyle = display, EditingElementStyle = editStyle,
                Binding = new System.Windows.Data.Binding(property)
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
