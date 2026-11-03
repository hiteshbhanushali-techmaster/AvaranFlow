using System;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Generations
{
    /// <summary>
    /// Represents an event that has been admitted into a specific pipeline generation.
    /// Guarantees that the event belongs to exactly one generation owner and can be
    /// claimed for processing at most once.
    /// </summary>
    public interface IAdmittedEvent
    {
        /// <summary>
        /// Gets the identifier of the unique pipeline generation that admitted this event.
        /// </summary>
        long GenerationId { get; }

        /// <summary>
        /// Gets the globally unique admission ticket identifier.
        /// </summary>
        Guid AdmissionTicketId { get; }

        /// <summary>
        /// Gets the UTC timestamp when the event was admitted.
        /// </summary>
        DateTime AdmittedUtc { get; }

        /// <summary>
        /// Gets the source ExceptionMaster context.
        /// </summary>
        ExceptionContext Context { get; }

        /// <summary>
        /// Gets the resolved alert severity determined at admission time.
        /// </summary>
        AlertSeverity ResolvedSeverity { get; }

        /// <summary>
        /// Gets whether this event has already been claimed for processing.
        /// </summary>
        bool IsClaimed { get; }

        /// <summary>
        /// Gets whether this event has completed processing through the pipeline.
        /// </summary>
        bool IsCompleted { get; }

        /// <summary>
        /// Gets a Task that completes when this event has completed processing and destination delivery.
        /// </summary>
        Task CompletionTask { get; }

        /// <summary>
        /// Atomically claims this admitted event for processing.
        /// Returns true if the caller was the first and only caller to successfully claim it;
        /// returns false if it has already been claimed.
        /// </summary>
        bool TryClaimForProcessing();

        /// <summary>
        /// Signals that processing of this admitted event has completed (or was dropped during drain/eviction).
        /// </summary>
        void MarkCompleted();
    }

    /// <summary>
    /// Default thread-safe implementation of IAdmittedEvent using atomic interlocked state transitions.
    /// </summary>
    public sealed class AdmittedEvent : IAdmittedEvent
    {
        private readonly TaskCompletionSource<object?> _completionTcs =
            new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _isClaimed;

        public long GenerationId { get; }
        public Guid AdmissionTicketId { get; }
        public DateTime AdmittedUtc { get; }
        public ExceptionContext Context { get; }
        public AlertSeverity ResolvedSeverity { get; }

        public bool IsClaimed => Volatile.Read(ref _isClaimed) == 1;
        public bool IsCompleted => _completionTcs.Task.IsCompleted;
        public Task CompletionTask => _completionTcs.Task;

        public AdmittedEvent(
            long generationId,
            ExceptionContext context,
            AlertSeverity resolvedSeverity,
            Guid? admissionTicketId = null,
            DateTime? admittedUtc = null)
        {
            if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId), "Generation ID must be positive.");
            GenerationId = generationId;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ResolvedSeverity = resolvedSeverity;
            AdmissionTicketId = admissionTicketId ?? Guid.NewGuid();
            AdmittedUtc = admittedUtc?.Kind == DateTimeKind.Utc
                ? admittedUtc.Value
                : (admittedUtc.HasValue ? DateTime.SpecifyKind(admittedUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow);
        }

        public bool TryClaimForProcessing()
        {
            return Interlocked.CompareExchange(ref _isClaimed, 1, 0) == 0;
        }

        public void MarkCompleted()
        {
            _completionTcs.TrySetResult(null);
        }

        public override string ToString()
        {
            return $"AdmittedEvent [Gen={GenerationId}, Ticket={AdmissionTicketId:N}, Key={Context.ExceptionKey}, Severity={ResolvedSeverity}, Claimed={IsClaimed}, Completed={IsCompleted}]";
        }
    }
}
