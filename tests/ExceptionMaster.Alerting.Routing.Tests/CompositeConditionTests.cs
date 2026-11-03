using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Rules;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class CompositeConditionTests
    {
        private static RuleEvaluationContext CreateContext(
            string category = "OrderModification",
            AlertSeverity severity = AlertSeverity.Error,
            long count = 15)
        {
            return new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "BrokerException",
                exceptionKey: "Key-1",
                severity: severity,
                category: category,
                source: "FixEngine",
                environmentName: "Production",
                machineName: "SRV-1",
                processName: "OrderApp",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                isFatal: false,
                count: count,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "CORR-1",
                caller: "Caller",
                message: "Message");
        }

        [Fact]
        public void AndCondition_AllTrue_ReturnsTrue()
        {
            var ctx = CreateContext(category: "OrderModification", severity: AlertSeverity.Error, count: 10);

            var condition = RuleCondition.And(
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error),
                RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, 10));

            Assert.True(condition.Evaluate(ctx));
        }

        [Fact]
        public void AndCondition_OneFalse_ReturnsFalse_AndShortCircuits()
        {
            var ctx = CreateContext(category: "OrderModification", count: 5);

            var condition = RuleCondition.And(
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, 10)); // False

            Assert.False(condition.Evaluate(ctx));
        }

        [Fact]
        public void OrCondition_AnyTrue_ReturnsTrue_AndShortCircuits()
        {
            var ctx = CreateContext(category: "MarketData", severity: AlertSeverity.Warning);

            var condition = RuleCondition.Or(
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"), // False
                RuleCondition.Category(PredicateOperator.Equals, "MarketData"));       // True

            Assert.True(condition.Evaluate(ctx));
        }

        [Fact]
        public void OrCondition_AllFalse_ReturnsFalse()
        {
            var ctx = CreateContext(category: "General");

            var condition = RuleCondition.Or(
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                RuleCondition.Category(PredicateOperator.Equals, "MarketData"));

            Assert.False(condition.Evaluate(ctx));
        }

        [Fact]
        public void NotCondition_InvertsResult()
        {
            var ctx = CreateContext(category: "OrderModification");

            var matchCondition = RuleCondition.Category(PredicateOperator.Equals, "OrderModification");
            var notCondition = RuleCondition.Not(matchCondition);

            Assert.True(matchCondition.Evaluate(ctx));
            Assert.False(notCondition.Evaluate(ctx));
        }

        [Fact]
        public void DeeplyNested_Composition_EvaluatesDeterministically()
        {
            // Category in (OrderModification, MarketConnectivity) AND Severity >= Error AND NOT (Environment == "Development")
            var ctx = CreateContext(category: "OrderModification", severity: AlertSeverity.Critical);

            var complex = RuleCondition.And(
                RuleCondition.Or(
                    RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                    RuleCondition.Category(PredicateOperator.Equals, "MarketConnectivity")),
                RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error),
                RuleCondition.Not(RuleCondition.EnvironmentName(PredicateOperator.Equals, "Development")));

            Assert.True(complex.Evaluate(ctx));
        }

        [Fact]
        public void EmptyAnd_EvaluatesToTrue_EmptyOr_EvaluatesToFalse()
        {
            var ctx = CreateContext();

            var emptyAnd = new AndCondition(new RuleCondition[0]);
            var emptyOr = new OrCondition(new RuleCondition[0]);

            Assert.True(emptyAnd.Evaluate(ctx));
            Assert.False(emptyOr.Evaluate(ctx));
        }

        [Fact]
        public void CompositeCondition_Validation_CatchesEmptyChildren()
        {
            var emptyAnd = new AndCondition(new RuleCondition[0]);
            var emptyOr = new OrCondition(new RuleCondition[0]);

            var andErrors = new List<string>();
            emptyAnd.Validate(andErrors);
            Assert.Single(andErrors);

            var orErrors = new List<string>();
            emptyOr.Validate(orErrors);
            Assert.Single(orErrors);
        }
    }
}
