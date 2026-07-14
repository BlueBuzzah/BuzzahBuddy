using BuzzahBuddy.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using System.Collections.Concurrent;
using System.Text;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Service for managing Bluetooth Low Energy communication with BlueBuzzah gloves.
/// Uses Nordic UART Service (NUS) for text-based command/response protocol.
/// </summary>
public class BluetoothService : IBluetoothService
{
    private readonly IAdapter _adapter;
    private readonly IBluetoothLE _bluetoothLE;
    private IDevice? _connectedBleDevice;
    private ICharacteristic? _txCharacteristic;  // Write - App → Glove
    private ICharacteristic? _rxCharacteristic;  // Notify - Glove → App
    private readonly ConcurrentDictionary<string, GloveDevice> _discoveredDevices = new();
    private readonly ConcurrentDictionary<string, IDevice> _discoveredBleDevices = new();

    // Response buffering and parsing
    private readonly RxFrameAssembler _rxAssembler = new();
    private readonly SemaphoreSlim _responseLock = new(1, 1);
    private TaskCompletionSource<CommandResponse>? _pendingResponseTcs;

    // Reconnection state
    private string? _lastConnectedDeviceId;
    private bool _userInitiatedDisconnect;

    public ConnectionState CurrentConnectionState { get; private set; } = ConnectionState.Disconnected;
    public GloveDevice? ConnectedDevice { get; private set; }
    public string? LastConnectedDeviceId => _lastConnectedDeviceId;
    public bool UserInitiatedDisconnect => _userInitiatedDisconnect;

    public event EventHandler<GloveDevice>? DeviceDiscovered;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;
    public event EventHandler<CommandResponse>? ResponseReceived;

    public BluetoothService()
    {
        _bluetoothLE = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.DeviceConnected += OnDeviceConnected;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLost;

        // Diagnostic logging for debugging BLE issues
        System.Diagnostics.Debug.WriteLine($"[BLE] Bluetooth state: {_bluetoothLE.State}");
        System.Diagnostics.Debug.WriteLine($"[BLE] IsOn: {_bluetoothLE.IsOn}, IsAvailable: {_bluetoothLE.IsAvailable}");
        System.Diagnostics.Debug.WriteLine("[BLE] Event handlers registered");
    }

    public async Task<IEnumerable<GloveDevice>> ScanForDevicesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine("[BLE SCAN] === Starting scan sequence ===");
        _discoveredDevices.Clear();
        _discoveredBleDevices.Clear();
        System.Diagnostics.Debug.WriteLine("[BLE SCAN] Cleared device caches");

        System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Checking Bluetooth... State={_bluetoothLE.State}, IsOn={_bluetoothLE.IsOn}");
        if (!await IsBluetoothEnabledAsync())
        {
            System.Diagnostics.Debug.WriteLine("[BLE SCAN] Bluetooth DISABLED - aborting scan");
            return Enumerable.Empty<GloveDevice>();
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Starting scan, timeout={timeout.TotalSeconds}s");
            _adapter.ScanTimeout = (int)timeout.TotalMilliseconds;
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Adapter.ScanTimeout set to: {_adapter.ScanTimeout}ms");
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Adapter.IsScanning before: {_adapter.IsScanning}");
            System.Diagnostics.Debug.WriteLine("[BLE SCAN] Calling StartScanningForDevicesAsync...");
            await _adapter.StartScanningForDevicesAsync(cancellationToken: cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Scan complete. Found {_discoveredDevices.Count} matching device(s)");
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled by user - this is expected, rethrow to let caller handle
            System.Diagnostics.Debug.WriteLine("[BLE SCAN] Scan cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Scan error: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Stack trace: {ex.StackTrace}");
        }

        System.Diagnostics.Debug.WriteLine($"[BLE SCAN] Returning {_discoveredDevices.Count} device(s) to caller");
        return _discoveredDevices.Values;
    }

    public async Task StopScanAsync()
    {
        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }
    }

    public async Task<bool> ConnectToDeviceAsync(
        GloveDevice device,
        CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"🔌 Attempting to connect to device: {device.Name} ({device.Id})");
            UpdateConnectionState(ConnectionState.Connecting);

            // Find the BLE device - first check our cached discovered devices
            IDevice? bleDevice = null;
            if (_discoveredBleDevices.TryGetValue(device.Id, out var cachedDevice))
            {
                bleDevice = cachedDevice;
                System.Diagnostics.Debug.WriteLine($"✅ Found device in cache");
            }
            else
            {
                // Fallback to adapter's lists
                bleDevice = _adapter.ConnectedDevices.FirstOrDefault(d => d.Id.ToString() == device.Id)
                    ?? _adapter.DiscoveredDevices.FirstOrDefault(d => d.Id.ToString() == device.Id);
                System.Diagnostics.Debug.WriteLine($"Searched adapter lists, found: {bleDevice != null}");
            }

            if (bleDevice == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Device not found for connection");
                UpdateConnectionState(ConnectionState.Error);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"📡 Connecting to BLE device...");
            var connectParameters = new ConnectParameters(autoConnect: false, forceBleTransport: true);
            await _adapter.ConnectToDeviceAsync(bleDevice, connectParameters, cancellationToken);

            _connectedBleDevice = bleDevice;
            System.Diagnostics.Debug.WriteLine($"✅ BLE connection established");

            // Discover Nordic UART Service and get TX/RX characteristics
            System.Diagnostics.Debug.WriteLine($"🔍 Discovering NUS service...");
            var service = await bleDevice.GetServiceAsync(BlueBuzzahConstants.NordicUartServiceUuid);
            if (service == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Nordic UART Service not found");
                // Prevent ReconnectionService from starting a reconnect loop for a first-connect failure
                _userInitiatedDisconnect = true;
                await DisconnectAsync();
                _userInitiatedDisconnect = false;
                UpdateConnectionState(ConnectionState.Error);
                return false;
            }
            System.Diagnostics.Debug.WriteLine($"✅ NUS service found");

            System.Diagnostics.Debug.WriteLine($"🔍 Getting TX/RX characteristics...");
            _txCharacteristic = await service.GetCharacteristicAsync(BlueBuzzahConstants.TxCharacteristicUuid);
            _rxCharacteristic = await service.GetCharacteristicAsync(BlueBuzzahConstants.RxCharacteristicUuid);

            if (_txCharacteristic == null || _rxCharacteristic == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ TX or RX characteristic not found (TX: {_txCharacteristic != null}, RX: {_rxCharacteristic != null})");
                // Prevent ReconnectionService from starting a reconnect loop for a first-connect failure
                _userInitiatedDisconnect = true;
                await DisconnectAsync();
                _userInitiatedDisconnect = false;
                UpdateConnectionState(ConnectionState.Error);
                return false;
            }
            System.Diagnostics.Debug.WriteLine($"✅ TX/RX characteristics found");

            // Subscribe to RX notifications automatically
            System.Diagnostics.Debug.WriteLine($"📬 Subscribing to notifications...");
            await SubscribeToNotificationsAsync();
            System.Diagnostics.Debug.WriteLine($"✅ Subscribed to notifications");

            // Identify ourselves so the firmware types this connection as PHONE
            // without waiting out its identification window. Best-effort: if this
            // write fails or the firmware predates IDENTIFY, the firmware's
            // IDENTIFY_TIMEOUT_MS (1s) window expires and classifies the connection
            // as PHONE anyway; a late IDENTIFY is silently consumed.
            try
            {
                var identify = Encoding.UTF8.GetBytes("IDENTIFY:PHONE" + BlueBuzzahConstants.CommandTerminator);
                await _txCharacteristic!.WriteAsync(identify);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IDENTIFY:PHONE write failed (non-fatal): {ex.Message}");
            }

            ConnectedDevice = device;
            device.ConnectionState = ConnectionState.Connected;
            device.LastConnected = DateTime.Now;

            UpdateConnectionState(ConnectionState.Connected);
            _lastConnectedDeviceId = device.Id;
            _userInitiatedDisconnect = false;
            System.Diagnostics.Debug.WriteLine($"🎉 Successfully connected to {device.Name}");
            return true;
        }
        catch (DeviceConnectionException ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Connection error: {ex.Message}");
            UpdateConnectionState(ConnectionState.Error);
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Unexpected error during connection: {ex.GetType().Name}: {ex.Message}");
            UpdateConnectionState(ConnectionState.Error);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connectedBleDevice != null)
        {
            try
            {
                _userInitiatedDisconnect = true;

                // Unsubscribe from notifications
                if (_rxCharacteristic != null)
                {
                    await UnsubscribeFromNotificationsAsync();
                }

                await _adapter.DisconnectDeviceAsync(_connectedBleDevice);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Disconnect error: {ex.Message}");
            }
            finally
            {
                _connectedBleDevice = null;
                _txCharacteristic = null;
                _rxCharacteristic = null;
                ConnectedDevice = null;
                UpdateConnectionState(ConnectionState.Disconnected);
            }
        }
    }

    /// <summary>
    /// Key each command's response must contain. The protocol has no correlation IDs,
    /// so a late/stray frame would otherwise resolve the wrong command and shift every
    /// subsequent response by one (permanent desync). A resolved frame missing its
    /// command's expected key is discarded as stale and the wait continues.
    /// Commands absent from this map accept any frame. Error frames always match.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedResponseKeys = new()
    {
        ["INFO"] = "ROLE",
        ["BATTERY"] = "BATP",
        ["PING"] = "PONG",
        ["PROFILE_LIST"] = "PROFILE",
        ["PROFILE_GET"] = "TYPE",
        ["PROFILE_LOAD"] = "STATUS",
        ["SESSION_START"] = "SESSION_STATUS",
        ["SESSION_PAUSE"] = "SESSION_STATUS",
        ["SESSION_RESUME"] = "SESSION_STATUS",
        ["SESSION_STOP"] = "SESSION_STATUS",
        ["SESSION_STATUS"] = "SESSION_STATUS",
        ["THERAPY_LED_OFF"] = "THERAPY_LED_OFF",
    };

    public async Task<CommandResponse> SendCommandAsync(
        string command,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        if (_txCharacteristic == null || _connectedBleDevice == null)
        {
            throw new InvalidOperationException("Not connected to device");
        }

        await _responseLock.WaitAsync(cancellationToken);
        try
        {
            // Clear any previous response buffer
            _rxAssembler.Reset();

            // Create task completion source for this command
            _pendingResponseTcs = new TaskCompletionSource<CommandResponse>();

            // Send command (add \n terminator)
            System.Diagnostics.Debug.WriteLine($"[BLE CMD] >>> {command}");
            var commandBytes = Encoding.UTF8.GetBytes(command + BlueBuzzahConstants.CommandTerminator);
            await _txCharacteristic.WriteAsync(commandBytes);

            // Wait for response with timeout
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            ExpectedResponseKeys.TryGetValue(command.Split(':')[0], out var expectedKey);

            try
            {
                while (true)
                {
                    var response = await _pendingResponseTcs.Task.WaitAsync(linkedCts.Token);

                    if (expectedKey != null && !response.IsError && response.GetString(expectedKey) == null)
                    {
                        // Stale frame from an earlier timed-out command — drop it and
                        // keep waiting for this command's real response.
                        System.Diagnostics.Debug.WriteLine(
                            $"[BLE] Discarded stale response for '{command}' (missing {expectedKey})");
                        _pendingResponseTcs = new TaskCompletionSource<CommandResponse>();
                        continue;
                    }

                    // Add recommended delay between commands (100ms per spec)
                    await Task.Delay(BlueBuzzahConstants.CommandDelayMs, cancellationToken);

                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.Token.IsCancellationRequested)
                {
                    throw new TimeoutException($"Command '{command}' timed out after {timeoutMs}ms");
                }
                throw;
            }
        }
        finally
        {
            _pendingResponseTcs = null;
            _responseLock.Release();
        }
    }

    public async Task SubscribeToNotificationsAsync()
    {
        if (_rxCharacteristic == null)
        {
            throw new InvalidOperationException("RX characteristic not available");
        }

        _rxCharacteristic.ValueUpdated += OnRxCharacteristicValueUpdated;
        await _rxCharacteristic.StartUpdatesAsync();
    }

    public async Task UnsubscribeFromNotificationsAsync()
    {
        if (_rxCharacteristic != null)
        {
            try
            {
                _rxCharacteristic.ValueUpdated -= OnRxCharacteristicValueUpdated;
                await _rxCharacteristic.StopUpdatesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unsubscribe error: {ex.Message}");
            }
        }
    }

    public Task<bool> IsBluetoothEnabledAsync()
    {
        return Task.FromResult(_bluetoothLE.IsOn);
    }

    public async Task<bool> ConnectToLastKnownDeviceAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_lastConnectedDeviceId))
        {
            System.Diagnostics.Debug.WriteLine("[BLE RECONNECT] No last connected device ID available");
            return false;
        }

        try
        {
            UpdateConnectionState(ConnectionState.Reconnecting);

            if (!Guid.TryParse(_lastConnectedDeviceId, out var guid))
            {
                System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] Invalid GUID: {_lastConnectedDeviceId}");
                UpdateConnectionState(ConnectionState.Disconnected);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] Connecting to known device: {guid}");
            var connectParams = new ConnectParameters(autoConnect: false, forceBleTransport: true);
            var bleDevice = await _adapter.ConnectToKnownDeviceAsync(guid, connectParams, ct);

            _connectedBleDevice = bleDevice;

            // Discover Nordic UART Service and get TX/RX characteristics
            var service = await bleDevice.GetServiceAsync(BlueBuzzahConstants.NordicUartServiceUuid);
            if (service == null)
            {
                System.Diagnostics.Debug.WriteLine("[BLE RECONNECT] Nordic UART Service not found");
                await DisconnectForReconnectAsync();
                UpdateConnectionState(ConnectionState.Disconnected);
                return false;
            }

            _txCharacteristic = await service.GetCharacteristicAsync(BlueBuzzahConstants.TxCharacteristicUuid);
            _rxCharacteristic = await service.GetCharacteristicAsync(BlueBuzzahConstants.RxCharacteristicUuid);

            if (_txCharacteristic == null || _rxCharacteristic == null)
            {
                System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] TX or RX characteristic not found (TX: {_txCharacteristic != null}, RX: {_rxCharacteristic != null})");
                await DisconnectForReconnectAsync();
                UpdateConnectionState(ConnectionState.Disconnected);
                return false;
            }

            await SubscribeToNotificationsAsync();

            // Identify ourselves immediately (parity with ConnectToDeviceAsync):
            // without this, the firmware types this connection UNKNOWN for up to
            // 1s and drops responses to any command sent in that window.
            try
            {
                var identify = Encoding.UTF8.GetBytes("IDENTIFY:PHONE" + BlueBuzzahConstants.CommandTerminator);
                await _txCharacteristic!.WriteAsync(identify);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] IDENTIFY:PHONE write failed (non-fatal): {ex.Message}");
            }

            var gloveDevice = new GloveDevice
            {
                Id = bleDevice.Id.ToString(),
                Name = bleDevice.Name ?? BlueBuzzahConstants.DeviceName,
                ConnectionState = ConnectionState.Connected,
                LastConnected = DateTime.Now
            };

            ConnectedDevice = gloveDevice;
            UpdateConnectionState(ConnectionState.Connected);
            _lastConnectedDeviceId = bleDevice.Id.ToString();
            _userInitiatedDisconnect = false;

            System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] Successfully reconnected to {gloveDevice.Name}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BLE RECONNECT] Failed: {ex.GetType().Name}: {ex.Message}");
            await DisconnectForReconnectAsync();
            return false;
        }
    }

    public async Task DisconnectForReconnectAsync()
    {
        try
        {
            if (_rxCharacteristic != null)
                await UnsubscribeFromNotificationsAsync();
            if (_connectedBleDevice != null)
                await _adapter.DisconnectDeviceAsync(_connectedBleDevice);
        }
        finally
        {
            _connectedBleDevice = null;
            _txCharacteristic = null;
            _rxCharacteristic = null;
            ConnectedDevice = null;
            UpdateConnectionState(ConnectionState.Disconnected);
        }
    }

    public async Task<ScanResult> ScanForDevicesWithResultAsync(int timeoutMs = 10000, CancellationToken ct = default)
    {
        try
        {
            var devices = await ScanForDevicesAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            var deviceList = devices.ToList();
            return deviceList.Count > 0
                ? new ScanResult(ScanOutcome.DevicesFound, deviceList)
                : new ScanResult(ScanOutcome.NoDevicesFound, deviceList);
        }
        catch (Exception ex)
        {
            return new ScanResult(ScanOutcome.ScanFailed, Enumerable.Empty<GloveDevice>(), ex.Message);
        }
    }

    private void OnRxCharacteristicValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        try
        {
            var data = e.Characteristic.Value;
            if (data == null || data.Length == 0)
                return;

            ProcessIncomingRxText(Encoding.UTF8.GetString(data));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RX parse error: {ex.Message}");
            _pendingResponseTcs?.TrySetException(ex);
        }
    }

    /// <summary>
    /// Delivers one CommandResponse per EOT-terminated frame reassembled by
    /// <see cref="_rxAssembler"/> from incoming RX text. Responses may span multiple
    /// BLE notification packets; a packet may also carry the tail of one frame and
    /// the head of the next. Firmware-internal glove↔glove messages are filtered
    /// inside <see cref="RxFrameAssembler.Append"/>.
    /// </summary>
    internal void ProcessIncomingRxText(string text)
    {
        foreach (var frame in _rxAssembler.Append(text))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BLE RSP] <<< {frame.Replace("\n", "|")} (pending={_pendingResponseTcs != null})");
            var response = CommandResponse.Parse(frame);
            ResponseReceived?.Invoke(this, response);
            _pendingResponseTcs?.TrySetResult(response);
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        // Log RAW discovery - BEFORE any filtering (critical for debugging)
        System.Diagnostics.Debug.WriteLine($"[BLE RAW] Device: Name='{e.Device.Name ?? "(null)"}' ID={e.Device.Id} RSSI={e.Device.Rssi}");

        // Filter for VL (PRIMARY) device only
        // App connects only to VL; VL relays commands to VR as needed
        if (string.IsNullOrEmpty(e.Device.Name))
        {
            System.Diagnostics.Debug.WriteLine($"[BLE FILTER] REJECTED - Name is null/empty (ID: {e.Device.Id})");
            return;
        }

        if (!e.Device.Name.Equals(BlueBuzzahConstants.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine($"[BLE FILTER] REJECTED - Name '{e.Device.Name}' != '{BlueBuzzahConstants.DeviceName}'");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[BLE FILTER] ACCEPTED - {e.Device.Name}");

        var deviceId = e.Device.Id.ToString();
        var gloveDevice = new GloveDevice
        {
            Id = deviceId,
            Name = e.Device.Name,
            SignalStrength = e.Device.Rssi,
            ConnectionState = ConnectionState.Disconnected
        };

        // Cache both the GloveDevice and the actual IDevice reference
        _discoveredBleDevices.TryAdd(deviceId, e.Device);

        if (_discoveredDevices.TryAdd(deviceId, gloveDevice))
        {
            System.Diagnostics.Debug.WriteLine($"[BLE] Added to discovered devices: {gloveDevice.Name} ({deviceId})");
            DeviceDiscovered?.Invoke(this, gloveDevice);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[BLE] Device already in cache: {gloveDevice.Name} ({deviceId})");
        }
    }

    private void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Connected;
        }
        // Deliberately NOT raising Connected here: this adapter event fires when the
        // GATT link comes up, BEFORE service/characteristic discovery has run, so
        // commands sent in response to it fail. Worse, the premature raise made
        // UpdateConnectionState dedupe the real one after discovery completed, so
        // "ready for commands" was never signaled. ConnectToDeviceAsync and the
        // reconnect path raise Connected once the UART characteristics are live.
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        // Clear stale response data before cancelling pending commands
        _rxAssembler.Reset();
        _pendingResponseTcs?.TrySetCanceled();

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Disconnected;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Disconnected);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        _connectedBleDevice = null;
        _txCharacteristic = null;
        _rxCharacteristic = null;

        // Clear stale response data before cancelling pending commands
        _rxAssembler.Reset();
        _pendingResponseTcs?.TrySetCanceled();

        if (ConnectedDevice != null)
        {
            ConnectedDevice.ConnectionState = ConnectionState.Error;
            ConnectedDevice = null;
        }
        UpdateConnectionState(ConnectionState.Error);
    }

    private void UpdateConnectionState(ConnectionState newState)
    {
        if (CurrentConnectionState != newState)
        {
            CurrentConnectionState = newState;
            ConnectionStateChanged?.Invoke(this, newState);
        }
    }
}
