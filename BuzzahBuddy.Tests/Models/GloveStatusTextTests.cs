using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>GloveStatus reads as one sentence (accessibility.md Screen readers; bundle.js GloveStatus label).</summary>
public class GloveStatusTextTests
{
    [Fact]
    public void Connected_with_battery() =>
        Assert.Equal("Primary glove, connected, battery 87 percent",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Connected, 87)));

    [Fact]
    public void Low_battery_says_low() =>
        Assert.Equal("Secondary glove, connected, battery 12 percent, low",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.Connected, 12)));

    [Fact]
    public void No_reading() =>
        Assert.Equal("Secondary glove, no reading",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.NoReading, null)));

    // A stale battery value must not be read out for a glove that isn't connected.
    [Fact]
    public void Disconnected_hides_battery() =>
        Assert.Equal("Primary glove, not connected",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Disconnected, 50)));

    [Theory]
    [InlineData(20, false)]
    [InlineData(19, true)]
    [InlineData(null, false)]
    public void Low_is_under_20(int? percent, bool low) => Assert.Equal(low, GloveStatusText.IsLow(percent));

    [Fact]
    public void Badges()
    {
        Assert.Equal((StatusTone.Success, "Connected"), GloveStatusText.Badge(GloveLinkStatus.Connected));
        Assert.Equal((StatusTone.Brand, "Connecting"), GloveStatusText.Badge(GloveLinkStatus.Connecting));
        Assert.Equal((StatusTone.Danger, "Not Connected"), GloveStatusText.Badge(GloveLinkStatus.Disconnected));
        Assert.Equal((StatusTone.Neutral, "No Reading"), GloveStatusText.Badge(GloveLinkStatus.NoReading));
    }

    [Fact]
    public void Sentinel_voltage_formats_as_no_reading() =>
        Assert.Equal("—", BatteryReading.Format(BatteryReading.FromRaw(0.0)));

    [Fact]
    public void Volts_have_a_space_and_two_decimals() =>
        Assert.Equal("3.72 V", BatteryReading.FormatVolts(3.72));
}
