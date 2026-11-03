using System.Collections.Generic;

namespace AvaranFlow.Alerting.Escalation
{
    /// <summary>
    /// Registry contract for managing and resolving escalation policies by identifier.
    /// </summary>
    public interface IEscalationPolicyRegistry
    {
        /// <summary>
        /// Registers an escalation policy. Throws if a policy with the same identifier already exists.
        /// </summary>
        void Register(IEscalationPolicy policy);

        /// <summary>
        /// Attempts to retrieve an escalation policy by identifier.
        /// </summary>
        bool TryGet(string policyId, out IEscalationPolicy? policy);

        /// <summary>
        /// Checks whether an escalation policy with the specified identifier is registered.
        /// </summary>
        bool Contains(string policyId);

        /// <summary>
        /// Gets the total number of registered escalation policies.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets all registered escalation policies as a read-only collection.
        /// </summary>
        IReadOnlyCollection<IEscalationPolicy> GetAll();
    }
}
