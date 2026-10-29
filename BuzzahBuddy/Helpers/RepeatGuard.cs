namespace BuzzahBuddy.Helpers;

/// <summary>
/// Ignores repeat activations inside a window, because a tremor can double-fire a tap
/// (design system "Focus and states"; accessibility.md Motor). Share one instance across
/// controls that replace each other in the same spot, such as Start then Pause.
/// </summary>
public sealed class RepeatGuard(TimeSpan window, TimeProvider? time = null)
{
    /// <summary>Start, Pause and Stop ignore a second tap within 400 ms.</summary>
    public static readonly TimeSpan SessionControlWindow = TimeSpan.FromMilliseconds(400);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTimeOffset? _lastAccepted;

    /// <summary>True for the first activation and for any after the window has passed.</summary>
    public bool TryAcquire()
    {
        var now = _time.GetUtcNow();
        if (_lastAccepted is { } last && now - last < window)
            return false;
        _lastAccepted = now;
        return true;
    }
}
