using BuzzahBuddy.Helpers;
using BuzzahBuddy.Services.AppLifecycle;
using BuzzahBuddy.Services.Theme;

namespace BuzzahBuddy;

public partial class App : Application
{
	private readonly IAppLifecycleService _lifecycle;
	private readonly IThemeService _theme;

	public App(IAppLifecycleService lifecycle, IThemeService theme)
	{
		InitializeComponent();
		_lifecycle = lifecycle;
		_theme = theme;
		Typography.ApplyFontWeights(OsAccessibility.BoldText);

		// One of four design-system themes, from the OS settings and the in-app override.
		theme.Apply();
		RequestedThemeChanged += (_, _) => theme.Apply();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		window.Stopped += (_, _) => _lifecycle.NotifyStopped();
		// Appearance toggled from Control Center or the notification shade reactivates the app.
		window.Activated += (_, _) => _theme.Apply();
#if IOS
		// RequestedThemeChanged doesn't fire under Shell on iOS; watch the window's style directly.
		window.HandlerChanged += (_, _) =>
		{
			if (window.Handler?.PlatformView is UIKit.UIWindow platformWindow && OperatingSystem.IsIOSVersionAtLeast(17))
				platformWindow.RegisterForTraitChanges(
					new[] { typeof(UIKit.UITraitUserInterfaceStyle) }, (UIKit.IUITraitEnvironment _, UIKit.UITraitCollection _) => _theme.Apply());
		};
#endif
		window.Resumed += (_, _) =>
		{
			Typography.ApplyFontWeights(OsAccessibility.BoldText);
			_lifecycle.NotifyResumed();
		};
		return window;
	}
}