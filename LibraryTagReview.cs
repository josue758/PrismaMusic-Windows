using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace PrismaMusic;

internal enum TagIssueKind { Missing, SplitAlbum, PossibleDuplicate }

internal sealed record TagIssue(TagIssueKind Kind, string Name, Track[] Tracks);

internal static class LibraryTagReview
{
    internal static TagIssue[] Find(IReadOnlyList<Track> tracks)
    {
        var result = new List<TagIssue>();
        var entries = tracks.Select(track => new
        {
            Track = track,
            Artist = Key(track.Artist),
            Album = Key(track.Album),
            Title = Key(track.Name)
        }).ToArray();
        var noArtist = entries.Where(entry => entry.Artist is "" or "ARTISTA DESCONOCIDO" or "UNKNOWN ARTIST" or "ARTISTA DESCONHECIDO")
            .Select(entry => entry.Track).ToArray();
        var noAlbum = entries.Where(entry => entry.Album is "" or "SIN ÁLBUM" or "UNKNOWN ALBUM" or "SEM ÁLBUM")
            .Select(entry => entry.Track).ToArray();
        if (noArtist.Length > 0) result.Add(new(TagIssueKind.Missing, "Pistas sin artista", noArtist));
        if (noAlbum.Length > 0) result.Add(new(TagIssueKind.Missing, "Pistas sin álbum", noAlbum));

        var missingArtists = noArtist.Select(track => track.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingAlbums = noAlbum.Select(track => track.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tagged = entries.Where(entry => !missingArtists.Contains(entry.Track.Path) && !missingAlbums.Contains(entry.Track.Path)).ToArray();
        foreach (var group in tagged.GroupBy(entry => (entry.Album, entry.Artist)))
        {
            var variants = group.Select(entry => (entry.Track.Album,
                Owner: string.IsNullOrWhiteSpace(entry.Track.AlbumArtist) ? entry.Track.Artist : entry.Track.AlbumArtist));
            if (variants.Distinct().Skip(1).Any())
            {
                var songs = group.Select(entry => entry.Track).OrderBy(track => track.Disc).ThenBy(track => track.Number)
                    .ThenBy(track => track.Name).ToArray();
                result.Add(new(TagIssueKind.SplitAlbum, $"{songs[0].Album} · {songs[0].Artist}", songs));
            }
        }

        foreach (var group in tagged.Where(entry => entry.Title.Length > 0 && entry.Track.Seconds > 0)
            .GroupBy(entry => (entry.Artist, entry.Album, entry.Title, entry.Track.Disc, entry.Track.Number)))
        {
            var ordered = group.Select(entry => entry.Track).DistinctBy(track => track.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(track => track.Seconds).ThenBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int start = 0; start < ordered.Length;)
            {
                int end = start + 1;
                while (end < ordered.Length && ordered[end].Seconds - ordered[start].Seconds <= 2) end++;
                if (end - start > 1)
                    result.Add(new(TagIssueKind.PossibleDuplicate, $"{ordered[start].Name} · {ordered[start].Artist}", ordered[start..end]));
                start = end;
            }
        }
        return result.OrderBy(issue => issue.Kind).ThenBy(issue => issue.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string Key(string value) => string.Join(" ", (value ?? "").Normalize(NormalizationForm.FormKC)
        .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

public partial class MainWindow
{
    private sealed record ReviewGroup(string Label, TagIssue Issue);
    private sealed record ReviewTrack(Track Track)
    {
        public string Title => Track.Name;
        public string Detail => string.Join(" · ", new[] { Track.Artist, Track.Album, Track.AlbumArtist, Track.FormatLabel }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        public string Path => Track.Path;
    }

    private void ShowTagReview(object sender, RoutedEventArgs args)
    {
        if (scanning || relocatingSource)
        {
            Status.Text = T("Espera a que termine el escaneo para editar etiquetas.");
            return;
        }
        var root = new DockPanel { Margin = new Thickness(22) };
        var surface = new Grid();
        var background = new Border { Child = root };
        background.SetResourceReference(BackgroundProperty, "SettingsBackground");
        surface.Children.Add(background);
        var dialog = new Window { Owner = this, Title = T("Revisión de etiquetas"), Width = 960, Height = 615,
            MinWidth = 570, MinHeight = 420, MaxWidth = Math.Max(570, SystemParameters.WorkArea.Width),
            MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height), Content = surface,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(HomeText("Revisión de etiquetas", 25, heading: true));
        header.Children.Add(SettingsHint("Revisa los grupos y selecciona las pistas que quieras corregir. Los duplicados son coincidencias de etiquetas y duración."));
        var category = new ComboBox { Style = (Style)FindResource("FilterCombo"), Width = 245,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(category, T("Tipo de revisión"));
        header.Children.Add(category);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var status = SettingsHint("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        footer.Children.Add(status);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions);
        root.Children.Add(footer);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        var groups = CreateList("Grupos de revisión");
        groups.DisplayMemberPath = nameof(ReviewGroup.Label);
        groups.Margin = new Thickness(0, 0, 10, 0);
        var tracks = CreateList("Pistas del grupo");
        tracks.SelectionMode = SelectionMode.Extended;
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetBinding(ToolTipProperty, new Binding(nameof(ReviewTrack.Path)));
        AddCell(nameof(ReviewTrack.Title), "Ink", true);
        AddCell(nameof(ReviewTrack.Detail), "Muted", false);
        AddCell(nameof(ReviewTrack.Path), "Muted", false);
        tracks.ItemTemplate = new DataTemplate { VisualTree = row };
        Grid.SetColumn(tracks, 1);
        lists.Children.Add(groups);
        lists.Children.Add(tracks);
        root.Children.Add(lists);

        var selectedIssue = Array.Empty<TagIssue>();
        bool reviewing = false;
        Button AddAction(string label, RoutedEventHandler handler = null)
        {
            var button = new Button { Content = T(label), Style = (Style)FindResource(typeof(Button)),
                Margin = new Thickness(6, 6, 0, 0) };
            button.SetResourceReference(BackgroundProperty, "SettingsCard");
            if (handler != null) button.Click += handler;
            actions.Children.Add(button);
            return button;
        }
        var copy = AddAction("Copiar ruta", (_, _) =>
        {
            if (tracks.SelectedItem is not ReviewTrack selected) return;
            try { Clipboard.SetText(selected.Path); }
            catch (System.Runtime.InteropServices.ExternalException error) { status.Text = error.Message; }
        });
        var all = AddAction("Seleccionar grupo", (_, _) => tracks.SelectAll());
        var refresh = AddAction("Volver a comprobar");
        var undo = AddAction("Deshacer edición por lotes");
        var edit = AddAction("Vista previa de edición");
        refresh.Click += async (_, _) => await Refresh();
        undo.Click += async (_, _) =>
        {
            UndoBatchTags(this, new RoutedEventArgs());
            await Refresh();
        };
        edit.Click += async (_, _) =>
        {
            if (scanning || relocatingSource) return;
            var selected = tracks.SelectedItems.Cast<ReviewTrack>().ToHashSet();
            var index = TrackIndex();
            var current = tracks.Items.Cast<ReviewTrack>().Where(selected.Contains)
                .Select(item => index.GetValueOrDefault(item.Path)).Where(track => track != null).ToArray();
            if (EditBatchTags(current, dialog)) await Refresh();
        };
        edit.SetResourceReference(BackgroundProperty, "AccentBrush");
        edit.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        AddAction("Cerrar", (_, _) => dialog.Close()).IsCancel = true;

        tracks.SelectionChanged += (_, _) => UpdateActions();
        groups.SelectionChanged += (_, _) =>
        {
            tracks.ItemsSource = groups.SelectedItem is ReviewGroup group
                ? group.Issue.Tracks.Select(track => new ReviewTrack(track)).ToArray() : Array.Empty<ReviewTrack>();
            UpdateActions();
        };
        category.SelectionChanged += (_, _) => UpdateGroups();
        dialog.Loaded += async (_, _) => await Refresh();
        UpdateActions();
        dialog.ShowDialog();

        async Task Refresh()
        {
            if (reviewing) return;
            reviewing = true;
            status.Text = T("Revisando etiquetas…");
            UpdateActions();
            try
            {
                var snapshot = library.Tracks.ToArray();
                var found = await Task.Run(() => LibraryTagReview.Find(snapshot));
                if (!dialog.IsVisible) return;
                selectedIssue = found;
                var previousCategory = category.SelectedIndex;
                category.ItemsSource = new[] { "Etiquetas faltantes", "Álbumes para revisar", "Posibles duplicados" }
                    .Select((label, index) => $"{T(label)} ({found.Count(item => (int)item.Kind == index):N0})").ToArray();
                category.SelectedIndex = previousCategory >= 0 ? previousCategory : found.Length > 0 ? (int)found[0].Kind : 0;
                UpdateGroups();
            }
            catch (Exception error) { if (dialog.IsVisible) status.Text = error.Message; }
            finally { reviewing = false; UpdateActions(); }
        }
        void UpdateGroups()
        {
            if (category.SelectedIndex < 0) return;
            groups.ItemsSource = selectedIssue.Where(issue => (int)issue.Kind == category.SelectedIndex).Select(issue =>
            {
                string name = issue.Kind == TagIssueKind.Missing ? T(issue.Name) : issue.Name;
                return new ReviewGroup($"{name} ({issue.Tracks.Length:N0})", issue);
            }).ToArray();
            groups.SelectedIndex = groups.Items.Count > 0 ? 0 : -1;
            status.Text = groups.Items.Count == 0 ? T("No se encontraron casos en esta categoría.") : "";
            UpdateActions();
        }
        void UpdateActions()
        {
            bool canEdit = !reviewing && !scanning && !relocatingSource;
            refresh.IsEnabled = !reviewing;
            edit.IsEnabled = canEdit && tracks.SelectedItems.Count > 0;
            all.IsEnabled = !reviewing && tracks.Items.Count > 0;
            copy.IsEnabled = !reviewing && tracks.SelectedItems.Count == 1;
            undo.IsEnabled = canEdit && undoBatchTags != null;
        }
        ListBox CreateList(string label)
        {
            var list = new ListBox { IsSynchronizedWithCurrentItem = false };
            list.SetResourceReference(BackgroundProperty, "SettingsCard");
            list.SetResourceReference(BorderBrushProperty, "SettingsStroke");
            list.SetResourceReference(ForegroundProperty, "Ink");
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            AutomationProperties.SetName(list, T(label));
            return list;
        }
        void AddCell(string property, string brush, bool bold)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(property));
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.FontSizeProperty, bold ? 13d : 11d);
            if (bold) text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            row.AppendChild(text);
        }
    }
}
