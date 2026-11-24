using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private DispatcherTimer visualizerSaveTimer;

    private void ShowVisualizerOptions(object sender, RoutedEventArgs args)
    {
        if (visualizerWindow?.Content is not Grid root) return;
        if (visualizerOptionsLayer != null)
        {
            CloseVisualizerOptions();
            return;
        }

        var layer = new Grid();
        Grid.SetRowSpan(layer, 3);
        var scrim = new Border { Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(78, 0, 0, 0)) };
        scrim.MouseLeftButtonDown += (_, _) => CloseVisualizerOptions();
        layer.Children.Add(scrim);

        var layout = new DockPanel { Margin = new Thickness(18, 15, 18, 15) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 13) };
        var close = CreateVisualizerControl("\uE8BB", T("Cerrar"), (_, _) => CloseVisualizerOptions());
        close.Width = close.Height = 34;
        close.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = T("Opciones del visualizador"), FontSize = 21,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var body = new StackPanel();
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Style = (Style)FindResource("LyricsScroll") };
        layout.Children.Add(scroll);
        var panel = new Border { Width = Math.Min(428, Math.Max(250, root.ActualWidth - 36)),
            MaxHeight = Math.Max(140, root.ActualHeight - 38), CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 17, 20, 17), Child = layout };
        panel.SetResourceReference(Border.BackgroundProperty, "SettingsBackground");
        panel.SetResourceReference(Border.BorderBrushProperty, "WorkspaceStroke");
        layout.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(panel, T("Opciones del visualizador"));
        layer.Children.Add(panel);

        var styleCard = AddSettingsCard(body, "Estilo");
        var presets = new UniformGrid { Columns = 2 };
        foreach (var (value, label, description) in VisualizerStyles)
            presets.Children.Add(StyleChoice(value, label, description));
        styleCard.Children.Add(presets);

        var response = AddSettingsCard(body, "Sensibilidad");
        var sensitivity = new Slider { Minimum = .5, Maximum = 2, Value = visualizerSensitivity,
            TickFrequency = .05, IsSnapToTickEnabled = true, SmallChange = .05,
            LargeChange = .25, IsMoveToPointEnabled = true, Style = (Style)FindResource("TransportSlider") };
        AutomationProperties.SetName(sensitivity, T("Sensibilidad"));
        var readout = new TextBlock { Text = SensitivityText(), FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, MinWidth = 45, TextAlignment = TextAlignment.Right };
        readout.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var scale = new DockPanel();
        DockPanel.SetDock(readout, Dock.Right);
        scale.Children.Add(readout);
        scale.Children.Add(sensitivity);
        response.Children.Add(scale);
        sensitivity.ValueChanged += (_, change) =>
        {
            visualizerSensitivity = change.NewValue;
            readout.Text = SensitivityText();
            UpdateVisualizer();
            QueueVisualizerSave();
        };

        var frameRate = AddSettingsCard(body, "Fluidez");
        var rates = new UniformGrid { Columns = VisualizerRates.Length };
        frameRate.Children.Add(rates);
        foreach (int rate in VisualizerRates)
        {
            var choice = Choice($"{rate} FPS", "VisualizerFrameRate", visualizerFramesPerSecond == rate);
            choice.HorizontalContentAlignment = HorizontalAlignment.Center;
            choice.Tag = rate;
            choice.Checked += (_, _) =>
            {
                visualizerFramesPerSecond = rate;
                if (visualizerTimer != null)
                    visualizerTimer.Interval = TimeSpan.FromMilliseconds(1000d / rate);
                QueueVisualizerSave();
            };
            rates.Children.Add(choice);
        }

        visualizerOptionsLayer = layer;
        visualizerOptionsPanel = panel;
        root.Children.Add(layer);
        AutomationProperties.SetItemStatus(visualizerOptionsButton, T("Expandido"));
        close.Focus();

        string SensitivityText() => visualizerSensitivity.ToString("0.##", CultureInfo.CurrentCulture) + "×";

        RadioButton StyleChoice(string value, string label, string help)
        {
            var copy = new StackPanel();
            copy.Children.Add(new TextBlock { Text = T(label), FontWeight = FontWeights.SemiBold });
            var hint = SettingsHint(help);
            hint.Margin = new Thickness(0, 3, 0, 0);
            copy.Children.Add(hint);
            var item = new DockPanel();
            var icon = VisualizerPresetSymbol(value);
            DockPanel.SetDock(icon, Dock.Left);
            item.Children.Add(icon);
            item.Children.Add(copy);
            var choice = Choice(T(label), "VisualizerStyle", visualizerStyle == value);
            choice.Tag = value;
            choice.Content = item;
            choice.Checked += (_, _) =>
            {
                visualizerStyle = value;
                UpdateVisualizer();
                QueueVisualizerSave();
            };
            return choice;
        }

        RadioButton Choice(string label, string group, bool selected)
        {
            var choice = new RadioButton { Content = label, GroupName = group, IsChecked = selected,
                Style = (Style)FindResource("VisualizerChoice") };
            AutomationProperties.SetName(choice, label);
            return choice;
        }
    }

    private static Canvas VisualizerPresetSymbol(string style)
    {
        var icon = new Canvas { Width = 25, Height = 22, Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        if (style == "nebula")
        {
            Dot(11, 11, 5, 1);
            Dot(4, 5, 2.5, .68);
            Dot(19, 4, 3, .82);
            Dot(21, 14, 2.2, .67);
            Dot(5, 17, 3, .85);
            var orbit = new Ellipse { Width = 20, Height = 8, StrokeThickness = 1, Opacity = .5,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(-29) };
            orbit.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            Canvas.SetLeft(orbit, 1);
            Canvas.SetTop(orbit, 7);
            icon.Children.Add(orbit);
        }
        else if (style == "orbit")
        {
            var ring = new Ellipse { Width = 13, Height = 13, StrokeThickness = 1.1, Opacity = .75 };
            ring.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            Canvas.SetLeft(ring, 5.5);
            Canvas.SetTop(ring, 4.5);
            icon.Children.Add(ring);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.Tau / 8;
                Dot(12 + Math.Cos(angle) * 9.5, 11 + Math.Sin(angle) * 9.5, i % 2 == 0 ? 2.6 : 1.9, .9);
            }
        }
        else if (style == "aurora")
        {
            foreach (double offset in new[] { 2.0, 6.0 })
            {
                var curve = new PathFigure { StartPoint = new Point(1, 11 + offset) };
                curve.Segments.Add(new BezierSegment(new Point(8, offset), new Point(14, 20 + offset), new Point(23, 6 + offset), true));
                var ribbon = new Path { Data = new PathGeometry(new[] { curve }), StrokeThickness = 1.2, Opacity = .83 };
                ribbon.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                icon.Children.Add(ribbon);
            }
        }
        else
        {
            int[] heights = { 4, 10, 15, 8, 5 };
            for (int i = 0; i < heights.Length; i++)
            {
                double x = 2 + i * 4;
                double h = heights[i];
                double y = style == "centered" ? 11 - h / 2 : style == "mirror" ? 14 - h * .7 : 19 - h;
                Bar(x, y, style == "mirror" ? h * .7 : h, .9);
                if (style == "mirror") Bar(x, 15, h * .30, .45);
            }
        }
        return icon;

        void Dot(double x, double y, double size, double opacity)
        {
            var dot = new Ellipse { Width = size, Height = size, Opacity = opacity };
            dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
            Canvas.SetLeft(dot, x - size / 2);
            Canvas.SetTop(dot, y - size / 2);
            icon.Children.Add(dot);
        }

        void Bar(double x, double y, double height, double opacity)
        {
            var bar = new Rectangle { Width = 2.4, Height = height, RadiusX = 1, RadiusY = 1, Opacity = opacity };
            bar.SetResourceReference(Shape.FillProperty, "AccentBrush");
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            icon.Children.Add(bar);
        }
    }

    private void CloseVisualizerOptions()
    {
        if (visualizerOptionsLayer == null || visualizerWindow?.Content is not Grid root) return;
        root.Children.Remove(visualizerOptionsLayer);
        visualizerOptionsLayer = null;
        visualizerOptionsPanel = null;
        FlushVisualizerSave();
        AutomationProperties.SetItemStatus(visualizerOptionsButton, T("Contraído"));
        visualizerOptionsButton.Focus();
    }

    private void QueueVisualizerSave()
    {
        if (visualizerSaveTimer == null)
        {
            visualizerSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(325) };
            visualizerSaveTimer.Tick += (_, _) => FlushVisualizerSave();
        }
        visualizerSaveTimer.Stop();
        visualizerSaveTimer.Start();
    }

    private void FlushVisualizerSave()
    {
        if (visualizerSaveTimer?.IsEnabled != true) return;
        visualizerSaveTimer.Stop();
        SaveAudioSettings();
    }
}
