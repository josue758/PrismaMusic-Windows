using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string SessionFile => Path.Combine(DataDirectory, "session.json");
    private sealed record ListeningSession(string Track, double Position, string[] Queue);

    private void MoveQueueUp(object sender, RoutedEventArgs args) => EditQueue(-1);
    private void MoveQueueDown(object sender, RoutedEventArgs args) => EditQueue(1);
    private void RemoveFromQueue(object sender, RoutedEventArgs args) => EditQueue(0);

    private void EditQueue(int direction)
    {
        if (Rows.SelectedItem is not LibraryRow row) return;
        var track = row.Tracks[0];
        int from = queue.IndexOf(track);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= queue.Count) return;
        if (direction == 0) queue.RemoveAt(from);
        else (queue[from], queue[to]) = (queue[to], queue[from]);
        RefreshRows();
        if (direction != 0 && Rows.ItemsSource is LibraryRow[] rows)
            Rows.SelectedIndex = Array.FindIndex(rows, item => item.Tracks[0] == track);
    }

    private void SaveSession()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var session = new ListeningSession(current?.Path, player.Position.TotalSeconds, queue.Select(t => t.Path).ToArray());
            File.WriteAllText(SessionFile + ".tmp", JsonSerializer.Serialize(session));
            File.Move(SessionFile + ".tmp", SessionFile, true);
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
    }

    private void RestoreSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return;
            var session = JsonSerializer.Deserialize<ListeningSession>(File.ReadAllText(SessionFile));
            if (session == null) return;
            var tracks = library.Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var path in session.Queue.Distinct(StringComparer.OrdinalIgnoreCase))
                if (tracks.TryGetValue(path, out var queued)) queue.Add(queued);
            if (session.Track != null && tracks.TryGetValue(session.Track, out var track))
                Play(track, false, session.Position);
        }
        catch (Exception error) { Status.Text = error.Message; }
    }
}

