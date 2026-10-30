using System.Text.Json;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Storage;

/// <summary>
/// The on-disk shape of the session history.
/// </summary>
public sealed class HistoryData
{
    public int Version { get; set; } = 1;
    public List<SessionRecord> Sessions { get; set; } = [];
    public List<DayNote> Notes { get; set; } = [];
}

/// <summary>
/// Keeps session history and day notes in one JSON file. Holds no MAUI types so it compiles
/// into the test project.
/// </summary>
// ponytail: whole file rewritten per save (~200 KB/year at the studied schedule); move to SQLite if it passes ~1 MB.
public sealed class HistoryStore
{
    public const string LegacyPreferencesKey = "therapy_sessions";
    public const int MaxNoteLength = 1000;

    private readonly string _filePath;
    private readonly Func<string?>? _readLegacy;
    private readonly Action? _clearLegacy;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HistoryData? _data;

    public HistoryStore(string filePath, Func<string?>? readLegacy = null, Action? clearLegacy = null)
    {
        _filePath = filePath;
        _readLegacy = readLegacy;
        _clearLegacy = clearLegacy;
    }

    /// <summary>
    /// A copy of the history, sessions newest first.
    /// </summary>
    public async Task<HistoryData> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var data = EnsureLoaded();
            return new HistoryData
            {
                Version = data.Version,
                Sessions = data.Sessions.OrderByDescending(s => s.StartedAt).ToList(),
                Notes = [.. data.Notes]
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddSessionAsync(SessionRecord record)
    {
        await _gate.WaitAsync();
        try
        {
            var data = EnsureLoaded();
            data.Sessions.RemoveAll(s => s.Id == record.Id);
            data.Sessions.Add(record);
            Save(data);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetDayNoteAsync(DateOnly day, string? text)
    {
        await _gate.WaitAsync();
        try
        {
            var data = EnsureLoaded();
            data.Notes.RemoveAll(n => n.Day == day);
            var trimmed = text?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                data.Notes.Add(new DayNote { Day = day, Text = Truncate(trimmed) });
            }
            Save(data);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendDayNoteAsync(DateOnly day, string text)
    {
        await _gate.WaitAsync();
        try
        {
            var data = EnsureLoaded();
            AppendNote(data, day, text);
            Save(data);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            _data = new HistoryData();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Truncate(string text) =>
        text.Length > MaxNoteLength ? text[..MaxNoteLength] : text;

    private static void AppendNote(HistoryData data, DateOnly day, string text)
    {
        var existing = data.Notes.FirstOrDefault(n => n.Day == day);
        if (existing is null)
        {
            data.Notes.Add(new DayNote { Day = day, Text = Truncate(text) });
        }
        else
        {
            existing.Text = Truncate(existing.Text + "\n" + text);
        }
    }

    // Callers hold _gate.
    private HistoryData EnsureLoaded()
    {
        if (_data is not null)
        {
            return _data;
        }

        _data = ReadFile();
        MigrateLegacy(_data);
        return _data;
    }

    private HistoryData ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            return new HistoryData();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, StorageJsonContext.Default.HistoryData)
                ?? throw new JsonException("History file is empty.");
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HISTORY] Corrupt history file: {ex.Message}");
            KeepAside();
            return new HistoryData();
        }
    }

    // Throws if the file cannot be moved: continuing with an empty store would let the next
    // save overwrite the corrupt file.
    private void KeepAside()
    {
        var stamp = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        for (var n = 0; ; n++)
        {
            var aside = n == 0 ? stamp : $"{stamp}-{n}";
            if (File.Exists(aside))
            {
                continue;
            }

            File.Move(_filePath, aside, overwrite: false);
            return;
        }
    }

    private void MigrateLegacy(HistoryData data)
    {
        string? json;
        try
        {
            json = _readLegacy?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HISTORY] Could not read legacy sessions: {ex.Message}");
            return;
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        List<TherapySession>? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize(json, StorageJsonContext.Default.ListTherapySession);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HISTORY] Legacy sessions unparseable, left in place: {ex.Message}");
            return;
        }

        if (legacy is null)
        {
            return;
        }

        foreach (var old in legacy)
        {
            var startedAt = new DateTimeOffset(old.StartTime);
            if (data.Sessions.All(s => s.Id != old.Id))
            {
                data.Sessions.Add(new SessionRecord
                {
                    Id = old.Id,
                    StartedAt = startedAt,
                    EndedAt = old.EndTime is { } end
                        ? new DateTimeOffset(end)
                        : startedAt.AddSeconds(old.ElapsedTimeSeconds),
                    TherapySeconds = old.ElapsedTimeSeconds,
                    PlannedSeconds = (old.ProfileUsed?.TimeSession ?? 0) * 60,
                    ProfileId = old.ProfileId,
                    ProfileName = old.ProfileUsed?.Name ?? "",
                    Outcome = old.IsCompleted ? SessionOutcome.Completed : SessionOutcome.EndedEarly,
                    DeviceId = old.DeviceId
                });
            }

            var note = old.Notes?.Trim();
            if (!string.IsNullOrEmpty(note))
            {
                var day = DateOnly.FromDateTime(old.StartTime);
                var existing = data.Notes.FirstOrDefault(n => n.Day == day);
                if (existing is null || !existing.Text.Contains(note))
                {
                    AppendNote(data, day, note);
                }
            }
        }

        try
        {
            Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[HISTORY] Could not save migrated history, legacy kept: {ex.Message}");
            return;
        }

        _clearLegacy?.Invoke();
    }

    private void Save(HistoryData data)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, StorageJsonContext.Default.HistoryData));
        File.Move(tmp, _filePath, overwrite: true);
    }
}
