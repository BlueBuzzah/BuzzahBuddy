using System;
using Foundation;
using ObjCRuntime;

namespace BuzzahBuddy.Native.Binding;

// Binds native/BuzzahBuddyKit/Voice/VoiceBridge.swift. Selectors and names must match its @objc
// attributes exactly; a mismatch fails silently (Siri hears "Your gloves aren't connected.").

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
	/// </summary>
	[Abstract, Export("run:profileId:completion:")]
	void Run(string command, nint profileId, Action<NSString> completion);

	/// <summary>Complete with null to ask "End your session?", or with the reply to speak instead.</summary>
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

	[NullAllowed, Export("provider", ArgumentSemantic.Strong)]
	IVoiceCommandProvider Provider { get; set; }
}
