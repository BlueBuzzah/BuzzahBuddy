using System.Text.Json;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Storage;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class DemoHistoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-7);

    private static HistoryData Build() => DemoHistory.Build(Today, Offset);

    [Fact]
    public void Build_SameTodayTwice_SameOutput()
    {
        var a = JsonSerializer.Serialize(Build(), StorageJsonContext.Default.HistoryData);
        var b = JsonSerializer.Serialize(Build(), StorageJsonContext.Default.HistoryData);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Build_Covers60DaysEndingToday()
    {
        var data = Build();
        var days = data.Sessions.Select(s => s.Day).Distinct().ToList();

        Assert.All(data.Sessions, s => Assert.InRange(s.Day, Today.AddDays(-59), Today));
        Assert.All(data.Sessions, s => Assert.Equal(Offset, s.StartedAt.Offset));
        Assert.Equal(54, days.Count);
        Assert.Equal(Today.AddDays(-59), days.Min());
        Assert.Equal(99, data.Sessions.Count);
    }

    [Fact]
    public void Build_HasAThreeDayGap()
    {
        var used = Build().Sessions.Select(s => s.Day).ToHashSet();
        var longest = 0;
        var run = 0;
        for (var d = Today.AddDays(-59); d <= Today; d = d.AddDays(1))
        {
            run = used.Contains(d) ? 0 : run + 1;
            longest = Math.Max(longest, run);
        }

        Assert.Equal(3, longest);
    }

    [Fact]
    public void Build_TodayHasOneMorningSession()
    {
        var today = Assert.Single(Build().Sessions, s => s.Day == Today);

        Assert.True(today.StartedAt.Hour < 12);
        Assert.Equal(SessionOutcome.Completed, today.Outcome);
    }

    [Fact]
    public void Build_Outcomes()
    {
        var sessions = Build().Sessions;

        Assert.Contains(sessions, s => s.Outcome == SessionOutcome.StoppedEarly);
        Assert.Contains(sessions, s => s.Outcome == SessionOutcome.EndedEarly);
        Assert.True(sessions.Count(s => s.Outcome == SessionOutcome.Completed) > sessions.Count * 0.9);
        Assert.All(sessions, s =>
        {
            Assert.Equal(7200, s.PlannedSeconds);
            Assert.InRange(s.TherapySeconds, 60, s.PlannedSeconds);
            Assert.Equal(s.Outcome == SessionOutcome.Completed, s.TherapySeconds == s.PlannedSeconds);
            Assert.Equal(s.StartedAt.AddSeconds(s.TherapySeconds), s.EndedAt);
        });
    }

    [Fact]
    public void Build_StartsNearTheStudySchedule()
    {
        foreach (var s in Build().Sessions)
        {
            var t = TimeOnly.FromDateTime(s.StartedAt.DateTime);
            Assert.True(
                t >= new TimeOnly(7, 30) && t <= new TimeOnly(9, 0) || t >= new TimeOnly(13, 30) && t <= new TimeOnly(15, 30),
                $"{s.Id} starts at {t}");
        }
    }

    [Fact]
    public void Build_SessionsOnADayDoNotOverlap()
    {
        foreach (var day in Build().Sessions.GroupBy(s => s.Day))
        {
            var list = day.OrderBy(s => s.StartedAt).ToList();
            for (var i = 1; i < list.Count; i++)
                Assert.True(list[i - 1].EndedAt <= list[i].StartedAt, $"{list[i - 1].Id} overlaps {list[i].Id}");
        }
    }

    [Fact]
    public void Build_IdsStableAndDistinct()
    {
        var sessions = Build().Sessions;

        Assert.Equal(sessions.Count, sessions.Select(s => s.Id).Distinct().Count());
        Assert.All(sessions, s => Assert.StartsWith($"demo-{s.Day:yyyyMMdd}-", s.Id));
        Assert.All(sessions, s => Assert.Equal("demo", s.DeviceId));
    }

    [Fact]
    public void Build_ProfilesArePresets_MostlyNoisy()
    {
        var presets = TherapyProfile.GetPresetProfiles().ToDictionary(p => p.ProfileId, p => p.Name);
        var sessions = Build().Sessions;

        Assert.All(sessions, s => Assert.Equal(presets[s.ProfileId], s.ProfileName));
        var counts = sessions.GroupBy(s => s.ProfileName).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(["Gentle", "Noisy", "Regular"], counts.Keys.Order(StringComparer.Ordinal));
        Assert.True(counts["Noisy"] > counts["Regular"] && counts["Regular"] > counts["Gentle"]);
    }

    [Fact]
    public void Build_UseRisesOverThePeriod()
    {
        var sessions = Build().Sessions;
        var first = sessions.Where(s => s.Day < Today.AddDays(-29)).Sum(s => s.TherapySeconds);
        var second = sessions.Where(s => s.Day >= Today.AddDays(-29)).Sum(s => s.TherapySeconds);

        Assert.True(second > first, $"{first} then {second}");
    }

    [Fact]
    public void Build_Notes()
    {
        var notes = Build().Notes;

        Assert.InRange(notes.Count, 5, 7);
        Assert.Equal(notes.Count, notes.Select(n => n.Day).Distinct().Count());
        Assert.All(notes, n =>
        {
            Assert.InRange(n.Day, Today.AddDays(-59), Today);
            Assert.False(string.IsNullOrWhiteSpace(n.Text));
            Assert.True(n.Text.All(c => c < 0x80), $"Not plain text: {n.Text}");
        });
    }

    [Fact]
    public void Report_Over30And90Days_HasTilesAndTrend()
    {
        var data = Build();
        var created = new DateTimeOffset(2026, 10, 30, 12, 0, 0, Offset);

        foreach (var range in new[] { ReportRange.Last30Days, ReportRange.Last90Days })
        {
            var (from, to) = HistoryReport.Range(range, Today, data.Sessions);
            var model = HistoryReport.Build(data, from, to, created, comparePrevious: true);

            Assert.True(model.Summary.Sessions > 0);
            Assert.True(model.Summary.DaysWithSessions > 0);
            Assert.NotEmpty(model.Trend);
            Assert.Contains(model.Trend, p => p.Seconds > 0);
            Assert.Equal(3, model.Profiles.Count);
            Assert.NotEmpty(model.RecentNotes);
            Assert.False(model.Sample);
        }
    }
}
