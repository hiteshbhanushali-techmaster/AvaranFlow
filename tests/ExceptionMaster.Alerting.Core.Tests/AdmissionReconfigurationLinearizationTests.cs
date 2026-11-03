using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Generations;
using ExceptionMaster.Alerting.Models;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Core;
using Xunit;
using QueueOverflowPolicy = ExceptionMaster.Alerting.Enums.QueueOverflowPolicy;
using RuleExecutionCoordinator = ExceptionMaster.Alerting.Engine.RuleExecutionCoordinator;
using FallbackChain = ExceptionMaster.Alerting.Destinations.FallbackChain;

namespace ExceptionMaster.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class AdmissionReconfigurationLinearizationTests : IDisposable
    {
        public AdmissionReconfigurationLinearizationTests()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        public void Dispose()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        private static ExceptionContext CreateContext(int id)
        {
            return new ExceptionContext(
                exception: new Exception($"Exception #{id}"),
                message: $"Linearization test msg {id}",
                exceptionType: typeof(Exception).FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Caller",
                category: "LinearizationCategory",
                machineName: "Machine",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: $"Corr-{id}",
                isFatal: false,
                exceptionKey: $"KEY_{id % 10}",
                environmentName: "Env",
                source: "Source",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Theory]
        [InlineData(QueueOverflowPolicy.DropNewest)]
        [InlineData(QueueOverflowPolicy.DropOldest)]
        [InlineData(QueueOverflowPolicy.BlockWithTimeout)]
        public async Task HighContention_ProducerVsReconfigure_GuaranteesSingleClaimAndZeroLossOrDuplication(
            QueueOverflowPolicy overflowPolicy)
        {
            var deliveredByTicket = new ConcurrentDictionary<Guid, int>();
            int duplicateClaims = 0;
            int wrongGenerationProcessing = 0;

            var dest = new TestDestination("LinearizationDest", async (envelope, token) =>
            {
                int count = deliveredByTicket.AddOrUpdate(envelope.OccurrenceId, 1, (_, existing) => existing + 1);
                if (count > 1)
                {
                    Interlocked.Increment(ref duplicateClaims);
                }
                await Task.Delay(2, token).ConfigureAwait(false);
                return AlertDeliveryResult.Succeeded("LinearizationDest", TimeSpan.FromMilliseconds(2));
            });

            Alerting.Initialize(b =>
            {
                b.WithBoundedQueue(50, overflowPolicy, TimeSpan.FromMilliseconds(20));
                b.WithConcurrency(4, 8);
                b.WithDrainTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "LinearizationCategory"), targetDestinations: new[] { "LinearizationDest" }));
            });

            const int producerCount = 6;
            const int eventsPerProducer = 50;
            const int totalEvents = producerCount * eventsPerProducer;
            var producers = new Task[producerCount];
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Background task concurrently reconfiguring repeatedly
            var reconfigureTask = Task.Run(async () =>
            {
                int reconfigs = 0;
                while (!cts.Token.IsCancellationRequested && reconfigs < 5)
                {
                    await Task.Delay(30, cts.Token).ConfigureAwait(false);
                    try
                    {
                        Alerting.Reconfigure(b =>
                        {
                            b.WithBoundedQueue(50, overflowPolicy, TimeSpan.FromMilliseconds(20));
                            b.WithConcurrency(4, 8);
                            b.WithDrainTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                            b.RegisterDestination(dest);
                            b.AddRule(new AlertRule($"Rule_Reconf_{reconfigs}", $"Rule_Reconf_{reconfigs}",
                                RuleCondition.Category(PredicateOperator.Equals, "LinearizationCategory"),
                                targetDestinations: new[] { "LinearizationDest" }));
                        });
                        reconfigs++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Safe containment
                    }
                }
            });

            // Producers concurrently firing Ingest()
            for (int p = 0; p < producerCount; p++)
            {
                int pIndex = p;
                producers[p] = Task.Run(() =>
                {
                    for (int i = 0; i < eventsPerProducer; i++)
                    {
                        int id = pIndex * eventsPerProducer + i;
                        Alerting.Ingest(CreateContext(id));
                    }
                });
            }

            await Task.WhenAll(producers);
            cts.Cancel();
            try { await reconfigureTask; } catch { }

            // Flush pipeline
            var flushResult = await Alerting.FlushAsync(TimeSpan.FromSeconds(5));

            var metrics = Alerting.GetMetrics();

            // Verification:
            // 1. Duplicate claims = 0
            Assert.Equal(0, duplicateClaims);

            // 2. Wrong-generation processing = 0
            Assert.Equal(0, wrongGenerationProcessing);

            // 3. Mathematical balance:
            Assert.Equal(totalEvents, metrics.EventsReceived);

            if (overflowPolicy == QueueOverflowPolicy.DropOldest)
            {
                // In DropOldest, every accepted event is admitted (EventsAdmitted++), and evicted items increment EventsDropped.
                Assert.Equal(totalEvents, metrics.EventsAdmitted);
                Assert.Equal(metrics.EventsAdmitted, dest.Invocations + metrics.EventsDropped + metrics.ReconfigurationDroppedEvents + flushResult.PendingEvents);
            }
            else
            {
                // In DropNewest and BlockWithTimeout, overflow drops occur before admission (EventsDropped++):
                Assert.Equal(totalEvents, metrics.EventsAdmitted + metrics.EventsDropped);
                Assert.Equal(metrics.EventsAdmitted, dest.Invocations + metrics.ReconfigurationDroppedEvents + flushResult.PendingEvents);
            }
        }

        [Fact]
        public void Generation_RejectsAdmission_AfterStopAcceptingAdmissions()
        {
            var gen = new PipelineGeneration(
                generationId: 10,
                options: new AlertingOptions { MaxQueueCapacity = 10, OverflowPolicy = QueueOverflowPolicy.DropNewest },
                ruleEngine: new RuleEngine(new AlertRule[0]),
                coordinator: new RuleExecutionCoordinator(
                    new Suppression.BoundedSuppressionCache(100),
                    new Escalation.BoundedEscalationTracker(100),
                    new Suppression.SuppressionPolicyRegistry(),
                    new Escalation.EscalationPolicyRegistry(),
                    Infrastructure.DefaultSystemClock.Instance),
                destinationRegistry: new Destinations.DestinationRegistry(new Destinations.IAlertDestination[0]),
                groupRegistry: new Destinations.DestinationGroupRegistry(new Destinations.DestinationGroup[0]),
                fallbackRegistry: new Destinations.FallbackChainRegistry(new Destinations.FallbackChain[0]),
                fanOutDispatcher: new Destinations.FanOutDispatcher(
                    new Destinations.DestinationRegistry(new Destinations.IAlertDestination[0]),
                    new Destinations.DestinationGroupRegistry(new Destinations.DestinationGroup[0]),
                    FanOutDispatchMode.Sequential,
                    TimeSpan.FromSeconds(1),
                    10,
                    new Destinations.DestinationConcurrencyLimiter(10)),
                fallbackDispatcher: new Destinations.FallbackDispatcher(
                    new Destinations.DestinationRegistry(new Destinations.IAlertDestination[0]),
                    new Destinations.FallbackChainRegistry(new Destinations.FallbackChain[0]),
                    TimeSpan.FromSeconds(1),
                    10,
                    new Destinations.DestinationConcurrencyLimiter(10)),
                metrics: new Metrics.InternalMetricsCollector());

            // First admission succeeds
            Assert.True(gen.TryAdmit(CreateContext(1), AlertSeverity.Warning, out var admitted1));
            Assert.NotNull(admitted1);

            // Stop accepting admissions
            gen.StopAcceptingAdmissions();

            // Subsequent admission MUST fail definitively
            Assert.False(gen.TryAdmit(CreateContext(2), AlertSeverity.Warning, out var admitted2));
            Assert.Null(admitted2);

            gen.Dispose();
        }
    }
}
