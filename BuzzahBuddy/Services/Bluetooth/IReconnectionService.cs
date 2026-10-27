namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Service interface for automatic BLE reconnection with exponential backoff.
/// </summary>
public interface IReconnectionService : IDisposable
{
    /// <summary>
    /// Whether a reconnection attempt is currently in progress.
    /// </summary>
    bool IsReconnecting { get; }

    /// <summary>
    /// The current reconnection attempt number (1-based).
    /// </summary>
    int CurrentAttempt { get; }

    /// <summary>
    /// Maximum number of reconnection attempts before giving up.
    /// </summary>
    int MaxAttempts { get; }

    /// <summary>
    /// Event raised when reconnection state changes.
    /// </summary>
    event EventHandler<ReconnectionStateEventArgs>? ReconnectionStateChanged;

    /// <summary>
    /// Cancels any in-progress reconnection attempt and clears the expecting reboot flag.
    /// </summary>
    /// <param name="supersededByConnect">
    /// True when a manual connect is replacing the reconnect. Observers then keep
    /// state tied to the dropped link (e.g. a running session's record) for that
    /// connect to settle, instead of treating the cancel as giving up.
    /// </param>
    void CancelReconnect(bool supersededByConnect = false);
}

/// <summary>
/// Possible states of the reconnection process.
/// </summary>
public enum ReconnectionState
{
    Idle,
    Reconnecting,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// Event arguments for reconnection state changes.
/// </summary>
public class ReconnectionStateEventArgs : EventArgs
{
    public ReconnectionState State { get; }
    public int Attempt { get; }
    public int MaxAttempts { get; }
    public string? Message { get; }

    /// <summary>
    /// On <see cref="ReconnectionState.Cancelled"/>: a manual connect replaced the
    /// reconnect (see <see cref="IReconnectionService.CancelReconnect"/>).
    /// </summary>
    public bool SupersededByConnect { get; }

    public ReconnectionStateEventArgs(ReconnectionState state, int attempt = 0, int maxAttempts = 0,
        string? message = null, bool supersededByConnect = false)
    {
        State = state;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        Message = message;
        SupersededByConnect = supersededByConnect;
    }
}
