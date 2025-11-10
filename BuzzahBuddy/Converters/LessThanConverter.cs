using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts a value to a boolean indicating if it is less than the converter parameter.
/// </summary>
public class LessThanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        if (int.TryParse(value.ToString(), out int intValue) && int.TryParse(parameter.ToString(), out int intParameter))
        {
            return intValue < intParameter;
        }

        if (double.TryParse(value.ToString(), out double doubleValue) && double.TryParse(parameter.ToString(), out double doubleParameter))
        {
            return doubleValue < doubleParameter;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
