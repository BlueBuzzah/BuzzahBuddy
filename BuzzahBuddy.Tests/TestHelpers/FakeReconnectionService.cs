using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;

namespace BuzzahBuddy.Tests.TestHelpers;

/// <summary>IReconnectionService fake: the test raises each state by hand.</summary>
public sealed class FakeReconnectionService(IGloveControlService? glove = null) : IReconnectionService
{
    public bool IsReconnecting { get; set; }
    public int CurrentAttempt { get; private set; }
    public int MaxAttempts => 20;

    public event EventHandler<ReconnectionStateEventArgs>? ReconnectionStateChanged;

    /// <summary>
    /// Sets IsReconnecting and raises the state. Like ReconnectionService, a loop that ends
    /// (succeeded, failed or cancelled) clears the glove's ExpectingReboot.
    /// </summary>
    public void Raise(ReconnectionState state, bool supersededByConnect = false)
    {
        IsReconnecting = state == ReconnectionState.Reconnecting;
        CurrentAttempt = IsReconnecting ? CurrentAttempt + 1 : 0;
        if (!IsReconnecting && state != ReconnectionState.Idle)
            glove?.ClearExpectingReboot();
        ReconnectionStateChanged?.Invoke(this,
            new ReconnectionStateEventArgs(state, CurrentAttempt, MaxAttempts, supersededByConnect: supersededByConnect));
    }

    public void CancelReconnect(bool supersededByConnect = false) =>
        Raise(ReconnectionState.Cancelled, supersededByConnect);

    public void Dispose() { }
}
