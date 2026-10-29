namespace BuzzahBuddy.Models;

/// <summary>The person's Settings choice: follow the phone, or force light or dark.</summary>
public enum ThemePreference { System, Light, Dark }

/// <summary>What the phone reports; Unknown when it doesn't say.</summary>
public enum OsAppearance { Unknown, Light, Dark }

/// <summary>The four design-system themes.</summary>
public enum AppliedTheme { Light, Dark, LightHc, DarkHc }

/// <summary>How the app sets the platform's own light/dark appearance (alerts, keyboard).</summary>
public enum NativeAppearance { FollowOs, Light, Dark }

public static class ThemeResolver
{
    /// <summary>
    /// Picks one of the four themes (design system README "Color"): the override or the
    /// OS appearance, dark when the OS doesn't say; the high-contrast twin when the
    /// in-app switch is on or the OS asks for more contrast.
    /// </summary>
    public static AppliedTheme Resolve(ThemePreference pref, bool highContrastSwitch, OsAppearance os, bool osHighContrast)
    {
        var dark = pref switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => os != OsAppearance.Light,
        };
        var highContrast = highContrastSwitch || osHighContrast;
        return (dark, highContrast) switch
        {
            (false, false) => AppliedTheme.Light,
            (false, true) => AppliedTheme.LightHc,
            (true, false) => AppliedTheme.Dark,
            (true, true) => AppliedTheme.DarkHc,
        };
    }

    /// <summary>
    /// The native appearance to request. Following the OS leaves it unset: pinning it
    /// would stop the OS's own light/dark changes from reaching the app.
    /// </summary>
    public static NativeAppearance Native(ThemePreference pref, OsAppearance os) => pref switch
    {
        ThemePreference.Light => NativeAppearance.Light,
        ThemePreference.Dark => NativeAppearance.Dark,
        _ => os == OsAppearance.Unknown ? NativeAppearance.Dark : NativeAppearance.FollowOs,
    };
}
