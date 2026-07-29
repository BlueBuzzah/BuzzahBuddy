using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>
/// The Custom profile editor's field values, as typed. Pure and MAUI-free so the
/// parsing, unit conversion, and deviation logic can be unit tested — the view
/// model is a thin shell over this.
/// </summary>
/// <remarks>
/// Fields are strings because that is what the entries hold mid-edit, including
/// half-typed and invalid states.
/// </remarks>
public class CustomProfileForm
{
    public string TimeOnMsText { get; set; } = string.Empty;
    public string TimeOffMsText { get; set; } = string.Empty;
    public string SessionMinutesText { get; set; } = string.Empty;
    public string AmplitudeMinText { get; set; } = string.Empty;
    public string AmplitudeMaxText { get; set; } = string.Empty;
    public string JitterText { get; set; } = string.Empty;
    public bool Mirror { get; set; }

    /// <summary>
    /// Active fingers as reported by the device. Not user-editable — the count is a
    /// compile-time board constant — but it drives the derived timing and is
    /// round-tripped back to the device unchanged.
    /// </summary>
    public int Fingers { get; set; } = ResearchDefaults.Fingers;

    /// <summary>Fills the form from a profile, leaving <see cref="Fingers"/> alone.</summary>
    public void PopulateFrom(TherapyProfile profile)
    {
        var inv = CultureInfo.InvariantCulture;
        TimeOnMsText = (profile.TimeOn * 1000.0).ToString("0.#", inv);
        TimeOffMsText = (profile.TimeOff * 1000.0).ToString("0.#", inv);
        SessionMinutesText = profile.TimeSession.ToString(inv);
        AmplitudeMinText = profile.AmplitudeMin.ToString(inv);
        AmplitudeMaxText = profile.AmplitudeMax.ToString(inv);
        JitterText = profile.Jitter.ToString("0.#", inv);
        Mirror = profile.Mirror;
    }

    /// <summary>
    /// Builds a profile from the current field values, converting the entered
    /// milliseconds to the model's seconds. Returns false if any field does not
    /// parse — expected mid-edit, not an error.
    /// </summary>
    public bool TryBuildProfile(out TherapyProfile profile)
    {
        profile = new TherapyProfile();
        var inv = CultureInfo.InvariantCulture;

        if (!double.TryParse(TimeOnMsText, NumberStyles.Float, inv, out var onMs) ||
            !double.TryParse(TimeOffMsText, NumberStyles.Float, inv, out var offMs) ||
            !int.TryParse(SessionMinutesText, NumberStyles.Integer, inv, out var session) ||
            !int.TryParse(AmplitudeMinText, NumberStyles.Integer, inv, out var ampMin) ||
            !int.TryParse(AmplitudeMaxText, NumberStyles.Integer, inv, out var ampMax) ||
            !double.TryParse(JitterText, NumberStyles.Float, inv, out var jitter))
        {
            return false;
        }

        profile = new TherapyProfile
        {
            ProfileId = TherapyProfile.CustomProfileId,
            TimeOn = onMs / 1000.0,      // entries are ms; the model is seconds
            TimeOff = offMs / 1000.0,
            TimeSession = session,
            AmplitudeMin = ampMin,
            AmplitudeMax = ampMax,
            Jitter = jitter,
            Fingers = Fingers,
            Mirror = Mirror,
        };
        return true;
    }

    /// <summary>Parses the burst timing in milliseconds. False if either field is unparseable.</summary>
    public bool TryParseTiming(out double onMs, out double offMs)
    {
        var inv = CultureInfo.InvariantCulture;
        offMs = 0;
        return double.TryParse(TimeOnMsText, NumberStyles.Float, inv, out onMs)
            && double.TryParse(TimeOffMsText, NumberStyles.Float, inv, out offMs);
    }

    /// <summary>
    /// Derived coordinated-reset timing for display, e.g. "CR period 668 ms · 1.50 Hz".
    /// </summary>
    public string DerivedTimingText =>
        TryParseTiming(out var onMs, out var offMs)
            ? TherapyTiming.OnOffRatioLabel(Fingers, onMs, offMs)
            : "CR period unavailable";

    /// <summary>
    /// Warning text when the entered jitter exceeds what this burst timing can
    /// accommodate, or null when there is nothing to say.
    /// </summary>
    public string? EffectiveJitterCapText
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            if (!TryParseTiming(out var onMs, out var offMs) ||
                !double.TryParse(JitterText, NumberStyles.Float, inv, out var jitter))
                return null;

            var cap = TherapyParameterBounds.EffectiveJitterCap(onMs, offMs);
            if (jitter <= cap)
                return null;

            return $"At this timing the gloves cap jitter near {cap.ToString("0.#", inv)}% " +
                   "so bursts don't run together.";
        }
    }

    /// <summary>
    /// Fields departing from the validated research configuration. Empty while any
    /// field is unparseable — a half-typed number is not a deviation to report.
    /// </summary>
    public IReadOnlySet<string> DeviatingFields(int motorCount) =>
        TryBuildProfile(out var profile)
            ? ResearchDefaults.DeviatingFields(profile, motorCount)
            : new HashSet<string>();
}
