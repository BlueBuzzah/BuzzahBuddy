using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Service for controlling BlueBuzzah glove therapy sessions and settings.
/// Covers the firmware's full BLE command surface — see IGloveControlService.
/// </summary>
public class GloveControlService : IGloveControlService
{
    private readonly IBluetoothService _bluetoothService;
    private SessionStatus _currentSessionStatus = SessionStatus.CreateIdle();
    private TherapyProfile? _currentProfile;
    private bool _expectingReboot;
    private DateTime _expectingRebootSetAt;

    /// <summary>
    /// How long the "expecting reboot" expectation stays valid. If the device says
    /// REBOOTING but never actually drops the link, this bounds the flag so a much
    /// later, unrelated disconnect isn't misread as an expected reboot.
    /// </summary>
    private static readonly TimeSpan ExpectingRebootTtl = TimeSpan.FromSeconds(30);

    private void SetExpectingReboot()
    {
        _expectingReboot = true;
        _expectingRebootSetAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Firmware drops the phone ~300 ms after replying STATUS:REBOOTING. Disconnecting
    /// first starts the reboot-aware reconnect (ExpectingReboot is already set) right
    /// away instead of waiting for the OS to notice the link is gone.
    /// </summary>
    private async Task DisconnectForRebootAsync()
    {
        try
        {
            await _bluetoothService.DisconnectForReconnectAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] Disconnect for reboot failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public event EventHandler<SessionStatus>? SessionStateChanged;

    /// <inheritdoc />
    public event EventHandler<int>? DeviceProfileChanged;

    /// <inheritdoc />
    public event EventHandler<GloveDeviceInfo>? DeviceInfoReceived;

    /// <inheritdoc />
    public event EventHandler<bool>? TherapyLedOffReceived;

    /// <inheritdoc />
    public SessionStatus CurrentSessionStatus => _currentSessionStatus;

    /// <inheritdoc />
    public TherapyProfile? CurrentProfile => _currentProfile;

    /// <inheritdoc />
    public int DeviceActuatorCount { get; private set; } = 4;

    /// <inheritdoc />
    public int DeviceProfileId { get; private set; }

    public bool PersistsCustomProfile { get; private set; }

    /// <inheritdoc />
    public bool ExpectingReboot =>
        _expectingReboot && (DateTime.UtcNow - _expectingRebootSetAt) < ExpectingRebootTtl;

    /// <inheritdoc />
    public void ClearExpectingReboot()
    {
        _expectingReboot = false;
    }

    public GloveControlService(IBluetoothService bluetoothService)
    {
        _bluetoothService = bluetoothService;
        _bluetoothService.ConnectionStateChanged += OnTransportConnectionStateChanged;
    }

    /// <summary>
    /// The single owner of post-(re)connect sync, so observers don't each send their
    /// own burst of commands: INFO (populates DeviceProfileId and raises
    /// DeviceProfileChanged, plus DeviceInfoReceived with both batteries — no separate
    /// BATTERY), SESSION_STATUS (raises SessionStateChanged — so a session the gloves
    /// auto-started before the app connected shows up instead of the UI looking idle),
    /// then THERAPY_LED_OFF (raises TherapyLedOffReceived). Uses the transport's own state
    /// (no race with the UI-facing connection service), with a growing backoff because
    /// the first commands after connect can be starved while the link/glove-sync settles.
    /// </summary>
    private void OnTransportConnectionStateChanged(object? sender, ConnectionState state)
    {
        if (state != ConnectionState.Connected)
        {
            // Readings from the old link say nothing about the next one
            _infoReading.Clear();
            _ledReading.Clear();
            return;
        }

        // Set before the task starts, so a page that appears on this same Connected
        // event already sees the sync as running
        Interlocked.Increment(ref _postConnectSyncsRunning);
        _ = Task.Run(async () =>
        {
            try
            {
                // Growing backoff: right after a profile-reboot reconnect the primary is
                // busy re-establishing clock sync with the secondary (~5s cold start) and
                // can starve early commands, so short retries all land in the busy window.
                int[] delaysMs = { 0, 2000, 5000, 10000 };
                // Retry only the step that failed: resending INFO for a failed
                // SESSION_STATUS cost another secondary-battery round trip and
                // re-raised its events, in the link's busiest window
                var haveInfo = false;
                var haveStatus = false;
                for (var attempt = 0; attempt < delaysMs.Length; attempt++)
                {
                    if (delaysMs[attempt] > 0)
                    {
                        await Task.Delay(delaysMs[attempt]);
                    }

                    if (_bluetoothService.CurrentConnectionState != ConnectionState.Connected)
                        return;

                    try
                    {
                        // ThisConnection: a page that fetched INFO since the link came
                        // up already raised its events; don't send it again
                        if (!haveInfo)
                        {
                            await GetDeviceInfoAsync(ThisConnection);
                            haveInfo = true;
                        }
                        // Sync session state too: the gloves auto-start a session ~30s
                        // after power-on, which can be before the app ever connects.
                        if (!haveStatus)
                        {
                            await GetSessionStatusAsync();
                            haveStatus = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[GLOVE_SERVICE] Post-connect sync failed (attempt {attempt + 1}/{delaysMs.Length}): {ex.Message}");
                        continue;
                    }

                    // Outside the retry ladder: firmware that predates THERAPY_LED_OFF
                    // rejects it every time, and observers treat no reply as "unsupported".
                    try
                    {
                        await GetTherapyLedOffAsync(ThisConnection);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[GLOVE_SERVICE] Post-connect THERAPY_LED_OFF failed: {ex.Message}");
                    }
                    return;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _postConnectSyncsRunning);
            }
        });
    }

    // Any reading the cache holds is from the current link (it is cleared on link
    // down), so "no older than the connection" is simply no age limit
    private static readonly TimeSpan ThisConnection = TimeSpan.MaxValue;

    private readonly DeviceReading<GloveDeviceInfo> _infoReading = new();
    private readonly DeviceReading<bool> _ledReading = new();

    /// <summary>
    /// One device query's result: concurrent callers share the query in flight, and a
    /// caller that accepts some staleness gets the last result. Several observers want
    /// the same INFO around every connect; without this each sent its own.
    /// </summary>
    private sealed class DeviceReading<T>
    {
        private readonly object _lock = new();
        private Task<T>? _inFlight;
        private T? _value;
        private DateTime _readAt;
        private bool _hasValue;
        private int _generation;

        public Task<T> GetAsync(Func<Task<T>> query, TimeSpan? maxAge)
        {
            lock (_lock)
            {
                if (maxAge is { } age && _hasValue && DateTime.UtcNow - _readAt <= age)
                    return Task.FromResult(_value!);
                if (_inFlight is { IsCompleted: false } running)
                    return running;
                return _inFlight = QueryAsync(query, _generation);
            }
        }

        private async Task<T> QueryAsync(Func<Task<T>> query, int generation)
        {
            var value = await query();
            lock (_lock)
            {
                // A query that outlived its link must not seed the next one's cache
                if (generation == _generation)
                {
                    _value = value;
                    _readAt = DateTime.UtcNow;
                    _hasValue = true;
                }
            }
            return value;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _generation++;
                _hasValue = false;
                _value = default;
                _inFlight = null;
            }
        }
    }

    // A count, not a flag: a reconnect can start a new sync while the previous
    // link's is still unwinding its retry ladder
    private int _postConnectSyncsRunning;

    /// <inheritdoc />
    public bool IsPostConnectSyncRunning => Volatile.Read(ref _postConnectSyncsRunning) > 0;

    /// <summary>
    /// Validates that the Bluetooth service is in a connected state before issuing a command.
    /// Provides a clear error message instead of letting the transport layer throw a generic exception.
    /// </summary>
    private void EnsureConnected()
    {
        if (_bluetoothService.CurrentConnectionState != ConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "Cannot send command: not connected to a BlueBuzzah device. " +
                "Please connect to a device first.");
        }
    }

    /// <summary>
    /// Updates the current session status and fires the SessionStateChanged event if the state changed.
    /// </summary>
    private void UpdateSessionStatus(SessionStatus newStatus)
    {
        var previousState = _currentSessionStatus.Status;
        _currentSessionStatus = newStatus;

        // Always fire the event so observers get updated progress, etc.
        SessionStateChanged?.Invoke(this, newStatus);

        if (previousState != newStatus.Status)
        {
            System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] Session state changed: {previousState} -> {newStatus.Status}");
        }
    }

    // ========== Device Information Commands ==========

    public Task<GloveDeviceInfo> GetDeviceInfoAsync(TimeSpan? maxAge = null) =>
        _infoReading.GetAsync(QueryDeviceInfoAsync, maxAge);

    private async Task<GloveDeviceInfo> QueryDeviceInfoAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("INFO");
        response.ThrowIfError();
        var info = GloveDeviceInfo.FromCommandResponse(response);
        DeviceActuatorCount = info.Motors;
        if (info.ProfileId > 0)
        {
            DeviceProfileId = info.ProfileId;
            _currentProfile = TherapyProfile.GetPresetProfiles().FirstOrDefault(p => p.ProfileId == info.ProfileId);
            DeviceProfileChanged?.Invoke(this, info.ProfileId);
        }
        DeviceInfoReceived?.Invoke(this, info);
        if (info.BatterySecondaryVoltage is null)
            WatchForSecondaryBattery();
        return info;
    }

    /// <summary>
    /// Waits before each re-ask for the secondary's battery; the last one repeats.
    /// Settable so tests don't wait out real time.
    /// </summary>
    internal TimeSpan[] SecondaryBatteryRetryDelays { get; set; } =
        [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private int _secondaryBatteryWatchRunning;

    /// <summary>
    /// The primary reports no secondary battery (BATS:0.00) until the secondary has
    /// rejoined it, which after a restart can be seconds after the phone reconnects.
    /// Re-sends INFO on a backoff so every page's DeviceInfoReceived handler picks the
    /// reading up, and stops once it arrives or the link drops. Skips turns during a
    /// session: each INFO costs a GET_BATTERY round trip on the gloves' sync link.
    /// </summary>
    // ponytail: with the secondary switched off this asks every 30s for as long as the
    // primary stays connected; INFO answers at once then, so add a cap only if that shows up.
    private void WatchForSecondaryBattery()
    {
        if (Interlocked.Exchange(ref _secondaryBatteryWatchRunning, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var delays = SecondaryBatteryRetryDelays;
                for (var attempt = 0; ; attempt++)
                {
                    await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)]);
                    if (_bluetoothService.CurrentConnectionState != ConnectionState.Connected)
                        return;
                    if (_currentSessionStatus.IsActive)
                        continue;
                    try
                    {
                        if ((await GetDeviceInfoAsync()).BatterySecondaryVoltage is not null)
                            return;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] Secondary battery re-ask failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _secondaryBatteryWatchRunning, 0);
            }
        });
    }

    public async Task<(double? primaryVoltage, double? secondaryVoltage)> GetBatteryAsync()
    {
        EnsureConnected();
        // BATTERY command may take up to 1 second (Primary queries Secondary)
        var response = await _bluetoothService.SendCommandAsync("BATTERY", timeoutMs: 3000);
        response.ThrowIfError();

        // Debug: Log all keys in response to see what device returns
        System.Diagnostics.Debug.WriteLine($"[BATTERY CMD] Response keys: {string.Join(", ", response.Keys)}");
        foreach (var key in response.Keys)
        {
            System.Diagnostics.Debug.WriteLine($"[BATTERY CMD] {key} = {response.GetString(key)}");
        }

        // Per BLE protocol v2.0.0: Battery keys are BATP and BATS
        // Missing keys and the firmware's 0.00 sentinel both mean "no reading".
        var primaryVoltage = BatteryReading.FromRaw(response.GetDouble("BATP"));
        var secondaryVoltage = BatteryReading.FromRaw(response.GetDouble("BATS"));

        System.Diagnostics.Debug.WriteLine($"[BATTERY CMD] Parsed values - BATP: {primaryVoltage}, BATS: {secondaryVoltage}");

        return (primaryVoltage, secondaryVoltage);
    }

    public async Task<bool> PingAsync(int timeoutMs = 2000)
    {
        EnsureConnected();
        try
        {
            var response = await _bluetoothService.SendCommandAsync("PING", timeoutMs: timeoutMs);

            System.Diagnostics.Debug.WriteLine($"[PING] Response keys: [{string.Join(", ", response.Keys)}]");
            System.Diagnostics.Debug.WriteLine($"[PING] HasData: {response.HasData}, ContainsKey('PONG'): {response.ContainsKey("PONG")}");

            return response.ContainsKey("PONG");
        }
        catch (TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine("[PING] Timed out");
            return false;
        }
    }

    // ========== Profile Management Commands ==========

    public async Task<List<TherapyProfile>> ListProfilesAsync()
    {
        EnsureConnected();
        System.Diagnostics.Debug.WriteLine("[PROFILE_LIST] Sending PROFILE_LIST command...");
        var response = await _bluetoothService.SendCommandAsync("PROFILE_LIST");
        response.ThrowIfError();

        // Use GetAllStrings to get all PROFILE values (device returns multiple PROFILE lines)
        var profileValues = response.GetAllStrings("PROFILE");
        System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Found {profileValues.Count} PROFILE entries");

        // Parse PROFILE values: "ID:NAME" format e.g., "1:regular_vcr"
        var profiles = new List<TherapyProfile>();
        foreach (var value in profileValues)
        {
            System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Processing: PROFILE={value}");
            // Format: "ID:NAME" e.g., "1:regular_vcr"
            var parts = value.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out var id))
            {
                var name = parts[1];
                System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Parsed profile ID={id}, Name={name}");
                // Match with preset profiles
                var preset = TherapyProfile.GetPresetProfiles()
                    .FirstOrDefault(p => p.ProfileId == id);
                if (preset != null)
                {
                    profiles.Add(preset);
                    System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Added preset profile: {preset.Name}");
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Parsed {profiles.Count} profiles from device");

        // If response parsing fails, return preset profiles as fallback
        if (profiles.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[PROFILE_LIST] No profiles parsed, using preset fallback");
            return TherapyProfile.GetPresetProfiles();
        }

        return profiles;
    }

    public async Task LoadProfileAsync(int profileId)
    {
        EnsureConnected();
        // Per BLE protocol v2.0.0: 6 profiles available (1-6)
        if (profileId < 1 || profileId > 6)
        {
            throw new ArgumentException("Profile ID must be 1-6", nameof(profileId));
        }

        // The app knows the new profile as soon as the gloves accept the command, so
        // record it here instead of waiting for the post-connect INFO sync — that sync
        // only runs if the transport surfaces the reboot's disconnect, and when it
        // doesn't, every page reading DeviceProfileId keeps naming the old profile.
        // Deliberately does NOT raise DeviceProfileChanged: subscribers treat that as
        // "the gloves are back" and use it to clear the restarting notice, which is
        // not true yet. Pages read the cached value when they next appear.
        void CacheLoadedProfile()
        {
            DeviceProfileId = profileId;
            _currentProfile = TherapyProfile.GetPresetProfiles()
                .FirstOrDefault(p => p.ProfileId == profileId);
        }

        // Note: Per BLE protocol v2.0.0, PROFILE_LOAD triggers a device reboot.
        // Set the flag BEFORE sending: the link can drop at any point after the
        // write, and ReconnectionService reads ExpectingReboot at disconnect time
        // to pick the reboot-aware loop (initial delay so both gloves finish
        // booting and pairing with each other before the phone reattaches).
        SetExpectingReboot();

        CommandResponse response;
        try
        {
            response = await _bluetoothService.SendCommandAsync($"PROFILE_LOAD:{profileId}");
        }
        catch (TimeoutException ex)
        {
            // A timeout right after PROFILE_LOAD almost always means the gloves
            // rebooted and dropped the link before (or instead of) responding —
            // the OS may not have surfaced the disconnect event yet. Trust the
            // reboot expectation rather than the not-yet-updated connection state.
            System.Diagnostics.Debug.WriteLine(
                $"[GLOVE_SERVICE] PROFILE_LOAD timed out — treating as reboot: {ex.Message}");
            CacheLoadedProfile();
            return;
        }
        catch (Exception ex)
        {
            // Non-timeout failure. The reboot can still drop the link before the
            // error surfaces, and the exception can beat the disconnect event, so
            // give the connection state a grace window to catch up.
            for (var i = 0; i < 4 && _bluetoothService.CurrentConnectionState == ConnectionState.Connected; i++)
            {
                await Task.Delay(250);
            }

            if (_bluetoothService.CurrentConnectionState != ConnectionState.Connected)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GLOVE_SERVICE] Link dropped during PROFILE_LOAD — treating as reboot: {ex.Message}");
                CacheLoadedProfile();
                return;
            }

            // Still connected after the grace window: a genuine failure, not a reboot.
            _expectingReboot = false;
            throw;
        }

        // Check if the device is rebooting before throwing on error
        if (response.GetString("STATUS") == "REBOOTING")
        {
            System.Diagnostics.Debug.WriteLine("[GLOVE_SERVICE] Device is rebooting after profile load");
            CacheLoadedProfile();
            await DisconnectForRebootAsync();
            return;
        }

        // Responded without rebooting — not a reboot after all.
        _expectingReboot = false;
        response.ThrowIfError();

        // Track the loaded profile
        CacheLoadedProfile();
        System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] Profile loaded: {_currentProfile?.Name ?? "Unknown"}");
    }

    public async Task<TherapyProfile> GetCurrentProfileAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("PROFILE_GET");
        response.ThrowIfError();

        // Per BLE protocol v2.0.0: Response uses shorthand keys
        // ON/OFF are in milliseconds in protocol, convert to seconds for app
        var onMs = response.GetDouble("ON") ?? 100.0;
        var offMs = response.GetDouble("OFF") ?? 67.0;

        // FINGERS was added to PROFILE_GET by the same firmware work that made
        // Custom-profile edits survive a restart, so its presence is an exact
        // marker for whether this device persists them.
        var fingers = response.GetInt("FINGERS");
        PersistsCustomProfile = fingers.HasValue;

        return new TherapyProfile
        {
            ActuatorType = response.GetString("TYPE") ?? "LRA",
            ActuatorFrequency = response.GetInt("FREQ") ?? 250,
            ActuatorVoltage = 2.5, // Not in protocol response, use default
            TimeOn = onMs / 1000.0, // Convert ms to seconds
            TimeOff = offMs / 1000.0, // Convert ms to seconds
            TimeSession = response.GetInt("SESSION") ?? 120,
            AmplitudeMin = response.GetInt("AMPMIN") ?? 100,
            AmplitudeMax = response.GetInt("AMPMAX") ?? 100,
            Jitter = response.GetDouble("JITTER") ?? 0,
            Mirror = response.GetBool("MIRROR") ?? false,
            PatternType = response.GetString("PATTERN")?.ToUpperInvariant() ?? "RNDP",
            // Firmware older than the FINGERS response key omits it. Fall back to
            // the device's motor count rather than a literal 4: those builds drive
            // every motor present, so on v3 hardware 4 would understate the CR
            // period. DeviceActuatorCount is itself 4 until INFO reports otherwise.
            Fingers = fingers ?? DeviceActuatorCount
        };
    }

    public async Task SetCustomProfileAsync(Dictionary<string, string> parameters)
    {
        EnsureConnected();
        if (parameters == null || parameters.Count == 0)
        {
            throw new ArgumentException("Parameters dictionary cannot be null or empty", nameof(parameters));
        }

        // Firmware parseCommand limits (menu_controller.h/.cpp): MAX_COMMAND_PARAMS=16
        // tokens (8 KEY:VAL pairs), PARAM_BUFFER_SIZE=64 per token, 256-char command
        // buffer, ':' as the token delimiter. The firmware SILENTLY drops anything
        // over these limits and still replies CUSTOM_LOADED, so reject here instead.
        const int maxPairs = 8;
        const int maxTokenLength = 63;
        const int maxCommandLength = 255;

        if (parameters.Count > maxPairs)
        {
            throw new ArgumentException(
                $"At most {maxPairs} parameters per PROFILE_CUSTOM command; the firmware silently drops extras",
                nameof(parameters));
        }

        // Build command: PROFILE_CUSTOM:KEY:VAL:KEY:VAL...
        var commandParts = new List<string> { "PROFILE_CUSTOM" };
        foreach (var kvp in parameters)
        {
            foreach (var token in new[] { kvp.Key, kvp.Value })
            {
                if (string.IsNullOrEmpty(token) || token.Length > maxTokenLength ||
                    token.Contains(':') || token.Contains('\n') || token.Contains(BlueBuzzahConstants.CommandTerminator))
                {
                    throw new ArgumentException(
                        $"Invalid parameter token '{token}': must be 1-{maxTokenLength} chars with no ':' or control characters",
                        nameof(parameters));
                }
            }
            commandParts.Add(kvp.Key);
            commandParts.Add(kvp.Value);
        }

        var command = string.Join(":", commandParts);
        if (command.Length > maxCommandLength)
        {
            throw new ArgumentException(
                $"PROFILE_CUSTOM command is {command.Length} chars; the firmware truncates past {maxCommandLength}",
                nameof(parameters));
        }
        var response = await _bluetoothService.SendCommandAsync(command);
        response.ThrowIfError();
    }

    public async Task ApplyCustomProfileAsync(TherapyProfile desired, TherapyProfile? baseline = null)
    {
        var parameters = BuildCustomProfileParameters(desired, baseline, DeviceActuatorCount);
        if (parameters.Count == 0)
            return;

        // With no baseline we can't know which amplitude bound widens the device's
        // current window, and the firmware rejects a bound that crosses the other.
        // Dropping the floor to the global minimum first is valid from any state and
        // makes the ordered pair that follows unconditionally acceptable.
        if (baseline == null)
        {
            await SetCustomProfileAsync(new Dictionary<string, string>
            {
                ["AMPMIN"] = TherapyParameterBounds.AmplitudeMin.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Firmware accepts at most 8 KEY:VAL pairs per PROFILE_CUSTOM command
        // (MAX_COMMAND_PARAMS=16 tokens). The Custom profile's eight persisted
        // parameters land exactly on that ceiling, so a full send is one command;
        // the loop remains because a future ninth parameter must not silently
        // overflow it.
        for (int i = 0; i < parameters.Count; i += 8)
        {
            await SetCustomProfileAsync(
                parameters.Skip(i).Take(8).ToDictionary(p => p.Key, p => p.Value));
        }
    }

    public async Task SendSavedCustomProfileAsync(TherapyProfile saved)
    {
        var baseline = await GetCurrentProfileAsync();
        // Fingers is a board constant, not a setting: never let a saved value change it.
        var desired = new TherapyProfile
        {
            ProfileId = TherapyProfile.CustomProfileId,
            TimeOn = saved.TimeOn,
            TimeOff = saved.TimeOff,
            TimeSession = saved.TimeSession,
            AmplitudeMin = saved.AmplitudeMin,
            AmplitudeMax = saved.AmplitudeMax,
            Jitter = saved.Jitter,
            Mirror = saved.Mirror,
            Fingers = baseline.Fingers,
        };
        await ApplyCustomProfileAsync(desired, baseline);
    }

    /// <summary>
    /// Maps a <see cref="TherapyProfile"/> to PROFILE_CUSTOM protocol parameters,
    /// validating against the firmware's setParameter ranges (profile_manager.cpp).
    /// When <paramref name="baseline"/> is provided, only parameters whose protocol
    /// value differs from the baseline are returned.
    /// </summary>
    public static List<KeyValuePair<string, string>> BuildCustomProfileParameters(
        TherapyProfile desired, TherapyProfile? baseline = null, int motorCount = 4)
    {
        // Firmware validation ranges (profile_manager.cpp setParameter). Round the
        // ms/percent floats to the 1 decimal the protocol carries ("0.#") before
        // range-checking, so a seconds value that computes to e.g. 1000.0000000002 ms
        // isn't spuriously rejected at the ceiling.
        var timeOnMs = Math.Round(desired.TimeOn * 1000.0, 1);
        var timeOffMs = Math.Round(desired.TimeOff * 1000.0, 1);
        var jitter = Math.Round(desired.Jitter, 1);
        if (!TherapyParameterBounds.IsTimeOnValid(timeOnMs))
            throw new ArgumentException(
                $"Burst duration must be {TherapyParameterBounds.TimeOnMsMin}-{TherapyParameterBounds.TimeOnMsMax} ms", nameof(desired));
        if (!TherapyParameterBounds.IsTimeOffValid(timeOffMs))
            throw new ArgumentException(
                $"Gap must be {TherapyParameterBounds.TimeOffMsMin}-{TherapyParameterBounds.TimeOffMsMax} ms", nameof(desired));
        if (!TherapyParameterBounds.IsSessionValid(desired.TimeSession))
            throw new ArgumentException(
                $"Session duration must be {TherapyParameterBounds.SessionMinutesMin}-{TherapyParameterBounds.SessionMinutesMax} minutes", nameof(desired));
        if (!TherapyParameterBounds.IsAmplitudeValid(desired.AmplitudeMin) ||
            !TherapyParameterBounds.IsAmplitudeValid(desired.AmplitudeMax))
            throw new ArgumentException(
                $"Amplitude must be {TherapyParameterBounds.AmplitudeMin}-{TherapyParameterBounds.AmplitudeMax}%", nameof(desired));
        if (desired.AmplitudeMin > desired.AmplitudeMax)
            throw new ArgumentException("Minimum amplitude cannot exceed maximum amplitude", nameof(desired));
        if (!TherapyParameterBounds.IsJitterValid(jitter))
            throw new ArgumentException(
                $"Jitter must be {TherapyParameterBounds.JitterMin}-{TherapyParameterBounds.JitterMax}%", nameof(desired));
        if (!TherapyParameterBounds.IsFingersValid(desired.Fingers, motorCount))
            throw new ArgumentException(
                $"Active fingers must be {TherapyParameterBounds.FingersMin}-{motorCount} on this device", nameof(desired));

        var desiredParams = ToProtocolParameters(desired, baseline);
        if (baseline == null)
        {
            return desiredParams;
        }

        // Order is irrelevant here — this is only a key→value lookup for the diff.
        var baselineParams = ToProtocolParameters(baseline, null).ToDictionary(p => p.Key, p => p.Value);
        return desiredParams
            .Where(p => !baselineParams.TryGetValue(p.Key, out var old) || old != p.Value)
            .ToList();
    }

    /// <summary>
    /// The eight parameters the firmware persists for the Custom profile
    /// (<c>CustomOverrideData</c>). TYPE, FREQ and PATTERN are deliberately absent:
    /// the firmware rejects them on the Custom profile because it has nowhere to
    /// store them, so sending them would fail the whole batch.
    /// </summary>
    /// <remarks>
    /// Ordered list (not Dictionary) so chunk membership across the 8-pair
    /// PROFILE_CUSTOM boundary is deterministic by contract, not by the CLR's
    /// incidental dictionary insertion-order behavior.
    /// </remarks>
    private static List<KeyValuePair<string, string>> ToProtocolParameters(
        TherapyProfile profile, TherapyProfile? baseline)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("ON", (profile.TimeOn * 1000.0).ToString("0.#", inv)),   // protocol uses ms
            new("OFF", (profile.TimeOff * 1000.0).ToString("0.#", inv)), // protocol uses ms
            new("SESSION", profile.TimeSession.ToString(inv)),
            new("MIRROR", profile.Mirror ? "1" : "0"),
            new("JITTER", profile.Jitter.ToString("0.#", inv)),
            new("FINGERS", profile.Fingers.ToString(inv)),
        };
        parameters.AddRange(AmplitudeParametersWideningFirst(profile, baseline));
        return parameters;
    }

    /// <summary>
    /// The firmware cross-checks each amplitude bound against the one currently on
    /// the device, and PROFILE_CUSTOM applies keys in the order sent. Writing the
    /// narrowing key first can therefore be rejected even though the target pair is
    /// perfectly valid (e.g. 30/70 → 80/100 fails if AMPMIN goes first). Emitting
    /// the key that widens the window first is always accepted.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> AmplitudeParametersWideningFirst(
        TherapyProfile profile, TherapyProfile? baseline)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var min = new KeyValuePair<string, string>("AMPMIN", profile.AmplitudeMin.ToString(inv));
        var max = new KeyValuePair<string, string>("AMPMAX", profile.AmplitudeMax.ToString(inv));

        // Without a baseline the device's current window is unknown, so neither
        // order is safe on its own; ApplyCustomProfileAsync drops the floor to the
        // global minimum first, which makes raising the ceiling always valid.
        if (baseline == null || profile.AmplitudeMax > baseline.AmplitudeMax)
        {
            yield return max;
            yield return min;
        }
        else
        {
            yield return min;
            yield return max;
        }
    }

    // ========== Session Control Commands ==========

    /// <summary>
    /// Pause between SESSION_START attempts while the gloves report "Sync not ready".
    /// Settable so tests don't wait out real time.
    /// </summary>
    internal TimeSpan SyncNotReadyRetryInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long SESSION_START keeps retrying "Sync not ready" before surfacing it.
    /// Outlasts the firmware's 10 s fallback (SYNC_START_FALLBACK_MS), after which
    /// the primary accepts the start with degraded timing, so a link that never
    /// converges still starts instead of showing an error.
    /// </summary>
    internal TimeSpan SyncNotReadyRetryWindow { get; set; } = TimeSpan.FromSeconds(12);

    public async Task StartSessionAsync()
    {
        EnsureConnected();
        // SESSION_START may take up to 500ms (establishes Primary↔Secondary sync).
        // The primary refuses with "Sync not ready" until bilateral clock sync has
        // converged (typically < 1 s after the gloves connect); that refusal is
        // transient, so retry it rather than show the user an error.
        var retryWindow = System.Diagnostics.Stopwatch.StartNew();
        CommandResponse response;
        while (true)
        {
            response = await _bluetoothService.SendCommandAsync("SESSION_START", timeoutMs: 7000);
            if (!IsSyncNotReady(response) ||
                retryWindow.Elapsed + SyncNotReadyRetryInterval > SyncNotReadyRetryWindow)
            {
                break;
            }

            System.Diagnostics.Debug.WriteLine("[GLOVE_SERVICE] SESSION_START: sync not ready, retrying");
            await Task.Delay(SyncNotReadyRetryInterval);
            EnsureConnected();
        }

        // The secondary dropped since the last INFO. Refresh it so the Session banner shows
        // with the alert, and the missing secondary battery keeps INFO re-asked until it's back.
        if (response.ErrorMessage?.Contains("secondary not connected", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                await GetDeviceInfoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] INFO after refused start failed: {ex.Message}");
            }
        }
        response.ThrowIfError();

        // Firmware that reports the real duration (correct for a Custom profile)
        // includes TOTAL/ELAPSED; older firmware doesn't, so fall back to the preset.
        var status = new SessionStatus
        {
            Status = SessionState.RUNNING,
            ElapsedTimeSeconds = response.GetInt("ELAPSED") ?? 0,
            TotalTimeSeconds = response.GetInt("TOTAL")
                ?? (_currentProfile?.TimeSession ?? 120) * 60, // Convert minutes to seconds
            Progress = 0,
            SessionId = response.GetInt("SESSION_ID")
        };
        UpdateSessionStatus(status);
    }

    private static bool IsSyncNotReady(CommandResponse response) =>
        response.ErrorMessage?.Contains("sync not ready", StringComparison.OrdinalIgnoreCase) == true;

    public async Task PauseSessionAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("SESSION_PAUSE");
        response.ThrowIfError();

        // Update session state to PAUSED (preserve current progress)
        var status = new SessionStatus
        {
            Status = SessionState.PAUSED,
            ElapsedTimeSeconds = _currentSessionStatus.ElapsedTimeSeconds,
            TotalTimeSeconds = _currentSessionStatus.TotalTimeSeconds,
            Progress = _currentSessionStatus.Progress,
            SessionId = _currentSessionStatus.SessionId,
            PausedSeconds = _currentSessionStatus.PausedSeconds
        };
        UpdateSessionStatus(status);
    }

    public async Task ResumeSessionAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("SESSION_RESUME");
        response.ThrowIfError();

        // Update session state to RUNNING (preserve current progress)
        var status = new SessionStatus
        {
            Status = SessionState.RUNNING,
            ElapsedTimeSeconds = _currentSessionStatus.ElapsedTimeSeconds,
            TotalTimeSeconds = _currentSessionStatus.TotalTimeSeconds,
            Progress = _currentSessionStatus.Progress,
            SessionId = _currentSessionStatus.SessionId,
            PausedSeconds = _currentSessionStatus.PausedSeconds
        };
        UpdateSessionStatus(status);
    }

    public async Task StopSessionAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("SESSION_STOP");
        response.ThrowIfError();

        // Update session state to IDLE
        UpdateSessionStatus(SessionStatus.CreateIdle());
    }

    public async Task<SessionStatus> GetSessionStatusAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("SESSION_STATUS");
        response.ThrowIfError();
        var status = SessionStatus.FromCommandResponse(response);

        // UNKNOWN means the reply carried no readable SESSION_STATUS, so it is the
        // absence of a reading rather than a state. Neither cache nor publish it:
        // the cache feeds Pause/Resume's elapsed time, and observers assign whatever
        // this event carries straight onto the session UI, which would end the
        // session on screen while the gloves keep running. The caller still gets it
        // back and counts it as a failed poll.
        if (status.Status != SessionState.UNKNOWN)
        {
            // Update cached status and notify observers
            UpdateSessionStatus(status);
        }

        return status;
    }

    // ========== Parameter Adjustment Commands ==========

    public async Task SetParameterAsync(string parameterName, string value)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("Parameter name cannot be null or empty", nameof(parameterName));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or empty", nameof(value));
        }

        var response = await _bluetoothService.SendCommandAsync($"PARAM_SET:{parameterName}:{value}");
        response.ThrowIfError();
    }

    // ========== Calibration Commands ==========

    public async Task EnterCalibrationAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("CALIBRATE_START");
        response.ThrowIfError();
    }

    public async Task BuzzFingerAsync(int fingerIndex, int intensity, int durationMs)
    {
        EnsureConnected();
        var maxFinger = DeviceActuatorCount * 2 - 1;
        if (fingerIndex < 0 || fingerIndex > maxFinger)
        {
            throw new ArgumentException($"Finger index must be 0-{maxFinger}", nameof(fingerIndex));
        }

        if (intensity < 0 || intensity > 100)
        {
            throw new ArgumentException("Intensity must be 0-100", nameof(intensity));
        }

        if (durationMs < 50 || durationMs > 2000)
        {
            throw new ArgumentException("Duration must be 50-2000ms", nameof(durationMs));
        }

        var response = await _bluetoothService.SendCommandAsync(
            $"CALIBRATE_BUZZ:{fingerIndex}:{intensity}:{durationMs}");
        response.ThrowIfError();
    }

    public async Task ExitCalibrationAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("CALIBRATE_STOP");
        response.ThrowIfError();
    }

    // ========== System Commands ==========

    public async Task<List<string>> GetAvailableCommandsAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("HELP");
        response.ThrowIfError();

        var commands = new List<string>();
        foreach (var key in response.Keys)
        {
            if (key == "COMMAND")
            {
                var cmdName = response.GetString(key);
                if (cmdName != null)
                {
                    commands.Add(cmdName);
                }
            }
        }

        return commands;
    }

    public async Task RestartDeviceAsync()
    {
        EnsureConnected();
        // RESTART command causes immediate disconnection
        SetExpectingReboot();
        try
        {
            var response = await _bluetoothService.SendCommandAsync("RESTART", timeoutMs: 2000);
            if (response.GetString("STATUS") == "REBOOTING")
            {
                await DisconnectForRebootAsync();
            }
        }
        catch (TimeoutException)
        {
            // Expected - device reboots before sending response
        }
        catch (InvalidOperationException)
        {
            // Expected - connection drops during reboot
        }
    }

    // ========== LED and Debug Commands (per BLE protocol v2.0.0) ==========

    public Task<bool> GetTherapyLedOffAsync(TimeSpan? maxAge = null) =>
        _ledReading.GetAsync(QueryTherapyLedOffAsync, maxAge);

    private async Task<bool> QueryTherapyLedOffAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("THERAPY_LED_OFF");
        response.ThrowIfError();

        // Response format: THERAPY_LED_OFF:true/false
        var value = response.GetString("THERAPY_LED_OFF");
        var ledOff = value?.ToLowerInvariant() == "true" || value == "1";
        TherapyLedOffReceived?.Invoke(this, ledOff);
        return ledOff;
    }

    public async Task SetTherapyLedOffAsync(bool enabled)
    {
        EnsureConnected();
        var value = enabled ? "true" : "false";
        var response = await _bluetoothService.SendCommandAsync($"THERAPY_LED_OFF:{value}");
        response.ThrowIfError();
    }

    public async Task<bool> GetDebugModeAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("DEBUG");
        response.ThrowIfError();

        // Response format: DEBUG:true/false
        var value = response.GetString("DEBUG");
        return value?.ToLowerInvariant() == "true" || value == "1";
    }

    public async Task SetDebugModeAsync(bool enabled)
    {
        EnsureConnected();
        var value = enabled ? "true" : "false";
        var response = await _bluetoothService.SendCommandAsync($"DEBUG:{value}");
        response.ThrowIfError();
    }
}
