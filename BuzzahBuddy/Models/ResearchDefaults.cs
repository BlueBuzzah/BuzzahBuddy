namespace BuzzahBuddy.Models;

/// <summary>
/// The regular vibrotactile coordinated-reset (vCR) arm of Pfeifer et al. 2021
/// (Front. Physiol. 12:624317), the same settings as firmware's built-in
/// <c>regular_vcr</c> and <c>custom_vcr</c> profiles.
/// <para>
/// The timing, pattern and frequency match the study's regular vCR: 100 ms bursts
/// at 1.5 Hz f_CR across four fingertips, no jitter, non-mirrored, random finger
/// order at 250 Hz, two hours twice daily. Amplitude is the firmware's full default,
/// not a study value: the study reported displacement in mm, which has no
/// percent equivalent. Everything inside <see cref="TherapyParameterBounds"/> is an
/// engineering envelope around these settings — a permitted value is not a studied one.
/// </para>
/// </summary>
public static class ResearchDefaults
{
    public const double TimeOnSeconds = 0.100;
    public const double TimeOffSeconds = 0.067;
    public const double Jitter = 0;
    public const int AmplitudeMin = 100;
    public const int AmplitudeMax = 100;
    public const int SessionMinutes = 120;

    /// <summary>Sessions per day in the study: 2 h in the morning plus 2 h in the afternoon or evening (Pfeifer et al. 2021).</summary>
    public const int SessionsPerDay = 2;

    public const bool Mirror = false;

    /// <summary>
    /// Jitter (%) and mirroring of the study's noisy vCR arm. Its timing and session
    /// match the regular arm, so <see cref="DeviatingFields"/> accepts either arm.
    /// </summary>
    public const double NoisyJitter = 23.5;
    public const bool NoisyMirror = true;

    /// <summary>
    /// Fingertips the study stimulated — it deliberately excluded the thumb. This is
    /// the reference for timing comparisons, NOT what a device runs: firmware sets
    /// its finger count to every motor present (5 on v3), and the user
    /// cannot change it. See <see cref="For"/>.
    /// </summary>
    public const int Fingers = 4;

    /// <summary>
    /// Builds the Custom profile with these settings for a device with
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
        Description = "Your own timing and strength",
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
    /// Names of the fields in <paramref name="current"/> that depart from the study's
    /// settings, so the editor can show the banner. An empty set means every field the
    /// study set matches its regular or noisy vCR arm; otherwise the names are the
    /// fields that depart from the regular arm.
    /// </summary>
    /// <remarks>
    /// Names match the editor's field identifiers, not protocol keys.
    /// </remarks>
    public static IReadOnlySet<string> DeviatingFields(TherapyProfile current, int motorCount)
    {
        var deviating = Deviations(current, For(motorCount));
        if (deviating.Count == 0)
            return deviating;

        var noisy = For(motorCount);
        noisy.Jitter = NoisyJitter;
        noisy.Mirror = NoisyMirror;
        return Deviations(current, noisy).Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : deviating;
    }

    private static HashSet<string> Deviations(TherapyProfile current, TherapyProfile reference)
    {
        var deviating = new HashSet<string>(StringComparer.Ordinal);

        // Compare in protocol units (ms / whole percent) so float representation of
        // the seconds values can't produce a phantom deviation.
        if (Math.Round(current.TimeOn * 1000.0, 1) != Math.Round(reference.TimeOn * 1000.0, 1))
            deviating.Add(nameof(TherapyProfile.TimeOn));
        if (Math.Round(current.TimeOff * 1000.0, 1) != Math.Round(reference.TimeOff * 1000.0, 1))
            deviating.Add(nameof(TherapyProfile.TimeOff));
        if (Math.Round(current.Jitter, 1) != Math.Round(reference.Jitter, 1))
            deviating.Add(nameof(TherapyProfile.Jitter));
        // Amplitude is deliberately not compared. The study reported displacement in mm,
        // so a firmware percent has no study equivalent and can't depart from it.
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
