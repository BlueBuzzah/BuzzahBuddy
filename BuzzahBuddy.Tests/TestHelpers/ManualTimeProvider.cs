namespace BuzzahBuddy.Tests.TestHelpers;

/// <summary>
/// A clock tests move by hand. Timers (and so <c>Task.Delay(delay, time)</c>) fire only
/// when <see cref="Advance"/> reaches their due time, on the caller's thread.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
            return _now;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += by;
            due = _timers.Where(t => t.DueAt <= _now).OrderBy(t => t.DueAt).ToList();
            foreach (var timer in due)
                _timers.Remove(timer);
        }
        foreach (var timer in due)
            timer.Fire();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private void Schedule(ManualTimer timer, TimeSpan dueTime)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.DueAt = _now + dueTime;
                _timers.Add(timer);
            }
        }
    }

    private void Unschedule(ManualTimer timer)
    {
        lock (_lock)
            _timers.Remove(timer);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public DateTimeOffset DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
                return false;
            _period = period;
            owner.Schedule(this, dueTime);
            return true;
        }

        public void Fire()
        {
            if (_disposed)
                return;
            // A periodic timer fires once per Advance, then waits a full period again.
            if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
                owner.Schedule(this, _period);
            callback(state);
        }

        public void Dispose()
        {
            _disposed = true;
            owner.Unschedule(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
