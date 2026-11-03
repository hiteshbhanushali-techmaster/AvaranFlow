using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Models;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Dispatches an alert envelope to one or more independent destinations in parallel or sequentially.
    /// Strictly guarantees bounded concurrency via shared IDestinationConcurrencyLimiter,
    /// per-destination timeouts, caller cancellation distinction, and destination-level failure containment.
    /// </summary>
    public sealed class FanOutDispatcher : IFanOutDispatcher, IDisposable
    {
        private readonly IDestinationRegistry _destinationRegistry;
        private readonly IDestinationGroupRegistry? _groupRegistry;
        private readonly FanOutDispatchMode _dispatchMode;
        private readonly TimeSpan _destinationTimeout;
        private readonly IDestinationConcurrencyLimiter _concurrencyLimiter;
        private readonly bool _ownsConcurrencyLimiter;
        private int _isDisposed;

        public FanOutDispatcher(
            IDestinationRegistry destinationRegistry,
            IDestinationGroupRegistry? groupRegistry = null,
            FanOutDispatchMode dispatchMode = FanOutDispatchMode.Concurrent,
            TimeSpan? destinationTimeout = null,
            int maxConcurrency = 16,
            IDestinationConcurrencyLimiter? sharedConcurrencyLimiter = null)
        {
            _destinationRegistry = destinationRegistry ?? throw new ArgumentNullException(nameof(destinationRegistry));
            _groupRegistry = groupRegistry;
            _dispatchMode = dispatchMode;
            _destinationTimeout = destinationTimeout ?? TimeSpan.FromSeconds(5);

            if (sharedConcurrencyLimiter != null)
            {
                _concurrencyLimiter = sharedConcurrencyLimiter;
                _ownsConcurrencyLimiter = false;
            }
            else
            {
                int concurrency = maxConcurrency > 0 ? maxConcurrency : 16;
                _concurrencyLimiter = new DestinationConcurrencyLimiter(concurrency);
                _ownsConcurrencyLimiter = true;
            }
        }

        public async Task<FanOutDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<string> destinationNames,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (destinationNames == null) return new FanOutDeliveryResult(null, TimeSpan.Zero);

            var resolvedDestinations = new List<IAlertDestination>();
            var missingResults = new List<AlertDeliveryResult>();

            foreach (var name in destinationNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var dest = _destinationRegistry.FindDestination(name);
                if (dest != null)
                {
                    resolvedDestinations.Add(dest);
                }
                else
                {
                    // Record missing destination as an immediate failure result without crashing
                    missingResults.Add(AlertDeliveryResult.Failed(
                        name,
                        TimeSpan.Zero,
                        null,
                        $"Destination '{name}' is not registered in the destination registry."));
                }
            }

            var dispatchResult = await DispatchAsync(envelope, resolvedDestinations, cancellationToken).ConfigureAwait(false);

            if (missingResults.Count > 0)
            {
                var combined = missingResults.Concat(dispatchResult.Results).ToList();
                return new FanOutDeliveryResult(combined, dispatchResult.TotalElapsed);
            }

            return dispatchResult;
        }

        public async Task<FanOutDeliveryResult> DispatchGroupAsync(
            AlertEnvelope envelope,
            string groupId,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return new FanOutDeliveryResult(
                    new[] { AlertDeliveryResult.Failed(groupId ?? "UnknownGroup", TimeSpan.Zero, null, "GroupId cannot be null or whitespace.") },
                    TimeSpan.Zero);
            }

            var group = _groupRegistry?.FindGroup(groupId);
            if (group == null)
            {
                return new FanOutDeliveryResult(
                    new[] { AlertDeliveryResult.Failed(groupId, TimeSpan.Zero, null, $"Destination group '{groupId}' was not found in the group registry.") },
                    TimeSpan.Zero);
            }

            return await DispatchAsync(envelope, group.DestinationNames, cancellationToken).ConfigureAwait(false);
        }

        public async Task<FanOutDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<IAlertDestination> destinations,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (destinations == null) return new FanOutDeliveryResult(null, TimeSpan.Zero);

            var destinationList = destinations.Where(d => d != null).ToList();
            if (destinationList.Count == 0)
            {
                return new FanOutDeliveryResult(null, TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();

            if (_dispatchMode == FanOutDispatchMode.Sequential)
            {
                // Sequential execution: preserves deterministic configured order
                var results = new List<AlertDeliveryResult>(destinationList.Count);
                foreach (var destination in destinationList)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        results.Add(AlertDeliveryResult.Cancelled(
                            destination.Name,
                            TimeSpan.Zero,
                            $"Sequential fan-out delivery to '{destination.Name}' cancelled by caller request."));
                        continue;
                    }

                    var result = await SafeDeliverAsync(destination, envelope, cancellationToken).ConfigureAwait(false);
                    results.Add(result);
                }

                sw.Stop();
                return new FanOutDeliveryResult(results, sw.Elapsed);
            }
            else
            {
                // Concurrent execution: all destinations are attempted independently in parallel,
                // bounded by the global concurrency limiter
                var tasks = destinationList.Select(d => SafeDeliverAsync(d, envelope, cancellationToken)).ToArray();
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                sw.Stop();
                return new FanOutDeliveryResult(results, sw.Elapsed);
            }
        }

        private async Task<AlertDeliveryResult> SafeDeliverAsync(
            IAlertDestination destination,
            AlertEnvelope envelope,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AlertDeliveryResult.Cancelled(
                    destination.Name,
                    TimeSpan.Zero,
                    $"Destination '{destination.Name}' delivery was cancelled by caller before slot acquisition.");
            }

            var sw = Stopwatch.StartNew();
            bool acquired = false;

            try
            {
                // Enforce global destination concurrency bound
                await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;

                if (cancellationToken.IsCancellationRequested)
                {
                    sw.Stop();
                    return AlertDeliveryResult.Cancelled(
                        destination.Name,
                        sw.Elapsed,
                        $"Destination '{destination.Name}' delivery was cancelled by caller after slot acquisition.");
                }

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(_destinationTimeout);
                    var result = await destination.DeliverAsync(envelope, timeoutCts.Token).ConfigureAwait(false);
                    sw.Stop();

                    return result ?? AlertDeliveryResult.Failed(
                        destination.Name,
                        sw.Elapsed,
                        null,
                        $"Destination '{destination.Name}' returned a null AlertDeliveryResult.");
                }
            }
            catch (OperationCanceledException oce)
            {
                sw.Stop();
                if (cancellationToken.IsCancellationRequested)
                {
                    // Case B: Caller / Host Cancellation
                    return AlertDeliveryResult.Cancelled(
                        destination.Name,
                        sw.Elapsed,
                        $"Destination '{destination.Name}' delivery was cancelled by caller/host request.");
                }

                // Case A: Destination Timeout (Alerting timeout expired while caller cancellation was NOT requested)
                return AlertDeliveryResult.Failed(
                    destination.Name,
                    sw.Elapsed,
                    oce,
                    $"Destination '{destination.Name}' delivery timed out after {_destinationTimeout.TotalMilliseconds:F0}ms.");
            }
            catch (Exception ex)
            {
                sw.Stop();
                return AlertDeliveryResult.Failed(
                    destination.Name,
                    sw.Elapsed,
                    ex,
                    $"Destination '{destination.Name}' delivery threw an exception: {ex.Message}");
            }
            finally
            {
                if (acquired)
                {
                    _concurrencyLimiter.Release();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
            if (_ownsConcurrencyLimiter)
            {
                _concurrencyLimiter.Dispose();
            }
        }
    }
}
