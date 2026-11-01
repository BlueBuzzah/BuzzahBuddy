using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Bluetooth;

public enum AutoConnectState { Idle, Connecting, Failed }

/// <summary>
/// Connects to the remembered gloves when the app opens (Settings, "Auto-Connect to Last
/// Used Gloves"). One attempt, bounded by the transport's 15 s connect timeout. While it runs,
/// Session and Gloves show their scanning state; when it fails, both show a banner with Try
/// Again. Any connection, from here or a manual connect, returns it to idle and the banner
/// goes (owner override of the Banner README's "don't auto-dismiss").
/// </summary>
public sealed class AutoConnectService
{
    /// <summary>Preferences key for the Settings switch; on by default.</summary>
    public const string SettingKey = "AutoConnect";

    private readonly IBluetoothService _bluetooth;
    private readonly Action<string> _announce;
    private string? _deviceId;
    private CancellationTokenSource? _cts;
    private Task _attempt = Task.CompletedTask;
    private bool _bluetoothOff;

    public AutoConnectState State { get; private set; }

    /// <summary>Raised on any thread; handlers dispatch to the main thread.</summary>
    public event EventHandler? StateChanged;

    public AutoConnectService(IBluetoothService bluetooth, Action<string> announce)
    {
        _bluetooth = bluetooth;
        _announce = announce;
        _bluetooth.ConnectionStateChanged += (_, state) =>
        {
            if (state == ConnectionState.Connected)
                SetState(AutoConnectState.Idle);
        };
    }

    /// <summary>The failure banner (action "Try Again"), or null. Session and Gloves show the same one.</summary>
    public (StatusTone Tone, string Title, string Message)? FailedBanner => State == AutoConnectState.Failed
        ? (StatusTone.Warning, "Couldn't Connect to Your Gloves",
            _bluetoothOff
                ? "Turn on Bluetooth, then tap Try Again."
                : $"Make sure both gloves are on and near your {DeviceNoun.Current}, then tap Try Again.")
        : null;

    /// <summary>Starts the launch attempt for the remembered primary glove.</summary>
    public void Start(string deviceId)
    {
        _deviceId = deviceId;
        TryAgain();
    }

    public void TryAgain()
    {
        if (_deviceId is null || State == AutoConnectState.Connecting
            || _bluetooth.CurrentConnectionState == ConnectionState.Connected)
            return;
        _attempt = RunAsync(_deviceId);
    }

    /// <summary>
    /// Stops an attempt in progress and clears the banner. Waits for the attempt to wind
    /// down, so a manual connect or a Demo Mode switch doesn't race it.
    /// </summary>
    public async Task CancelAsync()
    {
        _cts?.Cancel();
        await _attempt;
        SetState(AutoConnectState.Idle);
    }

    private async Task RunAsync(string deviceId)
    {
        using var cts = new CancellationTokenSource();
        _cts = cts;
        SetState(AutoConnectState.Connecting);

        bool connected;
        try
        {
            connected = await _bluetooth.ConnectToKnownDeviceAsync(deviceId, cts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AUTOCONNECT] {ex.GetType().Name}: {ex.Message}");
            connected = false;
        }
        _cts = null;

        if (connected)
        {
            SetState(AutoConnectState.Idle);
            _announce($"Connected to {_bluetooth.ConnectedDevice?.DisplayName ?? "your gloves"}");
        }
        else if (cts.IsCancellationRequested)
        {
            SetState(AutoConnectState.Idle);
        }
        else
        {
            _bluetoothOff = !await _bluetooth.IsBluetoothEnabledAsync();
            SetState(AutoConnectState.Failed);
        }
    }

    private void SetState(AutoConnectState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
