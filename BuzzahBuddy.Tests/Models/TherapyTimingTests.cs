using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class TherapyTimingTests
{
    [Fact]
    public void CrPeriodMs_CountsOneBurstPlusGapPerFinger()
        => Assert.Equal(668, TherapyTiming.CrPeriodMs(4, 100, 67));

    [Fact]
    public void CrFrequencyHz_AtResearchDefaults_IsAboutOnePointFive()
        => Assert.Equal(1.5, TherapyTiming.CrFrequencyHz(4, 100, 67), precision: 2);

    [Fact]
    public void CrFrequencyHz_WithFiveFingers_SlowsDown()
    {
        // Enabling the v3 thumb lengthens the CR period, so f_CR drops.
        Assert.True(TherapyTiming.CrFrequencyHz(5, 100, 67)
                  < TherapyTiming.CrFrequencyHz(4, 100, 67));
    }

    [Fact]
    public void CrFrequencyHz_WithDegenerateTiming_IsZero()
        => Assert.Equal(0, TherapyTiming.CrFrequencyHz(4, 0, 0));

    [Fact]
    public void OnOffRatioLabel_ReportsPeriodAndFrequency()
        => Assert.Equal("CR period 668 ms · 1.50 Hz", TherapyTiming.OnOffRatioLabel(4, 100, 67));

    [Fact]
    public void OnOffRatioLabel_WithDegenerateTiming_SaysUnavailable()
        => Assert.Equal("CR period unavailable", TherapyTiming.OnOffRatioLabel(0, 100, 67));
}
