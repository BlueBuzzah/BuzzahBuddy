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

    // The study excluded the thumb, but this profile is built to be APPLIED, and
    // firmware runs every motor present. Carrying 4 here would make applying the
    // research defaults silently drop a v3 glove to 4 active fingers.
    [Fact]
    public void For_CarriesTheDeviceMotorCountNotTheStudysFingerCount()
    {
        Assert.Equal(5, ResearchDefaults.For(5).Fingers);
        Assert.Equal(4, ResearchDefaults.Fingers);
    }

    [Fact]
    public void For_MatchesTheDeviceMotorCountExactly()
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
    public void DeviatingFields_IgnoresFingers()
    {
        // The editor doesn't expose Fingers, so a five-motor device running all
        // five must not raise a banner the user has no way to clear.
        var profile = ResearchDefaults.For(5);
        profile.Fingers = 5;

        Assert.Empty(ResearchDefaults.DeviatingFields(profile, 5));
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
        profile.Mirror = true;

        Assert.Equal(7, ResearchDefaults.DeviatingFields(profile, 4).Count);
    }
}
