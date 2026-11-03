using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Engine;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Alerting.Generations;
using AvaranFlow.Alerting.Infrastructure;
using AvaranFlow.Alerting.Metrics;
using AvaranFlow.Alerting.Models;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Severity;
using AvaranFlow.Alerting.Suppression;
using AvaranFlow.Core;
using Xunit;
using FallbackChain = AvaranFlow.Alerting.Destinations.FallbackChain;

namespace AvaranFlow.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class PerRuleFailureIsolationTests : IDisposable
    {
        public PerRuleFailureIsolationTests()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        public void Dispose()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        private static ExceptionContext CreateContext(string category = "Trading")
        {
            return new ExceptionContext(
                exception: new InvalidOperationException("Failure isolation test exception"),
                message: "Test message",
                exceptionType: typeof(InvalidOperationException).FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "OrderEngine.Submit",
                category: category,
                machineName: "Machine",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: "Corr",
                isFatal: false,
                exceptionKey: "KEY_FAIL_ISOLATION",
                environmentName: "Env",
                source: "Source",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public async Task DestinationFailureInRuleA_DoesNotPreventRuleBFromDelivering()
        {
            var failingDest = new TestDestination("FailingDest", (env, token) =>
                Task.FromResult(AlertDeliveryResult.Failed("FailingDest", TimeSpan.FromMilliseconds(5), diagnosticMessage: "Simulated destination network failure")));

            var succeedingDest = TestDestination.CreateSuccessful("SucceedingDest");

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(failingDest);
                b.RegisterDestination(succeedingDest);

                // Rule A matches and routes to failing destination
                b.AddRule(new AlertRule("RuleA", "Rule A",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "FailingDest" },
                    order: 10));

                // Rule B matches and routes to succeeding destination
                b.AddRule(new AlertRule("RuleB", "Rule B",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "SucceedingDest" },
                    order: 20));
            });

            Alerting.Ingest(CreateContext("Trading"));

            var flushResult = await Alerting.FlushAsync(TimeSpan.FromSeconds(3));
            Assert.True(flushResult.Success);

            // Verify Rule B executed and delivered!
            Assert.Equal(1, succeedingDest.Invocations);
            Assert.Equal(1, failingDest.Invocations);

            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.DestinationFailures);
            Assert.Equal(1, metrics.DestinationSuccesses);
            // Internal processing did not fail; destination delivery did:
            Assert.Equal(0, metrics.ProcessingFailures);
        }

        [Fact]
        public async Task DestinationExceptionInRuleA_DoesNotPreventRuleBFromDelivering()
        {
            var throwingDest = new TestDestination("ThrowingDest", (env, token) =>
                throw new TimeoutException("Simulated destination timeout throw"));

            var succeedingDest = TestDestination.CreateSuccessful("SucceedingDest2");

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(throwingDest);
                b.RegisterDestination(succeedingDest);

                b.AddRule(new AlertRule("RuleA", "Rule A",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "ThrowingDest" },
                    order: 10));

                b.AddRule(new AlertRule("RuleB", "Rule B",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "SucceedingDest2" },
                    order: 20));
            });

            Alerting.Ingest(CreateContext("Trading"));

            var flushResult = await Alerting.FlushAsync(TimeSpan.FromSeconds(3));
            Assert.True(flushResult.Success);

            Assert.Equal(1, succeedingDest.Invocations);

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.DestinationFailures >= 1);
        }

        [Fact]
        public async Task CoordinatorEvaluationFailureInRuleA_DoesNotPreventRuleBFromDelivering()
        {
            var metrics = new InternalMetricsCollector();
            var succeedingDest = TestDestination.CreateSuccessful("DestSuccess");
            var destRegistry = new DestinationRegistry(new[] { succeedingDest });
            var groupRegistry = new DestinationGroupRegistry(new DestinationGroup[0]);
            var fallbackRegistry = new FallbackChainRegistry(new FallbackChain[0]);
            var limiter = new DestinationConcurrencyLimiter(10);
            var fanOut = new FanOutDispatcher(destRegistry, groupRegistry, FanOutDispatchMode.Sequential, TimeSpan.FromSeconds(2), 10, limiter);
            var fallback = new FallbackDispatcher(destRegistry, fallbackRegistry, TimeSpan.FromSeconds(2), 10, limiter);

            var ruleA = new AlertRule("RuleA", "Rule A",
                RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                targetDestinations: new[] { "DestSuccess" },
                order: 10);

            var ruleB = new AlertRule("RuleB", "Rule B",
                RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                targetDestinations: new[] { "DestSuccess" },
                order: 20);

            var ruleEngine = new RuleEngine(new[] { ruleA, ruleB });

            // Coordinator that deliberately fails when evaluating Rule A, but succeeds for Rule B
            var throwingCoordinator = new FaultyRuleExecutionCoordinator("RuleA");

            var gen = new PipelineGeneration(
                generationId: 1,
                options: new AlertingOptions { WorkerConcurrency = 1 },
                ruleEngine: ruleEngine,
                coordinator: throwingCoordinator,
                destinationRegistry: destRegistry,
                groupRegistry: groupRegistry,
                fallbackRegistry: fallbackRegistry,
                fanOutDispatcher: fanOut,
                fallbackDispatcher: fallback,
                metrics: metrics);

            Assert.True(gen.TryAdmit(CreateContext("Trading"), AlertSeverity.Warning, out _));

            var flushRes = await gen.FlushAsync(TimeSpan.FromSeconds(3));
            Assert.True(flushRes.Success);

            // Rule B succeeded despite Rule A throwing in coordinator evaluation!
            Assert.Equal(1, succeedingDest.Invocations);
            Assert.Equal(1, metrics.CreateSnapshot().ProcessingFailures);

            gen.Dispose();
        }

        private sealed class FaultyRuleExecutionCoordinator : IRuleExecutionCoordinator
        {
            private readonly string _failingRuleId;

            public FaultyRuleExecutionCoordinator(string failingRuleId)
            {
                _failingRuleId = failingRuleId;
            }

            public RuleExecutionDecision Evaluate(AlertRule rule, RuleEvaluationContext context)
            {
                if (rule.RuleId == _failingRuleId)
                {
                    throw new InvalidOperationException($"Simulated coordinator evaluation failure for rule {rule.RuleId}");
                }

                return new RuleExecutionDecision(
                    ruleId: rule.RuleId,
                    isPermitted: true,
                    reason: "Permitted",
                    escalationLevel: 1,
                    thresholdCrossed: false,
                    occurrenceCount: 1,
                    effectiveDestinations: rule.TargetDestinations,
                    effectiveGroups: rule.TargetGroups,
                    firstOccurrenceUtc: DateTime.UtcNow,
                    lastOccurrenceUtc: DateTime.UtcNow);
            }
        }
    }
}
