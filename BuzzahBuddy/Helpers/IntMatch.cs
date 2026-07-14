namespace BuzzahBuddy.Helpers;

/// <summary>
/// Shared selected-state logic for the Int*Converter pair: a binding value
/// matches a converter parameter when both parse as the same integer.
/// </summary>
public static class IntMatch
{
    public static bool Matches(object? value, object? parameter) =>
        value is not null
        && parameter is not null
        && int.TryParse(value.ToString(), out var intValue)
        && int.TryParse(parameter.ToString(), out var intParameter)
        && intValue == intParameter;
}
