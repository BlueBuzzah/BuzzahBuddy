using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
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

	/// <summary>
	/// Centralized connection state service exposed for XAML binding.
	/// </summary>
	public IConnectionStateService ConnectionInfo { get; }

	[ObservableProperty]
	private bool _enableNotifications = true;

	[ObservableProperty]
	private bool _autoConnect = false;

	[ObservableProperty]
	private string _appVersion = "0.1.1";

	public SettingsViewModel(
			IBluetoothService bluetoothService,
			IDataStorageService storageService,
			IConnectionStateService connectionStateService)
	{
		_bluetoothService = bluetoothService;
		_storageService = storageService;
		ConnectionInfo = connectionStateService;

		Title = "Settings";

		// Load settings
		LoadSettings();
	}

	[RelayCommand]
	private async Task DisconnectDeviceAsync()
	{
		if (!ConnectionInfo.IsConnected)
		{
			await Shell.Current.DisplayAlert(
					"Not Connected",
					"No device is currently connected.",
					"OK");
			return;
		}

		var confirm = await Shell.Current.DisplayAlert(
				"Disconnect Device",
				$"Are you sure you want to disconnect from {ConnectionInfo.ConnectedDeviceName}?",
				"Yes",
				"No");

		if (confirm)
		{
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
}
