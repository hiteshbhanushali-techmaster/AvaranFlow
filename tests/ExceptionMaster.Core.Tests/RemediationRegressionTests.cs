using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Core;
using Xunit;
using EM = ExceptionMaster.Core.ExceptionMaster;

namespace ExceptionMaster.Core.Tests
{
    public class RemediationRegressionTests
    {
        private class TrackingSink : IExceptionSink
        {
            public List<ExceptionContext> Received = new List<ExceptionContext>();
            public int WriteBatchCallCount;
            public bool ShouldThrow;
            public ManualResetEventSlim Gate = new ManualResetEventSlim(true);

            public string Name { get; }

            public TrackingSink(string name = "TrackingSink", bool shouldThrow = false)
            {
                Name = name;
                ShouldThrow = shouldThrow;
            }

            public void Dispose() { }
            public void Flush() { }

            public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                Gate.Wait(cancellationToken);
                if (ShouldThrow) throw new InvalidOperationException("Sink write failed intentionally");
                lock (Received) { Received.Add(context); }
                return Task.CompletedTask;
            }

            public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                Gate.Wait(cancellationToken);
                Interlocked.Increment(ref WriteBatchCallCount);
                if (ShouldThrow) throw new InvalidOperationException("Sink batch write failed intentionally");
                lock (Received) { Received.AddRange(contexts); }
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void Preserve_ExceptionRaised_Subscriptions_Across_Configure()
        {
            var receivedBefore = new List<string>();
            var receivedAfter = new List<string>();

            Action<ExceptionContext> sub1 = ctx => { lock (receivedBefore) { receivedBefore.Add(ctx.Message); } };
            Action<ExceptionContext> sub2 = ctx => { lock (receivedAfter) { receivedAfter.Add(ctx.Message); } };

            try
            {
                // 1. Subscribe before Configure()
                EM.ExceptionRaised += sub1;

                // 2. Log first event
                EM.Log(new InvalidOperationException("Event Before Configure"));
                EM.Flush(TimeSpan.FromSeconds(1));

                // 3. Reconfigure ExceptionMaster
                EM.Configure(opts =>
                {
                    opts.EnableAsyncDispatch = true;
                    opts.BufferCapacity = 500;
                });

                // 4. Register a second subscriber after Configure()
                EM.ExceptionRaised += sub2;

                // 5. Log second event
                EM.Log(new InvalidOperationException("Event After Configure"));
                EM.Flush(TimeSpan.FromSeconds(1));

                // Verify sub1 received BOTH events (preserved across Configure!)
                lock (receivedBefore)
                {
                    Assert.Contains("Event Before Configure", receivedBefore);
                    Assert.Contains("Event After Configure", receivedBefore);
                }

                // Verify sub2 received the second event
                lock (receivedAfter)
                {
                    Assert.Contains("Event After Configure", receivedAfter);
                }
            }
            finally
            {
                EM.ExceptionRaised -= sub1;
                EM.ExceptionRaised -= sub2;
            }
        }

        [Fact]
        public void EventBroadcaster_SubscriberIsolation_And_ReentrancyProtection()
        {
            int healthySubscriberCalls = 0;
            var subscriberCalled = new ManualResetEventSlim(false);

            Action<ExceptionContext> throwingSubscriber = ctx =>
            {
                throw new ApplicationException("Exploding subscriber");
            };

            Action<ExceptionContext> reentrantSubscriber = ctx =>
            {
                Interlocked.Increment(ref healthySubscriberCalls);
                subscriberCalled.Set();
                // Call ExceptionMaster.Log recursively from within event handler
                EM.Log(new InvalidOperationException("Recursive inner error"));
            };

            try
            {
                EM.ExceptionRaised += throwingSubscriber;
                EM.ExceptionRaised += reentrantSubscriber;

                EM.Log(new InvalidOperationException("Root outer error"));
                subscriberCalled.Wait(TimeSpan.FromSeconds(2));
                EM.Flush(TimeSpan.FromSeconds(1));

                // Healthy subscriber was called despite throwing subscriber
                Assert.True(healthySubscriberCalls >= 1);
            }
            finally
            {
                EM.ExceptionRaised -= throwingSubscriber;
                EM.ExceptionRaised -= reentrantSubscriber;
            }
        }

        private class DelaySink : IExceptionSink
        {
            public int DelayMs;
            public string Name => "DelaySink";
            public DelaySink(int delayMs) { DelayMs = delayMs; }
            public void Dispose() { }
            public void Flush() { }

            public async Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                if (DelayMs > 0) await Task.Delay(DelayMs, cancellationToken).ConfigureAwait(false);
            }

            public async Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                if (DelayMs > 0) await Task.Delay(DelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private class ControlledGateSink : IExceptionSink
        {
            public ManualResetEventSlim Gate = new ManualResetEventSlim(true);
            public ManualResetEventSlim Entered = new ManualResetEventSlim(false);
            public string Name => "ControlledGateSink";

            public void Dispose() { }
            public void Flush() { }

            public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                Entered.Set();
                Gate.Wait(cancellationToken);
                return Task.CompletedTask;
            }

            public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                Entered.Set();
                Gate.Wait(cancellationToken);
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void DispatchPipeline_DropOldest_Accounting_NoGhostSignals_NoPermitDrift()
        {
            var testSink = new DelaySink(50);
            var options = new ExceptionMasterOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 5,
                BatchSize = 10,
                OverflowPolicy = QueueOverflowPolicy.DropOldest,
                FlushInterval = TimeSpan.FromHours(1)
            };
            options.Sinks.Add(testSink);

            using (var pipeline = new DispatchPipeline(options))
            {
                for (int i = 0; i < 25; i++)
                {
                    var ctx = new ExceptionContext(
                        new Exception($"msg_{i}"), $"msg_{i}", "Ex", "",
                        DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, $"k_{i}", "", "", "", Guid.NewGuid());
                    pipeline.Dispatch(ctx);
                }

                var health = pipeline.GetHealth();
                Assert.True(health.TotalDropped >= 10);

                pipeline.Flush(TimeSpan.FromSeconds(3));

                var healthAfterFlush = pipeline.GetHealth();
                Assert.Equal(0, healthAfterFlush.PendingQueueDepth);
                Assert.True(healthAfterFlush.TotalDispatched >= 5);
            }
        }

        [Fact]
        public void DispatchPipeline_BlockWithTimeout_FatalBypass_ConservesCapacityPermits()
        {
            var testSink = new ControlledGateSink();
            testSink.Gate.Reset(); // Block sink initially

            var options = new ExceptionMasterOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 2,
                BatchSize = 1, // Worker only takes 1 item
                OverflowPolicy = QueueOverflowPolicy.BlockWithTimeout,
                EnqueueBlockTimeout = TimeSpan.FromMilliseconds(50),
                FlushInterval = TimeSpan.FromHours(1)
            };
            options.Sinks.Add(testSink);

            using (var pipeline = new DispatchPipeline(options))
            {
                var regularCtx = new ExceptionContext(
                    new Exception("regular"), "regular", "Ex", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "regKey", "", "", "", Guid.NewGuid());

                var fatalCtx = new ExceptionContext(
                    new Exception("fatal"), "fatal", "Ex", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", true, "fatalKey", "", "", "", Guid.NewGuid());

                // 1st item picked up by worker and held in ControlledGateSink
                Assert.True(pipeline.Dispatch(regularCtx));
                Assert.True(testSink.Entered.Wait(TimeSpan.FromSeconds(2)));

                // Queue buffer capacity is 2: next 2 items fill the queue
                Assert.True(pipeline.Dispatch(regularCtx));
                Assert.True(pipeline.Dispatch(regularCtx));

                // 4th regular item times out because queue buffer (2) is full
                Assert.False(pipeline.Dispatch(regularCtx));

                // Fatal item bypasses capacity check and succeeds
                Assert.True(pipeline.Dispatch(fatalCtx));

                // Unblock sink and flush
                testSink.Gate.Set();
                pipeline.Flush(TimeSpan.FromSeconds(3));

                Assert.Equal(0, pipeline.GetHealth().PendingQueueDepth);

                // Capacity permits are conserved and replenished: can accept items again
                testSink.Entered.Reset();
                testSink.Gate.Reset();

                Assert.True(pipeline.Dispatch(regularCtx));
                Assert.True(testSink.Entered.Wait(TimeSpan.FromSeconds(2)));

                Assert.True(pipeline.Dispatch(regularCtx));
                Assert.True(pipeline.Dispatch(regularCtx));
                Assert.False(pipeline.Dispatch(regularCtx)); // times out again!

                testSink.Gate.Set();
                pipeline.Flush(TimeSpan.FromSeconds(3));
            }
        }

        [Fact]
        public void AggregationEngine_PeriodicSummary_EmitsUpdatedCountsToSinks()
        {
            var engine = new AggregationEngine(TimeSpan.FromMinutes(5), 1000);
            var now = DateTime.UtcNow;
            string key = "TestKey";

            // 1st occurrence
            bool first = engine.RecordOccurrence(
                key, now, false,
                () => new ExceptionContext(
                    new Exception("agg"), "agg", "Ex", "",
                    now, now, 1, "", "", "", "", 1, 1, "", "", false, key, "", "", "", Guid.NewGuid()),
                out var initialContext);

            Assert.True(first);
            Assert.NotNull(initialContext);
            Assert.Equal(1, initialContext.Count);

            // 9 repeated occurrences
            for (int i = 0; i < 9; i++)
            {
                bool repeated = engine.RecordOccurrence(key, now.AddSeconds(i + 1), false, null, out _);
                Assert.False(repeated);
            }

            // Summary query with count threshold 5
            var summaries = engine.GetPeriodicSummaryContexts(now.AddSeconds(10), TimeSpan.FromSeconds(30), 5);
            Assert.Single(summaries);
            Assert.Equal(10, summaries[0].Count);
            Assert.Equal(9, summaries[0].DeltaCount);

            // Shutdown/Flush final drain
            var finalSummaries = engine.DrainAllPendingSummaries();
            Assert.Empty(finalSummaries); // already reported up to 10
        }

        [Fact]
        public void DuplicateDelivery_Prevented_WhenSinkInFanOutAndFallback()
        {
            var failingPrimary = new TrackingSink("PrimaryFailing", shouldThrow: true);
            var secondaryFallback = new TrackingSink("SecondaryFallback", shouldThrow: false);

            var options = new ExceptionMasterOptions
            {
                EnableAsyncDispatch = false
            };

            // Secondary is configured in Fan-out AND in Fallback chain
            options.Sinks.Add(secondaryFallback);
            options.FallbackChains.Add(new FallbackChain(failingPrimary, secondaryFallback));

            using (var pipeline = new DispatchPipeline(options))
            {
                var ctx = new ExceptionContext(
                    new Exception("dup"), "dup", "Ex", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "dupKey", "", "", "", Guid.NewGuid());

                pipeline.Dispatch(ctx);

                // secondaryFallback should be called EXACTLY ONCE (during fan-out), not twice!
                Assert.Equal(1, secondaryFallback.WriteBatchCallCount);
            }
        }
    }
}
