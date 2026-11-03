using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaranFlow.Alerting.Suppression
{
    /// <summary>
    /// Thread-safe in-memory registry for <see cref="ISuppressionPolicy"/> instances.
    /// Case-insensitive lookup; rejects duplicate policy IDs.
    /// </summary>
    public sealed class SuppressionPolicyRegistry : ISuppressionPolicyRegistry
    {
        private readonly ConcurrentDictionary<string, ISuppressionPolicy> _policies =
            new ConcurrentDictionary<string, ISuppressionPolicy>(StringComparer.OrdinalIgnoreCase);

        public int Count => _policies.Count;

        public void Register(ISuppressionPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(policy.PolicyId))
                throw new ArgumentException("PolicyId must not be empty or whitespace.", nameof(policy));

            if (!_policies.TryAdd(policy.PolicyId.Trim(), policy))
            {
                throw new InvalidOperationException($"A suppression policy with ID '{policy.PolicyId}' is already registered.");
            }
        }

        public bool TryGet(string policyId, out ISuppressionPolicy? policy)
        {
            policy = null;
            if (string.IsNullOrWhiteSpace(policyId)) return false;
            return _policies.TryGetValue(policyId.Trim(), out policy);
        }

        public bool Contains(string policyId)
        {
            if (string.IsNullOrWhiteSpace(policyId)) return false;
            return _policies.ContainsKey(policyId.Trim());
        }

        public IReadOnlyCollection<ISuppressionPolicy> GetAll()
        {
            return new ReadOnlyCollection<ISuppressionPolicy>(_policies.Values.ToList());
        }
    }
}
