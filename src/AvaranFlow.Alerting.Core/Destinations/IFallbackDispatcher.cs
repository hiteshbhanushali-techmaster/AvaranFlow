using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Models;

namespace AvaranFlow.Alerting.Destinations
{
    /// <summary>
    /// Executes a sequential fallback chain of destinations.
    /// Attempts destinations in defined order until one succeeds or all fail.
    /// Subsequent fallback destinations are NOT invoked if an earlier destination succeeds.
    /// </summary>
    public interface IFallbackDispatcher
    {
        /// <summary>
        /// Executes the fallback chain identified by chainId.
        /// </summary>
        Task<FallbackDeliveryResult> DispatchChainAsync(
            AlertEnvelope envelope,
            string chainId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes the specified fallback chain instance.
        /// </summary>
        Task<FallbackDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            FallbackChain chain,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a sequence of destination names as a fallback chain.
        /// </summary>
        Task<FallbackDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<string> destinationNames,
            CancellationToken cancellationToken = default);
    }
}
