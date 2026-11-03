using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Destinations;
using ExceptionMaster.Alerting.Engine;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Metrics;
using ExceptionMaster.Alerting.Models;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Core;
using QueueOverflowPolicy = ExceptionMaster.Alerting.Enums.QueueOverflowPolicy;
using FallbackChain = ExceptionMaster.Alerting.Destinations.FallbackChain;

namespace ExceptionMaster.Alerting.Generations
{
    /// <summary>
    /// Represents an immutable pipeline generation instance owning its dedicated ingestion queue,
    /// worker tasks, rule engine, and destination dispatchers.
    /// Strictly guarantees that an admitted event belongs to exactly one pipeline generation.
    /// </summary>
    public sealed class PipelineGeneration : IPipelineGeneration
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));

        private readonly ConcurrentQueue<IAdmittedEvent> _queue;
        private readonly ConcurrentDictionary<Guid, IAdmittedEvent> _pendingEvents;
        private readonly SemaphoreSlim _workSignal;
        private readonly SemaphoreSlim _capacitySignal;
        private readonly CancellationTokenSource _cts;
        private readonly Task[] _workerTasks;
        private readonly object _stateLock = new object();

        private readonly IRuleEngine _ruleEngine;
        private readonly IRuleExecutionCoordinator _coordinator;
        private readonly IDestinationRegistry _destinationRegistry;
        private readonly IDestinationGroupRegistry _groupRegistry;
        private readonly IFallbackChainRegistry _fallbackRegistry;
        private readonly IFanOutDispatcher _fanOutDispatcher;
        private readonly IFallbackDispatcher _fallbackDispatcher;
        private readonly InternalMetricsCollector _metrics;

        private int _inFlightProcessing;
        private int _isDisposed;
        private GenerationState _state;

        public long GenerationId { get; }
        public DateTime CreatedUtc { get; }
        public AlertingOptions Options { get; }

        public GenerationState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _state = value;
                }
            }
        }

        public int QueueDepth => _queue.Count;

        public PipelineGeneration(
            long generationId,
            AlertingOptions options,
            IRuleEngine ruleEngine,
            IRuleExecutionCoordinator coordinator,
            IDestinationRegistry destinationRegistry,
            IDestinationGroupRegistry groupRegistry,
            IFallbackChainRegistry fallbackRegistry,
            IFanOutDispatcher fanOutDispatcher,
            IFallbackDispatcher fallbackDispatcher,
            InternalMetricsCollector metrics,
            DateTime? createdUtc = null)
        {
            if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId), "Generation ID must be positive.");
            GenerationId = generationId;
            Options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _destinationRegistry = destinationRegistry ?? throw new ArgumentNullException(nameof(destinationRegistry));
            _groupRegistry = groupRegistry ?? throw new ArgumentNullException(nameof(groupRegistry));
            _fallbackRegistry = fallbackRegistry ?? throw new ArgumentNullException(nameof(fallbackRegistry));
            _fanOutDispatcher = fanOutDispatcher ?? throw new ArgumentNullException(nameof(fanOutDispatcher));
            _fallbackDispatcher = fallbackDispatcher ?? throw new ArgumentNullException(nameof(fallbackDispatcher));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

            CreatedUtc = createdUtc?.Kind == DateTimeKind.Utc ? createdUtc.Value : DateTime.UtcNow;
            _state = GenerationState.Active;

            _queue = new ConcurrentQueue<IAdmittedEvent>();
            _pendingEvents = new ConcurrentDictionary<Guid, IAdmittedEvent>();
            _workSignal = new SemaphoreSlim(0);
            _capacitySignal = new SemaphoreSlim(Options.MaxQueueCapacity, Options.MaxQueueCapacity);
            _cts = new CancellationTokenSource();

            int workers = Math.Max(1, Options.WorkerConcurrency);
            _workerTasks = new Task[workers];
            for (int i = 0; i < workers; i++)
            {
                _workerTasks[i] = Task.Run(ProcessQueueAsync);
            }
        }

        public bool TryAdmit(ExceptionContext context, AlertSeverity resolvedSeverity, out IAdmittedEvent? admittedEvent)
        {
            return TryAdmitInternal(context, resolvedSeverity, out admittedEvent) == AdmissionStatus.Admitted;
        }

        internal AdmissionStatus TryAdmitInternal(ExceptionContext context, AlertSeverity resolvedSeverity, out IAdmittedEvent? admittedEvent)
        {
            admittedEvent = null;
            if (context == null) return AdmissionStatus.RejectedOverflow;

            lock (_stateLock)
            {
                if (_state != GenerationState.Active)
                {
                    return AdmissionStatus.GenerationNotActive;
                }
            }

            // Enforce bounded admission according to configured OverflowPolicy
            if (Options.OverflowPolicy == QueueOverflowPolicy.DropNewest)
            {
                if (!_capacitySignal.Wait(0))
                {
                    lock (_stateLock)
                    {
                        if (_state != GenerationState.Active)
                        {
                            return AdmissionStatus.GenerationNotActive;
                        }
                    }

                    _metrics.IncrementEventsDropped();
                    return AdmissionStatus.RejectedOverflow;
                }
            }
            else if (Options.OverflowPolicy == QueueOverflowPolicy.BlockWithTimeout)
            {
                int timeoutMs = (int)Math.Max(0, Options.EnqueueBlockTimeout.TotalMilliseconds);
                if (!_capacitySignal.Wait(timeoutMs))
                {
                    lock (_stateLock)
                    {
                        if (_state != GenerationState.Active)
                        {
                            return AdmissionStatus.GenerationNotActive;
                        }
                    }

                    _metrics.IncrementEventsDropped();
                    return AdmissionStatus.RejectedOverflow;
                }
            }
            else if (Options.OverflowPolicy == QueueOverflowPolicy.DropOldest)
            {
                if (!_capacitySignal.Wait(0))
                {
                    lock (_stateLock)
                    {
                        if (_state != GenerationState.Active)
                        {
                            return AdmissionStatus.GenerationNotActive;
                        }
                    }

                    // Evict oldest item from queue to make room
                    if (_queue.TryDequeue(out var evicted))
                    {
                        _metrics.IncrementEventsDropped();
                        _pendingEvents.TryRemove(evicted.AdmissionTicketId, out _);
                        evicted.MarkCompleted();
                    }
                    else
                    {
                        // Could not dequeue, take permit
                        _capacitySignal.Wait(0);
                    }
                }
            }

            // Linearization guarantee: re-check Active state under lock before committing admission
            lock (_stateLock)
            {
                if (_state != GenerationState.Active)
                {
                    // Generation stopped accepting admissions while waiting for capacity!
                    try
                    {
                        _capacitySignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // Safe containment
                    }
                    return AdmissionStatus.GenerationNotActive;
                }

                // Create immutable single-generation admission ticket
                var admitted = new AdmittedEvent(GenerationId, context, resolvedSeverity);
                _queue.Enqueue(admitted);
                _pendingEvents[admitted.AdmissionTicketId] = admitted;
                _workSignal.Release();

                _metrics.IncrementEventsAdmitted();
                admittedEvent = admitted;
                return AdmissionStatus.Admitted;
            }
        }

        public void StopAcceptingAdmissions()
        {
            lock (_stateLock)
            {
                if (_state == GenerationState.Active)
                {
                    _state = GenerationState.Draining;
                }
            }
        }

        public async Task<ExceptionMaster.Alerting.Models.FlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Snapshot all admitted events that have not yet completed at this exact flush boundary
            var targetEvents = _pendingEvents.Values.ToArray();
            if (targetEvents.Length == 0)
            {
                sw.Stop();
                return new ExceptionMaster.Alerting.Models.FlushResult(true, 0, 0, sw.Elapsed);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero && timeout != TimeSpan.MaxValue)
            {
                linkedCts.CancelAfter(timeout);
            }

            try
            {
                var tasks = new Task[targetEvents.Length];
                for (int i = 0; i < targetEvents.Length; i++)
                {
                    tasks[i] = targetEvents[i].CompletionTask;
                }

                var whenAllTask = Task.WhenAll(tasks);
                if (!linkedCts.Token.IsCancellationRequested)
                {
                    var cancelTcs = new TaskCompletionSource<object?>();
                    using (linkedCts.Token.Register(s => ((TaskCompletionSource<object?>?)s)?.TrySetResult(null), cancelTcs))
                    {
                        await Task.WhenAny(whenAllTask, cancelTcs.Task).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or caller cancellation reached
            }
            catch
            {
                // Safe containment
            }

            sw.Stop();

            int completed = 0;
            for (int i = 0; i < targetEvents.Length; i++)
            {
                if (targetEvents[i].IsCompleted)
                {
                    completed++;
                }
            }

            int pending = targetEvents.Length - completed;
            bool success = pending == 0 && !linkedCts.Token.IsCancellationRequested;

            if (!success)
            {
                _metrics.IncrementFlushTimeouts();
            }
            else
            {
                _metrics.IncrementFlushOperationsCompleted();
            }

            return new ExceptionMaster.Alerting.Models.FlushResult(success, completed, pending, sw.Elapsed);
        }

        public async Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (_state == GenerationState.Disposed) return;
                _state = GenerationState.Draining;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero && timeout != TimeSpan.MaxValue)
            {
                linkedCts.CancelAfter(timeout);
            }

            try
            {
                // Wait until queue is empty and in-flight workers finish
                while (!_queue.IsEmpty || Volatile.Read(ref _inFlightProcessing) > 0)
                {
                    if (linkedCts.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    await Task.Delay(20, linkedCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or caller cancellation reached
            }

            // Drop any uncompleted events remaining in the queue when drain timeout expires
            int droppedCount = 0;
            while (_queue.TryDequeue(out var undrained))
            {
                droppedCount++;
                _pendingEvents.TryRemove(undrained.AdmissionTicketId, out _);
                undrained.MarkCompleted();
            }

            if (droppedCount > 0)
            {
                _metrics.AddEventsDropped(droppedCount);
                _metrics.AddReconfigurationDroppedEvents(droppedCount);
                _metrics.IncrementReconfigurationTimeouts();
            }

            Dispose();
        }

        private async Task ProcessQueueAsync()
        {
            var token = _cts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _workSignal.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_queue.TryDequeue(out var admittedEvent))
                {
                    try
                    {
                        _capacitySignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // Safe containment
                    }

                    if (admittedEvent.TryClaimForProcessing())
                    {
                        Interlocked.Increment(ref _inFlightProcessing);
                        try
                        {
                            await ProcessEventAsync(admittedEvent, token).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Failure isolation: never crash worker task
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _inFlightProcessing);
                            _pendingEvents.TryRemove(admittedEvent.AdmissionTicketId, out _);
                            admittedEvent.MarkCompleted();
                        }
                    }
                    else
                    {
                        _pendingEvents.TryRemove(admittedEvent.AdmissionTicketId, out _);
                        admittedEvent.MarkCompleted();
                    }
                }
            }
        }

        private async Task ProcessEventAsync(IAdmittedEvent admittedEvent, CancellationToken cancellationToken)
        {
            var context = admittedEvent.Context;
            var evalContext = RuleEvaluationContext.FromExceptionContext(context, admittedEvent.ResolvedSeverity);

            // Step 1: Rule Engine evaluates matching rules in deterministic ascending Order
            IReadOnlyList<AlertRule> matchingRules;
            try
            {
                matchingRules = _ruleEngine.Evaluate(evalContext);
                _metrics.AddRulesEvaluated(_ruleEngine.Rules.Count);
                _metrics.AddRulesMatched(matchingRules.Count);
            }
            catch
            {
                _metrics.IncrementProcessingFailures();
                return;
            }

            // Step 2: All matching rules execute in deterministic order
            for (int r = 0; r < matchingRules.Count; r++)
            {
                var rule = matchingRules[r];
                try
                {
                    // Step 3: Critical order: Escalation -> Suppression -> Destination Dispatch
                    var decision = _coordinator.Evaluate(rule, evalContext);

                    if (decision.ThresholdCrossed)
                    {
                        _metrics.IncrementAlertsEscalated();
                    }

                    if (!decision.IsPermitted)
                    {
                        _metrics.IncrementAlertsSuppressed();
                        continue;
                    }

                    // Step 4: Safe AlertEnvelope Projection (Never expose raw Exception or unredacted metadata)
                    var envelope = ProjectSafeEnvelope(context, admittedEvent.ResolvedSeverity, decision, evalContext);

                    // Step 5: Deliver to Effective Destinations and Groups
                    await DeliverToDestinationsAsync(envelope, decision, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _metrics.IncrementProcessingFailures();
                    // One rule failure must not terminate other matching rules
                }
            }
        }

        private AlertEnvelope ProjectSafeEnvelope(
            ExceptionContext context,
            AlertSeverity resolvedSeverity,
            RuleExecutionDecision decision,
            RuleEvaluationContext evalContext)
        {
            IReadOnlyDictionary<string, string> safeMetadata = EmptyMetadata;
            if (Options.SafeMetadataKeys != null && Options.SafeMetadataKeys.Count > 0 && evalContext.Metadata.Count > 0)
            {
                var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in Options.SafeMetadataKeys)
                {
                    if (evalContext.Metadata.TryGetValue(key, out var val))
                    {
                        filtered[key] = val;
                    }
                }
                safeMetadata = new ReadOnlyDictionary<string, string>(filtered);
            }

            string? safeStackTrace = null;
            if (Options.IncludeSafeStackTrace && context.Exception != null)
            {
                try
                {
                    safeStackTrace = context.Exception.StackTrace;
                }
                catch
                {
                    safeStackTrace = null;
                }
            }

            return new AlertEnvelope(
                alertId: Guid.NewGuid(),
                occurrenceId: context.OccurrenceId,
                exceptionKey: context.ExceptionKey,
                exceptionType: context.ExceptionType,
                safeMessage: context.Message,
                severity: resolvedSeverity,
                category: context.Category,
                source: context.Source,
                correlationId: context.CorrelationId,
                environmentName: context.EnvironmentName,
                machineName: context.MachineName,
                processName: context.ProcessName,
                processId: context.ProcessId,
                isFatal: context.IsFatal,
                count: decision.OccurrenceCount,
                firstSeenUtc: decision.FirstOccurrenceUtc ?? context.FirstSeenUtc,
                lastSeenUtc: decision.LastOccurrenceUtc ?? context.LastSeenUtc,
                generatedUtc: DateTime.UtcNow,
                safeMetadata: safeMetadata,
                safeStackTrace: safeStackTrace);
        }

        private async Task DeliverToDestinationsAsync(
            AlertEnvelope envelope,
            RuleExecutionDecision decision,
            CancellationToken cancellationToken)
        {
            var destinationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect direct target destinations
            for (int i = 0; i < decision.EffectiveDestinations.Count; i++)
            {
                destinationIds.Add(decision.EffectiveDestinations[i]);
            }

            // Resolve target groups into individual destinations
            for (int g = 0; g < decision.EffectiveGroups.Count; g++)
            {
                var group = _groupRegistry.FindGroup(decision.EffectiveGroups[g]);
                if (group != null)
                {
                    for (int d = 0; d < group.DestinationNames.Count; d++)
                    {
                        destinationIds.Add(group.DestinationNames[d]);
                    }
                }
            }

            if (destinationIds.Count == 0) return;

            // Separate fallback chains from direct destinations
            var directDestinations = new List<string>();
            var fallbackChains = new List<FallbackChain>();

            foreach (var id in destinationIds)
            {
                var chain = _fallbackRegistry.FindChain(id);
                if (chain != null)
                {
                    fallbackChains.Add(chain);
                }
                else
                {
                    directDestinations.Add(id);
                }
            }

            // Fan-out to direct destinations
            if (directDestinations.Count > 0)
            {
                try
                {
                    var result = await _fanOutDispatcher.DispatchAsync(
                        envelope,
                        directDestinations,
                        cancellationToken).ConfigureAwait(false);

                    int succeeded = result.Results.Count(r => r.Success);
                    int failed = result.Results.Count - succeeded;

                    _metrics.AddDestinationAttempts(result.Results.Count);
                    _metrics.AddDestinationSuccesses(succeeded);
                    _metrics.AddDestinationFailures(failed);
                }
                catch
                {
                    _metrics.AddDestinationFailures(directDestinations.Count);
                }
            }

            // Dispatch fallback chains
            for (int c = 0; c < fallbackChains.Count; c++)
            {
                try
                {
                    _metrics.IncrementFallbackAttempts();
                    var fbResult = await _fallbackDispatcher.DispatchAsync(
                        envelope,
                        fallbackChains[c],
                        cancellationToken).ConfigureAwait(false);

                    int attempts = fbResult.AttemptResults.Count;
                    _metrics.AddDestinationAttempts(attempts);

                    if (fbResult.Succeeded)
                    {
                        _metrics.IncrementFallbackSuccesses();
                        _metrics.IncrementDestinationSuccesses();
                        _metrics.AddDestinationFailures(attempts - 1);
                    }
                    else
                    {
                        _metrics.IncrementFallbackFailures();
                        _metrics.AddDestinationFailures(attempts);
                    }
                }
                catch
                {
                    _metrics.IncrementFallbackFailures();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;

            lock (_stateLock)
            {
                _state = GenerationState.Disposed;
            }

            try
            {
                _cts.Cancel();
            }
            catch
            {
                // Safe containment
            }

            _workSignal.Dispose();
            _capacitySignal.Dispose();
            _cts.Dispose();
        }
    }
}
