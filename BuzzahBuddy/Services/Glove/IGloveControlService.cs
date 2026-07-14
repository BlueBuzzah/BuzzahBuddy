using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Service interface for controlling BlueBuzzah glove therapy sessions and settings.
/// Implements all 18 commands from the BlueBuzzah smartphone app specification.
/// </summary>
public interface IGloveControlService
{
    // ========== Session State Observation ==========

    /// <summary>
    /// Event fired when session state changes (IDLE, RUNNING, PAUSED).
    /// Use this to observe session state from other parts of the app (e.g., MainPage dashboard).
    /// </summary>
    event EventHandler<SessionStatus>? SessionStateChanged;

    /// <summary>
    /// Gets the current session status without querying the device.
    /// Returns the last known status, or an IDLE status if no session has been started.
    /// </summary>
    SessionStatus CurrentSessionStatus { get; }

    /// <summary>
    /// Gets the currently loaded therapy profile, if any.
    /// </summary>
    TherapyProfile? CurrentProfile { get; }

    /// <summary>
    /// Motors per glove on the connected device (4 or 5). Defaults to 4 until
    /// GetDeviceInfoAsync has run against a firmware that reports MOTORS.
    /// </summary>
    int DeviceActuatorCount { get; }

    /// <summary>
    /// Profile ID currently loaded on the device (1-6), 0 if unknown.
    /// </summary>
    int DeviceProfileId { get; }

    // ========== Reboot Handling ==========

    /// <summary>
    /// Indicates whether the device is expected to be rebooting (e.g., after profile load or restart).
    /// </summary>
    bool ExpectingReboot { get; }

    /// <summary>
    /// Clears the expecting reboot flag after reconnection is established.
    /// </summary>
    void ClearExpectingReboot();

    /// <summary>
    /// Synchronizes app state with device by running PING, BATTERY, and SESSION_STATUS commands.
    /// </summary>
    /// <returns>True if all sync commands succeeded, false on any failure.</returns>
    Task<bool> SyncStateAsync();

    // ========== Device Information Commands ==========

    /// <summary>
    /// Gets device information including firmware version, battery levels, and status.
    /// Command: INFO
    /// </summary>
    Task<GloveDeviceInfo> GetDeviceInfoAsync();

    /// <summary>
    /// Gets battery voltage levels for both devices.
    /// Command: BATTERY
    /// Note: May take up to 1 second as Primary queries Secondary via BLE.
    /// </summary>
    Task<(double primaryVoltage, double secondaryVoltage)> GetBatteryAsync();

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

    /// <summary>
    /// Applies a full profile edit to the device by diffing <paramref name="desired"/>
    /// against <paramref name="baseline"/> and sending only the changed parameters via
    /// PROFILE_CUSTOM (chunked to the firmware's 8-pairs-per-command limit).
    /// Changes affect the currently loaded profile and are NOT persisted by the
    /// firmware — they last until the gloves restart or another profile is loaded.
    /// NOT atomic: the protocol has no transactions, so if a chunk fails after an
    /// earlier one succeeded, some parameters are already applied on the device.
    /// Callers should re-read via <see cref="GetCurrentProfileAsync"/> after a failure.
    /// </summary>
    /// <param name="desired">Target parameter values.</param>
    /// <param name="baseline">Current device values (from <see cref="GetCurrentProfileAsync"/>); null sends every
    /// parameter. A stale baseline causes changed parameters to be silently skipped — read it fresh.</param>
    /// <exception cref="ArgumentException">If a value is outside the firmware's accepted range.</exception>
    /// <exception cref="BlueBuzzahCommandException">If a session is active or the firmware rejects a parameter.</exception>
    Task ApplyCustomProfileAsync(TherapyProfile desired, TherapyProfile? baseline = null);

    // ========== Session Control Commands ==========

    /// <summary>
    /// Starts a therapy session with the currently loaded profile.
    /// Command: SESSION_START
    /// Note: May take up to 500ms to establish Primary↔Secondary synchronization.
    /// </summary>
    /// <exception cref="BlueBuzzahCommandException">If Secondary not connected or battery too low</exception>
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
    /// <param name="fingerIndex">Finger index 0-7 (0-3=Primary device, 4-7=Secondary device)</param>
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

    /// <summary>
    /// Gets the current therapy LED off setting.
    /// Command: THERAPY_LED_OFF (query)
    /// When enabled, LED is turned off during active therapy to avoid visual distraction.
    /// Per BLE protocol v2.0.0.
    /// </summary>
    /// <returns>True if therapy LED is disabled during sessions</returns>
    Task<bool> GetTherapyLedOffAsync();

    /// <summary>
    /// Sets the therapy LED off setting.
    /// Command: THERAPY_LED_OFF:true/false
    /// When enabled, LED is turned off during active therapy to avoid visual distraction.
    /// Setting is persisted to flash and synced to SECONDARY device.
    /// Per BLE protocol v2.0.0.
    /// </summary>
    /// <param name="enabled">True to disable LED during therapy</param>
    Task SetTherapyLedOffAsync(bool enabled);

    /// <summary>
    /// Gets the current debug mode setting.
    /// Command: DEBUG (query)
    /// When enabled, PRIMARY and SECONDARY perform periodic synchronized LED flashes.
    /// Per BLE protocol v2.0.0.
    /// </summary>
    /// <returns>True if debug mode is enabled</returns>
    Task<bool> GetDebugModeAsync();

    /// <summary>
    /// Sets the debug mode setting.
    /// Command: DEBUG:true/false
    /// When enabled, PRIMARY and SECONDARY perform periodic synchronized LED flashes
    /// to verify clock sync accuracy (DEBUG_FLASH messages).
    /// Setting is persisted to flash and synced to SECONDARY device.
    /// Per BLE protocol v2.0.0.
    /// </summary>
    /// <param name="enabled">True to enable debug mode</param>
    Task SetDebugModeAsync(bool enabled);
}
