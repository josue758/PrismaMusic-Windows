using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record BatchUndo(Track Before, LibraryTagEdit Previous, LibraryTagEdit Applied, Track After);
    private BatchUndo[] undoBatchTags;
    private MenuItem batchTagsMenu;

    private void PrepareBatchTagMenu()
    {
        var items = Rows.ContextMenu.Items;
        bool batch = Rows.SelectedItems.Count > 1;
        if (batchTagsMenu != null) items.Remove(batchTagsMenu);
        foreach (var item in items.OfType<Control>()) item.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        if (!batch) return;
        batchTagsMenu = new MenuItem { Header = T("Editar etiquetas por lotes"), Style = (Style)FindResource("PlayerMenuItem") };
        batchTagsMenu.Click += EditBatchTags;
        items.Add(batchTagsMenu);
    }

    private void EditBatchTags(object sender, RoutedEventArgs args)
    {
        if (scanning || relocatingSource) return;
        var selected = Rows.SelectedItems.OfType<LibraryRow>().Where(row => row.Tracks.Length == 1)
            .Select(row => row.Tracks[0].Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tracks = Rows.Items.OfType<LibraryRow>().Where(row => row.Tracks.Length == 1 && selected.Remove(row.Tracks[0].Path))
            .Select(row => row.Tracks[0]).ToArray();
        if (tracks.Length < 2) return;

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(SettingsHint("Activa los campos que quieres cambiar. Los números de pista siguen el orden visible de la selección."));
        var owner = Field("Artista del álbum");
        var genre = Field("Género");
        var year = Field("Año");
        var numbering = Field("Numerar pistas desde", "1");
        var preview = new ListBox { Height = 168, Margin = new Thickness(0, 10, 0, 0) };
        preview.SetResourceReference(BackgroundProperty, "SettingsCard");
        preview.SetResourceReference(ForegroundProperty, "Ink");
        System.Windows.Automation.AutomationProperties.SetName(preview, T("Vista previa"));
        root.Children.Add(preview);
        var feedback = SettingsHint("");
        root.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = T("Aplicar cambios"), IsDefault = true };
        actions.Children.Add(new Button { Content = T("Cancelar"), IsCancel = true });
        actions.Children.Add(apply);
        root.Children.Add(actions);
        var dialog = new Window { Owner = this, Title = T("Editar etiquetas por lotes"), Width = 570,
            MaxHeight = SystemParameters.WorkArea.Height, SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var surface = new Grid();
        surface.Children.Add(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = surface;
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);
        foreach (var field in new[] { owner, genre, year, numbering })
        {
            field.Check.Checked += (_, _) => RefreshPreview();
            field.Check.Unchecked += (_, _) => RefreshPreview();
            field.Text.TextChanged += (_, _) => RefreshPreview();
        }
        RefreshPreview();
        apply.Click += (_, _) =>
        {
            if (!TryPlan(out var edits, tracks.Length)) return;
            try
            {
                var before = tracks.Select((track, index) => new BatchUndo(track, library.TagEdits.GetValueOrDefault(track.Path),
                    edits[index], edits[index].ApplyTo(track))).ToArray();
                SaveLibraryTagChanges(tracks.Select((track, index) => (track.Path, edits[index], (Track)null)).ToArray());
                undoBatchTags = before;
                Status.Text = LocalTextExtension.Format("Etiquetas guardadas en {0:N0} pistas.", tracks.Length);
                dialog.DialogResult = true;
            }
            catch (Exception error) { feedback.Text = error.Message; }
        };
        dialog.ShowDialog();

        (CheckBox Check, TextBox Text) Field(string label, string initial = "")
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var check = new CheckBox { Content = T(label), VerticalAlignment = VerticalAlignment.Center };
            var input = new TextBox { Text = initial, IsEnabled = false, Padding = new Thickness(7),
                Style = (Style)FindResource("LibrarySearch") };
            input.SetResourceReference(BackgroundProperty, "ControlFill");
            input.SetResourceReference(ForegroundProperty, "Ink");
            System.Windows.Automation.AutomationProperties.SetLabeledBy(input, check);
            Grid.SetColumn(input, 1);
            row.Children.Add(check);
            row.Children.Add(input);
            root.Children.Add(row);
            return (check, input);
        }

        bool TryPlan(out LibraryTagEdit[] edits, int count)
        {
            edits = null;
            bool editYear = year.Check.IsChecked == true;
            bool renumber = numbering.Check.IsChecked == true;
            uint newYear = 0, start = 0;
            if (editYear && year.Text.Text.Trim().Length > 0 &&
                (!uint.TryParse(year.Text.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out newYear) || newYear is 0 or > 9999))
                return false;
            if (renumber && (!uint.TryParse(numbering.Text.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out start) ||
                start == 0 || (long)start + tracks.Length - 1 > 9999)) return false;
            edits = tracks.Take(count).Select((track, index) =>
            {
                var existing = library.TagEdits.GetValueOrDefault(track.Path) ?? new LibraryTagEdit();
                return existing with
                {
                    Genre = genre.Check.IsChecked == true ? genre.Text.Text.Trim() : existing.Genre,
                    Year = editYear ? newYear : existing.Year,
                    AlbumArtist = owner.Check.IsChecked == true ? owner.Text.Text.Trim() : existing.AlbumArtist,
                    Number = renumber ? start + (uint)index : existing.Number
                };
            }).ToArray();
            return true;
        }

        void RefreshPreview()
        {
            var fields = new[] { owner, genre, year, numbering };
            foreach (var field in fields) field.Text.IsEnabled = field.Check.IsChecked == true;
            bool valid = TryPlan(out var edits, Math.Min(10, tracks.Length));
            apply.IsEnabled = valid && fields.Any(field => field.Check.IsChecked == true);
            preview.ItemsSource = valid ? edits.Select((edit, index) => edit.ApplyTo(tracks[index])).Select(track => string.Join(" · ", new[]
            {
                track.Number > 0 ? track.Number.ToString() : null, track.Name, track.AlbumArtist, track.Genre,
                track.Year > 0 ? track.Year.ToString() : null
            }.Where(value => !string.IsNullOrWhiteSpace(value)))).ToArray() : Array.Empty<string>();
            feedback.Text = valid ? LocalTextExtension.Format("Vista previa de {0:N0} de {1:N0} pistas.", edits.Length, tracks.Length)
                : T("Revisa el año y la numeración. Los valores admitidos van de 1 a 9999.");
        }
    }

    private void UndoBatchTags(object sender, RoutedEventArgs args)
    {
        if (scanning || relocatingSource || undoBatchTags is not { } batch) return;
        var currentTracks = library.Tracks.ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        if (batch.Any(item => currentTracks.GetValueOrDefault(item.Before.Path) != item.After ||
            library.TagEdits.GetValueOrDefault(item.Before.Path) != item.Applied))
        {
            Status.Text = T("Hay pistas que cambiaron desde la edición por lotes.");
            undoBatchTags = null;
            return;
        }
        try
        {
            SaveLibraryTagChanges(batch.Select(item => (item.Before.Path, item.Previous, item.Before)).ToArray());
            undoBatchTags = null;
            Status.Text = T("Se deshizo la edición por lotes.");
        }
        catch (Exception error) { Status.Text = error.Message; }
    }
}
