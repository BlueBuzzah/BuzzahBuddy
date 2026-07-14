using BuzzahBuddy.Models;
using System.Text;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Mock Bluetooth service for testing without hardware.
/// Implements all 20 BlueBuzzah commands with realistic responses.
/// Per BLE protocol v2.0.0.
/// This mock simulates a 4-motor BlueBuzzah primary board; 5-motor (PentaBuzzer) ranges
/// are only testable on hardware.
/// </summary>
public class MockBluetoothService : IBluetoothService
{
    private bool _isConnected;
    private GloveDevice? _connectedDevice;
    private string? _lastConnectedDeviceId;
    private bool _userInitiatedDisconnect;
    private SessionState _mockSessionState = SessionState.IDLE;
    private DateTime? _mockSessionStartTime;
    private DateTime? _mockSessionPauseTime;
    private TimeSpan _mockElapsedTime = TimeSpan.Zero;
    private int _mockCurrentProfile = 2; // Default: Noisy VCR
    private bool _isInCalibrationMode;
    private bool _therapyLedOff;
    private bool _debugMode;

    public ConnectionState CurrentConnectionState { get; private set; } = ConnectionState.Disconnected;
    public GloveDevice? ConnectedDevice => _connectedDevice;
    public string? LastConnectedDeviceId => _lastConnectedDeviceId;
    public bool UserInitiatedDisconnect => _userInitiatedDisconnect;

    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;
    public event EventHandler<CommandResponse>? ResponseReceived;

    public Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // Simulate discovery of BlueBuzzah device
        // Per BLE protocol v2.0.0: Device is named "BlueBuzzah"
        var mockDevice = new GloveDevice
        {
            Id = "MOCK-PRIMARY-001",
            Name = BlueBuzzahConstants.DeviceName, // "BlueBuzzah"
            SignalStrength = -45,
            ConnectionState = ConnectionState.Disconnected,
            FirmwareVersion = "2.0.0"
        };

        // Raise discovery event after short delay
        Task.Delay(500, cancellationToken).ContinueWith(_ =>
        {
            DeviceDiscovered?.Invoke(this, mockDevice);
        }, cancellationToken);

        return Task.FromResult<IEnumerable<GloveDevice>>(new[] { mockDevice });
    }

    public Task StopScanAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectToDeviceAsync(
        GloveDevice device,
        CancellationToken cancellationToken = default)
    {
        CurrentConnectionState = ConnectionState.Connecting;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Connecting);

        await Task.Delay(300, cancellationToken); // Simulate connection time

        _isConnected = true;
        _connectedDevice = device;
        _connectedDevice.ConnectionState = ConnectionState.Connected;
        CurrentConnectionState = ConnectionState.Connected;
        _lastConnectedDeviceId = device.Id;
        _userInitiatedDisconnect = false;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Connected);

        return true;
    }

    public Task DisconnectAsync()
    {
        _userInitiatedDisconnect = true;
        _isConnected = false;
        if (_connectedDevice != null)
        {
            _connectedDevice.ConnectionState = ConnectionState.Disconnected;
        }
        _connectedDevice = null;
        CurrentConnectionState = ConnectionState.Disconnected;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Disconnected);

        // Reset mock state
        _mockSessionState = SessionState.IDLE;
        _mockSessionStartTime = null;
        _mockSessionPauseTime = null;
        _mockElapsedTime = TimeSpan.Zero;
        _isInCalibrationMode = false;

        return Task.CompletedTask;
    }

    public async Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_lastConnectedDeviceId))
            return false;

        CurrentConnectionState = ConnectionState.Reconnecting;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Reconnecting);

        await Task.Delay(300, ct);

        var mockDevice = new GloveDevice
        {
            Id = _lastConnectedDeviceId,
            Name = BlueBuzzahConstants.DeviceName,
            SignalStrength = -45,
            ConnectionState = ConnectionState.Connected,
            LastConnected = DateTime.Now
        };

        _isConnected = true;
        _connectedDevice = mockDevice;
        _userInitiatedDisconnect = false;
        CurrentConnectionState = ConnectionState.Connected;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Connected);

        return true;
    }

    public Task DisconnectForReconnectAsync()
    {
        _isConnected = false;
        if (_connectedDevice != null)
        {
            _connectedDevice.ConnectionState = ConnectionState.Disconnected;
        }
        _connectedDevice = null;
        CurrentConnectionState = ConnectionState.Disconnected;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Disconnected);

        return Task.CompletedTask;
    }

    public async Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default)
    {
        var devices = await ScanForDevicesAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
        var deviceList = devices.ToList();
        return deviceList.Count > 0
            ? new ScanResult(ScanOutcome.DevicesFound, deviceList)
            : new ScanResult(ScanOutcome.NoDevicesFound, deviceList);
    }

    public async Task<CommandResponse> SendCommandAsync(
        string command,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected to device");
        }

        // Simulate command processing delay
        await Task.Delay(50, cancellationToken);

        var responseText = await GetRawResponseAsync(command, cancellationToken);

        var response = CommandResponse.Parse(responseText);
        ResponseReceived?.Invoke(this, response);
        return response;
    }

    /// <summary>
    /// Builds the raw, pre-parse wire response text for a command, exactly as the mock
    /// would send it over BLE (KEY:VALUE lines terminated with \x04). Exposed separately
    /// from <see cref="SendCommandAsync"/> so tests can assert on the literal wire text
    /// (e.g. the presence of a trailing colon) without CommandResponse.Parse normalizing
    /// away the distinction they care about.
    /// </summary>
    internal async Task<string> GetRawResponseAsync(string command, CancellationToken cancellationToken = default)
    {
        // Per BLE protocol v2.0.0: Handle all 20 commands
        return command.ToUpperInvariant() switch
        {
            "INFO" => GetMockInfoResponse(),
            "BATTERY" => await GetMockBatteryResponse(cancellationToken),
            "PING" => "PONG:\n\x04",
            "PROFILE_LIST" => GetMockProfileListResponse(),
            var cmd when cmd.StartsWith("PROFILE_LOAD:") => HandleProfileLoad(cmd),
            "PROFILE_GET" => GetMockProfileSettings(),
            var cmd when cmd.StartsWith("PROFILE_CUSTOM:") => "STATUS:CUSTOM_LOADED\n\x04",
            "SESSION_START" => await HandleSessionStart(cancellationToken),
            "SESSION_PAUSE" => HandleSessionPause(),
            "SESSION_RESUME" => HandleSessionResume(),
            "SESSION_STOP" => HandleSessionStop(),
            "SESSION_STATUS" => GetMockSessionStatus(),
            var cmd when cmd.StartsWith("PARAM_SET:") => HandleParamSet(cmd),
            "CALIBRATE_START" => HandleCalibrationStart(),
            var cmd when cmd.StartsWith("CALIBRATE_BUZZ:") => HandleCalibrationBuzz(cmd),
            "CALIBRATE_STOP" => HandleCalibrationStop(),
            "HELP" => GetMockHelpResponse(),
            "RESTART" => "STATUS:REBOOTING\n\x04",
            // New commands per BLE protocol v2.0.0
            "THERAPY_LED_OFF" => $"THERAPY_LED_OFF:{(_therapyLedOff ? "true" : "false")}\n\x04",
            var cmd when cmd.StartsWith("THERAPY_LED_OFF:") => HandleTherapyLedOff(cmd),
            "DEBUG" => $"DEBUG:{(_debugMode ? "true" : "false")}\n\x04",
            var cmd when cmd.StartsWith("DEBUG:") => HandleDebug(cmd),
            _ => $"ERROR:Unknown command: {command}\n\x04"
        };
    }

    public Task SubscribeToNotificationsAsync()
    {
        return Task.CompletedTask;
    }

    public Task UnsubscribeFromNotificationsAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> IsBluetoothEnabledAsync()
    {
        return Task.FromResult(true);
    }

    // ========== Mock Response Builders ==========

    private string GetMockInfoResponse()
    {
        // Per BLE protocol v2.0.0: Use BATP/BATS for battery keys
        return "ROLE:PRIMARY\n" +
               "NAME:BlueBuzzah\n" +
               "FW:2.0.0\n" +
               "MOTORS:4\n" +
               $"PROFILE:{_mockCurrentProfile}:{ProfileNameFor(_mockCurrentProfile)}\n" +
               "BATP:3.72\n" +
               "BATS:3.68\n" +
               $"STATUS:{_mockSessionState}\n" +
               "\x04";
    }

    private static string ProfileNameFor(int profileId) => profileId switch
    {
        1 => "regular_vcr",
        2 => "noisy_vcr",
        3 => "hybrid_vcr",
        4 => "custom_vcr",
        5 => "gentle",
        6 => "quick_test",
        _ => "unknown"
    };

    private async Task<string> GetMockBatteryResponse(CancellationToken cancellationToken)
    {
        // Simulate PRIMARY querying SECONDARY (takes ~1 second)
        await Task.Delay(800, cancellationToken);

        // Per BLE protocol v2.0.0: Use BATP/BATS for battery keys
        return "BATP:3.72\n" +
               "BATS:3.68\n" +
               "\x04";
    }

    private string GetMockProfileListResponse()
    {
        // Per BLE protocol v2.0.0: 6 profiles available
        return "PROFILE:1:regular_vcr\n" +
               "PROFILE:2:noisy_vcr\n" +
               "PROFILE:3:hybrid_vcr\n" +
               "PROFILE:4:custom_vcr\n" +
               "PROFILE:5:gentle\n" +
               "PROFILE:6:quick_test\n" +
               "\x04";
    }

    private string HandleProfileLoad(string command)
    {
        // Firmware rejects PROFILE_LOAD only in active states (mirrors isActiveState); must be stopped first.
        if (_mockSessionState is SessionState.RUNNING or SessionState.PAUSED or SessionState.LOW_BATTERY)
        {
            return "ERROR:Session must be stopped before loading a profile\n\x04";
        }

        var parts = command.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var profileId))
        {
            // Per BLE protocol v2.0.0: 6 profiles available (1-6)
            if (profileId >= 1 && profileId <= 6)
            {
                _mockCurrentProfile = profileId;
                var profileName = ProfileNameFor(profileId);
                // Per BLE protocol v2.0.0: Device reboots after PROFILE_LOAD
                return $"STATUS:REBOOTING\nPROFILE:{profileName}\n\x04";
            }
        }
        return "ERROR:Invalid profile ID\n\x04";
    }

    private string GetMockProfileSettings()
    {
        // Per BLE protocol v2.0.0: Use shorthand keys
        // ON/OFF are in milliseconds
        return "TYPE:LRA\n" +
               "FREQ:250\n" +
               "ON:100.0\n" +
               "OFF:67.0\n" +
               "SESSION:120\n" +
               "AMPMIN:100\n" +
               "AMPMAX:100\n" +
               "PATTERN:rndp\n" +
               "MIRROR:1\n" +
               "JITTER:23.5\n" +
               "\x04";
    }

    private string HandleParamSet(string command)
    {
        // Per BLE protocol v2.0.0: PARAM_SET:NAME:VALUE
        var parts = command.Split(':');
        if (parts.Length >= 3)
        {
            return $"PARAM:{parts[1]}\nVALUE:{parts[2]}\n\x04";
        }
        return "ERROR:Invalid parameter format\n\x04";
    }

    private async Task<string> HandleSessionStart(CancellationToken cancellationToken)
    {
        if (_mockSessionState != SessionState.IDLE)
        {
            return "ERROR:Session already active\n\x04";
        }

        // Simulate VL↔VR synchronization (takes ~500ms)
        await Task.Delay(400, cancellationToken);

        _mockSessionState = SessionState.RUNNING;
        _mockSessionStartTime = DateTime.Now;
        _mockElapsedTime = TimeSpan.Zero;

        return "SESSION_STATUS:RUNNING\n\x04";
    }

    private string HandleSessionPause()
    {
        if (_mockSessionState != SessionState.RUNNING)
        {
            return "ERROR:No active session\n\x04";
        }

        _mockSessionState = SessionState.PAUSED;
        _mockSessionPauseTime = DateTime.Now;

        // Capture elapsed time at pause
        if (_mockSessionStartTime.HasValue)
        {
            _mockElapsedTime = DateTime.Now - _mockSessionStartTime.Value;
        }

        return "SESSION_STATUS:PAUSED\n\x04";
    }

    private string HandleSessionResume()
    {
        if (_mockSessionState != SessionState.PAUSED)
        {
            return "ERROR:No paused session\n\x04";
        }

        _mockSessionState = SessionState.RUNNING;
        _mockSessionStartTime = DateTime.Now - _mockElapsedTime; // Adjust start time

        return "SESSION_STATUS:RUNNING\n\x04";
    }

    private string HandleSessionStop()
    {
        _mockSessionState = SessionState.IDLE;
        _mockSessionStartTime = null;
        _mockSessionPauseTime = null;
        _mockElapsedTime = TimeSpan.Zero;

        return "SESSION_STATUS:IDLE\n\x04";
    }

    /// <summary>
    /// Test-only seam: shifts the mock session's start time backwards so tests can exercise
    /// elapsed-time-dependent behavior (e.g. the LOW_BATTERY transition at ~95% progress)
    /// without waiting ~114 real minutes for a 2-hour session. Internal because the mock
    /// compiles directly into the test assembly.
    /// </summary>
    internal void AdvanceMockSession(TimeSpan elapsed)
    {
        if (_mockSessionStartTime.HasValue)
        {
            _mockSessionStartTime = _mockSessionStartTime.Value - elapsed;
        }
    }

    private string GetMockSessionStatus()
    {
        // Per BLE protocol v2.0.0: Use ELAPSED/TOTAL (not ELAPSED_TIME/TOTAL_TIME)
        if (_mockSessionState == SessionState.IDLE)
        {
            return "SESSION_STATUS:IDLE\n" +
                   "ELAPSED:0\n" +
                   "TOTAL:0\n" +
                   "PROGRESS:0\n" +
                   "\x04";
        }

        var totalSeconds = 120 * 60; // 2 hours default
        int elapsedSeconds;

        if (_mockSessionState == SessionState.RUNNING && _mockSessionStartTime.HasValue)
        {
            elapsedSeconds = (int)(DateTime.Now - _mockSessionStartTime.Value).TotalSeconds;
        }
        else
        {
            elapsedSeconds = (int)_mockElapsedTime.TotalSeconds;
        }

        var progress = Math.Min(100, (int)((double)elapsedSeconds / totalSeconds * 100));

        var stateStr = _mockSessionState.ToString();
        if (_mockSessionState == SessionState.RUNNING && progress >= 95)
        {
            stateStr = "LOW_BATTERY";   // exercise the app's non-trivial state handling in Debug builds
        }

        return $"SESSION_STATUS:{stateStr}\n" +
               $"ELAPSED:{elapsedSeconds}\n" +
               $"TOTAL:{totalSeconds}\n" +
               $"PROGRESS:{progress}\n" +
               "\x04";
    }

    private string HandleCalibrationStart()
    {
        _isInCalibrationMode = true;
        return "MODE:CALIBRATION\n\x04";
    }

    private string HandleCalibrationBuzz(string command)
    {
        if (!_isInCalibrationMode)
        {
            return "ERROR:Not in calibration mode\n\x04";
        }

        var parts = command.Split(':');
        if (parts.Length >= 4 &&
            int.TryParse(parts[1], out var finger) &&
            int.TryParse(parts[2], out var intensity) &&
            int.TryParse(parts[3], out var duration))
        {
            // Mock simulates a 4-motor BlueBuzzah primary; firmware's finger range is 0-7 for it.
            if (finger < 0 || finger > 7) return "ERROR:Invalid finger index (0-7)\n\x04";
            if (intensity < 0 || intensity > 100) return "ERROR:Intensity out of range (0-100)\n\x04";
            if (duration < 50 || duration > 2000) return "ERROR:Duration out of range (50-2000ms)\n\x04";
            return $"FINGER:{finger}\n" +
                   $"INTENSITY:{intensity}\n" +
                   $"DURATION:{duration}\n" +
                   "\x04";
        }

        return "ERROR:Invalid parameters\n\x04";
    }

    private string HandleCalibrationStop()
    {
        _isInCalibrationMode = false;
        return "MODE:NORMAL\n\x04";
    }

    private string GetMockHelpResponse()
    {
        // Per BLE protocol v2.0.0: 20 commands
        return "COMMAND:INFO\n" +
               "COMMAND:BATTERY\n" +
               "COMMAND:PING\n" +
               "COMMAND:PROFILE_LIST\n" +
               "COMMAND:PROFILE_LOAD\n" +
               "COMMAND:PROFILE_GET\n" +
               "COMMAND:PROFILE_CUSTOM\n" +
               "COMMAND:SESSION_START\n" +
               "COMMAND:SESSION_PAUSE\n" +
               "COMMAND:SESSION_RESUME\n" +
               "COMMAND:SESSION_STOP\n" +
               "COMMAND:SESSION_STATUS\n" +
               "COMMAND:PARAM_SET\n" +
               "COMMAND:CALIBRATE_START\n" +
               "COMMAND:CALIBRATE_BUZZ\n" +
               "COMMAND:CALIBRATE_STOP\n" +
               "COMMAND:HELP\n" +
               "COMMAND:RESTART\n" +
               "COMMAND:THERAPY_LED_OFF\n" +
               "COMMAND:DEBUG\n" +
               "\x04";
    }

    private string HandleTherapyLedOff(string command)
    {
        // Per BLE protocol v2.0.0: THERAPY_LED_OFF:true/false
        var parts = command.Split(':');
        if (parts.Length >= 2)
        {
            var value = parts[1].ToLowerInvariant();
            _therapyLedOff = value == "true" || value == "1";
            return $"THERAPY_LED_OFF:{(_therapyLedOff ? "true" : "false")}\n\x04";
        }
        return "ERROR:Invalid value. Use: true/false or 1/0\n\x04";
    }

    private string HandleDebug(string command)
    {
        // Per BLE protocol v2.0.0: DEBUG:true/false
        var parts = command.Split(':');
        if (parts.Length >= 2)
        {
            var value = parts[1].ToLowerInvariant();
            _debugMode = value == "true" || value == "1";
            return $"DEBUG:{(_debugMode ? "true" : "false")}\n\x04";
        }
        return "ERROR:Invalid value. Use: true/false or 1/0\n\x04";
    }
}
