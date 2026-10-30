using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Storage;
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

        Assert.Equal("08:02 · 2h · Noisy · Completed", line);
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
        Assert.Equal("3h 50min", row.Total);
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
        Assert.Equal("0min", today.Total);

        var earlier = rows.Single(r => r.Day == new DateOnly(2026, 10, 26));
        Assert.Equal(new[] { "No sessions" }, earlier.SessionLines);
        Assert.Equal("Monday, Oct 26. No sessions. Note: Slept badly", earlier.Spoken);
        Assert.Equal("Edit Note", earlier.NoteButtonText);
    }

    private static readonly DateOnly Now = new(2026, 10, 30);

    private static ReportModel Model(ReportRange range, params SessionRecord[] sessions)
    {
        var data = new HistoryData { Sessions = sessions.ToList() };
        var (from, to) = HistoryReport.Range(range, Now, data.Sessions);
        return HistoryReport.Build(data, from, to, DateTimeOffset.Parse("2026-10-30T12:00:00+00:00", Inv), comparePrevious: false);
    }

    [Fact]
    public void Tiles_ThirtyDays_ValuesDetailsAndSpoken()
    {
        var m = Model(ReportRange.Last30Days,
            Rec("2026-09-01T08:00:00+00:00", 3600), Rec("2026-10-01T08:00:00+00:00", 7200),
            Rec("2026-10-03T08:00:00+00:00", 9000), Rec("2026-10-03T14:00:00+00:00", 3600));

        var tiles = HistoryText.Tiles(m, ReportRange.Last30Days, Inv);

        Assert.Equal(new[] { "Total Time", "Days Used", "Average Day", "Sessions" }, tiles.Select(t => t.Label));
        Assert.Equal("5h 30min", tiles[0].Value);
        Assert.Equal("in the last 30 days", tiles[0].Detail);
        Assert.Equal("2", tiles[1].Value);
        Assert.Equal("days with a session", tiles[1].Detail);
        Assert.Equal("2h 45min", tiles[2].Value);
        Assert.Equal("on days used", tiles[2].Detail);
        Assert.Equal("3", tiles[3].Value);
        Assert.Equal("1h 50min each on average", tiles[3].Detail);
        Assert.Equal("Total Time, 5 hours 30 minutes, in the last 30 days", tiles[0].Spoken);
    }

    [Fact]
    public void Tiles_FirstSessionInsidePeriod_SaysSince()
    {
        var m = Model(ReportRange.Last30Days, Rec("2026-10-20T08:00:00+00:00", 7200));

        var tiles = HistoryText.Tiles(m, ReportRange.Last30Days, Inv);

        Assert.Equal("since your first session on Oct 20", tiles[0].Detail);
        Assert.Equal("day with a session", tiles[1].Detail);
    }

    [Fact]
    public void Tiles_AllTimeFromLastYear_IncludesYear()
    {
        var m = Model(ReportRange.AllTime, Rec("2025-12-02T08:00:00+00:00", 3600), Rec("2026-10-29T08:00:00+00:00", 3600));

        var tiles = HistoryText.Tiles(m, ReportRange.AllTime, Inv);

        Assert.Equal("since your first session on Dec 2, 2025", tiles[0].Detail);
    }

    [Fact]
    public void Tiles_EmptyPeriod_ZerosNotCrash()
    {
        var m = Model(ReportRange.Last30Days, Rec("2026-08-01T08:00:00+00:00", 3600));

        var tiles = HistoryText.Tiles(m, ReportRange.Last30Days, Inv);

        Assert.Equal(new[] { "0min", "0", "0min", "0" }, tiles.Select(t => t.Value));
        Assert.Equal("no sessions", tiles[3].Detail);
    }

    [Fact]
    public void TrendRows_WeeklyLabelsValuesAndSpoken()
    {
        var m = Model(ReportRange.Last30Days, Rec("2026-09-01T08:00:00+00:00", 3600), Rec("2026-10-24T08:00:00+00:00", 14400));

        var rows = HistoryText.TrendRows(m, Inv);

        Assert.True(HistoryText.IsWeekly(m));
        Assert.Equal(new[] { "Oct 1–2", "Oct 3–9", "Oct 10–16", "Oct 17–23", "Oct 24–30" }, rows.Select(r => r.Label));
        Assert.Equal("34min a day", rows[4].Value);
        Assert.Equal("October 24 to 30, 34 minutes a day", rows[4].Spoken);
        Assert.Equal("0min a day", rows[0].Value);
    }

    [Fact]
    public void TrendRows_CrossMonthLabel()
    {
        var m = Model(ReportRange.Last90Days, Rec("2026-01-01T08:00:00+00:00", 3600));

        var rows = HistoryText.TrendRows(m, Inv);

        var row = Assert.Single(rows, r => r.Label == "Sep 26–Oct 2");
        Assert.StartsWith("September 26 to October 2, ", row.Spoken);
    }

    [Fact]
    public void TrendRows_FirstBlockClampedToFirstSession()
    {
        var m = Model(ReportRange.Last30Days, Rec("2026-10-20T08:00:00+00:00", 3600));

        Assert.Equal("Oct 20–23", HistoryText.TrendRows(m, Inv)[0].Label);
    }

    [Fact]
    public void TrendRows_MonthlyKeepsLastTwelve()
    {
        var m = Model(ReportRange.AllTime, Rec("2025-01-15T08:00:00+00:00", 3600), Rec("2026-10-29T08:00:00+00:00", 3600));

        var rows = HistoryText.TrendRows(m, Inv);

        Assert.False(HistoryText.IsWeekly(m));
        Assert.Equal(12, rows.Count);
        Assert.Equal("Nov 2025", rows[0].Label);
        Assert.Equal("Oct 2026", rows[^1].Label);
        Assert.StartsWith("October 2026, ", rows[^1].Spoken);
    }

    [Fact]
    public void TrendTitleCaptionShareAndEmptyText()
    {
        Assert.Equal("Weekly Average", HistoryText.TrendTitle(true));
        Assert.Equal("Monthly Average", HistoryText.TrendTitle(false));
        Assert.Equal("Average time per day, by week. A full bar is 4 hours.", HistoryText.TrendCaption(true, false));
        Assert.Equal("Average time per day, by month. A full bar is 4 hours.", HistoryText.TrendCaption(false, false));
        Assert.Equal("Average time per day, by month. A full bar is 4 hours. The last 12 months.", HistoryText.TrendCaption(false, true));
        Assert.Equal("Share these 30 days with your care team as a PDF and a spreadsheet. It leaves this phone only when you share it.", HistoryText.ShareText(ReportRange.Last30Days));
        Assert.Equal("Share these 90 days with your care team as a PDF and a spreadsheet. It leaves this phone only when you share it.", HistoryText.ShareText(ReportRange.Last90Days));
        Assert.Equal("Share all your sessions with your care team as a PDF and a spreadsheet. It leaves this phone only when you share it.", HistoryText.ShareText(ReportRange.AllTime));
        Assert.Equal("No sessions in the last 30 days.", HistoryText.EmptyPeriodText(ReportRange.Last30Days));
        Assert.Equal("No sessions in the last 90 days.", HistoryText.EmptyPeriodText(ReportRange.Last90Days));
        Assert.Equal("No sessions yet.", HistoryText.EmptyPeriodText(ReportRange.AllTime));
    }
}
