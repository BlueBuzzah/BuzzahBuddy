using BuzzahBuddy.Helpers;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// Handles app preferences and device management.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
	private readonly IBluetoothService _bluetoothService;
	private readonly IDataStorageService _storageService;
	private readonly IGloveControlService _gloveControlService;

	/// <summary>Suppresses the device write in OnTherapyLedOffChanged while syncing from the device.</summary>
	private bool _suppressTherapyLedWrite;

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

	/// <summary>
	/// Device setting: turn the status LED off during therapy sessions (THERAPY_LED_OFF).
	/// </summary>
	[ObservableProperty]
	private bool _therapyLedOff;

	/// <summary>
	/// True once the therapy LED setting has been read from the connected device;
	/// the toggle is hidden until then.
	/// </summary>
	[ObservableProperty]
	private bool _therapyLedLoaded;

	/// <summary>
	/// True while a therapy-LED read or write is in flight; disables the switch so
	/// toggles can't overlap (a stale failure would otherwise desync the UI).
	/// </summary>
	[ObservableProperty]
	private bool _therapyLedBusy;

	public SettingsViewModel(
			IBluetoothService bluetoothService,
			IDataStorageService storageService,
			IConnectionStateService connectionStateService,
			IGloveControlService gloveControlService)
	{
		_bluetoothService = bluetoothService;
		_storageService = storageService;
		_gloveControlService = gloveControlService;
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
		await Shell.Current.GoToAsync(Routes.Devices);
	}

	[RelayCommand]
	private async Task NavigateToCalibrationAsync()
	{
		await Shell.Current.GoToAsync(Routes.Calibration);
	}

	/// <summary>
	/// Reads device-side settings (therapy LED) from the connected glove.
	/// Called from the page's OnAppearing.
	/// </summary>
	[RelayCommand]
	private async Task LoadDeviceSettingsAsync()
	{
		if (TherapyLedBusy)
			return;

		if (!ConnectionInfo.IsConnected)
		{
			TherapyLedLoaded = false;
			return;
		}

		TherapyLedBusy = true;
		try
		{
			_suppressTherapyLedWrite = true;
			TherapyLedOff = await _gloveControlService.GetTherapyLedOffAsync();
			TherapyLedLoaded = true;
		}
		catch
		{
			// Older firmware may not support THERAPY_LED_OFF; just hide the toggle.
			TherapyLedLoaded = false;
		}
		finally
		{
			_suppressTherapyLedWrite = false;
			TherapyLedBusy = false;
		}
	}

	partial void OnTherapyLedOffChanged(bool value)
	{
		if (_suppressTherapyLedWrite)
			return;

		// async via discard: Switch toggles aren't awaitable. TherapyLedBusy disables
		// the switch until the write settles, so writes can't overlap.
		_ = ApplyTherapyLedOffAsync(value);
	}

	private async Task ApplyTherapyLedOffAsync(bool value)
	{
		TherapyLedBusy = true;
		try
		{
			await _gloveControlService.SetTherapyLedOffAsync(value);
		}
		catch (Exception ex)
		{
			// Re-sync from the device rather than blind-reverting, so the switch
			// shows the device's actual state; if even the read fails, hide it.
			try
			{
				_suppressTherapyLedWrite = true;
				TherapyLedOff = await _gloveControlService.GetTherapyLedOffAsync();
			}
			catch
			{
				TherapyLedLoaded = false;
			}
			finally
			{
				_suppressTherapyLedWrite = false;
			}

			await Shell.Current.DisplayAlert(GetErrorTitle(ex), GetErrorMessage(ex), "OK");
		}
		finally
		{
			TherapyLedBusy = false;
		}
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
