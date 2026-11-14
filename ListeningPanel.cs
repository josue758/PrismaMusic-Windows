using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool listeningPanelHidden;
    private Track removedQueueTrack;
    private int removedQueueIndex;
    private Point queueDragOrigin;
    private sealed record LibraryFilters(string Query, string Artist, string Genre, string Year, int Sort);
    private bool restoringLibraryFilters;

    private void RestoreLibraryFilters(LibraryFilters filters)
    {
        restoringLibraryFilters = true;
        try
        {
            Search.Text = filters.Query;
            ArtistFilter.SelectedItem = filters.Artist;
            Genre.SelectedItem = filters.Genre;
            Year.SelectedItem = filters.Year;
            if (ArtistFilter.SelectedIndex < 0) ArtistFilter.SelectedIndex = 0;
            if (Genre.SelectedIndex < 0) Genre.SelectedIndex = 0;
            if (Year.SelectedIndex < 0) Year.SelectedIndex = 0;
            Sort.SelectedIndex = filters.Sort;
        }
        finally { restoringLibraryFilters = false; }
    }

    private void ToggleListeningPanel(object sender, RoutedEventArgs args)
    {
        listeningPanelHidden = !listeningPanelHidden;
        UpdateListeningPanelWidth();
        Listening.Visibility = listeningPanelHidden ? Visibility.Collapsed : Visibility.Visible;
        string label = T(listeningPanelHidden ? "Mostrar panel de reproducción" : "Plegar panel de reproducción");
        ListeningPanelButton.ToolTip = label;
        AutomationProperties.SetName(ListeningPanelButton, label);
    }

    private void UpdateListeningPanelWidth() =>
        ListeningColumn.Width = new GridLength(listeningPanelHidden ? 0 : ActualWidth < 1120 ? 280 : 330);

    private void RefreshSideQueue()
    {
        var selected = SideQueue.SelectedItem;
        SideQueue.ItemsSource = queue.ToArray();
        SideQueue.SelectedItem = selected;
        SideQueueEmpty.Visibility = queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoQueueButton.IsEnabled = removedQueueTrack != null;
    }

    private void MoveSideQueueUp(object sender, RoutedEventArgs args) => MoveSideQueue(-1);
    private void MoveSideQueueDown(object sender, RoutedEventArgs args) => MoveSideQueue(1);
    private void MoveSideQueue(int delta)
    {
        if (SideQueue.SelectedItem is not Track track) return;
        int from = queue.IndexOf(track);
        if (from >= 0) MoveQueueTrack(track, from + delta);
    }

    private void MoveQueueTrack(Track track, int target)
    {
        int from = queue.IndexOf(track);
        if (from < 0 || target < 0 || target >= queue.Count || from == target) return;
        queue.RemoveAt(from);
        queue.Insert(target, track);
        RefreshQueueViews(track);
    }

    private void RefreshQueueViews(Track selected = null)
    {
        if (Navigation.SelectedIndex == 4) RefreshRows();
        else RefreshSideQueue();
        if (selected != null) SideQueue.SelectedItem = selected;
    }

    private void RemoveSideQueueTrack(object sender, RoutedEventArgs args)
    {
        if (SideQueue.SelectedItem is not Track track) return;
        int index = queue.IndexOf(track);
        if (index >= 0) RemoveQueuedTrack(index);
    }

    private void RemoveQueuedTrack(int index)
    {
        removedQueueTrack = queue[index];
        removedQueueIndex = index;
        queue.RemoveAt(index);
        RefreshQueueViews();
    }

    private void UndoQueueRemoval(object sender, RoutedEventArgs args)
    {
        var track = removedQueueTrack;
        removedQueueTrack = null;
        if (track == null) return;
        if (library.Tracks.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, track.Path)) &&
            !queue.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, track.Path)))
            queue.Insert(Math.Min(removedQueueIndex, queue.Count), ApplyLibraryTags(track));
        RefreshQueueViews(track);
    }

    private void QueueDragStart(object sender, MouseButtonEventArgs args) => queueDragOrigin = args.GetPosition(SideQueue);

    private void QueueDragMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed || sender is not ListBoxItem { Content: Track track }) return;
        var delta = args.GetPosition(SideQueue) - queueDragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(Track), track), DragDropEffects.Move);
    }

    private void QueueDragOver(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent(typeof(Track)) ? DragDropEffects.Move : DragDropEffects.None;
        args.Handled = true;
    }

    private void QueueDrop(object sender, DragEventArgs args)
    {
        if (sender is ListBoxItem { Content: Track target } && args.Data.GetData(typeof(Track)) is Track track)
            MoveQueueTrack(track, queue.IndexOf(target));
        args.Handled = true;
    }
}
