using System;

namespace ExceptionMaster.Alerting.Generations
{
    /// <summary>
    /// Coordinates generation transitions, establishing the atomic admission linearization point
    /// and managing the background drain of superseded generations.
    /// </summary>
    public interface IPipelineGenerationCoordinator : IDisposable
    {
        /// <summary>
        /// Gets the currently active pipeline generation.
        /// Guaranteed to be non-null when initialized.
        /// </summary>
        IPipelineGeneration ActiveGeneration { get; }

        /// <summary>
        /// Gets the generation ID of the active generation.
        /// </summary>
        long CurrentGenerationId { get; }

        /// <summary>
        /// Atomically swaps the active generation to a newly created generation instance.
        /// Initiates bounded asynchronous drain on the superseded generation.
        /// </summary>
        /// <param name="generationFactory">Factory producing the next generation instance from its generation ID.</param>
        /// <param name="drainTimeout">Bounded drain timeout allotted to the superseded generation.</param>
        /// <returns>The newly activated generation.</returns>
        IPipelineGeneration AdvanceGeneration(Func<long, IPipelineGeneration> generationFactory, TimeSpan drainTimeout);

        /// <summary>
        /// Attempts to admit an incoming exception context into the currently active generation.
        /// Retries once if the generation was swapped concurrently during the admission attempt.
        /// </summary>
        bool TryAdmit(ExceptionMaster.Core.ExceptionContext context, ExceptionMaster.Alerting.Enums.AlertSeverity resolvedSeverity, out IAdmittedEvent? admittedEvent);

        /// <summary>
        /// Flushes all active and draining generations, awaiting completion of all admitted events
        /// through the full pipeline up to the specified timeout or cancellation.
        /// </summary>
        System.Threading.Tasks.Task<ExceptionMaster.Alerting.Models.FlushResult> FlushAsync(TimeSpan timeout, System.Threading.CancellationToken cancellationToken = default);
    }
}
