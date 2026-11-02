using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Runs a Siri command against the gloves and returns what Siri says. It never shows UI,
/// navigates, scans or starts a connect of its own: it waits, up to the connect timeout, for
/// the launch auto-connect or a reconnect already under way. Session commands share
/// <see cref="SessionCommandLock"/> with the on-screen buttons and read the gloves' state
/// once they hold it. Never throws: every path returns a reply.
/// </summary>
public sealed class VoiceCommandService(
    IBluetoothService bluetooth, IGloveControlService glove, IReconnectionService reconnection,
    AutoConnectService autoConnect, SessionCommandLock sessionLock,
    Func<Task<TherapyProfile?>> loadSavedCustom, Func<int, Task> saveLastProfile,
    TimeProvider? time = null)
{
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(BlueBuzzahConstants.ConnectionTimeoutSeconds);

    /// <summary>A profile switch gives up on the gloves coming back after this (the Gloves page's apply backstop).</summary>
    private static readonly TimeSpan SwitchBackstop = TimeSpan.FromSeconds(120);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private volatile bool _isSwitching;

    /// <summary>A voice profile switch is waiting for the gloves to restart, then starts the session.</summary>
    public bool IsSwitching => _isSwitching;

    /// <summary>The latest switch's follow-through; tests await it.</summary>
    internal Task FollowThrough { get; private set; } = Task.CompletedTask;

    public async Task<string> RunAsync(VoiceCommand command, int requestedProfileId, CancellationToken ct = default)
    {
        try
        {
            if (!await EnsureConnectedAsync(ct))
                return VoiceReplies.NotConnected;

            if (command is VoiceCommand.CheckBattery or VoiceCommand.CheckProfile)
                return VoiceReplies.Decide(command, requestedProfileId, await SnapshotAsync(command)).Reply;

            // Read the state under the lock, so a tap that got there first counts
            // (Pause then Pause must not Resume). Never held while waiting for a connect.
            using (await sessionLock.AcquireAsync(ct))
            {
                var decision = VoiceReplies.Decide(command, requestedProfileId, await SnapshotAsync(command));
                return decision.Action switch
                {
                    VoiceAction.None => decision.Reply,
                    VoiceAction.SwitchThenStart => await SwitchAsync(decision.ProfileId),
                    _ => await ActAsync(decision),
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VOICE] {command} failed: {ex.GetType().Name}: {ex.Message}");
            return command switch
            {
                VoiceCommand.Start => VoiceReplies.StartFailed,
                VoiceCommand.Pause => VoiceReplies.PauseFailed,
                VoiceCommand.Resume => VoiceReplies.ResumeFailed,
                VoiceCommand.Stop => VoiceReplies.StopFailed,
                _ => VoiceReplies.Decide(command, 0, Snapshot(null, glove.CurrentSessionStatus)).Reply,
            };
        }
    }

    /// <summary>The reply when there's nothing to stop; null means Siri asks "End your session?".</summary>
    public async Task<string?> StopPrecheckAsync(CancellationToken ct = default)
    {
        try
        {
            if (!await EnsureConnectedAsync(ct))
                return VoiceReplies.NotConnected;
            var decision = VoiceReplies.Decide(VoiceCommand.Stop, 0, await SnapshotAsync(VoiceCommand.Stop));
            return decision.Action == VoiceAction.ConfirmStop ? null : decision.Reply;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VOICE] Stop precheck failed: {ex.GetType().Name}: {ex.Message}");
            return VoiceReplies.StopFailed;
        }
    }

    private async Task<string> ActAsync(VoiceDecision decision)
    {
        Exception? error = null;
        try
        {
            await (decision.Action switch
            {
                VoiceAction.Start => glove.StartSessionAsync(),
                VoiceAction.Pause => glove.PauseSessionAsync(),
                VoiceAction.Resume => glove.ResumeSessionAsync(),
                // Swift calls Stop only after the person said yes to "End your session?"
                VoiceAction.ConfirmStop => glove.StopSessionAsync(),
                _ => throw new InvalidOperationException($"No session action for {decision.Action}"),
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VOICE] {decision.Action} failed: {ex.Message}");
            error = ex;
        }
        return VoiceReplies.Result(decision.Action, decision.ProfileId, error);
    }

    /// <summary>
    /// Loads the profile (the gloves reboot) and replies at once; the session starts in the
    /// background once they're back. Runs under the session lock.
    /// </summary>
    private async Task<string> SwitchAsync(int profileId)
    {
        _isSwitching = true;
        // Started before PROFILE_LOAD goes out: the link often drops inside the send, and only
        // a profile reported after that drop means the gloves have restarted.
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FollowThrough = FollowThroughAsync(profileId, loaded.Task);
        try
        {
            await glove.LoadProfileAsync(profileId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VOICE] PROFILE_LOAD:{profileId} failed: {ex.Message}");
            loaded.SetResult(false);
            // Only cleanup is left, and it never throws: wait so IsSwitching is clear on return.
            await FollowThrough;
            return VoiceReplies.Result(VoiceAction.SwitchThenStart, profileId, ex);
        }
        loaded.SetResult(true);
        return VoiceReplies.Result(VoiceAction.SwitchThenStart, profileId, null);
    }

    /// <summary>
    /// Pauses between start attempts while the primary says the secondary hasn't rejoined it
    /// after the restart; the last repeats until the switch's backstop.
    /// </summary>
    private static readonly TimeSpan[] SecondaryRetryDelays =
        [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private async Task FollowThroughAsync(int profileId, Task<bool> loaded)
    {
        // An INFO answered on the old link (before the reboot drops it) doesn't count.
        var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // true: the gloves are back on the profile; false: the reconnect gave up
        var outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLink(object? sender, ConnectionState state)
        {
            if (state != ConnectionState.Connected)
                dropped.TrySetResult();
        }
        void OnProfile(object? sender, int id)
        {
            if (id == profileId && dropped.Task.IsCompleted)
                outcome.TrySetResult(true);
        }
        void OnReconnect(object? sender, ReconnectionStateEventArgs e)
        {
            // A manual connect that replaces the reconnect still brings the gloves back.
            if (e.State == ReconnectionState.Failed || (e.State == ReconnectionState.Cancelled && !e.SupersededByConnect))
                outcome.TrySetResult(false);
        }

        bluetooth.ConnectionStateChanged += OnLink;
        glove.DeviceProfileChanged += OnProfile;
        reconnection.ReconnectionStateChanged += OnReconnect;
        using var backstopCts = new CancellationTokenSource();
        try
        {
            var backstop = Task.Delay(SwitchBackstop, _time, backstopCts.Token);
            if (!await loaded)
                return;
            await Task.WhenAny(outcome.Task, backstop);
            // The outcome wins a tie: its continuations run asynchronously, so the backstop
            // can finish the WhenAny first even when the gloves came back before it.
            if (!outcome.Task.IsCompleted || !await outcome.Task)
            {
                System.Diagnostics.Debug.WriteLine($"[VOICE] Gloves didn't come back on profile {profileId}; not starting");
                return;
            }

            // Firmware takes PROFILE_CUSTOM only while Custom is loaded. If it fails, don't
            // start on settings the person didn't choose.
            if (profileId == TherapyProfile.CustomProfileId && await loadSavedCustom() is { } saved)
                await glove.SendSavedCustomProfileAsync(saved);
            await saveLastProfile(profileId);

            // Siri promised the session. The secondary can rejoin the primary after the phone
            // does, so that refusal is retried until the backstop; any other ends it.
            for (var attempt = 0; ; attempt++)
            {
                using (await sessionLock.AcquireAsync())
                {
                    // A tap may have started one while the gloves came back.
                    if (glove.CurrentSessionStatus.IsActive)
                        return;
                    try
                    {
                        await glove.StartSessionAsync();
                        return;
                    }
                    catch (Exception ex) when (VoiceReplies.IsSecondaryNotConnected(ex))
                    {
                        System.Diagnostics.Debug.WriteLine($"[VOICE] Secondary not back yet (start {attempt + 1}); retrying");
                    }
                }

                var retry = Task.Delay(SecondaryRetryDelays[Math.Min(attempt, SecondaryRetryDelays.Length - 1)], _time, backstopCts.Token);
                await Task.WhenAny(retry, backstop);
                if (backstop.IsCompleted)
                {
                    System.Diagnostics.Debug.WriteLine($"[VOICE] Secondary never rejoined after switching to profile {profileId}; not starting");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VOICE] Start after switching to profile {profileId} failed: {ex.Message}");
        }
        finally
        {
            bluetooth.ConnectionStateChanged -= OnLink;
            glove.DeviceProfileChanged -= OnProfile;
            reconnection.ReconnectionStateChanged -= OnReconnect;
            backstopCts.Cancel();
            _isSwitching = false;
        }
    }

    /// <summary>
    /// Connected, or waits for the launch auto-connect and any connect or reconnect under
    /// way, up to the connect timeout. Starts no connect of its own, and doesn't retry a
    /// launch connect that already failed (Try Again is the person's call, on screen).
    /// </summary>
    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected)
            return true;
        if (autoConnect.State == AutoConnectState.Failed)
            return false;

        var gate = new object();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Wake()
        {
            lock (gate)
                changed.TrySetResult();
        }
        void OnConnection(object? sender, ConnectionState state) => Wake();
        void OnReconnect(object? sender, ReconnectionStateEventArgs e) => Wake();
        void OnAutoConnect(object? sender, EventArgs e) => Wake();

        bluetooth.ConnectionStateChanged += OnConnection;
        reconnection.ReconnectionStateChanged += OnReconnect;
        autoConnect.StateChanged += OnAutoConnect;
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var deadline = Task.Delay(ConnectWait, _time, deadlineCts.Token);
            // The launch connect, or the app deciding there's none to make, comes first.
            await Task.WhenAny(autoConnect.LaunchFinished, deadline);
            while (true)
            {
                Task next;
                lock (gate)
                    next = (changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                // Checked after arming, so a change in between still wakes the wait.
                if (IsConnected || !StillConnecting || deadline.IsCompleted)
                    break;
                await Task.WhenAny(next, deadline);
            }
            ct.ThrowIfCancellationRequested();
            return IsConnected;
        }
        finally
        {
            bluetooth.ConnectionStateChanged -= OnConnection;
            reconnection.ReconnectionStateChanged -= OnReconnect;
            autoConnect.StateChanged -= OnAutoConnect;
            deadlineCts.Cancel();
        }
    }

    private bool IsConnected => bluetooth.CurrentConnectionState == ConnectionState.Connected;

    private bool StillConnecting =>
        bluetooth.CurrentConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting
        || reconnection.IsReconnecting
        || autoConnect.State == AutoConnectState.Connecting;

    /// <summary>
    /// Battery (INFO) for Start and Check Battery; a fresh SESSION_STATUS for the commands
    /// that act on a running session and for Check Profile. A reading that fails stays null.
    /// </summary>
    private async Task<VoiceSnapshot> SnapshotAsync(VoiceCommand command)
    {
        GloveDeviceInfo? info = null;
        if (command is VoiceCommand.Start or VoiceCommand.CheckBattery)
        {
            try
            {
                info = await glove.GetDeviceInfoAsync(BatteryReading.Freshness);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VOICE] INFO failed: {ex.Message}");
            }
        }

        var session = glove.CurrentSessionStatus;
        if (command is VoiceCommand.CheckProfile or VoiceCommand.Pause or VoiceCommand.Resume or VoiceCommand.Stop)
        {
            try
            {
                var read = await glove.GetSessionStatusAsync();
                // UNKNOWN is no reading, not a state
                if (read.Status != SessionState.UNKNOWN)
                    session = read;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VOICE] SESSION_STATUS failed: {ex.Message}");
            }
        }

        return Snapshot(info, session);
    }

    private VoiceSnapshot Snapshot(GloveDeviceInfo? info, SessionStatus session) => new(
        IsConnected, session, glove.DeviceProfileId,
        info?.BatteryPrimaryVoltage, info?.BatteryPrimaryLow,
        info?.BatterySecondaryVoltage, info?.BatterySecondaryLow,
        info?.SecondaryLinked,
        // An Apply from the Gloves page reboots the gloves too: never send a second PROFILE_LOAD.
        IsSwitching || glove.ExpectingReboot);
}
