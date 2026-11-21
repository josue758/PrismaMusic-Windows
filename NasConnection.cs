using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private string AskNasSource(Window owner)
    {
        const string pathExample = @"\\servidor\Música";
        using var lifetime = new CancellationTokenSource();
        string acceptedPath = null;
        string signInPath = null;
        var root = new StackPanel { Margin = new Thickness(26, 23, 26, 20) };
        var dialog = new Window
        {
            Title = T("Conectar NAS"), Owner = owner, Width = 545, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = root
        };
        dialog.SetResourceReference(BackgroundProperty, "SettingsBackground");
        ApplyFluentDialog(dialog, root);
        dialog.Closed += (_, _) => lifetime.Cancel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 17) };
        var icon = new TextBlock
        {
            Text = "\uE968", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10),
            Child = icon, Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(Border.BackgroundProperty, "ControlFill");
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Add(tile);
        var intro = new StackPanel();
        var heading = new TextBlock { Text = T("Música desde tu NAS"), FontSize = 22, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        intro.Children.Add(heading);
        intro.Children.Add(SettingsHint("Escribe el nombre o la IP del NAS para ver sus carpetas, o pega una ruta SMB completa."));
        header.Children.Add(intro);
        root.Children.Add(header);

        var address = new TextBox { Height = 36, Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center, ToolTip = pathExample };
        address.SetResourceReference(ForegroundProperty, "Ink");
        address.SetResourceReference(BackgroundProperty, "ControlFill");
        address.SetResourceReference(BorderBrushProperty, "ControlStroke");
        var label = new TextBlock { Text = T("Dirección del NAS"), Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        AutomationProperties.SetLabeledBy(address, label);
        root.Children.Add(label);
        root.Children.Add(address);
        var hint = SettingsHint("Ejemplo de ruta o unidad de red");
        hint.Text += "  " + pathExample + @"  ·  Z:\";
        hint.Margin = new Thickness(0, 6, 0, 12);
        root.Children.Add(hint);

        var sharesLabel = new TextBlock { Text = T("Carpetas compartidas"), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 5), Visibility = Visibility.Collapsed };
        sharesLabel.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var shares = new ListBox { MaxHeight = 150, Visibility = Visibility.Collapsed };
        shares.SetResourceReference(ForegroundProperty, "Ink");
        shares.SetResourceReference(BackgroundProperty, "SettingsCard");
        shares.SetResourceReference(BorderBrushProperty, "SettingsStroke");
        AutomationProperties.SetLabeledBy(shares, sharesLabel);
        root.Children.Add(sharesLabel);
        root.Children.Add(shares);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var callout = new Border
        {
            Child = status, CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed
        };
        callout.SetResourceReference(Border.BackgroundProperty, "ControlFill");
        root.Children.Add(callout);
        var busy = new ProgressBar { Height = 3, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed };
        busy.SetResourceReference(ForegroundProperty, "AccentBrush");
        root.Children.Add(busy);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var browse = new Button { Content = T("Buscar carpeta…"), Margin = new Thickness(0, 0, 6, 0) };
        var signIn = new Button { Content = T("Iniciar sesión con Windows"), Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        var cancel = new Button { Content = T("Cancelar"), IsCancel = true, Margin = new Thickness(0, 0, 6, 0) };
        var connect = new Button { Content = T("Conectar"), IsDefault = true, MinWidth = 91, IsEnabled = false };
        connect.SetResourceReference(BackgroundProperty, "AccentBrush");
        connect.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        actions.Children.Add(browse);
        actions.Children.Add(signIn);
        actions.Children.Add(cancel);
        actions.Children.Add(connect);
        root.Children.Add(actions);

        address.TextChanged += (_, _) =>
        {
            connect.IsEnabled = !string.IsNullOrWhiteSpace(address.Text);
            connect.Content = T(NetworkMusicSource.ServerOnly(address.Text) == null ? "Conectar" : "Ver carpetas");
            signInPath = null;
            signIn.Visibility = callout.Visibility = Visibility.Collapsed;
            shares.Visibility = sharesLabel.Visibility = Visibility.Collapsed;
        };
        shares.SelectionChanged += (_, _) =>
        {
            if (shares.SelectedItem is not string path) return;
            address.Text = path;
            address.CaretIndex = path.Length;
            connect.Focus();
        };
        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = T("Elige la carpeta compartida de tu NAS") };
            if (picker.ShowDialog(dialog) == true) address.Text = picker.FolderName;
        };
        connect.Click += async (_, _) => await Connect(address.Text, authenticate: false);
        signIn.Click += async (_, _) => await Connect(address.Text, authenticate: true);
        dialog.Loaded += (_, _) => address.Focus();
        if (smokeTest && Environment.GetEnvironmentVariable("PRISMA_NAS_DIALOG_PROBE") is string probe)
        {
            dialog.ContentRendered += async (_, _) =>
            {
                CaptureDesignPreview(root, "PRISMA_NAS_PREVIEW");
                address.Text = probe;
                await Connect(probe, authenticate: false);
                if (dialog.IsVisible) dialog.Close();
            };
        }
        return dialog.ShowDialog() == true ? acceptedPath : null;

        void ShowMessage(string message)
        {
            status.Text = T(message);
            status.Visibility = callout.Visibility = Visibility.Visible;
        }

        async Task Connect(string input, bool authenticate)
        {
            var token = lifetime.Token;
            connect.IsEnabled = browse.IsEnabled = address.IsEnabled = signIn.IsEnabled = false;
            shares.IsEnabled = false;
            busy.Visibility = Visibility.Visible;
            string target = null;
            try
            {
                ShowMessage(authenticate ? "Esperando las credenciales de Windows…" : "Comprobando el acceso al NAS…");
                string server = NetworkMusicSource.ServerOnly(input);
                target = authenticate ? signInPath : server != null ? @"\\" + server
                    : await Task.Run(() => NetworkMusicSource.Normalize(input), token).WaitAsync(TimeSpan.FromSeconds(10), token);
                if (target == null)
                {
                    ShowMessage("Indica el nombre del NAS, su IP, una ruta SMB o una unidad de red.");
                    return;
                }
                if (authenticate)
                {
                    nint handle = new WindowInteropHelper(dialog).Handle;
                    await Task.Run(() => NetworkMusicSource.SignIn(target, handle), token).WaitAsync(token);
                }
                if (server != null)
                {
                    string[] paths = await Task.Run(() => NetworkMusicSource.ListShares(server), token)
                        .WaitAsync(TimeSpan.FromSeconds(12), token);
                    if (paths.Length == 0)
                    {
                        ShowMessage("El NAS no mostró carpetas públicas. Escribe la ruta exacta de tu carpeta compartida.");
                        return;
                    }
                    shares.ItemsSource = paths;
                    shares.SelectedItem = null;
                    shares.Visibility = sharesLabel.Visibility = Visibility.Visible;
                    ShowMessage("Selecciona la carpeta que contiene tu música.");
                    return;
                }
                await Task.Run(() => NetworkMusicSource.CheckReadable(target), token)
                    .WaitAsync(TimeSpan.FromSeconds(12), token);
                token.ThrowIfCancellationRequested();
                acceptedPath = target;
                dialog.DialogResult = true;
            }
            catch (TimeoutException) { ShowMessage("El NAS no respondió a tiempo. Comprueba la dirección y que esté conectado a la red."); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or Win32Exception)
            {
                if (token.IsCancellationRequested) return;
                int code = NetworkMusicSource.ErrorCode(error);
                if (NetworkMusicSource.RequiresSignIn(code) && target != null)
                {
                    signInPath = target;
                    signIn.Visibility = Visibility.Visible;
                    ShowMessage("Windows necesita credenciales para acceder. Inicia sesión y vuelve a intentarlo.");
                }
                else if (code == 1219)
                    ShowMessage("Windows ya se conectó a este NAS con otra cuenta. Revisa esa conexión en el Explorador de archivos.");
                else if (code == 1223)
                    ShowMessage("Se canceló el inicio de sesión.");
                else if (code is 2 or 3 or 67)
                    ShowMessage("Windows no encontró esa carpeta compartida. Revisa el nombre o consulta las carpetas del NAS.");
                else
                    ShowMessage("Windows no pudo abrir el NAS. Comprueba la dirección, la red y que el servicio SMB esté activo.");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    busy.Visibility = Visibility.Collapsed;
                    connect.IsEnabled = browse.IsEnabled = address.IsEnabled = signIn.IsEnabled = shares.IsEnabled = true;
                }
            }
        }
    }
}
