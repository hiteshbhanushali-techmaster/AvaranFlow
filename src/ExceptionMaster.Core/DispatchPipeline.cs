using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ExceptionMaster.Core
{
    public sealed class DispatchPipeline : IDisposable
    {
        private sealed class QueueItem
        {
            public ExceptionContext Context { get; }
            public bool HoldsCapacityPermit { get; }

            public QueueItem(ExceptionContext context, bool holdsCapacityPermit)
            {
                Context = context;
                HoldsCapacityPermit = holdsCapacityPermit;
            }
        }

        private readonly ExceptionMasterOptions _options;
        private readonly ConcurrentQueue<QueueItem> _queue;
        private readonly SemaphoreSlim _signal;
        private readonly SemaphoreSlim _capacityThrottle;
        private readonly CancellationTokenSource _cts;
        private readonly Task _workerTask;
        private readonly Timer _flushTimer;
        private readonly Func<IReadOnlyList<ExceptionContext>> _periodicSummaryProvider;
        private readonly Func<IReadOnlyList<ExceptionContext>> _evictionProvider;
        private int _inFlightDispatches;

        private long _totalLogged;
        private long _totalAggregated;
        private long _totalDropped;
        private long _totalDispatched;
        private long _totalSinkFailures;
        private long _totalFallbackInvocations;

        private int _isDisposed;
        private int _isShutdown;

        public bool IsShutdown => Volatile.Read(ref _isShutdown) == 1;

        public DispatchPipeline(
            ExceptionMasterOptions options,
            Func<IReadOnlyList<ExceptionContext>> periodicSummaryProvider = null,
            Func<IReadOnlyList<ExceptionContext>> evictionProvider = null)
        {
            _options = options ?? ExceptionMasterOptions.CreateDefault();
            _periodicSummaryProvider = periodicSummaryProvider;
            _evictionProvider = evictionProvider;

            _queue = new ConcurrentQueue<QueueItem>();
            _signal = new SemaphoreSlim(0, 1);
            _capacityThrottle = new SemaphoreSlim(_options.BufferCapacity, _options.BufferCapacity);
            _cts = new CancellationTokenSource();

            if (_options.EnableAsyncDispatch)
            {
                _workerTask = Task.Run(ProcessQueueAsync);
                _flushTimer = new Timer(OnTimerFlush, null, _options.FlushInterval, _options.FlushInterval);
            }
        }

        public void IncrementLogged() => Interlocked.Increment(ref _totalLogged);
        public void IncrementAggregated() => Interlocked.Increment(ref _totalAggregated);

        /// <summary>
        /// Dispatches an exception context into the pipeline.
        /// </summary>
        public bool Dispatch(ExceptionContext context)
        {
            if (context == null || IsShutdown)
            {
                return false;
            }

            if (!_options.EnableAsyncDispatch)
            {
                // Synchronous dispatch critical path
                ExecuteSyncDispatch(context);
                return true;
            }

            // Asynchronous queue path with bounded overflow policy
            if (context.IsFatal)
            {
                // Fatal bypass: always prioritize fatal events into the queue without consuming capacity permit
                _queue.Enqueue(new QueueItem(context, false));
                SignalWorker();
                return true;
            }

            switch (_options.OverflowPolicy)
            {
                case QueueOverflowPolicy.DropNewest:
                    if (_queue.Count >= _options.BufferCapacity)
                    {
                        Interlocked.Increment(ref _totalDropped);
                        return false;
                    }
                    _queue.Enqueue(new QueueItem(context, false));
                    SignalWorker();
                    return true;

                case QueueOverflowPolicy.DropOldest:
                    while (_queue.Count >= _options.BufferCapacity && _queue.TryDequeue(out var evicted))
                    {
                        if (evicted.HoldsCapacityPermit)
                        {
                            try { _capacityThrottle.Release(); } catch (SemaphoreFullException) { }
                        }
                        Interlocked.Increment(ref _totalDropped);
                    }
                    _queue.Enqueue(new QueueItem(context, false));
                    SignalWorker();
                    return true;

                case QueueOverflowPolicy.BlockWithTimeout:
                    bool acquired = _capacityThrottle.Wait((int)_options.EnqueueBlockTimeout.TotalMilliseconds);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalDropped);
                        return false;
                    }
                    _queue.Enqueue(new QueueItem(context, true));
                    SignalWorker();
                    return true;

                default:
                    _queue.Enqueue(new QueueItem(context, false));
                    SignalWorker();
                    return true;
            }
        }

        private void SignalWorker()
        {
            if (_signal.CurrentCount == 0)
            {
                try
                {
                    _signal.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Concurrent release handled safely
                }
            }
        }

        private void ExecuteSyncDispatch(ExceptionContext context)
        {
            try
            {
                var batch = new[] { context };
                DispatchToSinksAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
                Interlocked.Increment(ref _totalDispatched);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalSinkFailures);
                SafeEmergencyWrite($"[ExceptionMaster SyncDispatch Error]: {ex.Message}");
            }
        }

        private readonly object _processingLock = new object();

        private async Task ProcessQueueAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (!_queue.IsEmpty && !_cts.IsCancellationRequested)
                {
                    List<ExceptionContext> batch;
                    lock (_processingLock)
                    {
                        batch = DrainBatch(_options.BatchSize);
                    }

                    if (batch.Count > 0)
                    {
                        Interlocked.Increment(ref _inFlightDispatches);
                        try
                        {
                            await DispatchToSinksAsync(batch, _cts.Token).ConfigureAwait(false);
                            Interlocked.Add(ref _totalDispatched, batch.Count);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Add(ref _totalSinkFailures, batch.Count);
                            SafeEmergencyWrite($"[ExceptionMaster BatchDispatch Error]: {ex.Message}");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _inFlightDispatches);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private List<ExceptionContext> DrainBatch(int maxBatchSize)
        {
            var batch = new List<ExceptionContext>(maxBatchSize);
            while (batch.Count < maxBatchSize && _queue.TryDequeue(out var item))
            {
                batch.Add(item.Context);
                if (item.HoldsCapacityPermit)
                {
                    try
                    {
                        _capacityThrottle.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // Safe swallow
                    }
                }
            }
            return batch;
        }

        private void OnTimerFlush(object state)
        {
            if (IsShutdown) return;

            // 1. Collect periodic aggregation summaries
            CollectPeriodicSummaries();

            // 2. Perform off-hot-path eviction and dispatch any evicted entries with unpersisted counts
            CollectEvictedSummaries();

            // 3. Signal worker if there are pending items
            if (!_queue.IsEmpty)
            {
                SignalWorker();
            }
        }

        private void CollectPeriodicSummaries()
        {
            try
            {
                var summaries = _periodicSummaryProvider?.Invoke();
                if (summaries != null && summaries.Count > 0)
                {
                    foreach (var summary in summaries)
                    {
                        Dispatch(summary);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeEmergencyWrite($"[ExceptionMaster PeriodicSummaries Error]: {ex.Message}");
            }
        }

        private void CollectEvictedSummaries()
        {
            try
            {
                var evicted = _evictionProvider?.Invoke();
                if (evicted != null && evicted.Count > 0)
                {
                    foreach (var item in evicted)
                    {
                        Dispatch(item);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeEmergencyWrite($"[ExceptionMaster Eviction Error]: {ex.Message}");
            }
        }

        public async Task DispatchToSinksAsync(IReadOnlyList<ExceptionContext> batch, CancellationToken cancellationToken)
        {
            if (batch == null || batch.Count == 0) return;

            var successfullyWrittenSinks = new HashSet<IExceptionSink>();

            // 1. Fan-out to independent sinks
            if (_options.Sinks != null)
            {
                foreach (var sink in _options.Sinks)
                {
                    if (sink == null) continue;
                    try
                    {
                        await sink.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                        successfullyWrittenSinks.Add(sink);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _totalSinkFailures);
                        SafeEmergencyWrite($"[ExceptionMaster Sink '{sink.Name}' Error]: {ex.Message}");
                    }
                }
            }

            // 2. Process Fallback chains
            if (_options.FallbackChains != null)
            {
                foreach (var chain in _options.FallbackChains)
                {
                    if (chain == null || chain.Primary == null) continue;

                    bool primarySucceeded = false;
                    if (successfullyWrittenSinks.Contains(chain.Primary))
                    {
                        // Primary already wrote the batch during fan-out
                        primarySucceeded = true;
                    }
                    else
                    {
                        try
                        {
                            await chain.Primary.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                            primarySucceeded = true;
                            successfullyWrittenSinks.Add(chain.Primary);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _totalSinkFailures);
                            Interlocked.Increment(ref _totalFallbackInvocations);
                            SafeEmergencyWrite($"[ExceptionMaster Primary Sink '{chain.Primary.Name}' Failed, executing fallbacks]: {ex.Message}");
                        }
                    }

                    if (!primarySucceeded && chain.Fallbacks != null)
                    {
                        foreach (var fallback in chain.Fallbacks)
                        {
                            if (fallback == null) continue;

                            if (successfullyWrittenSinks.Contains(fallback))
                            {
                                // Fallback already received the batch during fan-out
                                break;
                            }

                            try
                            {
                                await fallback.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                                successfullyWrittenSinks.Add(fallback);
                                break; // Fallback succeeded, stop cascading
                            }
                            catch (Exception fbEx)
                            {
                                Interlocked.Increment(ref _totalSinkFailures);
                                SafeEmergencyWrite($"[ExceptionMaster Fallback Sink '{fallback.Name}' Failed]: {fbEx.Message}");
                            }
                        }
                    }
                }
            }
        }

        public void Flush(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            // Collect any pending periodic summaries before draining
            CollectPeriodicSummaries();
            CollectEvictedSummaries();

            lock (_processingLock)
            {
                // Drain queue
                while (!_queue.IsEmpty && DateTime.UtcNow < deadline)
                {
                    var batch = DrainBatch(_options.BatchSize);
                    if (batch.Count > 0)
                    {
                        try
                        {
                            DispatchToSinksAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
                            Interlocked.Add(ref _totalDispatched, batch.Count);
                        }
                        catch
                        {
                            Interlocked.Add(ref _totalSinkFailures, batch.Count);
                        }
                    }
                }

                // Wait for any concurrent background in-flight dispatch to complete
                while (Volatile.Read(ref _inFlightDispatches) > 0 && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(10);
                }

                // Flush underlying sinks
                FlushSinks();
            }
        }

        private void FlushSinks()
        {
            if (_options.Sinks != null)
            {
                foreach (var sink in _options.Sinks)
                {
                    try { sink?.Flush(); } catch { }
                }
            }

            if (_options.FallbackChains != null)
            {
                foreach (var chain in _options.FallbackChains)
                {
                    try { chain?.Primary?.Flush(); } catch { }
                    if (chain?.Fallbacks != null)
                    {
                        foreach (var fb in chain.Fallbacks)
                        {
                            try { fb?.Flush(); } catch { }
                        }
                    }
                }
            }
        }

        public void Shutdown(TimeSpan timeout)
        {
            if (Interlocked.Exchange(ref _isShutdown, 1) != 0) return;

            try
            {
                _flushTimer?.Dispose();
                Flush(timeout);
                _cts.Cancel();
                SignalWorker();
                _workerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Safe swallow
            }
            finally
            {
                DisposeSinks();
            }
        }

        private void DisposeSinks()
        {
            if (_options.Sinks != null)
            {
                foreach (var sink in _options.Sinks)
                {
                    try { sink?.Dispose(); } catch { }
                }
            }

            if (_options.FallbackChains != null)
            {
                foreach (var chain in _options.FallbackChains)
                {
                    try { chain?.Primary?.Dispose(); } catch { }
                    if (chain?.Fallbacks != null)
                    {
                        foreach (var fb in chain.Fallbacks)
                        {
                            try { fb?.Dispose(); } catch { }
                        }
                    }
                }
            }
        }

        public ExceptionMasterHealth GetHealth()
        {
            return new ExceptionMasterHealth(
                Volatile.Read(ref _totalLogged),
                Volatile.Read(ref _totalAggregated),
                Volatile.Read(ref _totalDropped),
                Volatile.Read(ref _totalDispatched),
                Volatile.Read(ref _totalSinkFailures),
                Volatile.Read(ref _totalFallbackInvocations),
                _queue.Count,
                IsShutdown);
        }

        private void SafeEmergencyWrite(string message)
        {
            if (!_options.EnableEmergencyDiagnostics) return;
            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
                // Safe swallow
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            Shutdown(TimeSpan.FromSeconds(2));
            _cts.Dispose();
            _signal.Dispose();
            _capacityThrottle.Dispose();
        }
    }
}
