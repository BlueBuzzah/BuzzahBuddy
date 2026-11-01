using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class GloveDeviceInfoTests
{
    [Fact]
    public void FromCommandResponse_BatteryPresent_ParsesVoltages()
    {
        var response = CommandResponse.Parse(
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nBATP:3.72\nBATS:3.68\nSTATUS:IDLE\n\x04");

        var info = GloveDeviceInfo.FromCommandResponse(response);

        Assert.Equal(3.72, info.BatteryPrimaryVoltage);
        Assert.Equal(3.68, info.BatterySecondaryVoltage);
    }

    [Fact]
    public void FromCommandResponse_BatteryMissing_VoltagesAreNull()
    {
        var response = CommandResponse.Parse(
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nSTATUS:IDLE\n\x04");

        var info = GloveDeviceInfo.FromCommandResponse(response);

        Assert.Null(info.BatteryPrimaryVoltage);
        Assert.Null(info.BatterySecondaryVoltage);
    }

    [Fact]
    public void FromCommandResponse_ZeroSentinel_VoltagesAreNull()
    {
        // Firmware sends BATP:0.00/BATS:0.00 for "no monitor", "no reading",
        // and "secondary timed out" — never a real pack voltage.
        var response = CommandResponse.Parse(
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nBATP:0.00\nBATS:0.00\nSTATUS:IDLE\n\x04");

        var info = GloveDeviceInfo.FromCommandResponse(response);

        Assert.Null(info.BatteryPrimaryVoltage);
        Assert.Null(info.BatterySecondaryVoltage);
    }

    [Theory]
    [InlineData("BATP:4.02\nSECONDARY:1\nBATS:0.00\n", true)]   // linked, battery reply timed out
    [InlineData("BATP:4.02\nSECONDARY:0\nBATS:0.00\n", false)]
    [InlineData("BATP:4.02\nBATS:3.85\n", null)]                 // firmware before the key
    public void FromCommandResponse_ParsesSecondaryLink(string battery, bool? linked)
    {
        var response = CommandResponse.Parse($"ROLE:PRIMARY\nFW:2.1.0\n{battery}STATUS:IDLE\n\x04");

        Assert.Equal(linked, GloveDeviceInfo.FromCommandResponse(response).SecondaryLinked);
    }
}
