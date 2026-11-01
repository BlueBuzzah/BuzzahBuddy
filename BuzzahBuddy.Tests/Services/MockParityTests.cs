using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class MockParityTests
{
    private static async Task<MockBluetoothService> ConnectedMock()
    {
        var mock = new MockBluetoothService { TransitionDelay = TimeSpan.Zero };
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
        Assert.Equal("PONG:", raw.Split('\n')[1]);
    }

    [Theory]
    [InlineData("PING", "PING")]
    [InlineData("SESSION_PAUSE", "SESSION_PAUSE")]
    [InlineData("PROFILE_LOAD:3", "PROFILE_LOAD")]
    [InlineData("THERAPY_LED_OFF:true", "THERAPY_LED_OFF")]
    [InlineData("BOGUS", "BOGUS")]
    public async Task EveryReply_StartsWithCmdEcho_LikeFirmware(string command, string echoed)
    {
        var mock = await ConnectedMock();
        var raw = await mock.GetRawResponseAsync(command);
        Assert.StartsWith($"CMD:{echoed}\n", raw);
    }

    [Fact]
    public async Task SessionStart_ReportsElapsedAndTotal()
    {
        var mock = await ConnectedMock();
        var raw = await mock.GetRawResponseAsync("SESSION_START");
        Assert.Contains("\nELAPSED:0\n", raw);
        Assert.Contains("\nTOTAL:7200\n", raw);
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

        // Like firmware, LOW_BATTERY means a glove really reads under the low line.
        var battery = await mock.GetRawResponseAsync("BATTERY");
        Assert.Contains("\nBATS:3.78\n", battery);
    }

    [Fact]
    public async Task Battery_DemoGloves_StartAboveLowLine()
    {
        var mock = await ConnectedMock();
        var raw = await mock.GetRawResponseAsync("INFO");
        Assert.Contains("\nBATP:3.95\nSECONDARY:1\nBATS:3.91\n", raw);
    }

    // Firmware refuses SESSION_START without the secondary, before the battery check.
    [Fact]
    public async Task SessionStart_SecondaryOff_IsRefused_LikeFirmware()
    {
        var mock = await ConnectedMock();
        mock.MockSecondaryLinked = false;
        mock.MockPrimaryVolts = 3.70;

        Assert.Contains("\nSECONDARY:0\nBATS:0.00\n", await mock.GetRawResponseAsync("INFO"));
        var raw = await mock.GetRawResponseAsync("SESSION_START");
        Assert.Contains("\nERROR:Secondary not connected\n", raw);
        Assert.DoesNotContain("SESSION_STATUS", raw);
    }

    // Firmware refuses SESSION_START while either glove is under 3.80 V.
    [Theory]
    [InlineData(3.95, 3.79, true)]
    [InlineData(3.79, 3.95, true)]
    [InlineData(3.80, 3.80, false)]
    public async Task SessionStart_GloveBelowLowLine_IsRefused_LikeFirmware(double primary, double secondary, bool refused)
    {
        var mock = await ConnectedMock();
        mock.MockPrimaryVolts = primary;
        mock.MockSecondaryVolts = secondary;
        var raw = await mock.GetRawResponseAsync("SESSION_START");
        Assert.Equal(refused, raw.Contains("\nERROR:Battery too low\n"));
        Assert.Equal(!refused, raw.Contains("\nSESSION_STATUS:RUNNING\n"));
    }

    [Fact]
    public async Task SessionStatus_AfterSessionLength_ReportsIdleZeros_LikeFirmware()
    {
        // Firmware stops the engine the tick elapsed reaches TOTAL, then reports IDLE 0/0/0.
        // PROGRESS never reads 100 on real gloves.
        var mock = await ConnectedMock();
        await mock.GetRawResponseAsync("SESSION_START");
        mock.AdvanceMockSession(TimeSpan.FromSeconds(7200));
        var raw = await mock.GetRawResponseAsync("SESSION_STATUS");
        Assert.Contains("SESSION_STATUS:IDLE\n", raw);
        Assert.Contains("\nELAPSED:0\n", raw);
        Assert.Contains("\nTOTAL:0\n", raw);
        Assert.Contains("\nPROGRESS:0\n", raw);
    }

    [Fact]
    public async Task SessionStartAndStatus_ReportSessionId_LikeFirmware()
    {
        var mock = await ConnectedMock();
        var start = await mock.GetRawResponseAsync("SESSION_START");
        var id = start.Split('\n').Single(l => l.StartsWith("SESSION_ID:"));
        var status = await mock.GetRawResponseAsync("SESSION_STATUS");
        Assert.Contains($"\n{id}\nPAUSED:0\n", status);
        Assert.DoesNotContain("LAST_", status);
    }

    [Fact]
    public async Task SessionStatus_AfterStop_ReportsLastSession_LikeFirmware()
    {
        var mock = await ConnectedMock();
        var start = await mock.GetRawResponseAsync("SESSION_START");
        var id = start.Split('\n').Single(l => l.StartsWith("SESSION_ID:"))["SESSION_ID:".Length..];
        mock.AdvanceMockSession(TimeSpan.FromSeconds(121));
        await mock.GetRawResponseAsync("SESSION_STOP");

        var raw = await mock.GetRawResponseAsync("SESSION_STATUS");
        Assert.Contains("\nPROGRESS:0\n" +
                        $"LAST_ID:{id}\nLAST_ELAPSED:121\nLAST_PAUSED:0\nLAST_TOTAL:7200\nLAST_REASON:STOPPED\n", raw);
        Assert.DoesNotContain("SESSION_ID:", raw);
    }

    [Fact]
    public async Task SessionStatus_AfterSessionLength_ReportsCompletedLastSession()
    {
        var mock = await ConnectedMock();
        await mock.GetRawResponseAsync("SESSION_START");
        mock.AdvanceMockSession(TimeSpan.FromSeconds(7200));
        var raw = await mock.GetRawResponseAsync("SESSION_STATUS");
        Assert.Contains("\nLAST_ELAPSED:7200\n", raw);
        Assert.Contains("\nLAST_REASON:COMPLETED\n", raw);
    }
}
