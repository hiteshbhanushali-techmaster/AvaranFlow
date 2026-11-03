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
    public class FlushSemanticsVerificationTests : IDisposable
    {
        public FlushSemanticsVerificationTests()
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
                exception: new Exception("Flush test exception"),
                message: "Flush test msg",
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

        // 1. Flush waits for currently admitted events to complete the entire pipeline, including destination delivery, not merely queue dequeue.
        [Fact]
        public async Task Flush_WaitsForDestinationDeliveryToComplete_NotJustQueueDequeue()
        {
            var destinationEntered = new ManualResetEventSlim(false);
            var allowDestinationToFinish = new ManualResetEventSlim(false);
            bool destinationFinished = false;

            var slowDest = new TestDestination("SlowDest", async (envelope, token) =>
            {
                destinationEntered.Set();
                allowDestinationToFinish.Wait(token);
                await Task.Delay(25, token).ConfigureAwait(false);
                destinationFinished = true;
                return AlertDeliveryResult.Succeeded("SlowDest", TimeSpan.FromMilliseconds(25));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(slowDest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "SlowDest" }));
            });

            Alerting.Ingest(CreateContext("FLUSH_1"));

            // Wait until worker dequeued and entered destination
            Assert.True(destinationEntered.Wait(TimeSpan.FromSeconds(3)), "Worker did not enter destination within timeout");

            // Flush should be waiting for destination delivery
            var flushTask = Alerting.FlushAsync(TimeSpan.FromSeconds(5));
            Assert.False(flushTask.IsCompleted, "Flush returned before destination finished!");

            // Allow destination to complete
            allowDestinationToFinish.Set();

            var result = await flushTask;
            Assert.True(result.Success);
            Assert.Equal(1, result.CompletedEvents);
            Assert.Equal(0, result.PendingEvents);
            Assert.True(destinationFinished, "Destination had not finished when flush completed");
        }

        // 2. Flush correctly accounts for events in: active generation AND draining/reconfiguring generation
        [Fact]
        public async Task Flush_CorrectlyAccountsForEventsInBothActiveAndDrainingGenerations()
        {
            var holdSignal = new ManualResetEventSlim(false);
            var dest = new TestDestination("DestMultiGen", async (env, token) =>
            {
                await Task.Yield();
                holdSignal.Wait(token);
                return AlertDeliveryResult.Succeeded("DestMultiGen", TimeSpan.FromMilliseconds(5));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestMultiGen" }));
            });

            // Ingest into Generation 1
            for (int i = 0; i < 5; i++)
            {
                Alerting.Ingest(CreateContext($"GEN1_{i}"));
            }

            // Reconfigure to trigger Generation 2 (Generation 1 is now draining)
            Alerting.Reconfigure(b =>
            {
                b.AddRule(new AlertRule("Rule2", "Rule2", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestMultiGen" }));
            });

            // Ingest into Generation 2
            for (int i = 0; i < 5; i++)
            {
                Alerting.Ingest(CreateContext($"GEN2_{i}"));
            }

            // Start flush while both Gen 1 and Gen 2 have pending events
            var flushTask = Alerting.FlushAsync(TimeSpan.FromSeconds(5));

            // Release hold so all 10 events can complete
            holdSignal.Set();

            var result = await flushTask;
            Assert.True(result.Success);
            Assert.Equal(10, result.CompletedEvents);
            Assert.Equal(0, result.PendingEvents);
        }

        // 3. Flush cannot race with generation disposal
        [Fact]
        public async Task Flush_CannotRaceWithGenerationDisposal()
        {
            var dest = TestDestination.CreateSuccessful("DestDisposalRace");

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestDisposalRace" }));
            });

            for (int i = 0; i < 10; i++)
            {
                Alerting.Ingest(CreateContext($"DISP_RACE_{i}"));
            }

            // Concurrently flush and shutdown/dispose
            var flushTask = Task.Run(() => Alerting.FlushAsync(TimeSpan.FromSeconds(2)));
            var shutdownTask = Task.Run(() => Alerting.ShutdownAsync(TimeSpan.FromSeconds(2)));

            await Task.WhenAll(flushTask, shutdownTask);

            var flushResult = await flushTask;
            Assert.NotNull(flushResult);
            // Neither throws ObjectDisposedException or deadlocks
        }

        // 4. Events admitted AFTER the flush boundary are not incorrectly counted as part of that flush operation
        [Fact]
        public async Task Flush_EventsAdmittedAfterFlushBoundary_AreNotCountedInFlush()
        {
            var inDestSignal = new ManualResetEventSlim(false);
            var releaseDestSignal = new ManualResetEventSlim(false);

            var slowDest = new TestDestination("SlowBoundaryDest", async (envelope, token) =>
            {
                inDestSignal.Set();
                releaseDestSignal.Wait(token);
                await Task.Delay(10, token).ConfigureAwait(false);
                return AlertDeliveryResult.Succeeded("SlowBoundaryDest", TimeSpan.FromMilliseconds(10));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(slowDest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "SlowBoundaryDest" }));
            });

            // Ingest 2 events before flush
            Alerting.Ingest(CreateContext("PRE_FLUSH_1"));
            Alerting.Ingest(CreateContext("PRE_FLUSH_2"));

            Assert.True(inDestSignal.Wait(TimeSpan.FromSeconds(3)));

            // Initiate flush snapshotting currently admitted items (exactly 2)
            var flushTask = Alerting.FlushAsync(TimeSpan.FromSeconds(5));

            // While flush is waiting, admit 5 MORE events
            for (int i = 0; i < 5; i++)
            {
                Alerting.Ingest(CreateContext($"POST_FLUSH_{i}"));
            }

            // Release destination
            releaseDestSignal.Set();

            var flushResult = await flushTask;
            Assert.True(flushResult.Success);
            Assert.Equal(2, flushResult.CompletedEvents); // Flush boundary accounted for only the 2 events admitted at boundary!
            Assert.Equal(0, flushResult.PendingEvents);
        }

        // 5. Flush timeout returns deterministic completed/pending counts
        [Fact]
        public async Task Flush_Timeout_ReturnsDeterministicCompletedAndPendingCounts()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("BlockedDest", async (envelope, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("BlockedDest", TimeSpan.FromMilliseconds(5));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "BlockedDest" }));
            });

            Alerting.Ingest(CreateContext("BLOCKED_1"));
            Alerting.Ingest(CreateContext("BLOCKED_2"));

            // Flush with tiny timeout of 80ms while destination is blocked
            var flushResult = await Alerting.FlushAsync(TimeSpan.FromMilliseconds(80));

            Assert.False(flushResult.Success);
            Assert.Equal(0, flushResult.CompletedEvents);
            Assert.Equal(2, flushResult.PendingEvents);

            // Verify flush timeout metric incremented
            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.FlushTimeouts);

            // Clean up: unblock destination so test terminates cleanly
            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
        }

        // 6. Flush cancellation behaves deterministically
        [Fact]
        public async Task Flush_Cancellation_BehavesDeterministically()
        {
            var blockDest = new ManualResetEventSlim(false);
            var dest = new TestDestination("CancelDest", async (envelope, token) =>
            {
                await Task.Yield();
                blockDest.Wait(token);
                return AlertDeliveryResult.Succeeded("CancelDest", TimeSpan.FromMilliseconds(5));
            });

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "CancelDest" }));
            });

            Alerting.Ingest(CreateContext("CANCEL_1"));

            using var cts = new CancellationTokenSource(100);

            var flushResult = await Alerting.FlushAsync(TimeSpan.FromSeconds(5), cts.Token);
            Assert.False(flushResult.Success);
            Assert.Equal(1, flushResult.PendingEvents);

            blockDest.Set();
            await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
        }

        // 7. Flush vs Shutdown is race-safe
        [Fact]
        public async Task Flush_Vs_Shutdown_IsRaceSafe()
        {
            var dest = TestDestination.CreateSuccessful("DestRace");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestRace" }));
            });

            for (int i = 0; i < 20; i++)
            {
                Alerting.Ingest(CreateContext($"RACE_{i}"));
            }

            var t1 = Task.Run(() => Alerting.Flush(TimeSpan.FromSeconds(2)));
            var t2 = Task.Run(() => Alerting.Shutdown(TimeSpan.FromSeconds(2)));

            await Task.WhenAll(t1, t2);

            Assert.False(Alerting.IsInitialized);
        }

        // 8. Flush called repeatedly is safe
        [Fact]
        public async Task Flush_CalledRepeatedly_IsSafeAndDeterministic()
        {
            var dest = TestDestination.CreateSuccessful("DestRepeat");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Category"), targetDestinations: new[] { "DestRepeat" }));
            });

            for (int r = 0; r < 5; r++)
            {
                Alerting.Ingest(CreateContext($"REPEAT_{r}"));
                var res = await Alerting.FlushAsync(TimeSpan.FromSeconds(2));
                Assert.True(res.Success);
                Assert.Equal(1, res.CompletedEvents);
                Assert.Equal(0, res.PendingEvents);
            }

            // Extra flush with no pending events returns success with 0 completed/pending
            var emptyRes = await Alerting.FlushAsync(TimeSpan.FromSeconds(1));
            Assert.True(emptyRes.Success);
            Assert.Equal(0, emptyRes.CompletedEvents);
            Assert.Equal(0, emptyRes.PendingEvents);
        }
    }
}
