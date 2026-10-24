using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinClonerX.App.Converters;

/// <summary>Shows the element when the bound value is not null (and not an empty string); collapses it otherwise.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null and not string { Length: 0 };
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("A visibility cannot be converted back to a value.");
    }
}
