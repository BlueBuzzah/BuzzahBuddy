using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Tests.TestHelpers;

/// <summary>
/// IBluetoothService fake: returns canned response text per command prefix
/// and records every command sent.
/// </summary>
public class FakeBluetoothService : IBluetoothService
{
    public Dictionary<string, string> CannedResponses { get; } = new();
    public List<string> SentCommands { get; } = new();

    public ConnectionState CurrentConnectionState { get; set; } = ConnectionState.Connected;
    public GloveDevice? ConnectedDevice { get; set; } = new() { Id = "FAKE-1", Name = "BlueBuzzah" };
    public string? LastConnectedDeviceId => ConnectedDevice?.Id;
    public bool UserInitiatedDisconnect => false;

#pragma warning disable CS0067 // Events required by IBluetoothService but not raised by this fake.
    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;
    public event EventHandler<CommandResponse>? ResponseReceived;
#pragma warning restore CS0067

    public Task<CommandResponse> SendCommandAsync(string command, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        SentCommands.Add(command);
        var key = CannedResponses.Keys.FirstOrDefault(k => command.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        var text = key != null ? CannedResponses[key] : $"ERROR:Unknown command: {command}\n\x04";
        return Task.FromResult(CommandResponse.Parse(text));
    }

    public Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<GloveDevice>());
    public Task StopScanAsync() => Task.CompletedTask;
    public Task<bool> ConnectToDeviceAsync(GloveDevice device, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectForReconnectAsync() => Task.CompletedTask;
    public Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default)
        => Task.FromResult(new ScanResult(ScanOutcome.NoDevicesFound, Enumerable.Empty<GloveDevice>()));
    public Task SubscribeToNotificationsAsync() => Task.CompletedTask;
    public Task UnsubscribeFromNotificationsAsync() => Task.CompletedTask;
    public Task<bool> IsBluetoothEnabledAsync() => Task.FromResult(true);
}
