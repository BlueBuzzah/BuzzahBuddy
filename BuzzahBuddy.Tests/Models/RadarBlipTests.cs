using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>A blip lights as the sweep passes it, then fades before the sweep comes round again.</summary>
public class RadarBlipTests
{
    [Fact]
    public void Lights_fully_as_the_sweep_passes() =>
        Assert.Equal(1, RadarBlip.Glow(sweepDegrees: 90, blipDegrees: 90));

    [Fact]
    public void Fades_out_with_an_ease_after_the_sweep() =>
        Assert.Equal(0.25, RadarBlip.Glow(sweepDegrees: 90 + RadarBlip.FadeDegrees / 2, blipDegrees: 90), 6);

    [Fact]
    public void Is_dark_once_the_fade_is_over() =>
        Assert.Equal(0, RadarBlip.Glow(sweepDegrees: 90 + RadarBlip.FadeDegrees + 1, blipDegrees: 90));

    // Just short of the blip is almost a full turn after it, not "before" it.
    [Fact]
    public void Is_dark_just_before_the_sweep_arrives() =>
        Assert.Equal(0, RadarBlip.Glow(sweepDegrees: 85, blipDegrees: 90));

    [Fact]
    public void Counts_past_a_full_turn() =>
        Assert.Equal(RadarBlip.Glow(sweepDegrees: 10, blipDegrees: -10), RadarBlip.Glow(sweepDegrees: 370, blipDegrees: 350), 6);
}
