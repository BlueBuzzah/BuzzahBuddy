using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class BatteryReadingTests
{
    // The firmware's discharge curve, interpolated and truncated like hardware.cpp.
    [Theory]
    [InlineData(3.27, 0)]
    [InlineData(3.30, 0)]  // critical: a stopped session is near empty
    [InlineData(4.2, 100)]
    [InlineData(3.72, 17)]
    [InlineData(3.79, 35)]
    [InlineData(3.80, 40)]
    [InlineData(3.81, 42)]
    [InlineData(2.9, 0)]
    [InlineData(4.5, 100)]
    public void ToPercentage_FollowsFirmwareCurve(double voltage, int expected)
    {
        Assert.Equal(expected, BatteryReading.ToPercentage(voltage));
    }

    [Fact]
    public void Format_ValidVoltage_ShowsPercent()
    {
        Assert.Equal("17%", BatteryReading.Format(3.72));
    }

    [Fact]
    public void Format_NoReading_ShowsPlaceholder()
    {
        Assert.Equal("—", BatteryReading.Format(null));
    }

    [Theory]
    [InlineData(null)]     // key missing / unparsable
    [InlineData(0.0)]      // firmware in-band "no reading" sentinel
    [InlineData(-1.0)]
    public void FromRaw_MissingOrSentinel_ReturnsNull(double? raw)
    {
        Assert.Null(BatteryReading.FromRaw(raw));
    }

    // Firmware calls a glove low below 3.80 V (orange LED, SESSION_START refused).
    [Theory]
    [InlineData(3.79, true)]
    [InlineData(3.7999, true)]
    [InlineData(3.80, false)]
    [InlineData(3.81, false)]
    [InlineData(null, false)]
    public void IsLow_BelowFirmwareLowLine(double? voltage, bool low) =>
        Assert.Equal(low, BatteryReading.IsLow(voltage));

    // The glove's own level wins: low latches until 3.90 V, so 3.85 V can still be low.
    // Firmware without BATLP/BATLS (null) falls back to the voltage.
    [Theory]
    [InlineData(3.85, true, true)]
    [InlineData(3.79, false, false)]
    [InlineData(3.79, null, true)]
    [InlineData(3.80, null, false)]
    [InlineData(null, true, false)]
    public void IsLow_PrefersGloveLevel(double? voltage, bool? gloveLow, bool low) =>
        Assert.Equal(low, BatteryReading.IsLow(voltage, gloveLow));

    [Theory]
    [InlineData("NORMAL", false)]
    [InlineData("LOW", true)]
    [InlineData("CRITICAL", true)]
    [InlineData(null, null)]
    [InlineData("SOMETHING", null)]
    public void GloveLow_MapsFirmwareLevel(string? level, bool? low) =>
        Assert.Equal(low, BatteryReading.GloveLow(level));

    [Fact]
    public void FromRaw_PlausibleVoltage_PassesThrough()
    {
        Assert.Equal(3.72, BatteryReading.FromRaw(3.72));
    }
}
