using BuzzahBuddy.Models;
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

    [Fact]
    public async Task LoadProfileAsync_RebootingResponse_SetsExpectingReboot()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_LOAD"] = "STATUS:REBOOTING\nPROFILE:hybrid_vcr\n\x04";
        var service = new GloveControlService(fake);

        await service.LoadProfileAsync(3);

        Assert.True(service.ExpectingReboot);
        Assert.Contains("PROFILE_LOAD:3", fake.SentCommands);
    }

    [Fact]
    public async Task SetCustomProfileAsync_WithinFirmwareLimits_Sends()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        await service.SetCustomProfileAsync(new Dictionary<string, string> { ["FREQ"] = "250", ["ON"] = "100" });

        Assert.Contains("PROFILE_CUSTOM:FREQ:250:ON:100", fake.SentCommands);
    }

    [Fact]
    public async Task SetCustomProfileAsync_OverFirmwareLimits_ThrowsInsteadOfSilentTruncation()
    {
        var service = new GloveControlService(new FakeBluetoothService());

        // >8 pairs: firmware parseCommand drops tokens past MAX_COMMAND_PARAMS=16
        // but still replies CUSTOM_LOADED
        var ninePairs = Enumerable.Range(1, 9).ToDictionary(i => $"KEY{i}", i => "1");
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCustomProfileAsync(ninePairs));

        // ':' in a value would shift the firmware's token parsing
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetCustomProfileAsync(new Dictionary<string, string> { ["FREQ"] = "2:50" }));

        // token longer than the firmware's 64-char PARAM_BUFFER_SIZE
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetCustomProfileAsync(new Dictionary<string, string> { ["FREQ"] = new string('9', 64) }));
    }

    private static TherapyProfile MakeProfile() => new()
    {
        ActuatorType = "LRA",
        ActuatorFrequency = 250,
        TimeOn = 0.100,
        TimeOff = 0.067,
        TimeSession = 120,
        AmplitudeMin = 100,
        AmplitudeMax = 100,
        Jitter = 0,
        Mirror = false,
        PatternType = "RNDP",
    };

    [Fact]
    public async Task ApplyCustomProfileAsync_WithBaseline_SendsOnlyChangedParameters()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.ActuatorFrequency = 240;
        desired.Jitter = 15.5;

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        Assert.Equal("PROFILE_CUSTOM:FREQ:240:JITTER:15.5", Assert.Single(fake.SentCommands));
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_NoBaseline_ChunksAllTenParametersAcrossTwoCommands()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        await service.ApplyCustomProfileAsync(MakeProfile(), baseline: null);

        // 10 parameters, firmware max 8 KEY:VAL pairs per command
        Assert.Equal(2, fake.SentCommands.Count);
        var combined = string.Join(":", fake.SentCommands);
        foreach (var key in new[] { "TYPE", "FREQ", "ON", "OFF", "SESSION", "AMPMIN", "AMPMAX", "PATTERN", "MIRROR", "JITTER" })
        {
            Assert.Contains($":{key}:", combined);
        }
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_MapsPatternAndUnitsToFirmwareVocabulary()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.PatternType = "SEQ";     // app/spec name; firmware only accepts "sequential"
        desired.TimeOn = 0.250;          // model is seconds; protocol is ms
        desired.Mirror = true;

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        var command = Assert.Single(fake.SentCommands);
        Assert.Contains("PATTERN:sequential", command);
        Assert.Contains("ON:250", command);
        Assert.Contains("MIRROR:1", command);
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_OutOfFirmwareRange_ThrowsWithoutSending()
    {
        var fake = new FakeBluetoothService();
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.ActuatorFrequency = 400; // firmware setParameter rejects >300

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyCustomProfileAsync(desired));
        Assert.Empty(fake.SentCommands);
    }

    [Fact]
    public async Task ListProfilesAsync_ReturnsAllSixDeviceProfiles()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_LIST"] =
            "PROFILE:1:regular_vcr\nPROFILE:2:noisy_vcr\nPROFILE:3:hybrid_vcr\n" +
            "PROFILE:4:custom_vcr\nPROFILE:5:gentle\nPROFILE:6:quick_test\n\x04";
        var service = new GloveControlService(fake);

        var profiles = await service.ListProfilesAsync();

        Assert.Equal(6, profiles.Count);
    }
}
