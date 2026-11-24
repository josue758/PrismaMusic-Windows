using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

internal static class LibraryRelocation
{
    internal static Dictionary<string, string> Verify(string source, string target, IEnumerable<string> paths, Func<string, bool> exists,
        CancellationToken token = default, IProgress<(int Verified, int Total)> progress = null)
    {
        token.ThrowIfCancellationRequested();
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (Contains(source, target) || Contains(target, source))
            throw new InvalidOperationException(LocalTextExtension.Translate("Elige una ubicación distinta y fuera de la fuente anterior."));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = paths as string[] ?? paths.ToArray();
        var affected = knownPaths.Where(path => Contains(source, path)).ToArray();
        progress?.Report((0, affected.Length));
        long lastReport = Environment.TickCount64;
        foreach (var path in affected)
        {
            token.ThrowIfCancellationRequested();
            string replacement = string.Equals(source, path, StringComparison.OrdinalIgnoreCase)
                ? target : Path.Combine(target, Path.GetRelativePath(source, path));
            bool found = exists(replacement);
            token.ThrowIfCancellationRequested();
            if (!found)
                throw new IOException(LocalTextExtension.Format("No se encontró el archivo en la nueva fuente: {0}", replacement));
            result.Add(path, replacement);
            if (result.Count == affected.Length || Environment.TickCount64 - lastReport >= 100)
            {
                progress?.Report((result.Count, affected.Length));
                lastReport = Environment.TickCount64;
            }
        }
        if (result.Count == 0) throw new InvalidOperationException(LocalTextExtension.Translate("La fuente no tiene pistas para reubicar."));
        var replacements = result.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (knownPaths.Any(path => !result.ContainsKey(path) && replacements.Contains(path)))
            throw new InvalidOperationException(LocalTextExtension.Translate("La nueva ubicación ya contiene pistas indexadas."));
        return result;
    }

    internal static Library Apply(Library library, string source, string target, IReadOnlyDictionary<string, string> paths)
    {
        string Map(string path) => paths.TryGetValue(path, out var replacement) ? replacement : path;
        return new Library
        {
            Folders = library.Folders.Select(folder => string.Equals(folder, source, StringComparison.OrdinalIgnoreCase) ? target :
                Contains(Path.TrimEndingDirectorySeparator(source), folder) ? Path.Combine(target, Path.GetRelativePath(source, folder)) : folder)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Tracks = library.Tracks.Select(track => paths.TryGetValue(track.Path, out var path) ? track with { Path = path } : track).ToList(),
            Favorites = new HashSet<string>(library.Favorites.Select(Map), StringComparer.OrdinalIgnoreCase),
            Playlist = library.Playlist.Select(Map).ToList(),
            Playlists = library.Playlists.ToDictionary(pair => pair.Key, pair => pair.Value.Select(Map).ToList(), library.Playlists.Comparer),
            SmartPlaylists = new(library.SmartPlaylists, StringComparer.OrdinalIgnoreCase),
            History = library.History.Select(Map).ToList(),
            PlayCounts = library.PlayCounts.ToDictionary(pair => Map(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            TagEdits = library.TagEdits.ToDictionary(pair => Map(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool Contains(string source, string path) => string.Equals(source, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

}

public partial class MainWindow
{
    private bool relocatingSource;

    private void RelocateLibrarySource(string source)
    {
        if (scanning || relocatingSource) return;
        bool singleFile = library.Tracks.Any(track => string.Equals(track.Path, source, StringComparison.OrdinalIgnoreCase));
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(SettingsHint("Elige la nueva ubicación o pega una ruta de red. Prisma comprobará los archivos antes de aplicar el cambio."));
        var destination = new TextBox { Text = source, Margin = new Thickness(0, 10, 0, 8), Padding = new Thickness(8),
            Style = (Style)FindResource("LibrarySearch") };
        destination.SetResourceReference(BackgroundProperty, "SettingsCard");
        destination.SetResourceReference(ForegroundProperty, "Ink");
        destination.SetResourceReference(BorderBrushProperty, "WorkspaceStroke");
        var location = new DockPanel();
        var buttons = (Style)FindResource(typeof(Button));
        var browse = new Button { Content = T("Examinar…"), Margin = new Thickness(8, 10, 0, 8), Style = buttons };
        browse.SetResourceReference(BackgroundProperty, "SettingsCard");
        DockPanel.SetDock(browse, Dock.Right);
        location.Children.Add(browse);
        location.Children.Add(destination);
        root.Children.Add(location);
        System.Windows.Automation.AutomationProperties.SetName(destination, T("Nueva ubicación"));
        var progressBar = new ProgressBar { Height = 4, Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
        progressBar.SetResourceReference(ForegroundProperty, "AccentBrush");
        root.Children.Add(progressBar);
        System.Windows.Automation.AutomationProperties.SetName(progressBar, T("Comprobando la nueva ubicación…"));
        var feedback = SettingsHint("");
        root.Children.Add(feedback);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(feedback, System.Windows.Automation.AutomationLiveSetting.Polite);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = T("Cancelar"), Style = buttons };
        var verify = new Button { Content = T("Verificar ubicación"), IsDefault = true, Style = buttons };
        var move = new Button { Content = T("Aplicar reubicación"), IsEnabled = false, Style = buttons };
        cancel.SetResourceReference(BackgroundProperty, "SettingsCard");
        verify.SetResourceReference(BackgroundProperty, "SettingsCard");
        move.SetResourceReference(BackgroundProperty, "AccentBrush");
        move.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        var surface = new Grid();
        var panel = new Border { Child = root };
        panel.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SettingsBackground");
        surface.Children.Add(panel);
        var dialog = new Window { Owner = this, Title = T("Reubicar fuente"), Width = 610, SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = surface };
        CancellationTokenSource verification = null;
        (string Target, Dictionary<string, string> Mapping, List<Track> Tracks, int TrackCount)? verified = null;
        browse.Click += (_, _) =>
        {
            if (singleFile)
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = T("Nueva ubicación"), CheckFileExists = true,
                    Filter = T("Archivos de audio") + "|" + LibraryScanner.AudioFilePattern
                };
                if (picker.ShowDialog(dialog) == true) destination.Text = picker.FileName;
            }
            else
            {
                using var picker = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = false, Description = T("Nueva ubicación") };
                if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) destination.Text = picker.SelectedPath;
            }
        };
        destination.TextChanged += (_, _) => ResetVerified();
        dialog.Closed += (_, _) => verification?.Cancel();
        cancel.Click += (_, _) =>
        {
            if (verification != null) CancelVerification();
            else dialog.Close();
        };
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            if (verification != null) CancelVerification();
            else dialog.Close();
            args.Handled = true;
        };
        verify.Click += async (_, _) =>
        {
            if (scanning || relocatingSource) return;
            ResetVerified();
            using var attempt = new CancellationTokenSource();
            verification = attempt;
            verify.IsEnabled = destination.IsEnabled = browse.IsEnabled = false;
            relocatingSource = true;
            cancel.Content = T("Cancelar verificación");
            feedback.Text = T("Comprobando la nueva ubicación…");
            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            try
            {
                string input = destination.Text.Trim();
                if (!Path.IsPathFullyQualified(input) && !input.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("Escribe una ruta completa."));
                var token = attempt.Token;
                string target = await Task.Run(() => Path.TrimEndingDirectorySeparator(Path.GetFullPath(NetworkMusicSource.Normalize(input) ?? input)), token)
                    .WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (library.Folders.Contains(target, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("La nueva ubicación ya contiene pistas indexadas."));
                var tracks = library.Tracks;
                var paths = tracks.Select(track => track.Path).ToArray();
                var progress = new Progress<(int Verified, int Total)>(value =>
                {
                    if (!ReferenceEquals(verification, attempt) || attempt.IsCancellationRequested || !dialog.IsVisible) return;
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = Math.Max(1, value.Total);
                    progressBar.Value = value.Verified;
                    feedback.Text = LocalTextExtension.Format("{0:N0} de {1:N0} archivos verificados.", value.Verified, value.Total);
                });
                var mapping = await Task.Run(() =>
                {
                    var directories = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    return LibraryRelocation.Verify(source, target, paths, path =>
                    {
                        if (singleFile) return File.Exists(path);
                        string directory = Path.GetDirectoryName(path);
                        if (!directories.TryGetValue(directory, out var entries))
                        {
                            entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string entry in Directory.EnumerateFiles(directory))
                            {
                                token.ThrowIfCancellationRequested();
                                entries.Add(entry);
                            }
                            directories[directory] = entries;
                        }
                        return entries.Contains(path);
                    }, token, progress);
                }, token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (!dialog.IsVisible) return;
                verified = (target, mapping, tracks, paths.Length);
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = mapping.Count;
                progressBar.Value = mapping.Count;
                feedback.Text = mapping.Count == 1 ? T("Se encontró 1 archivo. Puedes aplicar la reubicación.")
                    : LocalTextExtension.Format("Se encontraron {0:N0} archivos. Puedes aplicar la reubicación.", mapping.Count);
                move.IsEnabled = true;
                verify.IsDefault = false;
                move.IsDefault = true;
                move.Focus();
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                feedback.Text = T("Verificación cancelada.");
                progressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                feedback.Text = error.GetBaseException().Message;
                progressBar.Visibility = Visibility.Collapsed;
            }
            finally
            {
                verification = null;
                relocatingSource = false;
                verify.IsEnabled = destination.IsEnabled = browse.IsEnabled = true;
                cancel.Content = T("Cancelar");
            }
        };
        move.Click += (_, _) =>
        {
            if (scanning || relocatingSource || verified is not { } result) return;
            if (!ReferenceEquals(library.Tracks, result.Tracks) || library.Tracks.Count != result.TrackCount ||
                !library.Folders.Contains(source, StringComparer.OrdinalIgnoreCase) ||
                library.Folders.Contains(result.Target, StringComparer.OrdinalIgnoreCase))
            {
                ResetVerified();
                feedback.Text = T("La biblioteca cambió. Verifica la ubicación otra vez.");
                return;
            }
            var (target, mapping, _, _) = result;
            try
            {
                var next = LibraryRelocation.Apply(library, source, target, mapping);
                database.Save(next, true, mapping);
                library = next;
                if (pendingTrack != null && mapping.ContainsKey(pendingTrack.Path)) CancelPendingOpen();
                RebindPlaybackTracks(Remap);
                if (undoBatchTags != null)
                    undoBatchTags = undoBatchTags.Select(item => item with { Before = Remap(item.Before), After = Remap(item.After) }).ToArray();
                RefreshFilters();
                ShowLibrarySource(target);
                libraryHistory.Clear();
                LibraryBackButton.IsEnabled = false;
                if (!smokeTest) SaveSession();
                Status.Text = LocalTextExtension.Format("Fuente reubicada · {0:N0} pistas.", mapping.Count);
                dialog.DialogResult = true;

                Track Remap(Track track) => track != null && mapping.TryGetValue(track.Path, out var replacement)
                    ? track with { Path = replacement } : track;
            }
            catch (Exception error) { feedback.Text = error.GetBaseException().Message; }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(verify);
        actions.Children.Add(move);
        root.Children.Add(actions);
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);
        dialog.ShowDialog();

        void ResetVerified()
        {
            verified = null;
            move.IsEnabled = false;
            move.IsDefault = false;
            verify.IsDefault = true;
            progressBar.Visibility = Visibility.Collapsed;
            feedback.Text = "";
        }

        void CancelVerification()
        {
            verification.Cancel();
            feedback.Text = T("Cancelando verificación…");
        }
    }
}
