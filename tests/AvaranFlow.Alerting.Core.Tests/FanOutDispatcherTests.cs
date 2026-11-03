using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Models;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class FanOutDispatcherTests
    {
        private static AlertEnvelope CreateEnvelope()
        {
            return new AlertEnvelope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "OrderKey",
                "BrokerException",
                "Order processing failed",
                AlertSeverity.Error,
                "Trading",
                "FixGateway",
                "ORD-1",
                "Production",
                "TRD-SRV-1",
                "TradingApp",
                1234,
                false,
                1,
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow);
        }

        [Fact]
        public async Task SequentialFanOut_PreservesConfiguredOrder()
        {
            var executionOrder = new List<string>();

            var d1 = new TestDestination("Dest1", (env, ct) =>
            {
                lock (executionOrder) executionOrder.Add("Dest1");
                return Task.FromResult(AlertDeliveryResult.Succeeded("Dest1", TimeSpan.FromMilliseconds(5)));
            });

            var d2 = new TestDestination("Dest2", (env, ct) =>
            {
                lock (executionOrder) executionOrder.Add("Dest2");
                return Task.FromResult(AlertDeliveryResult.Succeeded("Dest2", TimeSpan.FromMilliseconds(5)));
            });

            var d3 = new TestDestination("Dest3", (env, ct) =>
            {
                lock (executionOrder) executionOrder.Add("Dest3");
                return Task.FromResult(AlertDeliveryResult.Succeeded("Dest3", TimeSpan.FromMilliseconds(5)));
            });

            var registry = new DestinationRegistry(new[] { d1, d2, d3 });
            using var dispatcher = new FanOutDispatcher(registry, dispatchMode: FanOutDispatchMode.Sequential);

            var envelope = CreateEnvelope();
            var result = await dispatcher.DispatchAsync(envelope, new[] { "Dest1", "Dest2", "Dest3" });

            Assert.True(result.AllSucceeded);
            Assert.Equal(3, result.Results.Count);
            Assert.Equal(new[] { "Dest1", "Dest2", "Dest3" }, executionOrder);
        }

        [Fact]
        public async Task ConcurrentFanOut_ExecutesAllDestinationsIndependently_AndHandlesPartialFailures()
        {
            var d1 = TestDestination.CreateSuccessful("Notifier");
            var d2 = TestDestination.CreateFailing("Email", "SMTP Server Unreachable");
            var d3 = TestDestination.CreateThrowing("SMS", "Twilio credentials invalid");
            var d4 = TestDestination.CreateSuccessful("AuditLog");

            var registry = new DestinationRegistry(new[] { d1, d2, d3, d4 });
            using var dispatcher = new FanOutDispatcher(registry, dispatchMode: FanOutDispatchMode.Concurrent);

            var envelope = CreateEnvelope();
            var fanOutResult = await dispatcher.DispatchAsync(envelope, new[] { "Notifier", "Email", "SMS", "AuditLog" });

            // Failure of Email and SMS MUST NOT prevent Notifier or AuditLog
            Assert.False(fanOutResult.AllSucceeded);
            Assert.True(fanOutResult.AnySucceeded);
            Assert.Equal(4, fanOutResult.Results.Count);

            var notifierResult = fanOutResult.Results.First(r => r.DestinationName == "Notifier");
            var emailResult = fanOutResult.Results.First(r => r.DestinationName == "Email");
            var smsResult = fanOutResult.Results.First(r => r.DestinationName == "SMS");
            var auditResult = fanOutResult.Results.First(r => r.DestinationName == "AuditLog");

            Assert.True(notifierResult.Success);
            Assert.False(emailResult.Success);
            Assert.False(smsResult.Success);
            Assert.True(auditResult.Success);

            Assert.Equal(1, d1.Invocations);
            Assert.Equal(1, d2.Invocations);
            Assert.Equal(1, d3.Invocations);
            Assert.Equal(1, d4.Invocations);
        }

        [Fact]
        public async Task FanOut_EnforcesDestinationTimeout()
        {
            // Destination takes 5s, but timeout is 50ms
            var slowDest = TestDestination.CreateTimingOut("SlowWebhook", TimeSpan.FromSeconds(5));
            var fastDest = TestDestination.CreateSuccessful("FastNotifier", TimeSpan.FromMilliseconds(5));

            var registry = new DestinationRegistry(new[] { slowDest, fastDest });
            using var dispatcher = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                destinationTimeout: TimeSpan.FromMilliseconds(50));

            var envelope = CreateEnvelope();
            var fanOutResult = await dispatcher.DispatchAsync(envelope, new[] { "SlowWebhook", "FastNotifier" });

            Assert.Equal(2, fanOutResult.Results.Count);

            var slowResult = fanOutResult.Results.First(r => r.DestinationName == "SlowWebhook");
            var fastResult = fanOutResult.Results.First(r => r.DestinationName == "FastNotifier");

            Assert.True(fastResult.Success);
            Assert.False(slowResult.Success);
            Assert.Contains("timed out", slowResult.DiagnosticMessage);
        }

        [Fact]
        public async Task FanOut_DispatchGroupAsync_ResolvesAllMembers()
        {
            var d1 = TestDestination.CreateSuccessful("D1");
            var d2 = TestDestination.CreateSuccessful("D2");
            var destRegistry = new DestinationRegistry(new[] { d1, d2 });

            var group = new DestinationGroup("TradingGroup", "Trading", new[] { "D1", "D2" });
            var groupRegistry = new DestinationGroupRegistry(new[] { group });

            using var dispatcher = new FanOutDispatcher(destRegistry, groupRegistry);

            var envelope = CreateEnvelope();
            var result = await dispatcher.DispatchGroupAsync(envelope, "TradingGroup");

            Assert.True(result.AllSucceeded);
            Assert.Equal(2, result.Results.Count);
        }

        [Fact]
        public async Task FanOut_MissingDestination_HandledSafelyWithoutThrowing()
        {
            var d1 = TestDestination.CreateSuccessful("PresentDest");
            var destRegistry = new DestinationRegistry(new[] { d1 });
            using var dispatcher = new FanOutDispatcher(destRegistry);

            var envelope = CreateEnvelope();
            var result = await dispatcher.DispatchAsync(envelope, new[] { "PresentDest", "GhostDest" });

            Assert.Equal(2, result.Results.Count);
            Assert.False(result.AllSucceeded);
            Assert.True(result.AnySucceeded);

            var ghostResult = result.Results.First(r => r.DestinationName == "GhostDest");
            Assert.False(ghostResult.Success);
            Assert.Contains("not registered", ghostResult.DiagnosticMessage);
        }
    }
}
