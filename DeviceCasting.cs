using System;
using System.Windows;
using System.Windows.Interop;
using Windows.Media.Casting;
using Windows.Media.Core;
using Windows.Storage;
using RemotePlayer = Windows.Media.Playback.MediaPlayer;

namespace PrismaMusic;

public partial class MainWindow
{
    private CastingDevicePicker castingPicker;
    private CastingConnection castingConnection;
    private RemotePlayer castingPlayer;
    private Track castingTrack;
    private bool castBusy;
    private string castFailure;
    private nint castingPickerOwner;

    private async void CastTrack(object sender, RoutedEventArgs args) => await ShowCastingPicker(CastToolbarButton);

    private async System.Threading.Tasks.Task ShowCastingPicker(FrameworkElement anchor)
    {
        if (castBusy) return;
        castFailure = null;
        try
        {
            if (castingConnection != null)
            {
                castBusy = true;
                try { await castingConnection.DisconnectAsync(); }
                finally { ResetCasting(); castBusy = false; }
                return;
            }

            castingTrack = current ?? Selected;
            if (castingTrack == null)
            {
                Status.Text = LocalTextExtension.Translate("Elige una pista para transmitir.");
                return;
            }
            var owner = Window.GetWindow(anchor) ?? this;
            var handle = new WindowInteropHelper(owner).Handle;
            if (castingPicker == null || castingPickerOwner != handle)
            {
                if (castingPicker != null)
                {
                    castingPicker.Hide();
                    castingPicker.CastingDeviceSelected -= DeviceSelected;
                }
                else Closed += (_, _) => { castingPicker?.Hide(); ResetCasting(); };
                var picker = new CastingDevicePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
                picker.Filter.SupportsAudio = true;
                picker.Filter.SupportsVideo = true;
                picker.CastingDeviceSelected += DeviceSelected;
                castingPicker = picker;
                castingPickerOwner = handle;
            }
            var origin = anchor.TranslatePoint(new Point(0, 0), owner);
            castingPicker.Show(new Windows.Foundation.Rect(origin.X, origin.Y, anchor.ActualWidth, anchor.ActualHeight),
                Windows.UI.Popups.Placement.Above);
        }
        catch (Exception error) { Status.Text = castFailure = LocalTextExtension.Translate("No se pudo usar el selector de dispositivos. ") + error.Message; }
    }

    private void DeviceSelected(CastingDevicePicker sender, CastingDeviceSelectedEventArgs args)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (castBusy || castingConnection != null || castingTrack == null || !IsLoaded) return;
            castBusy = true;
            var track = castingTrack;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(track.Path);
                if (!IsLoaded) return;
                castingPlayer = new RemotePlayer { Source = MediaSource.CreateFromStorageFile(file) };
                var connection = args.SelectedCastingDevice.CreateCastingConnection();
                castingConnection = connection;
                connection.StateChanged += CastingStateChanged;
                connection.ErrorOccurred += CastingError;
                var result = await connection.RequestStartCastingAsync(castingPlayer.GetAsCastingSource());
                if (castingConnection != connection) return;
                if (result != CastingConnectionErrorStatus.Succeeded)
                    throw new InvalidOperationException(result.ToString());
                if (playing) Toggle(this, new RoutedEventArgs());
                castingPlayer.Play();
                CastMenuItem.Header = LocalTextExtension.Translate("Desconectar");
                UpdateCastToolbar(true);
                System.Windows.Automation.AutomationProperties.SetName(CastMenuItem,
                    LocalTextExtension.Translate("Desconectar"));
                Status.Text = LocalTextExtension.Format("Transmitiendo «{0}» a {1}.", track.Name, args.SelectedCastingDevice.FriendlyName);
            }
            catch (Exception error)
            {
                ResetCasting();
                Status.Text = castFailure = LocalTextExtension.Translate("No se pudo transmitir la pista. ") + error.Message;
            }
            finally { castBusy = false; }
        });
    }

    private void CastingStateChanged(CastingConnection sender, object args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (castingConnection != sender || sender.State != CastingConnectionState.Disconnected) return;
            ResetCasting();
            Status.Text = LocalTextExtension.Translate("Dispositivo desconectado.");
        });
    }

    private void CastingError(CastingConnection sender, CastingConnectionErrorOccurredEventArgs args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (castingConnection != sender) return;
            ResetCasting();
            Status.Text = castFailure = LocalTextExtension.Translate("Error de transmisión. ") + args.Message;
        });
    }

    private void ResetCasting()
    {
        var connection = castingConnection;
        castingConnection = null;
        if (connection != null)
        {
            connection.StateChanged -= CastingStateChanged;
            connection.ErrorOccurred -= CastingError;
            connection.Dispose();
        }
        castingPlayer?.Dispose();
        castingPlayer = null;
        castingTrack = null;
        CastMenuItem.Header = LocalTextExtension.Translate("Transmitir a dispositivo");
        UpdateCastToolbar(false);
        System.Windows.Automation.AutomationProperties.SetName(CastMenuItem,
            LocalTextExtension.Translate("Transmitir a dispositivo"));
    }

    private void UpdateCastToolbar(bool active)
    {
        string label = LocalTextExtension.Translate(active ? "Desconectar" : "Transmitir (Miracast)");
        CastToolbarButton.ToolTip = label;
        CastToolbarButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, active ? "AccentBrush" : "Ink");
        System.Windows.Automation.AutomationProperties.SetName(CastToolbarButton, label);
    }
}

