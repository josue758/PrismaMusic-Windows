using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrismaMusic;

public partial class MainWindow
{
    private int LibrarySectionIndex
    {
        get => int.TryParse(Navigation.SelectedValue as string, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? index : -1;
        set => Navigation.SelectedValue = value.ToString(CultureInfo.InvariantCulture);
    }

    private static readonly DependencyPropertyKey CurrentTrackPathPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CurrentTrackPath), typeof(string), typeof(MainWindow), new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentTrackPathProperty = CurrentTrackPathPropertyKey.DependencyProperty;

    public string CurrentTrackPath
    {
        get => (string)GetValue(CurrentTrackPathProperty);
        private set => SetValue(CurrentTrackPathPropertyKey, value);
    }
}

public sealed class LibraryTrackMatches : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string path && values[1] is string current &&
        string.Equals(path, current, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
