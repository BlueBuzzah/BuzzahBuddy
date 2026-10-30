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

    private static readonly DateOnly From = new(2026, 9, 30);
    private static readonly DateTimeOffset Created = new(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(-7));

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
    public void Build_DailyChartForShortPeriods()
    {
        var model = HistoryReport.Build(Fixture(), From, Today, Created);

        Assert.Equal(30, model.Chart.Count);
        Assert.Equal("Daily Session Time", model.ChartTitle);
        Assert.Equal(14400, model.ChartReferenceSeconds);
        Assert.Equal(14400, model.ChartMaxSeconds);
        Assert.Equal(From, model.Chart[0].Start);
        Assert.Equal(14400, model.Chart.Single(p => p.Start == new DateOnly(2026, 10, 28)).Seconds);
        Assert.Equal("Oct 29", model.Chart[29].Label);
        Assert.Equal("Oct 22", model.Chart[22].Label);
        Assert.Equal("", model.Chart[25].Label);
    }

    [Fact]
    public void Build_WeeklyChartForLongPeriods()
    {
        var model = HistoryReport.Build(Fixture(), new DateOnly(2026, 1, 1), Today, Created);

        Assert.Equal(302, model.Summary.DaysInPeriod);
        Assert.Equal(44, model.Chart.Count);
        Assert.Equal("Weekly Session Time", model.ChartTitle);
        Assert.Equal(100800, model.ChartReferenceSeconds);
        Assert.Equal(new DateOnly(2026, 1, 1), model.Chart[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 23), model.Chart[^1].Start);
        Assert.Equal(14400, model.Chart[^1].Seconds);
    }

    [Fact]
    public void Build_MaxGrowsPastReference()
    {
        var data = new HistoryData { Sessions = { Rec("x", "2026-10-28T08:00:00-07:00", 5 * 3600 + 600, "Noisy", SessionOutcome.Completed) } };

        Assert.Equal(21600, HistoryReport.Build(data, From, Today, Created).ChartMaxSeconds);
    }

    [Fact]
    public void Build_ProfilesByTimeDescending()
    {
        var model = HistoryReport.Build(Fixture(), From, Today, Created);

        Assert.Equal(new[] { new ProfileRow("Noisy", 2, 14400), new ProfileRow("Regular", 1, 3600) }, model.Profiles);
    }

    [Fact]
    public void Build_WeeksOldestFirst()
    {
        var model = HistoryReport.Build(Fixture(), From, Today, Created);

        Assert.Equal(new WeekRow(From, new DateOnly(2026, 10, 1), 0, 0), model.Weeks[0]);
        Assert.Equal(new WeekRow(new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 22), 1, 3600), model.Weeks[^2]);
        Assert.Equal(new WeekRow(new DateOnly(2026, 10, 23), Today, 1, 14400), model.Weeks[^1]);
    }

    [Fact]
    public void Build_LogDaysOldestFirstWithNotes()
    {
        var model = HistoryReport.Build(Fixture(), From, Today, Created);

        Assert.Equal(2, model.LogDays.Count);
        Assert.Equal(new DateOnly(2026, 10, 20), model.LogDays[0].Day);
        Assert.Equal(new DateOnly(2026, 10, 28), model.LogDays[1].Day);
        Assert.Equal("Tremor felt lighter after lunch", model.LogDays[1].Note);
    }

    [Fact]
    public void LegalNotices_MatchDesignSystemWording()
    {
        Assert.Equal("BlueBuzzah devices are not FDA approved medical devices and not intended to diagnose, treat, cure, or prevent any disease. The information provided through BlueBuzzah is for general wellness purposes only and not a substitute for professional medical advice, diagnosis, or treatment.", LegalNotices.MedicalDisclaimer);
        Assert.Equal("Important: People with implanted electrodes from deep brain stimulation (DBS), heart pacemakers, or other internal electronic devices should take special precautions around all electronic devices, including BlueBuzzah. Consult your physician before use.", LegalNotices.ImplantedDeviceNotice);
    }
}
