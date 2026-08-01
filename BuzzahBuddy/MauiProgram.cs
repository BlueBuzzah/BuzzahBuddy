using Microsoft.Extensions.Logging;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using BuzzahBuddy.ViewModels;
using BuzzahBuddy.Views;

namespace BuzzahBuddy;

public static class MauiProgram
{
	/// <summary>
	/// Set to true to use mock Bluetooth service for testing without hardware.
	/// Set to false to use real Bluetooth hardware.
	/// </summary>
	private static readonly bool UseMockBluetooth = false;

	/// <summary>
	/// Removes the platform's own input decoration so the design system's
	/// <c>InputBorder</c> wrapper is the only border drawn.
	/// </summary>
	/// <remarks>
	/// On Android these controls are backed by <c>AppCompatEditText</c>, which paints
	/// a Material underline. Inside an <c>InputBorder</c> that reads as two stacked
	/// borders. iOS needs nothing — <c>UITextField</c> defaults to no border, which is
	/// why the input styling was originally specced assuming no platform chrome existed.
	/// </remarks>
	private static void StripNativeInputChrome()
	{
#if ANDROID
		static void ClearBackground(Android.Views.View platformView)
		{
			platformView.Background = null;
			platformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
			// The underline is drawn as padding-inset background; drop the inset too
			// so text sits centred in the wrapper rather than riding high.
			platformView.SetPadding(0, 0, 0, 0);
		}

		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
			nameof(StripNativeInputChrome), (h, _) => ClearBackground(h.PlatformView));
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(
			nameof(StripNativeInputChrome), (h, _) => ClearBackground(h.PlatformView));
		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(
			nameof(StripNativeInputChrome), (h, _) => ClearBackground(h.PlatformView));
#endif
	}

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>();

		StripNativeInputChrome();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Register Services
		// Toggle between mock and real Bluetooth service
		if (UseMockBluetooth)
		{
			builder.Services.AddSingleton<IBluetoothService, MockBluetoothService>();
			System.Diagnostics.Debug.WriteLine("🔧 Using MOCK Bluetooth Service (no hardware required)");
		}
		else
		{
			builder.Services.AddSingleton<IBluetoothService, BluetoothService>();
			System.Diagnostics.Debug.WriteLine("📡 Using REAL Bluetooth Service (hardware required)");
		}

		builder.Services.AddSingleton<IGloveControlService, GloveControlService>();
		builder.Services.AddSingleton<IReconnectionService, ReconnectionService>();
		builder.Services.AddSingleton<BuzzahBuddy.Services.ConnectionStateManagement.IConnectionStateService, BuzzahBuddy.Services.ConnectionStateManagement.ConnectionStateService>();
		builder.Services.AddSingleton<IDataStorageService, PreferencesStorageService>();
		builder.Services.AddSingleton<BuzzahBuddy.Services.AppLifecycle.IAppLifecycleService, BuzzahBuddy.Services.AppLifecycle.AppLifecycleService>();

		// Register ViewModels
		// Tab pages and their ViewModels live for the app's lifetime (Shell caches TabBar content)
		builder.Services.AddSingleton<DeviceListViewModel>();
		builder.Services.AddSingleton<DeviceSettingsViewModel>();
		builder.Services.AddSingleton<GloveControlViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddTransient<CalibrationViewModel>();
		builder.Services.AddTransient<ProfileSettingsViewModel>();

		// Register Views
		builder.Services.AddSingleton<DeviceListPage>();
		builder.Services.AddSingleton<GloveControlPage>();
		builder.Services.AddSingleton<SettingsPage>();
		builder.Services.AddTransient<CalibrationPage>();
		builder.Services.AddTransient<ProfileSettingsPage>();

		return builder.Build();
	}
}
