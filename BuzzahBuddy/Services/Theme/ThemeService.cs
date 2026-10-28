using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using BuzzahBuddy.Resources.Styles.Themes;
using BuzzahBuddy.Services.AppLifecycle;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.Services.Theme;

public sealed class ThemeService : IThemeService
{
    private const string PreferenceKey = "ThemePreference";
    private const string HighContrastKey = "HighContrast";

    private bool _applied;

    public ThemeService(IAppLifecycleService lifecycle)
    {
        lifecycle.Resumed += (_, _) => Apply();
#if IOS
        Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            UIKit.UIView.DarkerSystemColorsStatusDidChangeNotification, _ => MainThread.BeginInvokeOnMainThread(Apply));
#endif
    }

    public ThemePreference Preference
    {
        get => (ThemePreference)Preferences.Default.Get(PreferenceKey, (int)ThemePreference.System);
        set
        {
            Preferences.Default.Set(PreferenceKey, (int)value);
            Apply();
        }
    }

    public bool HighContrast
    {
        get => Preferences.Default.Get(HighContrastKey, false);
        set
        {
            Preferences.Default.Set(HighContrastKey, value);
            Apply();
        }
    }

    public AppliedTheme Current { get; private set; } = AppliedTheme.Dark;

    public void Apply()
    {
        if (Application.Current is not { } app)
            return;

        // AppInfo reads the live trait; Application.PlatformAppTheme goes stale on iOS under Shell.
        var os = AppInfo.RequestedTheme switch
        {
            AppTheme.Light => OsAppearance.Light,
            AppTheme.Dark => OsAppearance.Dark,
            _ => OsAppearance.Unknown,
        };

        // Native chrome (alerts, keyboard, status bar) matches; left unset while following the OS.
        app.UserAppTheme = ThemeResolver.Native(Preference, os) switch
        {
            NativeAppearance.Light => AppTheme.Light,
            NativeAppearance.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        var theme = ThemeResolver.Resolve(Preference, HighContrast, os, OsAccessibility.HighContrast);
        if (_applied && theme == Current)
            return;

        var merged = app.Resources.MergedDictionaries;
        var old = merged.FirstOrDefault(d => d is LightTheme or DarkTheme or LightHcTheme or DarkHcTheme);
        ResourceDictionary next = theme switch
        {
            AppliedTheme.Light => new LightTheme(),
            AppliedTheme.LightHc => new LightHcTheme(),
            AppliedTheme.DarkHc => new DarkHcTheme(),
            _ => new DarkTheme(),
        };
        if (old is not null)
            merged.Remove(old);
        merged.Add(next);

        Current = theme;
        _applied = true;
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(theme));
    }
}
