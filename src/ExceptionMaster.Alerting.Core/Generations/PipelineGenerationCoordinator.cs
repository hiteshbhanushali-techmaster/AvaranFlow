using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Enums;

namespace ExceptionMaster.Alerting.Generations
{
    /// <summary>
    /// Thread-safe coordinator managing pipeline generations, establishing the atomic admission
    /// linearization swap point and coordinating bounded asynchronous drain of superseded generations.
    /// </summary>
    public sealed class PipelineGenerationCoordinator : IPipelineGenerationCoordinator
    {
        private readonly object _swapLock = new object();
        private readonly ConcurrentDictionary<long, IPipelineGeneration> _drainingGenerations =
            new ConcurrentDictionary<long, IPipelineGeneration>();

        private IPipelineGeneration _activeGeneration;
        private readonly ExceptionMaster.Alerting.Metrics.InternalMetricsCollector? _metrics;
        private long _currentGenerationId;
        private int _isDisposed;

        public IPipelineGeneration ActiveGeneration
        {
            get
            {
                lock (_swapLock)
                {
                    return _activeGeneration;
                }
            }
        }

        public long CurrentGenerationId => Volatile.Read(ref _currentGenerationId);

        public PipelineGenerationCoordinator(
            Func<long, IPipelineGeneration> initialGenerationFactory,
            ExceptionMaster.Alerting.Metrics.InternalMetricsCollector? metrics = null)
        {
            if (initialGenerationFactory == null) throw new ArgumentNullException(nameof(initialGenerationFactory));

            _metrics = metrics;
            _currentGenerationId = 1;
            _activeGeneration = initialGenerationFactory(1) ?? throw new InvalidOperationException("Generation factory returned null.");
        }

        public IPipelineGeneration AdvanceGeneration(Func<long, IPipelineGeneration> generationFactory, TimeSpan drainTimeout)
        {
            if (generationFactory == null) throw new ArgumentNullException(nameof(generationFactory));
            if (Volatile.Read(ref _isDisposed) == 1)
                throw new ObjectDisposedException(nameof(PipelineGenerationCoordinator));

            lock (_swapLock)
            {
                long nextId = Interlocked.Increment(ref _currentGenerationId);
                var oldGen = _activeGeneration;
                var newGen = generationFactory(nextId) ?? throw new InvalidOperationException("Generation factory returned null.");

                // Atomically swap active generation
                _activeGeneration = newGen;

                // Stop admissions on superseded generation immediately
                oldGen?.StopAcceptingAdmissions();

                if (oldGen != null)
                {
                    _drainingGenerations.TryAdd(oldGen.GenerationId, oldGen);

                    // Bounded asynchronous background drain of superseded generation
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await oldGen.DrainAsync(drainTimeout).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Failure containment
                        }
                        finally
                        {
                            _drainingGenerations.TryRemove(oldGen.GenerationId, out _);
                        }
                    });
                }

                return newGen;
            }
        }

        public bool TryAdmit(ExceptionMaster.Core.ExceptionContext context, ExceptionMaster.Alerting.Enums.AlertSeverity resolvedSeverity, out IAdmittedEvent? admittedEvent)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var gen = ActiveGeneration;
                if (gen == null) break;

                if (gen is PipelineGeneration pg)
                {
                    var status = pg.TryAdmitInternal(context, resolvedSeverity, out admittedEvent);
                    if (status == AdmissionStatus.Admitted)
                    {
                        return true;
                    }
                    if (status == AdmissionStatus.RejectedOverflow)
                    {
                        // Active generation rejected and recorded drop according to its overflow policy
                        admittedEvent = null;
                        return false;
                    }

                    // GenerationNotActive: concurrent swap retired this generation; retry with new ActiveGeneration!
                    continue;
                }
                else
                {
                    if (gen.TryAdmit(context, resolvedSeverity, out admittedEvent))
                    {
                        return true;
                    }

                    if (gen.State != GenerationState.Active)
                    {
                        continue;
                    }

                    admittedEvent = null;
                    return false;
                }
            }

            // No active generation could admit the event (e.g. during shutdown / drained)
            _metrics?.IncrementEventsDropped();
            admittedEvent = null;
            return false;
        }

        /// <summary>
        /// Flushes all active and draining generations within the allotted timeout or cancellation.
        /// </summary>
        public async Task<ExceptionMaster.Alerting.Models.FlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero && timeout != TimeSpan.MaxValue)
            {
                linkedCts.CancelAfter(timeout);
            }

            int totalCompleted = 0;
            int totalPending = 0;
            bool allSuccess = true;

            // 1. Flush active generation
            var active = ActiveGeneration;
            if (active != null)
            {
                var activeResult = await active.FlushAsync(timeout, linkedCts.Token).ConfigureAwait(false);
                totalCompleted += activeResult.CompletedEvents;
                totalPending += activeResult.PendingEvents;
                allSuccess &= activeResult.Success;
            }

            // 2. Flush remaining draining generations
            foreach (var kvp in _drainingGenerations)
            {
                if (linkedCts.Token.IsCancellationRequested)
                {
                    allSuccess = false;
                    break;
                }

                try
                {
                    var drainResult = await kvp.Value.FlushAsync(timeout, linkedCts.Token).ConfigureAwait(false);
                    totalCompleted += drainResult.CompletedEvents;
                    totalPending += drainResult.PendingEvents;
                    allSuccess &= drainResult.Success;
                }
                catch
                {
                    allSuccess = false;
                }
            }

            sw.Stop();
            return new ExceptionMaster.Alerting.Models.FlushResult(allSuccess, totalCompleted, totalPending, sw.Elapsed);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;

            lock (_swapLock)
            {
                try
                {
                    _activeGeneration?.Dispose();
                }
                catch
                {
                    // Safe containment
                }

                foreach (var kvp in _drainingGenerations)
                {
                    try
                    {
                        kvp.Value.Dispose();
                    }
                    catch
                    {
                        // Safe containment
                    }
                }
                _drainingGenerations.Clear();
            }
        }
    }
}
