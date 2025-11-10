using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the main/home page of the application.
/// Displays connection status and provides quick actions.
/// </summary>
public partial class MainPageViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IGloveControlService _gloveControlService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectedDeviceName = "Not Connected";

    [ObservableProperty]
    private int _batteryLevel;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    public MainPageViewModel(
        IBluetoothService bluetoothService,
        IGloveControlService gloveControlService)
    {
        _bluetoothService = bluetoothService;
        _gloveControlService = gloveControlService;

        Title = "BuzzahBuddy";

        // Subscribe to connection state changes
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Initialize connection state (fire-and-forget is acceptable in constructor for UI initialization)
        _ = UpdateConnectionInfo();
    }

    [RelayCommand]
    private async Task NavigateToDeviceListAsync()
    {
        await Shell.Current.GoToAsync("//devices");
    }

    [RelayCommand]
    private async Task NavigateToControlAsync()
    {
        if (!IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Not Connected",
                "Please connect to a BlueBuzzah glove first.",
                "OK");
            return;
        }

        await Shell.Current.GoToAsync("//control");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            await UpdateConnectionInfo();

            if (IsConnected)
            {
                try
                {
                    var (leftVoltage, rightVoltage) = await _gloveControlService.GetBatteryAsync();
                    // Calculate percentage from minimum voltage (worst case)
                    var minVoltage = Math.Min(leftVoltage, rightVoltage);
                    BatteryLevel = VoltageToPercentage(minVoltage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Battery fetch error: {ex.Message}");
                }
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task UpdateConnectionInfo()
    {
        ConnectionState = _bluetoothService.CurrentConnectionState;
        IsConnected = ConnectionState == ConnectionState.Connected;

        if (IsConnected && _bluetoothService.ConnectedDevice != null)
        {
            ConnectedDeviceName = _bluetoothService.ConnectedDevice.Name;

            try
            {
                var (leftVoltage, rightVoltage) = await _gloveControlService.GetBatteryAsync();
                var minVoltage = Math.Min(leftVoltage, rightVoltage);
                BatteryLevel = VoltageToPercentage(minVoltage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Battery fetch error: {ex.Message}");
                BatteryLevel = 0;
            }
        }
        else
        {
            ConnectedDeviceName = "Not Connected";
            BatteryLevel = 0;
        }
    }

    private async void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        // Update on UI thread
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await UpdateConnectionInfo();
        });
    }

    /// <summary>
    /// Converts battery voltage to percentage estimate.
    /// </summary>
    /// <param name="voltage">Battery voltage (3.0-4.2V)</param>
    /// <returns>Percentage (0-100)</returns>
    private static int VoltageToPercentage(double voltage)
    {
        const double minVoltage = 3.0;
        const double maxVoltage = 4.2;

        if (voltage <= minVoltage) return 0;
        if (voltage >= maxVoltage) return 100;

        return (int)((voltage - minVoltage) / (maxVoltage - minVoltage) * 100);
    }
}
