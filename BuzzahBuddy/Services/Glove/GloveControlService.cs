using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Service for controlling BlueBuzzah glove therapy sessions and settings.
/// Implements all 18 commands from the BlueBuzzah smartphone app specification.
/// </summary>
public class GloveControlService : IGloveControlService
{
    private readonly IBluetoothService _bluetoothService;
    private SessionStatus _currentSessionStatus = SessionStatus.CreateIdle();
    private TherapyProfile? _currentProfile;
    private bool _expectingReboot;

    /// <inheritdoc />
    public event EventHandler<SessionStatus>? SessionStateChanged;

    /// <inheritdoc />
    public SessionStatus CurrentSessionStatus => _currentSessionStatus;

    /// <inheritdoc />
    public TherapyProfile? CurrentProfile => _currentProfile;

    /// <inheritdoc />
    public bool ExpectingReboot => _expectingReboot;

    /// <inheritdoc />
    public void ClearExpectingReboot()
    {
        _expectingReboot = false;
    }

    /// <inheritdoc />
    public async Task<bool> SyncStateAsync()
    {
        try
        {
            await PingAsync();
            await GetBatteryAsync();
            await GetSessionStatusAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GLOVE_SERVICE] SyncStateAsync failed: {ex.Message}");
            return false;
        }
    }

    public GloveControlService(IBluetoothService bluetoothService)
    {
        _bluetoothService = bluetoothService;
    }

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

    public async Task<GloveDeviceInfo> GetDeviceInfoAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("INFO");
        response.ThrowIfError();
        return GloveDeviceInfo.FromCommandResponse(response);
    }

    public async Task<(double primaryVoltage, double secondaryVoltage)> GetBatteryAsync()
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
        var primaryVoltage = response.GetDouble("BATP") ?? 0.0;
        var secondaryVoltage = response.GetDouble("BATS") ?? 0.0;

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
            // Filter to main therapy profiles only (1-3: Regular, Noisy, Hybrid)
            var fallback = TherapyProfile.GetPresetProfiles()
                .Where(p => p.ProfileId >= 1 && p.ProfileId <= 3)
                .ToList();
            System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Returning {fallback.Count} fallback profiles");
            return fallback;
        }

        // Filter to main therapy profiles only (1-3: Regular, Noisy, Hybrid)
        var filtered = profiles.Where(p => p.ProfileId >= 1 && p.ProfileId <= 3).ToList();
        System.Diagnostics.Debug.WriteLine($"[PROFILE_LIST] Returning {filtered.Count} filtered profiles");
        return filtered;
    }

    public async Task LoadProfileAsync(int profileId)
    {
        EnsureConnected();
        // Per BLE protocol v2.0.0: 6 profiles available (1-6)
        if (profileId < 1 || profileId > 6)
        {
            throw new ArgumentException("Profile ID must be 1-6", nameof(profileId));
        }

        // Note: Per BLE protocol v2.0.0, PROFILE_LOAD triggers a device reboot.
        // The device will disconnect after sending the response.
        var response = await _bluetoothService.SendCommandAsync($"PROFILE_LOAD:{profileId}");

        // Check if the device is rebooting before throwing on error
        if (response.GetString("STATUS") == "REBOOTING")
        {
            _expectingReboot = true;
            System.Diagnostics.Debug.WriteLine("[GLOVE_SERVICE] Device is rebooting after profile load");
            return;
        }

        response.ThrowIfError();

        // Track the loaded profile
        _currentProfile = TherapyProfile.GetPresetProfiles().FirstOrDefault(p => p.ProfileId == profileId);
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
            PatternType = response.GetString("PATTERN")?.ToUpperInvariant() ?? "RNDP"
        };
    }

    public async Task SetCustomProfileAsync(Dictionary<string, string> parameters)
    {
        EnsureConnected();
        if (parameters == null || parameters.Count == 0)
        {
            throw new ArgumentException("Parameters dictionary cannot be null or empty", nameof(parameters));
        }

        // Build command: PROFILE_CUSTOM:KEY:VAL:KEY:VAL...
        var commandParts = new List<string> { "PROFILE_CUSTOM" };
        foreach (var kvp in parameters)
        {
            commandParts.Add(kvp.Key);
            commandParts.Add(kvp.Value);
        }

        var command = string.Join(":", commandParts);
        var response = await _bluetoothService.SendCommandAsync(command);
        response.ThrowIfError();
    }

    // ========== Session Control Commands ==========

    public async Task StartSessionAsync()
    {
        EnsureConnected();
        // SESSION_START may take up to 500ms (establishes Primary↔Secondary sync)
        var response = await _bluetoothService.SendCommandAsync("SESSION_START", timeoutMs: 7000);
        response.ThrowIfError();

        // Update session state to RUNNING
        var status = new SessionStatus
        {
            Status = SessionState.RUNNING,
            ElapsedTimeSeconds = 0,
            TotalTimeSeconds = (_currentProfile?.TimeSession ?? 120) * 60, // Convert minutes to seconds
            Progress = 0
        };
        UpdateSessionStatus(status);
    }

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
            Progress = _currentSessionStatus.Progress
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
            Progress = _currentSessionStatus.Progress
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

        // Update cached status and notify observers
        UpdateSessionStatus(status);

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
        if (fingerIndex < 0 || fingerIndex > 7)
        {
            throw new ArgumentException("Finger index must be 0-7", nameof(fingerIndex));
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
        _expectingReboot = true;
        try
        {
            await _bluetoothService.SendCommandAsync("RESTART", timeoutMs: 2000);
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

    public async Task<bool> GetTherapyLedOffAsync()
    {
        EnsureConnected();
        var response = await _bluetoothService.SendCommandAsync("THERAPY_LED_OFF");
        response.ThrowIfError();

        // Response format: THERAPY_LED_OFF:true/false
        var value = response.GetString("THERAPY_LED_OFF");
        return value?.ToLowerInvariant() == "true" || value == "1";
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
