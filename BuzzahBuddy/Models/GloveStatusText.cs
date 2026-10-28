using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Models;

/// <summary>Which glove of the pair (firmware PRIMARY / SECONDARY).</summary>
public enum GloveRole { Primary, Secondary }

/// <summary>
/// A glove's link. NoReading: the primary is connected but reports nothing for the
/// secondary (older firmware or no reading), so the app can't honestly say either way.
/// </summary>
public enum GloveLinkStatus { Connected, Connecting, Disconnected, NoReading }

/// <summary>What one GloveStatus shows: role, link, battery, and optional technical lines.</summary>
public sealed record GloveStatusInfo(GloveRole Role, GloveLinkStatus Status, int? BatteryPercent,
    string? Firmware = null, string? Detail = null);

/// <summary>GloveStatus words and thresholds, after the design system's bundle.js GloveStatus.</summary>
public static class GloveStatusText
{
    public static string Name(GloveRole role) => role == GloveRole.Primary ? "Primary glove" : "Secondary glove";

    public static (StatusTone Tone, string Text) Badge(GloveLinkStatus status) => status switch
    {
        GloveLinkStatus.Connected => (StatusTone.Success, "Connected"),
        GloveLinkStatus.Connecting => (StatusTone.Brand, "Connecting"),
        GloveLinkStatus.NoReading => (StatusTone.Neutral, "No reading"),
        _ => (StatusTone.Danger, "Not connected"),
    };

    /// <summary>Under 20% the battery turns warning and says "Low".</summary>
    public static bool IsLow(int? percent) => percent is < 20;

    public static string BatteryGlyph(int percent) =>
        percent >= 70 ? LucideIcons.BatteryFull : percent >= 20 ? LucideIcons.BatteryMedium : LucideIcons.BatteryLow;

    /// <summary>One sentence for screen readers: "Primary glove, connected, battery 87 percent".</summary>
    public static string Describe(GloveStatusInfo info)
    {
        var text = $"{Name(info.Role)}, {Badge(info.Status).Text.ToLowerInvariant()}";
        if (info.Status == GloveLinkStatus.Connected && info.BatteryPercent is { } percent)
            text += $", battery {percent} percent{(IsLow(percent) ? ", low" : "")}";
        return text;
    }
}
