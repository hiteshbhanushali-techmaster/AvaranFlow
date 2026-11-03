using System;
using System.Collections.Generic;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Rules;
using Xunit;

namespace AvaranFlow.Alerting.Routing.Tests
{
    public class RuleValidationTests
    {
        [Fact]
        public void ValidRules_PassValidation()
        {
            var rules = new[]
            {
                new AlertRule("Rule1", "Trading Rule", RuleCondition.Category(PredicateOperator.Equals, "Trading"), 10, true, new[] { "Notifier" }),
                new AlertRule("Rule2", "Audit Rule", RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error), 20, true, new[] { "Email" })
            };

            var result = RuleValidator.Validate(rules);
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void DuplicateRuleIds_FailValidation()
        {
            var rules = new[]
            {
                new AlertRule("DUPLICATE_ID", "First Rule", RuleCondition.IsFatal(true)),
                new AlertRule("duplicate_id", "Second Rule", RuleCondition.IsFatal(false)) // Case-insensitive duplicate
            };

            var result = RuleValidator.Validate(rules);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Duplicate RuleId"));
        }

        [Fact]
        public void NullOrEmptyRuleCollection_HandledSafely()
        {
            var nullResult = RuleValidator.Validate(null);
            Assert.False(nullResult.IsValid);

            var emptyResult = RuleValidator.Validate(new AlertRule[0]);
            Assert.True(emptyResult.IsValid);
        }

        [Fact]
        public void MissingMetadataKey_FailsValidation()
        {
            // Terminal condition with RuleField.Metadata but null/empty MetadataKey
            var condition = new FieldPredicateCondition(RuleField.Metadata, PredicateOperator.Equals, "Expected", metadataKey: "");
            var rule = new AlertRule("Rule_MissingKey", "Rule", condition);

            var result = RuleValidator.Validate(new[] { rule });
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("MetadataKey"));
        }

        [Fact]
        public void UndefinedEnumValues_FailValidation()
        {
            var condition = new FieldPredicateCondition((RuleField)999, (PredicateOperator)888, "Val");
            var rule = new AlertRule("Rule_BadEnums", "Rule", condition);

            var result = RuleValidator.Validate(new[] { rule });
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("RuleField"));
            Assert.Contains(result.Errors, e => e.Contains("PredicateOperator"));
        }
    }
}
