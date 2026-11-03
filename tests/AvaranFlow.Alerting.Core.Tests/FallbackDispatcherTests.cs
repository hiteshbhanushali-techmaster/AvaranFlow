using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Models;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class FallbackDispatcherTests
    {
        private static AlertEnvelope CreateEnvelope()
        {
            return new AlertEnvelope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "FallbackKey",
                "ConnectionResetException",
                "Socket reset by peer",
                AlertSeverity.Critical,
                "Connectivity",
                "MarketFeed",
                "MKT-77",
                "Production",
                "FEED-01",
                "MarketFeedDaemon",
                5432,
                true,
                3,
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow);
        }

        [Fact]
        public async Task Fallback_PrimarySucceeds_SubsequentDestinationsAreNeverInvoked()
        {
            var primary = TestDestination.CreateSuccessful("SMS");
            var fallback1 = TestDestination.CreateSuccessful("Email");
            var fallback2 = TestDestination.CreateSuccessful("Webhook");

            var registry = new DestinationRegistry(new[] { primary, fallback1, fallback2 });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("PrimaryAlertChain", "Chain", new[] { "SMS", "Email", "Webhook" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain);

            // Verified: Primary succeeded -> Email and Webhook must NOT be invoked
            Assert.True(result.Succeeded);
            Assert.Equal("SMS", result.SuccessfulDestination);
            Assert.Single(result.AttemptResults);
            Assert.True(result.AttemptResults[0].Success);

            Assert.Equal(1, primary.Invocations);
            Assert.Equal(0, fallback1.Invocations);
            Assert.Equal(0, fallback2.Invocations);
        }

        [Fact]
        public async Task Fallback_PrimaryFails_SecondaryAttemptedAndSucceeds()
        {
            var primary = TestDestination.CreateFailing("SMS", "Provider quota exceeded");
            var fallback1 = TestDestination.CreateSuccessful("Email");
            var fallback2 = TestDestination.CreateSuccessful("Webhook");

            var registry = new DestinationRegistry(new[] { primary, fallback1, fallback2 });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("Chain", "Chain", new[] { "SMS", "Email", "Webhook" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain);

            // Verified: SMS failed -> Email succeeded -> Webhook never invoked
            Assert.True(result.Succeeded);
            Assert.Equal("Email", result.SuccessfulDestination);
            Assert.Equal(2, result.AttemptResults.Count);

            Assert.False(result.AttemptResults[0].Success);
            Assert.Equal("SMS", result.AttemptResults[0].DestinationName);

            Assert.True(result.AttemptResults[1].Success);
            Assert.Equal("Email", result.AttemptResults[1].DestinationName);

            Assert.Equal(1, primary.Invocations);
            Assert.Equal(1, fallback1.Invocations);
            Assert.Equal(0, fallback2.Invocations);
        }

        [Fact]
        public async Task Fallback_ThrowingDestination_TreatedAsFailure_ProceedsToNextFallback()
        {
            var primary = TestDestination.CreateThrowing("SMS", "Socket closed abruptly");
            var secondary = TestDestination.CreateSuccessful("Email");

            var registry = new DestinationRegistry(new[] { primary, secondary });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("Chain", "Chain", new[] { "SMS", "Email" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain);

            Assert.True(result.Succeeded);
            Assert.Equal("Email", result.SuccessfulDestination);
            Assert.Equal(2, result.AttemptResults.Count);

            Assert.False(result.AttemptResults[0].Success);
            Assert.True(result.AttemptResults[1].Success);

            Assert.Equal(1, primary.Invocations);
            Assert.Equal(1, secondary.Invocations);
        }

        [Fact]
        public async Task Fallback_AllDestinationsFail_ReturnsAggregatedFailure()
        {
            var d1 = TestDestination.CreateFailing("SMS", "Gateway down");
            var d2 = TestDestination.CreateThrowing("Email", "Auth failed");
            var d3 = TestDestination.CreateFailing("Webhook", "HTTP 500");

            var registry = new DestinationRegistry(new[] { d1, d2, d3 });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("FailChain", "Chain", new[] { "SMS", "Email", "Webhook" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain);

            Assert.False(result.Succeeded);
            Assert.Null(result.SuccessfulDestination);
            Assert.Equal(3, result.AttemptResults.Count);
            Assert.All(result.AttemptResults, a => Assert.False(a.Success));

            Assert.Equal(1, d1.Invocations);
            Assert.Equal(1, d2.Invocations);
            Assert.Equal(1, d3.Invocations);
        }

        [Fact]
        public async Task Fallback_Cancellation_StopsFurtherFallbackAttempts()
        {
            using var cts = new CancellationTokenSource();

            var d1 = new TestDestination("SMS", (env, ct) =>
            {
                cts.Cancel(); // Cancel mid-execution
                return Task.FromResult(AlertDeliveryResult.Failed("SMS", TimeSpan.FromMilliseconds(5), new OperationCanceledException()));
            });

            var d2 = TestDestination.CreateSuccessful("Email");

            var registry = new DestinationRegistry(new[] { d1, d2 });
            using var dispatcher = new FallbackDispatcher(registry);

            var chain = new FallbackChain("Chain", "Chain", new[] { "SMS", "Email" });
            var envelope = CreateEnvelope();

            var result = await dispatcher.DispatchAsync(envelope, chain, cts.Token);

            Assert.False(result.Succeeded);
            Assert.Equal(0, d2.Invocations); // Did not attempt d2 because token was cancelled
        }
    }
}
