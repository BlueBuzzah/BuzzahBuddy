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
        Assert.StartsWith("1:", response.GetString("PROFILE"));
    }

    // Firmware handleProfileGet: ON/OFF and JITTER with one decimal, lowercase pattern,
    // FINGERS last. Values are the loaded profile's.
    [Fact]
    public async Task ProfileGet_FreshMock_ReportsRegular_LikeInfo()
    {
        var mock = await ConnectedMock();
        Assert.Contains("\nPROFILE:1:regular_vcr\n", await mock.GetRawResponseAsync("INFO"));
        Assert.Equal("CMD:PROFILE_GET\nTYPE:LRA\nFREQ:250\nON:100.0\nOFF:67.0\nSESSION:120\n" +
                     "AMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:0\nJITTER:0.0\nFINGERS:4\n\x04",
            await mock.GetRawResponseAsync("PROFILE_GET"));
    }

    [Theory]
    [InlineData(2, "ON:100.0\nOFF:67.0\nSESSION:120\nAMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:1\nJITTER:23.5\n")]
    [InlineData(5, "ON:80.0\nOFF:87.0\nSESSION:60\nAMPMIN:30\nAMPMAX:70\nPATTERN:sequential\nMIRROR:1\nJITTER:15.0\n")]
    public async Task ProfileGet_AfterProfileLoad_ReportsTheLoadedProfile(int profileId, string values)
    {
        var mock = await ConnectedMock();
        await mock.GetRawResponseAsync($"PROFILE_LOAD:{profileId}");
        Assert.Equal($"CMD:PROFILE_GET\nTYPE:LRA\nFREQ:250\n{values}FINGERS:4\n\x04",
            await mock.GetRawResponseAsync("PROFILE_GET"));
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
        Assert.Contains("\nBATS:3.78\nBATLS:LOW\n", battery);
    }

    [Fact]
    public async Task Battery_DemoGloves_StartAboveLowLine()
    {
        var mock = await ConnectedMock();
        var raw = await mock.GetRawResponseAsync("INFO");
        Assert.Contains("\nBATP:3.95\nBATLP:NORMAL\nSECONDARY:1\nBATS:3.91\nBATLS:NORMAL\n", raw);
    }

    // Firmware refuses SESSION_START without the secondary, before the battery check.
    [Fact]
    public async Task SessionStart_SecondaryOff_IsRefused_LikeFirmware()
    {
        var mock = await ConnectedMock();
        mock.MockSecondaryLinked = false;
        mock.MockPrimaryVolts = 3.70;

        Assert.Contains("\nSECONDARY:0\nBATS:0.00\nSTATUS:", await mock.GetRawResponseAsync("INFO"));
        var raw = await mock.GetRawResponseAsync("SESSION_START");
        Assert.Contains("\nERROR:Secondary not connected\n", raw);
        Assert.DoesNotContain("SESSION_STATUS", raw);
    }

    // Firmware refuses SESSION_START when a glove is in ERROR, right after "Session already
    // active" and before the secondary link and battery checks. An unlinked faulty secondary
    // can't report, so it reads as not connected.
    [Theory]
    [InlineData(true, false, true, "Primary glove error")]
    [InlineData(true, true, false, "Primary glove error")]
    [InlineData(false, true, true, "Secondary glove error")]
    [InlineData(false, true, false, "Secondary not connected")]
    public async Task SessionStart_GloveFault_IsRefused_LikeFirmware(bool primaryFault, bool secondaryFault, bool linked, string error)
    {
        var mock = await ConnectedMock();
        mock.MockPrimaryFault = primaryFault;
        mock.MockSecondaryFault = secondaryFault;
        mock.MockSecondaryLinked = linked;
        mock.MockPrimaryVolts = 3.70;
        var raw = await mock.GetRawResponseAsync("SESSION_START");
        Assert.Contains($"\nERROR:{error}\n", raw);
        Assert.DoesNotContain("SESSION_STATUS", raw);
    }

    [Fact]
    public async Task SessionStart_WhileRunning_SaysAlreadyActive_BeforeGloveFault()
    {
        var mock = await ConnectedMock();
        await mock.GetRawResponseAsync("SESSION_START");
        mock.MockPrimaryFault = true;
        Assert.Contains("\nERROR:Session already active\n", await mock.GetRawResponseAsync("SESSION_START"));
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

    // Firmware handleInfo: MOTP (and MOTS when linked) right after MOTORS; v2 sends neither.
    [Fact]
    public async Task Info_NoMotorChecks_ByDefault_LikeV2()
    {
        var mock = await ConnectedMock();
        var raw = await mock.GetRawResponseAsync("INFO");
        Assert.DoesNotContain("MOTP:", raw);
        Assert.DoesNotContain("MOTS:", raw);
    }

    [Fact]
    public async Task Info_MotorChecks_FollowMotors_LikeFirmware()
    {
        var mock = await ConnectedMock();
        mock.MockPrimaryMotorChecks = "OK,OK,OPEN,OPEN,OPEN";
        mock.MockSecondaryMotorChecks = "OK,OK,OK,OK,OPEN";

        var raw = await mock.GetRawResponseAsync("INFO");

        Assert.Contains("\nMOTORS:4\nMOTP:OK,OK,OPEN,OPEN,OPEN\nMOTS:OK,OK,OK,OK,OPEN\nPROFILE:", raw);
    }

    [Fact]
    public async Task Info_SecondaryOff_SendsNoMots_LikeFirmware()
    {
        var mock = await ConnectedMock();
        mock.MockPrimaryMotorChecks = "OK,OK,OK,OK,OPEN";
        mock.MockSecondaryMotorChecks = "OK,OK,OK,OK,OPEN";
        mock.MockSecondaryLinked = false;

        var raw = await mock.GetRawResponseAsync("INFO");

        Assert.Contains("\nMOTORS:4\nMOTP:OK,OK,OK,OK,OPEN\nPROFILE:", raw);
        Assert.DoesNotContain("MOTS:", raw);
    }
}
