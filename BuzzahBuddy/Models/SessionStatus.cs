namespace BuzzahBuddy.Models;

/// <summary>
/// Session state enum matching BlueBuzzah glove responses. Mirrors the firmware's
/// full 11-state session vocabulary, plus UNKNOWN for forward compatibility with
/// wire strings the app does not yet recognize.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// No active session.
    /// </summary>
    IDLE,

    /// <summary>
    /// Gloves are connecting.
    /// </summary>
    CONNECTING,

    /// <summary>
    /// Gloves are connected and ready to start a session.
    /// </summary>
    READY,

    /// <summary>
    /// Session is actively running.
    /// </summary>
    RUNNING,

    /// <summary>
    /// Session is paused.
    /// </summary>
    PAUSED,

    /// <summary>
    /// Session is in the process of stopping.
    /// </summary>
    STOPPING,

    /// <summary>
    /// The gloves reported an error.
    /// </summary>
    ERROR,

    /// <summary>
    /// A glove battery is low; the session continues but a warning is shown.
    /// </summary>
    LOW_BATTERY,

    /// <summary>
    /// A glove battery is critically low; the session has been stopped.
    /// </summary>
    CRITICAL_BATTERY,

    /// <summary>
    /// Connection to the gloves was lost.
    /// </summary>
    CONNECTION_LOST,

    /// <summary>
    /// The phone disconnected from the gloves.
    /// </summary>
    PHONE_DISCONNECTED,

    /// <summary>
    /// Wire value did not match any known firmware state.
    /// </summary>
    UNKNOWN
}

/// <summary>
/// Represents the current status of a therapy session on the BlueBuzzah gloves.
/// This data is returned by the SESSION_STATUS command.
/// </summary>
public class SessionStatus
{
    /// <summary>
    /// Current session state (IDLE, RUNNING, or PAUSED).
    /// </summary>
    public SessionState Status { get; set; } = SessionState.IDLE;

    /// <summary>
    /// Elapsed time in seconds (excluding paused duration).
    /// </summary>
    public int ElapsedTimeSeconds { get; set; }

    /// <summary>
    /// Total session duration in seconds.
    /// </summary>
    public int TotalTimeSeconds { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Gets whether a session is currently active (mirrors firmware isActiveState:
    /// RUNNING, PAUSED, or LOW_BATTERY — a low battery does not stop the session).
    /// </summary>
    public bool IsActive => Status is SessionState.RUNNING or SessionState.PAUSED or SessionState.LOW_BATTERY;

    /// <summary>
    /// Gets whether the session is running (RUNNING or LOW_BATTERY; not idle or paused).
    /// </summary>
    public bool IsRunning => Status is SessionState.RUNNING or SessionState.LOW_BATTERY;

    /// <summary>
    /// Gets whether the session is paused.
    /// </summary>
    public bool IsPaused => Status == SessionState.PAUSED;

    /// <summary>
    /// Gets whether the session is idle (not started or stopped).
    /// </summary>
    public bool IsIdle => Status == SessionState.IDLE;

    /// <summary>
    /// Gets the elapsed time as a TimeSpan.
    /// </summary>
    public TimeSpan ElapsedTime => TimeSpan.FromSeconds(ElapsedTimeSeconds);

    /// <summary>
    /// Gets the total time as a TimeSpan.
    /// </summary>
    public TimeSpan TotalTime => TimeSpan.FromSeconds(TotalTimeSeconds);

    /// <summary>
    /// Gets the remaining time as a TimeSpan.
    /// </summary>
    public TimeSpan RemainingTime => TimeSpan.FromSeconds(Math.Max(0, TotalTimeSeconds - ElapsedTimeSeconds));

    /// <summary>
    /// Gets the elapsed time formatted as MM:SS.
    /// </summary>
    public string ElapsedTimeFormatted => $"{(int)ElapsedTime.TotalMinutes:D2}:{ElapsedTime.Seconds:D2}";

    /// <summary>
    /// Gets the remaining time formatted as MM:SS.
    /// </summary>
    public string RemainingTimeFormatted => $"{(int)RemainingTime.TotalMinutes:D2}:{RemainingTime.Seconds:D2}";

    /// <summary>
    /// Gets the total time formatted as MM:SS.
    /// </summary>
    public string TotalTimeFormatted => $"{(int)TotalTime.TotalMinutes:D2}:{TotalTime.Seconds:D2}";

    /// <summary>
    /// Parses a CommandResponse into a SessionStatus object.
    /// </summary>
    /// <param name="response">The response from SESSION_STATUS command</param>
    /// <returns>Parsed SessionStatus</returns>
    public static SessionStatus FromCommandResponse(CommandResponse response)
    {
        var status = new SessionStatus();

        // Parse status. Unknown wire strings map to UNKNOWN, never IDLE, so an
        // unrecognized firmware state cannot masquerade as a legitimate session end.
        var statusStr = response.GetString("SESSION_STATUS");
        if (statusStr != null)
        {
            status.Status = Enum.TryParse<SessionState>(statusStr, ignoreCase: true, out var state)
                ? state
                : SessionState.UNKNOWN;
        }

        // Per BLE protocol v2.0.0: Keys are ELAPSED and TOTAL (not ELAPSED_TIME/TOTAL_TIME)
        // Parse elapsed time
        status.ElapsedTimeSeconds = response.GetInt("ELAPSED") ?? 0;

        // Parse total time
        status.TotalTimeSeconds = response.GetInt("TOTAL") ?? 0;

        // Parse progress
        status.Progress = response.GetInt("PROGRESS") ?? 0;

        return status;
    }

    /// <summary>
    /// Creates an idle (not started) session status.
    /// </summary>
    public static SessionStatus CreateIdle()
    {
        return new SessionStatus
        {
            Status = SessionState.IDLE,
            ElapsedTimeSeconds = 0,
            TotalTimeSeconds = 0,
            Progress = 0
        };
    }
}
