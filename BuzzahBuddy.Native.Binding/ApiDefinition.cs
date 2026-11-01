using System;
using Foundation;
using ObjCRuntime;

namespace BuzzahBuddy.Native.Binding;

// Binds native/BuzzahBuddyKit/Voice/VoiceBridge.swift. Selectors and names must match its @objc
// attributes exactly: a selector mismatch on the VoiceCommandProvider subclass crashes the app with
// an unrecognized selector when an intent calls it. Only a nil provider makes Siri say
// "Your gloves aren't connected." on its own.

/// <summary>
/// Implemented in C# and set on <see cref="VoiceBridge.Provider"/>; every Siri intent calls it.
/// </summary>
[Protocol(Name = "BBVoiceCommandProvider"), Model]
[BaseType(typeof(NSObject))]
interface VoiceCommandProvider
{
	/// <summary>
	/// <paramref name="command"/> is start, pause, resume, stop, battery or profile;
	/// <paramref name="profileId"/> 0 means none. Call <paramref name="completion"/> once with the reply.
	/// Called off the main thread; <paramref name="completion"/> may be called from any thread.
	/// </summary>
	[Abstract, Export("run:profileId:completion:")]
	void Run(string command, nint profileId, Action<NSString> completion);

	/// <summary>
	/// Complete with null to ask "End your session?", or with the reply to speak instead.
	/// Called off the main thread; <paramref name="completion"/> may be called from any thread.
	/// </summary>
	[Abstract, Export("stopPrecheckWithCompletion:")]
	void StopPrecheck(Action<NSString> completion);
}

interface IVoiceCommandProvider { }

[BaseType(typeof(NSObject), Name = "BBVoice")]
[DisableDefaultCtor]
interface VoiceBridge
{
	[Static, Export("shared")]
	VoiceBridge Shared { get; }

	// Written once in AppDelegate.FinishedLaunching, before any intent can run, so it needs no lock.
	[NullAllowed, Export("provider", ArgumentSemantic.Strong)]
	IVoiceCommandProvider Provider { get; set; }
}
