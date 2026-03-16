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
	private const bool UseMockBluetooth = false;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

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
		builder.Services.AddSingleton<IDataStorageService, PreferencesStorageService>();

		// Register ViewModels
		builder.Services.AddTransient<MainPageViewModel>();
		builder.Services.AddTransient<DeviceListViewModel>();
		builder.Services.AddTransient<GloveControlViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<CalibrationViewModel>();

		// Register Views
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<DeviceListPage>();
		builder.Services.AddTransient<GloveControlPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<CalibrationPage>();

		return builder.Build();
	}
}
