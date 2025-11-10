namespace BuzzahBuddy.Models;

/// <summary>
/// Session state enum matching BlueBuzzah glove responses.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// No active session.
    /// </summary>
    IDLE,

    /// <summary>
    /// Session is actively running.
    /// </summary>
    RUNNING,

    /// <summary>
    /// Session is paused.
    /// </summary>
    PAUSED
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
    /// Gets whether a session is currently active (running or paused).
    /// </summary>
    public bool IsActive => Status == SessionState.RUNNING || Status == SessionState.PAUSED;

    /// <summary>
    /// Gets whether the session is running (not idle or paused).
    /// </summary>
    public bool IsRunning => Status == SessionState.RUNNING;

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

        // Parse status
        var statusStr = response.GetString("SESSION_STATUS");
        if (statusStr != null && Enum.TryParse<SessionState>(statusStr, out var state))
        {
            status.Status = state;
        }

        // Parse elapsed time
        status.ElapsedTimeSeconds = response.GetInt("ELAPSED_TIME") ?? 0;

        // Parse total time
        status.TotalTimeSeconds = response.GetInt("TOTAL_TIME") ?? 0;

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
