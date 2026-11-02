using BuzzahBuddy.Helpers;
using BuzzahBuddy.Native.Binding;
using BuzzahBuddy.Services.Glove;
using Foundation;

namespace BuzzahBuddy;

/// <summary>
/// Answers the Siri intents in native/BuzzahBuddyKit through <see cref="VoiceCommandService"/>.
/// Swift calls both methods off the main thread and takes the completion on any thread, so each
/// call returns at once and completes exactly once from the thread pool, whatever happens.
/// </summary>
sealed class VoiceCommandProvider : BuzzahBuddy.Native.Binding.VoiceCommandProvider
{
	// Resolved on first use: the provider is registered before base.FinishedLaunching builds the
	// services. A race resolves the same singleton twice, which is harmless.
	VoiceCommandService? _service;

	VoiceCommandService Service => _service ??=
		IPlatformApplication.Current?.Services.GetRequiredService<VoiceCommandService>()
		?? throw new InvalidOperationException("The app's services aren't built yet");

	public override void Run(string command, nint profileId, Action<NSString> completion) =>
		Task.Run(() => RunAsync(command, profileId, completion)).SafeFireAndForget("[VOICE]");

	public override void StopPrecheck(VoiceStopPrecheckCompletion completion) =>
		Task.Run(() => StopPrecheckAsync(completion)).SafeFireAndForget("[VOICE]");

	async Task RunAsync(string command, nint profileId, Action<NSString> completion)
	{
		var parsed = Parse(command);
		string reply;
		try
		{
			if (parsed is not { } voiceCommand)
				throw new ArgumentException($"Unknown command \"{command}\"", nameof(command));
			// Only the voice profiles (1-5) can be asked for; anything else is a plain Start.
			reply = await Service.RunAsync(voiceCommand, profileId is >= 1 and <= 5 ? (int)profileId : 0);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[VOICE] run {command} failed: {ex.GetType().Name}: {ex.Message}");
			reply = parsed switch
			{
				VoiceCommand.Pause => VoiceReplies.PauseFailed,
				VoiceCommand.Resume => VoiceReplies.ResumeFailed,
				VoiceCommand.Stop => VoiceReplies.StopFailed,
				_ => VoiceReplies.StartFailed,
			};
		}
		// Outside the try: a throw from the completion must not lead to a second call.
		completion(new NSString(reply));
	}

	async Task StopPrecheckAsync(VoiceStopPrecheckCompletion completion)
	{
		string? reply;
		try
		{
			reply = await Service.StopPrecheckAsync();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[VOICE] stopPrecheck failed: {ex.GetType().Name}: {ex.Message}");
			reply = VoiceReplies.StopFailed;
		}
		completion(reply is null ? null : new NSString(reply));
	}

	// Swift sends "stop" only after the person said yes to "End your session?".
	static VoiceCommand? Parse(string command) => command switch
	{
		"start" => VoiceCommand.Start,
		"pause" => VoiceCommand.Pause,
		"resume" => VoiceCommand.Resume,
		"stop" => VoiceCommand.Stop,
		"battery" => VoiceCommand.CheckBattery,
		"profile" => VoiceCommand.CheckProfile,
		_ => null,
	};
}
