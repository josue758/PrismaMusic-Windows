using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record ScanFailure(string Path, string Error)
    {
        public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } name
            ? name : Path;
    }
    private sealed record ScanReport(string[] AddedPaths, string[] UpdatedPaths, int Unchanged, bool Cancelled, ScanFailure[] Failures)
    {
        internal int Added => AddedPaths.Length;
        internal int Updated => UpdatedPaths.Length;
        internal bool Complete => !Cancelled && Failures.Length == 0;
    }
    private ScanReport lastScanReport;

    private void SetScanReport(IEnumerable<string> added, IEnumerable<string> updated, int unchanged,
        IEnumerable<KeyValuePair<string, string>> failures, bool cancelled, ScanReport previous, IReadOnlyCollection<string> attempted)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var newPaths = (previous?.AddedPaths ?? Array.Empty<string>()).Concat(added).ToHashSet(comparer);
        var updatedPaths = (previous?.UpdatedPaths ?? Array.Empty<string>()).Concat(updated).ToHashSet(comparer);
        updatedPaths.ExceptWith(newPaths);
        var unresolved = previous?.Failures.ToDictionary(item => item.Path, item => item.Error, comparer) ?? new(comparer);
        if (!cancelled)
            foreach (string path in attempted) unresolved.Remove(path);
        foreach (var failure in failures) unresolved[failure.Key] = failure.Value;
        lastScanReport = new(newPaths.Order(comparer).ToArray(), updatedPaths.Order(comparer).ToArray(),
            previous?.Unchanged ?? unchanged, cancelled || previous?.Cancelled == true, unresolved.OrderBy(pair => pair.Key, comparer)
                .Select(pair => new ScanFailure(pair.Key, pair.Value)).ToArray());
        ScanReportButton.Visibility = Visibility.Visible;
    }

    private void ShowScanReport(object sender, RoutedEventArgs args)
    {
        if (lastScanReport is not { } report) return;
        var surface = new Grid();
        var root = new DockPanel { Margin = new Thickness(22) };
        var background = new Border { Child = root };
        background.SetResourceReference(BackgroundProperty, "SettingsBackground");
        surface.Children.Add(background);
        var details = new StackPanel();
        DockPanel.SetDock(details, Dock.Top);
        root.Children.Add(details);
        details.Children.Add(new TextBlock
        {
            Text = T(report.Cancelled ? "Escaneo cancelado" : report.Complete ? "Escaneo completo" : "Escaneo parcial"),
            FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5)
        });
        details.Children.Add(SettingsHint(LocalTextExtension.Format(
            "{0:N0} nuevas · {1:N0} actualizadas · {2:N0} reutilizadas · {3:N0} rutas con error",
            report.Added, report.Updated, report.Unchanged, report.Failures.Length)));
        bool hasRoutes = report.Added + report.Updated + report.Failures.Length > 0;
        var tools = new DockPanel { Margin = new Thickness(0, 14, 0, 8), Visibility = hasRoutes ? Visibility.Visible : Visibility.Collapsed };
        var picker = new ComboBox { Style = (Style)FindResource("FilterCombo"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 180 };
        var search = new TextBox { Style = (Style)FindResource("LibrarySearch"), Padding = new Thickness(10, 4, 10, 4),
            MinWidth = 120, VerticalContentAlignment = VerticalAlignment.Center };
        search.SetResourceReference(BackgroundProperty, "SettingsCard");
        search.SetResourceReference(BorderBrushProperty, "SettingsStroke");
        search.SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetName(picker, T("Tipo de resultado"));
        var searchLabel = new Label { Content = T("Buscar ruta"), Target = search, VerticalAlignment = VerticalAlignment.Center };
        searchLabel.SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetLabeledBy(search, searchLabel);
        tools.Children.Add(picker);
        tools.Children.Add(searchLabel);
        tools.Children.Add(search);
        details.Children.Add(tools);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var feedback = SettingsHint("");
        feedback.MaxHeight = 76;
        feedback.Visibility = Visibility.Collapsed;
        footer.Children.Add(feedback);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions);
        var paths = new ListBox
        {
            IsSynchronizedWithCurrentItem = false,
            Visibility = hasRoutes ? Visibility.Visible : Visibility.Collapsed
        };
        AutomationProperties.SetName(paths, T("Rutas del escaneo"));
        VirtualizingPanel.SetVirtualizationMode(paths, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(paths, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(paths, ScrollBarVisibility.Disabled);
        paths.SetResourceReference(BackgroundProperty, "SettingsCard");
        paths.SetResourceReference(BorderBrushProperty, "SettingsStroke");
        paths.SetResourceReference(ForegroundProperty, "Ink");
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetBinding(ToolTipProperty, new Binding(nameof(ScanFailure.Path)));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(ScanFailure.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(name);
        var location = new FrameworkElementFactory(typeof(TextBlock));
        location.SetBinding(TextBlock.TextProperty, new Binding(nameof(ScanFailure.Path)));
        location.SetValue(TextBlock.FontSizeProperty, 12.0);
        location.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        location.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        row.AppendChild(location);
        paths.ItemTemplate = new DataTemplate { VisualTree = row };
        var dialog = new Window { Owner = this, Title = T("Informe del escaneo"), Width = 800, Height = hasRoutes ? 510 : 220,
            MinWidth = 440, MinHeight = hasRoutes ? 340 : 200, MaxWidth = Math.Max(440, SystemParameters.WorkArea.Width),
            MaxHeight = Math.Max(340, SystemParameters.WorkArea.Height), ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = surface };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, surface);
        void ShowFeedback(string message)
        {
            feedback.Text = message;
            feedback.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
        Button AddAction(string label, RoutedEventHandler handler)
        {
            var button = ActionButton(label, handler);
            button.Style = (Style)FindResource(typeof(Button));
            button.SetResourceReference(BackgroundProperty, "SettingsCard");
            button.Margin = new Thickness(6, 6, 0, 0);
            actions.Children.Add(button);
            return button;
        }
        async void Retry(string[] routes)
        {
            if (scanning || relocatingSource || !ReferenceEquals(lastScanReport, report)) return;
            dialog.Close();
            await Scan(routes, false, allSubfolders: true, retryFailures: true);
        }
        if (hasRoutes)
        {
            var groups = new (string Label, ScanFailure[] Rows)[]
            {
                (T("Rutas con error"), report.Failures),
                (T("Pistas nuevas"), report.AddedPaths.Select(path => new ScanFailure(path, null)).ToArray()),
                (T("Pistas actualizadas"), report.UpdatedPaths.Select(path => new ScanFailure(path, null)).ToArray())
            }.Where(group => group.Rows.Length > 0).ToArray();
            picker.ItemsSource = groups.Select(group => $"{group.Label} ({group.Rows.Length:N0})").ToArray();
            bool CanRetry() => !scanning && !relocatingSource && ReferenceEquals(lastScanReport, report);
            var show = AddAction("Abrir en el Explorador de archivos", (_, _) =>
            {
                if (paths.SelectedItem is ScanFailure item) RevealLibrarySource(item.Path);
            });
            var copy = AddAction("Copiar ruta", (_, _) =>
            {
                if (paths.SelectedItem is not ScanFailure item) return;
                try { Clipboard.SetText(item.Path); }
                catch (System.Runtime.InteropServices.ExternalException error) { ShowFeedback(error.Message); }
            });
            var retrySelected = AddAction("Reintentar ruta", (_, _) =>
            {
                if (paths.SelectedItem is ScanFailure item && item.Error != null) Retry(new[] { item.Path });
            });
            var retryAll = AddAction("Reintentar rutas con error", (_, _) => Retry(report.Failures.Select(item => item.Path).ToArray()));
            retryAll.IsEnabled = CanRetry();
            retrySelected.Visibility = retryAll.Visibility = report.Failures.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            void UpdateSelection()
            {
                var selected = paths.SelectedItem as ScanFailure;
                show.IsEnabled = copy.IsEnabled = selected != null;
                retrySelected.IsEnabled = selected?.Error != null && CanRetry();
                ShowFeedback(selected?.Error ?? (paths.Items.Count == 0 ? T("Ninguna ruta coincide con la búsqueda.") : ""));
            }
            void FilterPaths(ListCollectionView view)
            {
                string query = search.Text.Trim();
                view.Filter = query.Length == 0 ? null
                    : entry => ((ScanFailure)entry).Path.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var view = new ListCollectionView(groups[picker.SelectedIndex].Rows);
                FilterPaths(view);
                paths.ItemsSource = view;
                UpdateSelection();
            };
            search.TextChanged += (_, _) =>
            {
                if (paths.ItemsSource is ListCollectionView view) FilterPaths(view);
                UpdateSelection();
            };
            paths.SelectionChanged += (_, _) => UpdateSelection();
            picker.SelectedIndex = 0;
        }
        else ShowFeedback(T("No hay rutas nuevas, actualizadas o con error."));
        AddAction("Cerrar", (_, _) => dialog.Close()).IsCancel = true;
        root.Children.Add(paths);
        dialog.ShowDialog();
    }
}
