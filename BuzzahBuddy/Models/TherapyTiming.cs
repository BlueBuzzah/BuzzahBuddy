using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>
/// Coordinated-reset timing arithmetic, mirroring the firmware's therapy engine.
/// One CR period stimulates every active fingertip exactly once.
/// </summary>
public static class TherapyTiming
{
    /// <summary>
    /// Duration of one coordinated-reset period in milliseconds:
    /// every active fingertip gets one burst plus its following gap.
    /// </summary>
    public static double CrPeriodMs(int fingers, double onMs, double offMs) =>
        fingers * (onMs + offMs);

    /// <summary>
    /// Coordinated-reset frequency (f_CR) in Hz. The Pfeifer et al. protocol runs
    /// at 1.5 Hz. Returns 0 when the timing is degenerate.
    /// </summary>
    public static double CrFrequencyHz(int fingers, double onMs, double offMs)
    {
        var periodMs = CrPeriodMs(fingers, onMs, offMs);
        return periodMs <= 0 ? 0 : 1000.0 / periodMs;
    }

    /// <summary>
    /// Human-readable summary of the derived timing, e.g.
    /// <c>"One full cycle takes 668 ms, about 1.5 times a second."</c>
    /// </summary>
    public static string CrTimingLabel(int fingers, double onMs, double offMs)
    {
        var periodMs = CrPeriodMs(fingers, onMs, offMs);
        if (periodMs <= 0)
            return "Enter both times above to see the cycle length.";

        var inv = CultureInfo.InvariantCulture;
        return $"One full cycle takes {periodMs.ToString("0", inv)} ms, " +
               $"about {CrFrequencyHz(fingers, onMs, offMs).ToString("0.#", inv)} times a second.";
    }
}
