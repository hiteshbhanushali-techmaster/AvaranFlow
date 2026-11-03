using System;
using System.Collections.Generic;
using System.Linq;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Core;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class RuleEngineTests
    {
        private static ExceptionContext CreateContext(
            string category = "OrderModification",
            string exceptionType = "BrokerRejectedException",
            bool isFatal = false,
            long count = 17,
            string correlationId = "ORD-928374",
            Dictionary<string, string>? metadata = null)
        {
            var ex = new Exception("Broker rejected order modification");
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    ex.Data[kvp.Key] = kvp.Value;
                }
            }

            return new ExceptionContext(
                exception: ex,
                message: ex.Message,
                exceptionType: exceptionType,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow.AddMinutes(-10),
                lastSeenUtc: DateTime.UtcNow,
                count: count,
                caller: "ModifyOrder",
                category: category,
                machineName: "TRADING-DESK-01",
                processName: "TradingTerminal",
                processId: 1024,
                threadId: 5,
                appVersion: "3.0.0",
                correlationId: correlationId,
                isFatal: isFatal,
                exceptionKey: "OrderMod_BrokerReject",
                environmentName: "Production",
                source: "BrokerEngine",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public void Rules_AreSorted_InDeterministicAscendingOrder()
        {
            var rules = new[]
            {
                new AlertRule("Rule_100", "Order 100", RuleCondition.IsFatal(true), order: 100),
                new AlertRule("Rule_Neg5", "Order -5", RuleCondition.IsFatal(true), order: -5),
                new AlertRule("Rule_50", "Order 50", RuleCondition.IsFatal(true), order: 50),
                new AlertRule("Rule_10", "Order 10", RuleCondition.IsFatal(true), order: 10)
            };

            var engine = new RuleEngine(rules);

            Assert.Equal("Rule_Neg5", engine.Rules[0].RuleId);
            Assert.Equal("Rule_10", engine.Rules[1].RuleId);
            Assert.Equal("Rule_50", engine.Rules[2].RuleId);
            Assert.Equal("Rule_100", engine.Rules[3].RuleId);
        }

        [Fact]
        public void Rules_WithSameOrder_TieBreakByRuleIdAscending()
        {
            var rules = new[]
            {
                new AlertRule("Z_Rule", "Z", RuleCondition.IsFatal(true), order: 10),
                new AlertRule("A_Rule", "A", RuleCondition.IsFatal(true), order: 10),
                new AlertRule("M_Rule", "M", RuleCondition.IsFatal(true), order: 10)
            };

            var engine = new RuleEngine(rules);

            Assert.Equal("A_Rule", engine.Rules[0].RuleId);
            Assert.Equal("M_Rule", engine.Rules[1].RuleId);
            Assert.Equal("Z_Rule", engine.Rules[2].RuleId);
        }

        [Fact]
        public void AllMatchingRulesExecute_DefaultV1Behavior_NoShortCircuitAcrossRules()
        {
            // Scenario from user prompt:
            // Event: Category == "OrderModification", Severity == Error, Count == 17
            // Rule 1: Category == "OrderModification" AND Severity >= Error -> targets Notifier, File, Database
            // Rule 2: Category == "OrderModification" AND Severity >= Warning -> targets Slack
            // Rule 3: Category == "OrderModification" AND Count >= 10 -> targets Audit
            // Rule 4: Category == "MarketConnectivity" -> Does NOT match

            var context = CreateContext(
                category: "OrderModification",
                isFatal: false,
                count: 17);

            var rule1 = new AlertRule(
                "Rule1_GenericError",
                "OrderModification Errors",
                RuleCondition.And(
                    RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                    RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error)),
                order: 10,
                targetDestinations: new[] { "Notifier", "File", "Database" });

            var rule2 = new AlertRule(
                "Rule2_AllWarnings",
                "OrderModification Warnings",
                RuleCondition.And(
                    RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                    RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Warning)),
                order: 20,
                targetDestinations: new[] { "Slack" });

            var rule3 = new AlertRule(
                "Rule3_HighCount",
                "OrderModification Recurring",
                RuleCondition.And(
                    RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                    RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, 10)),
                order: 30,
                targetDestinations: new[] { "Audit" });

            var rule4 = new AlertRule(
                "Rule4_MarketConn",
                "Market Connectivity Only",
                RuleCondition.Category(PredicateOperator.Equals, "MarketConnectivity"),
                order: 40,
                targetDestinations: new[] { "PagerDuty" });

            var engine = new RuleEngine(new[] { rule4, rule2, rule1, rule3 }); // Arbitrary registration order

            // Evaluate directly against ExceptionContext with resolved severity Error
            var matchedRules = engine.Evaluate(context, AlertSeverity.Error);

            // Exactly 3 rules matched
            Assert.Equal(3, matchedRules.Count);

            // Verified deterministic execution order: Rule1 (order 10), Rule2 (order 20), Rule3 (order 30)
            Assert.Equal("Rule1_GenericError", matchedRules[0].RuleId);
            Assert.Equal("Rule2_AllWarnings", matchedRules[1].RuleId);
            Assert.Equal("Rule3_HighCount", matchedRules[2].RuleId);

            // Rule 4 did not match
            Assert.DoesNotContain(matchedRules, r => r.RuleId == "Rule4_MarketConn");
        }

        [Fact]
        public void DisabledRules_AreNotReturned()
        {
            var context = CreateContext(category: "OrderModification");

            var disabledRule = new AlertRule(
                "Rule_Disabled", "Disabled",
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                order: 10,
                enabled: false);

            var enabledRule = new AlertRule(
                "Rule_Enabled", "Enabled",
                RuleCondition.Category(PredicateOperator.Equals, "OrderModification"),
                order: 20,
                enabled: true);

            var engine = new RuleEngine(new[] { disabledRule, enabledRule });
            var matches = engine.Evaluate(context, AlertSeverity.Error);

            Assert.Single(matches);
            Assert.Equal("Rule_Enabled", matches[0].RuleId);
        }

        [Fact]
        public void RuleCondition_ThrowingException_IsIsolated_AndDoesNotAbortEngine()
        {
            var context = CreateContext();

            var throwingCondition = new ThrowingTestCondition();
            var badRule = new AlertRule("Rule_Bad", "Throws", throwingCondition, order: 10);
            var goodRule = new AlertRule("Rule_Good", "Matches", RuleCondition.Category(PredicateOperator.Equals, "OrderModification"), order: 20);

            var engine = new RuleEngine(new[] { badRule, goodRule });
            var matches = engine.Evaluate(context, AlertSeverity.Error);

            // Good rule still evaluated and returned despite badRule throwing
            Assert.Single(matches);
            Assert.Equal("Rule_Good", matches[0].RuleId);
        }

        private sealed class ThrowingTestCondition : RuleCondition
        {
            internal override bool Evaluate(RuleEvaluationContext context)
            {
                throw new InvalidOperationException("Simulated catastrophic predicate failure");
            }

            internal override void Validate(List<string> errors)
            {
            }
        }
    }
}
