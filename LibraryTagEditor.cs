using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrismaMusic;

public sealed record LibraryTagEdit(string Title, string Artist, string Album, string Genre, uint Year,
    string AlbumArtist = null, uint? Disc = null, uint? Number = null)
{
    internal Track ApplyTo(Track track)
    {
        var edited = track with { Name = Title, Artist = Artist, Album = Album, Genre = Genre, Year = Year,
            AlbumArtist = AlbumArtist ?? track.AlbumArtist, Disc = Disc ?? track.Disc, Number = Number ?? track.Number };
        return edited == track ? track : edited;
    }
}

public partial class MainWindow
{
    private Track ApplyLibraryTags(Track track) =>
        library.TagEdits.TryGetValue(track.Path, out var edit) ? edit.ApplyTo(track) : track;

    private void PrepareLibraryTagsMenu(object sender, ContextMenuEventArgs args)
    {
        if (args.CursorLeft >= 0)
        {
            var target = Mouse.DirectlyOver as DependencyObject;
            var row = target == null ? null : ItemsControl.ContainerFromElement(Rows, target) as ListBoxItem;
            if (row == null)
            {
                args.Handled = true;
                return;
            }
            if (!row.IsSelected) Rows.SelectedItem = row.Content;
        }
        args.Handled = Equals(Rows.Tag, "Gallery") || Rows.SelectedItem is not LibraryRow { Tracks.Length: 1 };
        if (!args.Handled) PrepareBatchTagMenu();
    }

    private void EditLibraryTags(object sender, RoutedEventArgs args) =>
        EditLibraryTags(Selected, listeningRoom ?? this);

    private void EditLibraryTags(Track track, Window owner)
    {
        if (track == null) return;
        if (scanning)
        {
            Status.Text = LocalTextExtension.Translate("Espera a que termine el escaneo para editar etiquetas.");
            return;
        }
        string Translate(string label) => LocalTextExtension.Translate(label);
        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        body.Children.Add(new TextBlock { Text = track.Name, FontSize = 23, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 12) });
        var title = AddField("Título", track.Name);
        var artist = AddField("Artista", track.Artist);
        var album = AddField("Álbum", track.Album);
        var albumArtist = AddField("Artista del álbum", track.AlbumArtist);
        var genre = AddField("Género", track.Genre);
        var numbers = new Grid();
        body.Children.Add(numbers);
        var year = AddNumber("Año", track.Year);
        var disc = AddNumber("Disco", track.Disc);
        var number = AddNumber("Pista", track.Number);
        body.Children.Add(new TextBlock
        {
            Text = Translate("Los cambios se guardan en tu biblioteca de Prisma Music."),
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10)
        });
        var validation = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 173, 171)),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);
        body.Children.Add(validation);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var buttonStyle = (Style)FindResource(typeof(Button));
        var save = new Button { Content = Translate("Guardar"), IsDefault = true, MinWidth = 92, Style = buttonStyle,
            Background = (Brush)FindResource("AccentBrush"), Foreground = (Brush)FindResource("OnAccentBrush") };
        var cancel = new Button { Content = Translate("Cancelar"), IsCancel = true, MinWidth = 92, Style = buttonStyle };
        cancel.SetResourceReference(BackgroundProperty, "SettingsCard");
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        var restore = new Button { Content = Translate("Recuperar etiquetas del archivo"), Style = buttonStyle,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
            Visibility = library.TagEdits.ContainsKey(track.Path) ? Visibility.Visible : Visibility.Collapsed };
        body.Children.Add(restore);
        body.Children.Add(footer);

        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "SettingsBackground");
        var root = new Grid();
        root.Children.Add(scroll);
        var dialog = new Window
        {
            Owner = owner, Title = Translate("Editar etiquetas de biblioteca"),
            Width = 530, MaxHeight = SystemParameters.WorkArea.Height, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = root
        };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, root);
        dialog.Loaded += (_, _) => { title.Focus(); title.SelectAll(); };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { ShowError("Escribe un título.", title); return; }
            if (!ValidateNumber(year, out uint parsedYear) || !ValidateNumber(disc, out uint parsedDisc) ||
                !ValidateNumber(number, out uint parsedNumber)) return;
            var edit = new LibraryTagEdit(title.Text.Trim(), artist.Text.Trim(), album.Text.Trim(), genre.Text.Trim(), parsedYear,
                albumArtist.Text.Trim(), parsedDisc, parsedNumber);
            try { SaveLibraryTagEdit(track, edit); }
            catch (Exception error)
            {
                validation.Text = Translate("No se guardó la biblioteca. ") + error.Message;
                validation.Visibility = Visibility.Visible;
                return;
            }
            dialog.DialogResult = true;
        };
        restore.Click += async (_, _) =>
        {
            restore.IsEnabled = save.IsEnabled = false;
            try
            {
                var original = await Task.Run(() => LibraryScanner.ReadFile(track.Path));
                if (!dialog.IsVisible) return;
                SaveLibraryTagEdit(track, null, original);
                dialog.DialogResult = true;
            }
            catch (Exception error)
            {
                validation.Text = Translate("No se pudieron recuperar las etiquetas. ") + error.Message;
                validation.Visibility = Visibility.Visible;
            }
            finally { restore.IsEnabled = save.IsEnabled = true; }
        };
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_TAG_EDITOR_PREVIEW") != null)
            dialog.ContentRendered += (_, _) =>
            {
                try { CaptureDesignPreview(scroll, "PRISMA_TAG_EDITOR_PREVIEW"); }
                finally { dialog.Close(); }
            };
        dialog.ShowDialog();

        TextBox AddNumber(string caption, uint value)
        {
            var group = new StackPanel { Margin = new Thickness(numbers.Children.Count == 0 ? 0 : 8, 0, 0, 0) };
            Grid.SetColumn(group, numbers.Children.Count);
            numbers.ColumnDefinitions.Add(new ColumnDefinition());
            numbers.Children.Add(group);
            return AddField(caption, value == 0 ? "" : value.ToString(CultureInfo.InvariantCulture), group);
        }

        TextBox AddField(string name, string value, Panel target = null)
        {
            var label = new TextBlock { Text = Translate(name), Margin = new Thickness(0, 0, 0, 3) };
            var field = new TextBox { Text = value, MinHeight = 32, Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(8, 4, 8, 4), Style = (Style)FindResource("LibrarySearch") };
            field.SetResourceReference(BackgroundProperty, "ControlFill");
            field.SetResourceReference(ForegroundProperty, "Ink");
            field.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
            AutomationProperties.SetLabeledBy(field, label);
            (target ?? body).Children.Add(label);
            (target ?? body).Children.Add(field);
            return field;
        }

        bool ValidateNumber(TextBox field, out uint value)
        {
            value = 0;
            string input = field.Text.Trim();
            if (input.Length == 0 || uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                value is > 0 and <= 9999) return true;
            ShowError("Escribe un número entre 1 y 9999 o deja el campo vacío.", field);
            return false;
        }

        void ShowError(string text, TextBox field)
        {
            validation.Text = Translate(text);
            validation.Visibility = Visibility.Visible;
            field.Focus();
        }
    }

    private void SaveLibraryTagEdit(Track track, LibraryTagEdit edit, Track restored = null)
    {
        SaveLibraryTagChanges(new[] { (track.Path, edit, restored) });
    }

    private void SaveLibraryTagChanges((string Path, LibraryTagEdit Edit, Track Restored)[] changes)
    {
        if (scanning) throw new InvalidOperationException(T("Espera a que termine el escaneo para editar etiquetas."));
        var oldTracks = library.Tracks;
        var oldEdits = library.TagEdits;
        var originals = oldTracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        var nextEdits = new System.Collections.Generic.Dictionary<string, LibraryTagEdit>(oldEdits, StringComparer.OrdinalIgnoreCase);
        var updated = new System.Collections.Generic.Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, edit, restored) in changes)
        {
            if (!originals.TryGetValue(path, out var original)) throw new InvalidOperationException(T("La pista ya no está en la biblioteca."));
            updated.Add(path, restored ?? edit.ApplyTo(original));
            if (edit == null) nextEdits.Remove(path);
            else nextEdits[path] = edit;
        }
        bool Matches(Track candidate) => candidate != null && updated.ContainsKey(candidate.Path);
        Track Replace(Track candidate) => candidate != null && updated.TryGetValue(candidate.Path, out var value) ? value : candidate;
        library.TagEdits = nextEdits;
        library.Tracks = oldTracks.Select(Replace).ToList();
        try { database.Save(library, tracksChanged: true); }
        catch
        {
            library.Tracks = oldTracks;
            library.TagEdits = oldEdits;
            throw;
        }
        unsavedTracks = false;
        context = context.Select(Replace).ToArray();
        originalContext = originalContext.Select(Replace).ToArray();
        for (int index = 0; index < queue.Count; index++) queue[index] = Replace(queue[index]);
        if (previous.Any(Matches))
        {
            var earlier = previous.Reverse().Select(Replace).ToArray();
            previous.Clear();
            foreach (var item in earlier) previous.Push(item);
        }
        collection = RefreshEditedCollection(collection, albumPage);
        for (int index = 0; index < libraryHistory.Count; index++)
        {
            var visit = libraryHistory[index];
            libraryHistory[index] = visit with { Collection = RefreshEditedCollection(visit.Collection, visit.Album) };
        }
        RefreshFilters();
        UpdateCollectionHeading();
        var selected = Rows.Items.OfType<LibraryRow>().FirstOrDefault(row => row.Tracks.Length == 1 && Matches(row.Tracks[0]));
        if (selected != null && !Equals(Rows.Tag, "Gallery")) Rows.SelectedItem = selected;
        if (Matches(current))
        {
            current = Replace(current);
            Now.Text = current.Name;
            Artist.Text = current.Detail;
            UpdateMini();
        }

        LibraryRow RefreshEditedCollection(LibraryRow page, bool isAlbum)
        {
            if (page?.Tracks.Any(Matches) != true) return page;
            var members = page.Tracks.Select(Replace).Where(candidate => isAlbum
                ? string.Equals(candidate.Album, page.Name, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(AlbumOwner(candidate), AlbumOwner(page.Tracks[0]), StringComparison.OrdinalIgnoreCase)
                : string.Equals(candidate.Artist, page.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Album).ThenBy(candidate => candidate.Disc)
                .ThenBy(candidate => candidate.Number).ThenBy(candidate => candidate.Name).ToArray();
            return members.Length == 0 ? null : page with { Tracks = members };
        }
    }
}
