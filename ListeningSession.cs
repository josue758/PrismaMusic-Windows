using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string SessionFile => Path.Combine(DataDirectory, "session.json");
    private sealed record ListeningSession(string Track, double Position, string[] Queue, string[] OriginalOrder = null,
        string[] PlaybackOrder = null, int Section = 0, bool? Shuffle = null, RepeatMode? Repeat = null, string PlaylistName = null);
    private string lastSavedSession;
    private int startupSection = 3;

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
        if (direction == 0)
        {
            RemoveQueuedTrack(from);
            return;
        }
        else (queue[from], queue[to]) = (queue[to], queue[from]);
        RefreshRows();
        if (direction != 0 && Rows.ItemsSource is LibraryRow[] rows)
            Rows.SelectedIndex = Array.FindIndex(rows, item => item.Tracks[0] == track);
    }

    private void StartSessionAutoSave()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { if (!restoringBackup && pendingTrack == null) SaveSession(); };
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private void SaveSession()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            double position = playbackRetry is { Interrupted: true } retry && retry.Track.Path == current?.Path
                ? retry.Position : player.Position.TotalSeconds;
            if (!double.IsFinite(position) || position < 0) position = 0;
            position = rememberPosition ? Math.Round(position, 2) : 0;
            var session = new ListeningSession(current?.Path, position, queue.Select(t => t.Path).ToArray(),
                originalContext.Select(t => t.Path).ToArray(), context.Select(t => t.Path).ToArray(),
                Math.Max(0, LibrarySectionIndex), shuffle, repeat, playlistName);
            string contents = JsonSerializer.Serialize(session);
            if (contents == lastSavedSession && File.Exists(SessionFile)) return;
            string temporary = SessionFile + $".{Environment.ProcessId}.tmp";
            try
            {
                File.WriteAllText(temporary, contents);
                File.Move(temporary, SessionFile, true);
                lastSavedSession = contents;
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
    }

    private async System.Threading.Tasks.Task RestoreSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return;
            var session = JsonSerializer.Deserialize<ListeningSession>(File.ReadAllText(SessionFile));
            if (session == null) return;
            if (session.Shuffle is bool shuffleMode)
            {
                shuffle = shuffleMode;
                MarkPlaybackToggle(ShuffleButton, shuffle);
            }
            if (session.Repeat is RepeatMode repeatMode && Enum.IsDefined(repeatMode))
            {
                repeat = repeatMode;
                UpdateRepeatButton();
            }
            if (session.PlaylistName != null && PlaylistPicker.Items.Contains(session.PlaylistName))
                PlaylistPicker.SelectedItem = session.PlaylistName;
            if (startupSection == 2 && session.Section >= 0 && session.Section < Navigation.Items.Count)
                LibrarySectionIndex = session.Section;
            var tracks = library.Tracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var path in (session.Queue ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                if (tracks.TryGetValue(path, out var queued)) queue.Add(queued);
            RefreshQueueViews();
            Track[] Resolve(string[] paths) => (paths ?? Array.Empty<string>())
                .Where(tracks.ContainsKey).Select(path => tracks[path]).ToArray();
            originalContext = Resolve(session.OriginalOrder);
            context = Resolve(session.PlaybackOrder);
            if (session.Track != null && tracks.TryGetValue(session.Track, out var track))
                await Play(track, false, rememberPosition ? session.Position : 0);
        }
        catch (Exception error) { Status.Text = error.Message; }
    }
}




