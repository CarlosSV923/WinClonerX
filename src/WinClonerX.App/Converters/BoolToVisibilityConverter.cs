using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinClonerX.App.Converters;

/// <summary>
/// True shows the element, false collapses it. With the parameter <c>Invert</c> the rule is reversed.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    private const string InvertParameter = "Invert";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is InvertParameter)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("A visibility cannot be converted back to a flag.");
    }
}
