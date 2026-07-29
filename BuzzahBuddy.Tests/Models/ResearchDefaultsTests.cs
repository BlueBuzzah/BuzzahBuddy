using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class ResearchDefaultsTests
{
    [Fact]
    public void For_ProducesTheValidatedCoordinatedResetTiming()
    {
        var profile = ResearchDefaults.For(4);

        // Pfeifer et al. 2021: 100 ms bursts, four fingertips, f_CR ≈ 1.5 Hz.
        Assert.Equal(1.5, TherapyTiming.CrFrequencyHz(
            profile.Fingers, profile.TimeOn * 1000, profile.TimeOff * 1000), precision: 2);
    }

    [Fact]
    public void For_LandsInTheCustomSlot()
        => Assert.True(ResearchDefaults.For(4).IsCustom);

    [Fact]
    public void For_ExcludesTheThumbEvenOnFiveMotorHardware()
        => Assert.Equal(4, ResearchDefaults.For(5).Fingers);

    [Fact]
    public void For_NeverExceedsTheDeviceMotorCount()
        => Assert.Equal(3, ResearchDefaults.For(3).Fingers);

    [Fact]
    public void For_IsEntirelyWithinTheFirmwareBounds()
    {
        var profile = ResearchDefaults.For(5);

        Assert.True(TherapyParameterBounds.IsTimeOnValid(profile.TimeOn * 1000));
        Assert.True(TherapyParameterBounds.IsTimeOffValid(profile.TimeOff * 1000));
        Assert.True(TherapyParameterBounds.IsJitterValid(profile.Jitter));
        Assert.True(TherapyParameterBounds.IsAmplitudeValid(profile.AmplitudeMin));
        Assert.True(TherapyParameterBounds.IsAmplitudeValid(profile.AmplitudeMax));
        Assert.True(TherapyParameterBounds.IsSessionValid(profile.TimeSession));
        Assert.True(TherapyParameterBounds.IsFingersValid(profile.Fingers, 5));
    }

    [Fact]
    public void DeviatingFields_ForTheDefaultsThemselves_IsEmpty()
        => Assert.Empty(ResearchDefaults.DeviatingFields(ResearchDefaults.For(4), 4));

    [Fact]
    public void DeviatingFields_NamesOnlyTheChangedField()
    {
        var profile = ResearchDefaults.For(4);
        profile.Jitter = 0;

        Assert.Equal(new[] { nameof(TherapyProfile.Jitter) },
            ResearchDefaults.DeviatingFields(profile, 4));
    }

    [Fact]
    public void DeviatingFields_FlagsTheThumbOnFiveMotorHardware()
    {
        var profile = ResearchDefaults.For(5);
        profile.Fingers = 5;

        Assert.Contains(nameof(TherapyProfile.Fingers),
            ResearchDefaults.DeviatingFields(profile, 5));
    }

    [Fact]
    public void DeviatingFields_IgnoresSubMillisecondFloatNoise()
    {
        var profile = ResearchDefaults.For(4);
        profile.TimeOn = 100.00000000000001 / 1000.0;

        Assert.Empty(ResearchDefaults.DeviatingFields(profile, 4));
    }

    [Fact]
    public void DeviatingFields_FlagsEveryEditableField()
    {
        var profile = ResearchDefaults.For(4);
        profile.TimeOn = 0.150;
        profile.TimeOff = 0.100;
        profile.Jitter = 0;
        profile.AmplitudeMin = 20;
        profile.AmplitudeMax = 90;
        profile.TimeSession = 30;
        profile.Fingers = 2;
        profile.Mirror = true;

        Assert.Equal(8, ResearchDefaults.DeviatingFields(profile, 4).Count);
    }
}
