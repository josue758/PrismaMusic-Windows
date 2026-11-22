using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

internal static class LibraryRelocation
{
    internal static Dictionary<string, string> Verify(string source, string target, IEnumerable<string> paths, Func<string, bool> exists)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (Contains(source, target) || Contains(target, source))
            throw new InvalidOperationException(LocalTextExtension.Translate("Elige una ubicación distinta y fuera de la fuente anterior."));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(path => Contains(source, path)))
        {
            string replacement = string.Equals(source, path, StringComparison.OrdinalIgnoreCase)
                ? target : Path.Combine(target, Path.GetRelativePath(source, path));
            if (!exists(replacement))
                throw new IOException(LocalTextExtension.Format("No se encontró el archivo en la nueva fuente: {0}", replacement));
            result.Add(path, replacement);
        }
        if (result.Count == 0) throw new InvalidOperationException(LocalTextExtension.Translate("La fuente no tiene pistas para reubicar."));
        var replacements = result.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paths.Any(path => !result.ContainsKey(path) && replacements.Contains(path)))
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
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(SettingsHint("Escribe la nueva ruta de la fuente. Prisma comprobará todos los archivos antes de guardar el cambio."));
        var destination = new TextBox { Text = source, Margin = new Thickness(0, 10, 0, 8), Padding = new Thickness(8),
            Style = (Style)FindResource("LibrarySearch") };
        destination.SetResourceReference(BackgroundProperty, "ControlFill");
        destination.SetResourceReference(ForegroundProperty, "Ink");
        root.Children.Add(destination);
        var feedback = SettingsHint("");
        root.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = T("Cancelar"), IsCancel = true };
        Window dialog = null;
        var move = new Button { Content = T("Verificar y reubicar"), IsDefault = true };
        move.Click += async (_, _) =>
        {
            if (scanning || relocatingSource) return;
            move.IsEnabled = destination.IsEnabled = cancel.IsEnabled = false;
            relocatingSource = true;
            feedback.Text = T("Comprobando la nueva ubicación…");
            try
            {
                string input = destination.Text.Trim();
                if (!Path.IsPathFullyQualified(input) && !input.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("Escribe una ruta completa."));
                string target = await Task.Run(() => Path.TrimEndingDirectorySeparator(Path.GetFullPath(NetworkMusicSource.Normalize(input) ?? input)));
                if (library.Folders.Contains(target, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("La nueva ubicación ya contiene pistas indexadas."));
                var paths = library.Tracks.Select(track => track.Path).ToArray();
                var mapping = await Task.Run(() =>
                {
                    var directories = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    return LibraryRelocation.Verify(source, target, paths, path =>
                    {
                        string directory = Path.GetDirectoryName(path);
                        if (!directories.TryGetValue(directory, out var entries))
                            directories[directory] = entries = Directory.EnumerateFiles(directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        return entries.Contains(path);
                    });
                });
                if (dialog?.IsVisible != true) return;
                var next = LibraryRelocation.Apply(library, source, target, mapping);
                database.Save(next, true);
                library = next;
                if (pendingTrack != null && mapping.ContainsKey(pendingTrack.Path)) CancelPendingOpen();
                for (int index = 0; index < queue.Count; index++) queue[index] = Remap(queue[index]);
                context = context.Select(Remap).ToArray();
                originalContext = originalContext.Select(Remap).ToArray();
                var earlier = previous.Reverse().Select(Remap).ToArray();
                previous.Clear();
                foreach (var track in earlier) previous.Push(track);
                var relocatedCurrent = Remap(current);
                if (relocatedCurrent != current)
                {
                    current = relocatedCurrent;
                    CurrentTrackPath = current.Path;
                    UpdateFavoriteToolbarButton();
                }
                removedQueueTrack = Remap(removedQueueTrack);
                undoBatchTags = null;
                RefreshFilters();
                ShowLibrarySource(target);
                libraryHistory.Clear();
                LibraryBackButton.IsEnabled = false;
                SaveSession();
                Status.Text = LocalTextExtension.Format("Fuente reubicada · {0:N0} pistas.", mapping.Count);
                dialog.DialogResult = true;

                Track Remap(Track track) => track != null && mapping.TryGetValue(track.Path, out var replacement)
                    ? track with { Path = replacement } : track;
            }
            catch (Exception error) { feedback.Text = error.GetBaseException().Message; }
            finally
            {
                relocatingSource = false;
                move.IsEnabled = destination.IsEnabled = cancel.IsEnabled = true;
            }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(move);
        root.Children.Add(actions);
        dialog = new Window { Owner = this, Title = T("Reubicar fuente"), Width = 550, SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, root);
        dialog.ShowDialog();
    }
}
