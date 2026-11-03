using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AvaranFlow.Alerting.Infrastructure;

namespace AvaranFlow.Alerting.Suppression
{
    /// <summary>
    /// Thread-safe bounded cache tracking rule-scoped alert suppression state.
    /// Strictly limits memory usage to a configured maximum entry count with deterministic LRU and TTL-based eviction.
    /// Does not introduce background timers or unbounded data structures.
    /// </summary>
    public sealed class BoundedSuppressionCache
    {
        private sealed class CacheEntry
        {
            public readonly object SyncRoot = new object();
            public DateTime LastDeliveredUtc;
            public TimeSpan CooldownPeriod;
            public long LastAccessTicks;
        }

        private readonly ConcurrentDictionary<SuppressionKey, CacheEntry> _entries;
        private readonly int _maxEntries;
        private readonly ISystemClock _clock;
        private int _evictionLock;

        /// <summary>
        /// Gets the current number of cached suppression entries.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Gets the maximum capacity of the suppression cache.
        /// </summary>
        public int MaxCapacity => _maxEntries;

        public BoundedSuppressionCache(int maxEntries = 10000, ISystemClock? clock = null)
        {
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries), "MaxEntries must be positive.");

            _maxEntries = maxEntries;
            _clock = clock ?? DefaultSystemClock.Instance;
            _entries = new ConcurrentDictionary<SuppressionKey, CacheEntry>();
        }

        /// <summary>
        /// Atomically checks whether delivery is permitted for the given suppression key.
        /// If not in cooldown, sets delivery timestamp to now, starting the cooldown, and returns true.
        /// If currently in cooldown, outputs remaining cooldown and returns false.
        /// </summary>
        public bool TryAcquireDelivery(SuppressionKey key, TimeSpan cooldownPeriod, out TimeSpan remainingCooldown)
        {
            if (cooldownPeriod <= TimeSpan.Zero)
            {
                remainingCooldown = TimeSpan.Zero;
                return true;
            }

            EnsureCapacity();

            var entry = _entries.GetOrAdd(key, _ => new CacheEntry());
            var now = _clock.UtcNow;

            lock (entry.SyncRoot)
            {
                entry.LastAccessTicks = Stopwatch.GetTimestamp();

                if (entry.LastDeliveredUtc != default)
                {
                    var elapsed = now - entry.LastDeliveredUtc;
                    if (elapsed < entry.CooldownPeriod)
                    {
                        remainingCooldown = entry.CooldownPeriod - elapsed;
                        return false;
                    }
                }

                // Not in cooldown: acquire delivery and reset cooldown window
                entry.LastDeliveredUtc = now;
                entry.CooldownPeriod = cooldownPeriod;
                remainingCooldown = TimeSpan.Zero;
                return true;
            }
        }

        /// <summary>
        /// Checks whether the specified key is currently in cooldown without mutating state.
        /// </summary>
        public bool IsInCooldown(SuppressionKey key, TimeSpan cooldownPeriod)
        {
            if (cooldownPeriod <= TimeSpan.Zero) return false;

            if (_entries.TryGetValue(key, out var entry))
            {
                lock (entry.SyncRoot)
                {
                    entry.LastAccessTicks = Stopwatch.GetTimestamp();
                    if (entry.LastDeliveredUtc != default)
                    {
                        var elapsed = _clock.UtcNow - entry.LastDeliveredUtc;
                        return elapsed < entry.CooldownPeriod;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Unconditionally starts a fresh cooldown window for the specified key at the current time.
        /// Used when an escalation threshold is crossed to bypass existing cooldown and immediately establish a new cooldown.
        /// </summary>
        public void ResetCooldown(SuppressionKey key, TimeSpan cooldownPeriod)
        {
            if (cooldownPeriod <= TimeSpan.Zero) return;

            EnsureCapacity();

            var entry = _entries.GetOrAdd(key, _ => new CacheEntry());
            lock (entry.SyncRoot)
            {
                entry.LastDeliveredUtc = _clock.UtcNow;
                entry.CooldownPeriod = cooldownPeriod;
                entry.LastAccessTicks = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Prunes entries whose cooldown periods have fully expired.
        /// </summary>
        /// <returns>The number of pruned entries.</returns>
        public int PruneExpired()
        {
            var now = _clock.UtcNow;
            int pruned = 0;

            foreach (var kvp in _entries)
            {
                var entry = kvp.Value;
                lock (entry.SyncRoot)
                {
                    if (entry.LastDeliveredUtc != default && (now - entry.LastDeliveredUtc) >= entry.CooldownPeriod)
                    {
                        if (_entries.TryRemove(kvp.Key, out _))
                        {
                            pruned++;
                        }
                    }
                }
            }

            return pruned;
        }

        /// <summary>
        /// Clears all entries from the suppression cache.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }

        private void EnsureCapacity()
        {
            if (_entries.Count < _maxEntries) return;

            // Attempt to take eviction lock; non-blocking to prevent contention spikes
            if (Interlocked.CompareExchange(ref _evictionLock, 1, 0) != 0)
                return;

            try
            {
                if (_entries.Count < _maxEntries) return;

                // Step 1: Prune expired entries
                PruneExpired();

                // Step 2: If still at or over capacity, evict oldest entries by LastAccessTicks (LRU)
                if (_entries.Count >= _maxEntries)
                {
                    int targetToRemove = Math.Max(1, _maxEntries / 10); // Remove 10%
                    var oldest = _entries
                        .Select(kvp => new { kvp.Key, kvp.Value.LastAccessTicks })
                        .OrderBy(x => x.LastAccessTicks)
                        .Take(targetToRemove)
                        .ToList();

                    foreach (var item in oldest)
                    {
                        _entries.TryRemove(item.Key, out _);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _evictionLock, 0);
            }
        }
    }
}
