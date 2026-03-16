namespace BuzzahBuddy.Models;

/// <summary>
/// Represents the connection state of a BlueBuzzah glove device.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Device is disconnected and not available.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Connection attempt is in progress.
    /// </summary>
    Connecting,

    /// <summary>
    /// Device is successfully connected and ready for communication.
    /// </summary>
    Connected,

    /// <summary>
    /// Attempting to reconnect to a previously connected device.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Connection error occurred.
    /// </summary>
    Error
}
