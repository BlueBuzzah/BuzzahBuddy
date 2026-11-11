namespace BuzzahBuddy.Models;

/// <summary>
/// One finger's boot motor check, from INFO's MOTP (primary) and MOTS (secondary) keys.
/// Mirrors firmware include/motor_check.h: OK, OPEN (no motor seen), NODRIVER (driver
/// init failed), UNKNOWN (probe timeout, bus busy or a supply dip).
/// </summary>
public enum MotorCheck { Ok, Open, NoDriver, Unknown }

/// <summary>
/// Parses the per-finger motor checks and words the warnings about them. A check is only a
/// warning: sessions always play index to little, and the thumb only when the primary found one.
/// </summary>
public static class MotorChecks
{
    // Index to little play in every session; the thumb (finger 4) is optional.
    private const int RequiredFingers = 4;
    private const int Thumb = 4;

    /// <summary>
    /// Comma-separated tokens in finger order (index, middle, ring, little, thumb). Null when
    /// the key is missing or empty (v2, or firmware before the check); an unknown token is
    /// <see cref="MotorCheck.Unknown"/>.
    /// </summary>
    public static IReadOnlyList<MotorCheck>? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Split(',').Select(token => token.Trim() switch
        {
            "OK" => MotorCheck.Ok,
            "OPEN" => MotorCheck.Open,
            "NODRIVER" => MotorCheck.NoDriver,
            _ => MotorCheck.Unknown,
        }).ToArray();
    }

    /// <summary>Index to little fingers that didn't check OK. The thumb is never one.</summary>
    public static IReadOnlyList<int> ProblemFingers(IReadOnlyList<MotorCheck>? checks) =>
        checks is null ? []
        : Enumerable.Range(0, Math.Min(RequiredFingers, checks.Count)).Where(f => checks[f] != MotorCheck.Ok).ToArray();

    /// <summary>
    /// Whether Test Motors marks a finger: index to little when not OK, the thumb only on a
    /// driver fault (an empty thumb port is normal). Null means no report.
    /// </summary>
    public static bool IsMarked(int finger, MotorCheck? check) =>
        check is { } c && (finger < RequiredFingers ? c != MotorCheck.Ok : finger == Thumb && c == MotorCheck.NoDriver);

    /// <summary>The fingers <see cref="IsMarked"/> marks, in finger order.</summary>
    public static IReadOnlyList<int> MarkedFingers(IReadOnlyList<MotorCheck>? checks) =>
        checks is null ? [] : Enumerable.Range(0, checks.Count).Where(f => IsMarked(f, checks[f])).ToArray();

    /// <summary>What a marked finger button adds for screen readers; null when it isn't marked.</summary>
    public static string? AccessibleSuffix(int finger, MotorCheck? check) =>
        !IsMarked(finger, check) ? null
        : check == MotorCheck.NoDriver ? ", driver fault"
        : ", didn't respond at startup";

    /// <summary>Lowercase finger names: "ring", "ring and little", "middle, ring and little".</summary>
    public static string JoinFingers(IReadOnlyList<int> fingers)
    {
        var names = fingers.Select(f => CalibrationSettings.GetFingerName(f).ToLowerInvariant()).ToList();
        return names.Count <= 1
            ? string.Concat(names)
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }

    /// <summary>
    /// The Gloves banner message naming each glove's problem fingers; null when neither glove
    /// has one. "Your primary glove's ring and little motors didn't respond when it turned on.
    /// Sessions still run. Test them to check."
    /// </summary>
    public static string? BannerMessage(IReadOnlyList<MotorCheck>? primary, IReadOnlyList<MotorCheck>? secondary)
    {
        var p = ProblemFingers(primary);
        var s = ProblemFingers(secondary);
        if (p.Count == 0 && s.Count == 0)
            return null;

        string Part(string glove, IReadOnlyList<int> fingers) =>
            $"{glove} glove's {JoinFingers(fingers)} {Motors(fingers)}";
        var both = p.Count > 0 && s.Count > 0;
        var subject = both ? $"{Part("primary", p)} and your {Part("secondary", s)}"
            : p.Count > 0 ? Part("primary", p)
            : Part("secondary", s);
        var them = p.Count + s.Count > 1 ? "them" : "it";
        return $"Your {subject} didn't respond when {(both ? "they" : "it")} turned on. Sessions still run. Test {them} to check.";
    }

    /// <summary>
    /// The line under a glove's heading in Test Motors, naming the fingers its buttons mark;
    /// null when none are marked.
    /// </summary>
    public static string? GloveLine(IReadOnlyList<MotorCheck>? checks)
    {
        var fingers = MarkedFingers(checks);
        if (fingers.Count == 0)
            return null;
        var names = JoinFingers(fingers);
        return $"{char.ToUpperInvariant(names[0])}{names[1..]} {Motors(fingers)} didn't respond at startup. "
            + "Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.";
    }

    private static string Motors(IReadOnlyList<int> fingers) => fingers.Count == 1 ? "motor" : "motors";
}
