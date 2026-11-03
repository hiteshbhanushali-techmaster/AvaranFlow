using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Governs the global destination execution concurrency budget across all fan-out, fallback,
    /// rule dispatches, and pipeline generations.
    /// </summary>
    public interface IDestinationConcurrencyLimiter : IDisposable
    {
        /// <summary>
        /// Gets the maximum concurrent destination delivery slots permitted globally.
        /// </summary>
        int MaxConcurrency { get; }

        /// <summary>
        /// Gets the current number of available delivery slots.
        /// </summary>
        int CurrentAvailableCount { get; }

        /// <summary>
        /// Asynchronously acquires a delivery slot, bounded by caller cancellation.
        /// </summary>
        Task WaitAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Asynchronously acquires a delivery slot, bounded by timeout and caller cancellation.
        /// </summary>
        Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Releases an acquired delivery slot back to the global budget.
        /// </summary>
        void Release();
    }
}
