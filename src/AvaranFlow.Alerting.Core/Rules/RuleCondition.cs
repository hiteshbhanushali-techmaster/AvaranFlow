using System;
using System.Collections.Generic;
using AvaranFlow.Alerting.Enums;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Abstract base class for all declarative AST rule conditions.
    /// Constructor is internal to prevent external callers from creating custom executable subclasses,
    /// enforcing strict declarative evaluation without arbitrary code execution.
    /// </summary>
    public abstract class RuleCondition : IRuleCondition
    {
        internal RuleCondition()
        {
        }

        internal abstract bool Evaluate(RuleEvaluationContext context);
        internal abstract void Validate(List<string> errors);

        bool IRuleCondition.Evaluate(RuleEvaluationContext context) => Evaluate(context);
        void IRuleCondition.Validate(List<string> errors) => Validate(errors);

        #region Factory Methods

        public static RuleCondition Field(
            RuleField field,
            PredicateOperator op,
            object? expectedValue = null,
            StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            return new FieldPredicateCondition(field, op, expectedValue, null, stringComparison);
        }

        public static RuleCondition Category(PredicateOperator op, string expectedValue)
        {
            return new FieldPredicateCondition(RuleField.Category, op, expectedValue);
        }

        public static RuleCondition ExceptionType(PredicateOperator op, string expectedValue)
        {
            return new FieldPredicateCondition(RuleField.ExceptionType, op, expectedValue);
        }

        public static RuleCondition ExceptionKey(PredicateOperator op, string expectedValue)
        {
            return new FieldPredicateCondition(RuleField.ExceptionKey, op, expectedValue);
        }

        public static RuleCondition Severity(PredicateOperator op, AlertSeverity expectedSeverity)
        {
            return new FieldPredicateCondition(RuleField.Severity, op, expectedSeverity);
        }

        public static RuleCondition Count(PredicateOperator op, long expectedCount)
        {
            return new FieldPredicateCondition(RuleField.Count, op, expectedCount);
        }

        public static RuleCondition IsFatal(bool expectedValue = true)
        {
            return new FieldPredicateCondition(RuleField.IsFatal, PredicateOperator.Equals, expectedValue);
        }

        public static RuleCondition CorrelationId(PredicateOperator op, string expectedValue)
        {
            return new FieldPredicateCondition(RuleField.CorrelationId, op, expectedValue);
        }

        public static RuleCondition EnvironmentName(PredicateOperator op, string expectedValue)
        {
            return new FieldPredicateCondition(RuleField.EnvironmentName, op, expectedValue);
        }

        public static RuleCondition Metadata(string metadataKey, PredicateOperator op, object? expectedValue = null)
        {
            if (string.IsNullOrWhiteSpace(metadataKey))
                throw new ArgumentException("MetadataKey must not be null or whitespace.", nameof(metadataKey));

            return new FieldPredicateCondition(RuleField.Metadata, op, expectedValue, metadataKey);
        }

        public static RuleCondition MetadataExists(string metadataKey)
        {
            if (string.IsNullOrWhiteSpace(metadataKey))
                throw new ArgumentException("MetadataKey must not be null or whitespace.", nameof(metadataKey));

            return new FieldPredicateCondition(RuleField.Metadata, PredicateOperator.Exists, null, metadataKey);
        }

        public static RuleCondition And(params RuleCondition[] conditions)
        {
            return new AndCondition(conditions);
        }

        public static RuleCondition And(IEnumerable<RuleCondition> conditions)
        {
            return new AndCondition(conditions);
        }

        public static RuleCondition Or(params RuleCondition[] conditions)
        {
            return new OrCondition(conditions);
        }

        public static RuleCondition Or(IEnumerable<RuleCondition> conditions)
        {
            return new OrCondition(conditions);
        }

        public static RuleCondition Not(RuleCondition condition)
        {
            return new NotCondition(condition);
        }

        #endregion
    }
}
