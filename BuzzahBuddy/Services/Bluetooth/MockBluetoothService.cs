using BuzzahBuddy.Models;
using System.Text;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Mock Bluetooth service for testing without hardware.
/// Implements all 18 BlueBuzzah commands with realistic responses.
/// </summary>
public class MockBluetoothService : IBluetoothService
{
    private bool _isConnected;
    private GloveDevice? _connectedDevice;
    private SessionState _mockSessionState = SessionState.IDLE;
    private DateTime? _mockSessionStartTime;
    private DateTime? _mockSessionPauseTime;
    private TimeSpan _mockElapsedTime = TimeSpan.Zero;
    private int _mockCurrentProfile = 2; // Default: Noisy VCR
    private bool _isInCalibrationMode;

    public ConnectionState CurrentConnectionState { get; private set; } = ConnectionState.Disconnected;
    public GloveDevice? ConnectedDevice => _connectedDevice;

    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;
    public event EventHandler<CommandResponse>? ResponseReceived;

    public Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // Simulate discovery of VL device
        var mockDevice = new GloveDevice
        {
            Id = "MOCK-VL-001",
            Name = "VL",
            SignalStrength = -45,
            ConnectionState = ConnectionState.Disconnected,
            BatteryLeftVoltage = 3.72,
            BatteryRightVoltage = 3.68,
            FirmwareVersion = "1.0.0"
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
        await Task.Delay(300, cancellationToken); // Simulate connection time

        _isConnected = true;
        _connectedDevice = device;
        _connectedDevice.ConnectionState = ConnectionState.Connected;
        CurrentConnectionState = ConnectionState.Connected;
        ConnectionStateChanged?.Invoke(this, ConnectionState.Connected);

        return true;
    }

    public Task DisconnectAsync()
    {
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

        var responseText = command.ToUpperInvariant() switch
        {
            "INFO" => GetMockInfoResponse(),
            "BATTERY" => await GetMockBatteryResponse(cancellationToken),
            "PING" => "PONG\n\x04",
            "PROFILE_LIST" => "PROFILE:1:Regular VCR\nPROFILE:2:Noisy VCR\nPROFILE:3:Hybrid VCR\n\x04",
            var cmd when cmd.StartsWith("PROFILE_LOAD:") => HandleProfileLoad(cmd),
            "PROFILE_GET" => GetMockProfileSettings(),
            var cmd when cmd.StartsWith("PROFILE_CUSTOM:") => "STATUS:CUSTOM_LOADED\n\x04",
            "SESSION_START" => await HandleSessionStart(cancellationToken),
            "SESSION_PAUSE" => HandleSessionPause(),
            "SESSION_RESUME" => HandleSessionResume(),
            "SESSION_STOP" => HandleSessionStop(),
            "SESSION_STATUS" => GetMockSessionStatus(),
            var cmd when cmd.StartsWith("PARAM_SET:") => "STATUS:UPDATED\n\x04",
            "CALIBRATE_START" => HandleCalibrationStart(),
            var cmd when cmd.StartsWith("CALIBRATE_BUZZ:") => HandleCalibrationBuzz(cmd),
            "CALIBRATE_STOP" => HandleCalibrationStop(),
            "HELP" => GetMockHelpResponse(),
            "RESTART" => "STATUS:REBOOTING\n\x04",
            _ => $"ERROR:Unknown command\n\x04"
        };

        var response = CommandResponse.Parse(responseText);
        ResponseReceived?.Invoke(this, response);
        return response;
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
        return "ROLE:PRIMARY\n" +
               "NAME:VL\n" +
               "FW:1.0.0\n" +
               "BAT_LEFT:3.72\n" +
               "BAT_RIGHT:3.68\n" +
               $"STATUS:{_mockSessionState}\n" +
               "\x04";
    }

    private async Task<string> GetMockBatteryResponse(CancellationToken cancellationToken)
    {
        // Simulate VL querying VR (takes ~1 second)
        await Task.Delay(800, cancellationToken);

        return "BAT_LEFT:3.72\n" +
               "BAT_RIGHT:3.68\n" +
               "\x04";
    }

    private string HandleProfileLoad(string command)
    {
        var parts = command.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var profileId))
        {
            if (profileId >= 1 && profileId <= 3)
            {
                _mockCurrentProfile = profileId;
                var profileName = profileId switch
                {
                    1 => "Regular VCR",
                    2 => "Noisy VCR",
                    3 => "Hybrid VCR",
                    _ => "Unknown"
                };
                return $"STATUS:LOADED\nPROFILE:{profileName}\n\x04";
            }
        }
        return "ERROR:Invalid profile ID\n\x04";
    }

    private string GetMockProfileSettings()
    {
        // Return Noisy VCR profile by default
        return "ACTUATOR_TYPE:LRA\n" +
               "ACTUATOR_FREQUENCY:250\n" +
               "ACTUATOR_VOLTAGE:2.50\n" +
               "TIME_ON:0.100\n" +
               "TIME_OFF:0.067\n" +
               "TIME_SESSION:120\n" +
               "AMPLITUDE_MIN:100\n" +
               "AMPLITUDE_MAX:100\n" +
               "JITTER:23.5\n" +
               "MIRROR:True\n" +
               "PATTERN_TYPE:RNDP\n" +
               "\x04";
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

    private string GetMockSessionStatus()
    {
        if (_mockSessionState == SessionState.IDLE)
        {
            return "SESSION_STATUS:IDLE\n" +
                   "ELAPSED_TIME:0\n" +
                   "TOTAL_TIME:7200\n" +
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

        return $"SESSION_STATUS:{_mockSessionState}\n" +
               $"ELAPSED_TIME:{elapsedSeconds}\n" +
               $"TOTAL_TIME:{totalSeconds}\n" +
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
        if (parts.Length >= 4)
        {
            return $"FINGER:{parts[1]}\n" +
                   $"INTENSITY:{parts[2]}\n" +
                   $"DURATION:{parts[3]}\n" +
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
               "COMMAND:RESTART\n" +
               "COMMAND:HELP\n" +
               "\x04";
    }
}
