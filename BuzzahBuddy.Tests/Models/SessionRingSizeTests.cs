using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>The session ring's size: by window width in one column, by the smaller side in its own pane.</summary>
public class SessionRingSizeTests
{
    [Fact] public void Small_phone() { Assert.Equal(240, SessionRingSize.For(320, null)); Assert.Equal(240, SessionRingSize.For(359, null)); }
    [Fact] public void Phone() { Assert.Equal(280, SessionRingSize.For(375, null)); Assert.Equal(280, SessionRingSize.For(599, null)); }
    [Fact] public void Wide_single_column() { Assert.Equal(360, SessionRingSize.For(600, null)); Assert.Equal(360, SessionRingSize.For(1032, null)); }
    [Fact] public void Own_pane_grows_with_the_smaller_side() => Assert.Equal(420, SessionRingSize.For(640, 468));
    [Fact] public void Own_pane_floor_and_cap() { Assert.Equal(360, SessionRingSize.For(380, 380)); Assert.Equal(480, SessionRingSize.For(700, 900)); }
}
