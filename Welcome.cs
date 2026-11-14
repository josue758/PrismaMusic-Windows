using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string WelcomeFile => Path.Combine(DataDirectory, "welcome-complete");

    private void ShowWelcomeOnFirstRun()
    {
        if (library.Tracks.Count == 0 && library.Folders.Count == 0 && !File.Exists(WelcomeFile))
            ShowWelcome(this, new RoutedEventArgs());
    }

    private void ShowWelcome(object sender, RoutedEventArgs args)
    {
        string T(string text) => LocalTextExtension.Translate(text);
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "PRISMA MUSIC", Foreground = Brushes.Lavender, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = T("Tu música, en un solo lugar"), FontWeight = FontWeights.SemiBold,
            FontSize = 29, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
        panel.Children.Add(new TextBlock { Text = T("Añade una carpeta local o de red, o prueba la aplicación con las pistas de demostración."),
            Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var dialog = new Window { Owner = this, Title = T("Primeros pasos"), Width = 510, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(31, 26, 42)), Content = panel };
        Action next = null;
        var actions = new WrapPanel();
        void Add(string label, Action action)
        {
            var button = new Button { Content = T(label), Margin = new Thickness(0, 0, 8, 6) };
            button.Click += (_, _) => { next = action; dialog.Close(); };
            actions.Children.Add(button);
        }
        Add("Añadir carpeta", () => AddFolder(this, new RoutedEventArgs()));
        Add("Probar demos", () => AddDemos(this, new RoutedEventArgs()));
        Add("Ahora no", null);
        panel.Children.Add(actions);
        dialog.ShowDialog();
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(WelcomeFile, "1");
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
        next?.Invoke();
    }
}
