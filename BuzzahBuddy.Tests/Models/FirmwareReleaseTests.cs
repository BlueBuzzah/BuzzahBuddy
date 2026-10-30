using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class FirmwareReleaseTests
{
    [Theory]
    [InlineData("2.0.5", "v2.1.1", true)]
    [InlineData("2.1.0", "v2.1.1", true)]
    [InlineData("1.9.9", "v2.0.0", true)]
    [InlineData("2.1.1", "v2.1.1", false)]
    [InlineData("2.2.0", "v2.1.1", false)] // a dev build ahead of the release
    [InlineData("2.1.1-beta.2", "v2.1.1", false)] // pre-release suffixes are ignored
    [InlineData("2.0.5", "v2.1.1-beta.1", true)]
    [InlineData("2.1", "v2.1.0", false)] // a missing patch counts as 0
    [InlineData("2.0.5", null, false)]
    [InlineData(null, "v2.1.1", false)]
    [InlineData("garbage", "v2.1.1", false)]
    [InlineData("2.0.5", "latest", false)]
    public void IsOutdated(string? glove, string? latestTag, bool expected) =>
        Assert.Equal(expected, FirmwareRelease.IsOutdated(glove, latestTag));
}
