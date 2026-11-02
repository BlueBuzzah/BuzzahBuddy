using BuzzahBuddy.Services.Glove;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class SessionCommandLockTests
{
    [Fact]
    public async Task SessionCommandLock_SecondWaiterRunsAfterFirstReleases()
    {
        var sessionLock = new SessionCommandLock();
        var first = await sessionLock.AcquireAsync();

        var second = sessionLock.AcquireAsync();
        Assert.False(second.IsCompleted);

        first.Dispose();

        using var held = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SessionCommandLock_CancelledWaiterThrows()
    {
        var sessionLock = new SessionCommandLock();
        using var first = await sessionLock.AcquireAsync();
        using var cts = new CancellationTokenSource();

        var second = sessionLock.AcquireAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }
}
