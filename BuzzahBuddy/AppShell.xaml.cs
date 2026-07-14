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
	}
}
