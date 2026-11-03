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
using AvaranFlow.Alerting.Metrics;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class GenerationIntegrationTests
    {
        private static ExceptionContext CreateContext(string key)
        {
            var ex = new InvalidOperationException("Test generation");
            return new ExceptionContext(
                exception: ex,
                message: "Test message",
                exceptionType: ex.GetType().FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Method",
                category: "Trading",
                machineName: "Host",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: "123",
                isFatal: false,
                exceptionKey: key,
                environmentName: "Test",
                source: "Test",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public void AdmittedEvent_BelongsToExactlyOneGeneration_AndCanBeClaimedAtMostOnce()
        {
            var ctx = CreateContext("KEY_SINGLE_GEN");
            var admitted = new AdmittedEvent(42, ctx, Enums.AlertSeverity.Error);

            Assert.Equal(42, admitted.GenerationId);
            Assert.False(admitted.IsClaimed);

            // First claim succeeds
            bool claimedFirst = admitted.TryClaimForProcessing();
            Assert.True(claimedFirst);
            Assert.True(admitted.IsClaimed);

            // Subsequent claims fail
            bool claimedSecond = admitted.TryClaimForProcessing();
            Assert.False(claimedSecond);
        }

        [Fact]
        public void PipelineGenerationCoordinator_AdvanceGeneration_TransitionsGenerationsSafely()
        {
            var metrics = new InternalMetricsCollector();
            var options = new AlertingOptions { WorkerConcurrency = 1 };

            Func<long, IPipelineGeneration> factory = genId =>
            {
                var dest = TestDestination.CreateSuccessful($"Dest_{genId}");
                var destReg = new DestinationRegistry(new[] { dest });
                var groupReg = new DestinationGroupRegistry(null);
                var fbReg = new FallbackChainRegistry(null);
                var suppReg = new SuppressionPolicyRegistry();
                var escReg = new EscalationPolicyRegistry();
                var rule = new AlertRule($"Rule_{genId}", $"Rule_{genId}", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { dest.Name });
                var ruleEngine = new RuleEngine(new[] { rule });
                var limiter = new DestinationConcurrencyLimiter(16);
                var fanOut = new FanOutDispatcher(destReg, sharedConcurrencyLimiter: limiter);
                var fallback = new FallbackDispatcher(destReg, sharedConcurrencyLimiter: limiter);
                var coordinator = new RuleExecutionCoordinator(new BoundedSuppressionCache(100), new BoundedEscalationTracker(100), suppReg, escReg);

                return new PipelineGeneration(genId, options, ruleEngine, coordinator, destReg, groupReg, fbReg, fanOut, fallback, metrics);
            };

            using var coordinator = new PipelineGenerationCoordinator(factory);

            Assert.Equal(1, coordinator.CurrentGenerationId);
            Assert.Equal(1, coordinator.ActiveGeneration.GenerationId);
            Assert.Equal(GenerationState.Active, coordinator.ActiveGeneration.State);

            // Admit into Generation 1
            bool adm1 = coordinator.ActiveGeneration.TryAdmit(CreateContext("EVENT_GEN1"), Enums.AlertSeverity.Error, out var ev1);
            Assert.True(adm1);
            Assert.Equal(1, ev1!.GenerationId);

            // Advance to Generation 2
            var gen2 = coordinator.AdvanceGeneration(factory, TimeSpan.FromSeconds(2));

            Assert.Equal(2, coordinator.CurrentGenerationId);
            Assert.Equal(2, coordinator.ActiveGeneration.GenerationId);
            Assert.Same(gen2, coordinator.ActiveGeneration);

            // Admit into Generation 2
            bool adm2 = coordinator.ActiveGeneration.TryAdmit(CreateContext("EVENT_GEN2"), Enums.AlertSeverity.Error, out var ev2);
            Assert.True(adm2);
            Assert.Equal(2, ev2!.GenerationId);
        }

        [Fact]
        public async Task SupersededGeneration_DrainsWithinTimeout_AndReleasesResources()
        {
            var metrics = new InternalMetricsCollector();
            var options = new AlertingOptions { WorkerConcurrency = 1 };

            var dest = TestDestination.CreateSuccessful("DestDrain", TimeSpan.FromMilliseconds(10));
            var destReg = new DestinationRegistry(new[] { dest });
            var groupReg = new DestinationGroupRegistry(null);
            var fbReg = new FallbackChainRegistry(null);
            var suppReg = new SuppressionPolicyRegistry();
            var escReg = new EscalationPolicyRegistry();
            var rule = new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestDrain" });
            var ruleEngine = new RuleEngine(new[] { rule });
            var limiter = new DestinationConcurrencyLimiter(16);
            var fanOut = new FanOutDispatcher(destReg, sharedConcurrencyLimiter: limiter);
            var fallback = new FallbackDispatcher(destReg, sharedConcurrencyLimiter: limiter);
            var coordinator = new RuleExecutionCoordinator(new BoundedSuppressionCache(100), new BoundedEscalationTracker(100), suppReg, escReg);

            var gen = new PipelineGeneration(1, options, ruleEngine, coordinator, destReg, groupReg, fbReg, fanOut, fallback, metrics);

            for (int i = 0; i < 5; i++)
            {
                gen.TryAdmit(CreateContext($"KEY_{i}"), Enums.AlertSeverity.Error, out _);
            }

            // Drain with 3s timeout
            await gen.DrainAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(GenerationState.Disposed, gen.State);
            Assert.Equal(5, dest.Invocations);
        }
    }
}
