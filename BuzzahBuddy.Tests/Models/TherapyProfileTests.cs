using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class TherapyProfileTests
{
    [Fact]
    public void CustomProfileId_MatchesTheFirmwareProfileTable()
        => Assert.Equal(4, TherapyProfile.CustomProfileId);

    [Fact]
    public void IsCustom_IsTrueForExactlyOnePresetProfile()
    {
        var custom = TherapyProfile.GetPresetProfiles().Where(p => p.IsCustom).ToList();

        Assert.Single(custom);
        Assert.Equal("Custom", custom[0].Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void IsCustom_IsFalseForPresets(int profileId)
    {
        var profile = TherapyProfile.GetPresetProfiles().Single(p => p.ProfileId == profileId);

        Assert.False(profile.IsCustom);
    }

    [Fact]
    public void ToCustomProfile_LandsInTheCustomSlot()
    {
        var noisy = TherapyProfile.GetPresetProfiles().Single(p => p.ProfileId == 2);

        var copy = noisy.ToCustomProfile();

        Assert.Equal(TherapyProfile.CustomProfileId, copy.ProfileId);
        Assert.True(copy.IsCustom);
        Assert.Equal(noisy.Jitter, copy.Jitter);
        Assert.Equal(noisy.Fingers, copy.Fingers);
    }

    // Rows are a hand-kept copy of firmware ProfileManager::initBuiltInProfiles
    // (src/profile_manager.cpp); update them with it.
    // Firmware's rndp/sequential are the app's RNDP/SEQ. Fingers isn't pinned: firmware
    // runs every motor present (4 on v2, 5 on v3).
    [Theory]
    [InlineData(1, 100, 67, 0, 100, 100, 120, false, "RNDP")]  // regular_vcr
    [InlineData(2, 100, 67, 23.5, 100, 100, 120, true, "RNDP")] // noisy_vcr
    [InlineData(3, 100, 67, 23.5, 100, 100, 120, false, "RNDP")] // hybrid_vcr
    [InlineData(4, 100, 67, 0, 100, 100, 120, false, "RNDP")]  // custom_vcr
    [InlineData(5, 80, 87, 15, 30, 70, 60, true, "SEQ")]        // gentle
    [InlineData(6, 100, 67, 23.5, 50, 100, 5, true, "RNDP")]   // quick_test
    public void Preset_MatchesFirmware(
        int profileId, double onMs, double offMs, double jitter,
        int ampMin, int ampMax, int sessionMin, bool mirror, string pattern)
    {
        var p = TherapyProfile.GetPresetProfiles().Single(x => x.ProfileId == profileId);

        Assert.Equal(onMs, Math.Round(p.TimeOn * 1000, 1));
        Assert.Equal(offMs, Math.Round(p.TimeOff * 1000, 1));
        Assert.Equal(jitter, p.Jitter);
        Assert.Equal(ampMin, p.AmplitudeMin);
        Assert.Equal(ampMax, p.AmplitudeMax);
        Assert.Equal(sessionMin, p.TimeSession);
        Assert.Equal(mirror, p.Mirror);
        Assert.Equal(pattern, p.PatternType);
    }

    // The Gloves page lists profiles in this order, with Custom at the bottom.
    [Fact]
    public void Custom_is_listed_last() =>
        Assert.True(TherapyProfile.GetPresetProfiles().Last().IsCustom);
}
