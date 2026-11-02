using System;
using Foundation;
using ObjCRuntime;

namespace BuzzahBuddy.Native.Binding;

// Binds native/BuzzahBuddyKit/Voice/VoiceBridge.swift. Selectors and names must match its @objc
// attributes exactly: a selector mismatch on the VoiceCommandProvider subclass crashes the app with
// an unrecognized selector when an intent calls it. Only a provider still nil after Swift's 3 s wait
// makes Siri say "Your gloves aren't connected." on its own.

/// <summary>
/// Implemented in C# and set on <see cref="VoiceBridge.Provider"/>; every Siri intent calls it.
/// </summary>
[Protocol(Name = "BBVoiceCommandProvider"), Model]
[BaseType(typeof(NSObject))]
interface VoiceCommandProvider
{
	/// <summary>
	/// <paramref name="command"/> is start, pause, resume, stop, battery or profile;
	/// <paramref name="profileId"/> 0 means none. Call <paramref name="completion"/> exactly once, on
	/// every path, with a non-null reply: Siri waits for it. Called off the main thread;
	/// <paramref name="completion"/> may be called from any thread.
	/// </summary>
	[Abstract, Export("run:profileId:completion:")]
	void Run(string command, nint profileId, Action<NSString> completion);

	/// <summary>
	/// Call <paramref name="completion"/> exactly once: with null to ask "End your session?", or with
	/// the reply to speak instead. Called off the main thread; <paramref name="completion"/> may be
	/// called from any thread.
	/// </summary>
	[Abstract, Export("stopPrecheckWithCompletion:")]
	void StopPrecheck(VoiceStopPrecheckCompletion completion);
}

/// <summary>Swift's <c>(String?) -> Void</c>: null means "ask for confirmation".</summary>
delegate void VoiceStopPrecheckCompletion([NullAllowed] NSString reply);

interface IVoiceCommandProvider { }

[BaseType(typeof(NSObject), Name = "BBVoice")]
[DisableDefaultCtor]
interface VoiceBridge
{
	[Static, Export("shared")]
	VoiceBridge Shared { get; }

	// Written once in AppDelegate.FinishedLaunching, before base.FinishedLaunching. Nothing orders a
	// background cold launch's intent after that, so Swift locks it and waits up to 3 s for it. An
	// intent can also arrive before the app's services exist; the C# provider waits for them itself.
	[NullAllowed, Export("provider", ArgumentSemantic.Strong)]
	IVoiceCommandProvider Provider { get; set; }
}
