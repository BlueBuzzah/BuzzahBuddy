namespace BuzzahBuddy.Models;

/// <summary>
/// One reading from a <see cref="SessionClock"/> tick.
/// </summary>
public readonly record struct SessionClockReading(
    TimeSpan Elapsed, TimeSpan Remaining, double Fraction, bool HasKnownTotal)
{
    /// <summary>Elapsed as MM:SS (floored to whole seconds).</summary>
    public string ElapsedFormatted => Format((int)Elapsed.TotalSeconds);

    /// <summary>Remaining as MM:SS (ceiling, so the countdown reaches 00:00 exactly at the end).</summary>
    public string RemainingFormatted => Format((int)Math.Ceiling(Remaining.TotalSeconds));

    private static string Format(int totalSeconds) => $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
}

/// <summary>
/// Local display clock for the therapy session timer. The device is authoritative:
/// every SESSION_STATUS arrival re-anchors the clock via <see cref="Anchor"/>, and
/// <see cref="TickTo"/> interpolates smoothly between anchors so the UI can tick
/// every second without extra BLE traffic. Pure logic — no timers, no MAUI types.
/// </summary>
public class SessionClock
{
    /// <summary>
    /// Anchor disagreements at or below this many seconds are absorbed without a
    /// visible backward jump (the display holds until the device catches up);
    /// larger disagreements snap to device truth.
    /// </summary>
    public const int SnapThresholdSeconds = 3;

    private SessionState _state = SessionState.IDLE;
    private double _anchorElapsed;
    private int _totalSeconds;
    private DateTime _anchorUtc;
    private double _minDisplayElapsed; // monotonic guard while running

    /// <summary>False when the device reported TOTAL:0 (unknown session length).</summary>
    public bool HasKnownTotal => _totalSeconds > 0;

    /// <summary>
    /// Re-anchors the clock to a device report. Call on every SessionStatus arrival.
    /// </summary>
    public void Anchor(SessionStatus status, DateTime utcNow)
    {
        if (IsRunningState(_state) && IsRunningState(status.Status))
        {
            var currentDisplay = Math.Max(ComputeRawElapsed(utcNow), _minDisplayElapsed);
            var backwardDrift = currentDisplay - status.ElapsedTimeSeconds;
            _minDisplayElapsed = backwardDrift > 0 && backwardDrift <= SnapThresholdSeconds
                ? currentDisplay // absorb: hold the display, don't jump back
                : 0;             // in agreement, catching up forward, or snapping back
        }
        else
        {
            _minDisplayElapsed = 0; // state transitions always take device truth
        }

        _state = status.Status;
        _anchorElapsed = status.ElapsedTimeSeconds;
        _totalSeconds = status.TotalTimeSeconds;
        _anchorUtc = utcNow;
    }

    /// <summary>Computes the values the UI should display at <paramref name="utcNow"/>.</summary>
    public SessionClockReading TickTo(DateTime utcNow)
    {
        var elapsed = Math.Max(ComputeRawElapsed(utcNow), _minDisplayElapsed);
        if (elapsed < 0)
            elapsed = 0;
        if (HasKnownTotal && elapsed > _totalSeconds)
            elapsed = _totalSeconds;

        var remaining = HasKnownTotal ? _totalSeconds - elapsed : 0;
        var fraction = HasKnownTotal ? elapsed / _totalSeconds : 0;
        return new SessionClockReading(
            TimeSpan.FromSeconds(elapsed), TimeSpan.FromSeconds(remaining), fraction, HasKnownTotal);
    }

    private double ComputeRawElapsed(DateTime utcNow) =>
        IsRunningState(_state)
            ? _anchorElapsed + (utcNow - _anchorUtc).TotalSeconds
            : _anchorElapsed; // PAUSED freezes; IDLE anchors at 0 via CreateIdle

    private static bool IsRunningState(SessionState state) =>
        state is SessionState.RUNNING or SessionState.LOW_BATTERY;
}
