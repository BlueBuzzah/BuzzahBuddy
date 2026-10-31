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

    /// <summary>
    /// When non-empty, each send dequeues the next response regardless of command,
    /// taking precedence over <see cref="CannedResponses"/>. Lets a test script
    /// per-call outcomes (e.g. first chunk succeeds, second fails).
    /// </summary>
    public Queue<string> QueuedResponses { get; } = new();

    /// <summary>
    /// Exceptions to throw instead of responding, keyed by command prefix. Models a
    /// link that drops mid-command — the gloves reboot on PROFILE_LOAD, so the send
    /// can fail even though the device acted on it.
    /// </summary>
    public Dictionary<string, Exception> ThrowOnCommand { get; } = new();

    public List<string> SentCommands { get; } = new();

    /// <summary>
    /// When set, every reply waits for this to complete - lets a test hold a command
    /// in flight while it makes overlapping calls.
    /// </summary>
    public TaskCompletionSource? HoldReplies { get; set; }

    public ConnectionState CurrentConnectionState { get; set; } = ConnectionState.Connected;
    public GloveDevice? ConnectedDevice { get; set; } = new() { Id = "FAKE-1", Name = "BlueBuzzah" };
    public string? LastConnectedDeviceId => ConnectedDevice?.Id;
    public bool UserInitiatedDisconnect => false;

#pragma warning disable CS0067 // Events required by IBluetoothService but not raised by this fake.
    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<CommandResponse>? ResponseReceived;
#pragma warning restore CS0067
    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    /// <summary>Sets the state and raises ConnectionStateChanged, as the transport does.</summary>
    public void RaiseConnectionStateChanged(ConnectionState state)
    {
        CurrentConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }

    public Task<CommandResponse> SendCommandAsync(string command, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        SentCommands.Add(command);
        if (HoldReplies is { } hold)
        {
            return hold.Task.ContinueWith(_ => Reply(command), TaskScheduler.Default).Unwrap();
        }
        return Reply(command);
    }

    private Task<CommandResponse> Reply(string command)
    {

        // Longest prefix wins, so "PROFILE_LOAD" beats "PROFILE" regardless of insertion order.
        var throwKey = ThrowOnCommand.Keys
            .Where(k => command.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        if (throwKey != null)
        {
            return Task.FromException<CommandResponse>(ThrowOnCommand[throwKey]);
        }

        if (QueuedResponses.Count > 0)
        {
            return Task.FromResult(CommandResponse.Parse(QueuedResponses.Dequeue()));
        }
        var key = CannedResponses.Keys
            .Where(k => command.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        var text = key != null ? CannedResponses[key] : $"ERROR:Unknown command: {command}\n\x04";
        return Task.FromResult(CommandResponse.Parse(text));
    }

    public Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<GloveDevice>());
    public Task StopScanAsync() => Task.CompletedTask;
    public Task<bool> ConnectToDeviceAsync(GloveDevice device, CancellationToken cancellationToken = default) => Task.FromResult(true);
    /// <summary>Drops the link and says so, as the transport does.</summary>
    public Task DisconnectAsync()
    {
        if (CurrentConnectionState != ConnectionState.Disconnected)
            RaiseConnectionStateChanged(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }
    public Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>What ConnectToKnownDeviceAsync does; defaults to connecting at once.</summary>
    public Func<string, CancellationToken, Task<bool>> ConnectToKnown { get; set; } = (_, _) => Task.FromResult(true);
    public Task<bool> ConnectToKnownDeviceAsync(string deviceId, CancellationToken ct = default) => ConnectToKnown(deviceId, ct);
    public bool BluetoothEnabled { get; set; } = true;
    public int DisconnectForReconnectCalls { get; private set; }

    public Task DisconnectForReconnectAsync()
    {
        DisconnectForReconnectCalls++;
        return Task.CompletedTask;
    }
    public Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default)
        => Task.FromResult(new ScanResult(ScanOutcome.NoDevicesFound, Enumerable.Empty<GloveDevice>()));
    public Task SubscribeToNotificationsAsync() => Task.CompletedTask;
    public Task UnsubscribeFromNotificationsAsync() => Task.CompletedTask;
    public Task<bool> IsBluetoothEnabledAsync() => Task.FromResult(BluetoothEnabled);
}
