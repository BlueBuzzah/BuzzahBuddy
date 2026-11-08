using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class ResearchDefaultsTests
{
    [Fact]
    public void For_ProducesTheStudiedCoordinatedResetTiming()
    {
        var profile = ResearchDefaults.For(4);

        // Pfeifer et al. 2021: 100 ms bursts, four fingertips, f_CR ≈ 1.5 Hz.
        Assert.Equal(1.5, TherapyTiming.CrFrequencyHz(
            profile.Fingers, profile.TimeOn * 1000, profile.TimeOff * 1000), precision: 2);
    }

    // Pinned to firmware's regular_vcr / custom_vcr: the study's regular vCR arm,
    // with the firmware's full amplitude.
    [Fact]
    public void For_MatchesTheFirmwareRegularVcrProfile()
    {
        var profile = ResearchDefaults.For(4);

        Assert.Equal(0.100, profile.TimeOn, precision: 6);
        Assert.Equal(0.067, profile.TimeOff, precision: 6);
        Assert.Equal(0, profile.Jitter);
        Assert.Equal(100, profile.AmplitudeMin);
        Assert.Equal(100, profile.AmplitudeMax);
        Assert.False(profile.Mirror);
        Assert.Equal(120, profile.TimeSession);
        Assert.Equal(250, profile.ActuatorFrequency);
        Assert.Equal("RNDP", profile.PatternType);
    }

    // The Gloves page lists this preset before the user has saved a Custom profile,
    // so it must show the same settings the editor starts from.
    [Fact]
    public void CustomPreset_MatchesTheDefaults()
    {
        var preset = TherapyProfile.GetPresetProfiles().Single(p => p.IsCustom);
        var defaults = ResearchDefaults.For(4);

        Assert.Equal(defaults.TimeOn, preset.TimeOn);
        Assert.Equal(defaults.TimeOff, preset.TimeOff);
        Assert.Equal(defaults.Jitter, preset.Jitter);
        Assert.Equal(defaults.AmplitudeMin, preset.AmplitudeMin);
        Assert.Equal(defaults.AmplitudeMax, preset.AmplitudeMax);
        Assert.Equal(defaults.TimeSession, preset.TimeSession);
        Assert.Equal(defaults.Mirror, preset.Mirror);
        Assert.Equal(defaults.PatternType, preset.PatternType);
        Assert.Equal(defaults.ActuatorFrequency, preset.ActuatorFrequency);
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
        profile.Jitter = 23.5;

        Assert.Equal(new[] { nameof(TherapyProfile.Jitter) },
            ResearchDefaults.DeviatingFields(profile, 4));
    }

    // The study's noisy vCR arm: the regular arm's timing and session with 23.5%
    // jitter, mirrored. It was studied too, so it raises no banner.
    [Fact]
    public void DeviatingFields_ForTheNoisyArm_IsEmpty()
    {
        var profile = ResearchDefaults.For(4);
        profile.Jitter = ResearchDefaults.NoisyJitter;
        profile.Mirror = ResearchDefaults.NoisyMirror;

        Assert.Equal(23.5, ResearchDefaults.NoisyJitter);
        Assert.True(ResearchDefaults.NoisyMirror);
        Assert.Empty(ResearchDefaults.DeviatingFields(profile, 4));
    }

    [Fact]
    public void DeviatingFields_NoisyJitterWithoutMirror_NamesJitter()
    {
        // Hybrid's mix of the two arms; neither arm ran it.
        var profile = ResearchDefaults.For(4);
        profile.Jitter = 23.5;
        profile.Mirror = false;

        Assert.Equal(new[] { nameof(TherapyProfile.Jitter) },
            ResearchDefaults.DeviatingFields(profile, 4));
    }

    [Fact]
    public void DeviatingFields_NoisyArmWithAnotherSession_NamesTheRegularArmDeviations()
    {
        var profile = ResearchDefaults.For(4);
        profile.Jitter = ResearchDefaults.NoisyJitter;
        profile.Mirror = ResearchDefaults.NoisyMirror;
        profile.TimeSession = 30;

        Assert.Equal(
            new HashSet<string> { nameof(TherapyProfile.Jitter), nameof(TherapyProfile.Mirror), nameof(TherapyProfile.TimeSession) },
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
    public void DeviatingFields_IgnoresAmplitude()
    {
        // Amplitude % has no study equivalent (the study reported displacement in
        // mm), so changing it must not raise the banner.
        var profile = ResearchDefaults.For(4);
        profile.AmplitudeMin = 20;
        profile.AmplitudeMax = 90;

        Assert.Empty(ResearchDefaults.DeviatingFields(profile, 4));
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
        profile.Jitter = 23.5;
        profile.AmplitudeMin = 20;   // changed but ignored: no study equivalent
        profile.AmplitudeMax = 90;
        profile.TimeSession = 30;
        profile.Mirror = true;

        Assert.Equal(
            new[]
            {
                nameof(TherapyProfile.Jitter), nameof(TherapyProfile.Mirror),
                nameof(TherapyProfile.TimeOff), nameof(TherapyProfile.TimeOn),
                nameof(TherapyProfile.TimeSession),
            },
            ResearchDefaults.DeviatingFields(profile, 4).Order(StringComparer.Ordinal));
    }
}
