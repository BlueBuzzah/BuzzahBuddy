using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Helpers;

/// <summary>
/// Shared helper for mapping ReconnectionStateEventArgs to display properties.
/// </summary>
public static class ReconnectionHelper
{
    /// <summary>
    /// Maps a reconnection state event to UI display values.
    /// </summary>
    public static (bool IsReconnecting, string ReconnectionMessage) MapReconnectionState(
        ReconnectionStateEventArgs e) => e.State switch
    {
        ReconnectionState.Reconnecting => (true, $"Reconnecting to BlueBuzzah... (attempt {e.Attempt}/{e.MaxAttempts})"),
        ReconnectionState.Succeeded => (false, string.Empty),
        ReconnectionState.Failed => (false, e.Message ?? "Connection lost. Tap to reconnect."),
        _ => (false, string.Empty)
    };
}
