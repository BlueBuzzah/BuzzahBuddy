using BuzzahBuddy.Helpers;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Helpers;

public class RepeatGuardTests
{
    [Fact]
    public void TryAcquire_RejectsWithinWindow()
    {
        var clock = new ManualTimeProvider();
        var guard = new RepeatGuard(RepeatGuard.SessionControlWindow, clock);

        Assert.True(guard.TryAcquire());
        clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.False(guard.TryAcquire());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(guard.TryAcquire());
    }

    // Start swaps to Pause in the same spot; a tremor's second tap must not pause.
    [Fact]
    public void TryAcquire_SharedAcrossControls()
    {
        var clock = new ManualTimeProvider();
        var sessionControls = new RepeatGuard(RepeatGuard.SessionControlWindow, clock);

        Assert.True(sessionControls.TryAcquire());   // Start
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.False(sessionControls.TryAcquire());  // Pause, same finger
    }

    [Fact]
    public void Rejected_taps_do_not_extend_the_window()
    {
        var clock = new ManualTimeProvider();
        var guard = new RepeatGuard(RepeatGuard.SessionControlWindow, clock);

        Assert.True(guard.TryAcquire());
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(guard.TryAcquire());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(guard.TryAcquire());
    }
}
