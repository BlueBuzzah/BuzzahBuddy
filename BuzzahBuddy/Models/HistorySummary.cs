namespace BuzzahBuddy.Models;

/// <summary>One calendar day of history: its sessions (oldest first), total, and note.</summary>
public sealed record DaySummary(DateOnly Day, int TherapySeconds, IReadOnlyList<SessionRecord> Sessions, string? Note);

/// <summary>Totals over an inclusive range of days.</summary>
public sealed record PeriodSummary(
    DateOnly From,
    DateOnly To,
    int DaysInPeriod,
    int DaysWithSessions,
    int TherapySeconds,
    int Sessions,
    int Completed,
    int StoppedEarly,
    int EndedEarly)
{
    public int AverageSecondsPerSessionDay => DaysWithSessions == 0 ? 0 : TherapySeconds / DaysWithSessions;
}

/// <summary>Groups recorded sessions by the local day they started on.</summary>
public static class HistorySummary
{
    /// <summary>The study's daily dose, which is a full bar or calendar well in History: 2 h twice a day.</summary>
    public const int StudyDailySeconds = ResearchDefaults.SessionMinutes * ResearchDefaults.SessionsPerDay * 60;

    /// <summary>Days with a session or a note, plus today, newest first.</summary>
    public static IReadOnlyList<DaySummary> Days(IEnumerable<SessionRecord> sessions, IEnumerable<DayNote> notes, DateOnly today)
    {
        var byDay = sessions.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartedAt).ToList());
        var noteByDay = new Dictionary<DateOnly, string>();
        foreach (var n in notes)
            noteByDay[n.Day] = n.Text;

        var keys = new HashSet<DateOnly>(byDay.Keys) { today };
        keys.UnionWith(noteByDay.Keys);

        return keys.OrderByDescending(d => d).Select(d => Build(d, byDay, noteByDay)).ToList();
    }

    /// <summary>Totals for sessions whose start day falls in <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    public static PeriodSummary Summarize(IEnumerable<SessionRecord> sessions, DateOnly from, DateOnly to)
    {
        var inRange = sessions.Where(s => s.Day >= from && s.Day <= to).ToList();
        return new PeriodSummary(
            from,
            to,
            to.DayNumber - from.DayNumber + 1,
            inRange.Select(s => s.Day).Distinct().Count(),
            inRange.Sum(s => s.TherapySeconds),
            inRange.Count,
            inRange.Count(s => s.Outcome == SessionOutcome.Completed),
            inRange.Count(s => s.Outcome == SessionOutcome.StoppedEarly),
            inRange.Count(s => s.Outcome == SessionOutcome.EndedEarly));
    }

    /// <summary>Share of the study's daily dose, clamped to a bar's 0 to 1.</summary>
    public static double BarFraction(int therapySeconds) =>
        Math.Clamp(therapySeconds / (double)StudyDailySeconds, 0, 1);

    private static DaySummary Build(DateOnly day, Dictionary<DateOnly, List<SessionRecord>> byDay, Dictionary<DateOnly, string> notes)
    {
        var list = byDay.TryGetValue(day, out var l) ? l : new List<SessionRecord>();
        return new DaySummary(day, list.Sum(s => s.TherapySeconds), list, notes.GetValueOrDefault(day));
    }
}
