using System;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Rules;

namespace ExceptionMaster.Alerting.Suppression
{
    /// <summary>
    /// Contract defining a policy for suppressing repeated notifications within a cooldown window.
    /// </summary>
    public interface ISuppressionPolicy
    {
        /// <summary>
        /// Gets the unique identifier of the suppression policy.
        /// </summary>
        string PolicyId { get; }

        /// <summary>
        /// Gets the human-readable name of the suppression policy.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the cooldown duration. While active, repeated events matching the dimension are suppressed.
        /// </summary>
        TimeSpan CooldownPeriod { get; }

        /// <summary>
        /// Gets the dimension used to partition suppression state.
        /// </summary>
        SuppressionDimension Dimension { get; }

        /// <summary>
        /// Gets the custom metadata field key if <see cref="Dimension"/> is <see cref="SuppressionDimension.Custom"/>.
        /// </summary>
        string? CustomDimensionKey { get; }

        /// <summary>
        /// Extracts the partition value for this policy's dimension from the safe evaluation context.
        /// </summary>
        string ExtractDimensionValue(RuleEvaluationContext context);
    }
}
