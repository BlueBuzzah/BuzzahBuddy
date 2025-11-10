using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Service interface for controlling BlueBuzzah glove therapy sessions and settings.
/// Implements all 18 commands from the BlueBuzzah smartphone app specification.
/// </summary>
public interface IGloveControlService
{
    // ========== Device Information Commands ==========

    /// <summary>
    /// Gets device information including firmware version, battery levels, and status.
    /// Command: INFO
    /// </summary>
    Task<GloveDeviceInfo> GetDeviceInfoAsync();

    /// <summary>
    /// Gets battery voltage levels for both gloves.
    /// Command: BATTERY
    /// Note: May take up to 1 second as VL queries VR via BLE.
    /// </summary>
    Task<(double leftVoltage, double rightVoltage)> GetBatteryAsync();

    /// <summary>
    /// Pings the device to test connection health.
    /// Command: PING
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 2000)</param>
    Task<bool> PingAsync(int timeoutMs = 2000);

    // ========== Profile Management Commands ==========

    /// <summary>
    /// Lists all available therapy profiles.
    /// Command: PROFILE_LIST
    /// Returns 3 profiles: Regular VCR, Noisy VCR, Hybrid VCR
    /// </summary>
    Task<List<TherapyProfile>> ListProfilesAsync();

    /// <summary>
    /// Loads a preset therapy profile by ID.
    /// Command: PROFILE_LOAD:N
    /// </summary>
    /// <param name="profileId">Profile ID (1=Regular VCR, 2=Noisy VCR, 3=Hybrid VCR)</param>
    /// <exception cref="BlueBuzzahCommandException">If profile ID is invalid or session is active</exception>
    Task LoadProfileAsync(int profileId);

    /// <summary>
    /// Gets the current profile settings.
    /// Command: PROFILE_GET
    /// </summary>
    Task<TherapyProfile> GetCurrentProfileAsync();

    /// <summary>
    /// Sets a custom profile by modifying specific parameters.
    /// Command: PROFILE_CUSTOM:KEY:VAL:KEY:VAL...
    /// </summary>
    /// <param name="parameters">Dictionary of parameter name/value pairs to modify</param>
    /// <exception cref="BlueBuzzahCommandException">If session is active or parameters invalid</exception>
    Task SetCustomProfileAsync(Dictionary<string, string> parameters);

    // ========== Session Control Commands ==========

    /// <summary>
    /// Starts a therapy session with the currently loaded profile.
    /// Command: SESSION_START
    /// Note: May take up to 500ms to establish VL↔VR synchronization.
    /// </summary>
    /// <exception cref="BlueBuzzahCommandException">If VR not connected or battery too low</exception>
    Task StartSessionAsync();

    /// <summary>
    /// Pauses the currently running therapy session.
    /// Command: SESSION_PAUSE
    /// Motors stop, elapsed time tracking pauses.
    /// </summary>
    /// <exception cref="BlueBuzzahCommandException">If no active session</exception>
    Task PauseSessionAsync();

    /// <summary>
    /// Resumes a paused therapy session.
    /// Command: SESSION_RESUME
    /// </summary>
    /// <exception cref="BlueBuzzahCommandException">If no paused session</exception>
    Task ResumeSessionAsync();

    /// <summary>
    /// Stops the current therapy session.
    /// Command: SESSION_STOP
    /// Returns to IDLE state, enabling profile changes.
    /// </summary>
    Task StopSessionAsync();

    /// <summary>
    /// Gets the current session status including progress and timing.
    /// Command: SESSION_STATUS
    /// Should be polled every 5-10 seconds during active sessions.
    /// </summary>
    Task<SessionStatus> GetSessionStatusAsync();

    // ========== Parameter Adjustment Commands ==========

    /// <summary>
    /// Sets an individual profile parameter.
    /// Command: PARAM_SET:NAME:VALUE
    /// </summary>
    /// <param name="parameterName">Parameter name (e.g., "AMPLITUDE_MIN", "JITTER")</param>
    /// <param name="value">Parameter value</param>
    /// <exception cref="BlueBuzzahCommandException">If session active or parameter invalid</exception>
    Task SetParameterAsync(string parameterName, string value);

    // ========== Calibration Commands ==========

    /// <summary>
    /// Enters calibration mode for testing individual finger motors.
    /// Command: CALIBRATE_START
    /// </summary>
    Task EnterCalibrationAsync();

    /// <summary>
    /// Tests an individual finger motor with specified intensity and duration.
    /// Command: CALIBRATE_BUZZ:INDEX:INTENSITY:DURATION
    /// </summary>
    /// <param name="fingerIndex">Finger index 0-7 (0-3=left, 4-7=right)</param>
    /// <param name="intensity">Vibration intensity 0-100%</param>
    /// <param name="durationMs">Duration in milliseconds (50-2000)</param>
    /// <exception cref="BlueBuzzahCommandException">If not in calibration mode</exception>
    Task BuzzFingerAsync(int fingerIndex, int intensity, int durationMs);

    /// <summary>
    /// Exits calibration mode and returns to normal operation.
    /// Command: CALIBRATE_STOP
    /// </summary>
    Task ExitCalibrationAsync();

    // ========== System Commands ==========

    /// <summary>
    /// Gets a list of all available commands (for debugging/diagnostics).
    /// Command: HELP
    /// </summary>
    Task<List<string>> GetAvailableCommandsAsync();

    /// <summary>
    /// Restarts the glove device (reboots firmware).
    /// Command: RESTART
    /// Warning: BLE connection will drop immediately.
    /// </summary>
    Task RestartDeviceAsync();
}
