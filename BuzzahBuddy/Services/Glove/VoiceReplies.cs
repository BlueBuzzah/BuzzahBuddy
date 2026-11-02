using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Glove;

public enum VoiceCommand { Start, Pause, Resume, Stop, CheckBattery, CheckProfile }

public enum VoiceAction { None, Start, SwitchThenStart, Pause, Resume, ConfirmStop }

/// <summary>What the gloves are doing right now, as the voice service read it.</summary>
public sealed record VoiceSnapshot(
    bool Connected, SessionStatus Session, int DeviceProfileId,
    double? PrimaryVoltage, bool? PrimaryLow, double? SecondaryVoltage, bool? SecondaryLow,
    bool? SecondaryLinked, bool Switching);

/// <summary>What Siri says and the action to run, if any. ProfileId is the profile the action targets.</summary>
public sealed record VoiceDecision(string Reply, VoiceAction Action, int ProfileId = 0);

/// <summary>
/// Pure decision table for the Siri commands: the reply to speak and the action to run for a
/// command and a snapshot of glove state, and the reply once an action has run.
/// </summary>
public static class VoiceReplies
{
    public const string NotConnected = "Your gloves aren't connected.";
    public const string Restarting = "Your gloves are restarting. Your session will start soon.";
    public const string NeedsCharging = "Your gloves need charging before a session.";
    public const string NoSecondary = "Your secondary glove isn't connected.";
    public const string StartFailed = "Couldn't start your session. Open BuzzahBuddy to try again.";
    public const string Paused = "Session paused.";
    public const string AlreadyPaused = "Your session is already paused.";
    public const string NoSession = "No session is running.";
    public const string Resumed = "Session resumed.";
    public const string ResumeWhileRunning = "Your session is already running.";
    public const string ConfirmStop = "End your session?";
    public const string Ended = "Session ended.";
    public const string StopDeclined = "Okay, your session keeps going.";
    public const string PauseFailed = "Couldn't pause your session. Open BuzzahBuddy to try again.";
    public const string ResumeFailed = "Couldn't resume your session. Open BuzzahBuddy to try again.";
    public const string StopFailed = "Couldn't end your session. Open BuzzahBuddy to try again.";
    /// <summary>Check Profile while connected but the profile wasn't read (INFO failed, or no PROFILE key).</summary>
    public const string ProfileUnreadable = "Couldn't read your gloves' profile. Open BuzzahBuddy to try again.";

    public static string Starting(string? profile) => $"Starting your {Named(profile)}session.";
    public static string AlreadyRunning(string? profile) => $"Your {Named(profile)}session is already running.";
    public static string AlreadyPausedStart(string? profile) => $"Your {Named(profile)}session is paused.";
    public static string Switching(string profile) => $"Switching to {profile}. Your session starts when the gloves restart.";

    public static VoiceDecision Decide(VoiceCommand command, int requestedProfileId, VoiceSnapshot s)
    {
        if (!s.Connected)
            return new(NotConnected, VoiceAction.None);

        // 0.00 V is the firmware's "no reading"; never trust the caller to have mapped it.
        s = s with
        {
            PrimaryVoltage = BatteryReading.FromRaw(s.PrimaryVoltage),
            SecondaryVoltage = BatteryReading.FromRaw(s.SecondaryVoltage),
        };

        return command switch
        {
            VoiceCommand.Start => DecideStart(requestedProfileId, s),
            VoiceCommand.Pause => s.Switching || !s.Session.IsActive ? None(NoSession)
                : s.Session.IsPaused ? None(AlreadyPaused)
                : new(Paused, VoiceAction.Pause),
            VoiceCommand.Resume => s.Switching || !s.Session.IsActive ? None(NoSession)
                : s.Session.IsPaused ? new(Resumed, VoiceAction.Resume)
                : None(ResumeWhileRunning),
            VoiceCommand.Stop => s.Switching || !s.Session.IsActive
                ? None(NoSession)
                : new(ConfirmStop, VoiceAction.ConfirmStop),
            VoiceCommand.CheckBattery => None(Battery(s)),
            VoiceCommand.CheckProfile => None(ProfileSummary(s)),
            _ => None(NoSession),
        };
    }

    /// <summary>The reply once <paramref name="action"/> ran; <paramref name="error"/> is null on success.</summary>
    public static string Result(VoiceAction action, int profileId, Exception? error) => action switch
    {
        VoiceAction.Start or VoiceAction.SwitchThenStart when error is not null => StartError(error),
        VoiceAction.Start => Starting(NameOf(profileId)),
        VoiceAction.SwitchThenStart => Switching(NameOf(profileId) ?? "that profile"),
        VoiceAction.Pause => error is null ? Paused : PauseFailed,
        VoiceAction.Resume => error is null ? Resumed : ResumeFailed,
        VoiceAction.ConfirmStop => error is null ? Ended : StopFailed,
        _ => string.Empty,
    };

    private static VoiceDecision DecideStart(int requestedProfileId, VoiceSnapshot s)
    {
        var name = NameOf(s.DeviceProfileId);
        if (s.Session.IsRunning)
            return None(AlreadyRunning(name));
        if (s.Session.IsPaused)
            return None(AlreadyPausedStart(name));
        if (s.Switching)
            return None(Restarting);
        if (BatteryReading.IsLow(s.PrimaryVoltage, s.PrimaryLow) || BatteryReading.IsLow(s.SecondaryVoltage, s.SecondaryLow))
            return None(NeedsCharging);
        if (s.SecondaryLinked == false)
            return None(NoSecondary);
        // Only the voice profiles (1-5) can be requested; anything else is a plain Start.
        if (requestedProfileId is < 1 or > 5 || requestedProfileId == s.DeviceProfileId)
            return new(Starting(name), VoiceAction.Start, s.DeviceProfileId);
        return new(Switching(NameOf(requestedProfileId) ?? "that profile"), VoiceAction.SwitchThenStart, requestedProfileId);
    }

    private static string StartError(Exception error)
    {
        var message = error is BlueBuzzahCommandException ? error.Message : string.Empty;
        if (message.Contains("Battery too low", StringComparison.OrdinalIgnoreCase))
            return NeedsCharging;
        if (IsSecondaryNotConnected(error))
            return NoSecondary;
        return StartFailed;
    }

    /// <summary>The firmware refused SESSION_START because the secondary glove isn't linked to the primary.</summary>
    public static bool IsSecondaryNotConnected(Exception error) =>
        error is BlueBuzzahCommandException
        && error.Message.Contains("Secondary not connected", StringComparison.OrdinalIgnoreCase);

    private static string Battery(VoiceSnapshot s) =>
        $"Primary glove {Glove(s.PrimaryVoltage, s.PrimaryLow)}, secondary {Glove(s.SecondaryVoltage, s.SecondaryLow)}.";

    private static string Glove(double? voltage, bool? low) =>
        voltage is not { } v
            ? "unavailable"
            : $"{BatteryReading.ToPercentage(v)} percent" + (BatteryReading.IsLow(v, low) ? ", low" : "");

    private static string ProfileSummary(VoiceSnapshot s)
    {
        var name = NameOf(s.DeviceProfileId);
        var session = s.Session;
        if (!session.IsRunning && !session.IsPaused)
            return name is null ? ProfileUnreadable : $"Your gloves are set to {name}.";

        var state = session.IsPaused ? "paused" : "running";
        var remaining = session.TotalTimeSeconds - session.ElapsedTimeSeconds;
        var left = session.TotalTimeSeconds > 0 && remaining > 0 ? $", {DurationText.Spoken(Math.Max(remaining, 30))} left" : "";
        return $"Your {Named(name)}session is {state}{left}.";
    }

    private static VoiceDecision None(string reply) => new(reply, VoiceAction.None);

    private static string Named(string? profile) => profile is null ? "" : profile + " ";

    private static string? NameOf(int profileId) =>
        TherapyProfile.GetPresetProfiles().FirstOrDefault(p => p.ProfileId == profileId)?.Name;
}
