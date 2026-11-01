using System.Globalization;
using System.Text;
using BuzzahBuddy.Services.Storage;

namespace BuzzahBuddy.Models;

/// <summary>How far back a shared report reaches.</summary>
public enum ReportRange
{
    Last30Days = 30,
    Last90Days = 90,
    AllTime = 0,
}

/// <summary>A point on the report's trend chart: one 7-day block or one month, as average seconds per day.</summary>
public sealed record ChartPoint(DateOnly Start, string Label, int Seconds);

/// <summary>Session count and time for one profile name.</summary>
public sealed record ProfileRow(string Name, int Sessions, int Seconds);

/// <summary>How the report's Daily Use calendar is drawn.</summary>
public enum CalendarStyle
{
    /// <summary>One 7-column grid per month, side by side.</summary>
    MonthGrids,

    /// <summary>One row of 31 squares per month.</summary>
    MonthRows,
}

/// <summary>A day in the Daily Use calendar and its session time.</summary>
public sealed record DayCell(DateOnly Day, int Seconds);

/// <summary>How the period's sessions ended, and their average length.</summary>
public sealed record OutcomeSplit(int Completed, int StoppedEarly, int EndedEarly, int AverageSeconds, int AveragePlannedSeconds);

/// <summary>A day note the report quotes.</summary>
public sealed record NoteQuote(DateOnly Day, string Text);

/// <summary>
/// What the PDF report shows, computed without any rendering. Use is measured from
/// <see cref="AdherenceFrom"/>: <see cref="From"/>, or the first session ever when that falls
/// later, so days before someone started don't read as missed. <see cref="Summary"/>, the lede,
/// the trend and the weekday averages cover <see cref="AdherenceFrom"/> to <see cref="To"/>.
/// </summary>
public sealed record ReportModel(
    DateOnly From,
    DateOnly To,
    DateOnly AdherenceFrom,
    DateTimeOffset CreatedAt,
    PeriodSummary Summary,
    PeriodSummary? Previous,
    IReadOnlyList<string> Lede,
    CalendarStyle Calendar,
    IReadOnlyList<DayCell> Days,
    string TrendTitle,
    IReadOnlyList<ChartPoint> Trend,
    int TrendReferenceSeconds,
    IReadOnlyList<int> StartsByHour,
    IReadOnlyList<int> SecondsByWeekday,
    OutcomeSplit Outcomes,
    IReadOnlyList<ProfileRow> Profiles,
    IReadOnlyList<NoteQuote> RecentNotes,
    int NoteCount,
    bool Sample = false,
    string Device = "phone");

/// <summary>The PDF report model and CSV the History tab hands to the share sheet.</summary>
public static class HistoryReport
{
    private const string ShortDateFormat = "MMM d";

    /// <summary>Periods up to this long trend by week; longer ones, by month.</summary>
    public const int WeeklyTrendMaxDays = 92;

    /// <summary>Periods spanning this many calendar months or fewer show one grid per month.</summary>
    private const int MonthGridsMaxMonths = 4;

    /// <summary>The calendar shows at most this many months, the most recent.</summary>
    public const int CalendarMaxMonths = 24;

    private const int RecentNoteCount = 3;
    private const int MaxQuoteLength = 200;

    /// <summary>Halves of the period whose daily averages differ by this much read as a rise or fall.</summary>
    private const int TrendChangeSeconds = 15 * 60;

    /// <summary>Inclusive first and last day for <paramref name="range"/>; all time starts at the earliest session.</summary>
    public static (DateOnly From, DateOnly To) Range(ReportRange range, DateOnly today, IEnumerable<SessionRecord> sessions)
    {
        if (range != ReportRange.AllTime)
            return (today.AddDays(1 - (int)range), today);

        // Never after today: sessions dated ahead (the clock moved back) would invert the range.
        var days = sessions.Select(s => s.Day).ToList();
        return (days.Count == 0 || days.Min() > today ? today : days.Min(), today);
    }

    /// <summary>
    /// The shared PDF and CSV names for a report ending on <paramref name="stamp"/> (yyyy-MM-dd).
    /// Demo Mode's say "Sample" so they can't pass for someone's own report.
    /// </summary>
    public static (string Pdf, string Csv) FileNames(string stamp, bool sample)
    {
        var prefix = sample ? "BuzzahBuddy Sample" : "BuzzahBuddy";
        return ($"{prefix} Report {stamp}.pdf", $"{prefix} Sessions {stamp}.csv");
    }

    /// <summary>File patterns that find every file <see cref="FileNames"/> makes, real or sample, to clear old reports.</summary>
    public static IReadOnlyList<string> OldReportPatterns { get; } =
    [
        "BuzzahBuddy Report *",
        "BuzzahBuddy Sessions *",
        "BuzzahBuddy Sample Report *",
        "BuzzahBuddy Sample Sessions *",
    ];

    /// <summary>Sessions that started from <paramref name="from"/> to <paramref name="to"/> inclusive, oldest first.</summary>
    public static IEnumerable<SessionRecord> InRange(IEnumerable<SessionRecord> sessions, DateOnly from, DateOnly to) =>
        sessions.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.StartedAt);

    /// <summary>
    /// One row per session, oldest first, with RFC 4180 quoting and CRLF line endings. A day's note
    /// goes on its first session row; a note on a day without sessions gets a row of its own.
    /// </summary>
    public static string Csv(IEnumerable<SessionRecord> sessions, IEnumerable<DayNote> notes)
    {
        var noteByDay = notes.Where(n => !string.IsNullOrWhiteSpace(n.Text)).GroupBy(n => n.Day).ToDictionary(g => g.Key, g => g.Last().Text);
        var byDay = sessions.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartedAt).ToList());
        var sb = new StringBuilder("Date,Start,End,Session Minutes,Planned Minutes,Profile,Outcome,Day Note\r\n");
        foreach (var day in byDay.Keys.Union(noteByDay.Keys).Order())
        {
            var note = noteByDay.GetValueOrDefault(day, "");
            var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!byDay.TryGetValue(day, out var list))
            {
                AppendRow(sb, date, "", "", "", "", "", "", note);
                continue;
            }
            foreach (var s in list)
            {
                AppendRow(sb,
                    date,
                    s.StartedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                    s.EndedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Minutes(s.TherapySeconds),
                    Minutes(s.PlannedSeconds),
                    s.ProfileName,
                    HistoryText.OutcomeText(s.Outcome),
                    note);
                note = "";
            }
        }
        return sb.ToString();
    }

    /// <summary>Everything the PDF report shows for <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    /// <param name="comparePrevious">Compare with the same number of days before <paramref name="from"/>; false for All Time.</param>
    /// <param name="sample">The data is Demo Mode's sample, which the report says on every page.</param>
    /// <param name="device">The noun for the device the report was recorded on, from <see cref="DeviceNoun"/>.</param>
    public static ReportModel Build(HistoryData data, DateOnly from, DateOnly to, DateTimeOffset createdAt, bool comparePrevious, bool sample = false, string device = "phone")
    {
        var sessions = InRange(data.Sessions, from, to).ToList();
        var secondsByDay = sessions.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.TherapySeconds));
        int SecondsOn(DateOnly day) => secondsByDay.GetValueOrDefault(day);

        // Days before the first session ever aren't missed days: use is counted from then.
        var firstEver = data.Sessions.Count == 0 ? from : data.Sessions.Min(s => s.Day);
        var adherenceFrom = firstEver > from && firstEver <= to ? firstEver : from;
        var summary = HistorySummary.Summarize(sessions, adherenceFrom, to);

        // Compare only with a window that lies wholly after the first session ever; one that
        // started earlier counts days before any use as missed and flatters the change.
        PeriodSummary? previous = null;
        var previousFrom = from.AddDays(-summary.DaysInPeriod);
        if (comparePrevious && adherenceFrom == from && firstEver <= previousFrom)
        {
            var before = HistorySummary.Summarize(data.Sessions, previousFrom, from.AddDays(-1));
            previous = before.Sessions == 0 ? null : before;
        }

        var months = (to.Year * 12 + to.Month) - (from.Year * 12 + from.Month) + 1;
        var calendarStart = new DateOnly(to.Year, to.Month, 1).AddMonths(1 - CalendarMaxMonths);
        if (calendarStart < from)
            calendarStart = from;
        var days = Enumerable.Range(0, to.DayNumber - calendarStart.DayNumber + 1)
            .Select(i => new DayCell(calendarStart.AddDays(i), SecondsOn(calendarStart.AddDays(i))))
            .ToList();

        // Blocks are counted back from the period's end; those before the first session are dropped.
        // The newest weekly point is labeled with the period's last day, so the axis spans it.
        var weekly = to.DayNumber - from.DayNumber + 1 <= WeeklyTrendMaxDays;
        var blocks = weekly ? Blocks(adherenceFrom, to) : MonthBlocks(adherenceFrom, to);
        var trend = blocks
            .Select((b, i) => new ChartPoint(
                b.Start,
                weekly
                    ? (i == blocks.Count - 1 ? to : b.Start).ToString(ShortDateFormat, CultureInfo.InvariantCulture)
                    : b.Start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                DailyAverage(b.Start, b.End, SecondsOn)))
            .ToList();

        var startsByHour = new int[24];
        foreach (var s in sessions)
            startsByHour[s.StartedAt.Hour]++;

        // Monday first; each weekday's total over how many of that weekday fall from the adherence start.
        var weekdayTotals = new int[7];
        var weekdayCounts = new int[7];
        for (var d = adherenceFrom; d <= to; d = d.AddDays(1))
        {
            var i = ((int)d.DayOfWeek + 6) % 7;
            weekdayTotals[i] += SecondsOn(d);
            weekdayCounts[i]++;
        }
        var secondsByWeekday = weekdayTotals.Select((t, i) => weekdayCounts[i] == 0 ? 0 : t / weekdayCounts[i]).ToList();

        var planned = sessions.Where(s => s.PlannedSeconds > 0).ToList();
        var outcomes = new OutcomeSplit(
            summary.Completed,
            summary.StoppedEarly,
            summary.EndedEarly,
            sessions.Count == 0 ? 0 : summary.TherapySeconds / sessions.Count,
            planned.Count == 0 ? 0 : planned.Sum(s => s.PlannedSeconds) / planned.Count);

        var profiles = sessions
            .GroupBy(s => s.ProfileName)
            .Select(g => new ProfileRow(g.Key, g.Count(), g.Sum(s => s.TherapySeconds)))
            .OrderByDescending(p => p.Seconds)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var notes = data.Notes
            .Where(n => n.Day >= from && n.Day <= to && !string.IsNullOrWhiteSpace(n.Text))
            .OrderByDescending(n => n.Day)
            .ToList();
        var recentNotes = notes.Take(RecentNoteCount).Select(n => new NoteQuote(n.Day, Quote200(n.Text.Trim()))).ToList();

        return new ReportModel(
            from,
            to,
            adherenceFrom,
            createdAt,
            summary,
            previous,
            Lede(summary, adherenceFrom > from, SecondsOn),
            months <= MonthGridsMaxMonths ? CalendarStyle.MonthGrids : CalendarStyle.MonthRows,
            days,
            weekly ? "Weekly Trend" : "Monthly Trend",
            trend,
            HistorySummary.StudyDailySeconds,
            startsByHour,
            secondsByWeekday,
            outcomes,
            profiles,
            recentNotes,
            notes.Count,
            sample,
            device);
    }

    /// <summary>The summary sentences above the tiles.</summary>
    /// <param name="sinceFirst">The summary starts at the first session ever, after the period's first day.</param>
    private static List<string> Lede(PeriodSummary summary, bool sinceFirst, Func<DateOnly, int> secondsOn)
    {
        if (summary.Sessions == 0)
            return ["No sessions were recorded in this period."];

        var since = sinceFirst ? $" since the first session on {summary.From.ToString(ShortDateFormat, CultureInfo.InvariantCulture)}" : "";
        var lede = new List<string>
        {
            $"Used on {summary.DaysWithSessions} of {summary.DaysInPeriod} {(summary.DaysInPeriod == 1 ? "day" : "days")}{since}, averaging {DurationText.Short(summary.AverageSecondsPerSessionDay)} on those days.",
        };

        // Missed days count as zero. A 1-day period has no first half, so it reads as steady.
        var firstDays = summary.DaysInPeriod / 2;
        var secondStart = summary.From.AddDays(firstDays);
        var first = firstDays == 0 ? 0 : DailyAverage(summary.From, secondStart.AddDays(-1), secondsOn);
        var second = DailyAverage(secondStart, summary.To, secondsOn);
        if (firstDays > 0 && Math.Abs(second - first) >= TrendChangeSeconds)
            lede.Add($"Daily time {(second > first ? "rose" : "fell")} from about {About(first)} in the first half of the period to about {About(second)} in the second half.");
        else
            lede.Add($"Daily time held steady at about {About(summary.TherapySeconds / summary.DaysInPeriod)}.");

        // The longest run of days without a session, the earliest if two tie.
        int longest = 0, run = 0;
        DateOnly gapEnd = summary.From;
        for (var d = summary.From; d <= summary.To; d = d.AddDays(1))
        {
            run = secondsOn(d) > 0 ? 0 : run + 1;
            if (run > longest)
            {
                longest = run;
                gapEnd = d;
            }
        }
        lede.Add(longest >= 2
            ? $"The longest stretch without a session was {longest} days, {gapEnd.AddDays(1 - longest).ToString(ShortDateFormat, CultureInfo.InvariantCulture)} to {gapEnd.ToString(ShortDateFormat, CultureInfo.InvariantCulture)}."
            : "There was no gap longer than a day.");
        return lede;
    }

    /// <summary>Average seconds per day from <paramref name="start"/> to <paramref name="end"/>, inclusive, empty days included.</summary>
    private static int DailyAverage(DateOnly start, DateOnly end, Func<DateOnly, int> secondsOn)
    {
        var total = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            total += secondsOn(d);
        return total / (end.DayNumber - start.DayNumber + 1);
    }

    /// <summary>A duration rounded to the nearest 10 minutes, as "2h 30min".</summary>
    private static string About(int seconds) =>
        DurationText.Short((int)Math.Round(seconds / 600.0, MidpointRounding.AwayFromZero) * 600);

    /// <summary>At most 200 characters, then "…", never splitting a surrogate pair.</summary>
    private static string Quote200(string text)
    {
        if (text.Length <= MaxQuoteLength)
            return text;
        var cut = char.IsHighSurrogate(text[MaxQuoteLength - 1]) ? MaxQuoteLength - 1 : MaxQuoteLength;
        return text[..cut] + "…";
    }

    /// <summary>Calendar months from <paramref name="from"/> to <paramref name="to"/>, each clamped to the period.</summary>
    private static List<(DateOnly Start, DateOnly End)> MonthBlocks(DateOnly from, DateOnly to)
    {
        var blocks = new List<(DateOnly, DateOnly)>();
        for (var start = new DateOnly(from.Year, from.Month, 1); start <= to; start = start.AddMonths(1))
        {
            var end = start.AddMonths(1).AddDays(-1);
            blocks.Add((start < from ? from : start, end > to ? to : end));
        }
        return blocks;
    }

    /// <summary>7-day blocks counted back from <paramref name="to"/>, oldest first, the oldest clamped to <paramref name="from"/>.</summary>
    private static List<(DateOnly Start, DateOnly End)> Blocks(DateOnly from, DateOnly to)
    {
        var blocks = new List<(DateOnly, DateOnly)>();
        for (var end = to; end >= from; end = end.AddDays(-7))
            blocks.Add((end.AddDays(-6) < from ? from : end.AddDays(-6), end));
        blocks.Reverse();
        return blocks;
    }

    private static string Minutes(int seconds) =>
        ((int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static void AppendRow(StringBuilder sb, params string[] fields) =>
        sb.Append(string.Join(",", fields.Select(f => Quote(Defuse(f))))).Append("\r\n");

    /// <summary>A spreadsheet runs a field starting with one of these as a formula; a leading ' keeps it text.</summary>
    private static string Defuse(string field) =>
        field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + field : field;

    private static string Quote(string field) =>
        field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
}
