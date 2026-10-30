using System.Globalization;
using BuzzahBuddy.Services.Storage;

namespace BuzzahBuddy.Models;

/// <summary>
/// The sample history History shows in Demo Mode: a fixed 60-day pattern laid onto the 60 days
/// ending today. Built in memory each time and never stored, so the person's own history is untouched.
/// </summary>
public static class DemoHistory
{
    public const int Days = 60;

    private const int PlannedSeconds = 7200;
    private const string DeviceId = "demo";

    /// <summary>
    /// One letter per day, oldest first, the last one today: B both sessions, M morning only,
    /// A afternoon only, - none. More misses early on, a 3-day gap, and today's morning only.
    /// </summary>
    private const string Pattern =
        "BMB-BBMBB-" +
        "BBMB---BBM" +
        "BBBMBBBBBB" +
        "BBBBMBBB-B" +
        "BBABBBBBBB" +
        "BBBBBMBBBM";

    private const int Regular = 1;
    private const int Noisy = 2;
    private const int Gentle = 5;

    /// <summary>Sessions that didn't run their planned length: (day index, afternoon, outcome, minutes).</summary>
    private static readonly (int Day, bool Afternoon, SessionOutcome Outcome, int Minutes)[] ShortSessions =
    [
        (5, true, SessionOutcome.StoppedEarly, 75),
        (12, false, SessionOutcome.StoppedEarly, 45),
        (22, true, SessionOutcome.EndedEarly, 100),
        (24, true, SessionOutcome.StoppedEarly, 95),
        (41, false, SessionOutcome.StoppedEarly, 60),
        (47, false, SessionOutcome.EndedEarly, 85),
    ];

    /// <summary>Days whose morning session ran Gentle.</summary>
    private static readonly int[] GentleMornings = [8, 33, 50];

    private static readonly (int Day, string Text)[] Notes =
    [
        (6, "Long drive, only did the morning."),
        (12, "Slept badly. Shorter session today."),
        (15, "Visiting family. The gloves stayed home."),
        (27, "Tremor felt lighter after lunch."),
        (44, "Hands steadier during the morning walk."),
        (55, "Forgot the afternoon session."),
    ];

    /// <summary>The same sample for the same <paramref name="today"/>, with start times in <paramref name="utcOffset"/>.</summary>
    public static HistoryData Build(DateOnly today, TimeSpan utcOffset)
    {
        var names = TherapyProfile.GetPresetProfiles().ToDictionary(p => p.ProfileId, p => p.Name);
        var first = today.AddDays(1 - Days);
        var data = new HistoryData();

        for (var i = 0; i < Days; i++)
        {
            var day = first.AddDays(i);
            var kind = Pattern[i];
            var n = 0;
            if (kind is 'B' or 'M')
            {
                // 7:30 to 9:00 in 15-minute steps; today's starts early, so it reads as done for most of the day.
                var start = new TimeOnly(7, 30).AddMinutes(i == Days - 1 ? 0 : (i * 3) % 7 * 15);
                data.Sessions.Add(Session(day, ++n, start, afternoon: false, GentleMornings.Contains(i) ? Gentle : Noisy));
            }
            if (kind is 'B' or 'A')
            {
                // 13:30 to 15:30 in 15-minute steps.
                var start = new TimeOnly(13, 30).AddMinutes((i * 5) % 9 * 15);
                data.Sessions.Add(Session(day, ++n, start, afternoon: true, i % 4 == 1 ? Regular : Noisy));
            }

            SessionRecord Session(DateOnly d, int number, TimeOnly start, bool afternoon, int profileId)
            {
                var (outcome, seconds) = ShortSessions
                    .Where(s => s.Day == i && s.Afternoon == afternoon)
                    .Select(s => (s.Outcome, s.Minutes * 60))
                    .DefaultIfEmpty((SessionOutcome.Completed, PlannedSeconds))
                    .First();
                var startedAt = new DateTimeOffset(d.ToDateTime(start), utcOffset);
                return new SessionRecord
                {
                    Id = $"demo-{d.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{number}",
                    StartedAt = startedAt,
                    EndedAt = startedAt.AddSeconds(seconds),
                    TherapySeconds = seconds,
                    PlannedSeconds = PlannedSeconds,
                    ProfileId = profileId,
                    ProfileName = names[profileId],
                    Outcome = outcome,
                    DeviceId = DeviceId,
                };
            }
        }

        foreach (var (index, text) in Notes)
            data.Notes.Add(new DayNote { Day = first.AddDays(index), Text = text });
        return data;
    }
}
