using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismaMusic;

public partial class MainWindow
{
    private bool listeningPanelHidden;
    private bool compactListeningOpen;
    private bool listeningIsOverlay;
    private bool IsListeningPanelVisible => listeningIsOverlay ? compactListeningOpen : !listeningPanelHidden;
    private Track removedQueueTrack;
    private int removedQueueIndex;
    private Point? queueDragOrigin;
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

    private void ToggleListeningPanel(object sender, RoutedEventArgs args) => SetListeningPanelHidden(IsListeningPanelVisible);

    private void SetListeningPanelHidden(bool hidden)
    {
        bool returnFocus = hidden && Listening.IsKeyboardFocusWithin;
        if (listeningIsOverlay) compactListeningOpen = !hidden;
        else listeningPanelHidden = hidden;
        UpdateListeningPanelWidth();
        if (!hidden && listeningIsOverlay) ListeningCloseButton.Focus();
        else if (returnFocus) (ListeningTabs.SelectedIndex switch
        {
            1 => QueueHeaderButton, 2 => InfoHeaderButton, _ => LyricsHeaderButton
        }).Focus();
    }

    private void ToggleQueueView(object sender, RoutedEventArgs args) => ToggleListeningTab(1);
    private void ToggleLyricsView(object sender, RoutedEventArgs args) => ToggleListeningTab(0);
    private void ToggleInfoView(object sender, RoutedEventArgs args) => ToggleListeningTab(2);

    private void ToggleListeningTab(int index)
    {
        if (IsListeningPanelVisible && ListeningTabs.SelectedIndex == index)
        {
            SetListeningPanelHidden(true);
            return;
        }
        ListeningTabs.SelectedIndex = index;
        SetListeningPanelHidden(false);
    }

    private void ListeningTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ReferenceEquals(args.Source, ListeningTabs)) UpdateListeningPanelCommands();
    }

    private void UpdateListeningPanelCommands()
    {
        if (ListeningTabs == null || ListeningTrackSummary == null) return;
        int selected = ListeningTabs.SelectedIndex;
        ListeningPanelTitle.Text = T(selected switch { 1 => "Cola de reproducción", 2 => "Información", _ => "Reproduciendo ahora" });
        ListeningTrackSummary.Visibility = selected == 1 || current == null ? Visibility.Collapsed : Visibility.Visible;
        SetActive(LyricsHeaderButton, 0);
        SetActive(QueueHeaderButton, 1);
        SetActive(InfoHeaderButton, 2);

        void SetActive(Button button, int index)
        {
            bool active = IsListeningPanelVisible && selected == index;
            button.SetResourceReference(ForegroundProperty, active ? "AccentBrush" : "Ink");
            button.Background = active ? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) : Brushes.Transparent;
            AutomationProperties.SetItemStatus(button, active ? T("Expandido") : T("Contraído"));
        }
    }

    private void UpdateListeningPanelWidth()
    {
        bool overlay = ActualWidth < 1200;
        bool changedMode = overlay != listeningIsOverlay;
        bool restoreFocus = changedMode && Listening.IsKeyboardFocusWithin;
        if (changedMode)
        {
            compactListeningOpen = false;
            listeningIsOverlay = overlay;
            Grid.SetColumn(Listening, overlay ? 0 : 2);
            Grid.SetColumnSpan(Listening, overlay ? 3 : 1);
            Listening.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            Listening.Margin = overlay ? new Thickness(0) : new Thickness(10, 0, 0, 0);
            Listening.SetResourceReference(BackgroundProperty, overlay ? "SettingsBackground" : "ListeningSurface");
            KeyboardNavigation.SetTabNavigation(Listening, overlay ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
        }
        double width = overlay ? Math.Clamp(ActualWidth * .40, 300, 410)
            : current == null ? 248 : Math.Clamp(ActualWidth * .232, 330, 420);
        bool visible = IsListeningPanelVisible;
        Listening.Width = overlay ? width : double.NaN;
        ListeningColumn.Width = new GridLength(visible && !overlay ? width : 0);
        Listening.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ListeningScrim.Visibility = visible && overlay ? Visibility.Visible : Visibility.Collapsed;
        ArtPanel.Height = overlay ? Math.Clamp(ActualHeight - 460, 92, Math.Min(185, width - 42))
            : Math.Clamp(ActualHeight - 540, 98, Math.Max(226, width - 42));
        string label = T(visible ? "Plegar panel de reproducción" : "Mostrar panel de reproducción");
        ListeningPanelMenuItem.Header = label;
        AutomationProperties.SetName(ListeningPanelMenuItem, label);
        UpdateListeningPanelCommands();
        if (restoreFocus && !visible)
            (ListeningTabs.SelectedIndex == 1 ? QueueHeaderButton : ListeningTabs.SelectedIndex == 2 ? InfoHeaderButton : LyricsHeaderButton).Focus();
    }

    private void UpdateTransportViewport()
    {
        bool narrow = listeningRoom == null && ActualWidth < 940;
        EqualizerToolbarButton.Visibility = CastToolbarButton.Visibility = FullscreenToolbarButton.Visibility =
            narrow ? Visibility.Collapsed : Visibility.Visible;
        CompactEqualizerMenuItem.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        TransportActionsColumn.MinWidth = narrow ? 160 : 216;
    }

    private void RefreshSideQueue()
    {
        InvalidateGaplessIfCandidateChanged();
        QueueNowCard.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateListeningPanelWidth();
        var selected = SideQueue.SelectedItem;
        SideQueue.ItemsSource = queue.ToArray();
        SideQueue.SelectedItem = selected;
        SideQueueEmpty.Visibility = queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SideQueueCount.Text = queue.Count.ToString("N0");
        UndoQueueButton.IsEnabled = removedQueueTrack != null;
    }

    private Track QueueTrack(object sender) => (sender as FrameworkElement)?.DataContext as Track ?? SideQueue.SelectedItem as Track;

    private void MoveSideQueueUp(object sender, RoutedEventArgs args) => MoveSideQueue(sender, -1);
    private void MoveSideQueueDown(object sender, RoutedEventArgs args) => MoveSideQueue(sender, 1);
    private void MoveSideQueue(object sender, int delta)
    {
        if (QueueTrack(sender) is not Track track) return;
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
        if (LibrarySectionIndex == 4) RefreshRows();
        else RefreshSideQueue();
        if (selected != null) SideQueue.SelectedItem = selected;
    }

    private void RemoveSideQueueTrack(object sender, RoutedEventArgs args)
    {
        if (QueueTrack(sender) is not Track track) return;
        int index = queue.IndexOf(track);
        if (index >= 0) RemoveQueuedTrack(index);
    }

    private async void PlaySideQueueTrack(object sender, RoutedEventArgs args)
    {
        if (QueueTrack(sender) is not Track track || pendingTrack != null) return;
        int index = queue.IndexOf(track);
        if (index < 0) return;
        queue.RemoveAt(index);
        RefreshQueueViews();
        await Play(track);
    }

    private async void LoadQueueArtwork(object sender, RoutedEventArgs args)
    {
        if (sender is not Image { DataContext: Track track } image) return;
        await artworkWorkers.WaitAsync();
        try
        {
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, track)) return;
            var row = new LibraryRow(track.Album, track.Artist, new[] { track });
            var cover = await System.Threading.Tasks.Task.Run(() => ReadCollectionImage(row, artist: false, size: 48));
            if (image.IsLoaded && ReferenceEquals(image.DataContext, track)) image.Source = cover;
        }
        finally { artworkWorkers.Release(); }
    }

    private void RemoveQueuedTrack(int index)
    {
        removedQueueTrack = queue[index];
        removedQueueIndex = index;
        queue.RemoveAt(index);
        if (undoNotices)
        {
            QueueRemovalNotice.Visibility = Visibility.Visible;
            queueNoticeDeadline = DateTime.UtcNow.AddSeconds(5);
        }
        RefreshQueueViews();
    }

    private void UndoQueueRemoval(object sender, RoutedEventArgs args)
    {
        var track = removedQueueTrack;
        removedQueueTrack = null;
        ClearQueueNotice();
        if (track == null) return;
        if (library.Tracks.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, track.Path)) &&
            !queue.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, track.Path)))
            queue.Insert(Math.Min(removedQueueIndex, queue.Count), ApplyLibraryTags(track));
        RefreshQueueViews(track);
    }

    private void QueueDragStart(object sender, MouseButtonEventArgs args)
    {
        queueDragOrigin = null;
        for (var hit = args.OriginalSource as Visual; hit != null && !ReferenceEquals(hit, sender);
            hit = VisualTreeHelper.GetParent(hit) as Visual)
            if (hit is Button) return;
        queueDragOrigin = args.GetPosition(SideQueue);
    }

    private void QueueDragMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed || queueDragOrigin is not Point origin ||
            sender is not ListBoxItem { Content: Track track }) return;
        var delta = args.GetPosition(SideQueue) - origin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        queueDragOrigin = null;
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
