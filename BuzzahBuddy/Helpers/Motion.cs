namespace BuzzahBuddy.Helpers;

/// <summary>
/// OS-level reduce-motion preference. Honoring this is mandatory — see the motion spec in
/// DESIGN_GUIDE.md ("Accessibility"): when true, collapse animations to instant (≤10ms),
/// press feedback becomes a color change only, and pulse effects render static at 0.7 opacity.
/// </summary>
public static class Motion
{
    /// <summary>
    /// True when the OS requests reduced motion. Animations must be skipped
    /// (or reduced to an instant state change) when this is true.
    /// </summary>
    public static bool Reduce
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            try
            {
                var resolver = Android.App.Application.Context.ContentResolver;
                return Android.Provider.Settings.Global.GetFloat(
                    resolver,
                    Android.Provider.Settings.Global.AnimatorDurationScale,
                    1f) == 0f;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
