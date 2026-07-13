using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class MockParityTests
{
    private static async Task<MockBluetoothService> ConnectedMock()
    {
        var mock = new MockBluetoothService();
        await mock.ConnectToDeviceAsync(new GloveDevice { Id = "M1", Name = "BlueBuzzah" });
        return mock;
    }

    [Fact]
    public async Task Ping_UsesColonFormat_LikeFirmware()
    {
        var mock = await ConnectedMock();
        // Assert on the raw pre-parse wire text: CommandResponse.Parse normalizes both
        // "PONG\n" and "PONG:\n" to the same key/value pair, so asserting on the parsed
        // CommandResponse cannot detect a regression to the bare (non-firmware) form.
        var raw = await mock.GetRawResponseAsync("PING");
        Assert.StartsWith("PONG:", raw);
    }

    [Fact]
    public async Task Info_ReportsMotorsAndProfile()
    {
        var mock = await ConnectedMock();
        var response = await mock.SendCommandAsync("INFO");
        Assert.Equal(4, response.GetInt("MOTORS"));
        Assert.StartsWith("2:", response.GetString("PROFILE"));
    }

    [Fact]
    public async Task ProfileLoad_DuringSession_IsRejected()
    {
        var mock = await ConnectedMock();
        await mock.SendCommandAsync("SESSION_START");
        var response = await mock.SendCommandAsync("PROFILE_LOAD:3");
        Assert.Contains("Session must be stopped", response.GetString("ERROR") ?? "");
    }

    [Fact]
    public async Task SessionStatus_AtNinetyFivePercentElapsed_ReportsLowBattery()
    {
        var mock = await ConnectedMock();
        await mock.SendCommandAsync("SESSION_START");

        // Default mock session length is 2 hours (120 min); advance to 95% elapsed
        // without waiting ~114 real minutes.
        mock.AdvanceMockSession(TimeSpan.FromMinutes(114));

        var raw = await mock.GetRawResponseAsync("SESSION_STATUS");
        Assert.Contains("SESSION_STATUS:LOW_BATTERY", raw);
    }
}
