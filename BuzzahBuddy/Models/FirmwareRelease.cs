namespace BuzzahBuddy.Models;

/// <summary>Compares the primary glove's firmware with the newest release tag.</summary>
public static class FirmwareRelease
{
    /// <summary>
    /// True only when both versions parse and the glove's is older ("2.0.5" vs "v2.1.1").
    /// Pre-release suffixes are ignored, so a beta of the latest release counts as current.
    /// </summary>
    public static bool IsOutdated(string? glove, string? latestTag) =>
        TryParse(glove, out var current) && TryParse(latestTag, out var latest) && current < latest;

    private static bool TryParse(string? text, out Version version)
    {
        version = new Version();
        var core = text?.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed))
            return false;
        // "2.1" and "2.1.0" are the same release; Version alone ranks the first lower.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
