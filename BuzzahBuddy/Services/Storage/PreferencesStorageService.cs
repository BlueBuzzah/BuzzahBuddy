using BuzzahBuddy.Models;
using System.Text.Json;

namespace BuzzahBuddy.Services.Storage;

/// <summary>
/// Simple storage service using MAUI Preferences API for persisting application data.
/// </summary>
public class PreferencesStorageService : IDataStorageService
{
    private const string LastProfileKey = "last_profile_id";
    private const string LastDeviceKey = "last_device";
    private const string CustomProfileKey = "custom_profile";
    private const string OpenSessionKey = "open_session";

    // Source-generated, not reflection-based: see StorageJsonContext for why this is
    // mandatory rather than an optimization (iOS Release AOT cannot JIT the invoke
    // wrappers STJ's reflection accessor needs).
    private static readonly StorageJsonContext Json = StorageJsonContext.Default;

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

    public Task SaveCustomProfileAsync(TherapyProfile profile)
    {
        Preferences.Default.Set(CustomProfileKey, JsonSerializer.Serialize(profile, Json.TherapyProfile));
        return Task.CompletedTask;
    }

    public Task<TherapyProfile?> GetCustomProfileAsync()
    {
        var json = Preferences.Default.Get(CustomProfileKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return Task.FromResult<TherapyProfile?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize(json, Json.TherapyProfile));
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deserialize custom profile error: {ex.Message}");
            return Task.FromResult<TherapyProfile?>(null);
        }
    }

    public async Task SaveLastDeviceAsync(GloveDevice device)
    {
        var json = JsonSerializer.Serialize(device, Json.GloveDevice);
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
                JsonSerializer.Deserialize(json, Json.GloveDevice));
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deserialize device error: {ex.Message}");
            return null;
        }
    }

    public void SaveOpenSession(OpenSession? session)
    {
        if (session is null)
        {
            Preferences.Default.Remove(OpenSessionKey);
            return;
        }

        Preferences.Default.Set(OpenSessionKey, JsonSerializer.Serialize(session, Json.OpenSession));
    }

    public OpenSession? LoadOpenSession()
    {
        var json = Preferences.Default.Get(OpenSessionKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, Json.OpenSession);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deserialize open session error: {ex.Message}");
            return null;
        }
    }

    public async Task ClearAllDataAsync()
    {
        Preferences.Default.Remove(LastProfileKey);
        Preferences.Default.Remove(LastDeviceKey);
        Preferences.Default.Remove(CustomProfileKey);
        Preferences.Default.Remove(OpenSessionKey);
        await Task.CompletedTask;
    }
}
