namespace BuzzahBuddy.Helpers;

/// <summary>
/// Looks up a design-system color token from the app resource dictionary,
/// falling back to the dark theme's value in <see cref="DesignColors"/> when the
/// application resources are unavailable (unit tests, early startup).
/// </summary>
public static class ColorResources
{
    public static Color Get(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Color color
            ? color
            : DesignColors.Themes["dark"][key];
}
