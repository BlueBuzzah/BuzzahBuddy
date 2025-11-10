using System.Globalization;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Multi-value converter that returns true only if all input values are true.
/// Used for complex visibility logic with multiple conditions.
/// </summary>
public class AllTrueConverter : IMultiValueConverter
{
    public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Length == 0)
            return false;

        foreach (var value in values)
        {
            if (value is not bool boolValue || !boolValue)
                return false;
        }

        return true;
    }

    public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
