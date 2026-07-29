using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// Handles app-level preferences only — device settings live on the Device page.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
	private readonly IDataStorageService _storageService;

	[ObservableProperty]
	private bool _enableNotifications = true;

	[ObservableProperty]
	private bool _autoConnect = false;

	[ObservableProperty]
	private string _appVersion = "0.1.1";

	public SettingsViewModel(IDataStorageService storageService)
	{
		_storageService = storageService;

		Title = "Settings";

		// Load settings
		LoadSettings();
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
