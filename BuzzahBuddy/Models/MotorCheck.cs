namespace BuzzahBuddy.Models;

/// <summary>
/// One finger's boot motor check, from INFO's MOTP (primary) and MOTS (secondary) keys.
/// Mirrors firmware include/motor_check.h: OK (motor present), OPEN (no motor seen),
/// NODRIVER (the port's driver never started), UNKNOWN (not checked, probe timeout, mux
/// select failure, or a pass voided by a supply dip; a busy bus leaves the previous result).
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
    /// the key is missing or malformed (v2, firmware before the check, or an empty token); an
    /// unknown token is <see cref="MotorCheck.Unknown"/>.
    /// </summary>
    public static IReadOnlyList<MotorCheck>? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var tokens = value.Split(',').Select(token => token.Trim()).ToArray();
        if (tokens.Any(token => token.Length == 0))
            return null;
        return tokens.Select(token => token switch
        {
            "OK" => MotorCheck.Ok,
            "OPEN" => MotorCheck.Open,
            "NODRIVER" => MotorCheck.NoDriver,
            _ => MotorCheck.Unknown,
        }).ToArray();
    }

    /// <summary>
    /// The one problem rule, shared with the firmware's boot warning: index to little when OPEN
    /// or NODRIVER, the thumb only when NODRIVER (an empty thumb port is normal). UNKNOWN never
    /// is. Null means no report.
    /// </summary>
    public static bool IsMarked(int finger, MotorCheck? check) =>
        finger < RequiredFingers ? check is MotorCheck.Open or MotorCheck.NoDriver
        : finger == Thumb && check == MotorCheck.NoDriver;

    /// <summary>The fingers <see cref="IsMarked"/> marks, in finger order.</summary>
    public static IReadOnlyList<int> ProblemFingers(IReadOnlyList<MotorCheck>? checks) =>
        checks is null ? [] : Enumerable.Range(0, checks.Count).Where(f => IsMarked(f, checks[f])).ToArray();

    /// <summary>
    /// What a marked finger button adds for screen readers; null when it isn't marked.
    /// </summary>
    public static string? AccessibleSuffix(int finger, MotorCheck? check) =>
        !IsMarked(finger, check) ? null
        : check == MotorCheck.NoDriver ? ", driver didn't start"
        : ", didn't respond when it turned on";

    /// <summary>Lowercase finger names: "ring", "ring and little", "middle, ring and little".</summary>
    public static string JoinFingers(IReadOnlyList<int> fingers)
    {
        var names = fingers.Select(f => CalibrationSettings.GetFingerName(f).ToLowerInvariant()).ToList();
        return names.Count <= 1
            ? string.Concat(names)
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }

    /// <summary>
    /// The Gloves banner message: one sentence per kind of problem on each glove, then what to
    /// do. Null when neither glove has a problem. "Your primary glove's ring and little motors
    /// didn't respond when it turned on. Sessions still run. Tap Test Motors to check them."
    /// </summary>
    public static string? BannerMessage(IReadOnlyList<MotorCheck>? primary, IReadOnlyList<MotorCheck>? secondary)
    {
        var sentences = GloveSentences("primary", primary).Concat(GloveSentences("secondary", secondary)).ToList();
        if (sentences.Count == 0)
            return null;
        var them = ProblemFingers(primary).Count + ProblemFingers(secondary).Count > 1 ? "them" : "it";
        return $"{string.Join(" ", sentences)} Sessions still run. Tap Test Motors to check {them}.";
    }

    // "Your primary glove's ring motor didn't respond when it turned on." then "Its thumb driver
    // didn't start." (or "Your primary glove's thumb driver didn't start." on its own).
    private static IEnumerable<string> GloveSentences(string glove, IReadOnlyList<MotorCheck>? checks)
    {
        var (open, noDriver) = Split(checks);
        var owner = $"Your {glove} glove's";
        if (open.Count > 0)
        {
            yield return $"{owner} {OpenProblem(open)}.";
            owner = "Its";
        }
        if (noDriver.Count > 0)
            yield return $"{owner} {NoDriverProblem(noDriver)}.";
    }

    /// <summary>
    /// The line under a glove's heading in Test Motors: what failed and the next step for each
    /// kind of problem. Null when no finger is marked.
    /// </summary>
    public static string? GloveLine(IReadOnlyList<MotorCheck>? checks)
    {
        var (open, noDriver) = Split(checks);
        var parts = new List<string>();
        if (open.Count > 0)
            parts.Add($"{Capitalize(OpenProblem(open, "the glove"))}. "
                + "Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.");
        if (noDriver.Count > 0)
            parts.Add($"{Capitalize(NoDriverProblem(noDriver))}. Charge the glove, then restart it.");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static (IReadOnlyList<int> Open, IReadOnlyList<int> NoDriver) Split(IReadOnlyList<MotorCheck>? checks)
    {
        var fingers = ProblemFingers(checks);
        return (fingers.Where(f => checks![f] == MotorCheck.Open).ToArray(),
            fingers.Where(f => checks![f] == MotorCheck.NoDriver).ToArray());
    }

    // The banner's sentence names the glove, so "it"; the Test Motors line doesn't, so "the glove".
    private static string OpenProblem(IReadOnlyList<int> fingers, string glove = "it") =>
        $"{JoinFingers(fingers)} {(fingers.Count == 1 ? "motor" : "motors")} didn't respond when {glove} turned on";

    private static string NoDriverProblem(IReadOnlyList<int> fingers) =>
        $"{JoinFingers(fingers)} {(fingers.Count == 1 ? "driver" : "drivers")} didn't start";

    private static string Capitalize(string text) => $"{char.ToUpperInvariant(text[0])}{text[1..]}";
}
