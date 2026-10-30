namespace BotFarm.TestKit;

/// <summary>
/// A <see cref="TimeProvider"/> whose current time only changes when a test advances it.
/// </summary>
/// <remarks>
/// Timers created through this provider (including those behind <c>Task.Delay</c> and <c>PeriodicTimer</c>)
/// fire only when <see cref="Advance"/> moves the clock past their due time.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Number of timers that are currently waiting to fire.
    /// </summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_sync)
            {
                return _timers.Count(timer => timer.DueAt is not null);
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    /// <summary>
    /// Moves the clock forward and fires every timer that becomes due, in due-time order.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        DateTimeOffset target;
        lock (_sync)
        {
            target = _utcNow + delta;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_sync)
            {
                next = _timers
                    .Where(timer => timer.DueAt is not null && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
                if (next is null)
                {
                    _utcNow = target;
                    return;
                }

                _utcNow = next.DueAt!.Value;
                next.DueAt = next.Period > TimeSpan.Zero ? _utcNow + next.Period : null;
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                if (!owner._timers.Contains(this))
                {
                    return false;
                }

                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._sync)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
