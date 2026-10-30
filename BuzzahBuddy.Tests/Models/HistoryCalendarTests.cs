using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Storage;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class HistoryCalendarTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static SessionRecord Rec(string start, int seconds) => new()
    {
        StartedAt = DateTimeOffset.Parse(start, Inv),
        TherapySeconds = seconds,
        PlannedSeconds = 7200,
        ProfileName = "Noisy",
        Outcome = SessionOutcome.Completed,
    };

    [Fact]
    public void Build_October2026_SundayFirst()
    {
        var sessions = new[] { Rec("2026-10-01T09:00:00", 14400), Rec("2026-10-30T09:00:00", 7800) };

        var month = HistoryCalendar.Build(sessions, new DateOnly(2026, 10, 12), Today, Inv);

        Assert.Equal("October 2026", month.Title);
        Assert.Equal("Sun", month.WeekdayNames[0]);
        Assert.Equal(4, month.LeadingBlanks);
        Assert.Equal(31, month.Days.Count);
        Assert.Equal("Thursday, October 1, 4 hours", month.Days[0].Spoken);
        Assert.Equal(1, month.Days[0].Fraction);
        Assert.True(month.Days[29].IsToday);
        Assert.Equal("Friday, October 30, today, 2 hours 10 minutes", month.Days[29].Spoken);
        Assert.True(month.Days[30].IsFuture);
        Assert.Equal("Friday, October 2, no sessions", month.Days[1].Spoken);
        Assert.False(month.Days[1].HasTime);
    }

    [Fact]
    public void Build_WeekStartFollowsCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;

        var month = HistoryCalendar.Build(Array.Empty<SessionRecord>(), Today, Today, culture);

        Assert.Equal("Mon", month.WeekdayNames[0]);
        Assert.Equal("Sun", month.WeekdayNames[6]);
        Assert.Equal(3, month.LeadingBlanks);
    }

    [Fact]
    public void Build_FractionClampsAndTinyDayHasTime()
    {
        var sessions = new[] { Rec("2026-10-05T09:00:00", 20000), Rec("2026-10-06T09:00:00", 300) };

        var month = HistoryCalendar.Build(sessions, Today, Today, Inv);

        Assert.Equal(1, month.Days[4].Fraction);
        Assert.True(month.Days[5].HasTime);
        Assert.Equal(300 / 14400.0, month.Days[5].Fraction, 4);
    }

    [Fact]
    public void Build_TodayWithoutSessions_SaysNoSessionsYet()
    {
        var month = HistoryCalendar.Build(Array.Empty<SessionRecord>(), Today, Today, Inv);

        Assert.Equal("Friday, October 30, today, no sessions yet", month.Days[29].Spoken);
    }

    [Fact]
    public void FirstMonth_EarliestSessionOrToday()
    {
        var sessions = new[] { Rec("2026-10-01T09:00:00", 600), Rec("2026-03-15T09:00:00", 600) };

        Assert.Equal(new DateOnly(2026, 3, 1), HistoryCalendar.FirstMonth(sessions, Today));
        Assert.Equal(new DateOnly(2026, 10, 1), HistoryCalendar.FirstMonth(Array.Empty<SessionRecord>(), Today));
    }

    [Fact]
    public void Step_ClampsToRange()
    {
        var oct = new DateOnly(2026, 10, 1);
        Assert.Equal(oct, HistoryCalendar.Step(oct, -1, oct, oct));
        Assert.Equal(oct, HistoryCalendar.Step(oct, 1, oct, oct));
        Assert.Equal(new DateOnly(2026, 3, 1),
            HistoryCalendar.Step(new DateOnly(2026, 2, 1), 0, new DateOnly(2026, 3, 1), oct));
    }

    [Fact]
    public void Step_CrossesYearBoundary()
    {
        var first = new DateOnly(2025, 11, 1);
        var last = new DateOnly(2026, 2, 1);

        Assert.Equal(new DateOnly(2026, 1, 1), HistoryCalendar.Step(new DateOnly(2025, 12, 1), 1, first, last));
        Assert.Equal(new DateOnly(2025, 12, 1), HistoryCalendar.Step(new DateOnly(2026, 1, 1), -1, first, last));
    }
}
