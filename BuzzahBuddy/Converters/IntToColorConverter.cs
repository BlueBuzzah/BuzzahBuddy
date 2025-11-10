using System.Globalization;
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an integer value to a color based on whether it matches the converter parameter.
/// Returns Primary color if matched, Secondary color otherwise.
/// </summary>
public class IntToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Application.Current?.Resources.TryGetValue("Secondary", out var secondaryColor) == true
                ? secondaryColor
                : Colors.Gray;

        bool isSelected = false;
        if (int.TryParse(value.ToString(), out int intValue) && int.TryParse(parameter.ToString(), out int intParameter))
        {
            isSelected = intValue == intParameter;
        }

        if (isSelected)
        {
            return Application.Current?.Resources.TryGetValue("Primary", out var primaryColor) == true
                ? primaryColor
                : Colors.Blue;
        }
        else
        {
            return Application.Current?.Resources.TryGetValue("Secondary", out var secondaryColor) == true
                ? secondaryColor
                : Colors.Gray;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
