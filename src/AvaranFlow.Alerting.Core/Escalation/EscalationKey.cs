using System;

namespace AvaranFlow.Alerting.Escalation
{
    /// <summary>
    /// Represents the composite identity for rule-scoped escalation state:
    /// RuleId + PolicyId + ExceptionKey.
    /// Guarantees that escalation counters and state progression are isolated across rules and policies.
    /// </summary>
    public readonly struct EscalationKey : IEquatable<EscalationKey>
    {
        public string RuleId { get; }
        public string PolicyId { get; }
        public string ExceptionKey { get; }

        public EscalationKey(string ruleId, string policyId, string exceptionKey)
        {
            RuleId = ruleId ?? string.Empty;
            PolicyId = policyId ?? string.Empty;
            ExceptionKey = exceptionKey ?? string.Empty;
        }

        public bool Equals(EscalationKey other)
        {
            return string.Equals(RuleId, other.RuleId, StringComparison.Ordinal) &&
                   string.Equals(PolicyId, other.PolicyId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ExceptionKey, other.ExceptionKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is EscalationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(RuleId);
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(PolicyId);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(ExceptionKey);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{RuleId}:{PolicyId}:{ExceptionKey}";
        }

        public static bool operator ==(EscalationKey left, EscalationKey right) => left.Equals(right);
        public static bool operator !=(EscalationKey left, EscalationKey right) => !left.Equals(right);
    }
}
