using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Dialogs;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the device list page.
/// Handles scanning for and connecting to BlueBuzzah gloves.
/// </summary>
public partial class DeviceListViewModel : BaseViewModel, IQueryAttributable
{
    /// <summary>Navigation parameter: start a scan on arrival (Session's Scan for Gloves).</summary>
    public const string ScanQueryKey = "scan";

    private readonly IBluetoothService _bluetoothService;
    private readonly IDataStorageService _storageService;
    private readonly IReconnectionService _reconnectionService;
    private readonly AutoConnectService _autoConnect;
    private CancellationTokenSource? _scanCancellationTokenSource;
    private readonly IConfirmationService _confirmation;
    // Scan start and stop share one spot; ignore a tremor's second tap within 400 ms.
    private readonly RepeatGuard _scanToggle = new(RepeatGuard.SessionControlWindow);
    private (string Title, string Message, string? Detail)? _lastError;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    /// <summary>
    /// Device settings (therapy profile, LED, battery, diagnostics) shown when connected.
    /// </summary>
    public DeviceSettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableCollection<GloveDevice> _availableDevices = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadyState))]
    [NotifyPropertyChangedFor(nameof(ShowNoResultsState))]
    [NotifyPropertyChangedFor(nameof(ShowScanningWithResults))]
    [NotifyPropertyChangedFor(nameof(ShowScanning))]
    private bool _isScanning;

    /// <summary>The launch auto-connect is trying: this page shows its scanning state for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadyState))]
    [NotifyPropertyChangedFor(nameof(ShowNoResultsState))]
    [NotifyPropertyChangedFor(nameof(ShowScanning))]
    [NotifyPropertyChangedFor(nameof(ScanningHint))]
    private bool _isAutoConnecting;

    [ObservableProperty]
    private bool _bluetoothEnabled = true;

    partial void OnBluetoothEnabledChanged(bool value) => ResolveBanner();

    [ObservableProperty]
    private GloveDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _connectingDeviceId;

    [ObservableProperty]
    private string _scanButtonText = "Scan for Gloves";

    // The scan view's one banner: Bluetooth off > last connect/scan failure > launch
    // auto-connect > apply that never reconnected (Banner README).
    [ObservableProperty]
    private bool _bannerVisible;

    [ObservableProperty]
    private StatusTone _bannerTone = StatusTone.Warning;

    [ObservableProperty]
    private string? _bannerTitle;

    [ObservableProperty]
    private string? _bannerMessage;

    [ObservableProperty]
    private string? _bannerDetail;

    [ObservableProperty]
    private string? _bannerActionText;

    [ObservableProperty]
    private System.Windows.Input.ICommand? _bannerActionCommand;

    [ObservableProperty]
    private bool _bannerActionAtEnd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadyState))]
    [NotifyPropertyChangedFor(nameof(ShowNoResultsState))]
    private bool _hasCompletedScan;

    public DeviceListViewModel(
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IReconnectionService reconnectionService,
        IConnectionStateService connectionStateService,
        DeviceSettingsViewModel deviceSettingsViewModel,
        IConfirmationService confirmation,
        AutoConnectService autoConnect)
    {
        _autoConnect = autoConnect;
        _autoConnect.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(OnAutoConnectStateChanged);
        _confirmation = confirmation;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        _reconnectionService = reconnectionService;
        ConnectionInfo = connectionStateService;
        Settings = deviceSettingsViewModel;

        Title = "Gloves";

        // Subscribe to device discovery
        _bluetoothService.DeviceDiscovered += OnDeviceDiscovered;

        // ShowScanningWithResults depends on the collection's emptiness
        AvailableDevices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowScanningWithResults));

        // Subscribe to connection state changes
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Gloves found before Demo Mode switched belong to the other service: forget them.
        WeakReferenceMessenger.Default.Register<DeviceListViewModel, DemoModeChangedMessage>(this, (vm, _) =>
        {
            vm._scanCancellationTokenSource?.Cancel();
            vm.AvailableDevices.Clear();
            vm.HasCompletedScan = false;
        });

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceSettingsViewModel.ApplyReconnectFailed))
                ResolveBanner();
        };

        // Check Bluetooth status and initial connection state
        CheckBluetoothStatusAsync().SafeFireAndForget("[DEVICELIST]");
        UpdateConnectionState();
        // The launch auto-connect starts before Shell builds this page.
        OnAutoConnectStateChanged();
    }

    private void OnAutoConnectStateChanged()
    {
        IsAutoConnecting = _autoConnect.State == AutoConnectState.Connecting;
        UpdateScanButtonState();
        ResolveBanner();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleScanAsync()
    {
        if (!_scanToggle.TryAcquire())
            return;

        if (IsAutoConnecting)
        {
            // Stop Scanning here stops the launch auto-connect.
            await _autoConnect.CancelAsync();
        }
        else if (IsScanning)
        {
            // Stop scanning
            await StopScanningAsync();
        }
        else
        {
            // Start scanning
            await StartScanningAsync();
        }
    }

    /// <summary>
    /// Called from DeviceListPage.OnAppearing. Cancels any in-progress reconnection
    /// (the user is choosing a device manually) and refreshes Bluetooth state.
    /// </summary>
    public void OnPageAppearing()
    {
        // Don't cancel auto-reconnect just because this page appeared: a profile change
        // reboots the gloves and relies on the reconnect loop, and tab switches during
        // that window must not kill it. Manual connect (ConnectAsync) cancels it instead.
        CheckBluetoothStatusAsync().SafeFireAndForget("[DEVICELIST]");
        UpdateConnectionState();
        Settings.OnPageAppearing();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.ContainsKey(ScanQueryKey) && !IsScanning)
            StartScanningAsync().SafeFireAndForget("[DEVICELIST]");
    }

    /// <summary>Empty-state variant: before the first scan has run.</summary>
    public bool ShowReadyState => !HasCompletedScan && !ShowScanning;

    /// <summary>Empty-state variant: a scan finished and found nothing.</summary>
    public bool ShowNoResultsState => HasCompletedScan && !ShowScanning;

    /// <summary>Empty-state variant: a scan, or the launch auto-connect, is under way.</summary>
    public bool ShowScanning => IsScanning || IsAutoConnecting;

    public string ScanningHint => IsAutoConnecting
        ? "This can take up to 15 seconds."
        : "This can take up to 10 seconds.";

    /// <summary>Scan still running with devices already listed — the EmptyView scanning
    /// indicator is hidden once the collection is non-empty, so a slim strip covers it.</summary>
    public bool ShowScanningWithResults => IsScanning && AvailableDevices.Count > 0;

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        // Pull-to-refresh: rescan unless a scan is already running.
        if (IsScanning)
        {
            IsRefreshing = false;
            return;
        }
        try
        {
            await StartScanningAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task StartScanningAsync()
    {
        if (!await EnsureBlePermissionsAsync())
            return;

        System.Diagnostics.Debug.WriteLine("[VM] StartScanningAsync called");
        AvailableDevices.Clear();
        IsScanning = true;
        IsBusy = true;
        UpdateScanButtonState();

        try
        {
            System.Diagnostics.Debug.WriteLine("[VM] Checking Bluetooth enabled status...");
            BluetoothEnabled = await _bluetoothService.IsBluetoothEnabledAsync();
            System.Diagnostics.Debug.WriteLine($"[VM] Bluetooth enabled: {BluetoothEnabled}");

            if (!BluetoothEnabled)
            {
                // The "Bluetooth Is Off" banner (OnBluetoothEnabledChanged) says what to do.
                return;
            }

            _lastError = null;
            ResolveBanner();

            _scanCancellationTokenSource = new CancellationTokenSource();

            System.Diagnostics.Debug.WriteLine("[VM] Calling ScanForDevicesWithResultAsync with 10s timeout...");
            var result = await _bluetoothService.ScanForDevicesWithResultAsync(
                timeoutMs: 10000,
                ct: _scanCancellationTokenSource.Token);

            switch (result.Outcome)
            {
                case ScanOutcome.DevicesFound:
                    var deviceList = result.Devices.ToList();
                    System.Diagnostics.Debug.WriteLine($"[VM] Scan returned {deviceList.Count} device(s)");

                    // Devices are added via event handler, but this ensures we have the final list
                    foreach (var device in deviceList)
                    {
                        if (!AvailableDevices.Any(d => d.Id == device.Id))
                        {
                            System.Diagnostics.Debug.WriteLine($"[VM] Adding device from return list: {device.Name}");
                            AvailableDevices.Add(device);
                        }
                    }

                    SemanticScreenReader.Announce(deviceList.Count == 1
                        ? "Scan complete. 1 glove found."
                        : $"Scan complete. {deviceList.Count} gloves found.");
                    break;

                case ScanOutcome.NoDevicesFound:
                    SemanticScreenReader.Announce("Scan complete. No gloves found.");
                    break;

                case ScanOutcome.ScanFailed:
                    ShowError("Scan didn't work", "Turn Bluetooth off and on, then scan again.", result.ErrorMessage);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // User stopped the scan - this is expected, no error message needed
            System.Diagnostics.Debug.WriteLine("[VM] Scan cancelled by user");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VM] Scan error: {ex.GetType().Name}: {ex.Message}");
            ShowError("Scan didn't work", "Turn Bluetooth off and on, then scan again.", ex.Message);
        }
        finally
        {
            // Clean up state
            IsScanning = false;
            IsBusy = false;
            HasCompletedScan = true;
            _scanCancellationTokenSource?.Dispose();
            _scanCancellationTokenSource = null;
            UpdateScanButtonState();
            System.Diagnostics.Debug.WriteLine($"[VM] Scan complete. UI has {AvailableDevices.Count} device(s)");
        }
    }

    private async Task StopScanningAsync()
    {
        // Cancel the ongoing scan operation
        _scanCancellationTokenSource?.Cancel();

        // Also call StopScan to ensure adapter stops immediately
        await _bluetoothService.StopScanAsync();

        // Note: State cleanup happens in StartScanningAsync's finally block
        // to avoid race conditions. We don't set state here.
    }

    /// <summary>
    /// Ensures BLE runtime permissions are granted before scanning.
    /// Android 12+ needs Bluetooth (SCAN/CONNECT); Android ≤ 11 needs location for BLE scans.
    /// iOS prompts automatically via CoreBluetooth, so non-Android platforms always return true.
    /// </summary>
    private async Task<bool> EnsureBlePermissionsAsync()
    {
#if ANDROID
        var status = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? await Permissions.CheckStatusAsync<Permissions.Bluetooth>()
            : await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

        if (status == PermissionStatus.Granted)
            return true;

        var showRationale = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? Permissions.ShouldShowRationale<Permissions.Bluetooth>()
            : Permissions.ShouldShowRationale<Permissions.LocationWhenInUse>();

        if (showRationale)
        {
            await Shell.Current.DisplayAlert(
                "Allow Bluetooth",
                "BuzzahBuddy needs Bluetooth to find and connect to your gloves.",
                "OK");
        }

        status = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? await Permissions.RequestAsync<Permissions.Bluetooth>()
            : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

        if (status == PermissionStatus.Granted)
            return true;

        var openSettings = await Shell.Current.DisplayAlert(
            "Bluetooth Isn't Allowed",
            "Turn on Bluetooth for BuzzahBuddy in your phone's settings, then scan again.",
            "Open Settings",
            "Not Now");
        if (openSettings)
            AppInfo.Current.ShowSettingsUI();

        return false;
#else
        return await Task.FromResult(true);
#endif
    }

    private void UpdateScanButtonState()
    {
        if (IsScanning || IsAutoConnecting)
        {
            ScanButtonText = "Stop Scanning";
        }
        else
        {
            ScanButtonText = "Scan for Gloves";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!ConnectionInfo.IsConnected)
            return;

        var confirm = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Disconnect Your Gloves?",
            "Your phone stops controlling the gloves. A running session keeps going on the gloves.",
            "Disconnect",
            Destructive: true));

        if (!confirm)
            return;

        try
        {
            await _bluetoothService.DisconnectAsync();

            // Explicitly update state after disconnect completes.
            // Cannot rely solely on ConnectionStateChanged event due to race condition
            // in BluetoothService where OnDeviceDisconnected fires before ConnectedDevice
            // is nulled, and the finally block's UpdateConnectionState is then a no-op.
            UpdateConnectionState();
        }
        catch (Exception ex)
        {
            var error = ErrorMessageHelper.FromException(ex);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
    }

    private void ShowError(string title, string message, string? detail)
    {
        _lastError = (title, message, detail);
        ResolveBanner();
    }

    private void ResolveBanner()
    {
        (StatusTone Tone, string Title, string Message, string? Detail, string? Action, System.Windows.Input.ICommand? Command)? banner =
            !BluetoothEnabled ? (StatusTone.Warning, "Bluetooth Is Off", "Turn on Bluetooth to find your gloves.", null, "Open Settings", OpenSettingsCommand)
            : _lastError is { } e ? (StatusTone.Danger, e.Title, e.Message, e.Detail, null, null)
            : _autoConnect.FailedBanner is { } a ? (a.Tone, a.Title, a.Message, null, "Try Again", TryAgainAutoConnectCommand)
            : Settings.ApplyReconnectFailed ? (StatusTone.Warning, "Your Gloves Haven't Reconnected",
                "The settings were sent. Make sure both gloves are on, then connect again below.", null, null, null)
            : null;
        BannerVisible = banner is not null;
        if (banner is { } b)
            (BannerTone, BannerTitle, BannerMessage, BannerDetail, BannerActionText, BannerActionCommand) = b;
        BannerActionAtEnd = BannerVisible && BannerActionCommand == TryAgainAutoConnectCommand;
    }

    [RelayCommand]
    private static void OpenSettings() => AppInfo.Current.ShowSettingsUI();

    [RelayCommand]
    private void TryAgainAutoConnect() => _autoConnect.TryAgain();

    [RelayCommand]
    private async Task NavigateToControlAsync()
    {
        await Shell.Current.GoToAsync(Routes.Control);
    }

    [RelayCommand]
    private async Task ConnectAsync(GloveDevice device)
    {
        if (device == null || IsConnecting)
            return;

        // A manual pick supersedes any in-progress auto-reconnect or launch auto-connect.
        _reconnectionService.CancelReconnect(supersededByConnect: true);
        await _autoConnect.CancelAsync();

        // Stop scanning if active
        if (IsScanning)
        {
            await StopScanningAsync();
        }

        IsConnecting = true;
        ConnectingDeviceId = device.Id;
        IsBusy = true;

        try
        {
            var success = await _bluetoothService.ConnectToDeviceAsync(device);

            if (success)
            {
                // Pretend gloves aren't remembered: their ID would fail against real ones at the
                // next launch. Demo Mode auto-connects pretend gloves under the real ID instead.
                if (!Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false))
                    await _storageService.SaveLastDeviceAsync(device);
                _lastError = null;
                ResolveBanner();
                SemanticScreenReader.Announce($"Connected to {device.DisplayName}");

                // Straight on to the Session tab; the announcement confirms it.
                await Shell.Current.GoToAsync(Routes.Control);
            }
            else
            {
                ShowError($"Couldn't connect to {device.DisplayName}", "Move closer to the glove and tap Connect again.", null);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't connect to {device.DisplayName}", "Move closer to the glove and tap Connect again.", ex.Message);
        }
        finally
        {
            IsConnecting = false;
            ConnectingDeviceId = null;
            IsBusy = false;
        }
    }

    private async Task CheckBluetoothStatusAsync()
    {
        BluetoothEnabled = await _bluetoothService.IsBluetoothEnabledAsync();
    }

    private void OnDeviceDiscovered(object? sender, GloveDevice device)
    {
        System.Diagnostics.Debug.WriteLine($"[VM EVENT] DeviceDiscovered event received: {device.Name} ({device.Id})");
        // Add to UI thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!AvailableDevices.Any(d => d.Id == device.Id))
            {
                System.Diagnostics.Debug.WriteLine($"[VM EVENT] Adding to AvailableDevices: {device.Name}");
                AvailableDevices.Add(device);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[VM EVENT] Device already in list: {device.Name}");
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionState();
        });
    }

    private void UpdateConnectionState()
    {
        var state = _bluetoothService.CurrentConnectionState;

        if (state != Models.ConnectionState.Connecting)
        {
            IsConnecting = false;
            ConnectingDeviceId = null;
        }
    }

    /// <summary>
    /// Unsubscribes from Bluetooth service events to prevent memory leaks.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.DeviceDiscovered -= OnDeviceDiscovered;
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            WeakReferenceMessenger.Default.Unregister<DemoModeChangedMessage>(this);
            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource?.Dispose();
            _scanCancellationTokenSource = null;
            System.Diagnostics.Debug.WriteLine("[DEVICELIST] ViewModel disposed, unsubscribed from events");
        }
        base.Dispose(disposing);
    }
}
