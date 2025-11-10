using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Multi-value converter that returns true if all input values are equal.
/// Used for comparing two bound values.
/// </summary>
public class MultiValueEqualConverter : IMultiValueConverter
{
    public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2)
            return false;

        var value1 = values[0];
        var value2 = values[1];

        if (value1 == null || value2 == null)
            return false;

        // Try int comparison first
        if (value1 is int int1 && value2 is int int2)
        {
            return int1 == int2;
        }

        // Fallback to string comparison
        return value1.ToString() == value2.ToString();
    }

    public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
