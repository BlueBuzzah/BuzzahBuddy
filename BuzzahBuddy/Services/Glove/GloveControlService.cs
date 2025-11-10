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

    public GloveControlService(IBluetoothService bluetoothService)
    {
        _bluetoothService = bluetoothService;
    }

    // ========== Device Information Commands ==========

    public async Task<GloveDeviceInfo> GetDeviceInfoAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("INFO");
        response.ThrowIfError();
        return GloveDeviceInfo.FromCommandResponse(response);
    }

    public async Task<(double leftVoltage, double rightVoltage)> GetBatteryAsync()
    {
        // BATTERY command may take up to 1 second (VL queries VR)
        var response = await _bluetoothService.SendCommandAsync("BATTERY", timeoutMs: 3000);
        response.ThrowIfError();

        var leftVoltage = response.GetDouble("BAT_LEFT") ?? 0.0;
        var rightVoltage = response.GetDouble("BAT_RIGHT") ?? 0.0;

        return (leftVoltage, rightVoltage);
    }

    public async Task<bool> PingAsync(int timeoutMs = 2000)
    {
        try
        {
            var response = await _bluetoothService.SendCommandAsync("PING", timeoutMs: timeoutMs);
            return response.GetString("PONG") != null || response.ContainsKey("PONG");
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ========== Profile Management Commands ==========

    public async Task<List<TherapyProfile>> ListProfilesAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("PROFILE_LIST");
        response.ThrowIfError();

        // Parse PROFILE:ID:NAME lines
        var profiles = new List<TherapyProfile>();
        foreach (var key in response.Keys)
        {
            if (key == "PROFILE")
            {
                var value = response.GetString(key);
                if (value != null)
                {
                    // Format: "ID:NAME" e.g., "1:Regular VCR"
                    var parts = value.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var id))
                    {
                        var name = parts[1];
                        // Match with preset profiles
                        var preset = TherapyProfile.GetPresetProfiles()
                            .FirstOrDefault(p => p.ProfileId == id);
                        if (preset != null)
                        {
                            profiles.Add(preset);
                        }
                    }
                }
            }
        }

        // If response parsing fails, return preset profiles as fallback
        if (profiles.Count == 0)
        {
            return TherapyProfile.GetPresetProfiles();
        }

        return profiles;
    }

    public async Task LoadProfileAsync(int profileId)
    {
        if (profileId < 1 || profileId > 3)
        {
            throw new ArgumentException("Profile ID must be 1-3", nameof(profileId));
        }

        var response = await _bluetoothService.SendCommandAsync($"PROFILE_LOAD:{profileId}");
        response.ThrowIfError();
    }

    public async Task<TherapyProfile> GetCurrentProfileAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("PROFILE_GET");
        response.ThrowIfError();

        return new TherapyProfile
        {
            ActuatorType = response.GetString("ACTUATOR_TYPE") ?? "LRA",
            ActuatorFrequency = response.GetInt("ACTUATOR_FREQUENCY") ?? 250,
            ActuatorVoltage = response.GetDouble("ACTUATOR_VOLTAGE") ?? 2.5,
            TimeOn = response.GetDouble("TIME_ON") ?? 0.1,
            TimeOff = response.GetDouble("TIME_OFF") ?? 0.067,
            TimeSession = response.GetInt("TIME_SESSION") ?? 120,
            AmplitudeMin = response.GetInt("AMPLITUDE_MIN") ?? 100,
            AmplitudeMax = response.GetInt("AMPLITUDE_MAX") ?? 100,
            Jitter = response.GetDouble("JITTER") ?? 0,
            Mirror = response.GetBool("MIRROR") ?? false,
            PatternType = response.GetString("PATTERN_TYPE") ?? "RNDP"
        };
    }

    public async Task SetCustomProfileAsync(Dictionary<string, string> parameters)
    {
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
        // SESSION_START may take up to 500ms (establishes VL↔VR sync)
        var response = await _bluetoothService.SendCommandAsync("SESSION_START", timeoutMs: 7000);
        response.ThrowIfError();
    }

    public async Task PauseSessionAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("SESSION_PAUSE");
        response.ThrowIfError();
    }

    public async Task ResumeSessionAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("SESSION_RESUME");
        response.ThrowIfError();
    }

    public async Task StopSessionAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("SESSION_STOP");
        response.ThrowIfError();
    }

    public async Task<SessionStatus> GetSessionStatusAsync()
    {
        var response = await _bluetoothService.SendCommandAsync("SESSION_STATUS");
        response.ThrowIfError();
        return SessionStatus.FromCommandResponse(response);
    }

    // ========== Parameter Adjustment Commands ==========

    public async Task SetParameterAsync(string parameterName, string value)
    {
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
        var response = await _bluetoothService.SendCommandAsync("CALIBRATE_START");
        response.ThrowIfError();
    }

    public async Task BuzzFingerAsync(int fingerIndex, int intensity, int durationMs)
    {
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
        var response = await _bluetoothService.SendCommandAsync("CALIBRATE_STOP");
        response.ThrowIfError();
    }

    // ========== System Commands ==========

    public async Task<List<string>> GetAvailableCommandsAsync()
    {
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
        // RESTART command causes immediate disconnection
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
}
