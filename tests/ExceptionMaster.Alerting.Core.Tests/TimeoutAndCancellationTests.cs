using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Destinations;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Models;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class TimeoutAndCancellationTests
    {
        private static AlertEnvelope CreateEnvelope()
        {
            return new AlertEnvelope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "TimeoutKey",
                "TimeoutException",
                "Request timed out",
                AlertSeverity.Error,
                "Network",
                "HttpClient",
                "REQ-1",
                "Production",
                "APP-01",
                "WebDaemon",
                100,
                false,
                1,
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow);
        }

        [Fact]
        public async Task DestinationTimeout_InFanOut_IsClassifiedAsTimeoutFailure_AndIndependentDestinationsContinue()
        {
            // Destination 1 times out (delays 5s, timeout 50ms)
            var slowDest = TestDestination.CreateTimingOut("SlowDest", TimeSpan.FromSeconds(5));
            // Destination 2 succeeds quickly
            var fastDest = TestDestination.CreateSuccessful("FastDest", TimeSpan.FromMilliseconds(5));

            var registry = new DestinationRegistry(new[] { slowDest, fastDest });
            using var dispatcher = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                destinationTimeout: TimeSpan.FromMilliseconds(50));

            var envelope = CreateEnvelope();
            var result = await dispatcher.DispatchAsync(envelope, new[] { "SlowDest", "FastDest" }, CancellationToken.None);

            Assert.Equal(2, result.Results.Count);

            var slowResult = result.Results.First(r => r.DestinationName == "SlowDest");
            var fastResult = result.Results.First(r => r.DestinationName == "FastDest");

            // Destination timeout must NOT be flagged as caller cancellation
            Assert.False(slowResult.Success);
            Assert.False(slowResult.IsCancelled);
            Assert.Contains("timed out", slowResult.DiagnosticMessage);

            // Fast destination succeeded independently
            Assert.True(fastResult.Success);
            Assert.False(fastResult.IsCancelled);
        }

        [Fact]
        public async Task CallerCancellation_InFanOut_IsClassifiedAsCancelled_NotOrdinaryTimeout()
        {
            using var callerCts = new CancellationTokenSource();
            callerCts.Cancel(); // Pre-cancelled by caller

            var dest = TestDestination.CreateSuccessful("Dest");
            var registry = new DestinationRegistry(new[] { dest });
            using var dispatcher = new FanOutDispatcher(registry);

            var envelope = CreateEnvelope();
            var result = await dispatcher.DispatchAsync(envelope, new[] { "Dest" }, callerCts.Token);

            Assert.Single(result.Results);
            var deliveryResult = result.Results[0];

            Assert.False(deliveryResult.Success);
            Assert.True(deliveryResult.IsCancelled);
            Assert.Contains("cancelled by caller", deliveryResult.DiagnosticMessage);
            Assert.DoesNotContain("timed out", deliveryResult.DiagnosticMessage);
        }

        [Fact]
        public async Task DestinationTimeout_InFallback_ProceedsToNextConfiguredDestination()
        {
            // Primary destination times out (delays 500ms, timeout 50ms)
            var slowPrimary = TestDestination.CreateTimingOut("SlowPrimary", TimeSpan.FromMilliseconds(500));
            // Fallback destination succeeds quickly
            var fastFallback = TestDestination.CreateSuccessful("FastFallback", TimeSpan.FromMilliseconds(5));

            var registry = new DestinationRegistry(new[] { slowPrimary, fastFallback });
            using var dispatcher = new FallbackDispatcher(
                registry,
                destinationTimeout: TimeSpan.FromMilliseconds(50));

            var chain = new FallbackChain("TimeoutChain", "Chain", new[] { "SlowPrimary", "FastFallback" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain, CancellationToken.None);

            // Fallback proceeded past timeout and succeeded on fastFallback!
            Assert.True(result.Succeeded);
            Assert.Equal("FastFallback", result.SuccessfulDestination);
            Assert.Equal(2, result.AttemptResults.Count);

            var firstAttempt = result.AttemptResults[0];
            Assert.False(firstAttempt.Success);
            Assert.False(firstAttempt.IsCancelled);
            Assert.Contains("timed out", firstAttempt.DiagnosticMessage);

            var secondAttempt = result.AttemptResults[1];
            Assert.True(secondAttempt.Success);
            Assert.False(secondAttempt.IsCancelled);
        }

        [Fact]
        public async Task CallerCancellation_InFallback_HaltsImmediately_WithoutInvokingNextDestination()
        {
            using var callerCts = new CancellationTokenSource();

            var cancellerDest = new TestDestination("CancellerPrimary", async (env, ct) =>
            {
                callerCts.Cancel(); // Host requests cancellation mid-delivery
                await Task.Delay(10, ct).ConfigureAwait(false);
                return AlertDeliveryResult.Cancelled("CancellerPrimary", TimeSpan.FromMilliseconds(10));
            });

            var uninvokedFallback = TestDestination.CreateSuccessful("UninvokedFallback");

            var registry = new DestinationRegistry(new[] { cancellerDest, uninvokedFallback });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("Chain", "Chain", new[] { "CancellerPrimary", "UninvokedFallback" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain, callerCts.Token);

            // Verified: When caller cancellation is requested, execution halts immediately
            Assert.False(result.Succeeded);
            Assert.Null(result.SuccessfulDestination);
            Assert.Single(result.AttemptResults);

            var attempt = result.AttemptResults[0];
            Assert.True(attempt.IsCancelled);
            Assert.Contains("cancelled by caller", attempt.DiagnosticMessage);

            // Subsequent fallback destination was NEVER invoked
            Assert.Equal(0, uninvokedFallback.Invocations);
        }
    }
}
