using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Escalation;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Alerting.Suppression;
using ExceptionMaster.Core;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class ReconfigureConcurrencyStressTests : IDisposable
    {
        public ReconfigureConcurrencyStressTests()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        public void Dispose()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        private static ExceptionContext CreateContext(string key)
        {
            var ex = new InvalidOperationException("Reconfigure stress test");
            return new ExceptionContext(
                exception: ex,
                message: "High-concurrency reconfigure stress event",
                exceptionType: ex.GetType().FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "OrderHost.Send",
                category: "Trading",
                machineName: "STRESS-SRV-01",
                processName: "TradingProc",
                processId: 1000,
                threadId: 10,
                appVersion: "1.0",
                correlationId: Guid.NewGuid().ToString("N"),
                isFatal: false,
                exceptionKey: key,
                environmentName: "Production",
                source: "StressOrderHost",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public async Task ConcurrentIngestAndReconfigure_PreservesInvariantsAndGlobalConcurrencyBudget()
        {
            const int globalMaxConcurrency = 4;
            var dest = TestDestination.CreateSuccessful("DestShared", TimeSpan.FromMilliseconds(5));

            var suppPolicy = new SuppressionPolicy("P_Supp", TimeSpan.FromMilliseconds(50), SuppressionDimension.ExceptionKey);
            var escPolicy = new EscalationPolicy("P_Esc", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "DestShared" }),
                EscalationStep.ForCount(2, 20, targetDestinations: new[] { "DestShared" })
            });

            Alerting.Initialize(b =>
            {
                b.ConfigureOptions(opt =>
                {
                    opt.GlobalMaxDestinationConcurrency = globalMaxConcurrency;
                    opt.WorkerConcurrency = 2;
                    opt.ReconfigureDrainTimeout = TimeSpan.FromSeconds(2);
                });
                b.RegisterDestination(dest);
                b.RegisterSuppressionPolicy(suppPolicy);
                b.RegisterEscalationPolicy(escPolicy);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    targetDestinations: new[] { "DestShared" },
                    suppressionPolicyId: "P_Supp",
                    escalationPolicyId: "P_Esc"));
            });

            const int ingestWorkers = 10;
            const int eventsPerWorker = 20;
            const int totalEvents = ingestWorkers * eventsPerWorker; // 200 events

            var reconfigureTasks = new List<Task>();
            var ingestTasks = new List<Task>();
            var startBarrier = new Barrier(ingestWorkers + 2);

            // Reconfigure Task 1
            reconfigureTasks.Add(Task.Run(async () =>
            {
                startBarrier.SignalAndWait();
                for (int r = 0; r < 3; r++)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                    try
                    {
                        Alerting.Reconfigure(b =>
                        {
                            b.ConfigureOptions(opt =>
                            {
                                opt.GlobalMaxDestinationConcurrency = globalMaxConcurrency;
                                opt.WorkerConcurrency = 2;
                                opt.ReconfigureDrainTimeout = TimeSpan.FromSeconds(2);
                            });
                            b.RegisterDestination(dest);
                            b.RegisterSuppressionPolicy(suppPolicy);
                            b.RegisterEscalationPolicy(escPolicy);
                            b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                                targetDestinations: new[] { "DestShared" },
                                suppressionPolicyId: "P_Supp",
                                escalationPolicyId: "P_Esc"));
                        });
                    }
                    catch
                    {
                        // Failure containment during race
                    }
                }
            }));

            // Reconfigure Task 2
            reconfigureTasks.Add(Task.Run(async () =>
            {
                startBarrier.SignalAndWait();
                for (int r = 0; r < 2; r++)
                {
                    await Task.Delay(35).ConfigureAwait(false);
                    try
                    {
                        Alerting.Reconfigure(b =>
                        {
                            b.RegisterDestination(dest);
                            b.RegisterSuppressionPolicy(suppPolicy);
                            b.RegisterEscalationPolicy(escPolicy);
                            b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                                targetDestinations: new[] { "DestShared" },
                                suppressionPolicyId: "P_Supp",
                                escalationPolicyId: "P_Esc"));
                        });
                    }
                    catch
                    {
                        // Failure containment
                    }
                }
            }));

            // Ingestion Tasks
            for (int w = 0; w < ingestWorkers; w++)
            {
                ingestTasks.Add(Task.Run(() =>
                {
                    startBarrier.SignalAndWait();
                    for (int e = 0; e < eventsPerWorker; e++)
                    {
                        Alerting.Ingest(CreateContext("STRESS_KEY_SHARED"));
                    }
                }));
            }

            await Task.WhenAll(ingestTasks);
            await Task.WhenAll(reconfigureTasks);

            Alerting.Flush(TimeSpan.FromSeconds(5));

            var metrics = Alerting.GetMetrics();

            // Verification:
            // 1. All events accounted for
            Assert.Equal(totalEvents, metrics.EventsReceived);
            Assert.Equal(totalEvents, metrics.EventsAdmitted + metrics.EventsDropped);

            // 2. Reconfigurations occurred
            Assert.True(metrics.ReconfigurationCount >= 2, $"Expected at least 2 reconfigurations. Actual: {metrics.ReconfigurationCount}");

            // 3. Destination concurrency budget never exceeded across all concurrent generations!
            Assert.True(
                dest.MaxObservedConcurrency <= globalMaxConcurrency,
                $"Max destination concurrency {dest.MaxObservedConcurrency} exceeded global limit {globalMaxConcurrency} across concurrent generations.");
        }
    }
}
