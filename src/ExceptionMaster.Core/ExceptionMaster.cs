using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ExceptionMaster.Core
{
    /// <summary>
    /// Central entry point for ExceptionMaster: a failure-contained exception logging,
    /// deduplication, and aggregation framework for classic .NET Framework and .NET Standard.
    /// </summary>
    public static class ExceptionMaster
    {
        private static readonly object ConfigSync = new object();
        private static readonly object SubscriberSync = new object();
        private static Action<ExceptionContext> _eventSubscribers;

        private static ExceptionMasterOptions _activeOptions;
        private static AggregationEngine _aggregationEngine;
        private static DispatchPipeline _pipeline;
        private static EventBroadcaster _broadcaster;
        private static bool _isInitialized;

        static ExceptionMaster()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Asynchronously notifies subscribers of observed exception contexts with bounded queuing,
        /// subscriber isolation, and recursion protection. Subscriptions are retained across Configure().
        /// </summary>
        public static event Action<ExceptionContext> ExceptionRaised
        {
            add
            {
                lock (SubscriberSync)
                {
                    _eventSubscribers += value;
                }
            }
            remove
            {
                lock (SubscriberSync)
                {
                    _eventSubscribers -= value;
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (Volatile.Read(ref _isInitialized)) return;

            lock (ConfigSync)
            {
                if (_isInitialized) return;

                var options = ExceptionMasterOptions.CreateDefault();
                InitializePipeline(options);
                _isInitialized = true;
            }
        }

        private static long _cumulativeLogged;
        private static long _cumulativeAggregated;
        private static long _cumulativeDropped;
        private static long _cumulativeDispatched;
        private static long _cumulativeSinkFailures;
        private static long _cumulativeFallbackInvocations;

        private static void InitializePipeline(ExceptionMasterOptions options)
        {
            _activeOptions = options;
            _aggregationEngine = new AggregationEngine(
                options.AggregationWindow,
                options.MaxAggregationEntries,
                options.CustomKeyGenerator);

            _pipeline = new DispatchPipeline(
                options,
                () => _aggregationEngine.GetPeriodicSummaryContexts(
                    DateTime.UtcNow,
                    options.AggregationPeriodicReportInterval,
                    options.AggregationPeriodicCountThreshold),
                () => _aggregationEngine.EvictExpired(DateTime.UtcNow));

            _broadcaster = new EventBroadcaster(
                () => { lock (SubscriberSync) return _eventSubscribers; },
                options.EventBroadcasterQueueCapacity);
        }

        /// <summary>
        /// Configures ExceptionMaster with custom options.
        /// Reconfiguration drains existing buffers and applies the new pipeline atomically,
        /// preserving all registered ExceptionRaised subscribers.
        /// </summary>
        public static void Configure(Action<ExceptionMasterOptions> configure)
        {
            if (configure == null) return;

            lock (ConfigSync)
            {
                var oldPipeline = _pipeline;
                var oldBroadcaster = _broadcaster;

                if (oldPipeline != null)
                {
                    var oldHealth = oldPipeline.GetHealth();
                    Interlocked.Add(ref _cumulativeLogged, oldHealth.TotalLogged);
                    Interlocked.Add(ref _cumulativeAggregated, oldHealth.TotalAggregated);
                    Interlocked.Add(ref _cumulativeDropped, oldHealth.TotalDropped);
                    Interlocked.Add(ref _cumulativeDispatched, oldHealth.TotalDispatched);
                    Interlocked.Add(ref _cumulativeSinkFailures, oldHealth.TotalSinkFailures);
                    Interlocked.Add(ref _cumulativeFallbackInvocations, oldHealth.TotalFallbackInvocations);
                }

                var options = new ExceptionMasterOptions();
                try
                {
                    configure(options);
                }
                catch
                {
                    // If configuration action fails, retain safe defaults
                    options = ExceptionMasterOptions.CreateDefault();
                }

                // Drain old pipeline before swapping
                try
                {
                    oldPipeline?.Flush(TimeSpan.FromSeconds(2));
                    oldPipeline?.Dispose();
                    oldBroadcaster?.Dispose();
                }
                catch
                {
                    // Safe swallow
                }

                InitializePipeline(options);
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Restores safe default configuration.
        /// </summary>
        public static void ConfigureDefaults()
        {
            Configure(opts => { });
        }

        /// <summary>
        /// Captures and logs an exception with automatic metadata extraction and aggregation.
        /// Guaranteed never to throw back to caller.
        /// </summary>
        public static void Log(
            Exception exception,
            [CallerMemberName] string caller = null,
            string category = null,
            string correlationId = null,
            bool isFatal = false)
        {
            if (exception == null) return;

            try
            {
                EnsureInitialized();

                var pipeline = _pipeline;
                if (pipeline == null || pipeline.IsShutdown) return;

                pipeline.IncrementLogged();

                DateTime utcNow = DateTime.UtcNow;
                string exceptionKey = _aggregationEngine.GenerateKey(exception, caller);
                bool isSubscriberContext = EventBroadcaster.IsCurrentContextInSubscriber;

                long windowTicks = _activeOptions.AggregationWindow.Ticks > 0
                    ? _activeOptions.AggregationWindow.Ticks
                    : TimeSpan.FromMinutes(5).Ticks;
                DateTime windowStartUtc = new DateTime((utcNow.Ticks / windowTicks) * windowTicks, DateTimeKind.Utc);

                if (_activeOptions.EnableAggregation)
                {
                    bool isFirstOccurrence = _aggregationEngine.RecordOccurrence(
                        exceptionKey,
                        utcNow,
                        isFatal,
                        () => CreateContext(
                            exception,
                            caller,
                            category,
                            correlationId,
                            isFatal,
                            exceptionKey,
                            utcNow,
                            utcNow,
                            1,
                            isSubscriberContext,
                            windowStartUtc,
                            1),
                        out var context);

                    if (isFirstOccurrence && context != null)
                    {
                        pipeline.Dispatch(context);
                        _broadcaster.TryEnqueue(context);
                    }
                    else
                    {
                        pipeline.IncrementAggregated();
                    }
                }
                else
                {
                    var context = CreateContext(
                        exception,
                        caller,
                        category,
                        correlationId,
                        isFatal,
                        exceptionKey,
                        utcNow,
                        utcNow,
                        1,
                        isSubscriberContext,
                        windowStartUtc,
                        1);

                    pipeline.Dispatch(context);
                    _broadcaster.TryEnqueue(context);
                }
            }
            catch
            {
                // Absolute failure containment: swallow any unexpected runtime error
            }
        }

        /// <summary>
        /// Logs an exception with explicit custom options.
        /// </summary>
        public static void Log(
            Exception exception,
            ExceptionLogOptions customOptions,
            [CallerMemberName] string caller = null)
        {
            if (exception == null) return;

            string category = customOptions?.Category;
            string correlationId = customOptions?.CorrelationId;
            bool isFatal = customOptions != null && customOptions.IsFatal;

            Log(exception, caller, category, correlationId, isFatal);
        }

        private static ExceptionContext CreateContext(
            Exception ex,
            string caller,
            string category,
            string correlationId,
            bool isFatal,
            string exceptionKey,
            DateTime firstSeenUtc,
            DateTime lastSeenUtc,
            long count,
            bool isFromSubscriber,
            DateTime? windowStartUtc = null,
            long? deltaCount = null)
        {
            return new ExceptionContext(
                exception: ex,
                message: ex.Message,
                exceptionType: ex.GetType().FullName,
                stackTrace: ex.StackTrace,
                firstSeenUtc: firstSeenUtc,
                lastSeenUtc: lastSeenUtc,
                count: count,
                caller: caller,
                category: category ?? "General",
                machineName: MetadataExtractor.MachineName,
                processName: MetadataExtractor.ProcessName,
                processId: MetadataExtractor.ProcessId,
                threadId: MetadataExtractor.CurrentThreadId,
                appVersion: MetadataExtractor.AppVersion,
                correlationId: correlationId ?? string.Empty,
                isFatal: isFatal,
                exceptionKey: exceptionKey,
                environmentName: MetadataExtractor.EnvironmentName,
                source: ex.Source,
                innerExceptionDetails: MetadataExtractor.FormatInnerExceptions(ex),
                occurrenceId: Guid.NewGuid(),
                schemaVersion: 1,
                isFromEventSubscriber: isFromSubscriber,
                windowStartUtc: windowStartUtc,
                deltaCount: deltaCount);
        }

        /// <summary>
        /// Drains all buffered events synchronously within the specified timeout.
        /// </summary>
        public static void Flush(TimeSpan? timeout = null)
        {
            try
            {
                var duration = timeout ?? _activeOptions?.ShutdownTimeout ?? TimeSpan.FromSeconds(5);
                _pipeline?.Flush(duration);
                _broadcaster?.Drain(duration);
            }
            catch
            {
                // Safe swallow
            }
        }

        /// <summary>
        /// Shuts down the ExceptionMaster pipeline, draining pending items and releasing sink resources.
        /// </summary>
        public static void Shutdown(TimeSpan? timeout = null)
        {
            try
            {
                var duration = timeout ?? _activeOptions?.ShutdownTimeout ?? TimeSpan.FromSeconds(5);
                _pipeline?.Shutdown(duration);
                _broadcaster?.Dispose();
            }
            catch
            {
                // Safe swallow
            }
        }

        /// <summary>
        /// Gets current health counters and diagnostics.
        /// </summary>
        public static ExceptionMasterHealth GetHealth()
        {
            try
            {
                var ph = _pipeline?.GetHealth();
                long totalLogged = (ph?.TotalLogged ?? 0) + Volatile.Read(ref _cumulativeLogged);
                long totalAggregated = (ph?.TotalAggregated ?? 0) + Volatile.Read(ref _cumulativeAggregated);
                long totalDropped = (ph?.TotalDropped ?? 0) + Volatile.Read(ref _cumulativeDropped);
                long totalDispatched = (ph?.TotalDispatched ?? 0) + Volatile.Read(ref _cumulativeDispatched);
                long totalSinkFailures = (ph?.TotalSinkFailures ?? 0) + Volatile.Read(ref _cumulativeSinkFailures);
                long totalFallbackInvocations = (ph?.TotalFallbackInvocations ?? 0) + Volatile.Read(ref _cumulativeFallbackInvocations);
                int queueDepth = ph?.PendingQueueDepth ?? 0;
                bool isShutdown = ph?.IsShutdown ?? true;

                return new ExceptionMasterHealth(
                    totalLogged,
                    totalAggregated,
                    totalDropped,
                    totalDispatched,
                    totalSinkFailures,
                    totalFallbackInvocations,
                    queueDepth,
                    isShutdown);
            }
            catch
            {
                return new ExceptionMasterHealth(0, 0, 0, 0, 0, 0, 0, true);
            }
        }
    }
}
