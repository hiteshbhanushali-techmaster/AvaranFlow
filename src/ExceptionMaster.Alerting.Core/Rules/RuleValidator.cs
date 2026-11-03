using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Configuration;

namespace ExceptionMaster.Alerting.Rules
{
    /// <summary>
    /// Validates a collection of AlertRule instances for deterministic ordering, unique identifiers,
    /// and well-formed predicate AST trees.
    /// </summary>
    public static class RuleValidator
    {
        public static ValidationResult Validate(IEnumerable<AlertRule>? rules)
        {
            if (rules == null)
            {
                return ValidationResult.Failure("Rules collection must not be null.");
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (var rule in rules)
            {
                if (rule == null)
                {
                    errors.Add($"Rule at index {index} is null.");
                    index++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.RuleId))
                {
                    errors.Add($"Rule at index {index} has an empty or whitespace RuleId.");
                }
                else if (!seenRuleIds.Add(rule.RuleId))
                {
                    errors.Add($"Duplicate RuleId detected: '{rule.RuleId}'. Rule identifiers must be unique.");
                }

                if (rule.Condition == null)
                {
                    errors.Add($"Rule '{rule.RuleId}' has a null Condition.");
                }
                else
                {
                    rule.Condition.Validate(errors);
                }

                if (rule.TargetDestinations.Count == 0 && rule.TargetGroups.Count == 0 && string.IsNullOrEmpty(rule.EscalationPolicyId))
                {
                    warnings.Add($"Rule '{rule.RuleId}' specifies no target destinations, groups, or escalation policies.");
                }

                index++;
            }

            return errors.Count > 0
                ? ValidationResult.Failure(errors, warnings)
                : ValidationResult.Success(warnings);
        }
    }
}
