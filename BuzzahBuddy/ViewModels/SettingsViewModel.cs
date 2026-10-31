using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Dialogs;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using BuzzahBuddy.Services.Theme;
using BuzzahBuddy.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// Handles app-level preferences only — device settings live on the Gloves page.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
	private readonly IDataStorageService _storageService;
	private readonly IThemeService _theme;
	private readonly IConfirmationService _confirmation;
	private readonly SwitchableBluetoothService _bluetooth;
	private readonly IReconnectionService _reconnection;
	private readonly AutoConnectService _autoConnectService;
	private readonly HistoryStore _history;

	/// <summary>Set while a cancelled confirmation puts the switch back, so it isn't applied.</summary>
	private bool _revertingDemoMode;

	/// <summary>System / Light / Dark, valued as <see cref="ThemePreference"/>.</summary>
	public IList<SegmentOption> ThemeOptions { get; } = new[]
	{
		new SegmentOption((int)ThemePreference.System, "System"),
		new SegmentOption((int)ThemePreference.Light, "Light"),
		new SegmentOption((int)ThemePreference.Dark, "Dark"),
	};

	/// <summary>Connect to the remembered gloves at launch (App reads it once per launch).</summary>
	[ObservableProperty]
	private bool _autoConnect;

	[ObservableProperty]
	private int _selectedTheme;

	[ObservableProperty]
	private bool _highContrast;

	[ObservableProperty]
	private bool _dataClearedVisible;

	[ObservableProperty]
	private string _appVersion = string.Empty;

	/// <summary>Pretend gloves instead of real ones, for testing and demonstrations.</summary>
	[ObservableProperty]
	private bool _demoMode;

	/// <summary>Demo Mode can't switch while a session runs or is paused.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanChangeDemoMode))]
	private bool _isSessionActive;

	public bool CanChangeDemoMode => !IsSessionActive;

	public SettingsViewModel(
		IDataStorageService storageService,
		IThemeService theme,
		IConfirmationService confirmation,
		SwitchableBluetoothService bluetooth,
		IReconnectionService reconnection,
		AutoConnectService autoConnectService,
		IGloveControlService gloveControl,
		HistoryStore history)
	{
		_storageService = storageService;
		_theme = theme;
		_confirmation = confirmation;
		_bluetooth = bluetooth;
		_reconnection = reconnection;
		_autoConnectService = autoConnectService;
		_history = history;
		_demoMode = bluetooth.IsDemoMode;
		_isSessionActive = gloveControl.CurrentSessionStatus.IsActive;
		// ponytail: never unsubscribed; this VM is a singleton living as long as the service.
		gloveControl.SessionStateChanged += (_, status) =>
			MainThread.BeginInvokeOnMainThread(() => IsSessionActive = status.IsActive);

		Title = "Settings";

		// Read without applying (setting the fields directly skips the change handlers).
		_selectedTheme = (int)theme.Preference;
		_highContrast = theme.HighContrast;
		AppVersion = AppInfo.Current.VersionString;

		_autoConnect = Preferences.Default.Get(AutoConnectService.SettingKey, true);
	}

	partial void OnAutoConnectChanged(bool value) => Preferences.Default.Set(AutoConnectService.SettingKey, value);

	partial void OnSelectedThemeChanged(int value)
	{
		var preference = (ThemePreference)value;
		_theme.Preference = preference;
		SemanticScreenReader.Announce($"Theme set to {preference}");
	}

	// SwitchRow already announces "On"/"Off" for the toggle itself.
	partial void OnHighContrastChanged(bool value) => _theme.HighContrast = value;

	partial void OnDemoModeChanged(bool value)
	{
		if (!_revertingDemoMode)
			ApplyDemoModeAsync(value).SafeFireAndForget("[SETTINGS]");
	}

	/// <summary>
	/// Switches between real and pretend gloves. Turning it on while real gloves are
	/// connected asks first, because it disconnects them.
	/// </summary>
	private async Task ApplyDemoModeAsync(bool on)
	{
		if (on && _bluetooth.CurrentConnectionState == ConnectionState.Connected)
		{
			var confirm = await _confirmation.ConfirmAsync(new ConfirmRequest(
				"Turn On Demo Mode?",
				"This disconnects your gloves. Demo Mode uses pretend gloves, so you can try BuzzahBuddy without real ones.",
				"Disconnect and Turn On"));
			if (!confirm)
			{
				_revertingDemoMode = true;
				DemoMode = false;
				_revertingDemoMode = false;
				return;
			}
		}

		// A reconnect loop still looking for the other service's gloves would never find them.
		_reconnection.CancelReconnect();
		await _autoConnectService.CancelAsync();
		await _bluetooth.SetDemoModeAsync(on);
		Preferences.Default.Set(SwitchableBluetoothService.DemoModeKey, on);
		WeakReferenceMessenger.Default.Send(new DemoModeChangedMessage(on));
	}

	[RelayCommand]
	private async Task ClearDataAsync()
	{
		var confirm = await _confirmation.ConfirmAsync(new ConfirmRequest(
			"Delete All Data?",
			"This deletes your saved sessions and notes and forgets your last glove and profile. It can't be undone.",
			"Delete All Data",
			Destructive: true));
		if (!confirm)
			return;

		// The failure banner's Try Again would reconnect to the glove just forgotten.
		await _autoConnectService.CancelAsync();
		await _storageService.ClearAllDataAsync();
		try
		{
			await _history.ClearAsync();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Don't say the data is gone when the history file is still there.
			System.Diagnostics.Debug.WriteLine($"[SETTINGS] Could not delete history: {ex.Message}");
			return;
		}
		DataClearedVisible = true;
		SemanticScreenReader.Announce("Data cleared");
	}
}
