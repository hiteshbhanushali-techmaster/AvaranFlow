using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Escalation
{
    /// <summary>
    /// Immutable implementation of <see cref="IEscalationPolicy"/>.
    /// Validates steps are non-empty, unique by level, and sorted in ascending level order.
    /// </summary>
    public sealed class EscalationPolicy : IEscalationPolicy
    {
        public string PolicyId { get; }
        public string Name { get; }
        public TimeSpan Ttl { get; }
        public IReadOnlyList<EscalationStep> Steps { get; }

        public EscalationPolicy(
            string policyId,
            IEnumerable<EscalationStep> steps,
            TimeSpan? ttl = null,
            string? name = null)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("PolicyId must be non-null and not whitespace.", nameof(policyId));
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));

            var stepList = steps.Where(s => s != null).OrderBy(s => s.Level).ToList();
            if (stepList.Count == 0)
                throw new ArgumentException("EscalationPolicy must define at least one EscalationStep.", nameof(steps));

            // Validate unique levels
            var duplicateLevel = stepList.GroupBy(s => s.Level).FirstOrDefault(g => g.Count() > 1);
            if (duplicateLevel != null)
                throw new ArgumentException($"Duplicate escalation level '{duplicateLevel.Key}' defined in policy '{policyId}'.", nameof(steps));

            PolicyId = policyId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? PolicyId : name!.Trim();
            Ttl = ttl ?? TimeSpan.FromMinutes(30);
            if (Ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ttl), "Ttl must be greater than zero.");

            Steps = new ReadOnlyCollection<EscalationStep>(stepList);
        }

        public EscalationStep? GetStep(int level)
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Level == level)
                    return Steps[i];
            }
            return null;
        }

        public override string ToString()
        {
            return $"EscalationPolicy [Id={PolicyId}, Steps={Steps.Count}, Ttl={Ttl}]";
        }
    }
}
