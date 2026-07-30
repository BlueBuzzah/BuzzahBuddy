using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>
/// These assertions encode the firmware's PARAM_* constants (include/config.h).
/// If firmware bounds change, these tests must change with them.
/// </summary>
public class TherapyParameterBoundsTests
{
    [Theory]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(200, true)]
    [InlineData(49, false)]
    [InlineData(201, false)]
    public void TimeOn_RangeMatchesFirmware(double value, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsTimeOnValid(value));

    [Theory]
    [InlineData(30, true)]
    [InlineData(67, true)]
    [InlineData(200, true)]
    [InlineData(29, false)]
    [InlineData(201, false)]
    public void TimeOff_RangeMatchesFirmware(double value, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsTimeOffValid(value));

    [Theory]
    [InlineData(0, true)]
    [InlineData(23.5, true)]
    [InlineData(50, true)]
    [InlineData(-0.1, false)]
    [InlineData(51, false)]
    public void Jitter_RangeMatchesFirmware(double value, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsJitterValid(value));

    [Theory]
    [InlineData(20, true)]
    [InlineData(70, true)]
    [InlineData(100, true)]
    [InlineData(19, false)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void Amplitude_RangeMatchesFirmware(int value, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsAmplitudeValid(value));

    [Theory]
    [InlineData(1, true)]
    [InlineData(240, true)]
    [InlineData(0, false)]
    [InlineData(241, false)]
    public void Session_RangeMatchesFirmware(int value, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsSessionValid(value));

    [Theory]
    [InlineData(1, 4, true)]
    [InlineData(4, 4, true)]
    [InlineData(5, 4, false)]   // v2 has no thumb motor
    [InlineData(5, 5, true)]    // v3 does
    [InlineData(0, 5, false)]
    public void Fingers_IsBoundedByTheDeviceMotorCount(int value, int motorCount, bool expected)
        => Assert.Equal(expected, TherapyParameterBounds.IsFingersValid(value, motorCount));

    [Fact]
    public void EffectiveJitterCap_AtResearchDefaults_IsTheStaticCap()
    {
        // 100/67 ms leaves far more headroom than 50% jitter consumes.
        Assert.Equal(TherapyParameterBounds.JitterMax,
            TherapyParameterBounds.EffectiveJitterCap(100, 67));
    }

    [Fact]
    public void EffectiveJitterCap_AtShortGap_FallsBelowTheStaticCap()
    {
        // 200/30 ms: excursion is capped at (30 - 5) ms of a 230 ms cycle.
        var cap = TherapyParameterBounds.EffectiveJitterCap(200, 30);

        Assert.True(cap < TherapyParameterBounds.JitterMax);
        Assert.Equal(200.0 * 25 / 230, cap, precision: 6);
    }

    [Fact]
    public void EffectiveJitterCap_WhenGapIsAtTheFloor_IsZero()
        => Assert.Equal(0, TherapyParameterBounds.EffectiveJitterCap(100, 5));

    [Fact]
    public void EffectiveJitterCap_WithDegenerateTiming_IsZero()
        => Assert.Equal(0, TherapyParameterBounds.EffectiveJitterCap(0, 0));
}
