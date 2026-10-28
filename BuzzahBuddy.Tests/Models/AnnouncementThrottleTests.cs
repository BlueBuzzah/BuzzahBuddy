using BuzzahBuddy.Models;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>The session timer speaks at most once a minute, but always on a state change.</summary>
public class AnnouncementThrottleTests
{
    [Fact]
    public void Updates_once_a_minute_and_on_state_change()
    {
        var clock = new ManualTimeProvider();
        var throttle = new AnnouncementThrottle(TimeSpan.FromMinutes(1), clock);

        Assert.True(throttle.ShouldUpdate("Session running, 10 minutes left", SessionRingState.Running));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(throttle.ShouldUpdate("Session running, 9 minutes left", SessionRingState.Running));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(throttle.ShouldUpdate("Session running, 9 minutes left", SessionRingState.Running));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(throttle.ShouldUpdate("Session paused, 9 minutes left", SessionRingState.Paused));
    }

    [Fact]
    public void Same_text_never_updates()
    {
        var clock = new ManualTimeProvider();
        var throttle = new AnnouncementThrottle(TimeSpan.FromMinutes(1), clock);

        Assert.True(throttle.ShouldUpdate("Session ready, 2 hours left", SessionRingState.Idle));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(throttle.ShouldUpdate("Session ready, 2 hours left", SessionRingState.Idle));
    }
}
