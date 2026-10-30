namespace BuzzahBuddy.Models;

/// <summary>
/// Why the gloves' most recent session ended (firmware <c>LAST_REASON</c>).
/// </summary>
public enum SessionEndReason
{
    Completed,
    Stopped,
    Battery,
    ConnectionLost,
    Error,
    PowerOff,

    /// <summary>
    /// A wire value this app does not know.
    /// </summary>
    Unknown
}

/// <summary>
/// How the gloves' most recent session ended, from the <c>LAST_*</c> keys firmware adds to a
/// SESSION_STATUS reply while no session runs. It persists across reboots until the next session
/// ends, so it may describe an older session: match it by <see cref="Id"/>.
/// </summary>
public sealed record LastSessionSummary(
    int Id,
    int ElapsedSeconds,
    int PausedSeconds,
    int TotalSeconds,
    SessionEndReason Reason)
{
    /// <summary>
    /// Seconds of therapy delivered: LAST_ELAPSED is wall time, pauses included.
    /// </summary>
    public int TherapySeconds => Math.Clamp(ElapsedSeconds - PausedSeconds, 0, TotalSeconds);

    /// <summary>
    /// Parses the summary, or null when the reply has none (a session is running, none has
    /// ended yet, or the firmware predates the summary).
    /// </summary>
    public static LastSessionSummary? FromCommandResponse(CommandResponse response)
    {
        var id = response.GetInt("LAST_ID");
        var elapsed = response.GetInt("LAST_ELAPSED");
        var total = response.GetInt("LAST_TOTAL");
        if (id is null || elapsed is null || total is null)
            return null;

        return new LastSessionSummary(
            id.Value,
            elapsed.Value,
            response.GetInt("LAST_PAUSED") ?? 0,
            total.Value,
            ParseReason(response.GetString("LAST_REASON")));
    }

    private static SessionEndReason ParseReason(string? wire) => wire?.ToUpperInvariant() switch
    {
        "COMPLETED" => SessionEndReason.Completed,
        "STOPPED" => SessionEndReason.Stopped,
        "BATTERY" => SessionEndReason.Battery,
        "CONNECTION_LOST" => SessionEndReason.ConnectionLost,
        "ERROR" => SessionEndReason.Error,
        "POWER_OFF" => SessionEndReason.PowerOff,
        _ => SessionEndReason.Unknown
    };
}
