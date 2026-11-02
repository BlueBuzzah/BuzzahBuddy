using BuzzahBuddy.Native.Binding;
using CoreSpotlight;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace BuzzahBuddy;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Before base.FinishedLaunching, so an intent that launched the app finds the provider. Its
		// commands wait for ServicesReady: the services (and App, which builds the glove services on
		// this thread) only exist once base.FinishedLaunching returns.
		VoiceCommandProvider? voice = null;
		if (OperatingSystem.IsIOSVersionAtLeast(16))
			VoiceBridge.Shared.Provider = voice = new VoiceCommandProvider();
		IndexForSpotlight();
		var launched = base.FinishedLaunching(application, launchOptions);
		voice?.ServicesReady();
		return launched;
	}

	// Spotlight matches only the app's name, so this item is what makes searching "BlueBuzzah"
	// find the app. Tapping it just opens the app. Indexed on every launch (same ID, so it
	// replaces itself) and never expires; the default expiry is about a month.
	static void IndexForSpotlight()
	{
		if (!CSSearchableIndex.IsIndexingAvailable)
			return;

		var attributes = new CSSearchableItemAttributeSet(UTTypes.Content)
		{
			Title = "BlueBuzzah",
			ContentDescription = "Run sessions with your BlueBuzzah gloves.",
			Keywords = ["BlueBuzzah", "BuzzahBuddy"],
		};
		var item = new CSSearchableItem("bluebuzzah", "com.rbonestell.buzzahbuddy", attributes)
		{
			ExpirationDate = NSDate.DistantFuture,
		};
		CSSearchableIndex.DefaultSearchableIndex.Index([item], _ => { });
	}
}
