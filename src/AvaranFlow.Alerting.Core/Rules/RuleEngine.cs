using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Thread-safe deterministic rule engine that evaluates incoming events against configured rules.
    /// Invariants:
    /// 1. Rules are evaluated strictly by Order ascending, then RuleId ascending.
    /// 2. ALL matching rules execute (no first-matching-rule wins).
    /// 3. An error in one rule condition never prevents other rules from evaluating.
    /// </summary>
    public sealed class RuleEngine : IRuleEngine
    {
        private static readonly IReadOnlyList<AlertRule> EmptyRules = new ReadOnlyCollection<AlertRule>(new AlertRule[0]);

        public IReadOnlyList<AlertRule> Rules { get; }

        public RuleEngine(IEnumerable<AlertRule>? rules)
        {
            if (rules == null)
            {
                Rules = EmptyRules;
                return;
            }

            var validation = RuleValidator.Validate(rules);
            if (!validation.IsValid)
            {
                throw new ArgumentException(
                    $"Cannot initialize RuleEngine with invalid rules: {string.Join("; ", validation.Errors)}",
                    nameof(rules));
            }

            // Order deterministically: Order ascending, then RuleId ascending as tie-breaker
            var sorted = rules
                .OrderBy(r => r.Order)
                .ThenBy(r => r.RuleId, StringComparer.Ordinal)
                .ToList();

            Rules = new ReadOnlyCollection<AlertRule>(sorted);
        }

        public IReadOnlyList<AlertRule> Evaluate(RuleEvaluationContext? context)
        {
            if (context == null || Rules.Count == 0)
            {
                return EmptyRules;
            }

            var matched = new List<AlertRule>();

            for (int i = 0; i < Rules.Count; i++)
            {
                var rule = Rules[i];
                if (!rule.Enabled) continue;

                try
                {
                    if (rule.Condition.Evaluate(context))
                    {
                        matched.Add(rule);
                    }
                }
                catch
                {
                    // Rule failure isolation: if a condition throws unexpectedly, do not break the pipeline
                }
            }

            return matched.Count > 0 ? new ReadOnlyCollection<AlertRule>(matched) : EmptyRules;
        }

        public IReadOnlyList<AlertRule> Evaluate(ExceptionContext? context, AlertSeverity resolvedSeverity)
        {
            if (context == null) return EmptyRules;
            var evalContext = RuleEvaluationContext.FromExceptionContext(context, resolvedSeverity);
            return Evaluate(evalContext);
        }
    }
}
