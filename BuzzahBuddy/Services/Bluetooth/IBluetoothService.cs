using BuzzahBuddy.Models;
using Plugin.BLE.Abstractions.Contracts;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Service interface for Bluetooth Low Energy communication with BlueBuzzah gloves.
/// </summary>
public interface IBluetoothService
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState CurrentConnectionState { get; }

    /// <summary>
    /// Gets the currently connected device, if any.
    /// </summary>
    GloveDevice? ConnectedDevice { get; }

    /// <summary>
    /// Event raised when a device is discovered during scanning.
    /// </summary>
    event EventHandler<GloveDevice>? DeviceDiscovered;

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Scans for available BlueBuzzah glove devices.
    /// </summary>
    /// <param name="timeout">Maximum duration to scan for devices.</param>
    /// <param name="cancellationToken">Cancellation token to stop scanning.</param>
    /// <returns>A collection of discovered devices.</returns>
    Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current device scan if one is in progress.
    /// </summary>
    Task StopScanAsync();

    /// <summary>
    /// Connects to the specified BlueBuzzah glove device.
    /// </summary>
    /// <param name="device">The device to connect to.</param>
    /// <param name="cancellationToken">Cancellation token to abort connection.</param>
    /// <returns>True if connection successful, false otherwise.</returns>
    Task<bool> ConnectToDeviceAsync(GloveDevice device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the currently connected device.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Event raised when a command response is received from the glove.
    /// </summary>
    event EventHandler<CommandResponse>? ResponseReceived;

    /// <summary>
    /// Sends a text command to the glove via the TX characteristic and waits for response.
    /// Commands are automatically terminated with \n.
    /// Responses are filtered to ignore internal VL↔VR sync messages.
    /// </summary>
    /// <param name="command">The command to send (without \n terminator)</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed command response</returns>
    Task<CommandResponse> SendCommandAsync(
        string command,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to notifications from the RX characteristic.
    /// This must be called after connecting to receive responses.
    /// </summary>
    Task SubscribeToNotificationsAsync();

    /// <summary>
    /// Unsubscribes from RX characteristic notifications.
    /// </summary>
    Task UnsubscribeFromNotificationsAsync();

    /// <summary>
    /// Checks if Bluetooth is enabled on the device.
    /// </summary>
    /// <returns>True if Bluetooth is enabled, false otherwise.</returns>
    Task<bool> IsBluetoothEnabledAsync();

    /// <summary>
    /// GUID string of the last successfully connected device.
    /// Captured on Connected state entry, persists across disconnects.
    /// </summary>
    string? LastConnectedDeviceId { get; }

    /// <summary>
    /// True when disconnect was user-initiated (via DisconnectAsync).
    /// Reset to false on next successful connection.
    /// </summary>
    bool UserInitiatedDisconnect { get; }

    /// <summary>
    /// Connect to the last known device by stored GUID without scanning.
    /// Uses Plugin.BLE's ConnectToKnownDeviceAsync.
    /// </summary>
    Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default);

    /// <summary>
    /// Disconnect without setting the user-initiated flag.
    /// Used by health check to trigger reconnection on unhealthy connection.
    /// </summary>
    Task DisconnectForReconnectAsync();

    /// <summary>
    /// Scan with differentiated result outcomes.
    /// </summary>
    Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default);
}
