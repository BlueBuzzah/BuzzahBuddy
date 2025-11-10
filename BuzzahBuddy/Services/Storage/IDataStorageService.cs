using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Storage;

/// <summary>
/// Service interface for storing and retrieving application data.
/// </summary>
public interface IDataStorageService
{
    /// <summary>
    /// Saves a therapy session to storage.
    /// </summary>
    /// <param name="session">The session to save.</param>
    Task SaveSessionAsync(TherapySession session);

    /// <summary>
    /// Retrieves the history of therapy sessions.
    /// </summary>
    /// <param name="limit">Maximum number of sessions to retrieve (0 for all).</param>
    /// <returns>A collection of therapy sessions, ordered by start time (most recent first).</returns>
    Task<IEnumerable<TherapySession>> GetSessionHistoryAsync(int limit = 0);

    /// <summary>
    /// Gets a specific therapy session by ID.
    /// </summary>
    /// <param name="sessionId">The ID of the session to retrieve.</param>
    /// <returns>The session if found, null otherwise.</returns>
    Task<TherapySession?> GetSessionByIdAsync(string sessionId);

    /// <summary>
    /// Deletes a therapy session from storage.
    /// </summary>
    /// <param name="sessionId">The ID of the session to delete.</param>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Saves the last used therapy profile ID.
    /// </summary>
    /// <param name="profileId">The profile ID (1-3 for presets, 0 for custom).</param>
    Task SaveLastProfileAsync(int profileId);

    /// <summary>
    /// Gets the last used therapy profile ID.
    /// </summary>
    /// <returns>The last used profile ID, or 2 (Noisy VCR) as default.</returns>
    Task<int> GetLastProfileAsync();

    /// <summary>
    /// Saves the last connected device information.
    /// </summary>
    /// <param name="device">The device to save.</param>
    Task SaveLastDeviceAsync(GloveDevice device);

    /// <summary>
    /// Gets the last connected device information.
    /// </summary>
    /// <returns>The last connected device, or null if none saved.</returns>
    Task<GloveDevice?> GetLastDeviceAsync();

    /// <summary>
    /// Clears all stored data (sessions, patterns, device info).
    /// </summary>
    Task ClearAllDataAsync();
}
