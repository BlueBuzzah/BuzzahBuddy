# Connection State Consistency — Full Architecture Audit & Fix

**Date:** 2026-03-24
**Status:** Approved
**Scope:** Full architecture audit — service layer, ViewModel layer, XAML/UX layer

## Problem

The BuzzahBuddy app has widespread inconsistencies in how device connection state is tracked, propagated, and reflected in the UI. Five ViewModels independently maintain their own `IsConnected`, `ConnectionState`, and `ConnectedDeviceName` properties by subscribing to `IBluetoothService.ConnectionStateChanged` and reading service state. This decentralized approach causes:

- Silent command failures (user taps a button, nothing happens)
- Stale UI state after disconnect (battery values, session status)
- Missing visual disabled states on buttons that require connection
- Entire pages with zero connection guards (CalibrationPage)
- Event subscription leaks (CalibrationViewModel never unsubscribes)
- Race conditions in service-layer state transitions

## Design

### Layer 1: Centralized Connection State Service

**New `IConnectionStateService` interface:**

```csharp
public interface IConnectionStateService : INotifyPropertyChanged
{
    ConnectionState ConnectionState { get; }
    bool IsConnected { get; }
    string? ConnectedDeviceName { get; }
    bool IsReconnecting { get; }
    string? ReconnectionMessage { get; }
}
```

**Implementation:** `ConnectionStateService` is a singleton that subscribes to `IBluetoothService.ConnectionStateChanged` and `IReconnectionService.ReconnectionStateChanged`. It maintains the canonical connection state and fires `PropertyChanged` on the main thread. All ViewModels inject this service instead of independently tracking state.

**Registration:** `AddSingleton<IConnectionStateService, ConnectionStateService>` in `MauiProgram.cs`.

**ViewModel binding pattern:** Each ViewModel removes its local `_connectionState`, `_isConnected`, `_connectedDeviceName` observable properties. ViewModels expose the service as a public property (`public IConnectionStateService ConnectionInfo { get; }`) and XAML binds via `{Binding ConnectionInfo.IsConnected}`. This preserves the ViewModel-as-binding-context MVVM pattern while avoiding duplicated forwarding properties. ViewModels retain `IBluetoothService.ConnectionStateChanged` subscriptions only for side-effect logic (timers, session cleanup) — NOT for state tracking.

### Layer 2: Service-Layer Fixes

**S1. `BluetoothService.OnDeviceConnectionLost` — null `ConnectedDevice`**
Add `ConnectedDevice = null;` **before** `UpdateConnectionState(ConnectionState.Error)` — same ordering as `OnDeviceDisconnected` to ensure any event handler that reads `ConnectedDevice` synchronously sees `null`. Currently leaves a stale device reference after unexpected BLE drop.

**S2. Cancel pending command TCS on disconnect**
In `OnDeviceDisconnected` and `OnDeviceConnectionLost`, add cleanup in this exact order: (1) `_responseBuffer.Clear();` first to prevent stale data, then (2) `_pendingResponseTcs?.TrySetCanceled();` to unblock any waiting caller. This ordering prevents a race where a concurrent `OnRxCharacteristicValueUpdated` could append to the buffer between TCS cancellation and buffer clear. Gives callers an immediate `OperationCanceledException` instead of waiting the full timeout (up to 5s of unresponsive UI).

**S3.** *(Merged into S2 — `_responseBuffer.Clear()` is part of the disconnect cleanup sequence above.)*

**S4. Fix spurious reconnect on first-connect failure**
In `ConnectToDeviceAsync`, when service/characteristic discovery fails, set `_userInitiatedDisconnect = true` before calling `DisconnectAsync()`, then reset after. Prevents `ReconnectionService` from launching a 20-attempt reconnect loop for a first-connect failure.

**S5. `GloveControlService` connection pre-check**
Add a private `EnsureConnected()` helper that throws `InvalidOperationException` when `CurrentConnectionState != Connected`. Call at the top of every command method for consistent early validation.

**S6. MockBluetoothService state parity**
Emit `Connecting` in `ConnectToDeviceAsync` and `Reconnecting` in `ConnectToLastKnownDeviceAsync` to match real service state transitions. Also fix `DisconnectForReconnectAsync` to assign `CurrentConnectionState` **before** firing `ConnectionStateChanged` (currently fires the event with stale state). Currently skips intermediate states that the real service emits.

### Layer 3: ViewModel Fixes

**V1. All ViewModels — Replace local connection state with `IConnectionStateService`**
Remove duplicate `_connectionState`, `_isConnected`, `_connectedDeviceName` from MainPageViewModel, DeviceListViewModel, GloveControlViewModel, CalibrationViewModel, SettingsViewModel. Inject `IConnectionStateService` and expose it as `public IConnectionStateService ConnectionInfo { get; }`. XAML binds via `{Binding ConnectionInfo.IsConnected}`. ViewModels that need side-effect logic on state changes (GloveControlViewModel, CalibrationViewModel, MainPageViewModel) retain `IBluetoothService.ConnectionStateChanged` subscriptions for that purpose only — they no longer set any local state properties in those handlers.

**V2. `CalibrationViewModel` — Add `Dispose` override**
Currently leaks `ConnectionStateChanged` subscription on every navigation. Add `Dispose(bool)` override that unsubscribes from `IBluetoothService.ConnectionStateChanged` (retained for side-effect logic: auto-exiting calibration mode on disconnect).

**V3. `CalibrationViewModel` — User feedback on disconnect during calibration**
Show `DisplayAlert` when calibration is auto-exited due to disconnect. Currently resets silently.

**V4. `GloveControlViewModel` — Persist incomplete sessions on disconnect**
Call `_storageService.SaveSessionAsync(_currentSession)` in `OnConnectionStateChanged` when `!IsConnected && IsSessionActive`, then set `_currentSession = null` to prevent `StopSessionAsync` from re-saving it as a duplicate completed session on reconnect. Currently, incomplete sessions are silently lost.

**V5. `GloveControlViewModel` — Refresh battery on reconnect**
Fire `RefreshBatteryAsync().SafeFireAndForget(...)` when `OnConnectionStateChanged` transitions to `Connected`. Currently only done in constructor.

**V6. `GloveControlViewModel` — Dispose timers properly**
Call `_statusPollTimer?.Dispose()` and `_healthCheckTimer?.Dispose()` in `Dispose(bool)`. `System.Timers.Timer` implements `IDisposable`.

**V7. `SettingsViewModel` — Add try-catch around `DisconnectAsync`**
Match the error-handling pattern in `DeviceListViewModel.DisconnectAsync`.

**V8. `DeviceListViewModel` — Await the connect success dialog**
Change `_ = Shell.Current.DisplayAlert(...)` to `await Shell.Current.DisplayAlert(...)` so the dialog completes before navigation, preventing it from appearing on the wrong page.

### Layer 4: XAML/UX Fixes

**X1. `GloveControlPage` — Disable buttons when disconnected**
Add `IsEnabled` bindings incorporating `IsConnected` to: session control button, test connection button, refresh battery button. Provides immediate visual feedback instead of "tap and get a dialog."

**X2. `GloveControlPage` — Battery card disconnected state**
Show "Connect to view battery status" when not connected instead of stale/zero values.

**X3. `CalibrationPage` — Add connection guards to all controls**
- "Enter Calibration" button: `IsEnabled` bound to `IsConnected`
- All 8 finger-test buttons: `IsEnabled` bound to `IsConnected AND IsInCalibrationMode`
- Intensity/duration buttons: same pattern
- Add "not connected" info card at top when disconnected

**X4. `SettingsPage` — Add instructional text when disconnected**
Show "Connect to a device to access calibration and device settings" when Calibrate/Disconnect buttons are hidden.

**X5. `MainPage` — Battery section disconnected state**
Show contextual messaging instead of stale values when device is not connected.

## Files Changed

### New Files
- `Services/ConnectionState/IConnectionStateService.cs`
- `Services/ConnectionState/ConnectionStateService.cs`

### Modified Files
- `MauiProgram.cs` — register new service
- `Services/Bluetooth/BluetoothService.cs` — S1, S2, S3, S4
- `Services/Bluetooth/MockBluetoothService.cs` — S6
- `Services/Glove/GloveControlService.cs` — S5
- `ViewModels/MainPageViewModel.cs` — V1
- `ViewModels/DeviceListViewModel.cs` — V1, V8
- `ViewModels/GloveControlViewModel.cs` — V1, V4, V5, V6
- `ViewModels/CalibrationViewModel.cs` — V1, V2, V3
- `ViewModels/SettingsViewModel.cs` — V1, V7
- `Views/GloveControlPage.xaml` — X1, X2
- `Views/CalibrationPage.xaml` — X3
- `Views/SettingsPage.xaml` — X4
- `Views/MainPage.xaml` — X5

## Testing Strategy

- Unit test `ConnectionStateService` — verify: `PropertyChanged` fires on main thread, `IsConnected` accurately reflects `ConnectionState == Connected`, reconnection state propagation from `IReconnectionService`
- Verify all existing unit tests still pass after refactoring
- Test each ViewModel's connection-dependent commands with mock service in both connected and disconnected states
- Test disconnect during active calibration wizard
- Test disconnect during active therapy session (verify session is persisted)
- Test reconnect after disconnect (verify battery refresh, timer restart)
- Test first-connect failure (verify no spurious reconnect loop)
- Test pending command cancellation on disconnect (verify immediate failure, not timeout)
- Visual audit of all 5 pages in disconnected state (verify disabled buttons, info messages)

## Success Criteria

- Zero silent command failures — every user action gives visual feedback
- Single source of truth for connection state — no ViewModel independently tracks it
- Consistent disabled/enabled button states across all pages
- CalibrationPage is fully connection-aware
- Incomplete sessions survive disconnects
- Battery status refreshes on reconnect
- No event subscription leaks
- Mock and real service emit identical state transitions
