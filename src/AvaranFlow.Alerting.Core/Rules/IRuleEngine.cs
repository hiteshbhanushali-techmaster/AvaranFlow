using System.Collections.Generic;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Evaluates incoming exception events against configured alert rules in deterministic order.
    /// All matching rules are returned (no first-match short-circuit across rules).
    /// </summary>
    public interface IRuleEngine
    {
        /// <summary>
        /// Gets the immutable list of rules configured in this engine, sorted in deterministic evaluation order.
        /// </summary>
        IReadOnlyList<AlertRule> Rules { get; }

        /// <summary>
        /// Evaluates all configured rules against the provided normalized RuleEvaluationContext.
        /// Returns all matching rules in deterministic ascending order.
        /// </summary>
        IReadOnlyList<AlertRule> Evaluate(RuleEvaluationContext context);

        /// <summary>
        /// Evaluates all configured rules against an ExceptionMaster ExceptionContext and resolved severity.
        /// </summary>
        IReadOnlyList<AlertRule> Evaluate(ExceptionContext context, AlertSeverity resolvedSeverity);
    }
}
