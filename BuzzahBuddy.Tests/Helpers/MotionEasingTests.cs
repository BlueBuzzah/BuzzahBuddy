using BuzzahBuddy.Helpers;
using Xunit;

namespace BuzzahBuddy.Tests.Helpers;

public class MotionEasingTests
{
    [Fact]
    public void Standard_Endpoints()
    {
        Assert.Equal(0, CubicBezier.Standard(0), 6);
        Assert.Equal(1, CubicBezier.Standard(1), 6);
    }

    [Fact]
    public void Standard_Monotonic()
    {
        var previous = 0.0;
        for (var x = 0.05; x <= 1.0001; x += 0.05)
        {
            var y = CubicBezier.Standard(x);
            Assert.True(y >= previous, $"ease({x:F2}) = {y} < {previous}");
            previous = y;
        }
    }

    // cubic-bezier(0.2, 0, 0, 1) is front-loaded: well past halfway at the midpoint.
    [Fact]
    public void Standard_IsEaseOut() => Assert.InRange(CubicBezier.Standard(0.5), 0.8, 0.95);
}
