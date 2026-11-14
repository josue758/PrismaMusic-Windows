using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace PrismaMusic;

internal static class LibraryExport
{
    internal static string PlaylistText(IEnumerable<Track> tracks)
    {
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var track in tracks)
        {
            string title = $"{track.Artist} - {track.Name}".Replace('\r', ' ').Replace('\n', ' ');
            text.Append("#EXTINF:").Append(Math.Max(0, track.Seconds)).Append(',').Append(title).Append('\n')
                .Append(track.Path).Append('\n');
        }
        return text.ToString();
    }

    internal static string LibraryJson(Library library) => JsonSerializer.Serialize(library, new JsonSerializerOptions
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    internal static async Task WriteAsync(string path, string contents)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path), $".prisma-export-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}

public partial class MainWindow
{
    private void ShowLibraryMenu(object sender, RoutedEventArgs args)
    {
        var menu = LibraryMenuButton.ContextMenu;
        menu.PlacementTarget = LibraryMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void ExportLibrary(object sender, RoutedEventArgs args) =>
        await ExportFile(".json", "Prisma Library.json", "JSON", () =>
        {
            var snapshot = new Library
            {
                Tracks = library.Tracks.ToList(), Folders = library.Folders.ToList(), History = library.History.ToList(),
                Favorites = new HashSet<string>(library.Favorites, StringComparer.OrdinalIgnoreCase),
                Playlist = library.Playlist.ToList(),
                Playlists = library.Playlists.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
                TagEdits = new Dictionary<string, LibraryTagEdit>(library.TagEdits, StringComparer.OrdinalIgnoreCase)
            };
            return Task.Run(() => LibraryExport.LibraryJson(snapshot));
        });

    private async void ExportCurrentPlaylist(object sender, RoutedEventArgs args)
    {
        var tracks = (galleryExportTracks ?? Rows.Items.OfType<LibraryRow>().SelectMany(row => row.Tracks)).ToArray();
        if (tracks.Length == 0)
        {
            Status.Text = LocalTextExtension.Translate("No hay pistas para exportar en esta vista.");
            return;
        }
        await ExportFile(".m3u8", "Prisma Music.m3u8", "M3U8", () => Task.Run(() => LibraryExport.PlaylistText(tracks)));
    }

    private async Task ExportFile(string extension, string name, string type, Func<Task<string>> buildContents)
    {
        string title = LocalTextExtension.Translate(extension == ".json" ? "Exportar biblioteca" : "Exportar lista");
        var dialog = new SaveFileDialog { Title = title, FileName = name, DefaultExt = extension,
            Filter = $"{type} (*{extension})|*{extension}", AddExtension = true, OverwritePrompt = true };
        Window owner = OwnedWindows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? this;
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            if (!string.Equals(Path.GetExtension(dialog.FileName), extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException(LocalTextExtension.Format("Usa la extensión {0}.", extension));
            await LibraryExport.WriteAsync(dialog.FileName, await buildContents());
            Status.Text = LocalTextExtension.Format("Exportado a {0}", dialog.FileName);
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
