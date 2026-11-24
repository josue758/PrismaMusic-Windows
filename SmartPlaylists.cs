using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PrismaMusic;

public sealed record SmartPlaylistRules(string Search = "", string Artist = "", string Genre = "", string Codec = "",
    int SampleRate = 0, bool Favorites = false, bool Unplayed = false, bool AddedThisMonth = false)
{
    internal bool Matches(Track track, Library library, HashSet<string> history, DateTimeOffset now) =>
        (Artist.Length == 0 || string.Equals(Artist, track.Artist, StringComparison.CurrentCultureIgnoreCase)) &&
        (Genre.Length == 0 || string.Equals(Genre, track.Genre, StringComparison.CurrentCultureIgnoreCase)) &&
        (Codec.Length == 0 || string.Equals(Codec, track.Codec, StringComparison.OrdinalIgnoreCase)) &&
        (SampleRate == 0 || SampleRate == track.SampleRate) &&
        (!Favorites || library.Favorites.Contains(track.Path)) &&
        (!Unplayed || library.PlayCounts.GetValueOrDefault(track.Path) == 0 && history?.Contains(track.Path) != true) &&
        (!AddedThisMonth || track.AddedAt is { } date && date <= now &&
            date.ToLocalTime().Month == now.Month && date.ToLocalTime().Year == now.Year);
}

public partial class MainWindow
{
    private sealed record PlaylistChoice(string Value, string Label);

    private SmartPlaylistRules SmartRules => library.SmartPlaylists.GetValueOrDefault(playlistName);
    private IEnumerable<Track> SmartPlaylistTracks(SmartPlaylistRules rules)
    {
        var now = DateTimeOffset.Now;
        var query = new LibraryQuery(rules.Search);
        var history = rules.Unplayed ? library.History.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        return library.Tracks.Where(track => rules.Matches(track, library, history, now) && (query.IsEmpty || query.Matches(track)));
    }

    private void NewSmartPlaylist(object sender, RoutedEventArgs args) => OpenSmartPlaylistEditor(null);
    private void EditSmartPlaylist(object sender, RoutedEventArgs args) => OpenSmartPlaylistEditor(playlistName);

    private void OpenSmartPlaylistEditor(string existingName)
    {
        var original = existingName == null ? new SmartPlaylistRules() : library.SmartPlaylists.GetValueOrDefault(existingName);
        if (original == null) return;
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = T(existingName == null ? "Nueva lista inteligente" : "Editar lista inteligente"),
            FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        var fields = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(fields, 1);
        root.Children.Add(fields);
        var name = TextField("Nombre de la lista", existingName ?? "");
        var search = TextField("Buscar música", original.Search);
        var artist = Picker("Artista", library.Tracks.Select(track => track.Artist).Append(original.Artist).Order(StringComparer.CurrentCultureIgnoreCase), original.Artist);
        var genre = Picker("Género", library.Tracks.Select(track => track.Genre).Append(original.Genre).Order(StringComparer.CurrentCultureIgnoreCase), original.Genre);
        var codec = Picker("Formato", library.Tracks.Select(track => track.Codec).Append(original.Codec).Order(StringComparer.CurrentCultureIgnoreCase), original.Codec);
        var sampleRate = Picker("Frecuencia", library.Tracks.Select(track => track.SampleRate).Append(original.SampleRate)
            .Where(rate => rate > 0).Distinct().Order().Select(rate => rate.ToString(CultureInfo.InvariantCulture)),
            original.SampleRate == 0 ? "" : original.SampleRate.ToString(CultureInfo.InvariantCulture),
            rate => $"{int.Parse(rate, CultureInfo.InvariantCulture) / 1000.0:0.###} kHz");
        var favorites = Toggle("Favoritos", original.Favorites);
        var unplayed = Toggle("Sin reproducir", original.Unplayed);
        var recent = Toggle("Añadidas este mes", original.AddedThisMonth);
        var preview = new DockPanel();
        Grid.SetRow(preview, 2);
        root.Children.Add(preview);
        var total = SettingsHint("");
        AutomationProperties.SetLiveSetting(total, AutomationLiveSetting.Polite);
        DockPanel.SetDock(total, Dock.Top);
        preview.Children.Add(total);
        var matches = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(3),
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        matches.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),
            new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), (Style)FindResource("ThinVerticalScrollBar")));
        ScrollViewer.SetHorizontalScrollBarVisibility(matches, ScrollBarVisibility.Disabled);
        matches.SetResourceReference(BackgroundProperty, "SettingsCard");
        matches.SetResourceReference(ForegroundProperty, "Ink");
        matches.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        AutomationProperties.SetName(matches, T("Vista previa"));
        var previewCard = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Child = matches };
        previewCard.SetResourceReference(Border.BackgroundProperty, "SettingsCard");
        previewCard.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        preview.Children.Add(previewCard);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        var validation = SettingsHint("");
        footer.Children.Add(validation);
        var cancel = new Button { Content = T("Cancelar"), IsCancel = true };
        var save = new Button { Content = T("Guardar"), IsDefault = true };
        save.SetResourceReference(BackgroundProperty, "AccentBrush");
        save.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        var surface = new Grid();
        surface.SetResourceReference(BackgroundProperty, "SettingsBackground");
        surface.Children.Add(root);
        var dialog = new Window { Owner = this, Title = T(existingName == null ? "Nueva lista inteligente" : "Editar reglas"),
            Width = 665, Height = Math.Min(630, SystemParameters.WorkArea.Height - 22), MinWidth = 520,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = surface };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);

        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        dialog.Closed += (_, _) => debounce.Stop();
        search.TextChanged += (_, _) => Schedule();
        foreach (var choice in new[] { artist, genre, codec, sampleRate }) choice.SelectionChanged += (_, _) => Schedule();
        foreach (var checkbox in new[] { favorites, unplayed, recent }) checkbox.Click += (_, _) => Schedule();
        save.Click += (_, _) =>
        {
            string title = name.Text.Trim();
            bool occupied = PlaylistPicker.Items.Cast<string>().Any(entry => string.Equals(entry, title, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry, existingName, StringComparison.OrdinalIgnoreCase));
            if (title.Length == 0 || occupied) { validation.Text = T("Escribe un nombre distinto para la lista."); return; }
            var previous = library.SmartPlaylists;
            var updated = new Dictionary<string, SmartPlaylistRules>(previous, StringComparer.OrdinalIgnoreCase);
            if (existingName != null) updated.Remove(existingName);
            updated[title] = Rules();
            library.SmartPlaylists = updated;
            try { database?.Save(library, unsavedTracks); }
            catch (Exception error)
            {
                library.SmartPlaylists = previous;
                validation.Text = error.Message;
                return;
            }
            unsavedTracks = false;
            playlistName = title;
            UpdatePlaylistPicker();
            Navigation.SelectedItem = PlaylistNavigation;
            RefreshRows();
            dialog.DialogResult = true;
        };
        Refresh();
        dialog.Loaded += (_, _) => name.Focus();
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_SMART_PLAYLIST_PREVIEW") != null)
            dialog.ContentRendered += (_, _) =>
            {
                try
                {
                    if (matches.Items.Count != 1) throw new InvalidOperationException("Smart playlist preview did not match the FLAC rule.");
                    CaptureDesignPreview(surface, "PRISMA_SMART_PLAYLIST_PREVIEW");
                }
                finally { dialog.Close(); }
            };
        dialog.ShowDialog();

        SmartPlaylistRules Rules() => new(search.Text.Trim(), Value(artist), Value(genre), Value(codec),
            int.TryParse(Value(sampleRate), CultureInfo.InvariantCulture, out int rate) ? rate : 0,
            favorites.IsChecked == true, unplayed.IsChecked == true, recent.IsChecked == true);
        static string Value(ComboBox choice) => choice.SelectedIndex > 0 ? (string)choice.SelectedValue : "";
        void Schedule() { debounce.Stop(); debounce.Start(); }
        void Refresh()
        {
            var found = SmartPlaylistTracks(Rules()).ToArray();
            total.Text = found.Length == 1 ? T("1 coincidencia") : LocalTextExtension.Format("{0:N0} coincidencias", found.Length);
            if (found.Length > 100) total.Text += "  ·  " + T("Vista previa de hasta 100 canciones");
            matches.ItemsSource = found.Take(100).Select(track => $"{track.Name}  ·  {track.Artist}").ToArray();
        }

        TextBox TextField(string label, string initial)
        {
            var input = new TextBox { Text = initial, MaxLength = 80, Style = (Style)FindResource("LibrarySearch"),
                Height = 34, Padding = new Thickness(9, 2, 9, 2) };
            input.SetResourceReference(BackgroundProperty, "ControlFill");
            input.SetResourceReference(ForegroundProperty, "Ink");
            input.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
            Field(label, input);
            return input;
        }

        ComboBox Picker(string label, IEnumerable<string> values, string selected, Func<string, string> format = null)
        {
            var options = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Select(value => new PlaylistChoice(value, format?.Invoke(value) ?? value))
                .Prepend(new PlaylistChoice("", T("Cualquiera"))).ToArray();
            var picker = new ComboBox { ItemsSource = options,
                DisplayMemberPath = nameof(PlaylistChoice.Label), SelectedValuePath = nameof(PlaylistChoice.Value),
                Height = 34, Style = (Style)FindResource("FilterCombo") };
            picker.SelectedValue = selected;
            Field(label, picker);
            return picker;
        }

        CheckBox Toggle(string label, bool value)
        {
            var item = new CheckBox { Content = T(label), IsChecked = value, Margin = new Thickness(0, 5, 14, 4),
                Style = (Style)FindResource("SettingsToggle"), MinWidth = 170 };
            fields.Children.Add(item);
            return item;
        }

        void Field(string label, Control control)
        {
            var group = new StackPanel { Width = 190, Margin = new Thickness(0, 0, 9, 8) };
            var caption = new TextBlock { Text = T(label), Margin = new Thickness(0, 0, 0, 4) };
            AutomationProperties.SetLabeledBy(control, caption);
            group.Children.Add(caption);
            group.Children.Add(control);
            fields.Children.Add(group);
        }
    }
}
