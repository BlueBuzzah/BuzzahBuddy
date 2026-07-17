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

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>();

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
