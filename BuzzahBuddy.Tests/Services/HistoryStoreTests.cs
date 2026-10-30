using System.Text.Json;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Storage;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static SessionRecord Record(string id, DateTimeOffset start, int seconds, SessionOutcome outcome) => new()
    {
        Id = id,
        StartedAt = start,
        EndedAt = start.AddSeconds(seconds),
        TherapySeconds = seconds,
        PlannedSeconds = 7200,
        ProfileId = 2,
        ProfileName = "Noisy",
        Outcome = outcome
    };

    private static readonly DateTimeOffset Start = new(2026, 10, 27, 8, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public async Task AddSession_PersistsAcrossInstances()
    {
        await new HistoryStore(FilePath).AddSessionAsync(Record("a", Start, 600, SessionOutcome.StoppedEarly));

        var data = await new HistoryStore(FilePath).LoadAsync();

        var s = Assert.Single(data.Sessions);
        Assert.Equal("a", s.Id);
        Assert.Equal(600, s.TherapySeconds);
        Assert.Equal(SessionOutcome.StoppedEarly, s.Outcome);
        Assert.Equal(TimeSpan.FromHours(-7), s.StartedAt.Offset);
    }

    [Fact]
    public async Task AddSession_SameId_Replaces()
    {
        var store = new HistoryStore(FilePath);
        await store.AddSessionAsync(Record("a", Start, 600, SessionOutcome.StoppedEarly));
        await store.AddSessionAsync(Record("a", Start, 900, SessionOutcome.StoppedEarly));

        var s = Assert.Single((await store.LoadAsync()).Sessions);
        Assert.Equal(900, s.TherapySeconds);
    }

    [Fact]
    public async Task SetDayNote_WhitespaceRemoves()
    {
        var store = new HistoryStore(FilePath);
        var day = new DateOnly(2026, 10, 27);
        await store.SetDayNoteAsync(day, "Felt steadier");
        await store.SetDayNoteAsync(day, "  ");

        Assert.Empty((await store.LoadAsync()).Notes);
    }

    [Fact]
    public async Task AppendDayNote_AddsLine()
    {
        var store = new HistoryStore(FilePath);
        var day = new DateOnly(2026, 10, 27);
        await store.AppendDayNoteAsync(day, "A");
        await store.AppendDayNoteAsync(day, "B");

        Assert.Equal("A\nB", Assert.Single((await store.LoadAsync()).Notes).Text);
    }

    [Fact]
    public async Task AppendDayNote_CapsAtMaxLength()
    {
        var store = new HistoryStore(FilePath);
        var day = new DateOnly(2026, 10, 27);
        await store.AppendDayNoteAsync(day, new string('x', 900));
        await store.AppendDayNoteAsync(day, new string('y', 200));

        Assert.Equal(1000, Assert.Single((await store.LoadAsync()).Notes).Text.Length);
    }

    [Fact]
    public async Task Load_CorruptFile_KeepsItAsideAndStartsEmpty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(FilePath, "{not json");
        var store = new HistoryStore(FilePath);

        Assert.Empty((await store.LoadAsync()).Sessions);
        var aside = Assert.Single(Directory.GetFiles(_dir, "history.json.corrupt-*"));
        Assert.Equal("{not json", await File.ReadAllTextAsync(aside));

        await store.AddSessionAsync(Record("a", Start, 600, SessionOutcome.Completed));

        Assert.Equal("{not json", await File.ReadAllTextAsync(aside));
    }

    [Fact]
    public async Task Load_CorruptFile_TwiceInOneSecond_KeepsBoth()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(FilePath, "{one");
        await new HistoryStore(FilePath).LoadAsync();
        await File.WriteAllTextAsync(FilePath, "{two");
        await new HistoryStore(FilePath).LoadAsync();

        var texts = Directory.GetFiles(_dir, "history.json.corrupt-*").Select(File.ReadAllText).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "{one", "{two" }, texts);
    }

    private static string LegacyJson()
    {
        var day = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Local);
        var list = new List<TherapySession>
        {
            new()
            {
                Id = "L1", StartTime = day, ElapsedTimeSeconds = 7200, IsCompleted = true,
                ProfileUsed = new TherapyProfile { Name = "Noisy", TimeSession = 120 },
                Notes = "Felt steadier"
            },
            new() { Id = "L2", StartTime = day.AddHours(3), ElapsedTimeSeconds = 1800, IsCompleted = false }
        };
        return JsonSerializer.Serialize(list, StorageJsonContext.Default.ListTherapySession);
    }

    [Fact]
    public async Task Legacy_ImportsOnceThenClears()
    {
        var cleared = 0;
        var store = new HistoryStore(FilePath, LegacyJson, () => cleared++);

        var data = await store.LoadAsync();

        var l1 = data.Sessions.Single(s => s.Id == "L1");
        Assert.Equal(SessionOutcome.Completed, l1.Outcome);
        Assert.Equal(7200, l1.TherapySeconds);
        Assert.Equal("Noisy", l1.ProfileName);
        Assert.Equal(7200, l1.PlannedSeconds);
        var l2 = data.Sessions.Single(s => s.Id == "L2");
        Assert.Equal(SessionOutcome.EndedEarly, l2.Outcome);
        Assert.Equal(1800, l2.TherapySeconds);
        var note = Assert.Single(data.Notes);
        Assert.Equal(new DateOnly(2026, 10, 27), note.Day);
        Assert.Equal("Felt steadier", note.Text);
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task Legacy_ImportedTwice_DoesNotDuplicate()
    {
        await new HistoryStore(FilePath, LegacyJson, () => { }).LoadAsync();

        var data = await new HistoryStore(FilePath, LegacyJson, () => { }).LoadAsync();

        Assert.Equal(2, data.Sessions.Count);
        Assert.Equal("Felt steadier", Assert.Single(data.Notes).Text);
    }

    [Fact]
    public async Task Legacy_InvalidJson_LeavesLegacyInPlace()
    {
        var cleared = 0;
        var store = new HistoryStore(FilePath, () => "garbage", () => cleared++);

        var data = await store.LoadAsync();

        Assert.Empty(data.Sessions);
        Assert.Equal(0, cleared);
    }

    [Fact]
    public async Task Clear_RemovesFile()
    {
        var store = new HistoryStore(FilePath);
        await store.AddSessionAsync(Record("a", Start, 600, SessionOutcome.Completed));
        Assert.True(File.Exists(FilePath));

        await store.ClearAsync();

        Assert.False(File.Exists(FilePath));
        Assert.Empty((await store.LoadAsync()).Sessions);
    }

    [Fact]
    public async Task HistoryData_WritesOutcomeAsText()
    {
        await new HistoryStore(FilePath).AddSessionAsync(Record("a", Start, 600, SessionOutcome.StoppedEarly));

        Assert.Contains("\"Outcome\": \"StoppedEarly\"", await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public void OpenSession_RoundTripsThroughStorageContext()
    {
        var open = new OpenSession
        {
            Id = "o1", StartedAt = Start, ProfileId = 2, ProfileName = "Noisy", DeviceId = "dev",
            PlannedSeconds = 7200, TherapySeconds = 300, LastElapsedSeconds = 320, LastRunning = true,
            LastObservedAt = Start.AddSeconds(320)
        };

        var back = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(open, StorageJsonContext.Default.OpenSession),
            StorageJsonContext.Default.OpenSession)!;

        Assert.Equal(open.Id, back.Id);
        Assert.Equal(open.StartedAt, back.StartedAt);
        Assert.Equal(open.ProfileId, back.ProfileId);
        Assert.Equal(open.ProfileName, back.ProfileName);
        Assert.Equal(open.DeviceId, back.DeviceId);
        Assert.Equal(open.PlannedSeconds, back.PlannedSeconds);
        Assert.Equal(open.TherapySeconds, back.TherapySeconds);
        Assert.Equal(open.LastElapsedSeconds, back.LastElapsedSeconds);
        Assert.Equal(open.LastRunning, back.LastRunning);
        Assert.Equal(open.LastObservedAt, back.LastObservedAt);
        Assert.Equal(TimeSpan.FromHours(-7), back.LastObservedAt.Offset);
    }
}
