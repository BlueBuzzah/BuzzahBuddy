using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class BatteryReadingTests
{
    [Theory]
    [InlineData(3.0, 0)]
    [InlineData(4.2, 100)]
    [InlineData(3.72, 60)]
    [InlineData(2.9, 0)]
    [InlineData(4.5, 100)]
    public void ToPercentage_MapsVoltageLinearly(double voltage, int expected)
    {
        Assert.Equal(expected, BatteryReading.ToPercentage(voltage));
    }

    [Fact]
    public void Format_ValidVoltage_ShowsPercentAndVolts()
    {
        Assert.Equal("60% (3.72V)", BatteryReading.Format(3.72));
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

    [Fact]
    public void FromRaw_PlausibleVoltage_PassesThrough()
    {
        Assert.Equal(3.72, BatteryReading.FromRaw(3.72));
    }
}
