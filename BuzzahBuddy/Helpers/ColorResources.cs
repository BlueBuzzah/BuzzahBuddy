namespace BuzzahBuddy.Helpers;

/// <summary>
/// Looks up a design-system color token from the app resource dictionary,
/// falling back to the matching <see cref="DesignColors"/> constant when the
/// application resources are unavailable (unit tests, early startup).
/// </summary>
public static class ColorResources
{
    public static Color Get(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Color color
            ? color
            : fallback;
}
