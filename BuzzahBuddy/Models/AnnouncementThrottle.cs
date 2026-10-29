namespace BuzzahBuddy.Models;

/// <summary>
/// Limits how often a changing spoken label updates: a timer read every second is noise
/// (accessibility.md: SessionRing updates "at most once a minute"). A state change always
/// gets through, and so does any change while not running.
/// </summary>
public sealed class AnnouncementThrottle(TimeSpan minInterval, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _text;
    private SessionRingState? _state;
    private DateTimeOffset _lastUpdate;

    public bool ShouldUpdate(string text, SessionRingState state)
    {
        var now = _time.GetUtcNow();
        var update = state != _state
            || (text != _text && (state != SessionRingState.Running || now - _lastUpdate >= minInterval));
        if (update)
        {
            _text = text;
            _state = state;
            _lastUpdate = now;
        }
        return update;
    }
}
