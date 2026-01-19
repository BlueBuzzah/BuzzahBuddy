namespace BuzzahBuddy;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Force dark theme - BuzzahBuddy is dark-mode only (BlueBuzzah.com design)
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}