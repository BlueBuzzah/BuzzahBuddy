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

/// <summary>A column in the report's session-time chart: one day, or one 7-day block.</summary>
public sealed record ChartPoint(DateOnly Start, string Label, int Seconds);

/// <summary>A row of the report's "By Week" table.</summary>
public sealed record WeekRow(DateOnly Start, DateOnly End, int DaysWithSessions, int Seconds);

/// <summary>Session count and time for one profile name.</summary>
public sealed record ProfileRow(string Name, int Sessions, int Seconds);

/// <summary>What the PDF report shows, computed without any rendering.</summary>
public sealed record ReportModel(
    DateOnly From,
    DateOnly To,
    DateTimeOffset CreatedAt,
    PeriodSummary Summary,
    string ChartTitle,
    IReadOnlyList<ChartPoint> Chart,
    int ChartReferenceSeconds,
    int ChartMaxSeconds,
    IReadOnlyList<WeekRow> Weeks,
    IReadOnlyList<ProfileRow> Profiles,
    IReadOnlyList<DaySummary> LogDays);

/// <summary>The PDF report model and CSV the History tab hands to the share sheet.</summary>
public static class HistoryReport
{
    private const string ShortDateFormat = "MMM d";

    /// <summary>Periods up to this long chart one column per day; longer ones, one per week.</summary>
    private const int DailyChartMaxDays = 92;

    /// <summary>Inclusive first and last day for <paramref name="range"/>; all time starts at the earliest session.</summary>
    public static (DateOnly From, DateOnly To) Range(ReportRange range, DateOnly today, IEnumerable<SessionRecord> sessions)
    {
        if (range != ReportRange.AllTime)
            return (today.AddDays(1 - (int)range), today);

        var days = sessions.Select(s => s.Day).ToList();
        return (days.Count == 0 ? today : days.Min(), today);
    }

    /// <summary>Sessions that started from <paramref name="from"/> to <paramref name="to"/> inclusive, oldest first.</summary>
    public static IEnumerable<SessionRecord> InRange(IEnumerable<SessionRecord> sessions, DateOnly from, DateOnly to) =>
        sessions.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.StartedAt);

    /// <summary>One row per session, oldest first, with RFC 4180 quoting and CRLF line endings.</summary>
    public static string Csv(IEnumerable<SessionRecord> sessions)
    {
        var sb = new StringBuilder("Date,Start,End,Session Minutes,Planned Minutes,Profile,Outcome\r\n");
        foreach (var s in sessions.OrderBy(s => s.StartedAt))
        {
            var fields = new[]
            {
                s.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                s.StartedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                s.EndedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                Minutes(s.TherapySeconds),
                Minutes(s.PlannedSeconds),
                s.ProfileName,
                HistoryText.OutcomeText(s.Outcome),
            };
            sb.Append(string.Join(",", fields.Select(Quote))).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Everything the PDF report shows for <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    public static ReportModel Build(HistoryData data, DateOnly from, DateOnly to, DateTimeOffset createdAt)
    {
        var sessions = InRange(data.Sessions, from, to).ToList();
        var summary = HistorySummary.Summarize(sessions, from, to);
        var weeks = Blocks(from, to)
            .Select(b => HistorySummary.Summarize(sessions, b.Start, b.End))
            .Select(p => new WeekRow(p.From, p.To, p.DaysWithSessions, p.TherapySeconds))
            .ToList();

        var daily = summary.DaysInPeriod <= DailyChartMaxDays;
        var reference = daily ? HistorySummary.StudyDailySeconds : 7 * HistorySummary.StudyDailySeconds;
        var secondsByDay = sessions.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.TherapySeconds));
        var points = daily
            ? Enumerable.Range(0, summary.DaysInPeriod)
                .Select(i => (Start: from.AddDays(i), Seconds: secondsByDay.GetValueOrDefault(from.AddDays(i))))
                .ToList()
            : weeks.Select(w => (w.Start, w.Seconds)).ToList();
        // The newest point and every 7th one back from it carry a date.
        var chart = points
            .Select((p, i) => new ChartPoint(
                p.Start,
                (points.Count - 1 - i) % 7 == 0 ? p.Start.ToString(ShortDateFormat, CultureInfo.InvariantCulture) : "",
                p.Seconds))
            .ToList();
        var largest = chart.Count == 0 ? 0 : chart.Max(p => p.Seconds);
        var max = Math.Max(reference, (largest + 3599) / 3600 * 3600);

        var profiles = sessions
            .GroupBy(s => s.ProfileName)
            .Select(g => new ProfileRow(g.Key, g.Count(), g.Sum(s => s.TherapySeconds)))
            .OrderByDescending(p => p.Seconds)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var notes = data.Notes.Where(n => n.Day >= from && n.Day <= to && !string.IsNullOrEmpty(n.Text)).ToList();
        var logDays = HistorySummary.Days(sessions, notes, to)
            .Where(d => d.Sessions.Count > 0 || d.Note != null)
            .OrderBy(d => d.Day)
            .ToList();

        return new ReportModel(
            from,
            to,
            createdAt,
            summary,
            daily ? "Daily Session Time" : "Weekly Session Time",
            chart,
            reference,
            max,
            weeks,
            profiles,
            logDays);
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

    private static string Quote(string field) =>
        field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
}
