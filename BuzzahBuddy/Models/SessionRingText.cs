namespace BuzzahBuddy.Models;

/// <summary>The SessionRing's four states (SessionRing README).</summary>
public enum SessionRingState { Idle, Running, Paused, Complete }

/// <summary>The ring's state words and spoken label, after bundle.js SessionRing.</summary>
public static class SessionRingText
{
    public static string Word(SessionRingState state) => state switch
    {
        SessionRingState.Running => "Running",
        SessionRingState.Paused => "Paused",
        SessionRingState.Complete => "Complete",
        _ => "Ready",
    };

    /// <summary>"Session running, 1 hour 42 minutes left", or "Session complete".</summary>
    public static string Accessible(SessionRingState state, int remainingSeconds) =>
        state == SessionRingState.Complete
            ? "Session complete"
            : $"Session {Word(state).ToLowerInvariant()}, {DurationText.Spoken(remainingSeconds)} left";

    /// <summary>When the session length is unknown and the ring counts up.</summary>
    public static string AccessibleCountUp(int elapsedSeconds) =>
        $"Session running, {DurationText.Spoken(elapsedSeconds)} so far";
}
