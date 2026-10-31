using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// The app's one <see cref="IBluetoothService"/>: real gloves, or the pretend ones
/// (<see cref="MockBluetoothService"/>) while Demo Mode is on. Every call goes to the
/// active service and only its events are passed on, so the services built on top
/// never know which one they're talking to.
/// </summary>
public sealed class SwitchableBluetoothService : IBluetoothService
{
    /// <summary>Preferences key holding whether Demo Mode is on; read at startup.</summary>
    public const string DemoModeKey = "demo_mode";

    private readonly IBluetoothService _real;
    private readonly IBluetoothService _demo;
    private IBluetoothService _active;

    public SwitchableBluetoothService(IBluetoothService real, IBluetoothService demo, bool demoMode)
    {
        _real = real;
        _demo = demo;
        _active = demoMode ? demo : real;

        foreach (var source in new[] { real, demo })
        {
            source.DeviceDiscovered += (_, e) => { if (source == _active) DeviceDiscovered?.Invoke(this, e); };
            source.ConnectionStateChanged += (_, e) => { if (source == _active) ConnectionStateChanged?.Invoke(this, e); };
            source.ResponseReceived += (_, e) => { if (source == _active) ResponseReceived?.Invoke(this, e); };
        }
    }

    /// <summary>True while the pretend gloves stand in for real ones.</summary>
    public bool IsDemoMode => _active == _demo;

    /// <summary>
    /// Switches between real and pretend gloves. Stops a scan and disconnects whatever
    /// the current service is connected to first, so nothing carries across.
    /// </summary>
    public async Task SetDemoModeAsync(bool demoMode)
    {
        var next = demoMode ? _demo : _real;
        if (next == _active)
            return;

        var previous = _active;
        await previous.StopScanAsync();
        if (previous.CurrentConnectionState != ConnectionState.Disconnected)
            await previous.DisconnectAsync();

        _active = next;
        // Observers last heard the previous service's state; correct it if this one differs.
        if (next.CurrentConnectionState != previous.CurrentConnectionState)
            ConnectionStateChanged?.Invoke(this, next.CurrentConnectionState);
    }

    public ConnectionState CurrentConnectionState => _active.CurrentConnectionState;
    public GloveDevice? ConnectedDevice => _active.ConnectedDevice;
    public string? LastConnectedDeviceId => _active.LastConnectedDeviceId;
    public bool UserInitiatedDisconnect => _active.UserInitiatedDisconnect;

    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;
    public event EventHandler<CommandResponse>? ResponseReceived;

    public Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _active.ScanForDevicesAsync(timeout, cancellationToken);

    public Task StopScanAsync() => _active.StopScanAsync();

    public Task<bool> ConnectToDeviceAsync(GloveDevice device, CancellationToken cancellationToken = default) =>
        _active.ConnectToDeviceAsync(device, cancellationToken);

    public Task DisconnectAsync() => _active.DisconnectAsync();

    public Task<CommandResponse> SendCommandAsync(string command, int timeoutMs = 5000, CancellationToken cancellationToken = default) =>
        _active.SendCommandAsync(command, timeoutMs, cancellationToken);

    public Task SubscribeToNotificationsAsync() => _active.SubscribeToNotificationsAsync();

    public Task UnsubscribeFromNotificationsAsync() => _active.UnsubscribeFromNotificationsAsync();

    public Task<bool> IsBluetoothEnabledAsync() => _active.IsBluetoothEnabledAsync();

    public Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default) => _active.ConnectToLastKnownDeviceAsync(ct);

    public Task<bool> ConnectToKnownDeviceAsync(string deviceId, CancellationToken ct = default) =>
        _active.ConnectToKnownDeviceAsync(deviceId, ct);

    public Task DisconnectForReconnectAsync() => _active.DisconnectForReconnectAsync();

    public Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default) =>
        _active.ScanForDevicesWithResultAsync(timeoutMs, ct);
}
