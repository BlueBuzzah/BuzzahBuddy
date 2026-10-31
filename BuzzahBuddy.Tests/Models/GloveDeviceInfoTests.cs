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
    public void FromCommandResponse_BatteryLevels_ParsesGloveVerdicts()
    {
        var response = CommandResponse.Parse(
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nBATP:4.02\nBATLP:NORMAL\nBATS:3.85\nBATLS:LOW\nSTATUS:IDLE\n\x04");

        var info = GloveDeviceInfo.FromCommandResponse(response);

        Assert.False(info.BatteryPrimaryLow);
        Assert.True(info.BatterySecondaryLow);
    }

    [Fact]
    public void FromCommandResponse_OlderFirmware_LevelsAreNull()
    {
        var response = CommandResponse.Parse(
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nBATP:3.72\nBATS:3.68\nSTATUS:IDLE\n\x04");

        var info = GloveDeviceInfo.FromCommandResponse(response);

        Assert.Null(info.BatteryPrimaryLow);
        Assert.Null(info.BatterySecondaryLow);
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
}
