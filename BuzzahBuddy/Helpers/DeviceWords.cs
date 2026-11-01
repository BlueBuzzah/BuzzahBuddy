using BuzzahBuddy.Models;

namespace BuzzahBuddy.Helpers;

/// <summary>
/// The noun for the device the app runs on. This is the one place the device idiom is read:
/// it names hardware for copy and never decides layout (layout follows window size).
/// </summary>
public static class DeviceWords
{
    /// <summary>"phone", "iPad" or "tablet", computed once.</summary>
    public static string This { get; } = DeviceNoun.For(Host());

    private static HostDevice Host()
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Tablet)
            return HostDevice.Phone;
        return DeviceInfo.Platform == DevicePlatform.iOS ? HostDevice.IPad : HostDevice.Tablet;
    }
}
