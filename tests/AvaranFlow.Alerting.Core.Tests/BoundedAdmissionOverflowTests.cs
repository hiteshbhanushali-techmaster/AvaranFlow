using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Engine;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Generations;
using AvaranFlow.Alerting.Metrics;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Core;
using Xunit;

using QueueOverflowPolicy = AvaranFlow.Alerting.Enums.QueueOverflowPolicy;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class BoundedAdmissionOverflowTests
    {
        private static ExceptionContext CreateContext(string key)
        {
            var ex = new InvalidOperationException("Test overflow");
            return new ExceptionContext(
                exception: ex,
                message: "Test message",
                exceptionType: ex.GetType().FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Method",
                category: "General",
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

        private static PipelineGeneration CreateGeneration(
            AlertingOptions options,
            InternalMetricsCollector metrics,
            IAlertDestination? destination = null)
        {
            var dest = destination ?? TestDestination.CreateSuccessful("Dest1");
            var destReg = new DestinationRegistry(new[] { dest });
            var groupReg = new DestinationGroupRegistry(null);
            var fbReg = new FallbackChainRegistry(null);
            var suppReg = new SuppressionPolicyRegistry();
            var escReg = new EscalationPolicyRegistry();

            var rule = new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "General"), targetDestinations: new[] { dest.Name });
            var ruleEngine = new RuleEngine(new[] { rule });

            var limiter = new DestinationConcurrencyLimiter(16);
            var fanOut = new FanOutDispatcher(destReg, sharedConcurrencyLimiter: limiter);
            var fallback = new FallbackDispatcher(destReg, sharedConcurrencyLimiter: limiter);

            var suppCache = new BoundedSuppressionCache(1000);
            var escTracker = new BoundedEscalationTracker(1000);
            var coordinator = new RuleExecutionCoordinator(suppCache, escTracker, suppReg, escReg);

            return new PipelineGeneration(
                1,
                options,
                ruleEngine,
                coordinator,
                destReg,
                groupReg,
                fbReg,
                fanOut,
                fallback,
                metrics);
        }

        [Fact]
        public void DropNewest_WhenQueueFull_RejectsNewIncomingEventsImmediately()
        {
            var metrics = new InternalMetricsCollector();
            var options = new AlertingOptions
            {
                MaxQueueCapacity = 2,
                OverflowPolicy = QueueOverflowPolicy.DropNewest,
                WorkerConcurrency = 1
            };

            // Destination holds execution so queue stays full
            var enteredDest = new TaskCompletionSource<bool>();
            var blocker = new TaskCompletionSource<bool>();
            var slowDest = new TestDestination("SlowDest", async (env, ct) =>
            {
                enteredDest.TrySetResult(true);
                await blocker.Task.ConfigureAwait(false);
                return Models.AlertDeliveryResult.Succeeded("SlowDest", TimeSpan.Zero);
            });

            using var gen = CreateGeneration(options, metrics, slowDest);

            // Fill queue
            // Event 1 dequeued by worker and blocked in destination
            gen.TryAdmit(CreateContext("KEY_1"), Enums.AlertSeverity.Error, out var adm1);
            Assert.True(enteredDest.Task.Wait(5000), "Worker must dequeue event 1 and enter destination.");

            // Event 2 & 3 fill capacity (capacity = 2)
            bool adm2 = gen.TryAdmit(CreateContext("KEY_2"), Enums.AlertSeverity.Error, out _);
            bool adm3 = gen.TryAdmit(CreateContext("KEY_3"), Enums.AlertSeverity.Error, out _);

            Assert.True(adm2);
            Assert.True(adm3);

            // Event 4 arrives while queue is saturated: DropNewest must reject!
            bool adm4 = gen.TryAdmit(CreateContext("KEY_4"), Enums.AlertSeverity.Error, out var adm4Result);

            Assert.False(adm4, "DropNewest must reject when queue capacity is saturated.");
            Assert.Null(adm4Result);

            var snapshot = metrics.CreateSnapshot();
            Assert.True(snapshot.EventsDropped >= 1);

            // Release blocker to clean up
            blocker.TrySetResult(true);
        }

        [Fact]
        public void DropOldest_WhenQueueFull_EvictsOldestBufferedEvent()
        {
            var metrics = new InternalMetricsCollector();
            var options = new AlertingOptions
            {
                MaxQueueCapacity = 2,
                OverflowPolicy = QueueOverflowPolicy.DropOldest,
                WorkerConcurrency = 1
            };

            var enteredDest = new TaskCompletionSource<bool>();
            var blocker = new TaskCompletionSource<bool>();
            var slowDest = new TestDestination("SlowDest", async (env, ct) =>
            {
                enteredDest.TrySetResult(true);
                await blocker.Task.ConfigureAwait(false);
                return Models.AlertDeliveryResult.Succeeded("SlowDest", TimeSpan.Zero);
            });

            using var gen = CreateGeneration(options, metrics, slowDest);

            // Event 1 in-flight
            gen.TryAdmit(CreateContext("KEY_1"), Enums.AlertSeverity.Error, out _);
            Assert.True(enteredDest.Task.Wait(5000), "Worker must dequeue event 1 and enter destination.");

            // Fill queue with 2 & 3
            gen.TryAdmit(CreateContext("KEY_2"), Enums.AlertSeverity.Error, out _);
            gen.TryAdmit(CreateContext("KEY_3"), Enums.AlertSeverity.Error, out _);

            // Event 4: DropOldest should evict oldest (KEY_2) and accept KEY_4
            bool adm4 = gen.TryAdmit(CreateContext("KEY_4"), Enums.AlertSeverity.Error, out var adm4Result);

            Assert.True(adm4, "DropOldest should admit the new event after evicting the oldest.");
            Assert.NotNull(adm4Result);

            var snapshot = metrics.CreateSnapshot();
            Assert.True(snapshot.EventsDropped >= 1);

            blocker.TrySetResult(true);
        }

        [Fact]
        public void BlockWithTimeout_WhenQueueFull_TimesOutAndDropsIfNoCapacityFreed()
        {
            var metrics = new InternalMetricsCollector();
            var options = new AlertingOptions
            {
                MaxQueueCapacity = 2,
                OverflowPolicy = QueueOverflowPolicy.BlockWithTimeout,
                EnqueueBlockTimeout = TimeSpan.FromMilliseconds(50),
                WorkerConcurrency = 1
            };

            var blocker = new TaskCompletionSource<bool>();
            var slowDest = new TestDestination("SlowDest", async (env, ct) =>
            {
                await blocker.Task.ConfigureAwait(false);
                return Models.AlertDeliveryResult.Succeeded("SlowDest", TimeSpan.Zero);
            });

            using var gen = CreateGeneration(options, metrics, slowDest);

            gen.TryAdmit(CreateContext("KEY_1"), Enums.AlertSeverity.Error, out _);
            Thread.Sleep(50);

            gen.TryAdmit(CreateContext("KEY_2"), Enums.AlertSeverity.Error, out _);
            gen.TryAdmit(CreateContext("KEY_3"), Enums.AlertSeverity.Error, out _);

            // Event 4 blocks for 50ms and times out
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool adm4 = gen.TryAdmit(CreateContext("KEY_4"), Enums.AlertSeverity.Error, out _);
            sw.Stop();

            Assert.False(adm4);
            Assert.True(sw.ElapsedMilliseconds >= 30, $"Should have waited up to timeout. Waited {sw.ElapsedMilliseconds}ms.");

            var snapshot = metrics.CreateSnapshot();
            Assert.True(snapshot.EventsDropped >= 1);

            blocker.TrySetResult(true);
        }
    }
}
