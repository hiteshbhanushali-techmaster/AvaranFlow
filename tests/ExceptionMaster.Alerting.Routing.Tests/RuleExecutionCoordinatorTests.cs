using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Engine;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Escalation;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Alerting.Suppression;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class RuleExecutionCoordinatorTests
    {
        private static RuleEvaluationContext CreateContext(string key = "TIMEOUT_GATEWAY", AlertSeverity severity = AlertSeverity.Error)
        {
            return new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "TimeoutException",
                exceptionKey: key,
                severity: severity,
                category: "Trading",
                source: "OrderGateway",
                environmentName: "Production",
                machineName: "PROD-SRV-01",
                processName: "TradingHost",
                processId: 1000,
                threadId: 10,
                appVersion: "2.1.0",
                isFatal: false,
                count: 1,
                firstSeenUtc: DateTime.UtcNow.AddMinutes(-5),
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "ORD-9988",
                caller: "OrderGateway.Send",
                message: "Gateway send timed out after 5000ms");
        }

        [Fact]
        public void MandatoryScenario_ThresholdCrossed_BypassesCooldown_DeliversEscalatedLevel_ResetsCooldown()
        {
            // Setup
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(1000, clock);
            var escTracker = new BoundedEscalationTracker(1000, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            // 5 minute cooldown
            var suppPolicy = new SuppressionPolicy("TradingCooldown", TimeSpan.FromMinutes(5), SuppressionDimension.ExceptionKey);
            suppRegistry.Register(suppPolicy);

            // Escalation: Level 1 -> Notifier; Level 2 (count >= 10) -> Notifier + Email
            var escPolicy = new EscalationPolicy("TradingEscalation", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 10, targetDestinations: new[] { "Notifier", "Email" })
            });
            escRegistry.Register(escPolicy);

            var rule = new AlertRule(
                ruleId: "Rule_TradingCritical",
                name: "Trading Critical Rule",
                condition: RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error),
                suppressionPolicyId: "TradingCooldown",
                escalationPolicyId: "TradingEscalation");

            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("TIMEOUT_GATEWAY");

            // --- Event 1 ---
            // Level 1: deliver, cooldown starts
            var d1 = coordinator.Evaluate(rule, ctx);
            Assert.True(d1.IsPermitted, "Event 1 must be permitted");
            Assert.Equal(1, d1.EscalationLevel);
            Assert.False(d1.ThresholdCrossed);
            Assert.Equal(1, d1.OccurrenceCount);
            Assert.Contains("Notifier", d1.EffectiveDestinations);
            Assert.DoesNotContain("Email", d1.EffectiveDestinations);

            // --- Events 2-9 ---
            // Same level: suppressed, escalation count continues increasing
            for (int i = 2; i <= 9; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10)); // still within 5 min cooldown
                var di = coordinator.Evaluate(rule, ctx);
                Assert.False(di.IsPermitted, $"Event {i} must be suppressed by cooldown");
                Assert.Equal(1, di.EscalationLevel);
                Assert.False(di.ThresholdCrossed);
                Assert.Equal(i, di.OccurrenceCount);
            }

            // --- Event 10 ---
            // Threshold crossed: Level 2!
            // MUST bypass existing cooldown
            // MUST deliver Level 2 destinations
            // Cooldown reset for Level 2
            clock.Advance(TimeSpan.FromSeconds(10)); // total elapsed ~1.5 min, well within original 5 min cooldown
            var d10 = coordinator.Evaluate(rule, ctx);
            Assert.True(d10.IsPermitted, "Event 10 must bypass cooldown and be permitted");
            Assert.Equal(2, d10.EscalationLevel);
            Assert.True(d10.ThresholdCrossed, "Event 10 must flag ThresholdCrossed = true");
            Assert.Equal(10, d10.OccurrenceCount);
            Assert.Contains("Notifier", d10.EffectiveDestinations);
            Assert.Contains("Email", d10.EffectiveDestinations);
            Assert.Equal("EscalationThresholdBypass", d10.Reason);

            // --- Subsequent Level 2 events (Events 11 to 15) ---
            // Suppressed while Level 2 cooldown remains active
            for (int i = 11; i <= 15; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10)); // within fresh 5 min cooldown established at Event 10
                var di = coordinator.Evaluate(rule, ctx);
                Assert.False(di.IsPermitted, $"Event {i} at Level 2 must be suppressed while Level 2 cooldown is active");
                Assert.Equal(2, di.EscalationLevel);
                Assert.False(di.ThresholdCrossed);
                Assert.Equal(i, di.OccurrenceCount);
            }

            // --- Event 16 after Level 2 cooldown expires (advance 5 minutes) ---
            clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
            var d16 = coordinator.Evaluate(rule, ctx);
            Assert.True(d16.IsPermitted, "Event 16 after Level 2 cooldown expiry must be permitted");
            Assert.Equal(2, d16.EscalationLevel);
            Assert.False(d16.ThresholdCrossed);
            Assert.Equal(16, d16.OccurrenceCount);
            Assert.Contains("Email", d16.EffectiveDestinations);
        }

        [Fact]
        public void RuleIsolation_RuleAAndRuleB_EvaluatingSameExceptionContext_DoNotInterfere()
        {
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(1000, clock);
            var escTracker = new BoundedEscalationTracker(1000, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            var suppPolicy = new SuppressionPolicy("P_Supp", TimeSpan.FromMinutes(5), SuppressionDimension.ExceptionKey);
            suppRegistry.Register(suppPolicy);

            var escPolicy = new EscalationPolicy("P_Esc", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 5, targetDestinations: new[] { "Notifier", "Email" })
            });
            escRegistry.Register(escPolicy);

            var ruleA = new AlertRule("Rule_A", "Rule A", RuleCondition.Category(PredicateOperator.Equals, "Trading"), suppressionPolicyId: "P_Supp", escalationPolicyId: "P_Esc");
            var ruleB = new AlertRule("Rule_B", "Rule B", RuleCondition.Category(PredicateOperator.Equals, "Trading"), suppressionPolicyId: "P_Supp", escalationPolicyId: "P_Esc");

            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("SHARED_KEY_123");

            // Evaluate Rule A 3 times
            var a1 = coordinator.Evaluate(ruleA, ctx);
            var a2 = coordinator.Evaluate(ruleA, ctx);
            var a3 = coordinator.Evaluate(ruleA, ctx);

            Assert.True(a1.IsPermitted);
            Assert.False(a2.IsPermitted, "Rule A event 2 must be suppressed");
            Assert.False(a3.IsPermitted, "Rule A event 3 must be suppressed");
            Assert.Equal(3, a3.OccurrenceCount);

            // Now evaluate Rule B with the EXACT SAME ExceptionContext!
            // Rule B must NOT be suppressed by Rule A's cooldown!
            // Rule B's count must start at 1, completely isolated from Rule A!
            var b1 = coordinator.Evaluate(ruleB, ctx);

            Assert.True(b1.IsPermitted, "Rule B must NOT be affected by Rule A's cooldown!");
            Assert.Equal(1, b1.OccurrenceCount);
            Assert.Equal(1, b1.EscalationLevel);

            // Rule B event 2 is suppressed by Rule B's own cooldown, count=2
            var b2 = coordinator.Evaluate(ruleB, ctx);
            Assert.False(b2.IsPermitted);
            Assert.Equal(2, b2.OccurrenceCount);

            // Rule A's count is still independent
            Assert.True(escTracker.TryGetState(new EscalationKey("Rule_A", "P_Esc", "SHARED_KEY_123"), out var stateA));
            Assert.True(escTracker.TryGetState(new EscalationKey("Rule_B", "P_Esc", "SHARED_KEY_123"), out var stateB));
            Assert.Equal(3, stateA!.OccurrenceCount);
            Assert.Equal(2, stateB!.OccurrenceCount);
        }

        [Fact]
        public void Suppression_NeverPrevents_EscalationStateFromUpdating()
        {
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(1000, clock);
            var escTracker = new BoundedEscalationTracker(1000, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            suppRegistry.Register(new SuppressionPolicy("P_Supp", TimeSpan.FromMinutes(10)));
            escRegistry.Register(new EscalationPolicy("P_Esc", new[] { EscalationStep.Initial() }));

            var rule = new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), suppressionPolicyId: "P_Supp", escalationPolicyId: "P_Esc");
            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("KEY_99");

            // Event 1 delivered
            coordinator.Evaluate(rule, ctx);

            // Events 2 through 10 suppressed
            for (int i = 2; i <= 10; i++)
            {
                var d = coordinator.Evaluate(rule, ctx);
                Assert.False(d.IsPermitted);
                Assert.Equal(i, d.OccurrenceCount);
            }

            // Verify tracker accurately holds count=10
            Assert.True(escTracker.TryGetState(new EscalationKey("Rule1", "P_Esc", "KEY_99"), out var state));
            Assert.Equal(10, state!.OccurrenceCount);
        }

        [Fact]
        public void MultipleEscalationLevels_CrossThreeLevelsDeterministically()
        {
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(1000, clock);
            var escTracker = new BoundedEscalationTracker(1000, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            suppRegistry.Register(new SuppressionPolicy("P_Supp", TimeSpan.FromMinutes(10)));
            escRegistry.Register(new EscalationPolicy("P_Esc", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 3, targetDestinations: new[] { "Notifier", "Email" }),
                EscalationStep.ForCount(3, 6, targetDestinations: new[] { "Notifier", "Email", "SMS" })
            }));

            var rule = new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), suppressionPolicyId: "P_Supp", escalationPolicyId: "P_Esc");
            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("KEY_MULTI");

            // Event 1 -> Level 1 (delivered)
            var d1 = coordinator.Evaluate(rule, ctx);
            Assert.True(d1.IsPermitted);
            Assert.Equal(1, d1.EscalationLevel);

            // Event 2 -> Level 1 (suppressed)
            var d2 = coordinator.Evaluate(rule, ctx);
            Assert.False(d2.IsPermitted);
            Assert.Equal(1, d2.EscalationLevel);

            // Event 3 -> Level 2 (threshold crossed -> bypass cooldown -> delivered)
            var d3 = coordinator.Evaluate(rule, ctx);
            Assert.True(d3.IsPermitted);
            Assert.Equal(2, d3.EscalationLevel);
            Assert.True(d3.ThresholdCrossed);
            Assert.Contains("Email", d3.EffectiveDestinations);

            // Event 4, 5 -> Level 2 (suppressed)
            var d4 = coordinator.Evaluate(rule, ctx);
            var d5 = coordinator.Evaluate(rule, ctx);
            Assert.False(d4.IsPermitted);
            Assert.False(d5.IsPermitted);

            // Event 6 -> Level 3 (threshold crossed -> bypass cooldown -> delivered)
            var d6 = coordinator.Evaluate(rule, ctx);
            Assert.True(d6.IsPermitted);
            Assert.Equal(3, d6.EscalationLevel);
            Assert.True(d6.ThresholdCrossed);
            Assert.Contains("SMS", d6.EffectiveDestinations);
        }

        [Fact]
        public void MissingPolicy_FallsBackGracefullyWithoutCrashing()
        {
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(100, clock);
            var escTracker = new BoundedEscalationTracker(100, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            // Rule references unregistered policies
            var rule = new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                targetDestinations: new[] { "DefaultDest" },
                suppressionPolicyId: "NonExistentSupp",
                escalationPolicyId: "NonExistentEsc");

            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("KEY_FALLBACK");

            var decision = coordinator.Evaluate(rule, ctx);

            Assert.True(decision.IsPermitted);
            Assert.Equal(1, decision.EscalationLevel);
            Assert.Contains("DefaultDest", decision.EffectiveDestinations);
        }
    }
}
