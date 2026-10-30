namespace BuzzahBuddy.Models;

/// <summary>
/// A session the app is tracking that has not been written to history yet. Persisted so a
/// session survives the app being killed or the link dropping.
/// <para>
/// Older firmware never tells the phone how a session finished (current firmware reports its
/// last session; see <see cref="End"/>). Older firmware's ELAPSED also keeps counting through a
/// pause, so without a session id delivered time is the sum of ELAPSED gains taken only while
/// the session was running, and a session that finished while the phone was away can read
/// RUNNING with ELAPSED:0 TOTAL:0 after a reconnect, so a reading with TOTAL of 0 is never tracked. On
/// completion the engine stops in the same tick that ELAPSED reaches TOTAL, so the next reading
/// is IDLE with 0/0/0 and PROGRESS never reaches 100. The end is therefore inferred from the
/// time since the last reading showing the session running.
/// </para>
/// </summary>
public sealed class OpenSession
{
    /// <summary>
    /// Slack allowed when deciding a session ran to its end: one 5 s poll plus the ~5 s
    /// command timeout, plus margin.
    /// </summary>
    public const int CompletionToleranceSeconds = 15;

    /// <summary>
    /// Sessions with less delivered time than this are not saved.
    /// </summary>
    public const int MinimumSavedSeconds = 60;

    public string Id { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public int ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public string? DeviceId { get; set; }
    public int PlannedSeconds { get; set; }

    /// <summary>
    /// The gloves' SESSION_ID for this session; null on older firmware.
    /// </summary>
    public int? FirmwareSessionId { get; set; }

    /// <summary>
    /// Seconds of therapy delivered so far, excluding paused time.
    /// </summary>
    public int TherapySeconds { get; set; }

    /// <summary>
    /// ELAPSED from the last reading.
    /// </summary>
    public int LastElapsedSeconds { get; set; }

    /// <summary>
    /// Whether the last reading showed the session running.
    /// </summary>
    public bool LastRunning { get; set; }

    /// <summary>
    /// When the last reading was taken.
    /// </summary>
    public DateTimeOffset LastObservedAt { get; set; }

    /// <summary>
    /// Whether a reading describes a real session worth tracking.
    /// </summary>
    public static bool CanTrack(SessionStatus status) =>
        status.IsActive && status.TotalTimeSeconds > 0;

    /// <summary>
    /// Starts tracking from a reading. Backdates the start by the elapsed time, which counts
    /// as delivered (the app may join a session already in progress), plus any time paused.
    /// </summary>
    public static OpenSession Begin(SessionStatus status, DateTimeOffset now, int profileId, string profileName, string? deviceId) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            StartedAt = now.AddSeconds(-(status.ElapsedTimeSeconds + (status.PausedSeconds ?? 0))),
            ProfileId = profileId,
            ProfileName = profileName,
            DeviceId = deviceId,
            PlannedSeconds = status.TotalTimeSeconds,
            FirmwareSessionId = status.SessionId,
            TherapySeconds = status.ElapsedTimeSeconds,
            LastElapsedSeconds = status.ElapsedTimeSeconds,
            LastRunning = status.IsRunning,
            LastObservedAt = now
        };

    /// <summary>
    /// Whether a reading belongs to this session rather than a ghost or a different one. A
    /// matching session id decides it: a pause the phone never saw can outlast the remaining
    /// time. Without ids (older firmware, whose clock runs through pauses), a session last seen
    /// running is over once more than its remaining time (plus tolerance) has passed; a reading
    /// after that is a later session.
    /// </summary>
    public bool Continues(SessionStatus status, string? deviceId, DateTimeOffset now) =>
        CanTrack(status)
        && status.TotalTimeSeconds == PlannedSeconds
        && status.ElapsedTimeSeconds >= LastElapsedSeconds
        && (DeviceId is null || DeviceId == deviceId)
        && (FirmwareSessionId is not null && status.SessionId is not null
            ? FirmwareSessionId == status.SessionId
            : !(LastRunning
                && (now - LastObservedAt).TotalSeconds > PlannedSeconds - LastElapsedSeconds + CompletionToleranceSeconds));

    /// <summary>
    /// Records a reading. Firmware that reports a session id holds ELAPSED while paused, so every
    /// gain is therapy; on older firmware time counts only when the previous reading showed the
    /// session running.
    /// </summary>
    public void Observe(SessionStatus status, DateTimeOffset now)
    {
        if (LastRunning || status.SessionId is not null)
        {
            TherapySeconds += Math.Max(0, status.ElapsedTimeSeconds - LastElapsedSeconds);
        }
        TherapySeconds = Math.Min(TherapySeconds, PlannedSeconds);
        LastElapsedSeconds = status.ElapsedTimeSeconds;
        LastRunning = status.IsRunning;
        LastObservedAt = now;
        FirmwareSessionId ??= status.SessionId;
    }

    /// <summary>
    /// Closes the session when a reading shows it is over. The gloves' summary of their last
    /// session gives the exact outcome and time when it is this session; otherwise (older
    /// firmware, a summary of another session) the end is inferred from timing.
    /// </summary>
    public (SessionOutcome Outcome, SessionRecord? Record) End(LastSessionSummary? last, bool userRequestedStop, DateTimeOffset now)
    {
        if (!IsSummaryOfThis(last))
        {
            var outcome = userRequestedStop ? SessionOutcome.StoppedEarly : InferEnd(now);
            return (outcome, Close(outcome, now));
        }

        var reported = userRequestedStop ? SessionOutcome.StoppedEarly : last!.Reason switch
        {
            SessionEndReason.Completed => SessionOutcome.Completed,
            SessionEndReason.Stopped => SessionOutcome.StoppedEarly,
            _ => SessionOutcome.EndedEarly
        };
        if (last!.TherapySeconds < MinimumSavedSeconds)
            return (reported, null);

        return (reported, new SessionRecord
        {
            Id = Id,
            StartedAt = StartedAt,
            EndedAt = StartedAt.AddSeconds(last.ElapsedSeconds),
            TherapySeconds = last.TherapySeconds,
            PlannedSeconds = PlannedSeconds,
            ProfileId = ProfileId,
            ProfileName = ProfileName,
            Outcome = reported,
            DeviceId = DeviceId
        });
    }

    /// <summary>
    /// Whether the gloves' summary describes this session. The id is exact; the other checks
    /// guard against a corrupt or foreign reading.
    /// </summary>
    public bool IsSummaryOfThis(LastSessionSummary? last) =>
        last is not null
        && FirmwareSessionId is not null
        && last.Id == FirmwareSessionId
        && last.TotalSeconds == PlannedSeconds
        && last.ElapsedSeconds >= LastElapsedSeconds;

    /// <summary>
    /// Infers how the session ended when the app was not watching.
    /// </summary>
    public SessionOutcome InferEnd(DateTimeOffset now)
    {
        var remaining = PlannedSeconds - LastElapsedSeconds;
        var gap = (now - LastObservedAt).TotalSeconds;
        return LastRunning && gap >= remaining - CompletionToleranceSeconds
            ? SessionOutcome.Completed
            : SessionOutcome.EndedEarly;
    }

    /// <summary>
    /// Closes the session into a history record, or returns null when too short to keep.
    /// </summary>
    public SessionRecord? Close(SessionOutcome outcome, DateTimeOffset now)
    {
        var remaining = Math.Max(0, PlannedSeconds - LastElapsedSeconds);
        var therapy = TherapySeconds;

        if (LastRunning && outcome is SessionOutcome.Completed or SessionOutcome.StoppedEarly)
        {
            var tail = (int)Math.Clamp((now - LastObservedAt).TotalSeconds, 0, remaining);
            therapy = Math.Min(therapy + tail, PlannedSeconds);
        }

        if (therapy < MinimumSavedSeconds)
        {
            return null;
        }

        var endedAt = outcome switch
        {
            SessionOutcome.Completed => LastObservedAt.AddSeconds(remaining),
            SessionOutcome.StoppedEarly => now,
            _ => LastObservedAt
        };

        return new SessionRecord
        {
            Id = Id,
            StartedAt = StartedAt,
            EndedAt = endedAt,
            TherapySeconds = therapy,
            PlannedSeconds = PlannedSeconds,
            ProfileId = ProfileId,
            ProfileName = ProfileName,
            Outcome = outcome,
            DeviceId = DeviceId
        };
    }
}
