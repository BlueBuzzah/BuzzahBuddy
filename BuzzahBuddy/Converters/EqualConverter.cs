using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts a value to a boolean indicating if it equals the converter parameter.
/// </summary>
public class EqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Try to convert parameter to the same type as value
        if (int.TryParse(value.ToString(), out int intValue) && int.TryParse(parameter.ToString(), out int intParameter))
        {
            return intValue == intParameter;
        }

        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
