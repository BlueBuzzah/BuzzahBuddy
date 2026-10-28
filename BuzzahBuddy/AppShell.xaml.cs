using BuzzahBuddy.Helpers;
using BuzzahBuddy.Views;

namespace BuzzahBuddy;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Register routes for navigation
		Routing.RegisterRoute(Routes.Calibration, typeof(CalibrationPage));
		Routing.RegisterRoute(Routes.ProfileSettings, typeof(ProfileSettingsPage));

		Loaded += OnFirstLoaded;
	}

	/// <summary>
	/// Shows the safety notices once, before first use. Pushed through the dispatcher:
	/// iOS rejects a modal pushed before the shell's view is in the window.
	/// </summary>
	private void OnFirstLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnFirstLoaded;
		if (Preferences.Default.Get(WelcomePage.AcceptedKey, false))
			return;
		Dispatcher.Dispatch(async () => await Navigation.PushModalAsync(new WelcomePage(), animated: !Motion.Reduce));
	}
}
