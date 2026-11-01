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

    // Sentences for XAML to reference with {x:Static}. A binding to DeviceWords.This with a
    // StringFormat did not render under the iOS Release (AOT) build; a static string always does.
    public static string ApplyingNearNote { get; } = $"Make sure both gloves are on and near your {This}.";
    public static string NoGlovesBullet { get; } = $"• Your {This} is within 30 feet of the gloves.";
    public static string CustomProfileNote { get; } = $"These settings make up your Custom profile. They're saved on this {This}, and your gloves get them when you pick Custom and tap Apply Settings.";
    public static string DaysNote { get; } = $"Times are what this {This} recorded. Paused time isn't counted.";
    public static string HighContrastNote { get; } = $"Stronger colors and edges. When this is off, BuzzahBuddy follows your {This}'s setting.";
    public static string DataNote { get; } = $"Saved sessions and notes stay on this {This}.";

    private static HostDevice Host()
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Tablet)
            return HostDevice.Phone;
        return DeviceInfo.Platform == DevicePlatform.iOS ? HostDevice.IPad : HostDevice.Tablet;
    }
}
