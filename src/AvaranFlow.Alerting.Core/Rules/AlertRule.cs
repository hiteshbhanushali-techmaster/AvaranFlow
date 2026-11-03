using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Represents an immutable alerting rule evaluated by the routing engine.
    /// Rules are evaluated in deterministic ascending Order.
    /// By default, ALL matching rules execute (no first-matching-rule short-circuit).
    /// </summary>
    public sealed class AlertRule
    {
        private static readonly IReadOnlyList<string> EmptyStrings = new ReadOnlyCollection<string>(new string[0]);

        public string RuleId { get; }
        public string Name { get; }
        public bool Enabled { get; }
        public int Order { get; }
        public RuleCondition Condition { get; }
        public IReadOnlyList<string> TargetDestinations { get; }
        public IReadOnlyList<string> TargetGroups { get; }
        public string? SuppressionPolicyId { get; }
        public string? EscalationPolicyId { get; }

        public AlertRule(
            string ruleId,
            string name,
            RuleCondition condition,
            int order = 0,
            bool enabled = true,
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null,
            string? suppressionPolicyId = null,
            string? escalationPolicyId = null)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new ArgumentException("RuleId must be non-null and not whitespace.", nameof(ruleId));

            RuleId = ruleId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? RuleId : name.Trim();
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Order = order;
            Enabled = enabled;

            TargetDestinations = targetDestinations != null
                ? new ReadOnlyCollection<string>(targetDestinations.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList())
                : EmptyStrings;

            TargetGroups = targetGroups != null
                ? new ReadOnlyCollection<string>(targetGroups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList())
                : EmptyStrings;

            SuppressionPolicyId = string.IsNullOrWhiteSpace(suppressionPolicyId) ? null : suppressionPolicyId!.Trim();
            EscalationPolicyId = string.IsNullOrWhiteSpace(escalationPolicyId) ? null : escalationPolicyId!.Trim();
        }

        public override string ToString()
        {
            return $"AlertRule [Id={RuleId}, Order={Order}, Enabled={Enabled}, Condition={Condition}]";
        }
    }
}
