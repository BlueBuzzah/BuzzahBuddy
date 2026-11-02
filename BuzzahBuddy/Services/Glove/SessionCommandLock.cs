namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Serializes session commands (Start, Pause, Resume, Stop) so a voice command and an on-screen
/// tap never send at once. Whoever waited re-reads the session state once it holds the lock.
/// </summary>
public sealed class SessionCommandLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for any in-flight session command; dispose to release.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
