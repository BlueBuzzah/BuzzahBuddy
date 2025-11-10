using BuzzahBuddy.Views;

namespace BuzzahBuddy;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Register routes for navigation
		Routing.RegisterRoute("calibration", typeof(CalibrationPage));
	}
}
