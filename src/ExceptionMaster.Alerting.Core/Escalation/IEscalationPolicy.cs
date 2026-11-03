using System;
using System.Collections.Generic;

namespace ExceptionMaster.Alerting.Escalation
{
    /// <summary>
    /// Contract defining a policy for escalating alerts to higher levels and wider destinations.
    /// </summary>
    public interface IEscalationPolicy
    {
        /// <summary>
        /// Gets the unique identifier of the escalation policy.
        /// </summary>
        string PolicyId { get; }

        /// <summary>
        /// Gets the human-readable name of the escalation policy.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the sliding TTL inactivity window. If no events arrive within this window,
        /// the escalation state for the key expires and resets to the baseline level.
        /// </summary>
        TimeSpan Ttl { get; }

        /// <summary>
        /// Gets the collection of escalation steps ordered by level ascending.
        /// </summary>
        IReadOnlyList<EscalationStep> Steps { get; }

        /// <summary>
        /// Gets the step corresponding to the given level, or null if not found.
        /// </summary>
        EscalationStep? GetStep(int level);
    }
}
