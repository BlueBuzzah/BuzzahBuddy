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

/// <summary>The plain-text report and CSV the History tab hands to the share sheet.</summary>
public static class HistoryReport
{
    private const string DateFormat = "MMM d, yyyy";
    private const string ShortDateFormat = "MMM d";

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

    /// <summary>The shareable report for <paramref name="from"/> to <paramref name="to"/>, with <c>\n</c> line endings.</summary>
    public static string Text(HistoryData data, DateOnly from, DateOnly to, DateTimeOffset createdAt, CultureInfo culture)
    {
        var sessions = InRange(data.Sessions, from, to).ToList();
        var sum = HistorySummary.Summarize(sessions, from, to);
        var sb = new StringBuilder();

        sb.Append("BuzzahBuddy Session Report\n");
        sb.Append($"Period: {Date(from, DateFormat, culture)} to {Date(to, DateFormat, culture)} ({Days(sum.DaysInPeriod)})\n");
        sb.Append($"Created: {createdAt.ToString(DateFormat, culture)}\n\n");

        sb.Append("Summary\n");
        sb.Append($"Days with sessions: {sum.DaysWithSessions} of {sum.DaysInPeriod}\n");
        sb.Append($"Total session time: {DurationText.Short(sum.TherapySeconds)}\n");
        sb.Append($"Average on days with sessions: {DurationText.Short(sum.AverageSecondsPerSessionDay)}\n");
        sb.Append($"Sessions: {sum.Sessions} ({sum.Completed} completed, {sum.StoppedEarly} stopped early, {sum.EndedEarly} ended early)\n\n");

        sb.Append("Early studies of vibrotactile coordinated reset used 2 hours of stimulation, twice a day (Pfeifer et al. 2021).\n\n");

        sb.Append("By week\n");
        var blocks = new List<PeriodSummary>();
        for (var end = to; end >= from; end = end.AddDays(-7))
        {
            var start = end.AddDays(-6) < from ? from : end.AddDays(-6);
            blocks.Add(HistorySummary.Summarize(sessions, start, end));
        }
        blocks.Reverse();
        foreach (var b in blocks)
            sb.Append($"{Date(b.From, ShortDateFormat, culture)} to {Date(b.To, ShortDateFormat, culture)}: {Days(b.DaysWithSessions)}, {DurationText.Short(b.TherapySeconds)}\n");
        sb.Append('\n');

        sb.Append("Daily log\n");
        var notes = data.Notes.Where(n => n.Day >= from && n.Day <= to && !string.IsNullOrEmpty(n.Text)).ToList();
        var logDays = HistorySummary.Days(sessions, notes, to)
            .Where(d => d.Sessions.Count > 0 || d.Note != null)
            .OrderBy(d => d.Day)
            .ToList();
        if (logDays.Count == 0)
            sb.Append("No sessions in this period.\n");
        foreach (var d in logDays)
        {
            sb.Append($"{Date(d.Day, DateFormat, culture)}: {DurationText.Short(d.TherapySeconds)}, {Count(d.Sessions.Count, "session")}\n");
            foreach (var s in d.Sessions)
                sb.Append($"  {s.StartedAt.ToString(culture.DateTimeFormat.ShortTimePattern, culture)}, {DurationText.Short(s.TherapySeconds)}, {s.ProfileName}, {HistoryText.OutcomeText(s.Outcome)}\n");
            if (!string.IsNullOrEmpty(d.Note))
            {
                var lines = d.Note.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                sb.Append("  Note: ").Append(lines[0]).Append('\n');
                foreach (var line in lines.Skip(1))
                    sb.Append("  ").Append(line).Append('\n');
            }
        }
        sb.Append('\n');

        sb.Append("About these numbers\n");
        sb.Append("Times are what BuzzahBuddy recorded on this phone. Paused time isn't counted. Sessions your gloves ran while this phone wasn't connected may be missing.\n\n");
        sb.Append(LegalNotices.MedicalDisclaimer).Append("\n\n");
        sb.Append(LegalNotices.ImplantedDeviceNotice).Append('\n');
        return sb.ToString();
    }

    private static string Date(DateOnly d, string format, CultureInfo culture) => d.ToString(format, culture);

    private static string Days(int n) => Count(n, "day");

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Minutes(int seconds) =>
        ((int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static string Quote(string field) =>
        field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
}
