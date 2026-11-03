using System;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Generations
{
    /// <summary>
    /// Represents an immutable pipeline generation instance that owns its dedicated ingestion queue,
    /// workers, and execution state. An admitted event belongs to exactly one pipeline generation.
    /// </summary>
    public interface IPipelineGeneration : IDisposable
    {
        /// <summary>
        /// Gets the unique generation identifier. Monotonically increasing across reconfigurations.
        /// </summary>
        long GenerationId { get; }

        /// <summary>
        /// Gets the UTC creation timestamp of this generation.
        /// </summary>
        DateTime CreatedUtc { get; }

        /// <summary>
        /// Gets the current lifecycle state of this generation (Active, Draining, or Disposed).
        /// </summary>
        GenerationState State { get; }

        /// <summary>
        /// Attempts to admit an incoming exception context into this generation.
        /// If this generation is not Active or the queue is saturated under drop policies,
        /// returns false and sets admittedEvent to null.
        /// </summary>
        bool TryAdmit(ExceptionContext context, AlertSeverity resolvedSeverity, out IAdmittedEvent? admittedEvent);

        /// <summary>
        /// Stops accepting any new event admissions into this generation immediately.
        /// Sets generation state to Draining so that in-flight workers can finish admitted items.
        /// </summary>
        void StopAcceptingAdmissions();

        /// <summary>
        /// Flushes all events admitted prior to the flush call, awaiting complete pipeline processing
        /// and destination delivery up to the specified timeout or cancellation.
        /// Does NOT dispose or stop the generation if it is Active.
        /// </summary>
        Task<AvaranFlow.Alerting.Models.FlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Drains already admitted events up to the specified bounded timeout.
        /// Uncompleted events remaining when timeout expires are dropped under configured overflow policy.
        /// </summary>
        Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}
