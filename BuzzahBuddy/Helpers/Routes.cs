namespace BuzzahBuddy.Helpers;

/// <summary>
/// Shell navigation routes. Absolute (//) routes are TabBar tabs declared in AppShell.xaml;
/// relative routes are pushed pages registered in AppShell.xaml.cs.
/// </summary>
public static class Routes
{
    public const string Home = "//home";
    public const string Control = "//control";
    public const string Devices = "//devices";
    public const string Settings = "//settings";
    public const string Calibration = "calibration";
    public const string ProfileSettings = "profilesettings";
}
