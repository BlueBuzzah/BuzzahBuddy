using System.Globalization;
using BuzzahBuddy.Helpers;
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an integer value to a background color based on whether it matches
/// the converter parameter. Returns Primary if matched, Secondary otherwise.
/// </summary>
public class IntToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IntMatch.Matches(value, parameter)
            ? ColorResources.Get("Primary", DesignColors.Primary)
            : ColorResources.Get("Secondary", DesignColors.Secondary);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(IntToColorConverter)} is one-way only.");
}
