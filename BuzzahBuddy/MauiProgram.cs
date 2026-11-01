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

	/// <summary>
	/// Gives every button icon its button's current text color, so the icon follows
	/// pressed and disabled states and theme changes the way the text does. A
	/// <c>FontImageSource</c> color is otherwise fixed: a disabled button showed grey
	/// text beside a white or blue icon.
	/// </summary>
	private static void TintButtonIconsWithText()
	{
		static void Tint(Microsoft.Maui.IButton view)
		{
			if (view is Button { ImageSource: FontImageSource icon, TextColor: { } color } && icon.Color != color)
				icon.Color = color;
		}

		// Text color changes with each visual state; the icon can be swapped by a trigger.
		Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(Button.TextColor), (_, view) => Tint(view));
		Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(Button.ImageSource), (_, view) => Tint(view));
	}

	public static MauiApp CreateMauiApp()
	{
		// Models and services that compile into the tests read the noun here, not from DeviceInfo.
		BuzzahBuddy.Models.DeviceNoun.Current = BuzzahBuddy.Helpers.DeviceWords.This;
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Lexend-Medium.ttf", "LexendMedium");
				fonts.AddFont("Lexend-SemiBold.ttf", "LexendSemiBold");
				fonts.AddFont("Lexend-Bold.ttf", "LexendBold");
				fonts.AddFont("AtkinsonHyperlegibleNext-Regular.ttf", "AtkinsonRegular");
				fonts.AddFont("AtkinsonHyperlegibleNext-Bold.ttf", "AtkinsonBold");
				fonts.AddFont("AtkinsonHyperlegibleNext-ExtraBold.ttf", "AtkinsonExtraBold");
				fonts.AddFont("AtkinsonHyperlegibleMono-Regular.ttf", "AtkinsonMonoRegular");
				fonts.AddFont("AtkinsonHyperlegibleMono-Bold.ttf", "AtkinsonMonoBold");
				fonts.AddFont("lucide.ttf", "Lucide");
			});

		StripNativeInputChrome();
		TintButtonIconsWithText();
#if IOS
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Shell, TabBarShellRenderer>());
#endif
		BuzzahBuddy.Helpers.Typography.RegisterHandlers();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Register Services
		// Real gloves, or the pretend ones while Demo Mode (Settings) is on; switchable at runtime.
		builder.Services.AddSingleton<BluetoothService>();
		builder.Services.AddSingleton<MockBluetoothService>();
		builder.Services.AddSingleton(sp => new SwitchableBluetoothService(
			sp.GetRequiredService<BluetoothService>(),
			sp.GetRequiredService<MockBluetoothService>(),
			Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false)));
		builder.Services.AddSingleton<IBluetoothService>(sp => sp.GetRequiredService<SwitchableBluetoothService>());

		builder.Services.AddSingleton<IGloveControlService, GloveControlService>();
		builder.Services.AddSingleton<IReconnectionService, ReconnectionService>();
		builder.Services.AddSingleton(sp => new AutoConnectService(
			sp.GetRequiredService<IBluetoothService>(),
			text => MainThread.BeginInvokeOnMainThread(() => SemanticScreenReader.Announce(text))));
		builder.Services.AddSingleton<BuzzahBuddy.Services.ConnectionStateManagement.IConnectionStateService, BuzzahBuddy.Services.ConnectionStateManagement.ConnectionStateService>();
		builder.Services.AddSingleton<IDataStorageService, PreferencesStorageService>();
		builder.Services.AddSingleton(_ => new HistoryStore(
			Path.Combine(FileSystem.AppDataDirectory, "history.json"),
			() => Preferences.Default.Get(HistoryStore.LegacyPreferencesKey, (string?)null),
			() => Preferences.Default.Remove(HistoryStore.LegacyPreferencesKey)));
		builder.Services.AddSingleton<BuzzahBuddy.Services.AppLifecycle.IAppLifecycleService, BuzzahBuddy.Services.AppLifecycle.AppLifecycleService>();
		builder.Services.AddSingleton<BuzzahBuddy.Services.Theme.IThemeService, BuzzahBuddy.Services.Theme.ThemeService>();
		builder.Services.AddSingleton<BuzzahBuddy.Services.Dialogs.IConfirmationService, BuzzahBuddy.Services.Dialogs.ConfirmationService>();

		// Register ViewModels
		// Tab pages and their ViewModels live for the app's lifetime (Shell caches TabBar content)
		builder.Services.AddSingleton<DeviceListViewModel>();
		builder.Services.AddSingleton<DeviceSettingsViewModel>();
		builder.Services.AddSingleton<GloveControlViewModel>();
		builder.Services.AddSingleton<HistoryViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddTransient<CalibrationViewModel>();
		builder.Services.AddTransient<ProfileSettingsViewModel>();

		// Register Views
		builder.Services.AddSingleton<DeviceListPage>();
		builder.Services.AddSingleton<GloveControlPage>();
		builder.Services.AddSingleton<HistoryPage>();
		builder.Services.AddSingleton<SettingsPage>();
		builder.Services.AddTransient<CalibrationPage>();
		builder.Services.AddTransient<ProfileSettingsPage>();

		return builder.Build();
	}
}
