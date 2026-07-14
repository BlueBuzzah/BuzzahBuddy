using System.Globalization;
using BuzzahBuddy.Helpers;
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an integer value to a text color based on whether it matches the
/// converter parameter. Returns PrimaryDarkText if matched (dark text on the
/// blue selected fill), Primary otherwise.
/// </summary>
public class IntToTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IntMatch.Matches(value, parameter)
            ? ColorResources.Get("PrimaryDarkText", DesignColors.PrimaryDarkText)
            : ColorResources.Get("Primary", DesignColors.Primary);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(IntToTextColorConverter)} is one-way only.");
}
