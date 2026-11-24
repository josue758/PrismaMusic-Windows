using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record PlaylistPreviewRow(string Title, string Detail);
    private void ShowPlaylistActions(object sender, RoutedEventArgs args)
    {
        EditSmartPlaylistButton.Visibility = SmartRules == null ? Visibility.Collapsed : Visibility.Visible;
        RenamePlaylistMenu.IsEnabled = DeletePlaylistMenu.IsEnabled = playlistName != "Mi lista";
        var menu = PlaylistActionsButton.ContextMenu;
        menu.PlacementTarget = PlaylistActionsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void RenamePlaylist(object sender, RoutedEventArgs args)
    {
        if (playlistName == "Mi lista") return;
        string source = playlistName;
        if (AskPlaylistName(T("Renombrar lista"), source, source) is not { } name) return;
        var rule = SmartRules;
        var contents = rule == null ? ActivePlaylist.ToList() : null;
        ChangePlaylists(name, (manual, smart) =>
        {
            manual.Remove(source);
            smart.Remove(source);
            if (rule != null) smart.Add(name, rule);
            else manual.Add(name, contents);
        });
    }

    private void DuplicatePlaylist(object sender, RoutedEventArgs args)
    {
        if (AskPlaylistName(T("Duplicar lista"), LocalTextExtension.Format("Copia de {0}", T(playlistName))) is not { } name) return;
        var rule = SmartRules;
        var contents = rule == null ? ActivePlaylist.ToList() : null;
        ChangePlaylists(name, (manual, smart) =>
        {
            if (rule != null) smart.Add(name, rule);
            else manual.Add(name, contents);
        });
    }

    private void DeletePlaylist(object sender, RoutedEventArgs args)
    {
        string source = playlistName;
        if (source == "Mi lista" || MessageBox.Show(this, LocalTextExtension.Format("¿Eliminar la lista «{0}»?", source),
            T("Eliminar lista"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ChangePlaylists("Mi lista", (manual, smart) => { manual.Remove(source); smart.Remove(source); });
    }

    private bool PlaylistNameFree(string name, string former = null) =>
        name.Length != 0 && !PlaylistPicker.Items.Cast<string>().Any(entry =>
            string.Equals(entry, name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry, former, StringComparison.OrdinalIgnoreCase));

    private bool ChangePlaylists(string active, Action<Dictionary<string, List<string>>, Dictionary<string, SmartPlaylistRules>> change)
    {
        var formerManual = library.Playlists;
        var formerSmart = library.SmartPlaylists;
        var manual = new Dictionary<string, List<string>>(formerManual, StringComparer.Ordinal);
        var smart = new Dictionary<string, SmartPlaylistRules>(formerSmart, StringComparer.OrdinalIgnoreCase);
        try
        {
            change(manual, smart);
            library.Playlists = manual;
            library.SmartPlaylists = smart;
            database?.Save(library, unsavedTracks);
        }
        catch (Exception error)
        {
            library.Playlists = formerManual;
            library.SmartPlaylists = formerSmart;
            Status.Text = error.Message;
            return false;
        }
        unsavedTracks = false;
        playlistName = active;
        UpdatePlaylistPicker();
        Navigation.SelectedItem = PlaylistNavigation;
        RefreshRows();
        return true;
    }

    private string AskPlaylistName(string title, string initial, string former = null)
    {
        var body = new StackPanel { Margin = new Thickness(22) };
        var input = PlaylistTextBox(initial);
        body.Children.Add(SettingsHint("Nombre de la lista"));
        body.Children.Add(input);
        var message = SettingsHint("");
        body.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var confirm = new Button { Content = T("Guardar"), IsDefault = true };
        buttons.Children.Add(new Button { Content = T("Cancelar"), IsCancel = true });
        buttons.Children.Add(confirm);
        body.Children.Add(buttons);
        var window = PlaylistDialog(title, body, 460, 220);
        window.SizeToContent = SizeToContent.Height;
        confirm.Click += (_, _) =>
        {
            if (!PlaylistNameFree(input.Text.Trim(), former)) { message.Text = T("Escribe un nombre distinto para la lista."); return; }
            window.DialogResult = true;
        };
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private Window PlaylistDialog(string title, FrameworkElement body, double width, double height, Window owner = null)
    {
        var root = new Grid();
        root.SetResourceReference(BackgroundProperty, "SettingsBackground");
        root.Children.Add(body);
        var window = new Window { Owner = owner ?? this, Title = title, Content = root, Width = width,
            Height = Math.Min(height, SystemParameters.WorkArea.Height - 24), ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        window.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(window, root);
        return window;
    }

    private TextBox PlaylistTextBox(string initial, int limit = 80)
    {
        var input = new TextBox { Text = initial, MaxLength = limit, Padding = new Thickness(8, 4, 8, 4),
            Style = (Style)FindResource("LibrarySearch"), MinHeight = 32, Margin = new Thickness(0, 0, 0, 7) };
        input.SetResourceReference(BackgroundProperty, "ControlFill");
        input.SetResourceReference(ForegroundProperty, "Ink");
        input.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        return input;
    }

    private async void ImportPlaylist(object sender, RoutedEventArgs args)
    {
        var picker = new OpenFileDialog { Filter = "M3U / M3U8|*.m3u;*.m3u8", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await OpenPlaylistImportPreview(picker.FileName);
    }

    private async Task OpenPlaylistImportPreview(string filename)
    {
        var tracks = library.Tracks.Where(track => !library.HiddenTracks.Contains(track.Path)).ToArray();
        string[] entries;
        try { entries = await Task.Run(() => PlaylistImport.ReadEntries(filename)); }
        catch (Exception error) { Status.Text = error.Message; return; }
        if (!IsLoaded) return;

        var panel = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = T("Importar lista"), FontSize = 25,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        header.Children.Add(SettingsHint("Nombre de la lista"));
        var name = PlaylistTextBox(Path.GetFileNameWithoutExtension(filename));
        header.Children.Add(name);
        var rootPaths = new Grid();
        rootPaths.ColumnDefinitions.Add(new ColumnDefinition());
        rootPaths.ColumnDefinitions.Add(new ColumnDefinition());
        var previous = AddPath("Ruta anterior del NAS", 0);
        var next = AddPath("Ruta actual del NAS", 1);
        var remap = new DockPanel();
        var check = new Button { Content = T("Revisar rutas"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(check, Dock.Right);
        remap.Children.Add(check);
        remap.Children.Add(rootPaths);
        header.Children.Add(new Expander { Header = T("Cambiar rutas del NAS"), Content = remap, Margin = new Thickness(0, 5, 0, 9) });
        var count = SettingsHint("");
        header.Children.Add(count);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        var import = new Button { Content = T("Importar lista"), IsDefault = true };
        import.SetResourceReference(BackgroundProperty, "AccentBrush");
        import.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        buttons.Children.Add(new Button { Content = T("Cancelar"), IsCancel = true });
        buttons.Children.Add(import);
        footer.Children.Add(buttons);
        var warning = SettingsHint("");
        footer.Children.Add(warning);
        var results = new ListBox { BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        results.Resources.Add(typeof(ScrollBar), new Style(typeof(ScrollBar), (Style)FindResource("ThinVerticalScrollBar")));
        var previewRow = new FrameworkElementFactory(typeof(StackPanel));
        previewRow.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 3, 3, 4));
        var titleLine = new FrameworkElementFactory(typeof(TextBlock));
        titleLine.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        titleLine.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PlaylistPreviewRow.Title)));
        previewRow.AppendChild(titleLine);
        var details = new FrameworkElementFactory(typeof(TextBlock));
        details.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        details.SetValue(TextBlock.FontSizeProperty, 11.0);
        details.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        details.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PlaylistPreviewRow.Detail)));
        previewRow.AppendChild(details);
        results.ItemTemplate = new DataTemplate { VisualTree = previewRow };
        results.SetResourceReference(BackgroundProperty, "SettingsCard");
        results.SetResourceReference(ForegroundProperty, "Ink");
        results.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        panel.Children.Add(results);
        var window = PlaylistDialog(T("Importar lista"), panel, 760, 570);
        PlaylistMatch[] matches = null;
        bool pathsChanged = false;
        check.Click += (_, _) => Preview();
        previous.TextChanged += (_, _) => { pathsChanged = true; import.IsEnabled = false; };
        next.TextChanged += (_, _) => { pathsChanged = true; import.IsEnabled = false; };
        import.Click += (_, _) =>
        {
            if (pathsChanged || matches == null) return;
            string title = name.Text.Trim();
            if (!PlaylistNameFree(title)) { warning.Text = T("Escribe un nombre distinto para la lista."); return; }
            var imported = matches.Where(match => match.Track != null).Select(match => match.Track.Path).ToList();
            if (ChangePlaylists(title, (manual, _) => manual.Add(title, imported))) window.DialogResult = true;
            else warning.Text = Status.Text;
        };
        Preview();
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_PLAYLIST_IMPORT_PREVIEW") != null)
            window.ContentRendered += (_, _) =>
            {
                try
                {
                    if (!import.IsEnabled || matches.Count(match => match.Track != null) != 2 || matches.Length != 3)
                        throw new InvalidOperationException("The playlist-import preview did not distinguish matched and missing rows.");
                    CaptureDesignPreview((Grid)window.Content, "PRISMA_PLAYLIST_IMPORT_PREVIEW");
                }
                finally { window.Close(); }
            };
        window.ShowDialog();

        TextBox AddPath(string label, int column)
        {
            var group = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };
            var input = PlaylistTextBox("", 32767);
            group.Children.Add(SettingsHint(label));
            group.Children.Add(input);
            Grid.SetColumn(group, column);
            rootPaths.Children.Add(group);
            return input;
        }

        void Preview()
        {
            matches = PlaylistImport.Match(entries, Path.GetDirectoryName(filename), tracks, previous.Text, next.Text);
            pathsChanged = false;
            int found = matches.Count(match => match.Track != null);
            count.Text = LocalTextExtension.Format("{0:N0} encontradas · {1:N0} con nueva ruta · {2:N0} faltantes",
                found, matches.Count(match => match.Remapped), matches.Length - found);
            results.ItemsSource = matches.Select(match => match.Track == null
                ? new PlaylistPreviewRow(T("Falta"), match.Original) : match.Remapped
                    ? new PlaylistPreviewRow($"{match.Track.Name}  ·  {T("Ruta actualizada")}", $"{match.Original} → {match.Track.Path}")
                    : new PlaylistPreviewRow(match.Track.Name, match.Track.Artist)).ToArray();
            import.IsEnabled = found > 0;
        }
    }
}
