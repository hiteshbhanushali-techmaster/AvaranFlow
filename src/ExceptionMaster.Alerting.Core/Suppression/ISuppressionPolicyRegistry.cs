using System.Collections.Generic;

namespace ExceptionMaster.Alerting.Suppression
{
    /// <summary>
    /// Registry contract for managing and resolving suppression policies by identifier.
    /// </summary>
    public interface ISuppressionPolicyRegistry
    {
        /// <summary>
        /// Registers a suppression policy. Throws if a policy with the same identifier already exists.
        /// </summary>
        void Register(ISuppressionPolicy policy);

        /// <summary>
        /// Attempts to retrieve a suppression policy by identifier.
        /// </summary>
        bool TryGet(string policyId, out ISuppressionPolicy? policy);

        /// <summary>
        /// Checks whether a suppression policy with the specified identifier is registered.
        /// </summary>
        bool Contains(string policyId);

        /// <summary>
        /// Gets the total number of registered suppression policies.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets all registered suppression policies as a read-only collection.
        /// </summary>
        IReadOnlyCollection<ISuppressionPolicy> GetAll();
    }
}
