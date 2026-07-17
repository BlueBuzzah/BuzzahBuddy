using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class SessionClockTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SessionStatus Status(SessionState state, int elapsed, int total = 900) =>
        new() { Status = state, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total };

    [Fact]
    public void TickTo_Running_InterpolatesFromAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);

        var r = clock.TickTo(T0.AddSeconds(2.5));

        Assert.Equal(62.5, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(837.5, r.Remaining.TotalSeconds, 3);
        Assert.Equal(62.5 / 900.0, r.Fraction, 6);
        Assert.True(r.HasKnownTotal);
        Assert.Equal("01:02", r.ElapsedFormatted);   // floor(62.5) = 62s
        Assert.Equal("13:58", r.RemainingFormatted); // ceil(837.5) = 838s
    }

    [Fact]
    public void TickTo_Paused_FreezesAtAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.PAUSED, 60), T0);

        var r = clock.TickTo(T0.AddSeconds(30));

        Assert.Equal(60, r.Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void Anchor_SmallBackwardDrift_IsAbsorbedMonotonically()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        // At T0+5 local display shows 65; device reports 63 (2s behind, <= threshold)
        clock.Anchor(Status(SessionState.RUNNING, 63), T0.AddSeconds(5));

        // Display holds at 65 rather than jumping back to 63...
        Assert.Equal(65, clock.TickTo(T0.AddSeconds(5)).Elapsed.TotalSeconds, 3);
        // ...and resumes normal counting once the new anchor catches up.
        Assert.Equal(66, clock.TickTo(T0.AddSeconds(8)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void Anchor_LargeBackwardDrift_Snaps()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        // Device reports 50 while local shows 65: 15s disagreement > threshold
        clock.Anchor(Status(SessionState.RUNNING, 50), T0.AddSeconds(5));

        Assert.Equal(50, clock.TickTo(T0.AddSeconds(5)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void TickTo_ClampsAtTotal()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 895), T0);

        var r = clock.TickTo(T0.AddSeconds(30));

        Assert.Equal(900, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0, r.Remaining.TotalSeconds, 3);
        Assert.Equal(1.0, r.Fraction, 6);
        Assert.Equal("00:00", r.RemainingFormatted);
    }

    [Fact]
    public void UnknownTotal_ReportsNoTotalAndInterpolatesElapsed()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60, total: 0), T0);

        var r = clock.TickTo(T0.AddSeconds(5));

        Assert.False(r.HasKnownTotal);
        Assert.Equal(65, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0.0, r.Fraction, 6);
    }

    [Fact]
    public void Idle_ReadsZero()
    {
        var clock = new SessionClock();
        clock.Anchor(SessionStatus.CreateIdle(), T0);

        var r = clock.TickTo(T0.AddSeconds(10));

        Assert.Equal(0, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0.0, r.Fraction, 6);
    }

    [Fact]
    public void LowBattery_CountsAsRunning()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.LOW_BATTERY, 60), T0);

        Assert.Equal(62, clock.TickTo(T0.AddSeconds(2)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void PauseThenResume_ContinuesFromResumeAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        clock.Anchor(Status(SessionState.PAUSED, 62), T0.AddSeconds(2));
        Assert.Equal(62, clock.TickTo(T0.AddSeconds(30)).Elapsed.TotalSeconds, 3);

        clock.Anchor(Status(SessionState.RUNNING, 62), T0.AddSeconds(30));
        Assert.Equal(63, clock.TickTo(T0.AddSeconds(31)).Elapsed.TotalSeconds, 3);
    }
}
