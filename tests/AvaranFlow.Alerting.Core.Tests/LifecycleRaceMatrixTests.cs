using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Models;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class LifecycleRaceMatrixTests : IDisposable
    {
        public LifecycleRaceMatrixTests()
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
            return new ExceptionContext(
                exception: new Exception("Lifecycle test exception"),
                message: "Test message",
                exceptionType: typeof(Exception).FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Caller",
                category: "Category",
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

        // 1. Initialize vs Initialize
        [Fact]
        public async Task Initialize_Vs_Initialize_ExactlyOneSucceedsAndOneThrows()
        {
            int successes = 0;
            int exceptions = 0;

            var t1 = Task.Run(() =>
            {
                try
                {
                    Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("Dest1")));
                    Interlocked.Increment(ref successes);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref exceptions);
                }
            });

            var t2 = Task.Run(() =>
            {
                try
                {
                    Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("Dest2")));
                    Interlocked.Increment(ref successes);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref exceptions);
                }
            });

            await Task.WhenAll(t1, t2);

            Assert.Equal(1, successes);
            Assert.Equal(1, exceptions);
            Assert.True(Alerting.IsInitialized);
        }

        // 2. Initialize vs Reconfigure
        [Fact]
        public async Task Initialize_Vs_Reconfigure_NoDeadlockAndDeterministic()
        {
            var t1 = Task.Run(() =>
            {
                try
                {
                    Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("DestInit")));
                }
                catch { }
            });

            var t2 = Task.Run(async () =>
            {
                await Task.Delay(10);
                try
                {
                    Alerting.Reconfigure(b => b.RegisterDestination(TestDestination.CreateSuccessful("DestReconf")));
                }
                catch (InvalidOperationException) { } // if reconfigure happened before initialize
            });

            await Task.WhenAll(t1, t2);
            Assert.True(Alerting.IsInitialized);
        }

        // 3. Reconfigure vs Reconfigure
        [Fact]
        public async Task Reconfigure_Vs_Reconfigure_SequentiallyAdvancesGenerationsWithoutDeadlock()
        {
            Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("DestInit")));

            var t1 = Task.Run(() =>
            {
                Alerting.Reconfigure(b => b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Cat"), targetDestinations: new[] { "DestInit" })));
            });

            var t2 = Task.Run(() =>
            {
                Alerting.Reconfigure(b => b.AddRule(new AlertRule("Rule2", "Rule2", RuleCondition.Category(PredicateOperator.Equals, "Cat"), targetDestinations: new[] { "DestInit" })));
            });

            await Task.WhenAll(t1, t2);

            var metrics = Alerting.GetMetrics();
            Assert.Equal(2, metrics.ReconfigurationCount);
        }

        // 4. Reconfigure vs Shutdown
        [Fact]
        public async Task Reconfigure_Vs_Shutdown_NoDeadlockNoObjectDisposedException()
        {
            Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("DestInit")));

            var t1 = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Alerting.Reconfigure(b => b.AddRule(new AlertRule($"R_{i}", $"R_{i}", RuleCondition.Category(PredicateOperator.Equals, "Cat"))));
                    }
                    catch (InvalidOperationException) { break; }
                    await Task.Delay(5);
                }
            });

            var t2 = Task.Run(() =>
            {
                Alerting.Shutdown(TimeSpan.FromSeconds(2));
            });

            await Task.WhenAll(t1, t2);
            Assert.False(Alerting.IsInitialized);
        }

        // 5. Flush vs Shutdown
        [Fact]
        public async Task Flush_Vs_Shutdown_NoDeadlockAndDeterministic()
        {
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(TestDestination.CreateSuccessful("Dest"));
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Cat"), targetDestinations: new[] { "Dest" }));
            });

            for (int i = 0; i < 10; i++)
            {
                Alerting.Ingest(CreateContext($"KEY_{i}"));
            }

            var tFlush = Task.Run(() => Alerting.Flush(TimeSpan.FromSeconds(2)));
            var tShutdown = Task.Run(() => Alerting.Shutdown(TimeSpan.FromSeconds(2)));

            await Task.WhenAll(tFlush, tShutdown);
            Assert.False(Alerting.IsInitialized);
        }

        // 6. Ingest vs Shutdown
        [Fact]
        public async Task Ingest_Vs_Shutdown_NoAdmissionsAfterShutdownEstablished()
        {
            var dest = TestDestination.CreateSuccessful("DestIngestShutdown");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestIngestShutdown" }));
            });

            var cts = new CancellationTokenSource();
            var ingestTask = Task.Run(() =>
            {
                int count = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    Alerting.Ingest(CreateContext($"KEY_{count++}"));
                }
            });

            await Task.Delay(20);
            await Alerting.ShutdownAsync(TimeSpan.FromSeconds(2));
            cts.Cancel();
            await ingestTask;

            // Ingest after shutdown must drop
            int droppedBefore = (int)Alerting.GetMetrics().EventsDropped;
            Alerting.Ingest(CreateContext("AFTER_SHUTDOWN"));
            int droppedAfter = (int)Alerting.GetMetrics().EventsDropped;

            Assert.Equal(droppedBefore + 1, droppedAfter);
        }

        // 7. Ingest vs Reconfigure
        [Fact]
        public async Task Ingest_Vs_Reconfigure_ContinuesAdmittingWithoutLoss()
        {
            var dest = TestDestination.CreateSuccessful("DestIngestReconf");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestIngestReconf" }));
            });

            var tIngest = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    Alerting.Ingest(CreateContext($"INGEST_{i}"));
                }
            });

            var tReconf = Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Alerting.Reconfigure(b => b.AddRule(new AlertRule($"R_{i}", $"R_{i}", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestIngestReconf" })));
                }
            });

            await Task.WhenAll(tIngest, tReconf);
            var flush = await Alerting.FlushAsync(TimeSpan.FromSeconds(5));
            Assert.True(flush.Success);
            Assert.True(dest.Invocations >= 50);
        }

        // 8. Flush vs Reconfigure
        [Fact]
        public async Task Flush_Vs_Reconfigure_CorrectlyFlushesWithoutDeadlock()
        {
            var dest = TestDestination.CreateSuccessful("DestFlushReconf");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("R", "R", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestFlushReconf" }));
            });

            for (int i = 0; i < 20; i++)
            {
                Alerting.Ingest(CreateContext($"FLUSH_RECONF_{i}"));
            }

            var tFlush = Task.Run(() => Alerting.FlushAsync(TimeSpan.FromSeconds(3)));
            var tReconf = Task.Run(() => Alerting.Reconfigure(b => b.AddRule(new AlertRule("R2", "R2", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestFlushReconf" }))));

            await Task.WhenAll(tFlush, tReconf);
            var res = await tFlush;
            Assert.True(res.Success);
        }

        // 9. Shutdown vs Shutdown
        [Fact]
        public async Task Shutdown_Vs_Shutdown_IsIdempotentAndSafe()
        {
            Alerting.Initialize(b => b.RegisterDestination(TestDestination.CreateSuccessful("DestShutdown")));

            var t1 = Task.Run(() => Alerting.Shutdown());
            var t2 = Task.Run(() => Alerting.Shutdown());

            await Task.WhenAll(t1, t2);

            Assert.False(Alerting.IsInitialized);

            // Repeated calls after completion are completely safe
            Alerting.Shutdown();
            await Alerting.ShutdownAsync();
            Assert.False(Alerting.IsInitialized);
        }
    }
}
