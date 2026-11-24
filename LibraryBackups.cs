using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace PrismaMusic;

internal sealed record LibraryBackup(string Name, DateTime Timestamp, bool BeforeRestore);

internal sealed partial class LibraryDatabase
{
    internal LibraryBackup[] Backups() => Directory.Exists(BackupDirectory)
        ? Directory.EnumerateFiles(BackupDirectory, "*.sqlite3").Select(path => new FileInfo(path))
            .Where(info => info.Name.StartsWith("library-", StringComparison.Ordinal) || info.Name.StartsWith("before-restore-", StringComparison.Ordinal))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new LibraryBackup(info.Name, info.LastWriteTime, info.Name.StartsWith("before-restore-", StringComparison.Ordinal))).ToArray()
        : Array.Empty<LibraryBackup>();

    internal Library ReadBackup(string name)
    {
        using var source = OpenBackup(name);
        return ReadLibrary(source, initialize: false);
    }

    internal Library RestoreBackup(string name)
    {
        using var source = OpenBackup(name);
        var recovered = ReadLibrary(source, initialize: false);
        var recoveredIndex = Index(recovered);
        using var active = Open();
        Directory.CreateDirectory(BackupDirectory);
        string previousName = $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.sqlite3";
        string previous = Path.Combine(BackupDirectory, previousName);
        string temporary = previous + ".tmp";
        try
        {
            using (var undo = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = temporary, Pooling = false }.ToString()))
            {
                undo.Open();
                active.BackupDatabase(undo);
            }
            File.Move(temporary, previous);
        }
        finally { File.Delete(temporary); }
        source.BackupDatabase(active);
        savedTracks = recoveredIndex;
        return recovered;
    }

    private SqliteConnection OpenBackup(string name)
    {
        if (name == null || Path.GetFileName(name) != name ||
            !Backups().Any(backup => backup.Name == name))
            throw new FileNotFoundException("No se encontró la copia de seguridad.", name);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(BackupDirectory, name),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            if (check.ExecuteScalar() as string != "ok") throw new InvalidDataException("La copia de seguridad está dañada.");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}

public partial class MainWindow
{
    private bool restoringBackup;

    private void ShowLibraryBackups(object sender, RoutedEventArgs args)
    {
        if (database == null || restoringBackup) return;
        Window owner = sender is DependencyObject caller ? Window.GetWindow(caller) ?? this : this;
        var panel = new DockPanel { Margin = new Thickness(20) };
        var picker = new ComboBox { Style = (Style)FindResource("FilterCombo") };
        var entries = database.Backups();
        picker.ItemsSource = entries.Select(entry =>
            $"{entry.Timestamp:g}  ·  {T(entry.BeforeRestore ? "Antes de restaurar" : "Copia diaria")}").ToArray();
        AutomationProperties.SetName(picker, T("Copias de seguridad"));
        DockPanel.SetDock(picker, Dock.Top);
        panel.Children.Add(picker);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var accept = new Button { Content = T("Restaurar copia"), IsEnabled = false };
        var exit = new Button { Content = T("Cerrar"), IsCancel = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(exit);
        actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        var feedback = SettingsHint("");
        footer.Children.Add(feedback);
        panel.Children.Add(footer);
        var overview = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };
        var count = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        overview.Children.Add(count);
        overview.Children.Add(SettingsHint("La restauración guarda una copia de tu biblioteca actual."));
        var sources = new ListBox();
        sources.SetResourceReference(BackgroundProperty, "SettingsCard");
        sources.SetResourceReference(ForegroundProperty, "Ink");
        overview.Children.Add(sources);
        panel.Children.Add(new ScrollViewer { Content = overview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var window = PlaylistDialog(T("Copias de seguridad"), panel, 650, 470, owner);
        LibraryBackup selected = null;
        window.Closing += (_, closing) => closing.Cancel = restoringBackup;
        picker.SelectionChanged += async (_, _) =>
        {
            int index = picker.SelectedIndex;
            selected = null;
            accept.IsEnabled = false;
            sources.ItemsSource = null;
            feedback.Text = "";
            if (index < 0) return;
            var target = entries[index];
            count.Text = T("Leyendo copia de seguridad…");
            try
            {
                var preview = await Task.Run(() => database.ReadBackup(target.Name));
                if (!window.IsVisible || picker.SelectedIndex != index) return;
                count.Text = LocalTextExtension.Format("{0:N0} pistas · {1:N0} listas · {2:N0} favoritas",
                    preview.Tracks.Count, preview.Playlists.Count + preview.SmartPlaylists.Count + (preview.Playlist.Count > 0 ? 1 : 0),
                    preview.Favorites.Count);
                sources.ItemsSource = preview.Folders;
                selected = target;
                accept.IsEnabled = true;
            }
            catch (Exception error)
            {
                if (window.IsVisible && picker.SelectedIndex == index) count.Text = error.Message;
            }
        };

        accept.Click += async (_, _) =>
        {
            if (selected is not { } target || restoringBackup) return;
            if (scanning || relocatingSource || editingAlbum)
            {
                feedback.Text = T("Espera a que termine la operación de biblioteca.");
                return;
            }
            if (MessageBox.Show(window, T("¿Restaurar esta copia de la biblioteca?"), T("Restaurar copia"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            restoringBackup = true;
            picker.IsEnabled = accept.IsEnabled = exit.IsEnabled = false;
            feedback.Text = T("Restaurando biblioteca…");
            try
            {
                if (unsavedTracks) database.Save(library, tracksChanged: true);
                SaveSession();
                CancelPendingOpen();
                InvalidateGapless();
                player.Pause();
                SetPlaying(false);
                var recovered = await Task.Run(() => database.RestoreBackup(target.Name));
                library = recovered;
                unsavedTracks = false;
                ClearForLibraryRestore();
                await RestoreSession();
                Status.Text = T("Biblioteca restaurada.");
            }
            catch (Exception error)
            {
                feedback.Text = error.Message;
                return;
            }
            finally
            {
                restoringBackup = false;
                picker.IsEnabled = accept.IsEnabled = exit.IsEnabled = true;
            }
            window.Close();
        };

        window.Loaded += (_, _) =>
        {
            if (entries.Length == 0) count.Text = T("No hay copias disponibles.");
            else picker.SelectedIndex = 0;
        };
        window.ShowDialog();
    }

    private void ClearForLibraryRestore()
    {
        player.Close();
        opened = false;
        current = null;
        CurrentTrackPath = null;
        paletteArtwork = null;
        Cover.Source = null;
        Now.Text = T("Sin pista");
        Artist.Text = TransportArtist.Text = AudioFormat.Text = "";
        SetLyrics("");
        playbackRetry = null;
        RetryTrackButton.Visibility = NasRecoveryNotice.Visibility = Visibility.Collapsed;
        queue.Clear();
        previous.Clear();
        context = originalContext = Array.Empty<Track>();
        collection = null;
        artRequest = null;
        libraryHistory.Clear();
        LibraryBackButton.IsEnabled = false;
        selectedLibrarySource = null;
        playlistName = "Mi lista";
        trackThumbnails.Clear();
        UpdateMini();
        RefreshFilters();
        UpdatePlaylistPicker();
        RefreshLibrarySources();
        UpdateCollectionHeading();
    }
}
