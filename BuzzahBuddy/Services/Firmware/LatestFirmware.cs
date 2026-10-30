using System.Text.Json;

namespace BuzzahBuddy.Services.Firmware;

/// <summary>
/// The newest published firmware release tag ("v2.1.1") from GitHub, cached for a day in
/// Preferences. The app's only network call. Null when it was never fetched; callers then
/// show nothing, since a missed nag is better than a wrong one.
/// </summary>
public static class LatestFirmware
{
    // releases/latest skips drafts and pre-releases.
    // ponytail: assumes every release ships both boards (v2 and v3 zips). A one-board release
    // would nag the other board's owners; check the assets against the glove's board if that happens.
    private const string Url = "https://api.github.com/repos/BlueBuzzah/BlueBuzzah-Firmware/releases/latest";
    public const string UpdaterUrl = "https://bluebuzzah.com/index.html#download";
    private const string TagKey = "latest_firmware_tag";
    private const string CheckedKey = "latest_firmware_checked";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();

    public static async Task<string?> GetTagAsync()
    {
        var cached = Preferences.Default.Get<string?>(TagKey, null);
        if (cached is not null && DateTime.UtcNow - Preferences.Default.Get(CheckedKey, DateTime.MinValue) < MaxAge)
            return cached;

        try
        {
            await using var stream = await Http.GetStreamAsync(Url);
            // JsonDocument, not a typed model: no serializer context needed under AOT.
            using var json = await JsonDocument.ParseAsync(stream);
            if (json.RootElement.GetProperty("tag_name").GetString() is not { Length: > 0 } tag)
                return cached;
            Preferences.Default.Set(TagKey, tag);
            Preferences.Default.Set(CheckedKey, DateTime.UtcNow);
            return tag;
        }
        catch (Exception ex)
        {
            // Offline or rate-limited: the last tag we saw still holds; retry on the next connect.
            System.Diagnostics.Debug.WriteLine($"[FIRMWARE] Latest release check failed: {ex.Message}");
            return cached;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BuzzahBuddy"); // GitHub rejects requests without one
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
