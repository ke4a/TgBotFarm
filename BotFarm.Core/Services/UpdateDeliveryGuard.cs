using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services;

/// <summary>
/// Keeps per-bot, in-memory records of accepted update ids, of temporary processing failures
/// and of the last error alert time.
/// </summary>
/// <remarks>
/// Update ids are remembered for <see cref="DuplicateWindow"/>, and at most
/// <see cref="MaxTrackedUpdatesPerBot"/> ids are kept per bot. Failure counts are remembered for
/// <see cref="FailureWindow"/> after an update first fails, and at most <see cref="MaxTrackedFailuresPerBot"/>
/// are kept per bot. In both cases the oldest entries are dropped first.
/// </remarks>
internal sealed class UpdateDeliveryGuard : IUpdateDeliveryGuard
{
    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);
    internal static readonly TimeSpan AlertInterval = TimeSpan.FromMinutes(1);
    internal const int MaxTrackedUpdatesPerBot = 10_000;
    internal const int MaxTrackedFailuresPerBot = 1_000;

    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, TrackedIds> _acceptedUpdates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedIds> _failedUpdates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastAlerts = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates the guard using <paramref name="timeProvider"/> as its clock.
    /// </summary>
    public UpdateDeliveryGuard(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool TryAcceptUpdate(string botName, int updateId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            var accepted = GetTracked(_acceptedUpdates, botName, DuplicateWindow, MaxTrackedUpdatesPerBot, now);
            if (accepted.Contains(updateId))
            {
                return false;
            }

            accepted.Increment(updateId, now);

            return true;
        }
    }

    public bool TryAcquireAlertSlot(string botName)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (_lastAlerts.TryGetValue(botName, out var lastAlert) && now - lastAlert < AlertInterval)
            {
                return false;
            }

            _lastAlerts[botName] = now;

            return true;
        }
    }

    public bool TryReleaseForRetry(string botName, int updateId, int maxAttempts, out int attempts)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            var failed = GetTracked(_failedUpdates, botName, FailureWindow, MaxTrackedFailuresPerBot, now);
            attempts = failed.Increment(updateId, now);

            if (attempts >= maxAttempts)
            {
                failed.Remove(updateId);
                return false;
            }

            GetTracked(_acceptedUpdates, botName, DuplicateWindow, MaxTrackedUpdatesPerBot, now).Remove(updateId);

            return true;
        }
    }

    public void ClearFailures(string botName, int updateId)
    {
        lock (_lock)
        {
            if (_failedUpdates.TryGetValue(botName, out var failed))
            {
                failed.Remove(updateId);
            }
        }
    }

    private static TrackedIds GetTracked(
        Dictionary<string, TrackedIds> perBot,
        string botName,
        TimeSpan lifetime,
        int capacity,
        DateTimeOffset now)
    {
        if (!perBot.TryGetValue(botName, out var tracked))
        {
            tracked = new TrackedIds(lifetime, capacity);
            perBot[botName] = tracked;
        }

        tracked.RemoveExpired(now);

        return tracked;
    }

    /// <summary>
    /// A bounded set of update ids, each with the time it was added and a counter.
    /// </summary>
    private sealed class TrackedIds(TimeSpan lifetime, int capacity)
    {
        // The queue holds ids in the order they were added, so expired and excess entries are always at its head.
        // An id that was removed and added again leaves a stale queue entry behind; the sequence number
        // tells it apart from the current one.
        private readonly Queue<(int Id, DateTimeOffset AddedAt, long Sequence)> _order = new();
        private readonly Dictionary<int, (long Sequence, int Count)> _entries = new();
        private long _nextSequence;

        public bool Contains(int updateId) => _entries.ContainsKey(updateId);

        /// <summary>
        /// Adds <paramref name="updateId"/> with a count of one, or increases its count when it is already present,
        /// and returns the new count.
        /// </summary>
        public int Increment(int updateId, DateTimeOffset now)
        {
            if (_entries.TryGetValue(updateId, out var entry))
            {
                _entries[updateId] = entry with { Count = entry.Count + 1 };
                return entry.Count + 1;
            }

            var sequence = _nextSequence++;
            _entries[updateId] = (sequence, 1);
            _order.Enqueue((updateId, now, sequence));

            while (_order.Count > capacity && _order.TryDequeue(out var oldest))
            {
                RemoveIfCurrent(oldest.Id, oldest.Sequence);
            }

            return 1;
        }

        public void Remove(int updateId) => _entries.Remove(updateId);

        public void RemoveExpired(DateTimeOffset now)
        {
            var cutoff = now - lifetime;
            while (_order.TryPeek(out var oldest) && oldest.AddedAt <= cutoff)
            {
                _order.Dequeue();
                RemoveIfCurrent(oldest.Id, oldest.Sequence);
            }
        }

        private void RemoveIfCurrent(int updateId, long sequence)
        {
            if (_entries.TryGetValue(updateId, out var entry) && entry.Sequence == sequence)
            {
                _entries.Remove(updateId);
            }
        }
    }
}
