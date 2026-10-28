using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>Tone → ground, ink and glyph, as bundle.js TONE_ICON and .bb-badge--* define them.</summary>
public class StatusToneTests
{
    [Fact]
    public void Success() => Assert.Equal(("SuccessSoft", "Success", LucideIcons.CircleCheck), StatusToneStyle.For(StatusTone.Success));

    [Fact]
    public void Warning() => Assert.Equal(("WarningSoft", "Warning", LucideIcons.TriangleAlert), StatusToneStyle.For(StatusTone.Warning));

    [Fact]
    public void Danger() => Assert.Equal(("DangerSoft", "Danger", LucideIcons.OctagonAlert), StatusToneStyle.For(StatusTone.Danger));

    [Fact]
    public void Brand() => Assert.Equal(("BrandSoft", "Brand", LucideIcons.Info), StatusToneStyle.For(StatusTone.Brand));

    [Fact]
    public void Info() => Assert.Equal(("BrandSoft", "Brand", LucideIcons.Info), StatusToneStyle.For(StatusTone.Info));

    // Neutral facts get a dot, not an icon.
    [Fact]
    public void Neutral() => Assert.Equal(("NeutralSoft", "Neutral", (string?)null), StatusToneStyle.For(StatusTone.Neutral));
}
