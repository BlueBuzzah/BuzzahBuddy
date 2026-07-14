using System.Globalization;
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an integer value to a text color based on whether it matches the converter parameter.
/// Returns PrimaryDarkText color if matched, Primary color otherwise.
/// </summary>
public class IntToTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Application.Current?.Resources.TryGetValue("Primary", out var primaryColor) == true
                ? primaryColor
                : Color.FromArgb("#35B6F2");

        bool isSelected = false;
        if (int.TryParse(value.ToString(), out int intValue) && int.TryParse(parameter.ToString(), out int intParameter))
        {
            isSelected = intValue == intParameter;
        }

        if (isSelected)
        {
            return Application.Current?.Resources.TryGetValue("PrimaryDarkText", out var primaryDarkTextColor) == true
                ? primaryDarkTextColor
                : Color.FromArgb("#0a0a0a");
        }
        else
        {
            return Application.Current?.Resources.TryGetValue("Primary", out var primaryColor) == true
                ? primaryColor
                : Color.FromArgb("#35B6F2");
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
