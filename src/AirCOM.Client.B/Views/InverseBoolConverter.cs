using System.Globalization;
using System.Windows.Data;

namespace AirCOM.Client.B.Views;

/// <summary>Inverts a boolean for IsEnabled bindings (e.g. Start button disabled while running).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
