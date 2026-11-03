using System;

namespace AvaranFlow.Alerting.Models
{
    /// <summary>
    /// Represents the deterministic outcome of a pipeline Flush operation.
    /// Captures the count of events admitted prior to the flush boundary that completed the entire pipeline,
    /// as well as any pending events that did not complete within the allotted timeout or before cancellation.
    /// </summary>
    public sealed class FlushResult
    {
        /// <summary>
        /// Gets whether all events admitted prior to the flush boundary completed processing and destination delivery.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Gets the number of admitted events that completed the full pipeline within the flush window.
        /// </summary>
        public int CompletedEvents { get; }

        /// <summary>
        /// Gets the number of admitted events that were still pending when the flush timeout or cancellation occurred.
        /// </summary>
        public int PendingEvents { get; }

        /// <summary>
        /// Gets the elapsed duration of the flush operation.
        /// </summary>
        public TimeSpan Elapsed { get; }

        public FlushResult(bool success, int completedEvents, int pendingEvents, TimeSpan elapsed = default)
        {
            Success = success;
            CompletedEvents = completedEvents >= 0 ? completedEvents : 0;
            PendingEvents = pendingEvents >= 0 ? pendingEvents : 0;
            Elapsed = elapsed;
        }

        public override string ToString() =>
            $"FlushResult [Success={Success}, Completed={CompletedEvents}, Pending={PendingEvents}, Elapsed={Elapsed.TotalMilliseconds:F1}ms]";
    }
}
