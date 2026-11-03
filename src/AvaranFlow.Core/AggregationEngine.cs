using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace AvaranFlow.Core
{
    public sealed class AggregationEngine
    {
        private readonly ConcurrentDictionary<string, AggregateEntry> _entries;
        private readonly TimeSpan _window;
        private readonly int _maxEntries;
        private readonly Func<Exception, string> _customKeyGenerator;

        public AggregationEngine(TimeSpan window, int maxEntries = 10000, Func<Exception, string> customKeyGenerator = null)
        {
            _window = window > TimeSpan.Zero ? window : TimeSpan.FromMinutes(5);
            _maxEntries = maxEntries > 0 ? maxEntries : 10000;
            _customKeyGenerator = customKeyGenerator;
            _entries = new ConcurrentDictionary<string, AggregateEntry>(StringComparer.Ordinal);
        }

        public int CurrentEntryCount => _entries.Count;

        /// <summary>
        /// Computes a stable aggregation key for the given exception.
        /// </summary>
        public string GenerateKey(Exception ex, string caller = null)
        {
            if (ex == null) return "NullEx";

            if (_customKeyGenerator != null)
            {
                try
                {
                    var custom = _customKeyGenerator(ex);
                    if (!string.IsNullOrEmpty(custom)) return custom;
                }
                catch
                {
                    // Fall back to default key generation if custom generator throws
                }
            }

            string typeName = ex.GetType().FullName ?? "Exception";
            string normalizedMessage = NormalizeMessage(ex.Message);
            string topFrame = MetadataExtractor.ExtractTopStackFrame(ex);

            return $"{typeName}|{normalizedMessage}|{topFrame}";
        }

        /// <summary>
        /// Normalizes dynamic content such as memory addresses, hex values, and GUIDs from exception messages.
        /// </summary>
        public static string NormalizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;

            // Trim leading/trailing whitespace
            string normalized = message.Trim();

            // Replace GUIDs with placeholder
            normalized = Regex.Replace(normalized, @"\b[A-Fa-f0-9]{8}-(?:[A-Fa-f0-9]{4}-){3}[A-Fa-f0-9]{12}\b", "{GUID}");

            // Replace hex pointers/addresses like 0x00007FF6B93F
            normalized = Regex.Replace(normalized, @"\b0x[0-9a-fA-F]+\b", "{PTR}");

            // Limit message length in key to 200 chars to avoid memory bloat
            if (normalized.Length > 200)
            {
                normalized = normalized.Substring(0, 200);
            }

            return normalized;
        }

        /// <summary>
        /// Processes an incoming exception occurrence. Returns true if this is the first occurrence (eligible for immediate logging),
        /// or false if it was aggregated into an existing entry.
        /// </summary>
        /// <summary>
        /// Processes an incoming exception occurrence. Returns true if this is the first occurrence (eligible for immediate logging),
        /// or false if it was aggregated into an existing entry.
        /// </summary>
        public bool RecordOccurrence(
            string key,
            DateTime utcNow,
            bool isFatal,
            out AggregateSnapshot snapshot)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                var newEntry = new AggregateEntry(key, utcNow, isFatal);
                if (_entries.TryAdd(key, newEntry))
                {
                    snapshot = newEntry.GetSnapshot();
                    return true; // First occurrence!
                }

                _entries.TryGetValue(key, out entry);
            }

            if (entry != null)
            {
                snapshot = entry.RecordOccurrence(utcNow, isFatal);
                return false; // Repeated occurrence aggregated
            }

            snapshot = new AggregateSnapshot(1, utcNow, utcNow, isFatal);
            return true;
        }

        /// <summary>
        /// Processes an incoming exception occurrence with a context factory. Returns true if first occurrence.
        /// </summary>
        public bool RecordOccurrence(
            string key,
            DateTime utcNow,
            bool isFatal,
            Func<ExceptionContext> contextFactory,
            out ExceptionContext contextToDispatch)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                var baseContext = contextFactory != null ? contextFactory() : null;
                var newEntry = new AggregateEntry(key, utcNow, isFatal, baseContext);
                if (_entries.TryAdd(key, newEntry))
                {
                    contextToDispatch = newEntry.BaseContext;
                    return true; // First occurrence!
                }

                _entries.TryGetValue(key, out entry);
            }

            if (entry != null)
            {
                entry.RecordOccurrence(utcNow, isFatal);
                contextToDispatch = null;
                return false; // Repeated occurrence aggregated
            }

            contextToDispatch = contextFactory != null ? contextFactory() : null;
            return true;
        }

        /// <summary>
        /// Gets all entries that are eligible for periodic summary re-logging.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, AggregateSnapshot>> GetPeriodicSummaries(DateTime utcNow, TimeSpan reportInterval, long countThreshold)
        {
            var summaries = new List<KeyValuePair<string, AggregateSnapshot>>();

            foreach (var kvp in _entries)
            {
                if (kvp.Value.TryMarkReported(utcNow, reportInterval, countThreshold, out var snapshot))
                {
                    summaries.Add(new KeyValuePair<string, AggregateSnapshot>(kvp.Key, snapshot));
                }
            }

            return summaries;
        }

        /// <summary>
        /// Gets all entries that are eligible for periodic summary re-logging as immutable ExceptionContext snapshots.
        /// </summary>
        public IReadOnlyList<ExceptionContext> GetPeriodicSummaryContexts(DateTime utcNow, TimeSpan reportInterval, long countThreshold)
        {
            var summaries = new List<ExceptionContext>();

            foreach (var kvp in _entries)
            {
                if (kvp.Value.TryMarkReported(utcNow, reportInterval, countThreshold, out var snapshot, out long deltaCount))
                {
                    if (kvp.Value.BaseContext != null)
                    {
                        var summary = kvp.Value.BaseContext.WithUpdatedOccurrence(snapshot.Count, deltaCount, snapshot.LastSeenUtc, snapshot.IsFatal);
                        summaries.Add(summary);
                    }
                }
            }

            return summaries;
        }

        /// <summary>
        /// Drains all pending unpersisted summary counts across all registered aggregates (used during Flush and Shutdown).
        /// </summary>
        public IReadOnlyList<ExceptionContext> DrainAllPendingSummaries()
        {
            var pending = new List<ExceptionContext>();

            foreach (var kvp in _entries)
            {
                if (kvp.Value.TryDrainFinal(out var snapshot, out long deltaCount))
                {
                    if (kvp.Value.BaseContext != null)
                    {
                        var summary = kvp.Value.BaseContext.WithUpdatedOccurrence(snapshot.Count, deltaCount, snapshot.LastSeenUtc, snapshot.IsFatal);
                        pending.Add(summary);
                    }
                }
            }

            return pending;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>
        /// Evicts expired entries and enforces max capacity off the hot path (e.g. during periodic timer flush).
        /// Returns any final summary contexts for evicted entries to ensure no aggregated counts are silently lost.
        /// </summary>
        public IReadOnlyList<ExceptionContext> EvictExpired(DateTime utcNow)
        {
            var evictedContexts = new List<ExceptionContext>();
            if (_entries.IsEmpty) return evictedContexts;

            long cutoffTicks = (utcNow - _window).Ticks;
            var expiredKeys = new List<string>();

            foreach (var kvp in _entries)
            {
                if (kvp.Value.GetSnapshot().LastSeenUtc.Ticks < cutoffTicks)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                if (_entries.TryRemove(key, out var evicted))
                {
                    if (evicted.TryDrainFinal(out var snapshot, out long deltaCount) && evicted.BaseContext != null)
                    {
                        evictedContexts.Add(evicted.BaseContext.WithUpdatedOccurrence(snapshot.Count, deltaCount, snapshot.LastSeenUtc, snapshot.IsFatal));
                    }
                }
            }

            // If still exceeding max entries, evict oldest by last seen timestamp
            if (_entries.Count > _maxEntries)
            {
                var oldestKeys = _entries
                    .OrderBy(x => x.Value.GetSnapshot().LastSeenUtc)
                    .Take(_entries.Count - _maxEntries)
                    .Select(x => x.Key)
                    .ToList();

                foreach (var key in oldestKeys)
                {
                    if (_entries.TryRemove(key, out var evicted))
                    {
                        if (evicted.TryDrainFinal(out var snapshot, out long deltaCount) && evicted.BaseContext != null)
                        {
                            evictedContexts.Add(evicted.BaseContext.WithUpdatedOccurrence(snapshot.Count, deltaCount, snapshot.LastSeenUtc, snapshot.IsFatal));
                        }
                    }
                }
            }

            return evictedContexts;
        }
    }
}
