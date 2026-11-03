using System;

namespace AvaranFlow.Alerting.Models
{
    /// <summary>
    /// Represents an immutable point-in-time snapshot of runtime health, throughput,
    /// delivery outcomes, queue depth, and reconfiguration counters.
    /// </summary>
    public sealed class AlertingMetrics
    {
        public DateTime TimestampUtc { get; }
        public long EventsReceived { get; }
        public long EventsAdmitted { get; }
        public long EventsDropped { get; }
        public long RulesEvaluated { get; }
        public long RulesMatched { get; }
        public long AlertsSuppressed { get; }
        public long AlertsEscalated { get; }
        public long DestinationAttempts { get; }
        public long DestinationSuccesses { get; }
        public long DestinationFailures { get; }
        public long FallbackAttempts { get; }
        public long FallbackSuccesses { get; }
        public long FallbackFailures { get; }
        public long ReconfigurationCount { get; }
        public long ReconfigurationDroppedEvents { get; }
        public long ReconfigurationTimeouts { get; }
        public int QueueDepth { get; }
        public long ConfigurationErrorCount { get; }
        public long ProcessingFailures { get; }
        public long FlushTimeouts { get; }
        public long FlushOperationsCompleted { get; }

        public AlertingMetrics(
            DateTime timestampUtc,
            long eventsReceived,
            long eventsAdmitted,
            long eventsDropped,
            long rulesEvaluated,
            long rulesMatched,
            long alertsSuppressed,
            long alertsEscalated,
            long destinationAttempts,
            long destinationSuccesses,
            long destinationFailures,
            long fallbackAttempts,
            long fallbackSuccesses,
            long fallbackFailures,
            long reconfigurationCount,
            long reconfigurationDroppedEvents,
            long reconfigurationTimeouts,
            int queueDepth,
            long configurationErrorCount,
            long processingFailures = 0,
            long flushTimeouts = 0,
            long flushOperationsCompleted = 0)
        {
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
            EventsReceived = eventsReceived;
            EventsAdmitted = eventsAdmitted;
            EventsDropped = eventsDropped;
            RulesEvaluated = rulesEvaluated;
            RulesMatched = rulesMatched;
            AlertsSuppressed = alertsSuppressed;
            AlertsEscalated = alertsEscalated;
            DestinationAttempts = destinationAttempts;
            DestinationSuccesses = destinationSuccesses;
            DestinationFailures = destinationFailures;
            FallbackAttempts = fallbackAttempts;
            FallbackSuccesses = fallbackSuccesses;
            FallbackFailures = fallbackFailures;
            ReconfigurationCount = reconfigurationCount;
            ReconfigurationDroppedEvents = reconfigurationDroppedEvents;
            ReconfigurationTimeouts = reconfigurationTimeouts;
            QueueDepth = queueDepth >= 0 ? queueDepth : 0;
            ConfigurationErrorCount = configurationErrorCount;
            ProcessingFailures = processingFailures;
            FlushTimeouts = flushTimeouts;
            FlushOperationsCompleted = flushOperationsCompleted;
        }

        public override string ToString()
        {
            return $"AlertingMetrics [{TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC] " +
                   $"Received={EventsReceived}, Admitted={EventsAdmitted}, Dropped={EventsDropped}, " +
                   $"RulesMatched={RulesMatched}/{RulesEvaluated}, Suppressed={AlertsSuppressed}, Escalated={AlertsEscalated}, " +
                   $"DestDeliveries={DestinationSuccesses}/{DestinationAttempts}, QueueDepth={QueueDepth}";
        }
    }
}
