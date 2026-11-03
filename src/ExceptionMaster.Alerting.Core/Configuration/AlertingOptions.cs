using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ExceptionMaster.Alerting.Enums;

namespace ExceptionMaster.Alerting.Configuration
{
    /// <summary>
    /// Configuration options governing the ExceptionMaster.Alerting pipeline,
    /// bounded queue capacities, overflow policies, concurrency, timeouts, and data projection.
    /// </summary>
    public sealed class AlertingOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of events permitted in the bounded ingestion queue. Default is 5000.
        /// </summary>
        public int MaxQueueCapacity { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the policy applied when the ingestion queue reaches capacity. Default is DropNewest.
        /// </summary>
        public QueueOverflowPolicy OverflowPolicy { get; set; } = QueueOverflowPolicy.DropNewest;

        /// <summary>
        /// Gets or sets the number of concurrent worker tasks processing ingestion and rule evaluation. Default is 2.
        /// </summary>
        public int WorkerConcurrency { get; set; } = 2;

        /// <summary>
        /// Gets or sets the default AlertSeverity applied when no explicit or metadata severity is resolved. Default is Error.
        /// </summary>
        public AlertSeverity DefaultSeverity { get; set; } = AlertSeverity.Error;

        /// <summary>
        /// Gets or sets the default timeout allotted to each destination delivery invocation. Default is 5 seconds.
        /// </summary>
        public TimeSpan DefaultDestinationTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the maximum global concurrency permitted across all external destination deliveries. Default is 16.
        /// </summary>
        public int GlobalMaxDestinationConcurrency { get; set; } = 16;

        /// <summary>
        /// Gets or sets the execution strategy for fan-out destination groups. Default is Concurrent.
        /// </summary>
        public FanOutDispatchMode FanOutMode { get; set; } = FanOutDispatchMode.Concurrent;

        /// <summary>
        /// Gets or sets the maximum time a caller may be blocked when QueueOverflowPolicy is BlockWithTimeout. Default is 200ms.
        /// </summary>
        public TimeSpan EnqueueBlockTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Gets or sets the maximum time allotted to drain in-flight alerts during shutdown. Default is 5 seconds.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the maximum time allotted to drain superseded pipeline generations during reconfiguration. Default is 3 seconds.
        /// </summary>
        public TimeSpan ReconfigureDrainTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets whether Alerting automatically subscribes to ExceptionMaster.ExceptionRaised upon startup. Default is true.
        /// </summary>
        public bool SubscribeToExceptionMaster { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum capacity of the bounded suppression cache. Default is 10000.
        /// </summary>
        public int MaxSuppressionEntries { get; set; } = 10000;

        /// <summary>
        /// Gets or sets the maximum capacity of the bounded escalation tracker. Default is 5000.
        /// </summary>
        public int MaxEscalationEntries { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the collection of metadata keys explicitly permitted in destination AlertEnvelopes.
        /// Any metadata keys not in this whitelist are redacted from external destinations.
        /// </summary>
        public IReadOnlyCollection<string> SafeMetadataKeys { get; set; } =
            new ReadOnlyCollection<string>(new string[0]);

        /// <summary>
        /// Gets or sets whether safe stack trace snippets may be included in projected AlertEnvelopes.
        /// External destinations strip this unless explicitly permitted. Default is false.
        /// </summary>
        public bool IncludeSafeStackTrace { get; set; } = false;

        public AlertingOptions()
        {
        }

        /// <summary>
        /// Creates a deep copy of the current configuration options.
        /// </summary>
        public AlertingOptions Clone()
        {
            return new AlertingOptions
            {
                MaxQueueCapacity = MaxQueueCapacity,
                OverflowPolicy = OverflowPolicy,
                WorkerConcurrency = WorkerConcurrency,
                DefaultSeverity = DefaultSeverity,
                DefaultDestinationTimeout = DefaultDestinationTimeout,
                GlobalMaxDestinationConcurrency = GlobalMaxDestinationConcurrency,
                FanOutMode = FanOutMode,
                EnqueueBlockTimeout = EnqueueBlockTimeout,
                ShutdownTimeout = ShutdownTimeout,
                ReconfigureDrainTimeout = ReconfigureDrainTimeout,
                SubscribeToExceptionMaster = SubscribeToExceptionMaster,
                MaxSuppressionEntries = MaxSuppressionEntries,
                MaxEscalationEntries = MaxEscalationEntries,
                SafeMetadataKeys = new ReadOnlyCollection<string>(new List<string>(SafeMetadataKeys ?? new string[0])),
                IncludeSafeStackTrace = IncludeSafeStackTrace
            };
        }
    }
}
