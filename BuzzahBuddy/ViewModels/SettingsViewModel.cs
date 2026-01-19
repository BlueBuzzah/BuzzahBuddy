using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// Handles app preferences and device management.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
	private readonly IBluetoothService _bluetoothService;
	private readonly IDataStorageService _storageService;

	[ObservableProperty]
	private bool _isConnected;

	[ObservableProperty]
	private string _connectedDeviceName = "None";

	[ObservableProperty]
	private ConnectionState _connectionState = ConnectionState.Disconnected;

	[ObservableProperty]
	private bool _enableNotifications = true;

	[ObservableProperty]
	private bool _autoConnect = false;

	[ObservableProperty]
	private string _appVersion = "0.1.1";

	public SettingsViewModel(
			IBluetoothService bluetoothService,
			IDataStorageService storageService)
	{
		_bluetoothService = bluetoothService;
		_storageService = storageService;

		Title = "Settings";

		// Subscribe to connection changes
		_bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

		// Load settings
		LoadSettings();
		UpdateConnectionInfo();
	}

	[RelayCommand]
	private async Task DisconnectDeviceAsync()
	{
		if (!IsConnected)
		{
			await Shell.Current.DisplayAlert(
					"Not Connected",
					"No device is currently connected.",
					"OK");
			return;
		}

		var confirm = await Shell.Current.DisplayAlert(
				"Disconnect Device",
				$"Are you sure you want to disconnect from {ConnectedDeviceName}?",
				"Yes",
				"No");

		if (confirm)
		{
			await _bluetoothService.DisconnectAsync();

			await Shell.Current.DisplayAlert(
					"Disconnected",
					"Device disconnected successfully.",
					"OK");
		}
	}

	[RelayCommand]
	private async Task ClearDataAsync()
	{
		var confirm = await Shell.Current.DisplayAlert(
				"Clear All Data",
				"This will delete all therapy sessions and custom patterns. Are you sure?",
				"Yes",
				"No");

		if (confirm)
		{
			await _storageService.ClearAllDataAsync();

			await Shell.Current.DisplayAlert(
					"Data Cleared",
					"All data has been cleared successfully.",
					"OK");
		}
	}

	[RelayCommand]
	private async Task NavigateToDevicesAsync()
	{
		await Shell.Current.GoToAsync("//devices");
	}

	[RelayCommand]
	private async Task NavigateToCalibrationAsync()
	{
		await Shell.Current.GoToAsync("calibration");
	}

	partial void OnEnableNotificationsChanged(bool value)
	{
		Preferences.Default.Set("EnableNotifications", value);
	}

	partial void OnAutoConnectChanged(bool value)
	{
		Preferences.Default.Set("AutoConnect", value);
	}

	private void LoadSettings()
	{
		EnableNotifications = Preferences.Default.Get("EnableNotifications", true);
		AutoConnect = Preferences.Default.Get("AutoConnect", false);

		// Get app version from assembly
		AppVersion = AppInfo.Current.VersionString;
	}

	private void UpdateConnectionInfo()
	{
		ConnectionState = _bluetoothService.CurrentConnectionState;
		IsConnected = ConnectionState == ConnectionState.Connected;

		if (IsConnected && _bluetoothService.ConnectedDevice != null)
		{
			ConnectedDeviceName = _bluetoothService.ConnectedDevice.Name;
		}
		else
		{
			ConnectedDeviceName = "None";
		}
	}

	private void OnConnectionStateChanged(object? sender, ConnectionState state)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			UpdateConnectionInfo();
		});
	}

	/// <summary>
	/// Unsubscribes from Bluetooth service events to prevent memory leaks.
	/// </summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
			System.Diagnostics.Debug.WriteLine("[SETTINGS] ViewModel disposed, unsubscribed from ConnectionStateChanged");
		}
		base.Dispose(disposing);
	}
}
