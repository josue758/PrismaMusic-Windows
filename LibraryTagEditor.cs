using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrismaMusic;

public sealed record LibraryTagEdit(string Title, string Artist, string Album, string Genre, uint Year)
{
    internal Track ApplyTo(Track track) => track with { Name = Title, Artist = Artist, Album = Album, Genre = Genre, Year = Year };
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
            Rows.SelectedItem = row.Content;
        }
        args.Handled = Rows.SelectedItem is not LibraryRow { Tracks.Length: 1 };
    }

    private void EditLibraryTags(object sender, RoutedEventArgs args)
    {
        var track = Selected;
        if (track == null) return;
        string Translate(string label) => LocalTextExtension.Translate(label);
        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        body.Children.Add(new TextBlock { Text = track.Name, FontSize = 23, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 12) });
        var title = AddField("Título", track.Name);
        var artist = AddField("Artista", track.Artist);
        var album = AddField("Álbum", track.Album);
        var genre = AddField("Género", track.Genre);
        var year = AddField("Año", track.Year == 0 ? "" : track.Year.ToString(CultureInfo.InvariantCulture));
        body.Children.Add(new TextBlock
        {
            Text = Translate("Los cambios se guardan en tu biblioteca de Prisma Music."),
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10)
        });
        var validation = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 173, 171)),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        body.Children.Add(validation);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = Translate("Guardar"), IsDefault = true, MinWidth = 92,
            Background = (Brush)FindResource("AccentBrush"), Foreground = (Brush)FindResource("OnAccentBrush") };
        footer.Children.Add(new Button { Content = Translate("Cancelar"), IsCancel = true, MinWidth = 92 });
        footer.Children.Add(save);
        body.Children.Add(footer);

        var surface = new SolidColorBrush(Color.FromRgb(33, 34, 40));
        var scroll = new ScrollViewer { Content = body, Background = surface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new Window
        {
            Owner = listeningRoom ?? this, Title = Translate("Editar etiquetas de biblioteca"),
            Width = 490, MaxHeight = SystemParameters.WorkArea.Height, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = surface, Content = scroll
        };
        dialog.Loaded += (_, _) => { title.Focus(); title.SelectAll(); };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { ShowError("Escribe un título.", title); return; }
            uint parsedYear = 0;
            if (year.Text.Trim().Length != 0 && (!uint.TryParse(year.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out parsedYear) || parsedYear is 0 or > 9999))
            {
                ShowError("Escribe un año entre 1 y 9999.", year);
                return;
            }
            var edit = new LibraryTagEdit(title.Text.Trim(), artist.Text.Trim(), album.Text.Trim(), genre.Text.Trim(), parsedYear);
            try { SaveLibraryTagEdit(track, edit); }
            catch (Exception error)
            {
                validation.Text = Translate("No se guardó la biblioteca. ") + error.Message;
                validation.Visibility = Visibility.Visible;
                return;
            }
            dialog.DialogResult = true;
        };
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_TAG_EDITOR_PREVIEW") != null)
            dialog.ContentRendered += (_, _) =>
            {
                try { CaptureDesignPreview(scroll, "PRISMA_TAG_EDITOR_PREVIEW"); }
                finally { dialog.Close(); }
            };
        dialog.ShowDialog();

        TextBox AddField(string name, string value)
        {
            var label = new TextBlock { Text = Translate(name), Margin = new Thickness(0, 0, 0, 3) };
            var field = new TextBox { Text = value, MinHeight = 32, Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Color.FromRgb(24, 25, 31)),
                Foreground = (Brush)FindResource("Ink"), BorderBrush = new SolidColorBrush(Color.FromRgb(89, 90, 101)) };
            System.Windows.Automation.AutomationProperties.SetLabeledBy(field, label);
            body.Children.Add(label);
            body.Children.Add(field);
            return field;
        }

        void ShowError(string text, TextBox field)
        {
            validation.Text = Translate(text);
            validation.Visibility = Visibility.Visible;
            field.Focus();
        }
    }

    private void SaveLibraryTagEdit(Track track, LibraryTagEdit edit)
    {
        bool Matches(Track candidate) => candidate != null && StringComparer.OrdinalIgnoreCase.Equals(candidate.Path, track.Path);
        Track Replace(Track candidate) => Matches(candidate) ? edit.ApplyTo(candidate) : candidate;
        var oldTracks = library.Tracks;
        var hadEdit = library.TagEdits.TryGetValue(track.Path, out var oldEdit);
        library.TagEdits[track.Path] = edit;
        library.Tracks = oldTracks.Select(Replace).ToList();
        try { database.Save(library, tracksChanged: true); }
        catch
        {
            library.Tracks = oldTracks;
            if (hadEdit) library.TagEdits[track.Path] = oldEdit;
            else library.TagEdits.Remove(track.Path);
            throw;
        }
        unsavedTracks = false;
        context = context.Select(Replace).ToArray();
        originalContext = originalContext.Select(Replace).ToArray();
        for (int index = 0; index < queue.Count; index++) queue[index] = Replace(queue[index]);
        if (collection != null) collection = collection with { Tracks = collection.Tracks.Select(Replace).ToArray() };
        RefreshFilters();
        RefreshRows();
        if (Matches(current))
        {
            current = Replace(current);
            Now.Text = current.Name;
            Artist.Text = current.Detail;
            UpdateMini();
        }
    }
}
