using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
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
	/// Shows Welcome until the person has agreed to the current Terms (first use, and again
	/// after the Terms effective date changes). Pushed through the dispatcher:
	/// iOS rejects a modal pushed before the shell's view is in the window.
	/// </summary>
	private void OnFirstLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnFirstLoaded;
		if (!TermsGate.NeedsAcceptance(Preferences.Default.Get<string?>(WelcomePage.TermsAcceptedDateKey, null)))
			return;
		Dispatcher.Dispatch(async () => await Navigation.PushModalAsync(new WelcomePage(), animated: !Motion.Reduce));
	}
}
