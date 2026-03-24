# Connection State Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all connection-state inconsistencies across the BuzzahBuddy app — centralize state tracking, fix service-layer race conditions, and ensure every UI element reflects connection state accurately.

**Architecture:** Introduce `IConnectionStateService` as the single source of truth for connection state. All 5 ViewModels stop independently tracking `IsConnected`/`ConnectionState`/`ConnectedDeviceName` and instead expose the centralized service. Service-layer fixes address race conditions in `BluetoothService` disconnect paths. XAML bindings are updated to disable/hide controls based on connection state.

**Tech Stack:** .NET 9.0, .NET MAUI, CommunityToolkit.Mvvm, Plugin.BLE

**Spec:** `docs/superpowers/specs/2026-03-24-connection-state-consistency-design.md`

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `Services/ConnectionState/IConnectionStateService.cs` | Interface — single source of truth for connection state properties |
| `Services/ConnectionState/ConnectionStateService.cs` | Implementation — subscribes to BLE + reconnection events, fires PropertyChanged on main thread |

### Modified Files (by task)
| File | Changes |
|------|---------|
| `MauiProgram.cs` | Register `IConnectionStateService` singleton |
| `Services/Bluetooth/BluetoothService.cs` | S1 (null ConnectedDevice on connection lost), S2 (cancel pending TCS + clear buffer on disconnect), S4 (prevent spurious reconnect on first-connect failure) |
| `Services/Bluetooth/MockBluetoothService.cs` | S6 (emit Connecting/Reconnecting intermediate states, fix DisconnectForReconnect state ordering) |
| `Services/Glove/GloveControlService.cs` | S5 (add EnsureConnected pre-check to all command methods) |
| `ViewModels/SettingsViewModel.cs` | V1 (centralize state), V7 (try-catch DisconnectAsync) |
| `ViewModels/CalibrationViewModel.cs` | V1, V2 (add Dispose), V3 (disconnect feedback during calibration) |
| `ViewModels/DeviceListViewModel.cs` | V1, V8 (await connect dialog) |
| `ViewModels/GloveControlViewModel.cs` | V1, V4 (persist incomplete sessions), V5 (battery refresh on reconnect), V6 (dispose timers) |
| `ViewModels/MainPageViewModel.cs` | V1 |
| `Views/GloveControlPage.xaml` | X1 (disable buttons when disconnected), X2 (battery card disconnected state) |
| `Views/CalibrationPage.xaml` | X3 (connection guards on all controls, not-connected banner) |
| `Views/SettingsPage.xaml` | X4 (instructional text when disconnected) |
| `Views/MainPage.xaml` | X5 (battery section disconnected state) |

---

## Task 1: Create IConnectionStateService

**Files:**
- Create: `BuzzahBuddy/Services/ConnectionState/IConnectionStateService.cs`
- Create: `BuzzahBuddy/Services/ConnectionState/ConnectionStateService.cs`
- Modify: `BuzzahBuddy/MauiProgram.cs:46` (add registration)

- [ ] **Step 1: Create the interface**

Create `BuzzahBuddy/Services/ConnectionState/IConnectionStateService.cs`:

```csharp
using System.ComponentModel;

namespace BuzzahBuddy.Services.ConnectionState;

/// <summary>
/// Single source of truth for device connection state.
/// ViewModels expose this service for XAML binding instead of independently tracking state.
/// </summary>
public interface IConnectionStateService : INotifyPropertyChanged
{
    /// <summary>Current connection state enum value.</summary>
    Models.ConnectionState ConnectionState { get; }

    /// <summary>True when ConnectionState == Connected.</summary>
    bool IsConnected { get; }

    /// <summary>Name of the connected device, or null when disconnected.</summary>
    string? ConnectedDeviceName { get; }

    /// <summary>True when auto-reconnection is in progress.</summary>
    bool IsReconnecting { get; }

    /// <summary>Human-readable reconnection status message.</summary>
    string? ReconnectionMessage { get; }
}
```

- [ ] **Step 2: Create the implementation**

Create `BuzzahBuddy/Services/ConnectionState/ConnectionStateService.cs`:

```csharp
using System.ComponentModel;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Services.ConnectionState;

/// <summary>
/// Singleton service that subscribes to IBluetoothService and IReconnectionService events
/// and maintains the canonical connection state. All property changes are marshalled
/// to the main thread for safe XAML binding.
/// </summary>
public class ConnectionStateService : IConnectionStateService, IDisposable
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IReconnectionService _reconnectionService;
    private bool _disposed;

    private Models.ConnectionState _connectionState;
    private bool _isConnected;
    private string? _connectedDeviceName;
    private bool _isReconnecting;
    private string? _reconnectionMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Models.ConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                OnPropertyChanged(nameof(ConnectionState));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public string? ConnectedDeviceName
    {
        get => _connectedDeviceName;
        private set
        {
            if (_connectedDeviceName != value)
            {
                _connectedDeviceName = value;
                OnPropertyChanged(nameof(ConnectedDeviceName));
            }
        }
    }

    public bool IsReconnecting
    {
        get => _isReconnecting;
        private set
        {
            if (_isReconnecting != value)
            {
                _isReconnecting = value;
                OnPropertyChanged(nameof(IsReconnecting));
            }
        }
    }

    public string? ReconnectionMessage
    {
        get => _reconnectionMessage;
        private set
        {
            if (_reconnectionMessage != value)
            {
                _reconnectionMessage = value;
                OnPropertyChanged(nameof(ReconnectionMessage));
            }
        }
    }

    public ConnectionStateService(
        IBluetoothService bluetoothService,
        IReconnectionService reconnectionService)
    {
        _bluetoothService = bluetoothService;
        _reconnectionService = reconnectionService;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _reconnectionService.ReconnectionStateChanged += OnReconnectionStateChanged;

        // Initialize from current state (must be on main thread for PropertyChanged)
        MainThread.BeginInvokeOnMainThread(SyncFromService);
    }

    private void SyncFromService()
    {
        ConnectionState = _bluetoothService.CurrentConnectionState;
        IsConnected = ConnectionState == Models.ConnectionState.Connected;
        ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;
    }

    private void OnConnectionStateChanged(object? sender, Models.ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionState = state;
            IsConnected = state == Models.ConnectionState.Connected;
            ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;
        });
    }

    private void OnReconnectionStateChanged(object? sender, ReconnectionStateEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsReconnecting = e.State == ReconnectionState.Reconnecting;
            ReconnectionMessage = e.State switch
            {
                ReconnectionState.Reconnecting => $"Reconnecting... (attempt {e.Attempt}/{e.MaxAttempts})",
                ReconnectionState.Succeeded => "Reconnected to BlueBuzzah gloves",
                ReconnectionState.Failed => "Could not reconnect. Please reconnect manually.",
                _ => null
            };
        });
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            _reconnectionService.ReconnectionStateChanged -= OnReconnectionStateChanged;
            _disposed = true;
        }
    }
}
```

- [ ] **Step 3: Register in DI**

In `BuzzahBuddy/MauiProgram.cs`, add the registration after the `IReconnectionService` registration (after line 47):

```csharp
builder.Services.AddSingleton<BuzzahBuddy.Services.ConnectionState.IConnectionStateService, BuzzahBuddy.Services.ConnectionState.ConnectionStateService>();
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 5: Commit**

```
git add BuzzahBuddy/Services/ConnectionState/ BuzzahBuddy/MauiProgram.cs
git commit -m "feat: add IConnectionStateService as centralized connection state source"
```

---

## Task 2: BluetoothService — Service-Layer Fixes (S1, S2, S4)

**Files:**
- Modify: `BuzzahBuddy/Services/Bluetooth/BluetoothService.cs`

- [ ] **Step 1: Fix OnDeviceConnectionLost (S1) — null ConnectedDevice before firing event**

In `BluetoothService.cs`, find the `OnDeviceConnectionLost` method (line 537). Replace the entire method:

Old (lines 537-548):
```csharp
    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Error;
        }
        UpdateConnectionState(ConnectionState.Error);
    }
```

New:
```csharp
    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Error;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Error);
    }
```

- [ ] **Step 2: Add disconnect cleanup to OnDeviceDisconnected (S2) — clear buffer, cancel TCS**

In `BluetoothService.cs`, find the `OnDeviceDisconnected` method (line 523). Add buffer clear and TCS cancellation after nulling fields, **before** `UpdateConnectionState`:

Old (lines 523-535):
```csharp
    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Disconnected;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Disconnected);
    }
```

New:
```csharp
    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        // Clear stale response data before cancelling pending commands
        _responseBuffer.Clear();
        _pendingResponseTcs?.TrySetCanceled();

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Disconnected;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Disconnected);
    }
```

- [ ] **Step 3: Add same cleanup to OnDeviceConnectionLost**

Update the `OnDeviceConnectionLost` method (just modified in Step 1) to also clear buffer and cancel TCS:

Old (from Step 1):
```csharp
    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Error;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Error);
    }
```

New:
```csharp
    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        // Clear stale response data before cancelling pending commands
        _responseBuffer.Clear();
        _pendingResponseTcs?.TrySetCanceled();

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Error;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Error);
    }
```

- [ ] **Step 4: Fix spurious reconnect on first-connect failure (S4)**

In `BluetoothService.cs`, find the `ConnectToDeviceAsync` method. Locate the NUS service-not-found path (around lines 151-157) and the characteristic-not-found paths (around lines 164-170). In each error path, set `_userInitiatedDisconnect = true` before calling `DisconnectAsync()`, then set it back to `false` after. This prevents `ReconnectionService` from treating a first-connect failure as an unexpected drop.

For each error path that calls `await DisconnectAsync();` followed by `UpdateConnectionState(ConnectionState.Error);`, wrap with the flag:

```csharp
// Prevent ReconnectionService from starting a reconnect loop for a first-connect failure
_userInitiatedDisconnect = true;
await DisconnectAsync();
_userInitiatedDisconnect = false;
UpdateConnectionState(ConnectionState.Error);
return false;
```

Apply this pattern to every error path in `ConnectToDeviceAsync` that calls `DisconnectAsync()` (NUS service not found, TX characteristic not found, RX characteristic not found).

- [ ] **Step 5: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 6: Commit**

```
git add BuzzahBuddy/Services/Bluetooth/BluetoothService.cs
git commit -m "fix(ble): null ConnectedDevice on connection lost, cancel pending commands on disconnect, prevent spurious reconnect on first-connect failure"
```

---

## Task 3: MockBluetoothService — State Parity (S6)

**Files:**
- Modify: `BuzzahBuddy/Services/Bluetooth/MockBluetoothService.cs`

- [ ] **Step 1: Add Connecting intermediate state to ConnectToDeviceAsync**

In `MockBluetoothService.cs`, find `ConnectToDeviceAsync` (line 66). Add `Connecting` state emission before the simulated delay:

After the method signature and before `await Task.Delay(...)`, add:
```csharp
CurrentConnectionState = ConnectionState.Connecting;
ConnectionStateChanged?.Invoke(this, ConnectionState.Connecting);
```

- [ ] **Step 2: Add Reconnecting intermediate state to ConnectToLastKnownDeviceAsync**

In `MockBluetoothService.cs`, find `ConnectToLastKnownDeviceAsync` (line 105). Add `Reconnecting` state emission before the simulated delay:

After the guard clause and before `await Task.Delay(...)`, add:
```csharp
CurrentConnectionState = ConnectionState.Reconnecting;
ConnectionStateChanged?.Invoke(this, ConnectionState.Reconnecting);
```

- [ ] **Step 3: Verify DisconnectForReconnectAsync state ordering**

In `MockBluetoothService.cs`, find `DisconnectForReconnectAsync` (line 130). Verify that `CurrentConnectionState` is assigned **before** firing the event. The current code already has the correct ordering (`CurrentConnectionState = Disconnected` before `ConnectionStateChanged?.Invoke`). Confirm this is the case — no change needed if ordering is already correct.

- [ ] **Step 4: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 5: Commit**

```
git add BuzzahBuddy/Services/Bluetooth/MockBluetoothService.cs
git commit -m "fix(mock): emit Connecting/Reconnecting intermediate states to match real service"
```

---

## Task 4: GloveControlService — Connection Pre-Check (S5)

**Files:**
- Modify: `BuzzahBuddy/Services/Glove/GloveControlService.cs`

- [ ] **Step 1: Add EnsureConnected helper method**

In `GloveControlService.cs`, add a private helper method after the constructor (after line 55):

```csharp
    /// <summary>
    /// Validates that the Bluetooth service is in a connected state before issuing a command.
    /// Provides a clear error message instead of letting the transport layer throw a generic exception.
    /// </summary>
    private void EnsureConnected()
    {
        if (_bluetoothService.CurrentConnectionState != ConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "Cannot send command: not connected to a BlueBuzzah device. " +
                "Please connect to a device first.");
        }
    }
```

- [ ] **Step 2: Add EnsureConnected call to every command method**

Add `EnsureConnected();` as the first line of every public method that calls `_bluetoothService.SendCommandAsync(...)`. This includes all of:
- `GetDeviceInfoAsync`, `GetBatteryAsync`, `PingAsync`
- `ListProfilesAsync`, `LoadProfileAsync`, `GetCurrentProfileAsync`, `SetCustomProfileAsync`
- `StartSessionAsync`, `PauseSessionAsync`, `ResumeSessionAsync`, `StopSessionAsync`, `GetSessionStatusAsync`
- `SetParameterAsync`
- `EnterCalibrationAsync`, `BuzzFingerAsync`, `ExitCalibrationAsync`
- `GetAvailableCommandsAsync`, `RestartDeviceAsync`
- `GetTherapyLedOffAsync`, `SetTherapyLedOffAsync`
- `GetDebugModeAsync`, `SetDebugModeAsync`

Do NOT add it to `SyncStateAsync` (which is a compound method that calls other methods — those methods have their own guards) or `ClearExpectingReboot()` (which doesn't send commands).

- [ ] **Step 3: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```
git add BuzzahBuddy/Services/Glove/GloveControlService.cs
git commit -m "fix(glove): add EnsureConnected pre-check to all command methods"
```

---

## Task 5: SettingsViewModel — Centralize State (V1) + Try-Catch (V7)

**Files:**
- Modify: `BuzzahBuddy/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add IConnectionStateService injection**

In the constructor (line 36), add `IConnectionStateService` parameter. Add a using directive for `BuzzahBuddy.Services.ConnectionState`. Store and expose it:

Add field and property:
```csharp
public IConnectionStateService ConnectionInfo { get; }
```

In the constructor, add the parameter and assignment:
```csharp
public SettingsViewModel(
    IBluetoothService bluetoothService,
    IDataStorageService storageService,
    IConnectionStateService connectionStateService)
{
    // ... existing assignments ...
    ConnectionInfo = connectionStateService;
```

- [ ] **Step 2: Remove local connection state properties**

Remove these `[ObservableProperty]` fields (lines 18-26):
- `_isConnected` (bool)
- `_connectedDeviceName` (string, default "None")
- `_connectionState` (ConnectionState)

- [ ] **Step 3: Remove UpdateConnectionInfo method and simplify OnConnectionStateChanged**

Remove the `UpdateConnectionInfo` method (lines 133-146) entirely — the centralized service handles state now.

Since `SettingsViewModel` has no side-effect logic on connection state changes (it only tracked state), remove the `OnConnectionStateChanged` handler (lines 148-154) and the event subscription from the constructor (line 46). Also remove the unsubscription from `Dispose` (lines 159-167 — remove only the `ConnectionStateChanged -=` line).

Remove the initial `UpdateConnectionInfo()` call from the constructor (line 50).

- [ ] **Step 4: Update IsConnected references in commands**

In `DisconnectDeviceAsync` (line 53), replace `if (!IsConnected)` with `if (!ConnectionInfo.IsConnected)`.

- [ ] **Step 5: Add try-catch around DisconnectAsync (V7)**

In `DisconnectDeviceAsync`, wrap the `_bluetoothService.DisconnectAsync()` call in try-catch:

```csharp
try
{
    await _bluetoothService.DisconnectAsync();
    await Shell.Current.DisplayAlert(
        "Disconnected",
        "Device has been disconnected.",
        "OK");
}
catch (Exception ex)
{
    await Shell.Current.DisplayAlert(
        "Disconnect Error",
        $"An error occurred while disconnecting: {ex.Message}",
        "OK");
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 7: Commit**

```
git add BuzzahBuddy/ViewModels/SettingsViewModel.cs
git commit -m "refactor(settings): centralize connection state via IConnectionStateService, add disconnect error handling"
```

---

## Task 6: CalibrationViewModel — Centralize State (V1) + Dispose (V2) + Disconnect Feedback (V3)

**Files:**
- Modify: `BuzzahBuddy/ViewModels/CalibrationViewModel.cs`

- [ ] **Step 1: Add IConnectionStateService injection**

Add using directive for `BuzzahBuddy.Services.ConnectionState`. Add to constructor parameters. Store and expose:

```csharp
public IConnectionStateService ConnectionInfo { get; }
```

- [ ] **Step 2: Remove local IsConnected property**

Remove the `[ObservableProperty] private bool _isConnected;` field (lines 20-21).

- [ ] **Step 3: Update UpdateConnectionState to use centralized service**

The `UpdateConnectionState` method (lines 275-286) currently sets `IsConnected` and handles calibration-mode exit on disconnect. Keep the calibration-mode exit logic but remove the `IsConnected` assignment:

```csharp
private void UpdateConnectionState()
{
    if (!ConnectionInfo.IsConnected && IsInCalibrationMode)
    {
        IsInCalibrationMode = false;
        SelectedFingerIndex = -1;
        SelectedFingerName = "None";

        // V3: Notify user that calibration was interrupted
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.DisplayAlert(
                "Device Disconnected",
                "Calibration mode has been exited because the device was disconnected.",
                "OK");
        });
    }
}
```

- [ ] **Step 4: Update IsConnected references in commands**

In `EnterCalibrationModeAsync` (line 74), replace `if (!IsConnected)` with `if (!ConnectionInfo.IsConnected)`.

- [ ] **Step 5: Add Dispose override (V2)**

Add a `Dispose(bool)` override to unsubscribe from the `ConnectionStateChanged` event:

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
    }
    base.Dispose(disposing);
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 7: Commit**

```
git add BuzzahBuddy/ViewModels/CalibrationViewModel.cs
git commit -m "refactor(calibration): centralize connection state, add Dispose, show disconnect alert during calibration"
```

---

## Task 7: DeviceListViewModel — Centralize State (V1) + Await Dialog (V8)

**Files:**
- Modify: `BuzzahBuddy/ViewModels/DeviceListViewModel.cs`

- [ ] **Step 1: Add IConnectionStateService injection**

Add using directive. Add to constructor parameters. Store and expose:

```csharp
public IConnectionStateService ConnectionInfo { get; }
```

- [ ] **Step 2: Remove local connection state properties**

Remove these `[ObservableProperty]` fields (lines 41-48):
- `_isAlreadyConnected` (bool)
- `_connectedDeviceName` (string?)
- `_connectionState` (ConnectionState)

Keep `_isConnecting` and `_connectingDeviceId` — these are local UI state for the connect-in-progress flow, not connection state.

- [ ] **Step 3: Simplify UpdateConnectionState**

The `UpdateConnectionState` method (lines 350-366) currently sets `ConnectionState`, `IsAlreadyConnected`, `ConnectedDeviceName`, `IsConnecting`, `ConnectingDeviceId`. Remove the first three assignments (now handled by centralized service). Keep the `IsConnecting`/`ConnectingDeviceId` reset logic:

```csharp
private void UpdateConnectionState()
{
    var state = _bluetoothService.CurrentConnectionState;

    if (state != Models.ConnectionState.Connecting)
    {
        IsConnecting = false;
        ConnectingDeviceId = null;
    }
}
```

- [ ] **Step 4: Update IsAlreadyConnected references**

Replace all `IsAlreadyConnected` references with `ConnectionInfo.IsConnected`:
- `DisconnectAsync` guard: `if (!ConnectionInfo.IsConnected)`
- `ConnectedDeviceName` references: use `ConnectionInfo.ConnectedDeviceName`

- [ ] **Step 5: Await connect success dialog (V8)**

In `ConnectAsync` (around line 287), change:
```csharp
_ = Shell.Current.DisplayAlert("Connected", ...);
```
to:
```csharp
await Shell.Current.DisplayAlert("Connected", ...);
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 7: Commit**

```
git add BuzzahBuddy/ViewModels/DeviceListViewModel.cs
git commit -m "refactor(devices): centralize connection state, await connect dialog before navigation"
```

---

## Task 8: GloveControlViewModel — Centralize State (V1) + Session Persist (V4) + Battery Refresh (V5) + Timer Dispose (V6)

**Files:**
- Modify: `BuzzahBuddy/ViewModels/GloveControlViewModel.cs`

This is the most complex ViewModel migration. Read the entire file before making changes.

- [ ] **Step 1: Add IConnectionStateService injection**

Add using directive. Add to constructor parameters (line 168). Store and expose:

```csharp
public IConnectionStateService ConnectionInfo { get; }
```

- [ ] **Step 2: Remove local connection state properties**

Remove these `[ObservableProperty]` fields (lines 127-151):
- `_isConnected` (bool)
- `_connectionState` (ConnectionState)
- `_connectedDeviceName` (string?)
- `_isReconnecting` (bool)
- `_reconnectionMessage` (string)

Keep all other ObservableProperties — they are local UI state.

- [ ] **Step 3: Add ShowReconnectionBanner as computed property**

Since `IsReconnecting` and `ReconnectionMessage` are now on `ConnectionInfo`, update `ShowReconnectionBanner` to read from the centralized service. It was previously a computed property based on local fields. Replace it with:

```csharp
public bool ShowReconnectionBanner =>
    ConnectionInfo.IsReconnecting || !string.IsNullOrEmpty(ConnectionInfo.ReconnectionMessage);
```

Subscribe to `ConnectionInfo.PropertyChanged` in the constructor to fire `OnPropertyChanged(nameof(ShowReconnectionBanner))` when `IsReconnecting` or `ReconnectionMessage` changes. **Important:** Unsubscribe from `ConnectionInfo.PropertyChanged` in `Dispose(bool)` to prevent the singleton service from holding a reference to the disposed transient ViewModel.

- [ ] **Step 4: Simplify UpdateConnectionState**

The `UpdateConnectionState` method (lines 725-741) currently sets `ConnectionState`, `IsConnected`, `ConnectedDeviceName`. Remove those assignments. Keep only the side-effect logic:

```csharp
private void UpdateConnectionState()
{
    if (ConnectionInfo.IsConnected)
    {
        RefreshBatteryAsync().SafeFireAndForget("[GLOVECONTROL]");
        StartConnectionHealthCheck();
    }
    else
    {
        StopConnectionHealthCheck();
        IsConnectionHealthy = false;
    }
}
```

- [ ] **Step 5: Simplify OnConnectionStateChanged**

The `OnConnectionStateChanged` handler (lines 743-785) currently sets `ConnectionState`, `IsConnected`, `ConnectedDeviceName` inline. Remove those assignments. Keep only the side-effect logic (timer management, session teardown, screen reader announcement).

**Critical:** Use the `state` parameter directly for all conditional checks — NOT `ConnectionInfo.IsConnected`. The `ConnectionStateService` also subscribes to the same event and updates via `BeginInvokeOnMainThread`. Since execution order of queued main-thread callbacks is not guaranteed, `ConnectionInfo.IsConnected` may still reflect the old value when this callback runs.

```csharp
private void OnConnectionStateChanged(object? sender, Models.ConnectionState state)
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        // Use the state parameter directly (not ConnectionInfo.IsConnected) because
        // ConnectionStateService updates via its own BeginInvokeOnMainThread callback,
        // which may not have executed yet when this callback runs.
        var isConnected = state == Models.ConnectionState.Connected;

        if (isConnected)
        {
            StartConnectionHealthCheck();
            // V5: Refresh battery on reconnect
            RefreshBatteryAsync().SafeFireAndForget("[GLOVECONTROL]");
        }
        else
        {
            StopConnectionHealthCheck();
            IsConnectionHealthy = false;
        }

        // Handle session teardown on disconnect
        if (!isConnected && IsSessionActive)
        {
            StopStatusPolling();
            SessionStatus = SessionStatus.CreateIdle();
            UpdateSessionState();

            // V4: Persist incomplete session before nulling
            if (_currentSession != null)
            {
                _currentSession.EndTime = DateTime.Now;
                _currentSession.IsCompleted = false;
                _storageService.SaveSessionAsync(_currentSession).SafeFireAndForget("[GLOVECONTROL]");
                _currentSession = null;
            }
        }

        var announcement = state switch
        {
            Models.ConnectionState.Connected => "Device connected",
            Models.ConnectionState.Disconnected => "Device disconnected",
            Models.ConnectionState.Error => "Device connection lost",
            Models.ConnectionState.Reconnecting => "Attempting to reconnect",
            _ => null
        };
        if (announcement != null)
        {
            SemanticScreenReader.Announce(announcement);
        }
    });
}
```

- [ ] **Step 6: Update IsConnected references in all commands**

Replace all `IsConnected` references with `ConnectionInfo.IsConnected`:
- `ToggleSessionAsync` guard (line 195)
- `TestConnectionAsync` guard (line 370)
- `RefreshBatteryAsync` guard (line 411)
- `UpdateSessionStatusAsync` guard (line 562)
- `CheckConnectionHealthAsync` guard (line 678)
- Any other references throughout the file

- [ ] **Step 7: Remove OnReconnectionStateChanged handler**

The reconnection state is now handled by `ConnectionStateService`. Remove the `OnReconnectionStateChanged` handler and its subscription from the constructor. Remove the unsubscription from `Dispose`.

However, if the handler has side-effect logic (e.g., restarting status polling), keep that logic but trigger it from the `ConnectionInfo.PropertyChanged` subscription instead.

- [ ] **Step 8: Dispose timers and unsubscribe from ConnectionInfo (V6)**

In the `Dispose(bool)` override (lines 900-911), add timer disposal after stopping them, and unsubscribe from `ConnectionInfo.PropertyChanged`:

```csharp
StopStatusPolling();
_statusPollTimer?.Dispose();
StopConnectionHealthCheck();
_healthCheckTimer?.Dispose();
((INotifyPropertyChanged)ConnectionInfo).PropertyChanged -= OnConnectionInfoPropertyChanged;
```

- [ ] **Step 9: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 10: Commit**

```
git add BuzzahBuddy/ViewModels/GloveControlViewModel.cs
git commit -m "refactor(control): centralize connection state, persist sessions on disconnect, refresh battery on reconnect, dispose timers"
```

---

## Task 9: MainPageViewModel — Centralize State (V1)

**Files:**
- Modify: `BuzzahBuddy/ViewModels/MainPageViewModel.cs`

- [ ] **Step 1: Add IConnectionStateService injection**

Add using directive. Add to constructor parameters (line 197). Store and expose:

```csharp
public IConnectionStateService ConnectionInfo { get; }
```

- [ ] **Step 2: Remove local connection state properties**

Remove these `[ObservableProperty]` fields (lines 26-54):
- `_connectionState` (ConnectionState)
- `_isConnected` (bool)
- `_connectedDeviceName` (string)
- `_isReconnecting` (bool)
- `_reconnectionMessage` (string)

Keep all `DashboardState`-related properties — these are computed from connection state but are distinct UI state.

- [ ] **Step 3: Update UpdateDashboardStateAsync**

The `UpdateDashboardStateAsync` method (lines 369-414) reads `ConnectionState`, `IsConnected`, and `ConnectedDeviceName` from local fields. Update to read from `ConnectionInfo` instead:

- Replace `ConnectionState` reads with `ConnectionInfo.ConnectionState`
- Replace `IsConnected` reads with `ConnectionInfo.IsConnected`
- Replace `ConnectedDeviceName` assignments with reads from `ConnectionInfo.ConnectedDeviceName`

- [ ] **Step 4: Update ShowReconnectionBanner**

If `ShowReconnectionBanner` was computed from local `IsReconnecting` and `ReconnectionMessage`, update to read from `ConnectionInfo.IsReconnecting` and `ConnectionInfo.ReconnectionMessage`.

Subscribe to `ConnectionInfo.PropertyChanged` in the constructor to trigger `OnPropertyChanged(nameof(ShowReconnectionBanner))` when relevant properties change. **Important:** Unsubscribe from `ConnectionInfo.PropertyChanged` in `Dispose(bool)` to prevent the singleton service from holding a reference to the disposed transient ViewModel.

- [ ] **Step 5: Update IsConnected references in commands**

Replace `if (IsConnected)` in `RefreshAsync` (line 278) with `if (ConnectionInfo.IsConnected)`.

- [ ] **Step 6: Remove OnReconnectionStateChanged handler**

The reconnection state is now handled by `ConnectionStateService`. Remove the handler and its subscription. Keep any side-effect logic triggered by reconnection (e.g., screen reader announcements) and move it to a `ConnectionInfo.PropertyChanged` handler.

- [ ] **Step 7: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 8: Commit**

```
git add BuzzahBuddy/ViewModels/MainPageViewModel.cs
git commit -m "refactor(main): centralize connection state via IConnectionStateService"
```

---

## Task 10: GloveControlPage.xaml — Disable Buttons (X1) + Battery Disconnected State (X2)

**Files:**
- Modify: `BuzzahBuddy/Views/GloveControlPage.xaml`

- [ ] **Step 1: Update Session Control button IsEnabled (X1)**

Find the primary session control button (around line 390). Change its `IsEnabled` from:
```xml
IsEnabled="{Binding IsNotBusy}"
```
to a `MultiBinding` with `AllTrueConverter`:
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="IsNotBusy" />
        <Binding Path="ConnectionInfo.IsConnected" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 2: Update Stop Session button IsEnabled**

Find the stop session button (around line 409). Apply the same pattern:
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="IsNotBusy" />
        <Binding Path="ConnectionInfo.IsConnected" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 3: Update Test Connection button IsEnabled**

**Important:** In .NET MAUI, `Converter=` on a child `Binding` inside a `MultiBinding` is **silently ignored** at runtime (unlike WPF). Each child binding passes its raw unconverted value to the `IMultiValueConverter`. To work around this, the ViewModel must expose pre-inverted boolean properties.

First, add computed properties to `GloveControlViewModel`:
```csharp
public bool IsNotTestingConnection => !IsTestingConnection;

partial void OnIsTestingConnectionChanged(bool value)
{
    OnPropertyChanged(nameof(IsNotTestingConnection));
}
```

Then update the button (around line 375):
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="IsNotTestingConnection" />
        <Binding Path="ConnectionInfo.IsConnected" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 4: Update Refresh Battery button IsEnabled**

Same pattern — add computed property to `GloveControlViewModel`:
```csharp
public bool IsNotRefreshingBattery => !IsRefreshingBattery;

partial void OnIsRefreshingBatteryChanged(bool value)
{
    OnPropertyChanged(nameof(IsNotRefreshingBattery));
}
```

Then update the button (around line 166):
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="IsNotRefreshingBattery" />
        <Binding Path="ConnectionInfo.IsConnected" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 5: Add battery card disconnected state (X2)**

Find the battery status card Border (around line 89). Inside the card's `VerticalStackLayout`, add a disconnected overlay that shows when not connected:

After the battery content, add:
```xml
<!-- Disconnected state overlay -->
<Label Text="Connect to a device to view battery status"
       IsVisible="{Binding ConnectionInfo.IsConnected, Converter={StaticResource InvertedBoolConverter}}"
       Style="{StaticResource BodyText}"
       TextColor="{StaticResource TextMuted}"
       HorizontalTextAlignment="Center"
       Margin="0,8,0,0"
       SemanticProperties.Description="Connect to a device to view battery status" />
```

Also add `IsVisible="{Binding ConnectionInfo.IsConnected}"` to the battery content elements (the `HorizontalStackLayout` with battery dots and the refresh stack) so they hide when disconnected.

- [ ] **Step 6: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 7: Commit**

```
git add BuzzahBuddy/Views/GloveControlPage.xaml
git commit -m "fix(control-ui): disable buttons when disconnected, show battery disconnected state"
```

---

## Task 11: CalibrationPage.xaml — Connection Guards (X3)

**Files:**
- Modify: `BuzzahBuddy/Views/CalibrationPage.xaml`

- [ ] **Step 1: Add not-connected info card at top**

Before the "Enter Calibration" button (around line 39), add a banner:

```xml
<!-- Not Connected Banner -->
<Border Padding="16"
        BackgroundColor="{StaticResource CardBackground}"
        StrokeThickness="0"
        IsVisible="{Binding ConnectionInfo.IsConnected, Converter={StaticResource InvertedBoolConverter}}"
        Margin="0,0,0,12">
    <Border.StrokeShape>
        <RoundRectangle CornerRadius="8" />
    </Border.StrokeShape>
    <HorizontalStackLayout Spacing="12">
        <Label Text="ℹ"
               FontSize="24"
               VerticalOptions="Center"
               AutomationProperties.IsInAccessibleTree="False" />
        <Label Text="Connect to a BlueBuzzah device to begin calibration."
               Style="{StaticResource BodyText}"
               TextColor="{StaticResource TextMuted}"
               LineBreakMode="WordWrap"
               VerticalOptions="Center"
               SemanticProperties.Description="Connect to a BlueBuzzah device to begin calibration" />
    </HorizontalStackLayout>
</Border>
```

- [ ] **Step 2: Add IsConnected guard to Enter Calibration button**

Find the Enter Calibration button (around line 39). Change its `IsEnabled` from:
```xml
IsEnabled="{Binding IsInCalibrationMode, Converter={StaticResource InvertedBoolConverter}}"
```
to:
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="IsInCalibrationMode" Converter="{StaticResource InvertedBoolConverter}" />
        <Binding Path="ConnectionInfo.IsConnected" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 3: Add IsEnabled to all finger-test buttons**

For each of the 8 finger-test buttons (BuzzFingerCommand, lines 241-384), add:
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="ConnectionInfo.IsConnected" />
        <Binding Path="IsInCalibrationMode" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 4: Add IsEnabled to all intensity and duration buttons**

Per spec X3 ("same pattern" as finger-test buttons), intensity and duration buttons need BOTH `IsConnected` AND `IsInCalibrationMode` guards. For all intensity buttons (SetIntensityCommand, lines 99-146) and duration buttons (SetDurationCommand, lines 165-202), add:
```xml
<Button.IsEnabled>
    <MultiBinding Converter="{StaticResource AllTrueConverter}">
        <Binding Path="ConnectionInfo.IsConnected" />
        <Binding Path="IsInCalibrationMode" />
    </MultiBinding>
</Button.IsEnabled>
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 6: Commit**

```
git add BuzzahBuddy/Views/CalibrationPage.xaml
git commit -m "fix(calibration-ui): add connection guards to all controls, show not-connected banner"
```

---

## Task 12: SettingsPage.xaml — Instructional Text (X4)

**Files:**
- Modify: `BuzzahBuddy/Views/SettingsPage.xaml`

- [ ] **Step 1: Add instructional text when disconnected**

After the Disconnect button (around line 51), or in the area where the Calibrate/Disconnect buttons appear, add a label visible only when disconnected:

```xml
<!-- Instructional text when disconnected -->
<Label Text="Connect to a device to access calibration and device settings."
       IsVisible="{Binding ConnectionInfo.IsConnected, Converter={StaticResource InvertedBoolConverter}}"
       Style="{StaticResource BodyText}"
       TextColor="{StaticResource TextMuted}"
       Margin="0,8,0,0"
       SemanticProperties.Description="Connect to a device to access calibration and device settings" />
```

- [ ] **Step 2: Update IsConnected references in XAML**

Replace any existing `{Binding IsConnected}` references with `{Binding ConnectionInfo.IsConnected}`:
- Calibrate button `IsVisible` (line 41)
- Disconnect button `IsVisible` (line 51)

- [ ] **Step 3: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```
git add BuzzahBuddy/Views/SettingsPage.xaml
git commit -m "fix(settings-ui): add instructional text when device not connected"
```

---

## Task 13: MainPage.xaml — Battery Disconnected State (X5)

**Files:**
- Modify: `BuzzahBuddy/Views/MainPage.xaml`

- [ ] **Step 1: Update battery section in idle state card**

Find the idle state battery card (around lines 240-323, with `IsVisible="{Binding ShowBatteryStatus}"`). Within the battery content, add a disconnected state message and gate the live battery values on `IsConnected`:

Add inside the card, alongside the existing battery content:
```xml
<!-- Disconnected state -->
<Label Text="Connect to view battery status"
       IsVisible="{Binding ConnectionInfo.IsConnected, Converter={StaticResource InvertedBoolConverter}}"
       Style="{StaticResource BodyText}"
       TextColor="{StaticResource TextMuted}"
       HorizontalTextAlignment="Center"
       SemanticProperties.Description="Connect to view battery status" />
```

Gate the existing battery percentage/color displays so they only show when connected by wrapping them or adding `IsVisible="{Binding ConnectionInfo.IsConnected}"` to the battery row container.

- [ ] **Step 2: Update IsConnected references in XAML**

Replace any existing `{Binding IsConnected, ...}` references with `{Binding ConnectionInfo.IsConnected, ...}`:
- Welcome Guide visibility (around line 55): change `IsConnected` to `ConnectionInfo.IsConnected`

- [ ] **Step 3: Build and verify**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```
git add BuzzahBuddy/Views/MainPage.xaml
git commit -m "fix(main-ui): show battery disconnected state, update bindings to centralized service"
```

---

## Task 14: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -v q`
Expected: 0 errors, minimal warnings (XC0023 for the profile TapGestureRecognizer x:DataType=null is expected)

- [ ] **Step 2: Run all tests**

Run: `dotnet test` (if test project exists)

- [ ] **Step 3: Verify no stale IsConnected references**

Search for any remaining direct `IsConnected` bindings in XAML that should use `ConnectionInfo.IsConnected`:
```
grep -rn "Binding IsConnected" BuzzahBuddy/Views/
grep -rn "Binding IsAlreadyConnected" BuzzahBuddy/Views/
```

Search for any remaining local `_isConnected` fields in ViewModels:
```
grep -rn "_isConnected\|_connectionState\|_connectedDeviceName" BuzzahBuddy/ViewModels/
```

Expected: No matches (all migrated to centralized service).

- [ ] **Step 4: Verify all XAML pages reference ConnectionInfo**

Check that every page that needs connection state uses `ConnectionInfo.IsConnected`:
```
grep -rn "ConnectionInfo" BuzzahBuddy/Views/
```

Expected: Matches in GloveControlPage.xaml, CalibrationPage.xaml, SettingsPage.xaml, MainPage.xaml, DeviceListPage.xaml.
