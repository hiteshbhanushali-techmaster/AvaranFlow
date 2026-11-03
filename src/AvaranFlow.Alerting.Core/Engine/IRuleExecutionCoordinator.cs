using AvaranFlow.Alerting.Rules;

namespace AvaranFlow.Alerting.Engine
{
    /// <summary>
    /// Contract for evaluating rule-scoped escalation and suppression policies for matching alert rules.
    /// Strictly adheres to the frozen evaluation order: Escalation -> Suppression.
    /// </summary>
    public interface IRuleExecutionCoordinator
    {
        /// <summary>
        /// Evaluates a matching rule against its configured escalation and suppression policies.
        /// </summary>
        /// <param name="rule">The matching rule being evaluated.</param>
        /// <param name="context">The normalized, safe evaluation context.</param>
        /// <returns>A <see cref="RuleExecutionDecision"/> containing delivery permission and target destinations.</returns>
        RuleExecutionDecision Evaluate(AlertRule rule, RuleEvaluationContext context);
    }
}
