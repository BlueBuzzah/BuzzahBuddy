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
    /// <c>"CR period 668 ms · 1.50 Hz"</c>.
    /// </summary>
    public static string OnOffRatioLabel(int fingers, double onMs, double offMs)
    {
        var periodMs = CrPeriodMs(fingers, onMs, offMs);
        if (periodMs <= 0)
            return "CR period unavailable";

        var inv = CultureInfo.InvariantCulture;
        return $"CR period {periodMs.ToString("0", inv)} ms · " +
               $"{CrFrequencyHz(fingers, onMs, offMs).ToString("0.00", inv)} Hz";
    }
}
