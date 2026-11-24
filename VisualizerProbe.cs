using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PrismaMusic;

public partial class MainWindow
{
    private async Task VerifyVisualizerControls(bool useFixtureSpectrum)
    {
        var root = (Grid)visualizerWindow.Content;
        visualizerWindow.UpdateLayout();
        ShowVisualizerOptions(visualizerOptionsButton, new RoutedEventArgs());
        if (visualizerOptionsPanel == null) throw new InvalidOperationException("The visualizer settings panel did not open.");
        root.UpdateLayout();
        var choices = Children<RadioButton>(visualizerOptionsPanel).ToArray();
        if (choices.Count(choice => choice.Tag is string) != VisualizerStyles.Length ||
            choices.Count(choice => choice.Tag is int) != VisualizerRates.Length)
            throw new InvalidOperationException("The visualizer settings panel is missing styles or frame rates.");

        var controlsPosition = visualizerSeek.TranslatePoint(new Point(0, 0), root);
        var trackPosition = visualizerTitle.TranslatePoint(new Point(0, 0), root);
        foreach (var (value, label, _) in VisualizerStyles.OrderBy(style => style.Value == "nebula"))
        {
            choices.Single(item => Equals(item.Tag, value)).IsChecked = true;
            root.UpdateLayout();
            if (visualizerStyle != value || visualizerStyleLabel.Text != T(label) ||
                !ReferenceEquals(visualizerSpectrum.Parent, root) || visualizerSpectrum.ActualWidth != root.ActualWidth ||
                visualizerSpectrum.ActualHeight != root.ActualHeight ||
                visualizerSeek.TranslatePoint(new Point(0, 0), root) != controlsPosition ||
                visualizerTitle.TranslatePoint(new Point(0, 0), root) != trackPosition)
                throw new InvalidOperationException($"The {value} style did not preserve the full-screen layout.");
        }
        choices.Single(item => Equals(item.Tag, 60)).IsChecked = true;
        Children<Slider>(visualizerOptionsPanel).Single().Value = 1.45;
        if (visualizerStyle != "nebula" || visualizerStyleLabel.Text != T("Nebulosa") || !ReferenceEquals(visualizerSpectrum.Parent, root) ||
            visualizerFramesPerSecond != 60 || Math.Abs(visualizerSensitivity - 1.45) > .001)
            throw new InvalidOperationException("Nebula layout, sensitivity or frame rate did not update.");

        if (useFixtureSpectrum)
        {
            visualizerTimer.Stop();
            FeedPreviewSpectrum();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        else await Task.Delay(160);
        CaptureDesignPreview(root, "PRISMA_VISUALIZER_OPTIONS_PREVIEW");

        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(visualizerOptionsPanel),
            Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        visualizerOptionsPanel.RaiseEvent(key);
        if (visualizerOptionsLayer != null || !visualizerWindow.IsVisible)
            throw new InvalidOperationException("Escape did not dismiss the options without leaving the visualizer.");
        CaptureDesignPreview(root, "PRISMA_VISUALIZER_PREVIEW");
        if (Environment.GetEnvironmentVariable("PRISMA_VISUALIZER_STYLES_PREVIEW") is { } folder)
        {
            Directory.CreateDirectory(folder);
            foreach (var (value, _, _) in VisualizerStyles)
            {
                visualizerStyle = value;
                UpdateVisualizer();
                if (useFixtureSpectrum) FeedPreviewSpectrum();
                else await Task.Delay(180);
                SaveDesignPreview(root, Path.Combine(folder, $"{value}.png"));
            }
        }
        Console.WriteLine("Visualizer settings, all six full-screen styles, fixed controls and Escape navigation passed.");

        void FeedPreviewSpectrum()
        {
            float[] spectrum = { .72f, .68f, .50f, .42f, .58f, .38f, .42f, .22f, .48f };
            for (int frame = 0; frame < 24; frame++)
                visualizerSpectrum.Update(spectrum, true, visualizerSensitivity, visualizerFramesPerSecond,
                    visualizerStyle, visualizerAccent, animate: true);
        }

        static IEnumerable<TElement> Children<TElement>(DependencyObject element) where TElement : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is TElement found) yield return found;
                foreach (var nested in Children<TElement>(child)) yield return nested;
            }
        }
    }
}
