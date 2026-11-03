using System.Collections.Generic;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Internal interface for AST condition nodes.
    /// External consumers must use declarative RuleCondition factory methods.
    /// </summary>
    internal interface IRuleCondition
    {
        bool Evaluate(RuleEvaluationContext context);
        void Validate(List<string> errors);
    }
}
