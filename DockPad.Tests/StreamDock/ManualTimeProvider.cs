namespace DockPad.Tests;

/// <summary>
/// Horloge des tests : le temps n'avance que par Advance, qui déclenche dans l'ordre les minuteurs échus,
/// sur le thread appelant.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        DateTimeOffset target;
        lock (_gate)
            target = _now + delta;

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.DueAt != null && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
                if (next == null)
                {
                    _now = target;
                    return;
                }
                _now = next.DueAt ?? _now;
                next.ScheduleNextTick();
            }
            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Prochaine échéance, null si le minuteur est arrêté.
        /// </summary>
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
                _period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                if (DueAt != null)
                    owner._timers.Add(this);
            }
            return true;
        }

        /// <summary>
        /// Appelé sous le verrou de l'horloge, avant le rappel : un minuteur sans période s'arrête.
        /// </summary>
        public void ScheduleNextTick()
        {
            if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
            {
                DueAt = null;
                owner._timers.Remove(this);
                return;
            }
            DueAt += _period;
        }

        public void Fire() => callback(state);

        public void Dispose() => Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
