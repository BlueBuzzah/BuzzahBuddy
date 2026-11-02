using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>
/// The Siri commands run against the real GloveControlService over FakeBluetoothService.
/// Every wait is driven by ManualTimeProvider or an explicit event; WaitAsync only bounds a
/// test that would otherwise hang on a regression.
/// </summary>
public class VoiceCommandServiceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static string Info(int profile = 1, string batteries = "BATP:4.02\nBATS:4.02", int secondary = 1) =>
        $"CMD:INFO\nROLE:PRIMARY\nMOTORS:4\nPROFILE:{profile}:preset\n{batteries}\nSECONDARY:{secondary}\nSTATUS:IDLE\n\x04";

    private const string Idle = "CMD:SESSION_STATUS\nSESSION_STATUS:IDLE\n\x04";
    private const string Running = "CMD:SESSION_STATUS\nSESSION_STATUS:RUNNING\nELAPSED:600\nTOTAL:3600\n\x04";
    private const string StartAccepted = "CMD:SESSION_START\nSESSION_STATUS:RUNNING\nELAPSED:0\nTOTAL:3600\n\x04";

    private sealed class Rig
    {
        public FakeBluetoothService Bluetooth { get; }
        public GloveControlService Glove { get; }
        public FakeReconnectionService Reconnection { get; }
        public AutoConnectService AutoConnect { get; }
        public SessionCommandLock Lock { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public List<int> SavedProfiles { get; } = new();
        public TherapyProfile? SavedCustom { get; set; }
        public VoiceCommandService Service { get; }

        public Rig(bool connected = true)
        {
            Bluetooth = new FakeBluetoothService();
            if (!connected)
            {
                Bluetooth.CurrentConnectionState = ConnectionState.Disconnected;
                Bluetooth.ConnectedDevice = null;
            }
            Bluetooth.CannedResponses["INFO"] = Info();
            Bluetooth.CannedResponses["SESSION_STATUS"] = Idle;
            Bluetooth.CannedResponses["SESSION_START"] = StartAccepted;
            Bluetooth.CannedResponses["SESSION_PAUSE"] = "CMD:SESSION_PAUSE\nSTATUS:PAUSED\n\x04";
            Bluetooth.CannedResponses["SESSION_STOP"] = "CMD:SESSION_STOP\nSTATUS:STOPPED\n\x04";
            Bluetooth.CannedResponses["PROFILE_LOAD"] = "CMD:PROFILE_LOAD\nSTATUS:REBOOTING\n\x04";
            Bluetooth.CannedResponses["PROFILE_GET"] =
                "CMD:PROFILE_GET\nTYPE:LRA\nFREQ:250\nON:100.0\nOFF:67.0\nSESSION:120\n" +
                "AMPMIN:100\nAMPMAX:100\nPATTERN:rndp\nMIRROR:0\nJITTER:0\nFINGERS:4\n\x04";
            Bluetooth.CannedResponses["PROFILE_CUSTOM"] = "CMD:PROFILE_CUSTOM\nSTATUS:CUSTOM_LOADED\n\x04";
            Glove = new GloveControlService(Bluetooth);
            Reconnection = new FakeReconnectionService(Glove);
            AutoConnect = new AutoConnectService(Bluetooth, _ => { });
            Service = new VoiceCommandService(
                Bluetooth, Glove, Reconnection, AutoConnect, Lock,
                () => Task.FromResult(SavedCustom),
                id => { SavedProfiles.Add(id); return Task.CompletedTask; },
                Time);
        }

        public CommandLog Sent => Bluetooth.SentCommands;

        /// <summary>
        /// The gloves reboot and come back: the link drops, the reconnect loop runs, the link
        /// returns, and the reconnect's INFO raises DeviceProfileChanged. The test sends that
        /// INFO itself (state set without the Connected event, so GloveControlService's
        /// background sync doesn't interleave with the follow-through's commands).
        /// </summary>
        public async Task RebootAndReconnectAsync(int profile)
        {
            Bluetooth.RaiseConnectionStateChanged(ConnectionState.Disconnected);
            Reconnection.Raise(ReconnectionState.Reconnecting);
            Bluetooth.CannedResponses["INFO"] = Info(profile);
            Bluetooth.CurrentConnectionState = ConnectionState.Connected;
            Reconnection.Raise(ReconnectionState.Succeeded);
            await Glove.GetDeviceInfoAsync();
        }
    }

    // ===== Connection =====

    [Fact]
    public async Task Connected_RunsImmediately()
    {
        var rig = new Rig();

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal("Starting your Regular session.", reply);
        Assert.Single(rig.Sent, c => c == "SESSION_START");
    }

    [Fact]
    public async Task AutoConnectConnecting_WaitsThenRuns()
    {
        var rig = new Rig(connected: false);
        var link = new TaskCompletionSource<bool>();
        rig.Bluetooth.ConnectToKnown = (_, _) =>
        {
            rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Connecting);
            return link.Task;
        };
        rig.AutoConnect.Start("GLOVE-1");

        var reply = rig.Service.RunAsync(VoiceCommand.CheckBattery, 0);
        Assert.False(reply.IsCompleted);

        rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Connected);
        link.SetResult(true);

        Assert.Equal("Primary glove 80 percent, secondary 80 percent.", await reply.WaitAsync(Bound));
    }

    [Fact]
    public async Task AutoConnectFails_NotConnected()
    {
        var rig = new Rig(connected: false);
        var link = new TaskCompletionSource<bool>();
        rig.Bluetooth.ConnectToKnown = (_, _) =>
        {
            rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Connecting);
            return link.Task;
        };
        rig.AutoConnect.Start("GLOVE-1");

        var reply = rig.Service.RunAsync(VoiceCommand.Start, 0);
        Assert.False(reply.IsCompleted);

        rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Disconnected);
        link.SetResult(false);

        Assert.Equal(VoiceReplies.NotConnected, await reply.WaitAsync(Bound));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task LaunchPending_Waits()
    {
        var rig = new Rig(connected: false);
        rig.Bluetooth.ConnectToKnown = (_, _) =>
        {
            rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Connected);
            return Task.FromResult(true);
        };

        var reply = rig.Service.RunAsync(VoiceCommand.CheckBattery, 0);
        Assert.False(reply.IsCompleted);

        rig.AutoConnect.Start("GLOVE-1");

        Assert.Equal("Primary glove 80 percent, secondary 80 percent.", await reply.WaitAsync(Bound));
    }

    [Fact]
    public async Task SettingOffOrNothingRemembered_NotConnected()
    {
        var rig = new Rig(connected: false);
        rig.AutoConnect.SkipLaunch();

        // The clock never moves, so this only finishes if nothing is waited for
        Assert.Equal(VoiceReplies.NotConnected, await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task ReconnectionRunning_RepliesWithinSirisLimit()
    {
        // A reconnect with the gloves off runs for minutes; Siri gives up after about 10 s.
        Assert.True(VoiceCommandService.ConnectWait <= TimeSpan.FromSeconds(5));
        var rig = new Rig(connected: false);
        rig.AutoConnect.SkipLaunch();
        rig.Reconnection.Raise(ReconnectionState.Reconnecting);

        var reply = rig.Service.RunAsync(VoiceCommand.Start, 0);
        rig.Time.Advance(VoiceCommandService.ConnectWait - TimeSpan.FromSeconds(1));
        Assert.False(reply.IsCompleted);

        rig.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(VoiceReplies.NotConnected, await reply.WaitAsync(Bound));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task AutoConnectFailedEarlier_NoTryAgain()
    {
        var rig = new Rig(connected: false);
        var attempts = 0;
        rig.Bluetooth.ConnectToKnown = (_, _) =>
        {
            attempts++;
            return Task.FromResult(false);
        };
        rig.AutoConnect.Start("GLOVE-1");
        Assert.Equal(AutoConnectState.Failed, rig.AutoConnect.State);

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.NotConnected, reply);
        Assert.Equal(1, attempts);
    }

    // ===== Snapshot =====

    [Fact]
    public async Task CheckBattery_UsesFreshInfo()
    {
        var rig = new Rig();
        // The gloves' own levels win: 3.85 V reads low, 3.73 V doesn't.
        rig.Bluetooth.CannedResponses["INFO"] = Info(batteries: "BATP:3.85\nBATS:3.73\nBATLP:LOW\nBATLS:NORMAL");

        var reply = await rig.Service.RunAsync(VoiceCommand.CheckBattery, 0).WaitAsync(Bound);

        Assert.Equal("Primary glove 55 percent, low, secondary 20 percent.", reply);
        Assert.Equal(new[] { "INFO" }, rig.Sent);
    }

    [Fact]
    public async Task CheckProfile_UsesSessionStatus()
    {
        var rig = new Rig();
        await rig.Glove.GetDeviceInfoAsync();
        rig.Sent.Clear();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        // The app's cached status says idle; the gloves say running.
        var reply = await rig.Service.RunAsync(VoiceCommand.CheckProfile, 0).WaitAsync(Bound);

        Assert.Equal("Your Regular session is running, 50 minutes left.", reply);
        Assert.Equal(new[] { "SESSION_STATUS" }, rig.Sent);
    }

    // Right after a cold-launch connect the post-connect INFO may not have landed yet.
    [Fact]
    public async Task CheckProfile_WithoutAPriorInfo_AsksForIt()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["INFO"] = Info(profile: 2);

        var reply = await rig.Service.RunAsync(VoiceCommand.CheckProfile, 0).WaitAsync(Bound);

        Assert.Equal("Your gloves are set to Noisy.", reply);
        Assert.Equal(new[] { "INFO", "SESSION_STATUS" }, rig.Sent);
    }

    // ===== Actions =====

    [Fact]
    public async Task Pause_SendsPauseUnderLock()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        var held = await rig.Lock.AcquireAsync();
        var reply = rig.Service.RunAsync(VoiceCommand.Pause, 0);
        Assert.Empty(rig.Sent);
        Assert.False(reply.IsCompleted);

        held.Dispose();

        Assert.Equal(VoiceReplies.Paused, await reply.WaitAsync(Bound));
        Assert.Single(rig.Sent, c => c == "SESSION_PAUSE");
    }

    // The on-screen Pause got the lock first: the voice Pause reads PAUSED once it holds it.
    [Fact]
    public async Task Pause_AfterATapPausedFirst_AlreadyPaused()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        var held = await rig.Lock.AcquireAsync();
        var reply = rig.Service.RunAsync(VoiceCommand.Pause, 0);
        Assert.Empty(rig.Sent);
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] =
            "CMD:SESSION_STATUS\nSESSION_STATUS:PAUSED\nELAPSED:600\nTOTAL:3600\n\x04";
        held.Dispose();

        Assert.Equal(VoiceReplies.AlreadyPaused, await reply.WaitAsync(Bound));
        Assert.DoesNotContain("SESSION_PAUSE", rig.Sent);
    }

    [Fact]
    public async Task Start_FirmwareRefusesBattery()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_START"] = "CMD:SESSION_START\nERROR:Battery too low\n\x04";

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.NeedsCharging, reply);
        Assert.Single(rig.Sent, c => c == "SESSION_START");
    }

    // The cached status stops updating while the app is in the background, where Siri runs:
    // a session that ended there still reads RUNNING until the gloves are asked.
    [Fact]
    public async Task Start_CachedRunningButTheGlovesAreIdle_Starts()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;
        await rig.Glove.GetSessionStatusAsync();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Idle;
        rig.Sent.Clear();

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal("Starting your Regular session.", reply);
        Assert.Single(rig.Sent, c => c == "SESSION_START");
    }

    [Fact]
    public async Task Start_SecondaryNotLinked_NoSecondary()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["INFO"] = Info(secondary: 0);

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.NoSecondary, reply);
        Assert.DoesNotContain("SESSION_START", rig.Sent);
    }

    // The primary's own low level wins over a voltage that would read fine.
    [Fact]
    public async Task Start_PrimaryReportsLow_NeedsCharging()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["INFO"] = Info(batteries: "BATP:4.02\nBATS:4.02\nBATLP:LOW\nBATLS:NORMAL");

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.NeedsCharging, reply);
        Assert.DoesNotContain("SESSION_START", rig.Sent);
    }

    [Fact]
    public async Task StopPrecheck_Idle()
    {
        var rig = new Rig();

        Assert.Equal(VoiceReplies.NoSession, await rig.Service.StopPrecheckAsync().WaitAsync(Bound));
        Assert.DoesNotContain("SESSION_STOP", rig.Sent);
    }

    [Fact]
    public async Task StopPrecheck_Running()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        Assert.Null(await rig.Service.StopPrecheckAsync().WaitAsync(Bound));
        Assert.DoesNotContain("SESSION_STOP", rig.Sent);
    }

    // Swift calls RunAsync(Stop) only after the person said yes to "End your session?"
    [Fact]
    public async Task Stop_AfterConfirming_EndsTheSession()
    {
        var rig = new Rig();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        var reply = await rig.Service.RunAsync(VoiceCommand.Stop, 0).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.Ended, reply);
        Assert.Single(rig.Sent, c => c == "SESSION_STOP");
    }

    // ===== Profile switch =====

    [Fact]
    public async Task SwitchThenStart_RepliesAtOnce_ThenStartsAfterReconnect()
    {
        var rig = new Rig();

        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        Assert.Equal("Switching to Noisy. Your session starts when the gloves restart.", reply);
        Assert.Contains("PROFILE_LOAD:2", rig.Sent);
        Assert.True(rig.Service.IsSwitching);
        Assert.DoesNotContain("SESSION_START", rig.Sent);

        await rig.RebootAndReconnectAsync(profile: 2);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.Equal(new[] { 2 }, rig.SavedProfiles);
        Assert.False(rig.Service.IsSwitching);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchToCustom_SendsSavedCustomBeforeStart(bool hasSaved)
    {
        var rig = new Rig();
        if (hasSaved)
        {
            rig.SavedCustom = new TherapyProfile
            {
                ProfileId = TherapyProfile.CustomProfileId,
                TimeOn = 0.100, TimeOff = 0.067, TimeSession = 120,
                AmplitudeMin = 100, AmplitudeMax = 100, Jitter = 15.5, Mirror = false, Fingers = 4,
            };
        }

        Assert.Equal("Switching to Custom. Your session starts when the gloves restart.",
            await rig.Service.RunAsync(VoiceCommand.Start, TherapyProfile.CustomProfileId).WaitAsync(Bound));
        await rig.RebootAndReconnectAsync(profile: TherapyProfile.CustomProfileId);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        var start = rig.Sent.IndexOf("SESSION_START");
        Assert.True(start >= 0);
        if (hasSaved)
            Assert.True(rig.Sent.IndexOf("PROFILE_CUSTOM:JITTER:15.5") is var custom && custom >= 0 && custom < start);
        else
            Assert.DoesNotContain(rig.Sent, c => c.StartsWith("PROFILE_GET") || c.StartsWith("PROFILE_CUSTOM"));
    }

    [Fact]
    public async Task SwitchThenStart_ReconnectGivesUp_NoStart()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        rig.Bluetooth.RaiseConnectionStateChanged(ConnectionState.Disconnected);
        rig.Reconnection.Raise(ReconnectionState.Reconnecting);
        rig.Reconnection.Raise(ReconnectionState.Failed);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.DoesNotContain("SESSION_START", rig.Sent);
        Assert.False(rig.Service.IsSwitching);

        // Connected again by hand later: a Start just starts.
        rig.Bluetooth.CannedResponses["INFO"] = Info(profile: 2);
        rig.Bluetooth.CurrentConnectionState = ConnectionState.Connected;
        var reply = await rig.Service.RunAsync(VoiceCommand.Start, 0).WaitAsync(Bound);
        Assert.NotEqual(VoiceReplies.Restarting, reply);
        Assert.Equal("Starting your Noisy session.", reply);
    }

    // A manual connect that replaces the reconnect still brings the gloves back on the new profile.
    [Fact]
    public async Task SwitchThenStart_ReconnectSupersededByConnect_StillStarts()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        rig.Reconnection.Raise(ReconnectionState.Cancelled, supersededByConnect: true);
        Assert.True(rig.Service.IsSwitching);
        await rig.RebootAndReconnectAsync(profile: 2);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
    }

    // ExpectingReboot's 30 s TTL runs on the wall clock, which Advance doesn't move, so this
    // doesn't go on to ask for another Start (in the app, 120 real seconds have passed by then).
    [Fact]
    public async Task SwitchThenStart_Backstop120s()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        rig.Time.Advance(TimeSpan.FromSeconds(119));
        Assert.True(rig.Service.IsSwitching);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.False(rig.Service.IsSwitching);
        Assert.Empty(rig.SavedProfiles);

        // The gloves turning up afterwards doesn't start anything
        await rig.RebootAndReconnectAsync(profile: 2);
        Assert.DoesNotContain("SESSION_START", rig.Sent);
    }

    // An INFO answered on the old link, before the reboot drops it, already names the new
    // profile: that isn't the gloves coming back, so it must not start them. The reconnect
    // then fails (raised in order on this thread, so the stale INFO had its chance first).
    [Fact]
    public async Task SwitchThenStart_ProfileReportedBeforeTheReboot_DoesNotStart()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        rig.Bluetooth.CannedResponses["INFO"] = Info(profile: 2);
        await rig.Glove.GetDeviceInfoAsync();
        rig.Reconnection.Raise(ReconnectionState.Failed);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.DoesNotContain("SESSION_START", rig.Sent);
        Assert.False(rig.Service.IsSwitching);
    }

    private const string SecondaryRefusal = "CMD:SESSION_START\nERROR:Secondary not connected\n\x04";

    /// <summary>Switches to Noisy and brings the gloves back with every start refused for the secondary.</summary>
    private static async Task<Rig> SwitchedWithSecondaryMissingAsync()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);
        rig.Bluetooth.CannedResponses["SESSION_START"] = SecondaryRefusal;
        await rig.RebootAndReconnectAsync(profile: 2);
        // The backstop plus the first retry's wait: the refused start is done
        await rig.Time.WhenPendingTimers(2).WaitAsync(Bound);
        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.True(rig.Service.IsSwitching);
        return rig;
    }

    [Fact]
    public async Task SwitchThenStart_SecondaryNotBackYet_RetriesUntilAccepted()
    {
        var rig = await SwitchedWithSecondaryMissingAsync();
        rig.Bluetooth.CannedResponses["SESSION_START"] = StartAccepted;

        rig.Time.Advance(TimeSpan.FromSeconds(3));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Equal(2, rig.Sent.Count(c => c == "SESSION_START"));
        Assert.True(rig.Glove.CurrentSessionStatus.IsRunning);
        Assert.False(rig.Service.IsSwitching);
    }

    [Fact]
    public async Task SwitchThenStart_SecondaryNeverBack_GivesUpAtTheBackstop()
    {
        var rig = await SwitchedWithSecondaryMissingAsync();

        // Retries 3, 5, then every 10 s after the first refusal: at 3, 8, 18 ... 118 s
        var elapsed = 0;
        foreach (var delay in new[] { 3, 5 }.Concat(Enumerable.Repeat(10, 20)))
        {
            if (elapsed + delay >= 120)
                break;
            await rig.Time.WhenPendingTimers(2).WaitAsync(Bound);
            rig.Time.Advance(TimeSpan.FromSeconds(delay));
            elapsed += delay;
        }
        Assert.Equal(118, elapsed);
        await rig.Time.WhenPendingTimers(2).WaitAsync(Bound);
        Assert.False(rig.Service.FollowThrough.IsCompleted);

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Equal(14, rig.Sent.Count(c => c == "SESSION_START"));
        Assert.False(rig.Service.IsSwitching);
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(14, rig.Sent.Count(c => c == "SESSION_START"));
    }

    [Fact]
    public async Task SwitchThenStart_SessionActiveWhenARetryFires_NoFurtherStart()
    {
        var rig = await SwitchedWithSecondaryMissingAsync();
        // The person started it on screen meanwhile
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;
        await rig.Glove.GetSessionStatusAsync();

        rig.Time.Advance(TimeSpan.FromSeconds(3));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.False(rig.Service.IsSwitching);
    }

    // The reconnect cleared ExpectingReboot, so only IsSwitching keeps a second Start from
    // sending another PROFILE_LOAD or a SESSION_START of its own.
    [Fact]
    public async Task StartDuringTheSecondaryRetries_Restarting()
    {
        var rig = await SwitchedWithSecondaryMissingAsync();
        Assert.False(rig.Glove.ExpectingReboot);

        Assert.Equal(VoiceReplies.Restarting, await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound));
        Assert.Equal(VoiceReplies.Restarting, await rig.Service.RunAsync(VoiceCommand.Start, 3).WaitAsync(Bound));
        Assert.Single(rig.Sent, c => c.StartsWith("PROFILE_LOAD"));
        Assert.Single(rig.Sent, c => c == "SESSION_START");

        rig.Bluetooth.CannedResponses["SESSION_START"] = StartAccepted;
        rig.Time.Advance(TimeSpan.FromSeconds(3));
        await rig.Service.FollowThrough.WaitAsync(Bound);
        Assert.Equal(2, rig.Sent.Count(c => c == "SESSION_START"));
    }

    // A tap holds the session lock across the 120 s mark: the retry waiting behind it gives up.
    [Fact]
    public async Task SwitchThenStart_RetryHeldPastTheBackstop_NoStart()
    {
        var rig = await SwitchedWithSecondaryMissingAsync();
        rig.Bluetooth.CannedResponses["SESSION_START"] = StartAccepted;

        var held = await rig.Lock.AcquireAsync();
        rig.Time.Advance(TimeSpan.FromSeconds(3));
        rig.Time.Advance(TimeSpan.FromSeconds(117));
        held.Dispose();
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.False(rig.Service.IsSwitching);
    }

    /// <summary>Switches to Noisy and brings the gloves back with the first start timing out.</summary>
    private static async Task<Rig> SwitchedWithStartTimingOutAsync()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);
        rig.Bluetooth.ThrowOnCommand["SESSION_START"] = new TimeoutException("Command 'SESSION_START' timed out after 7000ms");
        await rig.RebootAndReconnectAsync(profile: 2);
        // The backstop plus the first retry's wait: the timed-out start is done
        await rig.Time.WhenPendingTimers(2).WaitAsync(Bound);
        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.True(rig.Service.IsSwitching);
        rig.Bluetooth.ThrowOnCommand.Remove("SESSION_START");
        return rig;
    }

    // Right after the reboot the primary is busy re-syncing the secondary and can starve a command.
    [Fact]
    public async Task SwitchThenStart_StartTimesOut_RetriesUntilAccepted()
    {
        var rig = await SwitchedWithStartTimingOutAsync();

        rig.Time.Advance(TimeSpan.FromSeconds(3));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Equal(2, rig.Sent.Count(c => c == "SESSION_START"));
        // The retry asked the gloves first
        var first = rig.Sent.IndexOf("SESSION_START");
        Assert.Contains("SESSION_STATUS", rig.Sent.Skip(first + 1).TakeWhile(c => c != "SESSION_START"));
        Assert.True(rig.Glove.CurrentSessionStatus.IsRunning);
        Assert.False(rig.Service.IsSwitching);
    }

    // The timed-out start was accepted; only its reply was lost. Never start a running session again.
    [Fact]
    public async Task SwitchThenStart_TimedOutStartWasAccepted_NoSecondStart()
    {
        var rig = await SwitchedWithStartTimingOutAsync();
        rig.Bluetooth.CannedResponses["SESSION_STATUS"] = Running;

        rig.Time.Advance(TimeSpan.FromSeconds(3));
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.Equal("SESSION_STATUS", rig.Sent[^1]);
        Assert.False(rig.Service.IsSwitching);
    }

    [Fact]
    public async Task SwitchThenStart_OtherRefusal_EndsAtOnce()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);
        rig.Bluetooth.CannedResponses["SESSION_START"] = "CMD:SESSION_START\nERROR:Battery too low\n\x04";

        await rig.RebootAndReconnectAsync(profile: 2);
        await rig.Service.FollowThrough.WaitAsync(Bound);

        Assert.Single(rig.Sent, c => c == "SESSION_START");
        Assert.False(rig.Service.IsSwitching);
    }

    [Fact]
    public async Task SecondStartWhileSwitching_NoSecondProfileLoad()
    {
        var rig = new Rig();
        await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound);

        Assert.Equal(VoiceReplies.Restarting, await rig.Service.RunAsync(VoiceCommand.Start, 2).WaitAsync(Bound));
        Assert.Equal(VoiceReplies.Restarting, await rig.Service.RunAsync(VoiceCommand.Start, 3).WaitAsync(Bound));
        Assert.Single(rig.Sent, c => c.StartsWith("PROFILE_LOAD"));

        rig.Time.Advance(TimeSpan.FromSeconds(120));
        await rig.Service.FollowThrough.WaitAsync(Bound);
    }
}
