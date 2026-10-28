using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Theme;

/// <summary>
/// Chooses and applies one of the four design-system themes from the OS settings and
/// the in-app override (Settings: System / Light / Dark plus a High contrast switch).
/// </summary>
public interface IThemeService
{
    /// <summary>Persisted; setting it re-applies the theme.</summary>
    ThemePreference Preference { get; set; }

    /// <summary>On forces the high-contrast twin; off follows the OS. Persisted.</summary>
    bool HighContrast { get; set; }

    AppliedTheme Current { get; }

    /// <summary>Re-reads the OS settings and swaps the theme dictionary if it changed.</summary>
    void Apply();
}
