using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PrismaMusic;

public partial class MainWindow
{
    private sealed record AudioOutputInfo(string Name, string Id);

    private void ShowAudioOutputMenu(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 36, 44)),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(8),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x55, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            MinWidth = 390,
            MaxWidth = 510,
            MaxHeight = SystemParameters.WorkArea.Height - 32
        };
        var menuItemStyle = new Style(typeof(MenuItem));
        menuItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        menuItemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1)));
        menuItemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        menuItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
        menuItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        menuItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var headerText = new FrameworkElementFactory(typeof(TextBlock));
        headerText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        headerText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        headerText.SetValue(TextBlock.MaxWidthProperty, 450d);
        menuItemStyle.Setters.Add(new Setter(MenuItem.HeaderTemplateProperty, new DataTemplate { VisualTree = headerText }));
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x34, 180, 150, 255))));
        menuItemStyle.Triggers.Add(highlighted);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.ForegroundProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 153, 165))));
        menuItemStyle.Triggers.Add(disabled);
        menu.Resources.Add(typeof(MenuItem), menuItemStyle);
        var separatorStyle = new Style(typeof(Separator));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1d));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 5, 10, 5)));
        separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x35, 255, 255, 255))));
        menu.Resources.Add(typeof(Separator), separatorStyle);
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate("Salida de audio"),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        });
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate("Los DAC deben estar habilitados como salida de audio en Windows."),
            IsEnabled = false
        });

        int[] supportedOutputRates = Array.Empty<int>();
        try
        {
            using var devices = new MMDeviceEnumerator();
            var outputs = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => { using (device) return new AudioOutputInfo(device.FriendlyName, device.ID); })
                .ToList();
            MMDevice selectedDevice;
            try
            {
                selectedDevice = player.OutputId == null
                    ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : devices.GetDevice(player.OutputId);
            }
            catch (COMException) when (player.OutputId != null)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = LocalTextExtension.Translate("La salida guardada no está disponible; se muestra la predeterminada."),
                    IsEnabled = false
                });
                selectedDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            using (var activeDevice = selectedDevice)
            {
                WaveFormat mixFormat;
                using (var client = activeDevice.AudioClient) mixFormat = client.MixFormat;
                string activeName = outputs.FirstOrDefault(output => output.Id == activeDevice.ID)?.Name
                    ?? activeDevice.FriendlyName;
                menu.Items.Add(new MenuItem
                {
                    Header = LocalTextExtension.Format("Salida activa: {0}", activeName),
                    IsEnabled = false
                });
                menu.Items.Add(new MenuItem
                {
                    Header = player.StrictDirect && player.IsExclusive && !string.IsNullOrEmpty(player.OutputNotice)
                        ? LocalTextExtension.Format("Formato actual del dispositivo: {0}", player.OutputNotice)
                        : opened && player.OutputFormat is WaveFormat outputFormat
                            ? LocalTextExtension.Format("Formato actual del dispositivo: {0} Hz · {1} canales · {2} bits",
                                outputFormat.SampleRate.ToString("N0"), outputFormat.Channels, outputFormat.BitsPerSample)
                            : LocalTextExtension.Format("Formato actual del dispositivo: {0} Hz · {1} canales · {2} bits",
                                mixFormat.SampleRate.ToString("N0"), mixFormat.Channels, mixFormat.BitsPerSample),
                    IsEnabled = false
                });
                menu.Items.Add(new MenuItem
                {
                    Header = LocalTextExtension.Format("Modo de salida actual: {0}",
                        player.StrictDirect ? LocalTextExtension.Translate("PCM directo") :
                        player.IsExclusive ? LocalTextExtension.Translate("WASAPI exclusivo") :
                        LocalTextExtension.Translate("WASAPI compartido")),
                    IsEnabled = false
                });
                try
                {
                    using var client = activeDevice.AudioClient;
                    supportedOutputRates = SupportedOutputRates(client, mixFormat.Channels);
                    string rates = string.Join(" · ", SupportedExclusiveRates(client, mixFormat)
                        .Select(rate => (rate / 1000d).ToString("0.#") + " kHz"));
                    menu.Items.Add(new MenuItem
                    {
                        Header = LocalTextExtension.Format("Frecuencias PCM exclusivas compatibles: {0}",
                            string.IsNullOrEmpty(rates) ? LocalTextExtension.Translate("No se pudieron consultar") : rates),
                        IsEnabled = false
                    });
                }
                catch (COMException error)
                {
                    menu.Items.Add(new MenuItem
                    {
                        Header = LocalTextExtension.Translate("No se pudieron consultar las frecuencias del dispositivo.") + " " + error.Message,
                        IsEnabled = false
                    });
                }
            }
            outputs.Insert(0, new AudioOutputInfo(LocalTextExtension.Translate("Predeterminada del sistema"), null));
            menu.Items.Add(new Separator());

            foreach (var output in outputs)
            {
                bool selected = string.Equals(output.Id, player.OutputId, StringComparison.OrdinalIgnoreCase);
                var item = new MenuItem { Header = (selected ? "✓  " : "    ") + output.Name };
                item.Click += (_, _) =>
                {
                    player.OutputId = output.Id;
                    SaveAudioSettings();
                    _ = ReopenOutput();
                    Status.Text = LocalTextExtension.Format("Salida de audio seleccionada: {0}", output.Name);
                    UpdateAudioOutputTooltip();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LocalTextExtension.Translate("Frecuencia de salida WASAPI exclusivo"),
                IsEnabled = false
            });
            var automaticRate = new MenuItem
            {
                Header = LocalTextExtension.Translate("Automática · seguir la frecuencia de la pista") +
                    (player.OutputSampleRate == 0 ? "  ✓" : ""),
                IsEnabled = !player.StrictDirect,
                Tag = 0
            };
            void SelectOutputRate(MenuItem choice)
            {
                choice.Click += (_, _) =>
                {
                    player.OutputSampleRate = (int)choice.Tag;
                    player.Exclusive = true;
                    SaveAudioSettings();
                    _ = ReopenOutput();
                    Status.Text = player.OutputSampleRate == 0
                        ? LocalTextExtension.Translate("La frecuencia exclusiva seguirá automáticamente la frecuencia de cada pista.")
                        : LocalTextExtension.Format("Frecuencia WASAPI exclusiva solicitada: {0} kHz",
                            (player.OutputSampleRate / 1000d).ToString("0.#"));
                };
                menu.Items.Add(choice);
            }
            SelectOutputRate(automaticRate);
            foreach (int rate in supportedOutputRates)
                SelectOutputRate(new MenuItem
                {
                    Header = $"{rate / 1000d:0.#} kHz" + (player.OutputSampleRate == rate ? "  ✓" : ""),
                    IsEnabled = !player.StrictDirect,
                    Tag = rate
                });
            if (player.OutputSampleRate != 0 && !supportedOutputRates.Contains(player.OutputSampleRate))
                menu.Items.Add(new MenuItem
                {
                    Header = LocalTextExtension.Format("La salida actual no anuncia {0} kHz",
                        (player.OutputSampleRate / 1000d).ToString("0.#")),
                    IsEnabled = false
                });
            menu.Items.Add(new MenuItem
            {
                Header = LocalTextExtension.Translate("La frecuencia manual convierte la pista; PCM directo conserva el formato original."),
                IsEnabled = false
            });
            var refresh = new MenuItem { Header = LocalTextExtension.Translate("Actualizar dispositivos") };
            refresh.Click += (_, _) =>
            {
                menu.IsOpen = false;
                ShowAudioOutputMenu(AudioOutputButton, new RoutedEventArgs());
            };
            menu.Items.Insert(2, refresh);
        }
        catch (Exception error)
        {
            menu.Items.Add(new MenuItem
            {
                Header = LocalTextExtension.Translate("No se pudieron consultar las salidas de audio.") + " " + error.Message,
                IsEnabled = false
            });
        }

        menu.Items.Add(new Separator());
        var exclusive = new MenuItem
        {
            Header = LocalTextExtension.Translate("Solicitar acceso exclusivo"),
            IsCheckable = true,
            IsChecked = player.Exclusive
        };
        exclusive.Click += (_, _) =>
        {
            player.Exclusive = exclusive.IsChecked;
            SaveAudioSettings();
            _ = ReopenOutput();
        };
        menu.Items.Add(exclusive);

        var direct = new MenuItem
        {
            Header = LocalTextExtension.Translate("PCM directo exclusivo (WAV, AIFF y FLAC)"),
            IsCheckable = true,
            IsChecked = player.StrictDirect
        };
        direct.Click += (_, _) =>
        {
            player.StrictDirect = direct.IsChecked;
            UpdateVolumeControls();
            SaveAudioSettings();
            _ = ReopenOutput();
        };
        menu.Items.Add(direct);
        menu.Items.Add(new MenuItem
        {
            Header = LocalTextExtension.Translate(player.StrictDirect
                ? "PCM directo sin volumen ni procesamiento; requiere un formato y dispositivo compatibles."
                : "PCM directo evita el procesamiento y solo admite formatos compatibles."),
            IsEnabled = false
        });

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        AudioOutputButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

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
        string mode = player.StrictDirect
            ? LocalTextExtension.Translate("PCM directo")
            : player.IsExclusive
                ? LocalTextExtension.Translate("WASAPI exclusivo")
                : LocalTextExtension.Translate("WASAPI compartido");
        AudioOutputButton.ToolTip = LocalTextExtension.Format("Salida de audio · {0}", mode);
        System.Windows.Automation.AutomationProperties.SetName(AudioOutputButton,
            LocalTextExtension.Translate("Salida de audio") + " · " + mode);
    }
}
