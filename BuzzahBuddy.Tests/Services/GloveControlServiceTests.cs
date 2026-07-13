using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class GloveControlServiceTests
{
    [Fact]
    public async Task GetDeviceInfoAsync_CachesMotorsAndProfile()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] =
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nMOTORS:5\nPROFILE:2:noisy_vcr\nBATP:3.7\nBATS:3.6\nSTATUS:IDLE\n\x04";
        var service = new GloveControlService(fake);

        var info = await service.GetDeviceInfoAsync();

        Assert.Equal(5, info.Motors);
        Assert.Equal(2, info.ProfileId);
        Assert.Equal(5, service.DeviceActuatorCount);
        Assert.Equal(2, service.DeviceProfileId);
    }

    [Fact]
    public async Task BuzzFingerAsync_FiveMotorBoard_AcceptsIndexNine()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] =
            "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nMOTORS:5\nPROFILE:1:regular_vcr\nBATP:3.7\nBATS:3.6\nSTATUS:IDLE\n\x04";
        fake.CannedResponses["CALIBRATE_BUZZ"] = "FINGER:9\nINTENSITY:80\nDURATION:500\n\x04";
        var service = new GloveControlService(fake);
        await service.GetDeviceInfoAsync();

        await service.BuzzFingerAsync(9, 80, 500);   // must not throw
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuzzFingerAsync(10, 80, 500));
    }

    [Fact]
    public async Task BuzzFingerAsync_DefaultBoard_RejectsIndexEight()
    {
        var fake = new FakeBluetoothService();
        var service = new GloveControlService(fake);
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuzzFingerAsync(8, 80, 500));
    }
}
