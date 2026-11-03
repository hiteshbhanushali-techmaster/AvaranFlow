using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Rules;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class PredicateOperatorTests
    {
        private static RuleEvaluationContext CreateContext(
            string category = "OrderModification",
            string exceptionType = "BrokerTimeoutException",
            AlertSeverity severity = AlertSeverity.Error,
            long count = 15,
            bool isFatal = false,
            DateTime? lastSeenUtc = null,
            Dictionary<string, string>? metadata = null)
        {
            var meta = metadata ?? new Dictionary<string, string>
            {
                { "OrderId", "ORD-12345" },
                { "Symbol", "AAPL" },
                { "Quantity", "100" }
            };

            return new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: exceptionType,
                exceptionKey: "Key123",
                severity: severity,
                category: category,
                source: "FixGateway",
                environmentName: "Production",
                machineName: "PROD-APP-01",
                processName: "OrderService",
                processId: 4321,
                threadId: 10,
                appVersion: "2.1.0",
                isFatal: isFatal,
                count: count,
                firstSeenUtc: DateTime.UtcNow.AddMinutes(-30),
                lastSeenUtc: lastSeenUtc ?? DateTime.UtcNow,
                correlationId: "CORR-999",
                caller: "ExecuteOrder",
                message: "Gateway timed out responding to order",
                metadata: meta);
        }

        #region String Comparisons

        [Fact]
        public void String_Equals_And_NotEquals_WorkCaseInsensitively()
        {
            var ctx = CreateContext(category: "OrderModification");

            var equalsCond = RuleCondition.Category(PredicateOperator.Equals, "ordermodification");
            var notEqualsCond = RuleCondition.Category(PredicateOperator.NotEquals, "MarketData");

            Assert.True(equalsCond.Evaluate(ctx));
            Assert.True(notEqualsCond.Evaluate(ctx));
            Assert.False(RuleCondition.Category(PredicateOperator.Equals, "MarketData").Evaluate(ctx));
        }

        [Fact]
        public void String_Contains_StartsWith_EndsWith()
        {
            var ctx = CreateContext(exceptionType: "BrokerTimeoutException");

            Assert.True(RuleCondition.ExceptionType(PredicateOperator.Contains, "Timeout").Evaluate(ctx));
            Assert.True(RuleCondition.ExceptionType(PredicateOperator.StartsWith, "Broker").Evaluate(ctx));
            Assert.True(RuleCondition.ExceptionType(PredicateOperator.EndsWith, "Exception").Evaluate(ctx));
            Assert.False(RuleCondition.ExceptionType(PredicateOperator.StartsWith, "Database").Evaluate(ctx));
        }

        #endregion

        #region Numeric Comparisons

        [Theory]
        [InlineData(PredicateOperator.GreaterThan, 10, true)]
        [InlineData(PredicateOperator.GreaterThan, 15, false)]
        [InlineData(PredicateOperator.GreaterThanOrEqual, 15, true)]
        [InlineData(PredicateOperator.LessThan, 20, true)]
        [InlineData(PredicateOperator.LessThan, 15, false)]
        [InlineData(PredicateOperator.LessThanOrEqual, 15, true)]
        [InlineData(PredicateOperator.Equals, 15, true)]
        [InlineData(PredicateOperator.NotEquals, 15, false)]
        public void Numeric_Comparisons_WorkCorrectly(PredicateOperator op, long target, bool expected)
        {
            var ctx = CreateContext(count: 15);
            var cond = RuleCondition.Count(op, target);

            Assert.Equal(expected, cond.Evaluate(ctx));
        }

        #endregion

        #region Severity Comparisons

        [Theory]
        [InlineData(AlertSeverity.Warning, PredicateOperator.GreaterThanOrEqual, AlertSeverity.Warning, true)]
        [InlineData(AlertSeverity.Error, PredicateOperator.GreaterThanOrEqual, AlertSeverity.Warning, true)]
        [InlineData(AlertSeverity.Info, PredicateOperator.GreaterThanOrEqual, AlertSeverity.Warning, false)]
        [InlineData(AlertSeverity.Critical, PredicateOperator.Equals, AlertSeverity.Critical, true)]
        [InlineData(AlertSeverity.Critical, PredicateOperator.LessThan, AlertSeverity.Error, false)]
        public void Severity_Comparisons_FollowOrdinalPrecedence(
            AlertSeverity actual, PredicateOperator op, AlertSeverity target, bool expected)
        {
            var ctx = CreateContext(severity: actual);
            var cond = RuleCondition.Severity(op, target);

            Assert.Equal(expected, cond.Evaluate(ctx));
        }

        #endregion

        #region Boolean Comparisons

        [Fact]
        public void Boolean_Comparisons_WorkCorrectly()
        {
            var fatalCtx = CreateContext(isFatal: true);
            var nonFatalCtx = CreateContext(isFatal: false);

            Assert.True(RuleCondition.IsFatal(true).Evaluate(fatalCtx));
            Assert.False(RuleCondition.IsFatal(true).Evaluate(nonFatalCtx));
            Assert.True(RuleCondition.IsFatal(false).Evaluate(nonFatalCtx));
        }

        #endregion

        #region Timestamp Comparisons

        [Fact]
        public void Timestamp_Comparisons_WorkCorrectly()
        {
            var now = DateTime.UtcNow;
            var ctx = CreateContext(lastSeenUtc: now);

            var pastCondition = RuleCondition.Field(RuleField.LastSeenUtc, PredicateOperator.GreaterThan, now.AddMinutes(-5));
            var futureCondition = RuleCondition.Field(RuleField.LastSeenUtc, PredicateOperator.LessThan, now.AddMinutes(5));
            var impossibleCondition = RuleCondition.Field(RuleField.LastSeenUtc, PredicateOperator.GreaterThan, now.AddMinutes(5));

            Assert.True(pastCondition.Evaluate(ctx));
            Assert.True(futureCondition.Evaluate(ctx));
            Assert.False(impossibleCondition.Evaluate(ctx));
        }

        #endregion

        #region Exists & Missing Metadata

        [Fact]
        public void Exists_ReturnsTrueForPresentMetadataKey()
        {
            var ctx = CreateContext();
            var existsCond = RuleCondition.MetadataExists("OrderId");
            var missingCond = RuleCondition.MetadataExists("NonExistentKey");

            Assert.True(existsCond.Evaluate(ctx));
            Assert.False(missingCond.Evaluate(ctx));
        }

        [Fact]
        public void MissingMetadata_ReturnsFalseForEquals_AndTrueForNotEquals()
        {
            var ctx = CreateContext();
            var equalsMissing = RuleCondition.Metadata("MissingKey", PredicateOperator.Equals, "Value");
            var notEqualsMissing = RuleCondition.Metadata("MissingKey", PredicateOperator.NotEquals, "Value");

            Assert.False(equalsMissing.Evaluate(ctx));
            Assert.True(notEqualsMissing.Evaluate(ctx));
        }

        [Fact]
        public void InvalidTypeComparisons_SafelyReturnFalseWithoutThrowing()
        {
            var ctx = CreateContext();
            // Comparing numeric field to an unparseable string
            var malformedNumeric = RuleCondition.Field(RuleField.Count, PredicateOperator.GreaterThan, "NotANumber");
            var malformedSeverity = RuleCondition.Field(RuleField.Severity, PredicateOperator.Equals, "InvalidSeverityEnumName");

            Assert.False(malformedNumeric.Evaluate(ctx));
            Assert.False(malformedSeverity.Evaluate(ctx));
        }

        [Fact]
        public void NullContext_ReturnsFalseSafely()
        {
            var cond = RuleCondition.Category(PredicateOperator.Equals, "OrderModification");
            Assert.False(cond.Evaluate(null!));
        }

        #endregion
    }
}
