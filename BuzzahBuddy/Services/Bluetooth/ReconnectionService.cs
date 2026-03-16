using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Glove;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Automatically reconnects to a BlueBuzzah glove after unexpected disconnection.
/// Uses exponential backoff: 1s → 2s → 4s → 8s → 16s → 30s (cap), up to 20 attempts.
/// </summary>
public class ReconnectionService : IReconnectionService
{
    private static readonly int[] BackoffDelaysMs = { 1000, 2000, 4000, 8000, 16000, 30000 };
    private const int RebootInitialDelayMs = 3000;
    private const int MaxReconnectAttempts = 20;

    private readonly IBluetoothService _bluetoothService;
    private readonly IGloveControlService _gloveControlService;

    private CancellationTokenSource? _reconnectCts;
    private bool _isReconnecting;
    private int _currentAttempt;
    private bool _disposed;

    public bool IsReconnecting => _isReconnecting;
    public int CurrentAttempt => _currentAttempt;
    public int MaxAttempts => MaxReconnectAttempts;

    public event EventHandler<ReconnectionStateEventArgs>? ReconnectionStateChanged;

    public ReconnectionService(IBluetoothService bluetoothService, IGloveControlService gloveControlService)
    {
        _bluetoothService = bluetoothService;
        _gloveControlService = gloveControlService;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// Cancels any in-progress reconnection and clears the expecting reboot flag.
    /// </summary>
    public void CancelReconnect()
    {
        if (_reconnectCts != null && !_reconnectCts.IsCancellationRequested)
        {
            _reconnectCts.Cancel();
            _reconnectCts.Dispose();
            _reconnectCts = null;
        }

        _isReconnecting = false;
        _currentAttempt = 0;
        _gloveControlService.ClearExpectingReboot();

        RaiseStateChanged(ReconnectionState.Cancelled, 0, MaxReconnectAttempts, "Reconnection cancelled by user");
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState newState)
    {
        // Flag precedence: UserInitiatedDisconnect wins over ExpectingReboot.
        // If user initiated, never reconnect regardless of other flags.
        var userInitiated = _bluetoothService.UserInitiatedDisconnect;
        var expectingReboot = _gloveControlService.ExpectingReboot;

        switch (newState)
        {
            case ConnectionState.Error:
                // Connection lost unexpectedly
                if (!userInitiated)
                {
                    StartReconnectLoop(expectingReboot);
                }
                break;

            case ConnectionState.Disconnected:
                if (userInitiated)
                {
                    // User chose to disconnect — do NOT reconnect
                    return;
                }

                if (expectingReboot)
                {
                    // Device is rebooting (e.g., profile load, restart command)
                    // Use initial delay to let device boot before attempting
                    StartReconnectLoop(expectingReboot: true);
                }
                else
                {
                    // Unexpected disconnect (not user, not reboot) — reconnect immediately
                    StartReconnectLoop(expectingReboot: false);
                }
                break;

            case ConnectionState.Connected:
                // If we were reconnecting, the loop handles success internally.
                // External connections (e.g., user manual connect) should reset state.
                if (_isReconnecting)
                {
                    // Let the reconnect loop detect success via ConnectToLastKnownDeviceAsync return value
                }
                break;
        }
    }

    /// <summary>
    /// Starts the reconnection loop. async void is intentional — this is an event-handler-triggered fire-and-forget.
    /// </summary>
    private async void StartReconnectLoop(bool expectingReboot)
    {
        // Guard against re-entrant calls
        if (_isReconnecting)
            return;

        // Must have a last known device to reconnect to
        if (string.IsNullOrEmpty(_bluetoothService.LastConnectedDeviceId))
        {
            System.Diagnostics.Debug.WriteLine("[RECONNECT] No last connected device ID — cannot reconnect");
            return;
        }

        _isReconnecting = true;
        _currentAttempt = 0;
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;

        try
        {
            // If device is rebooting, wait for it to come back up
            if (expectingReboot)
            {
                System.Diagnostics.Debug.WriteLine($"[RECONNECT] Device rebooting — waiting {RebootInitialDelayMs}ms before first attempt");
                await Task.Delay(RebootInitialDelayMs, ct);
            }

            for (_currentAttempt = 1; _currentAttempt <= MaxReconnectAttempts; _currentAttempt++)
            {
                ct.ThrowIfCancellationRequested();

                System.Diagnostics.Debug.WriteLine($"[RECONNECT] Attempt {_currentAttempt}/{MaxReconnectAttempts}");
                RaiseStateChanged(ReconnectionState.Reconnecting, _currentAttempt, MaxReconnectAttempts,
                    $"Reconnecting (attempt {_currentAttempt}/{MaxReconnectAttempts})...");

                try
                {
                    // ConnectToLastKnownDeviceAsync sets ConnectionState.Reconnecting internally
                    var success = await _bluetoothService.ConnectToLastKnownDeviceAsync(ct);
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RECONNECT] Success on attempt {_currentAttempt}");

                        // Sync state with device and clear reboot flag
                        await _gloveControlService.SyncStateAsync();
                        _gloveControlService.ClearExpectingReboot();

                        _isReconnecting = false;
                        _currentAttempt = 0;
                        RaiseStateChanged(ReconnectionState.Succeeded, _currentAttempt, MaxReconnectAttempts,
                            "Reconnected successfully");
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RECONNECT] Attempt {_currentAttempt} failed: {ex.Message}");
                }

                // Backoff delay before next attempt
                if (_currentAttempt < MaxReconnectAttempts)
                {
                    var delayIndex = Math.Min(_currentAttempt - 1, BackoffDelaysMs.Length - 1);
                    var delayMs = BackoffDelaysMs[delayIndex];
                    System.Diagnostics.Debug.WriteLine($"[RECONNECT] Backing off {delayMs}ms before next attempt");
                    await Task.Delay(delayMs, ct);
                }
            }

            // Exhausted all attempts
            System.Diagnostics.Debug.WriteLine($"[RECONNECT] Failed after {MaxReconnectAttempts} attempts");
            _gloveControlService.ClearExpectingReboot();
            _isReconnecting = false;
            _currentAttempt = 0;
            RaiseStateChanged(ReconnectionState.Failed, MaxReconnectAttempts, MaxReconnectAttempts,
                $"Could not reconnect after {MaxReconnectAttempts} attempts");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[RECONNECT] Cancelled");
            _isReconnecting = false;
            _currentAttempt = 0;
            // CancelReconnect already raises Cancelled state
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RECONNECT] Unexpected error: {ex.Message}");
            _isReconnecting = false;
            _currentAttempt = 0;
            RaiseStateChanged(ReconnectionState.Failed, _currentAttempt, MaxReconnectAttempts,
                $"Reconnection error: {ex.Message}");
        }
    }

    private void RaiseStateChanged(ReconnectionState state, int attempt, int maxAttempts, string? message = null)
    {
        ReconnectionStateChanged?.Invoke(this, new ReconnectionStateEventArgs(state, attempt, maxAttempts, message));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }
}
