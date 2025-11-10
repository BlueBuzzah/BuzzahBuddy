using BuzzahBuddy.Models;
using System.Text.Json;

namespace BuzzahBuddy.Services.Storage;

/// <summary>
/// Simple storage service using MAUI Preferences API for persisting application data.
/// </summary>
public class PreferencesStorageService : IDataStorageService
{
    private const string SessionsKey = "therapy_sessions";
    private const string LastProfileKey = "last_profile_id";
    private const string LastDeviceKey = "last_device";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveSessionAsync(TherapySession session)
    {
        var sessions = (await GetSessionHistoryAsync()).ToList();

        // Update existing or add new
        var existing = sessions.FirstOrDefault(s => s.Id == session.Id);
        if (existing != null)
        {
            sessions.Remove(existing);
        }

        sessions.Add(session);

        // Keep only last 100 sessions
        if (sessions.Count > 100)
        {
            sessions = sessions.OrderByDescending(s => s.StartTime).Take(100).ToList();
        }

        var json = JsonSerializer.Serialize(sessions, _jsonOptions);
        Preferences.Default.Set(SessionsKey, json);

        await Task.CompletedTask;
    }

    public async Task<IEnumerable<TherapySession>> GetSessionHistoryAsync(int limit = 0)
    {
        var json = Preferences.Default.Get(SessionsKey, string.Empty);

        if (string.IsNullOrEmpty(json))
        {
            return Enumerable.Empty<TherapySession>();
        }

        try
        {
            var sessions = JsonSerializer.Deserialize<List<TherapySession>>(json, _jsonOptions)
                ?? new List<TherapySession>();

            var ordered = sessions.OrderByDescending(s => s.StartTime);

            if (limit > 0)
            {
                return await Task.FromResult(ordered.Take(limit).ToList());
            }

            return await Task.FromResult(ordered.ToList());
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deserialize sessions error: {ex.Message}");
            return Enumerable.Empty<TherapySession>();
        }
    }

    public async Task<TherapySession?> GetSessionByIdAsync(string sessionId)
    {
        var sessions = await GetSessionHistoryAsync();
        return sessions.FirstOrDefault(s => s.Id == sessionId);
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        var sessions = (await GetSessionHistoryAsync()).ToList();
        var toRemove = sessions.FirstOrDefault(s => s.Id == sessionId);

        if (toRemove != null)
        {
            sessions.Remove(toRemove);
            var json = JsonSerializer.Serialize(sessions, _jsonOptions);
            Preferences.Default.Set(SessionsKey, json);
        }
    }

    public async Task SaveLastProfileAsync(int profileId)
    {
        Preferences.Default.Set(LastProfileKey, profileId);
        await Task.CompletedTask;
    }

    public async Task<int> GetLastProfileAsync()
    {
        // Default to profile 2 (Noisy VCR) if not set
        var profileId = Preferences.Default.Get(LastProfileKey, 2);
        return await Task.FromResult(profileId);
    }

    public async Task SaveLastDeviceAsync(GloveDevice device)
    {
        var json = JsonSerializer.Serialize(device, _jsonOptions);
        Preferences.Default.Set(LastDeviceKey, json);
        await Task.CompletedTask;
    }

    public async Task<GloveDevice?> GetLastDeviceAsync()
    {
        var json = Preferences.Default.Get(LastDeviceKey, string.Empty);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return await Task.FromResult(
                JsonSerializer.Deserialize<GloveDevice>(json, _jsonOptions));
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deserialize device error: {ex.Message}");
            return null;
        }
    }

    public async Task ClearAllDataAsync()
    {
        Preferences.Default.Remove(SessionsKey);
        Preferences.Default.Remove(LastProfileKey);
        Preferences.Default.Remove(LastDeviceKey);
        await Task.CompletedTask;
    }
}
