namespace BuzzahBuddy.Helpers;

/// <summary>
/// OS accessibility settings the design system maps (accessibility.md "Follow the
/// person's settings") that MAUI doesn't expose.
/// </summary>
public static class OsAccessibility
{
    /// <summary>iOS Bold Text, Android 12+ font weight adjustment.</summary>
    public static bool BoldText
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.IsBoldTextEnabled;
#elif ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(31)
                && Android.App.Application.Context.Resources?.Configuration?.FontWeightAdjustment > 0;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// iOS Increase Contrast; Android 14+ contrast level above default. Older Android
    /// has no public API, so only the in-app switch applies there.
    /// </summary>
    public static bool HighContrast
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.DarkerSystemColorsEnabled;
#elif ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(34)
                && Android.App.Application.Context.GetSystemService(Android.Content.Context.UiModeService)
                    is Android.App.UiModeManager { Contrast: > 0 };
#else
            return false;
#endif
        }
    }

    /// <summary>The OS text-size multiplier (1 = default, 2 = 200%).</summary>
    public static double TextScale
    {
        get
        {
#if IOS
            return UIKit.UIFontMetrics.DefaultMetrics.GetScaledValue(1);
#elif ANDROID
            return Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1;
#else
            return 1;
#endif
        }
    }
}
