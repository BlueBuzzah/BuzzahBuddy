using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts a numeric value to a boolean indicating if it equals zero.
/// Used to detect empty collections.
/// </summary>
public class IsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return true; // Null is treated as zero/empty

        if (int.TryParse(value.ToString(), out int intValue))
        {
            return intValue == 0;
        }

        if (double.TryParse(value.ToString(), out double doubleValue))
        {
            return Math.Abs(doubleValue) < 0.001; // Handle floating point comparison
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
