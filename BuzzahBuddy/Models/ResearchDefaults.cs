namespace BuzzahBuddy.Models;

/// <summary>
/// The single vibrotactile coordinated-reset parameter set validated by
/// Pfeifer et al. 2021 (Front. Physiol. 12:624317).
/// <para>
/// The study validated exactly this configuration: 100 ms bursts at 1.5 Hz f_CR
/// across four fingertips with 23.5% jitter, two hours twice daily. Everything
/// inside <see cref="TherapyParameterBounds"/> is an engineering envelope around
/// this one point — a permitted value is not a validated one.
/// </para>
/// </summary>
public static class ResearchDefaults
{
    public const double TimeOnSeconds = 0.100;
    public const double TimeOffSeconds = 0.067;
    public const double Jitter = 23.5;
    public const int AmplitudeMin = 70;
    public const int AmplitudeMax = 100;
    public const int SessionMinutes = 120;
    public const bool Mirror = false;

    /// <summary>
    /// Fingertips the study stimulated — it deliberately excluded the thumb. This is
    /// the reference for timing comparisons, NOT what a device runs: firmware sets
    /// its finger count to every motor present (5 on PentaBuzzer), and the user
    /// cannot change it. See <see cref="For"/>.
    /// </summary>
    public const int Fingers = 4;

    /// <summary>
    /// Builds the research-default Custom profile for a device with
    /// <paramref name="motorCount"/> motors per glove.
    /// </summary>
    /// <remarks>
    /// Carries <paramref name="motorCount"/> as the finger count, not the study's
    /// four, because this profile is meant to be applied to a device: firmware runs
    /// every motor present, so applying it must never change that. Use
    /// <see cref="Fingers"/> when the study's value is what's wanted.
    /// </remarks>
    public static TherapyProfile For(int motorCount) => new()
    {
        ProfileId = TherapyProfile.CustomProfileId,
        Name = "Custom",
        Description = "User-defined therapy parameters",
        TimeOn = TimeOnSeconds,
        TimeOff = TimeOffSeconds,
        Jitter = Jitter,
        AmplitudeMin = AmplitudeMin,
        AmplitudeMax = AmplitudeMax,
        TimeSession = SessionMinutes,
        Mirror = Mirror,
        Fingers = motorCount,
        PatternType = "RNDP",
        ActuatorType = "LRA",
        ActuatorFrequency = 250,
        ActuatorVoltage = 2.5,
    };

    /// <summary>
    /// Names of the fields in <paramref name="current"/> that depart from the
    /// validated configuration, so the editor can mark them. An empty set means the
    /// profile is the studied one.
    /// </summary>
    /// <remarks>
    /// Names match the editor's field identifiers, not protocol keys.
    /// </remarks>
    public static IReadOnlySet<string> DeviatingFields(TherapyProfile current, int motorCount)
    {
        var reference = For(motorCount);
        var deviating = new HashSet<string>(StringComparer.Ordinal);

        // Compare in protocol units (ms / whole percent) so float representation of
        // the seconds values can't produce a phantom deviation.
        if (Math.Round(current.TimeOn * 1000.0, 1) != Math.Round(reference.TimeOn * 1000.0, 1))
            deviating.Add(nameof(TherapyProfile.TimeOn));
        if (Math.Round(current.TimeOff * 1000.0, 1) != Math.Round(reference.TimeOff * 1000.0, 1))
            deviating.Add(nameof(TherapyProfile.TimeOff));
        if (Math.Round(current.Jitter, 1) != Math.Round(reference.Jitter, 1))
            deviating.Add(nameof(TherapyProfile.Jitter));
        if (current.AmplitudeMin != reference.AmplitudeMin)
            deviating.Add(nameof(TherapyProfile.AmplitudeMin));
        if (current.AmplitudeMax != reference.AmplitudeMax)
            deviating.Add(nameof(TherapyProfile.AmplitudeMax));
        if (current.TimeSession != reference.TimeSession)
            deviating.Add(nameof(TherapyProfile.TimeSession));
        // Fingers is deliberately not compared. It is a compile-time board constant
        // the editor doesn't expose, so on five-motor hardware it would raise a
        // permanent banner the user has no way to clear.
        if (current.Mirror != reference.Mirror)
            deviating.Add(nameof(TherapyProfile.Mirror));

        return deviating;
    }
}
