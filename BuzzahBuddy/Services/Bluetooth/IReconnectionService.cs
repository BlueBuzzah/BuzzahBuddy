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
    void CancelReconnect();
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

    public ReconnectionStateEventArgs(ReconnectionState state, int attempt = 0, int maxAttempts = 0, string? message = null)
    {
        State = state;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        Message = message;
    }
}
