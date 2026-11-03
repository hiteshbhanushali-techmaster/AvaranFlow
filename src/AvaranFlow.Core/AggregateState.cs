using System;
using System.Threading;

namespace AvaranFlow.Core
{
    /// <summary>
    /// Immutable snapshot of aggregated statistics for an exception identity.
    /// </summary>
    public sealed class AggregateSnapshot
    {
        public long Count { get; }
        public DateTime FirstSeenUtc { get; }
        public DateTime LastSeenUtc { get; }
        public bool IsFatal { get; }

        public AggregateSnapshot(long count, DateTime firstSeenUtc, DateTime lastSeenUtc, bool isFatal)
        {
            Count = count;
            FirstSeenUtc = firstSeenUtc;
            LastSeenUtc = lastSeenUtc;
            IsFatal = isFatal;
        }
    }

    /// <summary>
    /// Thread-safe aggregate entry supporting atomic multi-field state transitions without global locks.
    /// </summary>
    public sealed class AggregateEntry
    {
        private AggregateSnapshot _snapshot;
        private long _lastReportedCount;
        private long _lastReportedTicksUtc;

        public string ExceptionKey { get; }
        public ExceptionContext BaseContext { get; }

        public AggregateEntry(string exceptionKey, DateTime utcNow, bool isFatal, ExceptionContext baseContext = null)
        {
            ExceptionKey = exceptionKey ?? string.Empty;
            BaseContext = baseContext;
            _snapshot = new AggregateSnapshot(1, utcNow, utcNow, isFatal);
            _lastReportedCount = 1;
            _lastReportedTicksUtc = utcNow.Ticks;
        }

        public AggregateSnapshot GetSnapshot()
        {
            return Volatile.Read(ref _snapshot);
        }

        /// <summary>
        /// Atomically records a repeated occurrence, updating occurrence count and last seen timestamp.
        /// </summary>
        public AggregateSnapshot RecordOccurrence(DateTime utcNow, bool isFatal)
        {
            var spin = new SpinWait();
            while (true)
            {
                var current = Volatile.Read(ref _snapshot);
                var updated = new AggregateSnapshot(
                    current.Count + 1,
                    current.FirstSeenUtc,
                    utcNow,
                    current.IsFatal || isFatal);

                if (Interlocked.CompareExchange(ref _snapshot, updated, current) == current)
                {
                    return updated;
                }
                spin.SpinOnce();
            }
        }

        /// <summary>
        /// Determines if this aggregate has accrued enough new occurrences or elapsed time to warrant periodic re-logging.
        /// </summary>
        public bool TryMarkReported(DateTime utcNow, TimeSpan reportInterval, long countThreshold, out AggregateSnapshot snapshot)
        {
            return TryMarkReported(utcNow, reportInterval, countThreshold, out snapshot, out _);
        }

        /// <summary>
        /// Determines if this aggregate has accrued enough new occurrences or elapsed time to warrant periodic re-logging,
        /// returning the delta count since the previous report.
        /// </summary>
        public bool TryMarkReported(DateTime utcNow, TimeSpan reportInterval, long countThreshold, out AggregateSnapshot snapshot, out long deltaCount)
        {
            snapshot = Volatile.Read(ref _snapshot);
            long currentReported = Volatile.Read(ref _lastReportedCount);
            long lastTicks = Volatile.Read(ref _lastReportedTicksUtc);

            long countDiff = snapshot.Count - currentReported;
            bool intervalElapsed = (utcNow.Ticks - lastTicks) >= reportInterval.Ticks;

            if (countDiff >= countThreshold || (intervalElapsed && countDiff > 0))
            {
                Interlocked.Exchange(ref _lastReportedCount, snapshot.Count);
                Interlocked.Exchange(ref _lastReportedTicksUtc, utcNow.Ticks);
                deltaCount = countDiff;
                return true;
            }

            deltaCount = 0;
            return false;
        }

        /// <summary>
        /// Drains any remaining unreported occurrences upon eviction or shutdown.
        /// </summary>
        public bool TryDrainFinal(out AggregateSnapshot snapshot, out long deltaCount)
        {
            snapshot = Volatile.Read(ref _snapshot);
            long currentReported = Volatile.Read(ref _lastReportedCount);
            long countDiff = snapshot.Count - currentReported;

            if (countDiff > 0)
            {
                Interlocked.Exchange(ref _lastReportedCount, snapshot.Count);
                deltaCount = countDiff;
                return true;
            }

            deltaCount = 0;
            return false;
        }
    }
}
