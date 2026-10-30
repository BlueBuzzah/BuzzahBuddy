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

/// <summary>A bar in the "Last 7 Days" chart.</summary>
public sealed record WeekBar(string Label, string Value, double Fraction, string Spoken);

/// <summary>Display and screen-reader text for history.</summary>
public static class HistoryText
{
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

    public static IReadOnlyList<WeekBar> WeekBars(IReadOnlyList<DaySummary> days, CultureInfo culture) =>
        days.Select(d => new WeekBar(
            d.Day.ToString("ddd d", culture),
            DurationText.Short(d.TherapySeconds),
            HistorySummary.BarFraction(d.TherapySeconds),
            d.TherapySeconds == 0
                ? $"{d.Day.ToString("dddd", culture)}, no sessions"
                : $"{d.Day.ToString("dddd", culture)}, {DurationText.Spoken(d.TherapySeconds)}")).ToList();

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
