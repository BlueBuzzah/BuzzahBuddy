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
        var csv = HistoryReport.Csv(Fixture().Sessions, Array.Empty<DayNote>());
        var lines = csv.Split("\r\n");
        Assert.Equal("Date,Start,End,Session Minutes,Planned Minutes,Profile,Outcome,Day Note", lines[0]);
        Assert.Equal("2026-10-20,09:00,10:00,60,120,Regular,Stopped early,", lines[1]);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void Csv_QuotesCommasAndQuotes()
    {
        var r = Rec("q", "2026-10-28T08:00:00-07:00", 3600, "Custom, \"Night\"", SessionOutcome.Completed);
        Assert.Contains(",\"Custom, \"\"Night\"\"\",", HistoryReport.Csv(new[] { r }, Array.Empty<DayNote>()));
    }

    [Fact]
    public void Csv_DayNoteColumn()
    {
        var data = Fixture();
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 24), Text = "=SUM(A1)" });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 25), Text = "  \n " });

        var lines = HistoryReport.Csv(data.Sessions, data.Notes).Split("\r\n");

        Assert.EndsWith(",Day Note", lines[0]);
        Assert.Equal(new[]
        {
            "2026-10-20,09:00,10:00,60,120,Regular,Stopped early,",
            "2026-10-24,,,,,,,'=SUM(A1)",
            "2026-10-28,08:00,10:00,120,120,Noisy,Completed,Tremor felt lighter after lunch",
            "2026-10-28,14:00,16:00,120,120,Noisy,Completed,",
            "",
        }, lines[1..]);
    }

    [Fact]
    public void Csv_GuardsEveryFormulaPrefix()
    {
        foreach (var prefix in new[] { "=", "+", "-", "@", "\t", "\r" })
        {
            var r = Rec("f", "2026-10-28T08:00:00-07:00", 3600, prefix + "1", SessionOutcome.Completed);
            Assert.Contains("'" + prefix + "1", HistoryReport.Csv(new[] { r }, Array.Empty<DayNote>()));
        }
    }

    private static ReportModel Build(HistoryData data, DateOnly from, bool comparePrevious = true) =>
        HistoryReport.Build(data, from, Today, Created, comparePrevious);

    /// <summary>The fixture plus a session long before every test period, so the period is the adherence window.</summary>
    private static HistoryData WithEarlierSession(HistoryData data)
    {
        data.Sessions.Add(Rec("early", "2025-06-01T08:00:00-07:00", 3600, "Noisy", SessionOutcome.Completed));
        return data;
    }

    [Fact]
    public void Build_LedeSentences()
    {
        // The first session ever was on Oct 20, so adherence counts Oct 20..29: 10 days, halves of 5.
        // Oct 20..24: 3600 s / 5 = 12 min, about 10 min. Oct 25..29: 14400 s / 5 = 48 min, about 50 min.
        Assert.Equal(new[]
        {
            "Used on 2 of 10 days since the first session on Oct 20, averaging 2h 30min on those days.",
            "Daily time rose from about 10min in the first half of the period to about 50min in the second half.",
            "The longest stretch without a session was 7 days, Oct 21 to Oct 27.",
        }, Build(Fixture(), From).Lede);
    }

    [Fact]
    public void Build_LedeSentences_FirstSessionBeforePeriod_KeepsWording()
    {
        Assert.Equal(new[]
        {
            "Used on 2 of 30 days, averaging 2h 30min on those days.",
            "Daily time rose from about 0min in the first half of the period to about 20min in the second half.",
            "The longest stretch without a session was 20 days, Sep 30 to Oct 19.",
        }, Build(WithEarlierSession(Fixture()), From).Lede);
    }

    [Fact]
    public void Build_LedeSentences_OneDayWindow_SaysDay()
    {
        var earlier = WithEarlierSession(Fixture());
        var oneDay = new DateOnly(2026, 10, 28);
        Assert.Equal("Used on 1 of 1 day, averaging 4h on those days.",
            HistoryReport.Build(earlier, oneDay, oneDay, Created, comparePrevious: true).Lede[0]);

        var firstToday = new HistoryData { Sessions = { Rec("t", "2026-10-29T08:00:00-07:00", 3600, "Noisy", SessionOutcome.Completed) } };
        Assert.Equal("Used on 1 of 1 day since the first session on Oct 29, averaging 1h on those days.", Build(firstToday, From).Lede[0]);
    }

    [Fact]
    public void Build_AdherenceStartsAtFirstSessionEver()
    {
        var model = Build(Fixture(), From);
        Assert.Equal(new DateOnly(2026, 10, 20), model.AdherenceFrom);
        Assert.Equal(new DateOnly(2026, 10, 20), model.Summary.From);
        Assert.Equal(10, model.Summary.DaysInPeriod);
        Assert.Equal(2, model.Summary.DaysWithSessions);
        Assert.Null(model.Previous);
        // The calendar still spans the whole period.
        Assert.Equal(From, model.Days[0].Day);

        var earlier = Build(WithEarlierSession(Fixture()), From);
        Assert.Equal(From, earlier.AdherenceFrom);
        Assert.Equal(30, earlier.Summary.DaysInPeriod);

        Assert.Equal(From, Build(new HistoryData(), From).AdherenceFrom);
    }

    [Fact]
    public void Build_LedeHeldSteadyAndFell()
    {
        var data = new HistoryData();
        for (var i = 0; i < 30; i++)
            data.Sessions.Add(Rec($"d{i}", $"{From.AddDays(i):yyyy-MM-dd}T08:00:00-07:00", i < 15 ? 6600 : 6000, "Noisy", SessionOutcome.Completed));
        Assert.Equal("Daily time held steady at about 1h 50min.", Build(data, From).Lede[1]);

        data.Sessions.Add(Rec("x", "2026-10-01T12:00:00-07:00", 7 * 3600, "Noisy", SessionOutcome.Completed));
        Assert.Equal("Daily time fell from about 2h 20min in the first half of the period to about 1h 40min in the second half.", Build(data, From).Lede[1]);
    }

    [Fact]
    public void Build_NoSessions_OneSentence()
    {
        Assert.Equal(new[] { "No sessions were recorded in this period." }, Build(new HistoryData(), From).Lede);
    }

    [Fact]
    public void Build_GapUnderTwoDays()
    {
        var data = new HistoryData();
        for (var i = 0; i < 30; i++)
            data.Sessions.Add(Rec($"d{i}", $"{From.AddDays(i):yyyy-MM-dd}T08:00:00-07:00", 3600, "Noisy", SessionOutcome.Completed));
        data.Sessions.RemoveAt(10);

        Assert.Equal("There was no gap longer than a day.", Build(data, From).Lede[2]);
    }

    [Fact]
    public void Build_PreviousWindow()
    {
        Assert.Null(Build(Fixture(), From).Previous);

        var data = Fixture();
        data.Sessions.Add(Rec("p", "2026-08-31T08:00:00-07:00", 1800, "Noisy", SessionOutcome.Completed));
        data.Sessions.Add(Rec("old", "2026-08-30T08:00:00-07:00", 1800, "Noisy", SessionOutcome.Completed));
        var previous = Build(data, From).Previous;

        Assert.NotNull(previous);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.From);
        Assert.Equal(new DateOnly(2026, 9, 29), previous.To);
        Assert.Equal(30, previous.DaysInPeriod);
        Assert.Equal(1800, previous.TherapySeconds);
        Assert.Null(Build(data, From, comparePrevious: false).Previous);
    }

    [Fact]
    public void Build_CalendarStyleAndDays()
    {
        var thirty = Build(Fixture(), From);
        Assert.Equal(CalendarStyle.MonthGrids, thirty.Calendar);
        Assert.Equal(30, thirty.Days.Count);
        Assert.Equal(From, thirty.Days[0].Day);
        Assert.Equal(new DayCell(new DateOnly(2026, 10, 28), 14400), thirty.Days[28]);

        Assert.Equal(CalendarStyle.MonthGrids, Build(Fixture(), new DateOnly(2026, 7, 1)).Calendar);
        Assert.Equal(CalendarStyle.MonthRows, Build(Fixture(), new DateOnly(2026, 6, 30)).Calendar);

        var year = Build(Fixture(), new DateOnly(2026, 1, 1));
        Assert.Equal(CalendarStyle.MonthRows, year.Calendar);
        Assert.Equal(302, year.Days.Count);

        var allTime = Build(Fixture(), new DateOnly(2023, 10, 30), comparePrevious: false);
        Assert.Equal(new DateOnly(2024, 11, 1), allTime.Days[0].Day);
        Assert.Equal(Today, allTime.Days[^1].Day);
    }

    [Fact]
    public void Build_TrendWeeklyAndMonthly()
    {
        // The newest point carries the period's last day, so the axis spans the whole period.
        var weekly = Build(WithEarlierSession(Fixture()), From);
        Assert.Equal("Weekly Trend", weekly.TrendTitle);
        Assert.Equal(14400, weekly.TrendReferenceSeconds);
        Assert.Equal(5, weekly.Trend.Count);
        Assert.Equal(new ChartPoint(From, "Sep 30", 0), weekly.Trend[0]);
        Assert.Equal(new ChartPoint(new DateOnly(2026, 10, 16), "Oct 16", 3600 / 7), weekly.Trend[^2]);
        Assert.Equal(new ChartPoint(new DateOnly(2026, 10, 23), "Oct 29", 14400 / 7), weekly.Trend[^1]);

        // From the first session ever: blocks before Oct 20 are dropped and Oct 16..22 is clamped.
        var sinceFirst = Build(Fixture(), From);
        Assert.Equal(new[]
        {
            new ChartPoint(new DateOnly(2026, 10, 20), "Oct 20", 3600 / 3),
            new ChartPoint(new DateOnly(2026, 10, 23), "Oct 29", 14400 / 7),
        }, sinceFirst.Trend);

        var monthly = Build(WithEarlierSession(Fixture()), new DateOnly(2026, 1, 1));
        Assert.Equal("Monthly Trend", monthly.TrendTitle);
        Assert.Equal(14400, monthly.TrendReferenceSeconds);
        Assert.Equal(10, monthly.Trend.Count);
        Assert.Equal(new ChartPoint(new DateOnly(2026, 1, 1), "Jan 2026", 0), monthly.Trend[0]);
        // October has 29 days inside the period.
        Assert.Equal(new ChartPoint(new DateOnly(2026, 10, 1), "Oct 2026", 18000 / 29), monthly.Trend[^1]);
    }

    [Fact]
    public void Build_HoursWeekdaysOutcomes()
    {
        var model = Build(Fixture(), From);

        Assert.Equal(24, model.StartsByHour.Count);
        Assert.Equal(1, model.StartsByHour[8]);
        Assert.Equal(1, model.StartsByHour[9]);
        Assert.Equal(1, model.StartsByHour[14]);
        Assert.Equal(3, model.StartsByHour.Sum());

        // Monday first, from the first session ever: Oct 20..29 holds two Wednesdays and two Tuesdays.
        Assert.Equal(7, model.SecondsByWeekday.Count);
        Assert.Equal(14400 / 2, model.SecondsByWeekday[2]);
        Assert.Equal(3600 / 2, model.SecondsByWeekday[1]);
        Assert.Equal(0, model.SecondsByWeekday[0]);

        // Over the whole period: 09-30..10-29 holds five Wednesdays and four Tuesdays.
        var whole = Build(WithEarlierSession(Fixture()), From);
        Assert.Equal(14400 / 5, whole.SecondsByWeekday[2]);
        Assert.Equal(3600 / 4, whole.SecondsByWeekday[1]);

        Assert.Equal(new OutcomeSplit(2, 1, 0, 6000, 7200), model.Outcomes);
    }

    [Fact]
    public void Build_AveragePlanned_SkipsUnplannedSessions()
    {
        var data = Fixture();
        data.Sessions[0].PlannedSeconds = 0;

        Assert.Equal(7200, Build(data, From).Outcomes.AveragePlannedSeconds);
        Assert.Equal(new OutcomeSplit(0, 0, 0, 0, 0), Build(new HistoryData(), From).Outcomes);
    }

    [Fact]
    public void Build_RecentNotes()
    {
        var data = new HistoryData();
        var longNote = new string('a', 250);
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 1), Text = "First" });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 5), Text = "Second" });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 9), Text = "  Third  " });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 20), Text = longNote });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 28), Text = "Fifth" });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 10, 29), Text = "" });
        data.Notes.Add(new DayNote { Day = new DateOnly(2026, 9, 1), Text = "Before the period" });

        var model = Build(data, From);

        Assert.Equal(new[]
        {
            new NoteQuote(new DateOnly(2026, 10, 28), "Fifth"),
            new NoteQuote(new DateOnly(2026, 10, 20), new string('a', 200) + "…"),
            new NoteQuote(new DateOnly(2026, 10, 9), "Third"),
        }, model.RecentNotes);
        Assert.Equal(5, model.NoteCount);
    }

    [Fact]
    public void Build_ProfilesByTimeDescending()
    {
        var model = Build(Fixture(), From);

        Assert.Equal(new[] { new ProfileRow("Noisy", 2, 14400), new ProfileRow("Regular", 1, 3600) }, model.Profiles);
    }

    [Fact]
    public void LegalNotices_MatchDesignSystemWording()
    {
        Assert.Equal("BlueBuzzah devices are not FDA approved medical devices and not intended to diagnose, treat, cure, or prevent any disease. The information provided through BlueBuzzah is for general wellness purposes only and not a substitute for professional medical advice, diagnosis, or treatment.", LegalNotices.MedicalDisclaimer);
        Assert.Equal("Important: People with implanted electrodes from deep brain stimulation (DBS), heart pacemakers, or other internal electronic devices should take special precautions around all electronic devices, including BlueBuzzah. Consult your physician before use.", LegalNotices.ImplantedDeviceNotice);
    }
}
