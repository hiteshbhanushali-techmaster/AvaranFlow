using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Models;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Executes sequential fallback chains with strict distinction between destination timeouts and caller cancellation.
    /// Invariants:
    /// 1. Fallback proceeds only when the preceding destination fails or times out.
    /// 2. When a destination succeeds, later destinations are not invoked.
    /// 3. When caller/host cancellation is requested, execution halts immediately; subsequent fallbacks are NOT attempted.
    /// </summary>
    public sealed class FallbackDispatcher : IFallbackDispatcher, IDisposable
    {
        private readonly IDestinationRegistry _destinationRegistry;
        private readonly IFallbackChainRegistry? _chainRegistry;
        private readonly TimeSpan _destinationTimeout;
        private readonly IDestinationConcurrencyLimiter _concurrencyLimiter;
        private readonly bool _ownsConcurrencyLimiter;
        private int _isDisposed;

        public FallbackDispatcher(
            IDestinationRegistry destinationRegistry,
            IFallbackChainRegistry? chainRegistry = null,
            TimeSpan? destinationTimeout = null,
            int maxConcurrency = 16,
            IDestinationConcurrencyLimiter? sharedConcurrencyLimiter = null)
        {
            _destinationRegistry = destinationRegistry ?? throw new ArgumentNullException(nameof(destinationRegistry));
            _chainRegistry = chainRegistry;
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

        public async Task<FallbackDeliveryResult> DispatchChainAsync(
            AlertEnvelope envelope,
            string chainId,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (string.IsNullOrWhiteSpace(chainId))
            {
                return new FallbackDeliveryResult(
                    false,
                    null,
                    new[] { AlertDeliveryResult.Failed(chainId ?? "UnknownChain", TimeSpan.Zero, null, "ChainId cannot be null or whitespace.") },
                    TimeSpan.Zero);
            }

            var chain = _chainRegistry?.FindChain(chainId);
            if (chain == null)
            {
                return new FallbackDeliveryResult(
                    false,
                    null,
                    new[] { AlertDeliveryResult.Failed(chainId, TimeSpan.Zero, null, $"Fallback chain '{chainId}' was not found in the fallback chain registry.") },
                    TimeSpan.Zero);
            }

            return await DispatchAsync(envelope, chain, cancellationToken).ConfigureAwait(false);
        }

        public Task<FallbackDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            FallbackChain chain,
            CancellationToken cancellationToken = default)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            return DispatchAsync(envelope, chain.DestinationNames, cancellationToken);
        }

        public async Task<FallbackDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<string> destinationNames,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (destinationNames == null)
            {
                return new FallbackDeliveryResult(false, null, null, TimeSpan.Zero);
            }

            var destinations = destinationNames.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList();
            if (destinations.Count == 0)
            {
                return new FallbackDeliveryResult(false, null, null, TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();
            var attempts = new List<AlertDeliveryResult>(destinations.Count);

            for (int i = 0; i < destinations.Count; i++)
            {
                // Check if host/caller cancellation was requested before next attempt
                if (cancellationToken.IsCancellationRequested)
                {
                    attempts.Add(AlertDeliveryResult.Cancelled(
                        destinations[i],
                        TimeSpan.Zero,
                        "Fallback chain execution was cancelled by caller prior to attempt."));
                    break; // STOP: do not attempt subsequent fallback destinations
                }

                string destName = destinations[i];
                var destination = _destinationRegistry.FindDestination(destName);

                if (destination == null)
                {
                    // Destination missing from registry
                    attempts.Add(AlertDeliveryResult.Failed(
                        destName,
                        TimeSpan.Zero,
                        null,
                        $"Destination '{destName}' is not registered in the destination registry."));
                    // Continue to next fallback destination in chain
                    continue;
                }

                var attemptResult = await SafeDeliverAsync(destination, envelope, cancellationToken).ConfigureAwait(false);
                attempts.Add(attemptResult);

                if (attemptResult.Success)
                {
                    // Success! Stop execution immediately. Later fallback destinations are NOT invoked.
                    sw.Stop();
                    return new FallbackDeliveryResult(true, destName, attempts, sw.Elapsed);
                }

                if (attemptResult.IsCancelled || cancellationToken.IsCancellationRequested)
                {
                    // Caller/host cancellation requested!
                    // Stop fallback execution immediately — do NOT continue fallback attempts.
                    sw.Stop();
                    return new FallbackDeliveryResult(false, null, attempts, sw.Elapsed);
                }

                // If attemptResult is a failure or destination timeout, the loop proceeds to the next configured fallback destination
            }

            sw.Stop();
            // All destinations in chain failed or execution was cancelled
            return new FallbackDeliveryResult(false, null, attempts, sw.Elapsed);
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
                        $"Destination '{destination.Name}' returned null result.");
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
