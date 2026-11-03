using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Escalation
{
    /// <summary>
    /// Thread-safe in-memory registry for <see cref="IEscalationPolicy"/> instances.
    /// Case-insensitive lookup; rejects duplicate policy IDs.
    /// </summary>
    public sealed class EscalationPolicyRegistry : IEscalationPolicyRegistry
    {
        private readonly ConcurrentDictionary<string, IEscalationPolicy> _policies =
            new ConcurrentDictionary<string, IEscalationPolicy>(StringComparer.OrdinalIgnoreCase);

        public int Count => _policies.Count;

        public void Register(IEscalationPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(policy.PolicyId))
                throw new ArgumentException("PolicyId must not be empty or whitespace.", nameof(policy));

            if (!_policies.TryAdd(policy.PolicyId.Trim(), policy))
            {
                throw new InvalidOperationException($"An escalation policy with ID '{policy.PolicyId}' is already registered.");
            }
        }

        public bool TryGet(string policyId, out IEscalationPolicy? policy)
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

        public IReadOnlyCollection<IEscalationPolicy> GetAll()
        {
            return new ReadOnlyCollection<IEscalationPolicy>(_policies.Values.ToList());
        }
    }
}
