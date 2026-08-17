using System.Globalization;
using System.Windows.Data;

namespace AirCOM.App.Views;

/// <summary>Converts ConnectionMode (int) to RadioButton IsChecked: true when value == parameter.</summary>
public sealed class ModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int mode && parameter is string ps && int.TryParse(ps, out var p))
            return mode == p;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Two-way: when a radio is checked, set the mode from its parameter.
        if (value is true && parameter is string ps && int.TryParse(ps, out var p))
            return p;
        return System.Windows.DependencyProperty.UnsetValue;
    }
}
