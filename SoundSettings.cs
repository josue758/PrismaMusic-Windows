using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Casting;

namespace PrismaMusic;

public partial class MainWindow
{
    private void AddBalanceSettings(StackPanel card, Action save)
    {
        var balance = new Slider
        {
            Style = (Style)FindResource("TransportSlider"), Minimum = -1, Maximum = 1,
            Value = player.Balance, IsSnapToTickEnabled = true, TickFrequency = .05
        };
        AutomationProperties.SetName(balance, T("Balance estéreo"));
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 10) };
        var left = new TextBlock { Text = T("L"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var right = new TextBlock { Text = T("R"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(left);
        row.Children.Add(right);
        row.Children.Add(balance);
        balance.ValueChanged += (_, _) => { player.Balance = (float)balance.Value; save(); };
        card.Children.Add(row);

        var reset = SettingsButton("Centrar", (_, _) => balance.Value = 0);
        reset.Margin = new Thickness(0, 0, 0, 14);
        card.Children.Add(reset);

        var header = new DockPanel();
        var readout = SettingsHint("");
        readout.ToolTip = T("Nivel RMS de la señal de audio de Prisma");
        DockPanel.SetDock(readout, Dock.Right);
        header.Children.Add(readout);
        header.Children.Add(new TextBlock { Text = T("Señal de audio"), FontWeight = FontWeights.SemiBold });
        card.Children.Add(header);

        const double silence = -60;
        var meterBackground = new Border
        {
            Background = (Brush)FindResource("SettingsBackground"), Height = 5, CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 2, 0, 1), ClipToBounds = true
        };
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        meterBackground.Child = fill;
        card.Children.Add(meterBackground);
        double displayed = silence;
        void Refresh()
        {
            bool remote = IsCastingAudio;
            bool raw = opened && player.StrictDirect;
            bool bypassed = player.Direct || player.StrictDirect;
            balance.IsEnabled = reset.IsEnabled = !bypassed && !remote;
            double level = playing && !raw && !remote ? player.Level : 0;
            double db = level > 0 ? Math.Clamp(20 * Math.Log10(level), silence, 0) : silence;
            displayed = Math.Max(db, displayed - 5);
            fill.Width = remote || raw ? 0 : meterBackground.ActualWidth * (displayed - silence) / -silence;
            readout.Text = raw || remote ? T("Medición no disponible") : level <= 0 ? "−∞ dBFS" : $"{db:0.0} dBFS";
        }
        WatchSettingsCard(card, 75, Refresh);
    }

    private bool IsCastingAudio => castingConnection?.State == CastingConnectionState.Connected;

    private void AddAudioDeviceSettings(StackPanel card, Func<Task> apply)
    {
        var refresh = new Button { Content = T("Actualizar dispositivos") };
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Margin = new Thickness(8, 0, 0, 0);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = (TextBlock)card.Children[0];
        card.Children.Remove(title);
        title.Margin = new Thickness(0);
        title.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(refresh, Dock.Right);
        header.Children.Add(refresh);
        header.Children.Add(title);
        card.Children.Add(header);

        var choice = new ComboBox
        {
            Style = (Style)FindResource("FilterCombo"), DisplayMemberPath = nameof(AudioOutputInfo.Name),
            IsEnabled = false, Margin = new Thickness(0, 0, 0, 10)
        };
        AutomationProperties.SetName(choice, T("Salida de audio"));
        card.Children.Add(new TextBlock { Text = T("Salida de audio"), Margin = new Thickness(0, 0, 0, 5) });
        card.Children.Add(choice);
        var active = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
        card.Children.Add(active);
        var format = SettingsHint("");
        card.Children.Add(format);
        var status = SettingsHint("");
        card.Children.Add(status);

        var direct = Toggle("Salida directa · PCM exclusivo", player.StrictDirect);
        card.Children.Add(SettingsHint("Entrega WAV, AIFF y FLAC compatibles en el formato original. Omite el ecualizador, balance, velocidad y volumen de Prisma."));
        var exclusive = Toggle("Solicitar acceso exclusivo", player.Exclusive);
        var bypass = Toggle("Omitir ecualizador, balance y velocidad", player.Direct);
        var route = AddSoundSubsection(card, "Ruta de audio");
        var routeMode = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        routeMode.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        route.Children.Add(routeMode);
        var rates = SettingsHint("");
        rates.Margin = new Thickness(0, 4, 0, 6);
        route.Children.Add(rates);
        var routeDetail = SettingsHint("");
        route.Children.Add(routeDetail);

        var wireless = AddSoundSubsection(card, "Transmisión inalámbrica");
        var cast = new Button { Content = T("Transmitir a dispositivo"), HorizontalAlignment = HorizontalAlignment.Left };
        cast.Click += async (_, _) => await ShowCastingPicker(cast);
        wireless.Children.Add(SettingsHint("Selecciona un destino compatible de Windows. La transmisión usa su propia ruta de audio."));
        wireless.Children.Add(cast);
        var castStatus = SettingsHint("");
        castStatus.Margin = new Thickness(0, 6, 0, 0);
        wireless.Children.Add(castStatus);

        AudioOutputSnapshot snapshot = null;
        bool updatingDevices = false;
        bool applying = false;
        choice.SelectionChanged += async (_, _) =>
        {
            if (updatingDevices || applying || choice.SelectedItem is not AudioOutputInfo next ||
                SameDevice(next.Id, player.OutputId)) return;
            player.OutputId = next.Id;
            await ApplyAndRefresh();
        };

        async Task ReadDevices()
        {
            if (updatingDevices || applying) return;
            updatingDevices = true;
            RefreshState();
            var requested = player.OutputId;
            snapshot = await Task.Run(() => ReadAudioOutputs(requested, includeRates: false));
            var choices = new List<AudioOutputInfo> { new(T("Predeterminada del sistema"), null) };
            choices.AddRange(snapshot.Devices);
            int selection = choices.FindIndex(device => SameDevice(device.Id, requested));
            if (selection < 0)
            {
                var savedName = choice.SelectedItem is AudioOutputInfo previous && SameDevice(previous.Id, requested)
                    ? previous.Name : T("Dispositivo guardado no disponible");
                choices.Add(new AudioOutputInfo(savedName, requested));
                selection = choices.Count - 1;
            }
            choice.ItemsSource = choices;
            choice.SelectedIndex = selection;
            updatingDevices = false;
            RefreshState();
        }

        async Task ApplyAndRefresh()
        {
            applying = true;
            RefreshState();
            try { await apply(); }
            finally { applying = false; }
            await ReadDevices();
        }

        CheckBox Toggle(string label, bool initial)
        {
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("SettingsToggle"), Content = T(label), IsChecked = initial
            };
            card.Children.Add(toggle);
            return toggle;
        }

        void RefreshState()
        {
            bool connected = IsCastingAudio;
            bool activeSignal = opened && pendingTrack == null && player.OutputFormat != null && player.ActiveOutputId != null;
            var currentFormat = activeSignal ? player.OutputFormat : snapshot?.MixFormat;
            bool fallback = activeSignal && player.OutputId != null && !SameDevice(player.OutputId, player.ActiveOutputId);
            active.Text = activeSignal ? LocalTextExtension.Format(playing ? "Sonando en: {0}" : "Salida abierta · {0}", player.ActiveOutputName)
                : snapshot?.ActiveName is string available ? LocalTextExtension.Format("Dispositivo disponible: {0}", available) : T("Sin dispositivo activo");
            format.Text = currentFormat == null ? "" : LocalTextExtension.Format("{0:0.###} kHz · {1} bit · {2}",
                currentFormat.SampleRate / 1000d, currentFormat.BitsPerSample,
                currentFormat.Channels == 1 ? T("1 canal") : LocalTextExtension.Format("{0} canales", currentFormat.Channels));
            format.Visibility = currentFormat == null ? Visibility.Collapsed : Visibility.Visible;
            status.Text = applying ? T("Aplicando salida…") : updatingDevices ? T("Consultando dispositivos…")
                : fallback ? LocalTextExtension.Format("Salida solicitada: {0}", (choice.SelectedItem as AudioOutputInfo)?.Name)
                : activeSignal && !player.StrictDirect && player.OutputNotice != null ? player.OutputNotice
                : snapshot?.Error ?? (snapshot?.MissingDevice == true ? T("La salida guardada no está disponible; se muestra la predeterminada.") : "");
            status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            routeMode.Text = connected ? T("Transmisión inalámbrica") : activeSignal ? AudioOutputMode() : T("Sin ruta de reproducción activa");
            rates.Text = activeSignal && !connected && player.SourceFormat != null
                ? LocalTextExtension.Format("Ruta de frecuencia: {0} → {1} kHz",
                    (player.SourceFormat.SampleRate / 1000d).ToString("0.###"), (currentFormat.SampleRate / 1000d).ToString("0.###")) : "";
            if (rates.Text.Length > 0 && player.SourceFormat.SampleRate != currentFormat.SampleRate)
                rates.Text += " · " + T("Conversión de frecuencia");
            rates.Visibility = rates.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            routeDetail.Text = connected ? T("Windows negocia el formato con el dispositivo de destino.")
                : activeSignal ? (player.StrictDirect ? T("Prisma entrega el formato original a WASAPI exclusivo.")
                    : player.IsExclusive ? T("WASAPI exclusivo usa el formato indicado para esta sesión.")
                    : T("Windows puede volver a convertir la señal en modo compartido."))
                : player.OutputNotice ?? T("Reproduce una pista para ver el formato y el modo de salida activos.");
            cast.Content = T(connected ? "Desconectar" : "Transmitir a dispositivo");
            cast.IsEnabled = !castBusy && (connected || current != null || Selected != null);
            castStatus.Text = castFailure ?? "";
            castStatus.Visibility = castStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            refresh.IsEnabled = !applying && !updatingDevices;
            choice.IsEnabled = !applying && !updatingDevices && snapshot != null;
            direct.IsEnabled = !applying;
            exclusive.IsEnabled = bypass.IsEnabled = !applying && !player.StrictDirect;
        }

        direct.Click += async (_, _) =>
        {
            player.StrictDirect = direct.IsChecked == true;
            UpdateVolumeControls();
            await ApplyAndRefresh();
        };
        exclusive.Click += async (_, _) =>
        {
            player.Exclusive = exclusive.IsChecked == true;
            await ApplyAndRefresh();
        };
        bypass.Click += async (_, _) =>
        {
            player.Direct = bypass.IsChecked == true;
            await ApplyAndRefresh();
        };
        refresh.Click += async (_, _) => await ReadDevices();
        card.Loaded += async (_, _) => await ReadDevices();
        WatchSettingsCard(card, 300, RefreshState);
    }

    private StackPanel AddSoundSubsection(StackPanel card, string label)
    {
        card.Children.Add(new Border
        {
            Height = 1, Background = (Brush)FindResource("WorkspaceStroke"), Margin = new Thickness(0, 10, 0, 14)
        });
        var section = new StackPanel();
        section.Children.Add(new TextBlock
        {
            Text = T(label), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7)
        });
        card.Children.Add(section);
        return section;
    }

    private static void WatchSettingsCard(FrameworkElement card, int milliseconds, Action refresh)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => refresh();
        card.Loaded += (_, _) => { refresh(); timer.Start(); };
        card.Unloaded += (_, _) => timer.Stop();
    }
}
