using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool ChangeHiddenTracks(IEnumerable<string> paths, bool hide)
    {
        var prior = library.HiddenTracks;
        library.HiddenTracks = new(prior, StringComparer.OrdinalIgnoreCase);
        if (hide) library.HiddenTracks.UnionWith(paths);
        else library.HiddenTracks.ExceptWith(paths);
        try { database?.Save(library, unsavedTracks); }
        catch (Exception error)
        {
            library.HiddenTracks = prior;
            Status.Text = error.Message;
            return false;
        }
        unsavedTracks = false;
        if (collection is { } page)
        {
            var members = page.Tracks.Where(track => !library.HiddenTracks.Contains(track.Path)).ToArray();
            collection = members.Length == 0 ? null : page with { Tracks = members };
        }
        libraryHistory.Clear();
        LibraryBackButton.IsEnabled = false;
        UpdateCollectionHeading();
        return true;
    }

    private void CompareDuplicates(TagIssue issue, Window owner)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = issue.Tracks,
            HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false, CanUserDeleteRows = false,
            BorderThickness = new Thickness(1), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        grid.SetResourceReference(BackgroundProperty, "SettingsCard");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SettingsCard");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "SettingsBackground");
        grid.SetResourceReference(ForegroundProperty, "Ink");
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("SettingsBackground")));
        header.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Ink")));
        grid.ColumnHeaderStyle = header;
        grid.RowStyle = new Style(typeof(DataGridRow));
        grid.RowStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Ink")));
        grid.Columns.Add(new DataGridTextColumn { Header = T("Formato"), Binding = new System.Windows.Data.Binding(nameof(Track.FormatLabel)), Width = 195 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Duración"), Binding = new System.Windows.Data.Binding(nameof(Track.DurationLabel)), Width = 86 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Ruta"), Binding = new System.Windows.Data.Binding(nameof(Track.Path)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var content = new DockPanel { Margin = new Thickness(20) };
        var title = HomeText(issue.Name, 21, translate: false, heading: true);
        title.Margin = new Thickness(0, 0, 0, 9);
        DockPanel.SetDock(title, Dock.Top);
        content.Children.Add(title);
        var close = new Button { Content = T("Cerrar"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        content.Children.Add(close);
        content.Children.Add(grid);
        PlaylistDialog(T("Comparar copias"), content, 840, 420, owner).ShowDialog();
    }

    private void ShowHiddenTracks(object sender, RoutedEventArgs args)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var restore = new Button { Content = T("Volver a mostrar"), IsEnabled = false };
        footer.Children.Add(new Button { Content = T("Cerrar"), IsCancel = true });
        footer.Children.Add(restore);
        var list = new ListBox { SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(Track.Path), Margin = new Thickness(0, 0, 0, 10) };
        list.SetResourceReference(BackgroundProperty, "SettingsCard");
        list.SetResourceReference(ForegroundProperty, "Ink");
        root.Children.Add(list);
        list.SelectionChanged += (_, _) => restore.IsEnabled = list.SelectedItems.Count > 0;
        restore.Click += (_, _) =>
        {
            if (ChangeHiddenTracks(list.SelectedItems.Cast<Track>().Select(track => track.Path), hide: false)) Refresh();
        };
        Refresh();
        var owner = sender is DependencyObject caller ? Window.GetWindow(caller) ?? this : this;
        PlaylistDialog(T("Pistas ocultas"), root, 730, 460, owner).ShowDialog();

        void Refresh() => list.ItemsSource = library.Tracks.Where(track => library.HiddenTracks.Contains(track.Path)).ToArray();
    }
}
