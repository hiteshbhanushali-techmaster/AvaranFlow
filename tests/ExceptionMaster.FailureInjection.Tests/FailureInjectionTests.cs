using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Core;
using Xunit;
using EM = ExceptionMaster.Core.ExceptionMaster;

namespace ExceptionMaster.FailureInjection.Tests
{
    public class FailureInjectionTests
    {
        private class ThrowingSink : IExceptionSink
        {
            public string Name => "ThrowingSink";
            public void Dispose() { }
            public void Flush() { }

            public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Simulated sink hard crash");
            }

            public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Simulated sink batch hard crash");
            }
        }

        private class SpySink : IExceptionSink
        {
            public List<ExceptionContext> Received = new List<ExceptionContext>();
            public string Name => "SpySink";
            public void Dispose() { }
            public void Flush() { }

            public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                lock (Received) { Received.Add(context); }
                return Task.CompletedTask;
            }

            public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                lock (Received) { Received.AddRange(contexts); }
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void ThrowingSink_DoesNotCrashCaller()
        {
            var throwingSink = new ThrowingSink();
            EM.Configure(opts =>
            {
                opts.EnableAsyncDispatch = false; // Synchronous dispatch to test immediate boundary
                opts.Sinks.Add(throwingSink);
            });

            // Must not throw!
            EM.Log(new InvalidOperationException("Test caller safety"));
            var health = EM.GetHealth();
            Assert.True(health.TotalSinkFailures >= 1);
        }

        [Fact]
        public void FallbackChain_PrimaryFails_EngagesFallbackSink()
        {
            var throwingPrimary = new ThrowingSink();
            var fallbackSpy = new SpySink();

            EM.Configure(opts =>
            {
                opts.EnableAsyncDispatch = false;
                var chain = new FallbackChain(throwingPrimary, fallbackSpy);
                opts.FallbackChains.Add(chain);
            });

            EM.Log(new InvalidOperationException("Test fallback cascade"));

            lock (fallbackSpy.Received)
            {
                Assert.Single(fallbackSpy.Received);
                Assert.Equal("Test fallback cascade", fallbackSpy.Received[0].Message);
            }

            var health = EM.GetHealth();
            Assert.True(health.TotalFallbackInvocations >= 1);
        }

        [Fact]
        public void FallbackChain_PrimaryAndFirstFallbackFail_EngagesSecondFallback()
        {
            var throwingPrimary = new ThrowingSink();
            var throwingFallback1 = new ThrowingSink();
            var successfulFallback2 = new SpySink();

            EM.Configure(opts =>
            {
                opts.EnableAsyncDispatch = false;
                var chain = new FallbackChain(throwingPrimary, throwingFallback1, successfulFallback2);
                opts.FallbackChains.Add(chain);
            });

            EM.Log(new InvalidOperationException("Test multi-stage fallback"));

            lock (successfulFallback2.Received)
            {
                Assert.Single(successfulFallback2.Received);
                Assert.Equal("Test multi-stage fallback", successfulFallback2.Received[0].Message);
            }
        }

        [Fact]
        public async Task FireAndForget_ThrowingSubscriber_IsolatedFromOtherSubscribers()
        {
            EM.ConfigureDefaults();

            var sub2Received = new ManualResetEventSlim(false);

            Action<ExceptionContext> badSubscriber = ctx =>
            {
                throw new InvalidOperationException("Exploding subscriber");
            };

            Action<ExceptionContext> goodSubscriber = ctx =>
            {
                sub2Received.Set();
            };

            EM.ExceptionRaised += badSubscriber;
            EM.ExceptionRaised += goodSubscriber;

            try
            {
                EM.Log(new InvalidOperationException("Notification isolation test"));

                bool arrived = sub2Received.Wait(TimeSpan.FromSeconds(3));
                Assert.True(arrived);
            }
            finally
            {
                EM.ExceptionRaised -= badSubscriber;
                EM.ExceptionRaised -= goodSubscriber;
            }
        }

        [Fact]
        public async Task FireAndForget_SubscriberLoggingNewException_DoesNotRecurse()
        {
            EM.ConfigureDefaults();

            int eventInvocationCount = 0;
            var completed = new ManualResetEventSlim(false);

            Action<ExceptionContext> reentrantSubscriber = null;
            reentrantSubscriber = ctx =>
            {
                int count = Interlocked.Increment(ref eventInvocationCount);
                if (count == 1)
                {
                    // Inside subscriber: log another exception!
                    EM.Log(new InvalidOperationException("Secondary exception logged from subscriber"));
                    completed.Set();
                }
            };

            EM.ExceptionRaised += reentrantSubscriber;

            try
            {
                EM.Log(new InvalidOperationException("Initial exception"));
                bool triggered = completed.Wait(TimeSpan.FromSeconds(2));
                Assert.True(triggered);

                // Wait a bit to ensure no secondary recursion happened
                await Task.Delay(200);

                // Should be invoked exactly once because secondary exception from subscriber was suppressed from re-raising event
                Assert.Equal(1, Volatile.Read(ref eventInvocationCount));
            }
            finally
            {
                EM.ExceptionRaised -= reentrantSubscriber;
            }
        }
    }
}
