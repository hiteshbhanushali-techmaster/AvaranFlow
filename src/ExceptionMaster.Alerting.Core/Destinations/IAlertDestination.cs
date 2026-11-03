using System;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Models;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Represents an alert delivery endpoint (e.g. Notifier, Email, SMS, Webhook).
    /// Execution must be asynchronous, bounded, and failure-isolated.
    /// DeliverAsync implementations MUST NOT allow exceptions to escape.
    /// </summary>
    public interface IAlertDestination : IDisposable
    {
        /// <summary>
        /// Gets the unique identifier/name for this destination.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Asynchronously delivers an alert envelope to this destination.
        /// </summary>
        /// <param name="envelope">The safe destination alert envelope.</param>
        /// <param name="cancellationToken">Cancellation token governing delivery timeout.</param>
        /// <returns>An AlertDeliveryResult indicating success or failure.</returns>
        Task<AlertDeliveryResult> DeliverAsync(AlertEnvelope envelope, CancellationToken cancellationToken);
    }
}
