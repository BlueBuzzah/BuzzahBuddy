using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>A day as the History list shows it.</summary>
public sealed record DayRow(
    DateOnly Day,
    string Title,
    string Total,
    IReadOnlyList<string> SessionLines,
    string? Note,
    string NoteButtonText,
    string Spoken);

/// <summary>A row in the weekly or monthly average chart.</summary>
public sealed record BarRow(string Label, string Value, double Fraction, string Spoken);

/// <summary>A figure in the History overview.</summary>
public sealed record StatTile(string Label, string Value, string Detail, string Spoken);

/// <summary>Display and screen-reader text for history.</summary>
public static class HistoryText
{
    /// <summary>The monthly average chart shows at most this many months, the most recent.</summary>
    public const int MonthlyRowLimit = 12;

    private static string UsualShareText => $"as a PDF and a spreadsheet. It leaves this {DeviceNoun.Current} only when you share it.";

    /// <summary>The four overview tiles: Total Time, Days Used, Average Day, Sessions.</summary>
    public static IReadOnlyList<StatTile> Tiles(ReportModel m, ReportRange range, CultureInfo culture)
    {
        var s = m.Summary;
        var since = m.AdherenceFrom > m.From || range == ReportRange.AllTime;
        var format = m.AdherenceFrom.Year == m.To.Year ? "MMM d" : "MMM d, yyyy";
        var totalDetail = since
            ? $"since your first session on {m.AdherenceFrom.ToString(format, culture)}"
            : $"in the last {(int)range} days";
        var count = s.Sessions.ToString(culture);
        var each = s.Sessions == 0 ? "no sessions" : null;

        return
        [
            new StatTile("Total Time", DurationText.Short(s.TherapySeconds), totalDetail,
                $"Total Time, {DurationText.Spoken(s.TherapySeconds)}, {totalDetail}"),
            Tile("Days Used", s.DaysWithSessions.ToString(culture), s.DaysWithSessions == 1 ? "day with a session" : "days with a session"),
            new StatTile("Average Day", DurationText.Short(s.AverageSecondsPerSessionDay), "on days used",
                $"Average Day, {DurationText.Spoken(s.AverageSecondsPerSessionDay)}, on days used"),
            new StatTile("Sessions", count,
                each ?? $"{DurationText.Short(m.Outcomes.AverageSeconds)} each on average",
                $"Sessions, {count}, {each ?? $"{DurationText.Spoken(m.Outcomes.AverageSeconds)} each on average"}"),
        ];
    }

    private static StatTile Tile(string label, string value, string detail) =>
        new(label, value, detail, $"{label}, {value}, {detail}");

    /// <summary>True when the trend is by week; longer periods trend by month.</summary>
    public static bool IsWeekly(ReportModel m) =>
        m.To.DayNumber - m.From.DayNumber + 1 <= HistoryReport.WeeklyTrendMaxDays;

    /// <summary>Average time per day for each week or month of the trend, oldest first.</summary>
    public static IReadOnlyList<BarRow> TrendRows(ReportModel m, CultureInfo culture)
    {
        var weekly = IsWeekly(m);
        var points = weekly ? m.Trend : m.Trend.TakeLast(MonthlyRowLimit).ToList();
        return points.Select((p, i) =>
        {
            var next = i + 1 < points.Count ? points[i + 1].Start : m.To.AddDays(1);
            var end = next.AddDays(-1);
            var (label, spokenLabel) = weekly
                ? WeekLabels(p.Start, end, culture)
                : (p.Start.ToString("MMM yyyy", culture), p.Start.ToString("MMMM yyyy", culture));
            return new BarRow(
                label,
                $"{DurationText.Short(p.Seconds)} a day",
                HistorySummary.BarFraction(p.Seconds),
                $"{spokenLabel}, {DurationText.Spoken(p.Seconds)} a day");
        }).ToList();
    }

    private static (string Label, string Spoken) WeekLabels(DateOnly start, DateOnly end, CultureInfo culture)
    {
        if (start == end)
            return (start.ToString("MMM d", culture), start.ToString("MMMM d", culture));
        if (start.Month == end.Month && start.Year == end.Year)
            return ($"{start.ToString("MMM d", culture)}–{end.Day.ToString(culture)}",
                $"{start.ToString("MMMM d", culture)} to {end.Day.ToString(culture)}");
        return ($"{start.ToString("MMM d", culture)}–{end.ToString("MMM d", culture)}",
            $"{start.ToString("MMMM d", culture)} to {end.ToString("MMMM d", culture)}");
    }

    public static string TrendTitle(bool weekly) => weekly ? "Weekly Average" : "Monthly Average";

    public static string TrendCaption(bool weekly, bool capped) => weekly
        ? "Average time per day, by week. A full bar is 4 hours."
        : "Average time per day, by month. A full bar is 4 hours." + (capped ? " The last 12 months." : "");

    public static string ShareText(ReportRange range) => range switch
    {
        ReportRange.Last30Days => $"Share these 30 days with your care team {UsualShareText}",
        ReportRange.Last90Days => $"Share these 90 days with your care team {UsualShareText}",
        _ => $"Share all your sessions with your care team {UsualShareText}",
    };

    public static string EmptyPeriodText(ReportRange range) => range switch
    {
        ReportRange.Last30Days => "No sessions in the last 30 days.",
        ReportRange.Last90Days => "No sessions in the last 90 days.",
        _ => "No sessions yet.",
    };

    public static string OutcomeText(SessionOutcome outcome) => outcome switch
    {
        SessionOutcome.Completed => "Completed",
        SessionOutcome.StoppedEarly => "Stopped early",
        _ => "Ended early",
    };

    public static string DayTitle(DateOnly day, DateOnly today, CultureInfo culture)
    {
        if (day == today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";
        return day.ToString("dddd, MMM d", culture);
    }

    public static string SessionLine(SessionRecord s, CultureInfo culture) =>
        string.Join(" · ",
            s.StartedAt.ToString(culture.DateTimeFormat.ShortTimePattern, culture),
            DurationText.Short(s.TherapySeconds),
            s.ProfileName,
            OutcomeText(s.Outcome));

    public static IReadOnlyList<DayRow> DayRows(IReadOnlyList<DaySummary> days, DateOnly today, CultureInfo culture) =>
        days.Select(d => Row(d, today, culture)).ToList();

    private static DayRow Row(DaySummary d, DateOnly today, CultureInfo culture)
    {
        var title = DayTitle(d.Day, today, culture);
        var hasNote = !string.IsNullOrEmpty(d.Note);
        var parts = new List<string> { title };
        List<string> lines;

        if (d.Sessions.Count == 0)
        {
            var none = d.Day == today ? "No sessions yet" : "No sessions";
            lines = new List<string> { none };
            parts.Add(none);
        }
        else
        {
            lines = d.Sessions.Select(s => SessionLine(s, culture)).ToList();
            parts.Add(DurationText.Spoken(d.TherapySeconds));
            parts.Add(d.Sessions.Count == 1 ? "1 session" : $"{d.Sessions.Count} sessions");
        }

        if (hasNote)
            parts.Add($"Note: {d.Note}");

        return new DayRow(
            d.Day,
            title,
            DurationText.Short(d.TherapySeconds),
            lines,
            d.Note,
            hasNote ? "Edit Note" : "Add Note",
            string.Join(". ", parts));
    }
}
