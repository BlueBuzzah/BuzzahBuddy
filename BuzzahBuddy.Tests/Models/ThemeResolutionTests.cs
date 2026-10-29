using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class ThemeResolutionTests
{
    [Fact]
    public void Unknown_os_defaults_to_dark() =>
        Assert.Equal(AppliedTheme.Dark, ThemeResolver.Resolve(ThemePreference.System, false, OsAppearance.Unknown, false));

    [Fact]
    public void System_follows_os() =>
        Assert.Equal(AppliedTheme.Light, ThemeResolver.Resolve(ThemePreference.System, false, OsAppearance.Light, false));

    [Fact]
    public void Override_beats_os() =>
        Assert.Equal(AppliedTheme.Dark, ThemeResolver.Resolve(ThemePreference.Dark, false, OsAppearance.Light, false));

    [Fact]
    public void Os_contrast_picks_hc_twin() =>
        Assert.Equal(AppliedTheme.LightHc, ThemeResolver.Resolve(ThemePreference.System, false, OsAppearance.Light, true));

    [Fact]
    public void Switch_forces_hc() =>
        Assert.Equal(AppliedTheme.LightHc, ThemeResolver.Resolve(ThemePreference.Light, true, OsAppearance.Dark, false));

    [Fact]
    public void Switch_off_still_follows_os_contrast() =>
        Assert.Equal(AppliedTheme.DarkHc, ThemeResolver.Resolve(ThemePreference.Dark, false, OsAppearance.Dark, true));

    // Pinning the native appearance while following the OS would stop OS changes
    // from reaching the app; only an override (or an unknown OS) pins it.
    [Theory]
    [InlineData(ThemePreference.System, OsAppearance.Light, NativeAppearance.FollowOs)]
    [InlineData(ThemePreference.System, OsAppearance.Dark, NativeAppearance.FollowOs)]
    [InlineData(ThemePreference.System, OsAppearance.Unknown, NativeAppearance.Dark)]
    [InlineData(ThemePreference.Light, OsAppearance.Dark, NativeAppearance.Light)]
    [InlineData(ThemePreference.Dark, OsAppearance.Light, NativeAppearance.Dark)]
    public void Native_appearance_only_pinned_by_override(ThemePreference pref, OsAppearance os, NativeAppearance expected) =>
        Assert.Equal(expected, ThemeResolver.Native(pref, os));
}
