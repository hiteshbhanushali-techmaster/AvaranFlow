using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Models;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Dispatches an alert envelope to one or more independent destinations in parallel or sequentially.
    /// Guarantees bounded concurrency, per-destination timeouts, and destination-level failure containment.
    /// </summary>
    public interface IFanOutDispatcher
    {
        /// <summary>
        /// Dispatches an alert envelope to the specified list of destination names independently.
        /// </summary>
        Task<FanOutDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<string> destinationNames,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Dispatches an alert envelope to all member destinations of the specified destination group.
        /// </summary>
        Task<FanOutDeliveryResult> DispatchGroupAsync(
            AlertEnvelope envelope,
            string groupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Dispatches an alert envelope directly to the specified destination instances.
        /// </summary>
        Task<FanOutDeliveryResult> DispatchAsync(
            AlertEnvelope envelope,
            IEnumerable<IAlertDestination> destinations,
            CancellationToken cancellationToken = default);
    }
}
