using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class CalibrationSettingsTests
{
    [Theory]
    [InlineData(0, 4, "Primary Index")]
    [InlineData(3, 4, "Primary Pinky")]
    [InlineData(4, 4, "Secondary Index")]
    [InlineData(7, 4, "Secondary Pinky")]
    [InlineData(4, 5, "Primary Thumb")]
    [InlineData(9, 5, "Secondary Thumb")]
    public void GetFingerLabel_MatchesFirmwareMap(int index, int perGlove, string expected)
    {
        Assert.Equal(expected, CalibrationSettings.GetFingerLabel(index, perGlove));
    }

    [Fact]
    public void CreateAll_FiveMotorBoard_YieldsTenFingers()
    {
        var all = CalibrationSettings.CreateAll(5);
        Assert.Equal(10, all.Count);
        Assert.True(all[9].IsSecondaryDevice);
    }
}
