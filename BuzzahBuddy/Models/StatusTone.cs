using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Models;

/// <summary>Status tones shared by StatusBadge and Banner (design system README "Color").</summary>
public enum StatusTone { Neutral, Brand, Info, Success, Warning, Danger }

public static class StatusToneStyle
{
    /// <summary>
    /// The soft ground, the ink (text, icon, high-contrast edge) and the glyph for a tone,
    /// per bundle.js TONE_ICON. A null glyph means a dot: neutral facts carry no judgment.
    /// </summary>
    public static (string GroundKey, string InkKey, string? Glyph) For(StatusTone tone) => tone switch
    {
        StatusTone.Success => ("SuccessSoft", "Success", LucideIcons.CircleCheck),
        StatusTone.Warning => ("WarningSoft", "Warning", LucideIcons.TriangleAlert),
        StatusTone.Danger => ("DangerSoft", "Danger", LucideIcons.OctagonAlert),
        StatusTone.Brand or StatusTone.Info => ("BrandSoft", "Brand", LucideIcons.Info),
        _ => ("NeutralSoft", "Neutral", null),
    };
}
