using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AvaranFlow.Alerting.Infrastructure;
using AvaranFlow.Alerting.Rules;

namespace AvaranFlow.Alerting.Escalation
{
    /// <summary>
    /// Thread-safe bounded tracker managing rule-scoped escalation state.
    /// Strictly limits memory usage to a configured maximum entry count with deterministic LRU and TTL-based eviction.
    /// Guarantees atomic occurrence counter increments without lost updates under high concurrency.
    /// Does not introduce background timers or unbounded data structures.
    /// </summary>
    public sealed class BoundedEscalationTracker
    {
        private sealed class EscalationEntry
        {
            public readonly object SyncRoot = new object();
            public int CurrentLevel = 1;
            public DateTime FirstOccurrenceUtc;
            public DateTime LastOccurrenceUtc;
            public long OccurrenceCount;
            public long LastAccessTicks;
        }

        private static readonly IReadOnlyList<string> EmptyStrings = new List<string>(0).AsReadOnly();

        private readonly ConcurrentDictionary<EscalationKey, EscalationEntry> _entries;
        private readonly int _maxEntries;
        private readonly ISystemClock _clock;
        private int _evictionLock;

        /// <summary>
        /// Gets the current number of tracked escalation entries.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Gets the maximum capacity of the escalation tracker.
        /// </summary>
        public int MaxCapacity => _maxEntries;

        public BoundedEscalationTracker(int maxEntries = 5000, ISystemClock? clock = null)
        {
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries), "MaxEntries must be positive.");

            _maxEntries = maxEntries;
            _clock = clock ?? DefaultSystemClock.Instance;
            _entries = new ConcurrentDictionary<EscalationKey, EscalationEntry>();
        }

        /// <summary>
        /// Atomically increments the occurrence count and evaluates escalation triggers for the specified key.
        /// Serializes updates per key to guarantee zero lost increments under high concurrency.
        /// </summary>
        public EscalationEvaluationResult UpdateAndEvaluate(
            EscalationKey key,
            IEscalationPolicy policy,
            RuleEvaluationContext context)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (context == null) throw new ArgumentNullException(nameof(context));

            EnsureCapacity(policy.Ttl);

            var entry = _entries.GetOrAdd(key, _ => new EscalationEntry());
            var now = _clock.UtcNow;

            lock (entry.SyncRoot)
            {
                entry.LastAccessTicks = Stopwatch.GetTimestamp();

                int previousLevel;
                // Check TTL expiration
                if (entry.OccurrenceCount > 0 && policy.Ttl > TimeSpan.Zero && (now - entry.LastOccurrenceUtc) > policy.Ttl)
                {
                    // Inactivity window exceeded: reset state to baseline
                    entry.OccurrenceCount = 1;
                    entry.FirstOccurrenceUtc = now;
                    entry.LastOccurrenceUtc = now;
                    entry.CurrentLevel = 1;
                    previousLevel = 1;
                }
                else
                {
                    entry.OccurrenceCount++;
                    if (entry.OccurrenceCount == 1)
                    {
                        entry.FirstOccurrenceUtc = now;
                        entry.CurrentLevel = 1;
                    }
                    entry.LastOccurrenceUtc = now;
                    previousLevel = entry.CurrentLevel;
                }

                var elapsedTime = now - entry.FirstOccurrenceUtc;

                // Evaluate if any higher escalation step is triggered
                EscalationStep? highestTriggeredStep = null;
                for (int i = 0; i < policy.Steps.Count; i++)
                {
                    var step = policy.Steps[i];
                    if (step.Level > entry.CurrentLevel && step.IsTriggered(entry.OccurrenceCount, elapsedTime, context.Severity))
                    {
                        if (highestTriggeredStep == null || step.Level > highestTriggeredStep.Level)
                        {
                            highestTriggeredStep = step;
                        }
                    }
                }

                bool thresholdCrossed = false;
                IReadOnlyList<string> effectiveDestinations;
                IReadOnlyList<string> effectiveGroups;

                if (highestTriggeredStep != null)
                {
                    thresholdCrossed = true;
                    entry.CurrentLevel = highestTriggeredStep.Level;
                    effectiveDestinations = highestTriggeredStep.TargetDestinations;
                    effectiveGroups = highestTriggeredStep.TargetGroups;
                }
                else
                {
                    var currentStep = policy.GetStep(entry.CurrentLevel);
                    effectiveDestinations = currentStep?.TargetDestinations ?? EmptyStrings;
                    effectiveGroups = currentStep?.TargetGroups ?? EmptyStrings;
                }

                return new EscalationEvaluationResult(
                    previousLevel: previousLevel,
                    currentLevel: entry.CurrentLevel,
                    thresholdCrossed: thresholdCrossed,
                    occurrenceCount: entry.OccurrenceCount,
                    firstOccurrenceUtc: entry.FirstOccurrenceUtc,
                    lastOccurrenceUtc: entry.LastOccurrenceUtc,
                    effectiveDestinations: effectiveDestinations,
                    effectiveGroups: effectiveGroups);
            }
        }

        /// <summary>
        /// Attempts to retrieve a point-in-time snapshot of the escalation state for the given key.
        /// </summary>
        public bool TryGetState(EscalationKey key, out EscalationState? state)
        {
            state = null;
            if (_entries.TryGetValue(key, out var entry))
            {
                lock (entry.SyncRoot)
                {
                    state = new EscalationState(
                        entry.CurrentLevel,
                        entry.FirstOccurrenceUtc,
                        entry.LastOccurrenceUtc,
                        entry.OccurrenceCount);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Prunes entries that have been inactive longer than the specified TTL.
        /// </summary>
        public int PruneExpired(TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero) return 0;

            var now = _clock.UtcNow;
            int pruned = 0;

            foreach (var kvp in _entries)
            {
                var entry = kvp.Value;
                lock (entry.SyncRoot)
                {
                    if (entry.OccurrenceCount > 0 && (now - entry.LastOccurrenceUtc) > ttl)
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
        /// Clears all entries from the escalation tracker.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }

        private void EnsureCapacity(TimeSpan fallbackTtl)
        {
            if (_entries.Count < _maxEntries) return;

            if (Interlocked.CompareExchange(ref _evictionLock, 1, 0) != 0)
                return;

            try
            {
                if (_entries.Count < _maxEntries) return;

                // Step 1: Prune expired entries
                PruneExpired(fallbackTtl > TimeSpan.Zero ? fallbackTtl : TimeSpan.FromMinutes(30));

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
