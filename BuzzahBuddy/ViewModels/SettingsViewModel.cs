using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Dialogs;
using BuzzahBuddy.Services.Storage;
using BuzzahBuddy.Services.Theme;
using BuzzahBuddy.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

	/// <summary>System / Light / Dark, valued as <see cref="ThemePreference"/>.</summary>
	public IList<SegmentOption> ThemeOptions { get; } = new[]
	{
		new SegmentOption((int)ThemePreference.System, "System"),
		new SegmentOption((int)ThemePreference.Light, "Light"),
		new SegmentOption((int)ThemePreference.Dark, "Dark"),
	};

	[ObservableProperty]
	private int _selectedTheme;

	[ObservableProperty]
	private bool _highContrast;

	[ObservableProperty]
	private bool _dataClearedVisible;

	[ObservableProperty]
	private string _appVersion = string.Empty;

	public SettingsViewModel(IDataStorageService storageService, IThemeService theme, IConfirmationService confirmation)
	{
		_storageService = storageService;
		_theme = theme;
		_confirmation = confirmation;

		Title = "Settings";

		// Read without applying (setting the fields directly skips the change handlers).
		_selectedTheme = (int)theme.Preference;
		_highContrast = theme.HighContrast;
		AppVersion = AppInfo.Current.VersionString;

		// The old "Enable notifications" and "Auto-connect" switches did nothing; drop
		// what they stored.
		Preferences.Default.Remove("EnableNotifications");
		Preferences.Default.Remove("AutoConnect");
	}

	partial void OnSelectedThemeChanged(int value)
	{
		var preference = (ThemePreference)value;
		_theme.Preference = preference;
		SemanticScreenReader.Announce($"Theme set to {preference}");
	}

	// SwitchRow already announces "On"/"Off" for the toggle itself.
	partial void OnHighContrastChanged(bool value) => _theme.HighContrast = value;

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

		await _storageService.ClearAllDataAsync();
		DataClearedVisible = true;
		SemanticScreenReader.Announce("Data cleared");
	}
}
