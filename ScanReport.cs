using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record ScanFailure(string Path, string Error);
    private sealed record ScanReport(string[] AddedPaths, string[] UpdatedPaths, int Unchanged, bool Complete, ScanFailure[] Failures)
    {
        internal int Added => AddedPaths.Length;
        internal int Updated => UpdatedPaths.Length;
    }
    private ScanReport lastScanReport;

    private void SetScanReport(IEnumerable<string> added, IEnumerable<string> updated, int unchanged,
        IEnumerable<KeyValuePair<string, string>> failures, bool complete)
    {
        lastScanReport = new(added.Order(StringComparer.OrdinalIgnoreCase).ToArray(), updated.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            unchanged, complete, failures.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ScanFailure(pair.Key, pair.Value)).ToArray());
        ScanReportButton.Visibility = Visibility.Visible;
    }

    private void ShowScanReport(object sender, RoutedEventArgs args)
    {
        if (lastScanReport is not { } report) return;
        var root = new DockPanel { Margin = new Thickness(22) };
        var details = new StackPanel();
        DockPanel.SetDock(details, Dock.Top);
        root.Children.Add(details);
        details.Children.Add(SettingsHint(report.Complete ? "Escaneo completo" : "Escaneo parcial"));
        details.Children.Add(SettingsHint(LocalTextExtension.Format(
            "{0:N0} nuevas · {1:N0} actualizadas · {2:N0} reutilizadas · {3:N0} rutas con error",
            report.Added, report.Updated, report.Unchanged, report.Failures.Length)));
        bool hasRoutes = report.Added + report.Updated + report.Failures.Length > 0;
        var picker = new ComboBox { Style = (Style)FindResource("FilterCombo"), Margin = new Thickness(0, 8, 0, 8),
            Visibility = hasRoutes ? Visibility.Visible : Visibility.Collapsed };
        details.Children.Add(picker);
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        actions.Children.Add(new Button { Content = T("Cerrar"), IsCancel = true });
        var paths = new ListBox { Visibility = hasRoutes ? Visibility.Visible : Visibility.Collapsed };
        paths.SetResourceReference(BackgroundProperty, "SettingsCard");
        paths.SetResourceReference(ForegroundProperty, "Ink");
        var dialog = new Window { Owner = this, Title = T("Informe del escaneo"), Width = 820, Height = hasRoutes ? 470 : 240,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root };
        dialog.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        ApplyFluentDialog(dialog, root);
        if (hasRoutes)
        {
            var groups = new (string Label, string[] Paths, string[] Rows)[]
            {
                (T("Rutas con error"), report.Failures.Select(failure => failure.Path).ToArray(),
                    report.Failures.Select(failure => failure.Path + "\n" + failure.Error).ToArray()),
                (T("Pistas nuevas"), report.AddedPaths, report.AddedPaths),
                (T("Pistas actualizadas"), report.UpdatedPaths, report.UpdatedPaths)
            }.Where(group => group.Paths.Length > 0).ToArray();
            picker.ItemsSource = groups.Select(group => group.Label).ToArray();
            picker.SelectionChanged += (_, _) => paths.ItemsSource =
                picker.SelectedIndex >= 0 ? groups[picker.SelectedIndex].Rows : Array.Empty<string>();
            picker.SelectedIndex = 0;
            string SelectedPath() => paths.SelectedIndex >= 0 && picker.SelectedIndex >= 0
                ? groups[picker.SelectedIndex].Paths[paths.SelectedIndex] : null;
            var show = ActionButton("Abrir en el Explorador de archivos", (_, _) =>
            {
                if (SelectedPath() is { } path) RevealLibrarySource(path);
            });
            var copy = ActionButton("Copiar ruta", (_, _) =>
            {
                if (SelectedPath() is not { } path) return;
                try { Clipboard.SetText(path); }
                catch (System.Runtime.InteropServices.ExternalException error) { Status.Text = error.Message; }
            });
            show.IsEnabled = copy.IsEnabled = false;
            paths.SelectionChanged += (_, _) => show.IsEnabled = copy.IsEnabled = paths.SelectedIndex >= 0;
            actions.Children.Add(show);
            actions.Children.Add(copy);
        }
        if (report.Failures.Length > 0)
        {
            var retry = ActionButton("Reintentar rutas con error", async (_, _) =>
            {
                if (scanning || relocatingSource) return;
                dialog.Close();
                await Scan(report.Failures.Select(failure => failure.Path).ToArray(), false, allSubfolders: true, retryFailures: true);
            });
            retry.IsEnabled = !scanning && !relocatingSource;
            actions.Children.Add(retry);
        }
        root.Children.Add(paths);
        dialog.ShowDialog();
    }
}
