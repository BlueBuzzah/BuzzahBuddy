namespace BuzzahBuddy.Models;

/// <summary>The kind of device BuzzahBuddy is running on.</summary>
public enum HostDevice
{
    Phone,
    IPad,
    Tablet,
}

/// <summary>
/// The word user-facing copy uses for the device the app runs on ("this phone", "this iPad").
/// "Device" always means a glove, so the host gets its own noun. Never start a sentence with it:
/// "iPad" is cased for mid-sentence use.
/// </summary>
public static class DeviceNoun
{
    public static string For(HostDevice host) => host switch
    {
        HostDevice.IPad => "iPad",
        HostDevice.Tablet => "tablet",
        _ => "phone",
    };

    /// <summary>
    /// The noun for this device. The app sets it once at startup (from <c>DeviceWords.This</c>);
    /// code that compiles into the tests reads it here, since it can't reach MAUI's DeviceInfo.
    /// </summary>
    public static string Current { get; set; } = For(HostDevice.Phone);
}
