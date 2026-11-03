using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Thread-safe implementation of IDestinationConcurrencyLimiter backed by a single SemaphoreSlim.
    /// Shared across all dispatchers, rules, and concurrent pipeline generations to enforce
    /// the global destination execution budget.
    /// </summary>
    public sealed class DestinationConcurrencyLimiter : IDestinationConcurrencyLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private int _isDisposed;

        public int MaxConcurrency { get; }
        public int CurrentAvailableCount => _semaphore.CurrentCount;

        public DestinationConcurrencyLimiter(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "MaxConcurrency must be positive.");

            MaxConcurrency = maxConcurrency;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            return _semaphore.WaitAsync(cancellationToken);
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return _semaphore.WaitAsync(timeout, cancellationToken);
        }

        public void Release()
        {
            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Safe swallow during shutdown races
            }
            catch (SemaphoreFullException)
            {
                // Safety containment: prevent double-release from corrupting budget
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
            _semaphore.Dispose();
        }
    }
}
