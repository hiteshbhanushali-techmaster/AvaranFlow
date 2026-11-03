using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Engine;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Escalation;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Alerting.Suppression;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class SuppressionAndEscalationStressTests
    {
        private static RuleEvaluationContext CreateContext(string key, AlertSeverity severity = AlertSeverity.Error)
        {
            return new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "TestException",
                exceptionKey: key,
                severity: severity,
                category: "StressTest",
                source: "StressApp",
                environmentName: "Test",
                machineName: "StressHost",
                processName: "StressProc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                isFatal: false,
                count: 1,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                correlationId: Guid.NewGuid().ToString("N"),
                caller: "StressMethod",
                message: "High concurrency stress test");
        }

        [Fact]
        public async Task HighConcurrency_SameKey_ZeroLostIncrements()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(1000, clock);
            var policy = new EscalationPolicy("P_Stress", new[] { EscalationStep.Initial() });
            var key = new EscalationKey("Rule_Stress", "P_Stress", "HIGH_CONTENTION_KEY");
            var ctx = CreateContext("HIGH_CONTENTION_KEY");

            const int workerCount = 20;
            const int iterationsPerWorker = 100;
            const int expectedTotal = workerCount * iterationsPerWorker;

            var tasks = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < iterationsPerWorker; j++)
                    {
                        tracker.UpdateAndEvaluate(key, policy, ctx);
                    }
                });
            }

            await Task.WhenAll(tasks);

            Assert.True(tracker.TryGetState(key, out var state));
            Assert.Equal(expectedTotal, state!.OccurrenceCount);
        }

        [Fact]
        public async Task HighConcurrency_ManyDifferentKeys_TracksAllConcurrently()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(5000, clock);
            var policy = new EscalationPolicy("P_Many", new[] { EscalationStep.Initial() });

            const int distinctKeys = 50;
            const int updatesPerKey = 20;

            var tasks = new List<Task>();
            for (int k = 0; k < distinctKeys; k++)
            {
                string keyName = $"KEY_{k:D3}";
                tasks.Add(Task.Run(() =>
                {
                    var escKey = new EscalationKey("Rule_Multi", "P_Many", keyName);
                    var ctx = CreateContext(keyName);
                    for (int u = 0; u < updatesPerKey; u++)
                    {
                        tracker.UpdateAndEvaluate(escKey, policy, ctx);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            for (int k = 0; k < distinctKeys; k++)
            {
                string keyName = $"KEY_{k:D3}";
                var escKey = new EscalationKey("Rule_Multi", "P_Many", keyName);
                Assert.True(tracker.TryGetState(escKey, out var state), $"Key {keyName} should be tracked.");
                Assert.Equal(updatesPerKey, state!.OccurrenceCount);
            }
        }

        [Fact]
        public async Task HighConcurrency_SameExceptionKey_AcrossMultipleRules_MaintainsIndependence()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(1000, clock);
            var policy = new EscalationPolicy("P_Shared", new[] { EscalationStep.Initial() });
            const string sharedKey = "SHARED_EXCEPTION_KEY";
            var ctx = CreateContext(sharedKey);

            const int ruleCount = 5;
            const int updatesPerRule = 50;

            var tasks = new List<Task>();
            for (int r = 0; r < ruleCount; r++)
            {
                string ruleId = $"Rule_{r}";
                tasks.Add(Task.Run(() =>
                {
                    var escKey = new EscalationKey(ruleId, "P_Shared", sharedKey);
                    for (int u = 0; u < updatesPerRule; u++)
                    {
                        tracker.UpdateAndEvaluate(escKey, policy, ctx);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            for (int r = 0; r < ruleCount; r++)
            {
                string ruleId = $"Rule_{r}";
                var escKey = new EscalationKey(ruleId, "P_Shared", sharedKey);
                Assert.True(tracker.TryGetState(escKey, out var state));
                Assert.Equal(updatesPerRule, state!.OccurrenceCount);
            }
        }

        [Fact]
        public async Task ConcurrentSuppressionEvaluation_ExactlyOneDeliveryAcquiredPerCooldownWindow()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(1000, clock);
            var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "RACE_KEY");

            const int concurrentCallers = 25;
            var barrier = new Barrier(concurrentCallers);
            var results = new ConcurrentBag<bool>();

            var tasks = new Task[concurrentCallers];
            for (int i = 0; i < concurrentCallers; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    bool acquired = cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out _);
                    results.Add(acquired);
                });
            }

            await Task.WhenAll(tasks);

            int permittedCount = results.Count(x => x == true);
            int suppressedCount = results.Count(x => x == false);

            Assert.Equal(1, permittedCount);
            Assert.Equal(concurrentCallers - 1, suppressedCount);
        }

        [Fact]
        public async Task ThresholdCrossingUnderConcurrency_ExactlyOneThresholdCrossedPerTransition()
        {
            var clock = new TestClock();
            var suppCache = new BoundedSuppressionCache(1000, clock);
            var escTracker = new BoundedEscalationTracker(1000, clock);
            var suppRegistry = new SuppressionPolicyRegistry();
            var escRegistry = new EscalationPolicyRegistry();

            suppRegistry.Register(new SuppressionPolicy("P_Supp", TimeSpan.FromMinutes(5)));
            escRegistry.Register(new EscalationPolicy("P_Esc", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 10, targetDestinations: new[] { "Notifier", "Email" })
            }));

            var rule = new AlertRule("Rule_Race", "Race Rule", RuleCondition.Category(PredicateOperator.Equals, "StressTest"),
                suppressionPolicyId: "P_Supp",
                escalationPolicyId: "P_Esc");

            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppRegistry, escRegistry, clock);
            var ctx = CreateContext("THRESHOLD_RACE_KEY");

            const int totalEvents = 30; // Will cross threshold at count 10
            var decisions = new ConcurrentBag<RuleExecutionDecision>();

            var tasks = new List<Task>();
            for (int i = 0; i < totalEvents; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var d = coordinator.Evaluate(rule, ctx);
                    decisions.Add(d);
                }));
            }

            await Task.WhenAll(tasks);

            // Verify total occurrences is 30 with zero lost increments
            Assert.True(escTracker.TryGetState(new EscalationKey("Rule_Race", "P_Esc", "THRESHOLD_RACE_KEY"), out var state));
            Assert.Equal(totalEvents, state!.OccurrenceCount);

            // Exactly ONE decision should have ThresholdCrossed = true!
            int thresholdCrossedCount = decisions.Count(d => d.ThresholdCrossed);
            Assert.Equal(1, thresholdCrossedCount);

            // Exactly TWO decisions should be permitted: Event 1 (initial deliver) and Event 10 (threshold bypass)
            int permittedCount = decisions.Count(d => d.IsPermitted);
            Assert.Equal(2, permittedCount);

            // The remaining 28 events should be suppressed
            int suppressedCount = decisions.Count(d => !d.IsPermitted);
            Assert.Equal(totalEvents - 2, suppressedCount);
        }
    }
}
