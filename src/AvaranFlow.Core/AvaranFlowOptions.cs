using System;
using System.Collections.Generic;

namespace AvaranFlow.Core
{
    public enum QueueOverflowPolicy
    {
        /// <summary>
        /// Drops newly incoming events when the queue is saturated.
        /// </summary>
        DropNewest,

        /// <summary>
        /// Evicts the oldest buffered event to accommodate the new event.
        /// </summary>
        DropOldest,

        /// <summary>
        /// Blocks the caller thread for a bounded time waiting for queue capacity.
        /// </summary>
        BlockWithTimeout
    }

    /// <summary>
    /// Configuration settings governing ExceptionMaster runtime behavior.
    /// </summary>
    public sealed class AvaranFlowOptions
    {
        public bool EnableAggregation { get; set; } = true;
        public TimeSpan AggregationWindow { get; set; } = TimeSpan.FromMinutes(5);
        public int MaxAggregationEntries { get; set; } = 10000;
        public long AggregationPeriodicCountThreshold { get; set; } = 100;
        public TimeSpan AggregationPeriodicReportInterval { get; set; } = TimeSpan.FromSeconds(30);

        public bool EnableAsyncDispatch { get; set; } = true;
        public int BufferCapacity { get; set; } = 1000;
        public int BatchSize { get; set; } = 50;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
        public QueueOverflowPolicy OverflowPolicy { get; set; } = QueueOverflowPolicy.DropNewest;
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan EnqueueBlockTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

        public bool EnableEmergencyDiagnostics { get; set; } = true;
        public int EventBroadcasterQueueCapacity { get; set; } = 2000;

        public IList<IExceptionSink> Sinks { get; } = new List<IExceptionSink>();
        public IList<FallbackChain> FallbackChains { get; } = new List<FallbackChain>();

        public Func<Exception, string> CustomKeyGenerator { get; set; }

        public static AvaranFlowOptions CreateDefault()
        {
            return new AvaranFlowOptions();
        }
    }

    /// <summary>
    /// Custom options passed on individual Log calls.
    /// </summary>
    public sealed class ExceptionLogOptions
    {
        public string Category { get; set; }
        public string CorrelationId { get; set; }
        public bool IsFatal { get; set; }
        public string Source { get; set; }
        public string CustomKey { get; set; }
    }
}
