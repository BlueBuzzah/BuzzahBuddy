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
}
