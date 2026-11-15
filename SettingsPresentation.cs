using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PrismaMusic;

public partial class MainWindow
{
    private StackPanel AddSettingsCard(StackPanel page, string title, string detail = null)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = T(title), FontSize = 17, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        if (detail != null) content.Children.Add(SettingsHint(detail));

        var surface = new Border
        {
            Background = (Brush)FindResource("SettingsCard"),
            BorderBrush = (Brush)FindResource("WorkspaceStroke"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10),
            Child = content
        };
        surface.SetResourceReference(Border.BackgroundProperty, "SettingsCard");
        surface.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        page.Children.Add(surface);
        return content;
    }

    private TextBlock SettingsHint(string text)
    {
        var hint = new TextBlock { Text = T(text), TextWrapping = TextWrapping.Wrap,
            FontSize = 12, Margin = new Thickness(0, 0, 0, 9) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return hint;
    }


    private (FrameworkElement View, Action Refresh) CreateEqualizerGraph()
    {
        var graph = new Canvas
        {
            Height = 68, ClipToBounds = true, Background = (Brush)FindResource("SettingsBackground"),
            IsHitTestVisible = false, Margin = new Thickness(0, 4, 0, 6)
        };
        var center = new Line { Stroke = (Brush)FindResource("SettingsStroke"), StrokeDashArray = new DoubleCollection { 3, 3 } };
        var curve = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        curve.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        graph.Children.Add(center);
        graph.Children.Add(curve);
        var dots = new Ellipse[gains.Length];
        for (int index = 0; index < dots.Length; index++)
        {
            var dot = new Ellipse { Width = 5, Height = 5 };
            dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
            dots[index] = dot;
            graph.Children.Add(dot);
        }
        void Refresh()
        {
            double width = graph.ActualWidth;
            double middle = graph.Height / 2;
            center.X2 = width;
            center.Y1 = center.Y2 = middle;
            curve.Opacity = eqEnabled ? 1 : .4;
            var points = new PointCollection(gains.Length);
            for (int index = 0; index < gains.Length; index++)
            {
                double x = width * (index + .5) / gains.Length;
                double y = middle - gains[index] / 12 * (middle - 7);
                points.Add(new System.Windows.Point(x, y));
                Canvas.SetLeft(dots[index], x - 2.5);
                Canvas.SetTop(dots[index], y - 2.5);
                dots[index].Opacity = curve.Opacity;
            }
            curve.Points = points;
        }
        graph.SizeChanged += (_, _) => Refresh();
        return (graph, Refresh);
    }

    private Button SettingsButton(string label, RoutedEventHandler action)
    {
        var button = new Button { Content = T(label), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += action;
        return button;
    }

    private void OpenSettingsFolder(object sender, RoutedEventArgs args)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Window owner = Window.GetWindow((DependencyObject)sender) ?? this;
            MessageBox.Show(owner, error.Message, T("Abrir carpeta de datos"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddShortcutRow(StackPanel card, string label, string keys)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 6), LastChildFill = true };
        var key = new Border
        {
            Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("SettingsBackground"),
            BorderBrush = (Brush)FindResource("WorkspaceStroke"), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = T(keys) }
        };
        DockPanel.SetDock(key, Dock.Right);
        row.Children.Add(key);
        row.Children.Add(new TextBlock { Text = T(label), VerticalAlignment = VerticalAlignment.Center });
        card.Children.Add(row);
    }

    private void AddLibrarySettings(StackPanel card)
    {
        var sources = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        card.Children.Add(sources);
        var actions = new WrapPanel();
        var add = SettingsButton("Añadir carpeta", AddFolder);
        var scan = SettingsButton("Volver a escanear", Rescan);
        var cancel = SettingsButton("Cancelar", CancelScan);
        foreach (var button in new[] { add, scan, cancel })
        {
            button.Margin = new Thickness(0, 0, 8, 4);
            actions.Children.Add(button);
        }
        card.Children.Add(actions);
        var progress = SettingsHint("");
        progress.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = ScanProgressText });
        card.Children.Add(progress);

        string[] lastFolders = Array.Empty<string>();
        sources.Children.Add(SettingsHint("Añade una carpeta para empezar tu biblioteca."));
        void Refresh()
        {
            if (!System.Linq.Enumerable.SequenceEqual(lastFolders, library.Folders))
            {
                lastFolders = library.Folders.ToArray();
                sources.Children.Clear();
                if (lastFolders.Length == 0) sources.Children.Add(SettingsHint("Añade una carpeta para empezar tu biblioteca."));
                foreach (string folder in lastFolders)
                {
                    var entry = SettingsHint(folder);
                    entry.ToolTip = folder;
                    sources.Children.Add(entry);
                }
            }
            add.IsEnabled = !scanning;
            scan.IsEnabled = !scanning && lastFolders.Length > 0;
            cancel.Visibility = progress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        }

        var updater = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        updater.Tick += (_, _) => Refresh();
        card.Loaded += (_, _) => { Refresh(); updater.Start(); };
        card.Unloaded += (_, _) => updater.Stop();
    }

    private void AddSleepSettings(StackPanel card)
    {
        var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var status = SettingsHint("Temporizador desactivado");
        Button cancel = SettingsButton("Cancelar temporizador", (_, _) => SetSleepTimer(null));
        foreach (int minutes in SleepTimerPresets)
        {
            var button = new Button
            {
                Content = LocalTextExtension.Format("{0} min", minutes),
                Margin = new Thickness(0, 0, 6, 6), MinWidth = 67
            };
            AutomationProperties.SetName(button, LocalTextExtension.Format("Pausar en {0} min", minutes));
            button.Click += (_, _) => SetSleepTimer(minutes);
            choices.Children.Add(button);
        }
        cancel.Margin = new Thickness(0, 0, 0, 6);
        choices.Children.Add(cancel);
        card.Children.Add(choices);
        card.Children.Add(status);

        var endOfTrack = new CheckBox
        {
            Style = (Style)FindResource("SettingsToggle"),
            Content = T("Pausar al final de esta canción"),
            IsChecked = stopAfterTrack
        };
        endOfTrack.Click += (_, _) => SetStopAfterTrack(endOfTrack.IsChecked == true);
        card.Children.Add(endOfTrack);

        void Refresh()
        {
            cancel.IsEnabled = sleepAt.HasValue;
            status.Text = sleepAt is DateTime deadline
                ? LocalTextExtension.Format("Tiempo restante · {0}", FormatClock((deadline - DateTime.UtcNow).TotalSeconds))
                : T("Temporizador desactivado");
            endOfTrack.IsChecked = stopAfterTrack;
        }

        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => Refresh();
        card.Loaded += (_, _) => { Refresh(); clock.Start(); };
        card.Unloaded += (_, _) => clock.Stop();
        foreach (Button button in choices.Children) button.Click += (_, _) => Refresh();
    }
}
