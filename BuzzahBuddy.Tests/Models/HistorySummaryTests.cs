using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class HistorySummaryTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private static SessionRecord Rec(string start, int seconds, SessionOutcome outcome = SessionOutcome.Completed) => new()
    {
        Id = start,
        StartedAt = DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
        TherapySeconds = seconds,
        PlannedSeconds = 7200,
        ProfileName = "Noisy",
        Outcome = outcome,
    };

    [Fact]
    public void Days_GroupsByStartDay_NewestFirst_IncludesToday()
    {
        var sessions = new[] { Rec("2026-10-28T18:00:00+00:00", 6600), Rec("2026-10-28T08:00:00+00:00", 7200) };
        var notes = new[] { new DayNote { Day = new DateOnly(2026, 10, 26), Text = "Slept badly" } };

        var days = HistorySummary.Days(sessions, notes, Today);

        Assert.Equal(new[] { new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 26) }, days.Select(d => d.Day));
        Assert.Equal(0, days[0].TherapySeconds);
        Assert.Equal(13800, days[1].TherapySeconds);
        Assert.Equal(2, days[1].Sessions.Count);
        Assert.True(days[1].Sessions[0].StartedAt < days[1].Sessions[1].StartedAt);
        Assert.Empty(days[2].Sessions);
        Assert.Equal("Slept badly", days[2].Note);
    }

    [Fact]
    public void Days_SessionCrossingMidnight_CountsOnStartDay()
    {
        var days = HistorySummary.Days(new[] { Rec("2026-10-27T23:30:00+00:00", 7200) }, Array.Empty<DayNote>(), Today);

        Assert.Equal(new DateOnly(2026, 10, 27), days.Single(d => d.Sessions.Count > 0).Day);
        Assert.Equal(2, days.Count);
    }

    [Fact]
    public void Days_UsesEachRecordsOwnOffset()
    {
        var sessions = new[] { Rec("2026-11-01T00:30:00-07:00", 3600), Rec("2026-11-01T23:30:00-08:00", 3600) };

        var days = HistorySummary.Days(sessions, Array.Empty<DayNote>(), new DateOnly(2026, 11, 1));

        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 11, 1), day.Day);
        Assert.Equal(2, day.Sessions.Count);
    }

    [Fact]
    public void LastDays_FillsEmptyDays_OldestFirst()
    {
        var days = HistorySummary.LastDays(new[] { Rec("2026-10-28T08:00:00+00:00", 13800) }, Today);

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 23), days[0].Day);
        Assert.Equal(Today, days[6].Day);
        Assert.Equal(13800, days[5].TherapySeconds);
        Assert.Equal(13800, days.Sum(d => d.TherapySeconds));
        Assert.All(days, d => Assert.Null(d.Note));
    }

    [Fact]
    public void Summarize_CountsOutcomesAndDays()
    {
        var sessions = new[]
        {
            Rec("2026-10-28T08:00:00+00:00", 7200),
            Rec("2026-10-28T18:00:00+00:00", 7200),
            Rec("2026-10-20T08:00:00+00:00", 3600, SessionOutcome.StoppedEarly),
            Rec("2026-09-30T08:00:00+00:00", 7200),
        };

        var s = HistorySummary.Summarize(sessions, new DateOnly(2026, 10, 1), Today);

        Assert.Equal(29, s.DaysInPeriod);
        Assert.Equal(2, s.DaysWithSessions);
        Assert.Equal(3, s.Sessions);
        Assert.Equal(2, s.Completed);
        Assert.Equal(1, s.StoppedEarly);
        Assert.Equal(0, s.EndedEarly);
        Assert.Equal(18000, s.TherapySeconds);
        Assert.Equal(9000, s.AverageSecondsPerSessionDay);
    }

    [Fact]
    public void BarFraction_FullBarIsFourHours()
    {
        Assert.Equal(0, HistorySummary.BarFraction(0));
        Assert.Equal(0.5, HistorySummary.BarFraction(7200));
        Assert.Equal(1, HistorySummary.BarFraction(14400));
        Assert.Equal(1, HistorySummary.BarFraction(20000));
    }
}
