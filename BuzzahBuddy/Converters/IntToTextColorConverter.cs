using System.Globalization;
using BuzzahBuddy.Helpers;
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Converters;

/// <summary>
/// Converts an integer value to a text color based on whether it matches the
/// converter parameter. Returns OnBrand if matched (text on the
/// brand selected fill), Brand otherwise.
/// </summary>
public class IntToTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IntMatch.Matches(value, parameter)
            ? ColorResources.Get("OnBrand")
            : ColorResources.Get("Brand");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(IntToTextColorConverter)} is one-way only.");
}
