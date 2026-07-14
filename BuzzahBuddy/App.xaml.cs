using BuzzahBuddy.Services.AppLifecycle;

namespace BuzzahBuddy;

public partial class App : Application
{
	private readonly IAppLifecycleService _lifecycle;

	public App(IAppLifecycleService lifecycle)
	{
		InitializeComponent();
		_lifecycle = lifecycle;

		// Force dark theme - BuzzahBuddy is dark-mode only (BlueBuzzah.com design)
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		window.Stopped += (_, _) => _lifecycle.NotifyStopped();
		window.Resumed += (_, _) => _lifecycle.NotifyResumed();
		return window;
	}
}