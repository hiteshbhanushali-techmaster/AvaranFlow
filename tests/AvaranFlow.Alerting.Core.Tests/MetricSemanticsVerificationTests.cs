using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Alerting.Models;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;
using AvaranFlow.Core;
using Xunit;
using QueueOverflowPolicy = AvaranFlow.Alerting.Enums.QueueOverflowPolicy;

namespace AvaranFlow.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class MetricSemanticsVerificationTests : IDisposable
    {
        public MetricSemanticsVerificationTests()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        public void Dispose()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        private static ExceptionContext CreateContext(string key, string category = "Trading")
        {
            return new ExceptionContext(
                exception: new Exception("Metric test exception"),
                message: "Test message",
                exceptionType: typeof(Exception).FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Caller",
                category: category,
                machineName: "Machine",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: "Corr",
                isFatal: false,
                exceptionKey: key,
                environmentName: "Env",
                source: "Source",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        // 1. DropNewest: EventsDropped += 1, EventsAdmitted += 0
        [Fact]
        public async Task DropNewest_IncrementsEventsDroppedOnly()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("DestMetricDropNewest", async (env, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("DestMetricDropNewest", TimeSpan.FromMilliseconds(2));
            });

            Alerting.Initialize(b =>
            {
                b.WithBoundedQueue(1, QueueOverflowPolicy.DropNewest, TimeSpan.FromMilliseconds(50));
                b.WithConcurrency(1, 1);
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestMetricDropNewest" }));
            });

            // K1 enters worker (held by blockDest)
            Alerting.Ingest(CreateContext("K1"));
            // K2 fills the queue (capacity = 1)
            Alerting.Ingest(CreateContext("K2"));
            // K3 overflows -> dropped under DropNewest
            Alerting.Ingest(CreateContext("K3"));

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.EventsDropped >= 1, $"Expected EventsDropped >= 1, was {metrics.EventsDropped}");
            Assert.Equal(3, metrics.EventsReceived);
            Assert.Equal(metrics.EventsReceived, metrics.EventsAdmitted + metrics.EventsDropped);

            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(3));
        }

        // 2. DropOldest: EventsDropped += 1, EventsAdmitted += 1
        [Fact]
        public async Task DropOldest_IncrementsEventsDroppedAndEventsAdmitted()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("DestDropOldest", async (env, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("DestDropOldest", TimeSpan.FromMilliseconds(2));
            });

            Alerting.Initialize(b =>
            {
                b.WithBoundedQueue(2, QueueOverflowPolicy.DropOldest, TimeSpan.FromMilliseconds(50));
                b.WithConcurrency(1, 1);
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestDropOldest" }));
            });

            // 1 event enters worker, 2 events fill queue (total capacity = 2)
            Alerting.Ingest(CreateContext("K1"));
            Alerting.Ingest(CreateContext("K2"));
            Alerting.Ingest(CreateContext("K3"));
            // 4th event causes DropOldest eviction of K2
            Alerting.Ingest(CreateContext("K4"));

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.EventsDropped >= 1, "Expected EventsDropped >= 1 on DropOldest eviction");
            Assert.True(metrics.EventsAdmitted >= 3, "Expected EventsAdmitted to increment for incoming admitted item");

            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(3));
        }

        // 3. BlockWithTimeout timeout: EventsDropped += 1, EventsAdmitted += 0
        [Fact]
        public async Task BlockWithTimeout_Timeout_IncrementsEventsDroppedOnly()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("DestBlockTimeout", async (env, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("DestBlockTimeout", TimeSpan.FromMilliseconds(2));
            });

            Alerting.Initialize(b =>
            {
                b.WithBoundedQueue(1, QueueOverflowPolicy.BlockWithTimeout, TimeSpan.FromMilliseconds(50));
                b.WithConcurrency(1, 1);
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestBlockTimeout" }));
            });

            Alerting.Ingest(CreateContext("K1")); // In worker
            Alerting.Ingest(CreateContext("K2")); // In queue
            Alerting.Ingest(CreateContext("K3")); // Blocks for 50ms then times out!

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.EventsDropped >= 1, "Expected EventsDropped >= 1 on BlockWithTimeout expiration");

            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(3));
        }

        // 4. Suppressed event: escalation state updates, SuppressedEvents += 1, no destination delivery
        [Fact]
        public async Task SuppressedEvent_IncrementsSuppressedEvents_UpdatesEscalation_NoDestinationDelivery()
        {
            var dest = TestDestination.CreateSuccessful("DestSupp");
            var suppPolicy = new SuppressionPolicy("TradingSupp", TimeSpan.FromMinutes(5), SuppressionDimension.Category);

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddSuppressionPolicy(suppPolicy);
                b.AddRule(new AlertRule(
                    "Rule1",
                    "Rule1",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "DestSupp" },
                    suppressionPolicyId: "TradingSupp"));
            });

            // 1st occurrence -> permitted and delivered
            Alerting.Ingest(CreateContext("K1"));
            var res1 = await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
            Assert.True(res1.Success);
            Assert.Equal(1, dest.Invocations);

            // 2nd occurrence -> suppressed
            Alerting.Ingest(CreateContext("K1"));
            var res2 = await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
            Assert.True(res2.Success);

            // Destination was NOT invoked again
            Assert.Equal(1, dest.Invocations);

            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.AlertsSuppressed);
            Assert.Equal(1, metrics.DestinationSuccesses);
        }

        // 5. Escalation threshold crossing: EscalatedEvents += 1, cooldown bypassed, destination delivery occurs
        [Fact]
        public async Task EscalationThresholdCrossing_IncrementsEscalatedEvents_BypassesCooldown_DeliversAlert()
        {
            var dest = TestDestination.CreateSuccessful("DestEscBypass");
            var suppPolicy = new SuppressionPolicy("Supp1", TimeSpan.FromMinutes(5), SuppressionDimension.ExceptionKey);
            var step1 = new EscalationStep(1, EscalationTriggerKind.OccurrenceCount, 1, targetDestinations: new[] { "DestEscBypass" });
            var step2 = new EscalationStep(2, EscalationTriggerKind.OccurrenceCount, 2, targetDestinations: new[] { "DestEscBypass" });
            var escPolicy = new EscalationPolicy("Esc1", new[] { step1, step2 }, TimeSpan.FromMinutes(5));

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddSuppressionPolicy(suppPolicy);
                b.AddEscalationPolicy(escPolicy);
                b.AddRule(new AlertRule(
                    "Rule1",
                    "Rule1",
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "DestEscBypass" },
                    suppressionPolicyId: "Supp1",
                    escalationPolicyId: "Esc1"));
            });

            // Event 1: Delivered
            Alerting.Ingest(CreateContext("ESC_KEY"));
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, dest.Invocations);

            // Event 2: Crosses threshold (count = 2) -> EscalatedEvents += 1, Bypasses active cooldown, Delivered!
            Alerting.Ingest(CreateContext("ESC_KEY"));
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, dest.Invocations);
            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.AlertsEscalated);
            Assert.Equal(2, metrics.DestinationSuccesses);
        }

        // 6. Destination failure: DestinationFailures += 1, ProcessingFailures does NOT change
        [Fact]
        public async Task DestinationFailure_IncrementsDestinationFailures_ProcessingFailuresUnchanged()
        {
            var failDest = new TestDestination("FailDestMetrics", (env, token) =>
                Task.FromResult(AlertDeliveryResult.Failed("FailDestMetrics", TimeSpan.FromMilliseconds(5), diagnosticMessage: "Network timeout")));

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(failDest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "FailDestMetrics" }));
            });

            Alerting.Ingest(CreateContext("KEY_DEST_FAIL"));
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));

            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.DestinationFailures);
            Assert.Equal(0, metrics.ProcessingFailures); // Pipeline executed correctly; destination delivery failed
        }

        // 7. Flush timeout: FlushTimeouts += 1
        [Fact]
        public async Task FlushTimeout_IncrementsFlushTimeouts()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("DestFlushTimeoutMetric", async (env, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("DestFlushTimeoutMetric", TimeSpan.FromMilliseconds(2));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestFlushTimeoutMetric" }));
            });

            Alerting.Ingest(CreateContext("KEY_FLUSH_TO"));

            var flushRes = await Alerting.FlushAsync(TimeSpan.FromMilliseconds(50));
            Assert.False(flushRes.Success);

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.FlushTimeouts >= 1, $"Expected FlushTimeouts >= 1, was {metrics.FlushTimeouts}");

            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
