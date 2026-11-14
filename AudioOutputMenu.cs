using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record AudioOutputInfo(string Name, string Id)
    {
        public override string ToString() => Name;
    }
    private sealed record AudioOutputSnapshot(AudioOutputInfo[] Devices, string ActiveName, WaveFormat MixFormat,
        int[] Rates, int[] ExclusiveRates, bool MissingDevice, string Error);

    private Popup audioOutputPopup;
    private Border audioOutputSurface;
    private ComboBox audioDevicePicker;
    private ComboBox audioRatePicker;

    private async void ShowAudioOutputMenu(object sender, RoutedEventArgs args)
    {
        if (audioOutputPopup?.IsOpen == true)
        {
            audioOutputPopup.IsOpen = false;
            return;
        }

        var anchor = (FrameworkElement)sender;
        var layout = new DockPanel { Margin = new Thickness(16) };
        TextElement.SetFontFamily(layout, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        TextElement.SetFontSize(layout, 13);
        KeyboardNavigation.SetTabNavigation(layout, KeyboardNavigationMode.Cycle);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var refresh = new Button { Content = "", Style = (Style)FindResource("TransportIcon"),
            ToolTip = T("Actualizar dispositivos"), Width = 32, Height = 32 };
        AutomationProperties.SetName(refresh, T("Actualizar dispositivos"));
        DockPanel.SetDock(refresh, Dock.Right);
        header.Children.Add(refresh);
        header.Children.Add(new TextBlock { Text = T("Salida de audio"), FontSize = 23, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Style = (Style)FindResource("LyricsScroll") };
        layout.Children.Add(scroll);
        var surface = new Border { Width = Math.Min(436, SystemParameters.WorkArea.Width - 24),
            MaxHeight = Math.Min(560, SystemParameters.WorkArea.Height - 44), Background = (Brush)FindResource("SettingsBackground"),
            BorderBrush = (Brush)FindResource("SettingsStroke"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13), Child = layout };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top,
            VerticalOffset = -6, AllowsTransparency = true, StaysOpen = false, Child = surface };
        audioOutputPopup = popup;
        audioOutputSurface = surface;
        popup.Closed += (_, _) =>
        {
            audioOutputPopup = null;
            audioOutputSurface = null;
            audioDevicePicker = null;
            audioRatePicker = null;
        };
        layout.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != Key.Escape || audioDevicePicker?.IsDropDownOpen == true || audioRatePicker?.IsDropDownOpen == true) return;
            key.Handled = true;
            popup.IsOpen = false;
            anchor.Focus();
        };
        refresh.Click += async (_, _) => await Populate();
        popup.IsOpen = true;
        refresh.Focus();
        await Populate();

        async Task Populate()
        {
            refresh.IsEnabled = false;
            content.Children.Clear();
            content.Children.Add(Note(T("Consultando dispositivos…")));
            string outputId = player.OutputId;
            var snapshot = await Task.Run(() => ReadAudioOutputs(outputId));
            if (!popup.IsOpen) return;
            content.Children.Clear();
            refresh.IsEnabled = true;

            Card(T("Dispositivo"), section =>
            {
                var options = new[] { new AudioOutputInfo(T("Predeterminada del sistema"), null) }
                    .Concat(snapshot.Devices).ToList();
                if (player.OutputId != null && options.All(output => !SameDevice(output.Id, player.OutputId)))
                    options.Add(new AudioOutputInfo(T("Dispositivo guardado no disponible"), player.OutputId));
                audioDevicePicker = Picker(T("Dispositivo"), options, options.FindIndex(output => SameDevice(output.Id, player.OutputId)));
                audioDevicePicker.DisplayMemberPath = nameof(AudioOutputInfo.Name);
                audioDevicePicker.SelectionChanged += async (_, _) =>
                {
                    if (audioDevicePicker?.SelectedItem is not AudioOutputInfo output) return;
                    player.OutputId = output.Id;
                    await ApplyChanges();
                };
                section.Children.Add(audioDevicePicker);
                string requestedName = options.FirstOrDefault(output => SameDevice(output.Id, player.OutputId))?.Name;
                if (player.ActiveOutputName is string playingDevice)
                    section.Children.Add(Note(LocalTextExtension.Format("Sonando en: {0}", playingDevice)));
                else if (snapshot.ActiveName != null)
                    section.Children.Add(Note(LocalTextExtension.Format("Dispositivo disponible: {0}", snapshot.ActiveName)));
                if (player.ActiveOutputId != null && player.OutputId != null && !SameDevice(player.ActiveOutputId, player.OutputId))
                    section.Children.Add(Note(LocalTextExtension.Format("Salida solicitada: {0}", requestedName)));
                if (snapshot.Error != null) section.Children.Add(Note(snapshot.Error));
                else if (snapshot.MissingDevice)
                    section.Children.Add(Note(T("La salida guardada no está disponible; se muestra la predeterminada.")));
            });
            if (snapshot.MixFormat != null)
            {
                bool activeSignal = opened && player.ActiveOutputId != null && player.OutputFormat != null;
                var format = activeSignal ? player.OutputFormat : snapshot.MixFormat;
                Card(T(activeSignal ? "Señal de salida" : "Formato predeterminado de Windows"), section =>
                {
                    var signal = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
                    var mode = new TextBlock { Text = AudioOutputMode(), FontSize = 11,
                        Foreground = (Brush)FindResource("AccentBrush"), VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(mode, Dock.Right);
                    signal.Children.Add(mode);
                    signal.Children.Add(new TextBlock { Text = $"{format.SampleRate / 1000d:0.###} kHz · {format.BitsPerSample} bit",
                        FontSize = 19, FontWeight = FontWeights.SemiBold });
                    section.Children.Add(signal);
                    section.Children.Add(Note(format.Channels == 1 ? T("1 canal") : LocalTextExtension.Format("{0} canales", format.Channels)));
                    if (activeSignal && player.SourceFormat is WaveFormat source)
                    {
                        section.Children.Add(Note(LocalTextExtension.Format("Ruta de frecuencia: {0} → {1} kHz",
                            (source.SampleRate / 1000d).ToString("0.###"), (format.SampleRate / 1000d).ToString("0.###"))));
                        if (source.SampleRate != format.SampleRate) section.Children.Add(Note(T("Conversión de frecuencia")));
                        if (!player.IsExclusive) section.Children.Add(Note(T("Windows puede volver a convertir la señal en modo compartido.")));
                        if (player.OutputId != null && !SameDevice(player.ActiveOutputId, player.OutputId))
                            section.Children.Add(Note(T("Se está usando la salida predeterminada.")));
                    }
                });
            }
            Card(T("Modo de salida"), section =>
            {
                Toggle(T("Solicitar acceso exclusivo"), player.Exclusive, !player.StrictDirect, value => player.Exclusive = value);
                Toggle(T("PCM directo"), player.StrictDirect, true, value => { player.StrictDirect = value; UpdateVolumeControls(); });
                if (player.StrictDirect)
                    section.Children.Add(Note(T("PCM directo sin volumen ni procesamiento; requiere un formato y dispositivo compatibles.")));

                if (!player.StrictDirect)
                {
                    var rateValues = new[] { 0 }.Concat(snapshot.Rates).ToList();
                    if (!rateValues.Contains(player.OutputSampleRate)) rateValues.Add(player.OutputSampleRate);
                    var labels = rateValues.Select(rate => rate == 0
                        ? T("Automática · seguir la frecuencia de la pista")
                        : $"{rate / 1000d:0.###} kHz").ToArray();
                    var rateLabel = Note(T("Frecuencia de salida WASAPI exclusivo"));
                    section.Children.Add(rateLabel);
                    audioRatePicker = Picker(rateLabel.Text, labels, rateValues.IndexOf(player.OutputSampleRate));
                    audioRatePicker.Margin = new Thickness(0, 6, 0, 0);
                    audioRatePicker.SelectionChanged += async (_, _) =>
                    {
                        if (audioRatePicker == null || audioRatePicker.SelectedIndex < 0) return;
                        player.OutputSampleRate = rateValues[audioRatePicker.SelectedIndex];
                        player.Exclusive = true;
                        await ApplyChanges();
                    };
                    section.Children.Add(audioRatePicker);
                    if (player.OutputSampleRate > 0 && !snapshot.Rates.Contains(player.OutputSampleRate))
                        section.Children.Add(Note(LocalTextExtension.Format("La salida actual no anuncia {0} kHz", (player.OutputSampleRate / 1000d).ToString("0.###"))));
                }
                else audioRatePicker = null;

                if (snapshot.ExclusiveRates.Length > 0)
                {
                    var details = Note(string.Join(" · ", snapshot.ExclusiveRates.Select(rate => $"{rate / 1000d:0.###} kHz")));
                    details.Visibility = Visibility.Collapsed;
                    var disclosure = new Button { Content = T("Frecuencias PCM compatibles"),
                        HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
                        Margin = new Thickness(0, 9, 0, 0) };
                    AutomationProperties.SetItemStatus(disclosure, T("Contraído"));
                    disclosure.Click += (_, _) =>
                    {
                        bool expand = details.Visibility != Visibility.Visible;
                        details.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                        AutomationProperties.SetItemStatus(disclosure, T(expand ? "Expandido" : "Contraído"));
                    };
                    section.Children.Add(disclosure);
                    section.Children.Add(details);
                }

                void Toggle(string label, bool value, bool enabled, Action<bool> change)
                {
                    var toggle = new CheckBox { Content = label, IsChecked = value, IsEnabled = enabled,
                        Style = (Style)FindResource("SettingsToggle") };
                    toggle.Click += async (_, _) => { change(toggle.IsChecked == true); await ApplyChanges(); };
                    section.Children.Add(toggle);
                }
            });
        }

        async Task ApplyChanges()
        {
            content.IsEnabled = refresh.IsEnabled = false;
            if (!smokeTest) SaveAudioSettings();
            await ReopenOutput();
            UpdateAudioOutputTooltip();
            if (!popup.IsOpen) return;
            content.IsEnabled = true;
            await Populate();
            refresh.Focus();
        }

        void Card(string title, Action<StackPanel> add)
        {
            var section = new StackPanel();
            section.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7) });
            add(section);
            content.Children.Add(new Border { Background = (Brush)FindResource("SettingsCard"),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = section });
        }

        ComboBox Picker(string label, System.Collections.IEnumerable items, int index)
        {
            var picker = new ComboBox { Style = (Style)FindResource("FilterCombo"), ItemsSource = items,
                SelectedIndex = index };
            AutomationProperties.SetName(picker, label);
            return picker;
        }
        TextBlock Note(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0) };
    }

    private static AudioOutputSnapshot ReadAudioOutputs(string outputId)
    {
        var outputs = new System.Collections.Generic.List<AudioOutputInfo>();
        try
        {
            using var devices = new MMDeviceEnumerator();
            foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device) outputs.Add(new AudioOutputInfo(device.FriendlyName, device.ID));
            bool missing = outputId != null && outputs.All(device => !SameDevice(device.Id, outputId));
            using var active = outputId == null || missing
                ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(outputId);
            using var client = active.AudioClient;
            var format = client.MixFormat;
            return new(outputs.ToArray(), active.FriendlyName, format, SupportedOutputRates(client, format.Channels),
                SupportedExclusiveRates(client, format), missing, null);
        }
        catch (Exception error)
        {
            return new(outputs.ToArray(), null, null, Array.Empty<int>(), Array.Empty<int>(), false,
                T("No se pudieron consultar las salidas de audio.") + " " + error.Message);
        }
    }

    private static string T(string label) => LocalTextExtension.Translate(label);
    private static bool SameDevice(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private string AudioOutputMode() => player.StrictDirect && player.IsExclusive
        ? T("PCM directo") : player.IsExclusive ? T("WASAPI exclusivo") : T("WASAPI compartido");

    private static int[] SupportedExclusiveRates(AudioClient client, WaveFormat mixFormat)
    {
        int[] bitDepths = new[] { 16, 24, 32, mixFormat.BitsPerSample }.Distinct().ToArray();
        return AudioSampleRatePolicy.CommonRates.Where(rate => bitDepths.Any(bits =>
        {
            try
            {
                int blockAlign = mixFormat.Channels * bits / 8;
                var format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, rate, mixFormat.Channels,
                    rate * blockAlign, blockAlign, bits);
                return client.IsFormatSupported(AudioClientShareMode.Exclusive, format);
            }
            catch (Exception error) when (error is COMException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        })).ToArray();
    }

    private static int[] SupportedOutputRates(AudioClient client, int channels) =>
        AudioSampleRatePolicy.CommonRates.Where(rate =>
            AudioSampleRatePolicy.SupportsOutputRate(client, rate, channels)).ToArray();

    private void UpdateAudioOutputTooltip()
    {
        string mode = AudioOutputMode();
        AudioOutputButton.ToolTip = LocalTextExtension.Format("Salida de audio · {0}", mode);
        System.Windows.Automation.AutomationProperties.SetName(AudioOutputButton,
            LocalTextExtension.Translate("Salida de audio") + " · " + mode);
    }
}
