using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the device list page.
/// Handles scanning for and connecting to BlueBuzzah gloves.
/// </summary>
public partial class DeviceListViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IDataStorageService _storageService;
    private CancellationTokenSource? _scanCancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<GloveDevice> _availableDevices = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _bluetoothEnabled = true;

    [ObservableProperty]
    private GloveDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _connectingDeviceId;

    [ObservableProperty]
    private bool _isAlreadyConnected;

    [ObservableProperty]
    private string? _connectedDeviceName;

    [ObservableProperty]
    private string _scanButtonText = "Scan for Devices";

    [ObservableProperty]
    private string _scanButtonDescription = "Start scanning for BlueBuzzah gloves";

    [ObservableProperty]
    private bool _hasCompletedScan;

    public DeviceListViewModel(
        IBluetoothService bluetoothService,
        IDataStorageService storageService)
    {
        _bluetoothService = bluetoothService;
        _storageService = storageService;

        Title = "Devices";

        // Subscribe to device discovery
        _bluetoothService.DeviceDiscovered += OnDeviceDiscovered;

        // Subscribe to connection state changes
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Check Bluetooth status and initial connection state
        CheckBluetoothStatus();
        UpdateConnectionState();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
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

    private async Task StartScanningAsync()
    {
        AvailableDevices.Clear();
        IsScanning = true;
        IsBusy = true;
        UpdateScanButtonState();

        try
        {
            BluetoothEnabled = await _bluetoothService.IsBluetoothEnabledAsync();

            if (!BluetoothEnabled)
            {
                await Shell.Current.DisplayAlert(
                    "Bluetooth Disabled",
                    "Please enable Bluetooth to scan for devices.",
                    "OK");
                return;
            }

            _scanCancellationTokenSource = new CancellationTokenSource();

            var devices = await _bluetoothService.ScanForDevicesAsync(
                TimeSpan.FromSeconds(10),
                _scanCancellationTokenSource.Token);

            // Devices are added via event handler, but this ensures we have the final list
            foreach (var device in devices)
            {
                if (!AvailableDevices.Any(d => d.Id == device.Id))
                {
                    AvailableDevices.Add(device);
                }
            }

            // Don't show alert - UI will show empty state instead
        }
        catch (OperationCanceledException)
        {
            // User stopped the scan - this is expected, no error message needed
            System.Diagnostics.Debug.WriteLine("Scan cancelled by user");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                "Scan Error",
                $"An error occurred while scanning: {ex.Message}",
                "OK");
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

    private void UpdateScanButtonState()
    {
        if (IsScanning)
        {
            ScanButtonText = "Stop Scanning";
            ScanButtonDescription = "Stop the current device scan";
        }
        else
        {
            ScanButtonText = "Scan for Devices";
            ScanButtonDescription = "Start scanning for BlueBuzzah gloves";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsAlreadyConnected)
            return;

        var confirm = await Shell.Current.DisplayAlert(
            "Disconnect Device",
            $"Are you sure you want to disconnect from {ConnectedDeviceName}?",
            "Disconnect",
            "Cancel");

        if (!confirm)
            return;

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
                $"An error occurred: {ex.Message}",
                "OK");
        }
    }

    [RelayCommand]
    private async Task NavigateToControlAsync()
    {
        await Shell.Current.GoToAsync("//control");
    }

    [RelayCommand]
    private async Task ConnectAsync(GloveDevice device)
    {
        if (device == null || IsBusy || IsConnecting)
            return;

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
                await _storageService.SaveLastDeviceAsync(device);

                // Show brief non-blocking feedback
                // Note: Using DisplayAlert for now, but should be replaced with Toast in production
                _ = Shell.Current.DisplayAlert(
                    "Connected",
                    $"Successfully connected to {device.Name}",
                    "OK");

                // Navigate immediately to control page
                await Shell.Current.GoToAsync("//control");
            }
            else
            {
                // Only show alert for failures
                await Shell.Current.DisplayAlert(
                    "Connection Failed",
                    $"Could not connect to {device.Name}. Please try again.",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                "Connection Error",
                $"An error occurred: {ex.Message}",
                "OK");
        }
        finally
        {
            IsConnecting = false;
            ConnectingDeviceId = null;
            IsBusy = false;
        }
    }

    private async void CheckBluetoothStatus()
    {
        BluetoothEnabled = await _bluetoothService.IsBluetoothEnabledAsync();
    }

    private void OnDeviceDiscovered(object? sender, GloveDevice device)
    {
        // Add to UI thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!AvailableDevices.Any(d => d.Id == device.Id))
            {
                AvailableDevices.Add(device);
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
        IsAlreadyConnected = _bluetoothService.CurrentConnectionState == ConnectionState.Connected;
        ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;

        if (_bluetoothService.CurrentConnectionState == ConnectionState.Connecting)
        {
            IsConnecting = true;
            ConnectingDeviceId = _bluetoothService.ConnectedDevice?.Id;
        }
        else if (_bluetoothService.CurrentConnectionState != ConnectionState.Connecting)
        {
            IsConnecting = false;
            ConnectingDeviceId = null;
        }
    }
}
