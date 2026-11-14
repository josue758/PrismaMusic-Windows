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

    private async void CastTrack(object sender, RoutedEventArgs args)
    {
        if (castBusy) return;
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
                Status.Text = "Elige una pista para transmitir.";
                return;
            }
            if (castingPicker == null)
            {
                var picker = new CastingDevicePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, new WindowInteropHelper(this).Handle);
                picker.Filter.SupportsAudio = true;
                picker.CastingDeviceSelected += DeviceSelected;
                castingPicker = picker;
                Closed += (_, _) => { castingPicker.Hide(); ResetCasting(); };
            }
            var anchor = (FrameworkElement)sender;
            var origin = anchor.TranslatePoint(new Point(0, 0), this);
            castingPicker.Show(new Windows.Foundation.Rect(origin.X, origin.Y, anchor.ActualWidth, anchor.ActualHeight),
                Windows.UI.Popups.Placement.Above);
        }
        catch (Exception error) { Status.Text = "No se pudo usar el selector de dispositivos. " + error.Message; }
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
                CastButton.Content = "Desconectar";
                Status.Text = $"Transmitiendo «{track.Name}» a {args.SelectedCastingDevice.FriendlyName}.";
            }
            catch (Exception error)
            {
                ResetCasting();
                Status.Text = "No se pudo transmitir la pista. " + error.Message;
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
            Status.Text = "Dispositivo desconectado.";
        });
    }

    private void CastingError(CastingConnection sender, CastingConnectionErrorOccurredEventArgs args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (castingConnection != sender) return;
            ResetCasting();
            Status.Text = "Error de transmisión. " + args.Message;
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
        CastButton.Content = "Transmitir";
    }
}
