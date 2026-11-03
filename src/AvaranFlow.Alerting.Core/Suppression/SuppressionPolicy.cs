using System;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Rules;

namespace AvaranFlow.Alerting.Suppression
{
    /// <summary>
    /// Immutable implementation of <see cref="ISuppressionPolicy"/>.
    /// </summary>
    public sealed class SuppressionPolicy : ISuppressionPolicy
    {
        public string PolicyId { get; }
        public string Name { get; }
        public TimeSpan CooldownPeriod { get; }
        public SuppressionDimension Dimension { get; }
        public string? CustomDimensionKey { get; }

        public SuppressionPolicy(
            string policyId,
            TimeSpan cooldownPeriod,
            SuppressionDimension dimension = SuppressionDimension.ExceptionKey,
            string? name = null,
            string? customDimensionKey = null)
        {
            if (string.IsNullOrWhiteSpace(policyId))
                throw new ArgumentException("PolicyId must be non-null and not whitespace.", nameof(policyId));
            if (cooldownPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cooldownPeriod), "CooldownPeriod must be greater than zero.");
            if (dimension == SuppressionDimension.Custom && string.IsNullOrWhiteSpace(customDimensionKey))
                throw new ArgumentException("CustomDimensionKey must be provided when Dimension is Custom.", nameof(customDimensionKey));

            PolicyId = policyId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? PolicyId : name!.Trim();
            CooldownPeriod = cooldownPeriod;
            Dimension = dimension;
            CustomDimensionKey = string.IsNullOrWhiteSpace(customDimensionKey) ? null : customDimensionKey!.Trim();
        }

        public string ExtractDimensionValue(RuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            switch (Dimension)
            {
                case SuppressionDimension.ExceptionKey:
                    return context.ExceptionKey;
                case SuppressionDimension.Category:
                    return context.Category;
                case SuppressionDimension.Source:
                    return context.Source;
                case SuppressionDimension.ExceptionType:
                    return context.ExceptionType;
                case SuppressionDimension.CorrelationId:
                    return context.CorrelationId;
                case SuppressionDimension.Message:
                    return context.Message;
                case SuppressionDimension.Rule:
                    return string.Empty;
                case SuppressionDimension.Custom:
                    if (CustomDimensionKey != null && context.Metadata.TryGetValue(CustomDimensionKey, out var customVal))
                        return customVal ?? string.Empty;
                    return string.Empty;
                default:
                    return context.ExceptionKey;
            }
        }

        public override string ToString()
        {
            return $"SuppressionPolicy [Id={PolicyId}, Dimension={Dimension}, Cooldown={CooldownPeriod}]";
        }
    }
}
