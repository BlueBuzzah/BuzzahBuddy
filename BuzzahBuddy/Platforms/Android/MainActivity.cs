using Android.App;
using Android.Content.PM;
using Android.OS;

namespace BuzzahBuddy;

// ScreenOrientation.Portrait below is honoured on phones only. At targetSdk 36
// Android ignores screenOrientation, resizeableActivity and aspect-ratio limits
// on any display >=600dp wide (tablets, foldables), so those devices can rotate
// and resize freely. That is an accepted trade-off, not an oversight - verify
// landscape rather than fight it, paying attention to SessionTimerView's fixed
// 260x260 ring. If a hard lock is ever required, the manifest opt-out is
// <property android:name="android.window.PROPERTY_COMPAT_ALLOW_RESTRICTED_RESIZABILITY"
// android:value="true" /> - itself removed at API 37.

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
