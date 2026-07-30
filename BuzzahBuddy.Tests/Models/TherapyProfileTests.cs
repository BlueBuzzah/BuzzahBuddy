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
}
