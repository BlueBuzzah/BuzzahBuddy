using System.Globalization;
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class HistoryTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
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
    public void DayTitle_TodayYesterdayAndDate()
    {
        Assert.Equal("Today", HistoryText.DayTitle(Today, Today, Inv));
        Assert.Equal("Yesterday", HistoryText.DayTitle(new DateOnly(2026, 10, 28), Today, Inv));
        Assert.Equal("Tuesday, Oct 27", HistoryText.DayTitle(new DateOnly(2026, 10, 27), Today, Inv));
    }

    [Fact]
    public void OutcomeText_UsesTitleCaseFreeWords()
    {
        Assert.Equal("Completed", HistoryText.OutcomeText(SessionOutcome.Completed));
        Assert.Equal("Stopped early", HistoryText.OutcomeText(SessionOutcome.StoppedEarly));
        Assert.Equal("Ended early", HistoryText.OutcomeText(SessionOutcome.EndedEarly));
    }

    [Fact]
    public void SessionLine_JoinsTimeLengthProfileOutcome()
    {
        var line = HistoryText.SessionLine(Rec("2026-10-28T08:02:00+00:00", 7200), Inv);

        Assert.Equal("08:02 · 2 h · Noisy · Completed", line);
    }

    [Fact]
    public void DayRows_SpokenSummaryReadsAsOneSentence()
    {
        var sessions = new[] { Rec("2026-10-28T08:00:00+00:00", 7200), Rec("2026-10-28T18:00:00+00:00", 6600) };
        var notes = new[] { new DayNote { Day = new DateOnly(2026, 10, 28), Text = "Tremor felt lighter after lunch" } };
        var days = HistorySummary.Days(sessions, notes, Today);

        var row = HistoryText.DayRows(days, Today, Inv).Single(r => r.Day == new DateOnly(2026, 10, 28));

        Assert.Equal("Yesterday. 3 hours 50 minutes. 2 sessions. Note: Tremor felt lighter after lunch", row.Spoken);
        Assert.Equal("Edit Note", row.NoteButtonText);
        Assert.Equal("3 h 50 min", row.Total);
        Assert.Equal(2, row.SessionLines.Count);
        Assert.Equal("Tremor felt lighter after lunch", row.Note);
    }

    [Fact]
    public void DayRows_SingleSessionIsSingular()
    {
        var days = HistorySummary.Days(new[] { Rec("2026-10-28T08:00:00+00:00", 7200) }, Array.Empty<DayNote>(), Today);

        var row = HistoryText.DayRows(days, Today, Inv).Single(r => r.Day == new DateOnly(2026, 10, 28));

        Assert.Equal("Yesterday. 2 hours. 1 session", row.Spoken);
        Assert.Equal("Add Note", row.NoteButtonText);
    }

    [Fact]
    public void DayRows_EmptyDays()
    {
        var notes = new[] { new DayNote { Day = new DateOnly(2026, 10, 26), Text = "Slept badly" } };
        var rows = HistoryText.DayRows(HistorySummary.Days(Array.Empty<SessionRecord>(), notes, Today), Today, Inv);

        var today = rows.Single(r => r.Day == Today);
        Assert.Equal(new[] { "No sessions yet" }, today.SessionLines);
        Assert.Equal("Today. No sessions yet", today.Spoken);
        Assert.Equal("Add Note", today.NoteButtonText);
        Assert.Equal("0 min", today.Total);

        var earlier = rows.Single(r => r.Day == new DateOnly(2026, 10, 26));
        Assert.Equal(new[] { "No sessions" }, earlier.SessionLines);
        Assert.Equal("Monday, Oct 26. No sessions. Note: Slept badly", earlier.Spoken);
        Assert.Equal("Edit Note", earlier.NoteButtonText);
    }

    [Fact]
    public void WeekBars_LabelsAndSpoken()
    {
        var days = HistorySummary.LastDays(new[] { Rec("2026-10-28T08:00:00+00:00", 7800) }, Today);

        var bars = HistoryText.WeekBars(days, Inv);

        var bar = bars[5];
        Assert.Equal("Wed 28", bar.Label);
        Assert.Equal("2 h 10 min", bar.Value);
        Assert.Equal(7800 / 14400.0, bar.Fraction, 4);
        Assert.Equal("Wednesday, 2 hours 10 minutes", bar.Spoken);
        Assert.Equal("Thursday, no sessions", bars[6].Spoken);
    }
}
