using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Destinations;
using ExceptionMaster.Alerting.Engine;
using ExceptionMaster.Alerting.Escalation;
using ExceptionMaster.Alerting.Generations;
using ExceptionMaster.Alerting.Infrastructure;
using ExceptionMaster.Alerting.Metrics;
using ExceptionMaster.Alerting.Models;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Alerting.Severity;
using ExceptionMaster.Alerting.Suppression;
using ExceptionMaster.Core;

namespace ExceptionMaster.Alerting
{
    /// <summary>
    /// Master public facade for ExceptionMaster.Alerting.
    /// Provides lifecycle management, configuration, event ingestion, and metrics collection.
    /// Never exposes raw exceptions, unredacted diagnostic contexts, or vendor dependencies.
    /// </summary>
    public static class Alerting
    {
        private static readonly object SyncLock = new object();
        private static readonly InternalMetricsCollector MetricsCollector = new InternalMetricsCollector();

        private static PipelineGenerationCoordinator? _coordinator;
        private static IDestinationConcurrencyLimiter? _concurrencyLimiter;
        private static BoundedSuppressionCache? _suppressionCache;
        private static BoundedEscalationTracker? _escalationTracker;
        private static ISeverityResolver? _severityResolver;
        private static AlertingOptions _currentOptions = new AlertingOptions();
        private static bool _isSubscribedToEM;
        private static int _isInitialized;

        /// <summary>
        /// Gets whether the Alerting pipeline is currently initialized and active.
        /// </summary>
        public static bool IsInitialized => Volatile.Read(ref _isInitialized) == 1;

        /// <summary>
        /// Initializes the Alerting engine with the specified configuration.
        /// Throws if already initialized. Use <see cref="Reconfigure"/> to update active configuration.
        /// </summary>
        public static void Initialize(Action<AlertingOptionsBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            lock (SyncLock)
            {
                if (IsInitialized)
                {
                    throw new InvalidOperationException("Alerting is already initialized. Use Alerting.Reconfigure() to update configuration.");
                }

                var builder = new AlertingOptionsBuilder();
                configure(builder);

                // Step 1: Validate Options
                var validation = DefaultAlertingOptionsValidator.Instance.Validate(builder.Options);
                if (!validation.IsValid)
                {
                    MetricsCollector.IncrementConfigurationErrors();
                    throw new InvalidOperationException($"Alerting configuration validation failed: {string.Join("; ", validation.Errors)}");
                }

                _currentOptions = builder.Options.Clone();

                // Step 2: Initialize Shared Process-Scoped Concurrency and State Trackers
                _concurrencyLimiter = new DestinationConcurrencyLimiter(builder.Options.GlobalMaxDestinationConcurrency);
                var clock = builder.Clock ?? DefaultSystemClock.Instance;
                _suppressionCache = new BoundedSuppressionCache(builder.Options.MaxSuppressionEntries, clock);
                _escalationTracker = new BoundedEscalationTracker(builder.Options.MaxEscalationEntries, clock);
                _severityResolver = builder.SeverityResolver ?? DefaultSeverityResolver.Instance;

                // Step 3: Create Generation 1 via Coordinator
                _coordinator = new PipelineGenerationCoordinator(genId => CreateGeneration(genId, builder, clock), MetricsCollector);

                // Step 4: Hook ExceptionMaster Subscription if enabled
                if (builder.Options.SubscribeToExceptionMaster)
                {
                    try
                    {
                        ExceptionMaster.Core.ExceptionMaster.ExceptionRaised += OnExceptionRaised;
                        _isSubscribedToEM = true;
                    }
                    catch
                    {
                        // Failure containment: never let subscription wiring crash
                    }
                }

                Volatile.Write(ref _isInitialized, 1);
            }
        }

        /// <summary>
        /// Atomically applies a new configuration snapshot, swapping active generation and draining superseded workers.
        /// </summary>
        public static void Reconfigure(Action<AlertingOptionsBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            lock (SyncLock)
            {
                if (!IsInitialized || _coordinator == null)
                {
                    throw new InvalidOperationException("Alerting has not been initialized. Call Alerting.Initialize() first.");
                }

                var builder = new AlertingOptionsBuilder(_currentOptions);
                configure(builder);

                var validation = DefaultAlertingOptionsValidator.Instance.Validate(builder.Options);
                if (!validation.IsValid)
                {
                    MetricsCollector.IncrementConfigurationErrors();
                    throw new InvalidOperationException($"Alerting configuration validation failed: {string.Join("; ", validation.Errors)}");
                }

                _currentOptions = builder.Options.Clone();
                var clock = builder.Clock ?? DefaultSystemClock.Instance;

                // Advance generation: creates Generation N+1 and initiates bounded drain of Generation N
                _coordinator.AdvanceGeneration(
                    genId => CreateGeneration(genId, builder, clock),
                    builder.Options.ReconfigureDrainTimeout);

                MetricsCollector.IncrementReconfigurationCount();
            }
        }

        /// <summary>
        /// Primary ingestion entry point. Enqueues an ExceptionMaster event for asynchronous policy evaluation and delivery.
        /// Never blocks the caller indefinitely and never throws into the producer.
        /// </summary>
        public static void Ingest(ExceptionContext context)
        {
            if (context == null) return;

            if (!IsInitialized)
            {
                MetricsCollector.IncrementEventsDropped();
                return;
            }

            try
            {
                MetricsCollector.IncrementEventsReceived();

                var resolver = _severityResolver ?? DefaultSeverityResolver.Instance;
                var severity = resolver.Resolve(context, null, _currentOptions.DefaultSeverity);

                var coordinator = _coordinator;
                if (coordinator != null)
                {
                    if (coordinator.TryAdmit(context, severity, out _))
                    {
                        return;
                    }

                    // Drop was already recorded by coordinator / active generation
                    return;
                }

                MetricsCollector.IncrementEventsDropped();
            }
            catch
            {
                // Absolute failure isolation: never throw into host application or ExceptionMaster
                MetricsCollector.IncrementEventsDropped();
            }
        }

        /// <summary>
        /// Asynchronously flushes all pending admitted events across active and draining generations
        /// up to the specified bounded timeout or cancellation token.
        /// </summary>
        public static async Task<ExceptionMaster.Alerting.Models.FlushResult> FlushAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsInitialized) return new ExceptionMaster.Alerting.Models.FlushResult(true, 0, 0, TimeSpan.Zero);

            var coordinator = _coordinator;
            if (coordinator == null) return new ExceptionMaster.Alerting.Models.FlushResult(true, 0, 0, TimeSpan.Zero);

            var flushTimeout = timeout ?? _currentOptions.ShutdownTimeout;
            try
            {
                return await coordinator.FlushAsync(flushTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ExceptionMaster.Alerting.Models.FlushResult(false, 0, 0, TimeSpan.Zero);
            }
            catch
            {
                return new ExceptionMaster.Alerting.Models.FlushResult(false, 0, 0, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Synchronously flushes all pending admitted events across active and draining generations up to the specified bounded timeout.
        /// </summary>
        public static ExceptionMaster.Alerting.Models.FlushResult Flush(TimeSpan? timeout = null)
        {
            return FlushAsync(timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gracefully shuts down the Alerting engine, stops admissions, unhooks subscribers, drains active work,
        /// and releases all generation and destination concurrency resources.
        /// Idempotent and safe to invoke multiple times.
        /// </summary>
        public static void Shutdown(TimeSpan? timeout = null)
        {
            ShutdownAsync(timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously shuts down the Alerting engine, stops admissions, unhooks subscribers, drains active work,
        /// and releases all generation and destination concurrency resources.
        /// Idempotent and safe to invoke multiple times.
        /// </summary>
        public static async Task ShutdownAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            PipelineGenerationCoordinator? coordinatorToDispose = null;
            IDestinationConcurrencyLimiter? limiterToDispose = null;

            lock (SyncLock)
            {
                if (!IsInitialized) return;
                Volatile.Write(ref _isInitialized, 0);

                if (_isSubscribedToEM)
                {
                    try
                    {
                        ExceptionMaster.Core.ExceptionMaster.ExceptionRaised -= OnExceptionRaised;
                    }
                    catch
                    {
                        // Safe containment
                    }
                    _isSubscribedToEM = false;
                }

                coordinatorToDispose = _coordinator;
                _coordinator = null;

                limiterToDispose = _concurrencyLimiter;
                _concurrencyLimiter = null;

                _suppressionCache = null;
                _escalationTracker = null;
                _severityResolver = null;
            }

            var shutdownTimeout = timeout ?? _currentOptions.ShutdownTimeout;
            if (coordinatorToDispose != null)
            {
                try
                {
                    await coordinatorToDispose.FlushAsync(shutdownTimeout, cancellationToken).ConfigureAwait(false);
                    coordinatorToDispose.Dispose();
                }
                catch
                {
                    // Safe containment
                }
            }

            if (limiterToDispose is IDisposable disposableLimiter)
            {
                try
                {
                    disposableLimiter.Dispose();
                }
                catch
                {
                    // Safe containment
                }
            }
        }

        /// <summary>
        /// Retrieves an immutable point-in-time snapshot of runtime health, throughput, queue depth, and delivery outcomes.
        /// </summary>
        public static AlertingMetrics GetMetrics()
        {
            int queueDepth = 0;
            var coordinator = _coordinator;
            if (coordinator?.ActiveGeneration is PipelineGeneration activePipe)
            {
                queueDepth = activePipe.QueueDepth;
            }

            return MetricsCollector.CreateSnapshot(queueDepth);
        }

        /// <summary>
        /// Resets all internal metric counters. Primarily intended for testing.
        /// </summary>
        internal static void ResetMetrics()
        {
            MetricsCollector.Reset();
        }

        private static void OnExceptionRaised(ExceptionContext context)
        {
            try
            {
                Ingest(context);
            }
            catch
            {
                // Isolation: never escape into ExceptionMaster
            }
        }

        private static IPipelineGeneration CreateGeneration(
            long generationId,
            AlertingOptionsBuilder builder,
            ISystemClock clock)
        {
            var destRegistry = new DestinationRegistry(builder.Destinations);
            var groupRegistry = new DestinationGroupRegistry(builder.Groups);
            var fallbackRegistry = new FallbackChainRegistry(builder.FallbackChains);

            var suppRegistry = new SuppressionPolicyRegistry();
            for (int i = 0; i < builder.SuppressionPolicies.Count; i++)
            {
                suppRegistry.Register(builder.SuppressionPolicies[i]);
            }

            var escRegistry = new EscalationPolicyRegistry();
            for (int i = 0; i < builder.EscalationPolicies.Count; i++)
            {
                escRegistry.Register(builder.EscalationPolicies[i]);
            }

            // Validate Destination, Group, and Fallback Referential Integrity
            var destValidation = DestinationValidator.Validate(destRegistry, groupRegistry, fallbackRegistry);
            if (!destValidation.IsValid)
            {
                MetricsCollector.IncrementConfigurationErrors();
                throw new InvalidOperationException($"Destination referential integrity validation failed: {string.Join("; ", destValidation.Errors)}");
            }

            var ruleEngine = new RuleEngine(builder.Rules);

            var limiter = _concurrencyLimiter ?? new DestinationConcurrencyLimiter(builder.Options.GlobalMaxDestinationConcurrency);
            var fanOutDispatcher = new FanOutDispatcher(
                destRegistry,
                groupRegistry,
                builder.Options.FanOutMode,
                builder.Options.DefaultDestinationTimeout,
                builder.Options.GlobalMaxDestinationConcurrency,
                limiter);

            var fallbackDispatcher = new FallbackDispatcher(
                destRegistry,
                fallbackRegistry,
                builder.Options.DefaultDestinationTimeout,
                builder.Options.GlobalMaxDestinationConcurrency,
                limiter);

            var suppressionCache = _suppressionCache ?? new BoundedSuppressionCache(builder.Options.MaxSuppressionEntries, clock);
            var escalationTracker = _escalationTracker ?? new BoundedEscalationTracker(builder.Options.MaxEscalationEntries, clock);
            var coordinator = new RuleExecutionCoordinator(suppressionCache, escalationTracker, suppRegistry, escRegistry, clock);

            return new PipelineGeneration(
                generationId: generationId,
                options: builder.Options,
                ruleEngine: ruleEngine,
                coordinator: coordinator,
                destinationRegistry: destRegistry,
                groupRegistry: groupRegistry,
                fallbackRegistry: fallbackRegistry,
                fanOutDispatcher: fanOutDispatcher,
                fallbackDispatcher: fallbackDispatcher,
                metrics: MetricsCollector);
        }
    }
}
