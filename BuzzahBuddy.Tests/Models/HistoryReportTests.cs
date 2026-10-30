using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Storage;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class HistoryReportTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static SessionRecord Rec(string id, string start, int seconds, string profile, SessionOutcome outcome)
    {
        var s = DateTimeOffset.Parse(start, Inv);
        return new SessionRecord
        {
            Id = id,
            StartedAt = s,
            EndedAt = s.AddSeconds(seconds),
            TherapySeconds = seconds,
            PlannedSeconds = 7200,
            ProfileName = profile,
            Outcome = outcome,
        };
    }

    private static HistoryData Fixture() => new()
    {
        Sessions =
        {
            Rec("a", "2026-10-28T08:00:00-07:00", 7200, "Noisy", SessionOutcome.Completed),
            Rec("b", "2026-10-28T14:00:00-07:00", 7200, "Noisy", SessionOutcome.Completed),
            Rec("c", "2026-10-20T09:00:00-07:00", 3600, "Regular", SessionOutcome.StoppedEarly),
        },
        Notes = { new DayNote { Day = new DateOnly(2026, 10, 28), Text = "Tremor felt lighter after lunch" } },
    };

    private static string Report(HistoryData data) =>
        HistoryReport.Text(data, new DateOnly(2026, 9, 30), Today, new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(-7)), Inv);

    [Fact]
    public void Range_ThirtyNinetyAndAllTime()
    {
        var sessions = Fixture().Sessions;
        Assert.Equal((new DateOnly(2026, 9, 30), Today), HistoryReport.Range(ReportRange.Last30Days, Today, sessions));
        Assert.Equal((new DateOnly(2026, 8, 1), Today), HistoryReport.Range(ReportRange.Last90Days, Today, sessions));
        Assert.Equal((new DateOnly(2026, 10, 20), Today), HistoryReport.Range(ReportRange.AllTime, Today, sessions));
        Assert.Equal((Today, Today), HistoryReport.Range(ReportRange.AllTime, Today, Array.Empty<SessionRecord>()));
    }

    [Fact]
    public void Csv_HeaderAndRows()
    {
        var csv = HistoryReport.Csv(Fixture().Sessions);
        var lines = csv.Split("\r\n");
        Assert.Equal("Date,Start,End,Session Minutes,Planned Minutes,Profile,Outcome", lines[0]);
        Assert.Equal("2026-10-20,09:00,10:00,60,120,Regular,Stopped early", lines[1]);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void Csv_QuotesCommasAndQuotes()
    {
        var r = Rec("q", "2026-10-28T08:00:00-07:00", 3600, "Custom, \"Night\"", SessionOutcome.Completed);
        Assert.Contains(",\"Custom, \"\"Night\"\"\",", HistoryReport.Csv(new[] { r }));
    }

    [Fact]
    public void Text_SummaryLines()
    {
        var text = Report(Fixture());
        Assert.Contains("Period: Sep 30, 2026 to Oct 29, 2026 (30 days)", text);
        Assert.Contains("Days with sessions: 2 of 30", text);
        Assert.Contains("Total session time: 5 h", text);
        Assert.Contains("Average on days with sessions: 2 h 30 min", text);
        Assert.Contains("Sessions: 3 (2 completed, 1 stopped early, 0 ended early)", text);
    }

    [Fact]
    public void Text_DailyLogWithNote()
    {
        var text = Report(Fixture());
        Assert.Contains("Oct 28, 2026: 4 h, 2 sessions", text);
        Assert.Contains("  08:00, 2 h, Noisy, Completed", text);
        Assert.Contains("  Note: Tremor felt lighter after lunch", text);
    }

    [Fact]
    public void Text_MultiLineNote_IndentsContinuationLines()
    {
        var data = Fixture();
        data.Notes[0].Text = "First line\r\nSecond line";
        Assert.Contains("  Note: First line\n  Second line\n", Report(data));
    }

    [Fact]
    public void Text_WeeksOldestFirst()
    {
        var text = Report(Fixture());
        var older = text.IndexOf("Oct 16 to Oct 22: 1 day, 1 h", StringComparison.Ordinal);
        var newer = text.IndexOf("Oct 23 to Oct 29: 1 day, 4 h", StringComparison.Ordinal);
        Assert.True(older >= 0 && newer > older);
        Assert.Contains("Sep 30 to Oct 1: 0 days, 0 min", text);
    }

    [Fact]
    public void Text_EndsWithBothLegalNoticesVerbatim()
    {
        Assert.EndsWith(LegalNotices.MedicalDisclaimer + "\n\n" + LegalNotices.ImplantedDeviceNotice + "\n", Report(Fixture()));
    }

    [Fact]
    public void Text_EmptyPeriod_SaysSo()
    {
        var text = Report(new HistoryData());
        Assert.Contains("No sessions in this period.", text);
        Assert.Contains("Days with sessions: 0 of 30", text);
    }

    [Fact]
    public void LegalNotices_MatchDesignSystemWording()
    {
        Assert.Equal("BlueBuzzah devices are not FDA approved medical devices and not intended to diagnose, treat, cure, or prevent any disease. The information provided through BlueBuzzah is for general wellness purposes only and not a substitute for professional medical advice, diagnosis, or treatment.", LegalNotices.MedicalDisclaimer);
        Assert.Equal("Important: People with implanted electrodes from deep brain stimulation (DBS), heart pacemakers, or other internal electronic devices should take special precautions around all electronic devices, including BlueBuzzah. Consult your physician before use.", LegalNotices.ImplantedDeviceNotice);
    }
}
