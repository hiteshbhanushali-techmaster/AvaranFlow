using System;

namespace AvaranFlow.Alerting.Suppression
{
    /// <summary>
    /// Represents the composite identity for rule-scoped suppression state:
    /// RuleId + PolicyId + Dimension + DimensionValue.
    /// Guarantees that suppression state is isolated across rules, policies, and dimensions.
    /// </summary>
    public readonly struct SuppressionKey : IEquatable<SuppressionKey>
    {
        public string RuleId { get; }
        public string PolicyId { get; }
        public string Dimension { get; }
        public string DimensionValue { get; }

        public SuppressionKey(string ruleId, string policyId, string dimension, string? dimensionValue)
        {
            RuleId = ruleId ?? string.Empty;
            PolicyId = policyId ?? string.Empty;
            Dimension = dimension ?? string.Empty;
            DimensionValue = dimensionValue ?? string.Empty;
        }

        public bool Equals(SuppressionKey other)
        {
            return string.Equals(RuleId, other.RuleId, StringComparison.Ordinal) &&
                   string.Equals(PolicyId, other.PolicyId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Dimension, other.Dimension, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(DimensionValue, other.DimensionValue, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is SuppressionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(RuleId);
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(PolicyId);
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Dimension);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(DimensionValue);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{RuleId}:{PolicyId}:{Dimension}:{DimensionValue}";
        }

        public static bool operator ==(SuppressionKey left, SuppressionKey right) => left.Equals(right);
        public static bool operator !=(SuppressionKey left, SuppressionKey right) => !left.Equals(right);
    }
}
