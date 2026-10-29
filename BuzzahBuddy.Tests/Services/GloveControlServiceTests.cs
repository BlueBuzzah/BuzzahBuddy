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

    private const string InfoWithoutSecondary =
        "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nMOTORS:4\nPROFILE:2:noisy_vcr\nBATP:3.7\nBATS:0.00\nSTATUS:IDLE\n\x04";
    private const string InfoWithSecondary =
        "ROLE:PRIMARY\nNAME:BlueBuzzah\nFW:2.1.0\nMOTORS:4\nPROFILE:2:noisy_vcr\nBATP:3.7\nBATS:3.8\nSTATUS:IDLE\n\x04";

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
    }

    // After a restart the secondary rejoins the primary a few seconds after the phone
    // connects, so the first INFO has no secondary battery. The service asks again.
    [Fact]
    public async Task MissingSecondaryBattery_IsAskedForAgainUntilItArrives()
    {
        var fake = new FakeBluetoothService();
        foreach (var reply in new[] { InfoWithoutSecondary, InfoWithoutSecondary, InfoWithSecondary })
            fake.QueuedResponses.Enqueue(reply);
        var service = new GloveControlService(fake) { SecondaryBatteryRetryDelays = [TimeSpan.FromMilliseconds(5)] };
        var readings = new List<double?>();
        service.DeviceInfoReceived += (_, info) => { lock (readings) readings.Add(info.BatterySecondaryVoltage); };

        await service.GetDeviceInfoAsync();
        await WaitUntil(() => { lock (readings) return readings.Count >= 3; });
        await Task.Delay(50);

        lock (readings)
            Assert.Equal(new double?[] { null, null, 3.8 }, readings);
        Assert.Equal(3, fake.SentCommands.Count(c => c == "INFO"));
    }

    [Fact]
    public async Task MissingSecondaryBattery_StopsAskingOnceDisconnected()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] = InfoWithoutSecondary;
        var service = new GloveControlService(fake) { SecondaryBatteryRetryDelays = [TimeSpan.FromMilliseconds(20)] };

        await service.GetDeviceInfoAsync();
        fake.CurrentConnectionState = ConnectionState.Disconnected;
        await Task.Delay(150);

        Assert.Single(fake.SentCommands, c => c == "INFO");
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
    public async Task LoadProfileAsync_RebootingResponse_DisconnectsForReconnectImmediately()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_LOAD"] = "CMD:PROFILE_LOAD\nSTATUS:REBOOTING\n\x04";
        var service = new GloveControlService(fake);

        await service.LoadProfileAsync(3);

        Assert.Equal(1, fake.DisconnectForReconnectCalls);
    }

    [Fact]
    public async Task RestartDeviceAsync_RebootingResponse_DisconnectsForReconnectImmediately()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["RESTART"] = "CMD:RESTART\nSTATUS:REBOOTING\n\x04";
        var service = new GloveControlService(fake);

        await service.RestartDeviceAsync();

        Assert.True(service.ExpectingReboot);
        Assert.Equal(1, fake.DisconnectForReconnectCalls);
    }

    [Fact]
    public async Task StartSessionAsync_ReplyWithTotalAndElapsed_UsesTheDevicesDuration()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["SESSION_START"] =
            "CMD:SESSION_START\nSESSION_STATUS:RUNNING\nELAPSED:0\nTOTAL:2700\n\x04";
        var service = new GloveControlService(fake);

        await service.StartSessionAsync();

        Assert.Equal(SessionState.RUNNING, service.CurrentSessionStatus.Status);
        Assert.Equal(2700, service.CurrentSessionStatus.TotalTimeSeconds);
        Assert.Equal(0, service.CurrentSessionStatus.ElapsedTimeSeconds);
    }

    [Fact]
    public async Task StartSessionAsync_LegacyReplyWithoutTotal_FallsBackToTheProfileDuration()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["SESSION_START"] = "SESSION_STATUS:RUNNING\n\x04";
        var service = new GloveControlService(fake);

        await service.StartSessionAsync();

        Assert.Equal(120 * 60, service.CurrentSessionStatus.TotalTimeSeconds);
    }

    [Fact]
    public async Task StartSessionAsync_SyncNotReady_RetriesUntilTheGlovesAccept()
    {
        var fake = new FakeBluetoothService();
        fake.QueuedResponses.Enqueue("CMD:SESSION_START\nERROR:Sync not ready\n\x04");
        fake.QueuedResponses.Enqueue("CMD:SESSION_START\nERROR:Sync not ready\n\x04");
        fake.QueuedResponses.Enqueue("CMD:SESSION_START\nSESSION_STATUS:RUNNING\nELAPSED:0\nTOTAL:7200\n\x04");
        var service = new GloveControlService(fake) { SyncNotReadyRetryInterval = TimeSpan.FromMilliseconds(1) };

        await service.StartSessionAsync();

        Assert.Equal(3, fake.SentCommands.Count(c => c == "SESSION_START"));
        Assert.Equal(SessionState.RUNNING, service.CurrentSessionStatus.Status);
    }

    [Fact]
    public async Task StartSessionAsync_SyncNeverReady_SurfacesTheErrorAfterTheRetryWindow()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["SESSION_START"] = "CMD:SESSION_START\nERROR:Sync not ready\n\x04";
        var service = new GloveControlService(fake)
        {
            SyncNotReadyRetryInterval = TimeSpan.FromMilliseconds(5),
            SyncNotReadyRetryWindow = TimeSpan.FromMilliseconds(50)
        };

        var ex = await Assert.ThrowsAsync<BlueBuzzahCommandException>(() => service.StartSessionAsync());

        Assert.Contains("Sync not ready", ex.Message);
        Assert.True(fake.SentCommands.Count(c => c == "SESSION_START") > 1);
    }

    [Fact]
    public async Task StartSessionAsync_OtherError_IsNotRetried()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["SESSION_START"] = "CMD:SESSION_START\nERROR:Battery too low\n\x04";
        var service = new GloveControlService(fake) { SyncNotReadyRetryInterval = TimeSpan.FromMilliseconds(1) };

        await Assert.ThrowsAsync<BlueBuzzahCommandException>(() => service.StartSessionAsync());

        Assert.Single(fake.SentCommands, c => c == "SESSION_START");
    }

    [Fact]
    public async Task OnConnect_SyncsInfoThenSessionStatusThenLed_WithoutSeparateBatteryOrPing()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] =
            "CMD:INFO\nROLE:PRIMARY\nMOTORS:4\nPROFILE:2:noisy_vcr\nBATP:3.72\nBATS:3.68\nSTATUS:IDLE\n\x04";
        fake.CannedResponses["SESSION_STATUS"] = "CMD:SESSION_STATUS\nSESSION_STATUS:IDLE\n\x04";
        fake.CannedResponses["THERAPY_LED_OFF"] = "CMD:THERAPY_LED_OFF\nTHERAPY_LED_OFF:true\n\x04";
        var service = new GloveControlService(fake);
        var infoReceived = new TaskCompletionSource<GloveDeviceInfo>();
        var ledReceived = new TaskCompletionSource<bool>();
        service.DeviceInfoReceived += (_, info) => infoReceived.TrySetResult(info);
        service.TherapyLedOffReceived += (_, ledOff) => ledReceived.TrySetResult(ledOff);

        fake.RaiseConnectionStateChanged(ConnectionState.Connected);

        var info = await infoReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await ledReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3.72, info.BatteryPrimaryVoltage);
        Assert.Equal(3.68, info.BatterySecondaryVoltage);
        Assert.Equal(new[] { "INFO", "SESSION_STATUS", "THERAPY_LED_OFF" }, fake.SentCommands);
    }

    [Fact]
    public async Task OnConnect_ReportsSyncRunningUntilItFinishes()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] = "CMD:INFO\nROLE:PRIMARY\nPROFILE:2:noisy_vcr\nBATP:3.72\nBATS:3.68\n\x04";
        fake.CannedResponses["SESSION_STATUS"] = "CMD:SESSION_STATUS\nSESSION_STATUS:IDLE\n\x04";
        fake.CannedResponses["THERAPY_LED_OFF"] = "CMD:THERAPY_LED_OFF\nTHERAPY_LED_OFF:false\n\x04";
        var service = new GloveControlService(fake);
        Assert.False(service.IsPostConnectSyncRunning);

        fake.RaiseConnectionStateChanged(ConnectionState.Connected);

        // Visible as soon as Connected is raised, before the sync's first reply
        Assert.True(service.IsPostConnectSyncRunning);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (service.IsPostConnectSyncRunning && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.False(service.IsPostConnectSyncRunning);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_OverlappingCalls_ShareOneInfo()
    {
        var fake = new FakeBluetoothService { HoldReplies = new TaskCompletionSource() };
        fake.CannedResponses["INFO"] = "CMD:INFO\nROLE:PRIMARY\nPROFILE:2:noisy_vcr\nBATP:3.72\nBATS:3.68\n\x04";
        var service = new GloveControlService(fake);
        var infoEvents = 0;
        service.DeviceInfoReceived += (_, _) => infoEvents++;

        var first = service.GetDeviceInfoAsync();
        var second = service.GetDeviceInfoAsync(TimeSpan.FromSeconds(30));
        fake.HoldReplies.SetResult();

        Assert.Same(await first, await second);
        Assert.Single(fake.SentCommands, c => c == "INFO");
        Assert.Equal(1, infoEvents);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_WithMaxAge_ServesRecentReadingUntilTheLinkDrops()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["INFO"] = "CMD:INFO\nROLE:PRIMARY\nPROFILE:2:noisy_vcr\nBATP:3.72\nBATS:3.68\n\x04";
        var service = new GloveControlService(fake);

        await service.GetDeviceInfoAsync();
        await service.GetDeviceInfoAsync(TimeSpan.FromSeconds(30));
        Assert.Single(fake.SentCommands, c => c == "INFO");

        // A reading from the old link must not answer for the next one
        fake.RaiseConnectionStateChanged(ConnectionState.Disconnected);
        fake.CurrentConnectionState = ConnectionState.Connected;
        await service.GetDeviceInfoAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, fake.SentCommands.Count(c => c == "INFO"));
    }

    [Fact]
    public async Task OnConnect_RetriesOnlyTheStepThatFailed()
    {
        var fake = new FakeBluetoothService();
        fake.QueuedResponses.Enqueue("CMD:INFO\nROLE:PRIMARY\nPROFILE:2:noisy_vcr\nBATP:3.72\nBATS:3.68\n\x04");
        fake.QueuedResponses.Enqueue("CMD:SESSION_STATUS\nERROR:Busy\n\x04");
        fake.QueuedResponses.Enqueue("CMD:SESSION_STATUS\nSESSION_STATUS:IDLE\n\x04");
        fake.QueuedResponses.Enqueue("CMD:THERAPY_LED_OFF\nTHERAPY_LED_OFF:false\n\x04");
        var service = new GloveControlService(fake);
        var ledReceived = new TaskCompletionSource<bool>();
        service.TherapyLedOffReceived += (_, ledOff) => ledReceived.TrySetResult(ledOff);

        fake.RaiseConnectionStateChanged(ConnectionState.Connected);

        // The second attempt waits out the ladder's 2 s step
        Assert.False(await ledReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "INFO", "SESSION_STATUS", "SESSION_STATUS", "THERAPY_LED_OFF" }, fake.SentCommands);
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
        desired.Jitter = 15.5;

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        Assert.Equal("PROFILE_CUSTOM:JITTER:15.5", Assert.Single(fake.SentCommands));
    }

    // The firmware rejects TYPE, FREQ and PATTERN on the Custom profile — its
    // CustomOverrideData has nowhere to store them — so a batch containing any of
    // them fails outright.
    [Theory]
    [InlineData("TYPE")]
    [InlineData("FREQ")]
    [InlineData("PATTERN")]
    public async Task ApplyCustomProfileAsync_NeverSendsParametersTheCustomProfileRejects(string key)
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        await service.ApplyCustomProfileAsync(MakeProfile(), baseline: null);

        Assert.DoesNotContain(fake.SentCommands, c => c.Contains($":{key}:"));
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_NoBaseline_SendsThePersistedEightInOneCommand()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        await service.ApplyCustomProfileAsync(MakeProfile(), baseline: null);

        // One floor-widening prelude, then the eight persisted parameters — which
        // is exactly the firmware's 8-pair-per-command ceiling.
        Assert.Equal(2, fake.SentCommands.Count);
        Assert.Equal("PROFILE_CUSTOM:AMPMIN:20", fake.SentCommands[0]);
        Assert.Equal(
            new[] { "ON", "OFF", "SESSION", "MIRROR", "JITTER", "FINGERS", "AMPMAX", "AMPMIN" },
            Keys(fake.SentCommands[1]));
    }

    private static string[] Keys(string command) =>
        command.Split(':').Skip(1).Where((_, i) => i % 2 == 0).ToArray();

    [Fact]
    public async Task ApplyCustomProfileAsync_MapsUnitsToFirmwareVocabulary()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.TimeOn = 0.200;          // model is seconds; protocol is ms
        desired.Mirror = true;

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        var command = Assert.Single(fake.SentCommands);
        Assert.Contains("ON:200", command);
        Assert.Contains("MIRROR:1", command);
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_SendsFingersWhenItChanges()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.Fingers = 3;

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        Assert.Equal("PROFILE_CUSTOM:FINGERS:3", Assert.Single(fake.SentCommands));
    }

    // Each amplitude bound is cross-checked against the one already on the device,
    // and keys apply in the order sent — so the narrowing key must go second.
    [Fact]
    public async Task ApplyCustomProfileAsync_RaisingTheWindow_SendsAmpMaxFirst()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var baseline = MakeProfile();
        baseline.AmplitudeMin = 30;
        baseline.AmplitudeMax = 70;
        var desired = MakeProfile();
        desired.AmplitudeMin = 80;
        desired.AmplitudeMax = 100;

        await service.ApplyCustomProfileAsync(desired, baseline);

        // AMPMIN:80 first would be rejected against the device's ceiling of 70.
        Assert.Equal(new[] { "AMPMAX", "AMPMIN" }, Keys(Assert.Single(fake.SentCommands)));
    }

    // The gloves reboot on PROFILE_LOAD, and the post-connect INFO sync only runs if
    // the transport surfaces that disconnect. When it doesn't, the cached id is the
    // app's only record of the change — every page names the profile from it.
    [Fact]
    public async Task LoadProfile_WhenDeviceReboots_UpdatesTheCachedProfileId()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_LOAD"] = "STATUS:REBOOTING\nPROFILE:regular_vcr\n\x04";
        var service = new GloveControlService(fake);

        await service.LoadProfileAsync(1);

        Assert.Equal(1, service.DeviceProfileId);
    }

    [Fact]
    public async Task LoadProfile_WhenTheLinkDropsBeforeResponding_StillUpdatesTheCachedProfileId()
    {
        var fake = new FakeBluetoothService();
        fake.ThrowOnCommand["PROFILE_LOAD"] = new TimeoutException("link dropped");
        var service = new GloveControlService(fake);

        // A timeout here is treated as the reboot having happened, so the cached id
        // must move with it — otherwise it stays stale for the rest of the session.
        await service.LoadProfileAsync(1);

        Assert.Equal(1, service.DeviceProfileId);
    }

    // Settings saved on the phone carry whatever finger count they were seeded with
    // (another glove's, or the study's 4); the device's own count must always win.
    [Fact]
    public async Task SendSavedCustomProfile_KeepsTheDeviceFingersAndSendsOnlyChanges()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_GET"] =
            "TYPE:LRA\nFREQ:250\nON:100.0\nOFF:67.0\nSESSION:120\n" +
            "AMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:0\nJITTER:0\nFINGERS:4\n\x04";
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var saved = MakeProfile();
        saved.Fingers = 5;
        saved.Jitter = 15.5;

        await service.SendSavedCustomProfileAsync(saved);

        Assert.Equal(new[] { "PROFILE_GET", "PROFILE_CUSTOM:JITTER:15.5" }, fake.SentCommands);
    }

    // FINGERS in PROFILE_GET marks firmware that also persists Custom edits. If it
    // is absent, the app must not promise the settings survive a restart.
    [Fact]
    public async Task GetCurrentProfile_WithoutFingersKey_ReportsNoCustomPersistence()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_GET"] =
            "TYPE:LRA\nFREQ:250\nON:100.0\nOFF:67.0\nSESSION:120\n" +
            "AMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:1\nJITTER:23.5\n\x04";
        var service = new GloveControlService(fake);

        await service.GetCurrentProfileAsync();

        Assert.False(service.PersistsCustomProfile);
    }

    [Fact]
    public async Task GetCurrentProfile_WithFingersKey_ReportsCustomPersistence()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_GET"] =
            "TYPE:LRA\nFREQ:250\nON:100.0\nOFF:67.0\nSESSION:120\n" +
            "AMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:1\nJITTER:23.5\nFINGERS:4\n\x04";
        var service = new GloveControlService(fake);

        await service.GetCurrentProfileAsync();

        Assert.True(service.PersistsCustomProfile);
    }

    // At the tie the ordering branch is unobservable: an unchanged AMPMAX is
    // filtered out by the baseline diff, so only AMPMIN is ever sent. Pinned
    // because the branch condition looks like it should matter here and doesn't.
    [Fact]
    public async Task ApplyCustomProfileAsync_UnchangedCeiling_SendsOnlyAmpMin()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var baseline = MakeProfile();
        baseline.AmplitudeMin = 30;
        baseline.AmplitudeMax = 70;
        var desired = MakeProfile();
        desired.AmplitudeMin = 50;
        desired.AmplitudeMax = 70;   // unchanged

        await service.ApplyCustomProfileAsync(desired, baseline);

        Assert.Equal("PROFILE_CUSTOM:AMPMIN:50", Assert.Single(fake.SentCommands));
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_LoweringTheWindow_SendsAmpMinFirst()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var baseline = MakeProfile();
        baseline.AmplitudeMin = 80;
        baseline.AmplitudeMax = 100;
        var desired = MakeProfile();
        desired.AmplitudeMin = 30;
        desired.AmplitudeMax = 70;

        await service.ApplyCustomProfileAsync(desired, baseline);

        // AMPMAX:70 first would be rejected against the device's floor of 80.
        Assert.Equal(new[] { "AMPMIN", "AMPMAX" }, Keys(Assert.Single(fake.SentCommands)));
    }

    // One case per validation branch in BuildCustomProfileParameters
    // (mirroring firmware profile_manager.cpp setParameter ranges).
    public static TheoryData<string> InvalidProfileFields => new()
    {
        "OnLow", "OnHigh", "OffLow", "OffHigh",
        "SessionLow", "SessionHigh", "AmpLow", "AmpHigh", "AmpMinAboveMax",
        "JitterHigh", "FingersLow", "FingersAboveMotorCount",
    };

    [Theory]
    [MemberData(nameof(InvalidProfileFields))]
    public async Task ApplyCustomProfileAsync_OutOfFirmwareRange_ThrowsWithoutSending(string field)
    {
        var fake = new FakeBluetoothService();
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        switch (field)
        {
            case "OnLow": desired.TimeOn = 0.049; break;
            case "OnHigh": desired.TimeOn = 0.201; break;
            case "OffLow": desired.TimeOff = 0.029; break;
            case "OffHigh": desired.TimeOff = 0.201; break;
            case "SessionLow": desired.TimeSession = 0; break;
            case "SessionHigh": desired.TimeSession = 241; break;
            case "AmpLow": desired.AmplitudeMin = 19; break;
            case "AmpHigh": desired.AmplitudeMax = 101; break;
            case "AmpMinAboveMax": desired.AmplitudeMin = 90; desired.AmplitudeMax = 30; break;
            case "JitterHigh": desired.Jitter = 51; break;
            case "FingersLow": desired.Fingers = 0; break;
            // DeviceActuatorCount defaults to 4 until INFO reports otherwise.
            case "FingersAboveMotorCount": desired.Fingers = 5; break;
        }

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyCustomProfileAsync(desired));
        Assert.Empty(fake.SentCommands);
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_ValueAtCeilingWithFloatError_IsNotRejected()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.TimeOn = 0.2; // 0.2 * 1000.0 may compute just past the 200 ms ceiling

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        Assert.Contains("ON:200", Assert.Single(fake.SentCommands));
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_RoundsJitterToTheProtocolPrecision()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["PROFILE_CUSTOM"] = "STATUS:CUSTOM_LOADED\n\x04";
        var service = new GloveControlService(fake);

        var desired = MakeProfile();
        desired.Jitter = 15.36;        // "0.#" format → "15.4"

        await service.ApplyCustomProfileAsync(desired, MakeProfile());

        Assert.Contains("JITTER:15.4", Assert.Single(fake.SentCommands));
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_NoChanges_SendsNothing()
    {
        var fake = new FakeBluetoothService();
        var service = new GloveControlService(fake);

        await service.ApplyCustomProfileAsync(MakeProfile(), MakeProfile());

        Assert.Empty(fake.SentCommands);
    }

    [Fact]
    public async Task ApplyCustomProfileAsync_SecondChunkFails_ThrowsAfterFirstChunkApplied()
    {
        var fake = new FakeBluetoothService();
        fake.QueuedResponses.Enqueue("STATUS:CUSTOM_LOADED\n\x04");
        fake.QueuedResponses.Enqueue("ERROR:Invalid parameter: MIRROR\n\x04");
        var service = new GloveControlService(fake);

        // No baseline → amplitude-floor prelude succeeds, main batch errors.
        await Assert.ThrowsAsync<BlueBuzzahCommandException>(
            () => service.ApplyCustomProfileAsync(MakeProfile(), baseline: null));
        Assert.Equal(2, fake.SentCommands.Count);
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

    [Fact]
    public async Task GetBatteryAsync_ValidReadings_ReturnsVoltages()
    {
        var fake = new FakeBluetoothService();
        fake.CannedResponses["BATTERY"] = "BATP:3.72\nBATS:3.68\n\x04";
        var service = new GloveControlService(fake);

        var (primary, secondary) = await service.GetBatteryAsync();

        Assert.Equal(3.72, primary);
        Assert.Equal(3.68, secondary);
    }

    [Fact]
    public async Task GetBatteryAsync_MissingKeys_ReturnsNulls()
    {
        // A response without BATP/BATS (and no ERROR key) must not masquerade
        // as a 0.0V reading.
        var fake = new FakeBluetoothService();
        fake.CannedResponses["BATTERY"] = "STATUS:IDLE\n\x04";
        var service = new GloveControlService(fake);

        var (primary, secondary) = await service.GetBatteryAsync();

        Assert.Null(primary);
        Assert.Null(secondary);
    }

    [Fact]
    public async Task GetBatteryAsync_ZeroSentinel_ReturnsNulls()
    {
        // Firmware reports 0.00 for "no monitor"/"no reading"/"secondary
        // timed out" — treat as unavailable, not as an empty pack.
        var fake = new FakeBluetoothService();
        fake.CannedResponses["BATTERY"] = "BATP:0.00\nBATS:0.00\n\x04";
        var service = new GloveControlService(fake);

        var (primary, secondary) = await service.GetBatteryAsync();

        Assert.Null(primary);
        Assert.Null(secondary);
    }
}
