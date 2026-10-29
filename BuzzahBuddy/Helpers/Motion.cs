namespace BuzzahBuddy.Helpers;

/// <summary>
/// OS-level reduce-motion preference. Honoring this is mandatory — see the BlueBuzzah design
/// system's "Motion and haptics" and accessibility.md. When true, every transition is instant
/// (0 ms) and the "ready" breath becomes a static halo.
/// </summary>
public static class Motion
{
    /// <summary>The design system's <c>ease-standard</c>, cubic-bezier(0.2, 0, 0, 1).</summary>
    public static readonly Easing Standard = new(CubicBezier.Standard);

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
