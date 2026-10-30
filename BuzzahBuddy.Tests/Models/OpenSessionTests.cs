using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class OpenSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 8, 0, 0, TimeSpan.FromHours(-7));

    private static SessionStatus S(SessionState state, int elapsed, int total = 7200) =>
        new() { Status = state, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total };

    private static OpenSession Fresh() =>
        OpenSession.Begin(S(SessionState.RUNNING, 0), T0, 2, "Noisy", "G1");

    private const int FwId = 184467;

    private static SessionStatus Sid(SessionState state, int elapsed, int total = 300) =>
        new() { Status = state, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total, SessionId = FwId };

    /// <summary>A 5-minute Quick Test begun from Start's reply, which carries the session id.</summary>
    private static OpenSession QuickTest() =>
        OpenSession.Begin(Sid(SessionState.RUNNING, 0), T0, 6, "Quick Test", "G1");

    private static LastSessionSummary Last(int elapsed, SessionEndReason reason, int id = FwId, int total = 300, int paused = 0) =>
        new(id, elapsed, paused, total, reason);

    [Fact]
    public void Begin_FreshStart_StartsNowWithNoTime()
    {
        var open = Fresh();
        Assert.Equal(T0, open.StartedAt);
        Assert.Equal(0, open.TherapySeconds);
        Assert.Equal(7200, open.PlannedSeconds);
        Assert.Equal("Noisy", open.ProfileName);
    }

    [Fact]
    public void Begin_MidSession_BackdatesStartAndCountsElapsed()
    {
        var open = OpenSession.Begin(S(SessionState.RUNNING, 600), T0, 2, "Noisy", "G1");
        Assert.Equal(T0.AddSeconds(-600), open.StartedAt);
        Assert.Equal(600, open.TherapySeconds);
    }

    [Theory]
    [InlineData(SessionState.RUNNING, 0, 7200, true)]
    [InlineData(SessionState.PAUSED, 10, 7200, true)]
    [InlineData(SessionState.LOW_BATTERY, 10, 7200, true)]
    [InlineData(SessionState.RUNNING, 0, 0, false)]   // Defect 7 ghost
    [InlineData(SessionState.IDLE, 0, 0, false)]
    public void CanTrack_RejectsGhostAndIdle(SessionState state, int elapsed, int total, bool expected) =>
        Assert.Equal(expected, OpenSession.CanTrack(S(state, elapsed, total)));

    [Fact]
    public void Observe_Running_AddsElapsedDelta()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 300), T0.AddSeconds(300));
        Assert.Equal(300, open.TherapySeconds);
    }

    [Fact]
    public void Observe_ExcludesTimeSpentPaused()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 300), T0.AddSeconds(300));
        open.Observe(S(SessionState.PAUSED, 310), T0.AddSeconds(310));
        Assert.Equal(310, open.TherapySeconds);
        open.Observe(S(SessionState.RUNNING, 910), T0.AddSeconds(910)); // firmware clock ran through the pause
        Assert.Equal(310, open.TherapySeconds);
        open.Observe(S(SessionState.RUNNING, 1000), T0.AddSeconds(1000));
        Assert.Equal(400, open.TherapySeconds);
    }

    [Fact]
    public void Continues_RejectsGhostAndOtherSessions()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 300), T0.AddSeconds(300));
        var now = T0.AddSeconds(400);
        Assert.False(open.Continues(S(SessionState.RUNNING, 0, 0), "G1", now));
        Assert.False(open.Continues(S(SessionState.RUNNING, 100), "G1", now));
        Assert.False(open.Continues(S(SessionState.RUNNING, 400), "G2", now));
        Assert.False(open.Continues(S(SessionState.IDLE, 0, 0), "G1", now));
        Assert.True(open.Continues(S(SessionState.RUNNING, 400), "G1", now));
    }

    [Fact]
    public void Continues_SessionThatMustHaveEnded_IsNotContinuedByALaterOne()
    {
        // A runs 30 s, the phone stops watching, A ends unseen; hours later the gloves
        // auto-start B with the same length and the phone joins 90 s into it.
        var open = Fresh();
        var seen = T0.AddSeconds(30);
        open.Observe(S(SessionState.RUNNING, 30), seen);
        var later = seen.AddHours(8);

        Assert.False(open.Continues(S(SessionState.RUNNING, 90), "G1", later));
        Assert.Equal(SessionOutcome.Completed, open.InferEnd(later));
        var record = open.Close(SessionOutcome.Completed, later)!;
        Assert.Equal(7200, record.TherapySeconds);
        Assert.Equal(T0.AddSeconds(7200), record.EndedAt);
    }

    [Fact]
    public void Continues_UnpolledStartIsNotContinuedByALaterStart()
    {
        var open = Fresh(); // begun from Start's synthesized 0/T, never polled
        Assert.False(open.Continues(S(SessionState.RUNNING, 0), "G1", T0.AddHours(3)));
    }

    [Theory]
    [InlineData(15, true)]  // exactly the remaining time plus the tolerance
    [InlineData(16, false)]
    public void Continues_EndsOnceRemainingTimePlusToleranceHasPassed(int beyondRemaining, bool expected)
    {
        var open = Fresh();
        var seen = T0.AddSeconds(30);
        open.Observe(S(SessionState.RUNNING, 30), seen);
        var now = seen.AddSeconds(7200 - 30 + beyondRemaining);
        Assert.Equal(expected, open.Continues(S(SessionState.RUNNING, 90), "G1", now));
    }

    [Fact]
    public void InferEnd_IdleAfterExpectedEnd_IsCompleted()
    {
        var open = Fresh();
        var t = T0.AddSeconds(7100);
        open.Observe(S(SessionState.RUNNING, 7100), t);
        Assert.Equal(SessionOutcome.Completed, open.InferEnd(t.AddSeconds(90)));
        Assert.Equal(SessionOutcome.EndedEarly, open.InferEnd(t.AddSeconds(60)));
    }

    [Fact]
    public void InferEnd_LastSeenPaused_IsEndedEarly()
    {
        var open = Fresh();
        open.Observe(S(SessionState.PAUSED, 7100), T0.AddSeconds(7100));
        Assert.Equal(SessionOutcome.EndedEarly, open.InferEnd(T0.AddHours(5)));
    }

    [Fact]
    public void Close_CompletedHoursLater_EndsAtProjectedEnd()
    {
        var open = Fresh();
        var t = T0.AddSeconds(7100);
        open.Observe(S(SessionState.RUNNING, 7100), t);
        var record = open.Close(SessionOutcome.Completed, t.AddHours(3))!;
        Assert.Equal(t.AddSeconds(100), record.EndedAt);
        Assert.Equal(7200, record.TherapySeconds);
        Assert.Equal(SessionOutcome.Completed, record.Outcome);
        Assert.Equal(open.Id, record.Id);
    }

    [Fact]
    public void Close_StoppedEarly_AddsTimeSinceLastReading()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 600), T0.AddSeconds(600));
        var record = open.Close(SessionOutcome.StoppedEarly, T0.AddSeconds(620))!;
        Assert.Equal(620, record.TherapySeconds);
        Assert.Equal(T0.AddSeconds(620), record.EndedAt);
    }

    [Fact]
    public void Close_EndedEarly_EndsAtLastReading()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 600), T0.AddSeconds(600));
        var record = open.Close(SessionOutcome.EndedEarly, T0.AddHours(1))!;
        Assert.Equal(600, record.TherapySeconds);
        Assert.Equal(T0.AddSeconds(600), record.EndedAt);
    }

    [Fact]
    public void Close_UnderAMinute_ReturnsNull()
    {
        var open = Fresh();
        open.Observe(S(SessionState.RUNNING, 30), T0.AddSeconds(30));
        Assert.Null(open.Close(SessionOutcome.StoppedEarly, T0.AddSeconds(30)));
    }

    [Fact]
    public void End_GlovesSwitchedOffUnwatched_RecordsTheirTimeNotACompletion()
    {
        // The hardware check: phone locked right after Start, gloves off at 2 min, back after 7.
        var open = QuickTest();
        open.Observe(Sid(SessionState.RUNNING, 5), T0.AddSeconds(5));
        var now = T0.AddMinutes(7);
        Assert.Equal(SessionOutcome.Completed, open.InferEnd(now)); // what the guess would say

        var (outcome, record) = open.End(Last(121, SessionEndReason.PowerOff), userRequestedStop: false, now);

        Assert.Equal(SessionOutcome.EndedEarly, outcome);
        Assert.Equal(121, record!.TherapySeconds);
        Assert.Equal(T0.AddSeconds(121), record.EndedAt);
        Assert.Equal(SessionOutcome.EndedEarly, record.Outcome);
        Assert.Equal(300, record.PlannedSeconds);
    }

    [Fact]
    public void End_ReturnBeforePlannedEnd_RecordsTheirTimeNotTheLastReading()
    {
        var open = QuickTest();
        open.Observe(Sid(SessionState.RUNNING, 30), T0.AddSeconds(30));
        var (outcome, record) = open.End(Last(200, SessionEndReason.ConnectionLost), false, T0.AddSeconds(240));
        Assert.Equal(SessionOutcome.EndedEarly, outcome);
        Assert.Equal(200, record!.TherapySeconds);
    }

    [Theory]
    [InlineData(SessionEndReason.Completed, SessionOutcome.Completed)]
    [InlineData(SessionEndReason.Stopped, SessionOutcome.StoppedEarly)]
    [InlineData(SessionEndReason.Battery, SessionOutcome.EndedEarly)]
    [InlineData(SessionEndReason.Error, SessionOutcome.EndedEarly)]
    [InlineData(SessionEndReason.Unknown, SessionOutcome.EndedEarly)]
    public void End_OutcomeComesFromTheGlovesReason(SessionEndReason reason, SessionOutcome expected)
    {
        var open = QuickTest();
        var (outcome, _) = open.End(Last(250, reason), false, T0.AddHours(1));
        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void End_UserStop_StillWins()
    {
        var open = QuickTest();
        var (outcome, record) = open.End(Last(250, SessionEndReason.Completed), userRequestedStop: true, T0.AddSeconds(250));
        Assert.Equal(SessionOutcome.StoppedEarly, outcome);
        Assert.Equal(250, record!.TherapySeconds);
    }

    [Fact]
    public void End_ExcludesPausedTime()
    {
        var open = QuickTest();
        var (_, record) = open.End(Last(300, SessionEndReason.Completed, paused: 90), false, T0.AddHours(1));
        Assert.Equal(210, record!.TherapySeconds);
        Assert.Equal(T0.AddSeconds(300), record.EndedAt);
    }

    [Fact]
    public void End_UnderAMinuteOfTherapy_IsNotSaved()
    {
        var open = QuickTest();
        var (outcome, record) = open.End(Last(45, SessionEndReason.PowerOff), false, T0.AddHours(1));
        Assert.Equal(SessionOutcome.EndedEarly, outcome);
        Assert.Null(record);
    }

    [Fact]
    public void End_SummaryOfAnotherSession_FallsBackToTheGuess()
    {
        // The summary persists until the next session ends: after a battery pull it still
        // describes the session before this one.
        var open = QuickTest();
        open.Observe(Sid(SessionState.RUNNING, 100), T0.AddSeconds(100));
        var now = T0.AddMinutes(7);

        var (outcome, record) = open.End(Last(121, SessionEndReason.PowerOff, id: FwId - 1), false, now);

        Assert.Equal(open.InferEnd(now), outcome);
        Assert.Equal(300, record!.TherapySeconds);
    }

    [Theory]
    [InlineData(600, 100)]  // different planned length
    [InlineData(300, 50)]   // ended before a reading this session already saw
    public void End_ImplausibleSummary_FallsBackToTheGuess(int total, int elapsed)
    {
        var open = QuickTest();
        open.Observe(Sid(SessionState.RUNNING, 60), T0.AddSeconds(60));
        Assert.False(open.IsSummaryOfThis(Last(elapsed, SessionEndReason.PowerOff, total: total)));
    }

    [Fact]
    public void End_OlderFirmware_NoIdOrSummary_FallsBackToTheGuess()
    {
        var open = OpenSession.Begin(S(SessionState.RUNNING, 0, 300), T0, 6, "Quick Test", "G1");
        Assert.Null(open.FirmwareSessionId);
        Assert.False(open.IsSummaryOfThis(Last(121, SessionEndReason.PowerOff)));

        var (outcome, _) = open.End(null, false, T0.AddMinutes(7));
        Assert.Equal(SessionOutcome.Completed, outcome);
    }

    [Fact]
    public void Observe_LearnsTheIdFromAPollWhenStartLackedIt()
    {
        var open = OpenSession.Begin(S(SessionState.RUNNING, 0, 300), T0, 6, "Quick Test", "G1");
        open.Observe(Sid(SessionState.RUNNING, 5), T0.AddSeconds(5));
        Assert.Equal(FwId, open.FirmwareSessionId);
    }

    [Fact]
    public void Continues_RejectsADifferentSessionId()
    {
        var open = QuickTest();
        var now = T0.AddSeconds(10);
        var other = Sid(SessionState.RUNNING, 10);
        other.SessionId = FwId + 1;
        Assert.False(open.Continues(other, "G1", now));
        Assert.True(open.Continues(Sid(SessionState.RUNNING, 10), "G1", now));
        Assert.True(open.Continues(S(SessionState.RUNNING, 10, 300), "G1", now)); // synthesized, no id
    }
}
