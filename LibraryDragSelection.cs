using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PrismaMusic;

public partial class MainWindow
{
    private void InitializeLibraryDragSelection()
    {
        int anchor = -1;
        int last = -1;
        Point start = default;
        object source = null;

        Rows.PreviewMouseLeftButtonDown += (_, args) =>
        {
            anchor = last = -1;
            if (Rows.SelectionMode != SelectionMode.Extended || Keyboard.Modifiers != ModifierKeys.None ||
                args.ClickCount != 1 || args.OriginalSource is not TextBlock text ||
                ItemsControl.ContainerFromElement(Rows, text) is not ListBoxItem row) return;
            anchor = Rows.ItemContainerGenerator.IndexFromContainer(row);
            start = args.GetPosition(Rows);
            source = Rows.ItemsSource;
        };
        Rows.PreviewMouseMove += (_, args) =>
        {
            if (anchor < 0 || args.LeftButton != MouseButtonState.Pressed ||
                !ReferenceEquals(source, Rows.ItemsSource)) return;
            Point point = args.GetPosition(Rows);
            if (last < 0 && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (Rows.InputHitTest(point) is not DependencyObject element ||
                ItemsControl.ContainerFromElement(Rows, element) is not ListBoxItem row) return;
            int index = Rows.ItemContainerGenerator.IndexFromContainer(row);
            if (index < 0 || index == last) return;

            if (last < 0)
            {
                if (index == anchor || !Mouse.Capture(Rows, CaptureMode.SubTree)) return;
                Rows.SelectedItems.Clear();
                Rows.SelectedItems.Add(Rows.Items[anchor]);
                last = anchor;
            }

            int previousLower = Math.Min(anchor, last);
            int previousUpper = Math.Max(anchor, last);
            int lower = Math.Min(anchor, index);
            int upper = Math.Max(anchor, index);
            for (int i = Math.Min(lower, previousLower); i <= Math.Max(upper, previousUpper); i++)
            {
                if (i < lower || i > upper) Rows.SelectedItems.Remove(Rows.Items[i]);
                else if (i < previousLower || i > previousUpper) Rows.SelectedItems.Add(Rows.Items[i]);
            }
            last = index;
            args.Handled = true;
        };
        Rows.PreviewMouseLeftButtonUp += (_, args) =>
        {
            anchor = -1;
            if (last < 0) return;
            last = -1;
            Rows.ReleaseMouseCapture();
            args.Handled = true;
        };
        Rows.LostMouseCapture += (_, _) =>
        {
            if (Mouse.Captured == Rows || last < 0) return;
            anchor = last = -1;
        };
    }
}
