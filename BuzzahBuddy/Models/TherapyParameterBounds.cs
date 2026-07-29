namespace BuzzahBuddy.Models;

/// <summary>
/// Therapy parameter bounds for the Custom profile.
/// <para>
/// These MUST stay identical to the firmware's <c>PARAM_*</c> constants in
/// <c>include/config.h</c>. A mismatch means the app either sends values the
/// firmware rejects, or blocks values it would have accepted.
/// </para>
/// <para>
/// The bounds are engineering envelopes around the single parameter set validated
/// by Pfeifer et al. 2021 — not a validated efficacy range. See
/// <see cref="ResearchDefaults"/>.
/// </para>
/// </summary>
public static class TherapyParameterBounds
{
    public const double TimeOnMsMin = 50;    // PARAM_MIN_TIME_ON_MS
    public const double TimeOnMsMax = 200;   // PARAM_MAX_TIME_ON_MS
    public const double TimeOffMsMin = 30;   // PARAM_MIN_TIME_OFF_MS
    public const double TimeOffMsMax = 200;  // PARAM_MAX_TIME_OFF_MS

    public const double JitterMin = 0;
    public const double JitterMax = 50;      // PARAM_MAX_JITTER_PCT

    public const int AmplitudeMin = 20;      // PARAM_MIN_AMPLITUDE_PCT
    public const int AmplitudeMax = 100;

    public const int SessionMinutesMin = 1;  // PARAM_MIN_SESSION_MIN
    public const int SessionMinutesMax = 240; // PARAM_MAX_SESSION_MIN

    public const int FingersMin = 1;         // firmware: 1..MAX_ACTUATORS

    /// <summary>
    /// Smallest gap the firmware's jitter clamp will leave between bursts
    /// (<c>MIN_INTER_BURST_GAP_MS</c>). A sub-millisecond gap masks the coordinated
    /// reset as thoroughly as a zero gap, so the firmware never closes below this.
    /// </summary>
    public const double MinInterBurstGapMs = 5;

    public static bool IsTimeOnValid(double v) => v >= TimeOnMsMin && v <= TimeOnMsMax;
    public static bool IsTimeOffValid(double v) => v >= TimeOffMsMin && v <= TimeOffMsMax;
    public static bool IsJitterValid(double v) => v >= JitterMin && v <= JitterMax;
    public static bool IsAmplitudeValid(int v) => v >= AmplitudeMin && v <= AmplitudeMax;
    public static bool IsSessionValid(int v) => v >= SessionMinutesMin && v <= SessionMinutesMax;

    /// <summary>
    /// Whether the requested finger count is valid for a device with
    /// <paramref name="motorCount"/> motors per glove (4 on v2, 5 on v3).
    /// </summary>
    public static bool IsFingersValid(int v, int motorCount) => v >= FingersMin && v <= motorCount;

    /// <summary>
    /// The largest jitter percentage that will actually be honoured at the given
    /// burst timing. The firmware clamps the jitter excursion so the inter-burst
    /// gap never falls below <see cref="MinInterBurstGapMs"/>; above this cap,
    /// raising Jitter changes nothing.
    /// </summary>
    /// <remarks>
    /// Firmware: <c>jitterAmount = (on + off) * (jitter/100) / 2</c>, clamped to
    /// <c>off - MinInterBurstGapMs</c>. Solving for jitter gives the cap below.
    /// </remarks>
    public static double EffectiveJitterCap(double onMs, double offMs)
    {
        var cycleMs = onMs + offMs;
        if (cycleMs <= 0)
            return 0;

        var headroomMs = Math.Max(0, offMs - MinInterBurstGapMs);
        return Math.Min(200.0 * headroomMs / cycleMs, JitterMax);
    }
}
