using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an enum value to a boolean by comparing it with a parameter value.
/// Returns true if the value equals the parameter.
/// </summary>
/// <example>
/// Usage in XAML:
/// IsVisible="{Binding DashboardState, Converter={StaticResource EnumToBoolConverter},
///             ConverterParameter={x:Static models:DashboardState.Connecting}}"
/// </example>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Direct enum comparison
        if (value.GetType().IsEnum && parameter.GetType().IsEnum)
        {
            return value.Equals(parameter);
        }

        // Fallback to string comparison
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
