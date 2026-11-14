using System.Globalization;
using System.Windows.Data;

namespace PricePin.Controls;

/// <summary>
/// Binds a group of RadioButtons to one enum or string property:
/// IsChecked="{Binding Size, Converter={StaticResource Equals}, ConverterParameter=Large}".
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
            return Binding.DoNothing;
        var text = parameter.ToString()!;
        return targetType.IsEnum ? Enum.Parse(targetType, text, ignoreCase: true) : text;
    }
}
