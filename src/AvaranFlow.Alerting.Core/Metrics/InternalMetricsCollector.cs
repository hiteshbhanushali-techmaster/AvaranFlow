using System;
using System.Threading;
using AvaranFlow.Alerting.Models;

namespace AvaranFlow.Alerting.Metrics
{
    /// <summary>
    /// Thread-safe metrics collector using atomic interlocked operations for zero-lock counter progression.
    /// Produces immutable point-in-time snapshots of runtime metrics.
    /// </summary>
    public sealed class InternalMetricsCollector
    {
        private long _eventsReceived;
        private long _eventsAdmitted;
        private long _eventsDropped;
        private long _rulesEvaluated;
        private long _rulesMatched;
        private long _alertsSuppressed;
        private long _alertsEscalated;
        private long _destinationAttempts;
        private long _destinationSuccesses;
        private long _destinationFailures;
        private long _fallbackAttempts;
        private long _fallbackSuccesses;
        private long _fallbackFailures;
        private long _reconfigurationCount;
        private long _reconfigurationDroppedEvents;
        private long _reconfigurationTimeouts;
        private long _configurationErrorCount;
        private long _processingFailures;
        private long _flushTimeouts;
        private long _flushOperationsCompleted;

        public void IncrementEventsReceived() => Interlocked.Increment(ref _eventsReceived);
        public void IncrementEventsAdmitted() => Interlocked.Increment(ref _eventsAdmitted);
        public void IncrementEventsDropped() => Interlocked.Increment(ref _eventsDropped);
        public void AddEventsDropped(long count) => Interlocked.Add(ref _eventsDropped, count);

        public void IncrementRulesEvaluated() => Interlocked.Increment(ref _rulesEvaluated);
        public void AddRulesEvaluated(long count) => Interlocked.Add(ref _rulesEvaluated, count);

        public void IncrementRulesMatched() => Interlocked.Increment(ref _rulesMatched);
        public void AddRulesMatched(long count) => Interlocked.Add(ref _rulesMatched, count);

        public void IncrementAlertsSuppressed() => Interlocked.Increment(ref _alertsSuppressed);
        public void IncrementAlertsEscalated() => Interlocked.Increment(ref _alertsEscalated);

        public void IncrementDestinationAttempts() => Interlocked.Increment(ref _destinationAttempts);
        public void AddDestinationAttempts(long count) => Interlocked.Add(ref _destinationAttempts, count);

        public void IncrementDestinationSuccesses() => Interlocked.Increment(ref _destinationSuccesses);
        public void AddDestinationSuccesses(long count) => Interlocked.Add(ref _destinationSuccesses, count);

        public void IncrementDestinationFailures() => Interlocked.Increment(ref _destinationFailures);
        public void AddDestinationFailures(long count) => Interlocked.Add(ref _destinationFailures, count);

        public void IncrementFallbackAttempts() => Interlocked.Increment(ref _fallbackAttempts);
        public void IncrementFallbackSuccesses() => Interlocked.Increment(ref _fallbackSuccesses);
        public void IncrementFallbackFailures() => Interlocked.Increment(ref _fallbackFailures);

        public void IncrementReconfigurationCount() => Interlocked.Increment(ref _reconfigurationCount);
        public void AddReconfigurationDroppedEvents(long count) => Interlocked.Add(ref _reconfigurationDroppedEvents, count);
        public void IncrementReconfigurationTimeouts() => Interlocked.Increment(ref _reconfigurationTimeouts);
        public void IncrementConfigurationErrors() => Interlocked.Increment(ref _configurationErrorCount);

        public void IncrementProcessingFailures() => Interlocked.Increment(ref _processingFailures);
        public void IncrementFlushTimeouts() => Interlocked.Increment(ref _flushTimeouts);
        public void IncrementFlushOperationsCompleted() => Interlocked.Increment(ref _flushOperationsCompleted);

        /// <summary>
        /// Creates an immutable point-in-time snapshot of the current metrics.
        /// </summary>
        public AlertingMetrics CreateSnapshot(int currentQueueDepth = 0)
        {
            return new AlertingMetrics(
                timestampUtc: DateTime.UtcNow,
                eventsReceived: Volatile.Read(ref _eventsReceived),
                eventsAdmitted: Volatile.Read(ref _eventsAdmitted),
                eventsDropped: Volatile.Read(ref _eventsDropped),
                rulesEvaluated: Volatile.Read(ref _rulesEvaluated),
                rulesMatched: Volatile.Read(ref _rulesMatched),
                alertsSuppressed: Volatile.Read(ref _alertsSuppressed),
                alertsEscalated: Volatile.Read(ref _alertsEscalated),
                destinationAttempts: Volatile.Read(ref _destinationAttempts),
                destinationSuccesses: Volatile.Read(ref _destinationSuccesses),
                destinationFailures: Volatile.Read(ref _destinationFailures),
                fallbackAttempts: Volatile.Read(ref _fallbackAttempts),
                fallbackSuccesses: Volatile.Read(ref _fallbackSuccesses),
                fallbackFailures: Volatile.Read(ref _fallbackFailures),
                reconfigurationCount: Volatile.Read(ref _reconfigurationCount),
                reconfigurationDroppedEvents: Volatile.Read(ref _reconfigurationDroppedEvents),
                reconfigurationTimeouts: Volatile.Read(ref _reconfigurationTimeouts),
                queueDepth: currentQueueDepth,
                configurationErrorCount: Volatile.Read(ref _configurationErrorCount),
                processingFailures: Volatile.Read(ref _processingFailures),
                flushTimeouts: Volatile.Read(ref _flushTimeouts),
                flushOperationsCompleted: Volatile.Read(ref _flushOperationsCompleted));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _eventsReceived, 0);
            Interlocked.Exchange(ref _eventsAdmitted, 0);
            Interlocked.Exchange(ref _eventsDropped, 0);
            Interlocked.Exchange(ref _rulesEvaluated, 0);
            Interlocked.Exchange(ref _rulesMatched, 0);
            Interlocked.Exchange(ref _alertsSuppressed, 0);
            Interlocked.Exchange(ref _alertsEscalated, 0);
            Interlocked.Exchange(ref _destinationAttempts, 0);
            Interlocked.Exchange(ref _destinationSuccesses, 0);
            Interlocked.Exchange(ref _destinationFailures, 0);
            Interlocked.Exchange(ref _fallbackAttempts, 0);
            Interlocked.Exchange(ref _fallbackSuccesses, 0);
            Interlocked.Exchange(ref _fallbackFailures, 0);
            Interlocked.Exchange(ref _reconfigurationCount, 0);
            Interlocked.Exchange(ref _reconfigurationDroppedEvents, 0);
            Interlocked.Exchange(ref _reconfigurationTimeouts, 0);
            Interlocked.Exchange(ref _configurationErrorCount, 0);
            Interlocked.Exchange(ref _processingFailures, 0);
            Interlocked.Exchange(ref _flushTimeouts, 0);
            Interlocked.Exchange(ref _flushOperationsCompleted, 0);
        }
    }
}
