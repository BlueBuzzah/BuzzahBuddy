using System.Diagnostics;
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
		// Before base.FinishedLaunching, so an intent that launched the app finds the provider.
		if (OperatingSystem.IsIOSVersionAtLeast(16))
			VoiceBridge.Shared.Provider = new SpikeVoiceProvider(Stopwatch.GetTimestamp());
		IndexForSpotlight();
		return base.FinishedLaunching(application, launchOptions);
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

	// SPIKE (replaced in Task 5): answers every Siri command with "Spike <command> <profileId>" and
	// logs timings for the device checks. Console.WriteLine reaches the device log (Console.app)
	// in Release too, unlike Debug.WriteLine.
	sealed class SpikeVoiceProvider : VoiceCommandProvider
	{
		// Taken in FinishedLaunching, milliseconds after process start.
		readonly long _launchTimestamp;

		public SpikeVoiceProvider(long launchTimestamp) => _launchTimestamp = launchTimestamp;

		void Log(string message) =>
			Console.WriteLine($"[VOICE] {message} launch+{(long)Stopwatch.GetElapsedTime(_launchTimestamp).TotalMilliseconds}");

		public override void Run(string command, nint profileId, Action<NSString> completion)
		{
			Log($"run {command} {profileId}");
			var reply = new NSString($"Spike {command} {profileId}");
			switch (command)
			{
				case "battery":
					// Measures how long Siri waits for perform(): the last second logged before it gives up.
					Task.Run(async () =>
					{
						for (var second = 1; second <= 30; second++)
						{
							await Task.Delay(1000);
							Log($"battery wait {second}s");
						}
						completion(reply);
					});
					break;
				case "start" when profileId > 0:
					// Replies at once, then proves the app keeps running after perform() returns.
					completion(reply);
					Task.Run(async () =>
					{
						for (var second = 1; second <= 60; second++)
						{
							await Task.Delay(1000);
							Log($"start background {second}s");
						}
					});
					break;
				default:
					completion(reply);
					break;
			}
		}

		public override void StopPrecheck(Action<NSString> completion)
		{
			Log("stopPrecheck");
			completion(null!);
		}
	}
}
