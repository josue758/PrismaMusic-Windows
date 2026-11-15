using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace PrismaMusic;

public partial class MainWindow
{
    private readonly CancellationTokenSource updateLifetime = new();
    private ApplicationUpdates updater;
    private DispatcherTimer updateTimer;
    private Task<ApplicationUpdate> updateCheck;
    private ApplicationUpdate availableUpdate;
    private string installationDirectory;

    private void InitializeUpdates()
    {
        if (smokeTest || SessionProbeDirectory() != null) return;
        installationDirectory = ApplicationUpdates.InstallationDirectory();
        if (installationDirectory == null) return;

        updater = ApplicationUpdates.Create();
        _ = Task.Run(ApplicationUpdates.RemoveExpiredInstallers);
        updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(12) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(4);
            try { await CheckForUpdate(); }
            catch (Exception error) when (IsUpdateFailure(error)) { }
        };
        updateTimer.Start();
        Closed += (_, _) =>
        {
            updateTimer.Stop();
            updateLifetime.Cancel();
            updateLifetime.Dispose();
        };
    }

    private async Task<ApplicationUpdate> CheckForUpdate()
    {
        if (updateCheck == null || updateCheck.IsCompleted)
            updateCheck = updater.CheckAsync(updateLifetime.Token);
        availableUpdate = await updateCheck;
        if (!updateLifetime.IsCancellationRequested)
            UpdateAvailableButton.Visibility = availableUpdate == null ? Visibility.Collapsed : Visibility.Visible;
        return availableUpdate;
    }

    private void OpenUpdateDialog(object sender, RoutedEventArgs args)
    {
        if (updater == null) return;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        var window = new Window
        {
            Owner = Window.GetWindow((DependencyObject)sender),
            Title = T("Actualizaciones de Prisma Music"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 470,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        window.SetResourceReference(BackgroundProperty, "SettingsBackground");
        var root = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = window.Title, FontSize = 23, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        root.Children.Add(heading);
        var version = new TextBlock
        {
            Text = string.Format(T("Versión instalada {0}"), ApplicationUpdates.InstalledVersion.ToString(3)),
            Margin = new Thickness(0, 5, 0, 18)
        };
        version.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        root.Children.Add(version);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 42 };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        root.Children.Add(status);
        var progress = new ProgressBar { Height = 5, Margin = new Thickness(0, 10, 0, 2), Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(progress, T("Descargando actualización…"));
        root.Children.Add(progress);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var close = new Button { Content = T("Cerrar"), IsCancel = true, MinWidth = 88 };
        var action = new Button { MinWidth = 132, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        action.SetResourceReference(BackgroundProperty, "AccentBrush");
        action.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        buttons.Children.Add(close);
        buttons.Children.Add(action);
        root.Children.Add(buttons);
        window.Content = root;
        ApplyFluentDialog(window, root);
        window.Closed += (_, _) => cancellation.Cancel();

        ApplicationUpdate update = availableUpdate;
        void ShowReady()
        {
            status.Text = string.Format(T("Ya está disponible Prisma Music {0}. La aplicación se cerrará y volverá a abrir al terminar."), update.Version.ToString(3));
            action.Content = T("Descargar e instalar");
        }

        async Task FindUpdate()
        {
            action.IsEnabled = false;
            status.Text = T("Buscando actualizaciones…");
            try
            {
                update = await CheckForUpdate();
                if (cancellation.IsCancellationRequested) return;
                if (update == null) status.Text = T("Tienes la versión más reciente.");
                else ShowReady();
            }
            catch (Exception error) when (IsUpdateFailure(error))
            {
                if (!cancellation.IsCancellationRequested)
                    status.Text = T("No se pudo buscar actualizaciones. Comprueba tu conexión y vuelve a intentarlo.");
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    if (update == null) action.Content = T("Buscar actualizaciones");
                    action.IsEnabled = true;
                }
            }
        }

        action.Click += async (_, _) =>
        {
            if (update == null)
            {
                await FindUpdate();
                return;
            }
            if (scanCancellation != null)
            {
                status.Text = T("Espera a que termine el escaneo de la biblioteca para instalar la actualización.");
                return;
            }

            string installer = null;
            action.IsEnabled = false;
            close.Content = T("Cancelar");
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;
            status.Text = T("Descargando actualización…");
            try
            {
                using var download = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                download.CancelAfter(TimeSpan.FromMinutes(20));
                installer = await updater.DownloadAsync(update, new Progress<double>(percent =>
                {
                    if (!cancellation.IsCancellationRequested) progress.Value = percent;
                }), download.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                string language = LocalTextExtension.Language switch
                {
                    "en" => "english",
                    "pt" => "brazilianportuguese",
                    _ => "spanish"
                };
                if (scanCancellation != null)
                {
                    status.Text = T("Espera a que termine el escaneo de la biblioteca para instalar la actualización.");
                    return;
                }
                ApplicationUpdates.LaunchInstaller(installer, installationDirectory, language);
                installer = null;
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception error) when (IsUpdateFailure(error))
            {
                if (!cancellation.IsCancellationRequested)
                    status.Text = T("No se pudo preparar la actualización. Vuelve a intentarlo.");
            }
            finally
            {
                ApplicationUpdates.DiscardInstaller(installer);
                if (!cancellation.IsCancellationRequested)
                {
                    progress.Visibility = Visibility.Collapsed;
                    close.Content = T("Cerrar");
                    action.IsEnabled = true;
                }
            }
        };
        if (update != null) ShowReady();
        else window.Loaded += async (_, _) => await FindUpdate();
        window.ShowDialog();
    }

    private static bool IsUpdateFailure(Exception error) =>
        error is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or
        OperationCanceledException or UnauthorizedAccessException or Win32Exception or
        System.Collections.Generic.KeyNotFoundException or FormatException;
}
