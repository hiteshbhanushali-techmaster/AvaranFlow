using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Alerting.Destinations;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Models;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class DestinationSecurityAndConcurrencyTests
    {
        private static AlertEnvelope CreateEnvelope()
        {
            var meta = new Dictionary<string, string> { { "AccountId", "ACC-789" } };
            return new AlertEnvelope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SecKey",
                "UnauthorizedAccessException",
                "Access denied",
                AlertSeverity.Error,
                "Security",
                "AuthService",
                "CORR-SEC",
                "Production",
                "AUTH-01",
                "AuthDaemon",
                9999,
                false,
                1,
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow,
                meta);
        }

        #region Security Tests

        [Fact]
        public void DestinationContract_MethodParameters_OnlyAcceptAlertEnvelope()
        {
            var method = typeof(IAlertDestination).GetMethod(nameof(IAlertDestination.DeliverAsync));
            Assert.NotNull(method);

            var parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);

            // First parameter is AlertEnvelope
            Assert.Equal(typeof(AlertEnvelope), parameters[0].ParameterType);
            // Second parameter is CancellationToken
            Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);

            // Return type is Task<AlertDeliveryResult>
            Assert.Equal(typeof(Task<AlertDeliveryResult>), method.ReturnType);
        }

        [Fact]
        public async Task DestinationReceives_OnlySafeProjectedEnvelope_AndNoRawException()
        {
            AlertEnvelope? receivedEnvelope = null;

            var testDest = new TestDestination("Inspector", (env, ct) =>
            {
                receivedEnvelope = env;
                return Task.FromResult(AlertDeliveryResult.Succeeded("Inspector", TimeSpan.FromMilliseconds(1)));
            });

            var registry = new DestinationRegistry(new[] { testDest });
            using var dispatcher = new FanOutDispatcher(registry);

            var sourceEnvelope = CreateEnvelope();
            var result = await dispatcher.DispatchAsync(sourceEnvelope, new[] { "Inspector" });

            Assert.True(result.AllSucceeded);
            Assert.NotNull(receivedEnvelope);
            Assert.Same(sourceEnvelope, receivedEnvelope);

            // Verify received envelope does not expose raw Exception or unredacted context
            var properties = receivedEnvelope.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.DoesNotContain(properties, p => p.PropertyType == typeof(Exception));
            Assert.DoesNotContain(properties, p => p.PropertyType.FullName != null && p.PropertyType.FullName.Contains("ExceptionContext"));
        }

        #endregion

        #region Concurrency Tests

        [Fact]
        public async Task GlobalDestinationConcurrency_IsStrictlyBounded()
        {
            const int maxConcurrencyLimit = 4;
            const int totalTasks = 30;

            var testDest = new TestDestination("ThrottledDest", async (env, ct) =>
            {
                // Artificially hold execution for 30ms to create concurrency pressure
                await Task.Delay(30, ct).ConfigureAwait(false);
                return AlertDeliveryResult.Succeeded("ThrottledDest", TimeSpan.FromMilliseconds(30));
            });

            var registry = new DestinationRegistry(new[] { testDest });
            using var dispatcher = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                maxConcurrency: maxConcurrencyLimit);

            var envelope = CreateEnvelope();

            // Fire 30 concurrent dispatches simultaneously
            var tasks = Enumerable.Range(0, totalTasks)
                .Select(_ => dispatcher.DispatchAsync(envelope, new[] { "ThrottledDest" }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(totalTasks, results.Length);
            Assert.All(results, r => Assert.True(r.AllSucceeded));

            // Verify observed concurrency NEVER exceeded maxConcurrencyLimit (4)
            Assert.True(
                testDest.MaxObservedConcurrency <= maxConcurrencyLimit,
                $"Observed concurrency ({testDest.MaxObservedConcurrency}) exceeded limit ({maxConcurrencyLimit})");
        }

        [Fact]
        public async Task ConcurrentFanOut_UnderStormConditions_NeverDuplicatesOrDrops()
        {
            const int destinationCount = 10;
            const int eventCount = 20;

            var destinations = Enumerable.Range(1, destinationCount)
                .Select(i => TestDestination.CreateSuccessful($"Dest_{i}", TimeSpan.FromMilliseconds(5)))
                .ToList();

            var registry = new DestinationRegistry(destinations);
            using var dispatcher = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                maxConcurrency: 16);

            var destinationNames = destinations.Select(d => d.Name).ToList();

            // Run 20 parallel dispatches across all 10 destinations
            var tasks = Enumerable.Range(0, eventCount)
                .Select(_ => dispatcher.DispatchAsync(CreateEnvelope(), destinationNames))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(eventCount, results.Length);
            Assert.All(results, r => Assert.True(r.AllSucceeded));

            // Each destination must have received exactly 20 invocations (zero duplicates, zero lost)
            foreach (var dest in destinations)
            {
                Assert.Equal(eventCount, dest.Invocations);
            }
        }

        [Fact]
        public async Task SharedGlobalConcurrencyBudget_AcrossMultipleDispatchersAndGenerations_NeverExceedsLimit()
        {
            // The architecture mandates that GlobalMaxDestinationConcurrency bounds total in-flight DeliverAsync
            // across all fan-out dispatches, all fallback dispatches, all concurrent events, all rules,
            // and all active/draining pipeline generations.
            const int globalLimit = 3;
            var sharedLimiter = new DestinationConcurrencyLimiter(globalLimit);

            int combinedActiveConcurrency = 0;
            int maxCombinedObserved = 0;

            Func<AlertEnvelope, CancellationToken, Task<AlertDeliveryResult>> handler = async (env, ct) =>
            {
                int current = Interlocked.Increment(ref combinedActiveConcurrency);
                int initial;
                do
                {
                    initial = Volatile.Read(ref maxCombinedObserved);
                    if (current <= initial) break;
                } while (Interlocked.CompareExchange(ref maxCombinedObserved, current, initial) != initial);

                try
                {
                    await Task.Delay(30, ct).ConfigureAwait(false);
                    return AlertDeliveryResult.Succeeded("SharedDest", TimeSpan.FromMilliseconds(30));
                }
                finally
                {
                    Interlocked.Decrement(ref combinedActiveConcurrency);
                }
            };

            var d1 = new TestDestination("Dest_A", handler);
            var d2 = new TestDestination("Dest_B", handler);
            var registry = new DestinationRegistry(new[] { d1, d2 });

            // Dispatcher 1 (represents Generation N)
            using var dispatcherGenN = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                sharedConcurrencyLimiter: sharedLimiter);

            // Dispatcher 2 (represents Generation N+1 active simultaneously during reconfiguration drain)
            using var dispatcherGenNPlus1 = new FanOutDispatcher(
                registry,
                dispatchMode: FanOutDispatchMode.Concurrent,
                sharedConcurrencyLimiter: sharedLimiter);

            // Fallback dispatcher (concurrent fallback requests sharing the same budget)
            var fallbackChain = new FallbackChain("Chain", "Chain", new[] { "Dest_A", "Dest_B" });
            using var fallbackDispatcher = new FallbackDispatcher(
                registry,
                sharedConcurrencyLimiter: sharedLimiter);

            var envelope = CreateEnvelope();

            // Simultaneously fire 15 dispatches from Gen N, 15 from Gen N+1, and 15 fallback dispatches (45 total)
            var tasksGenN = Enumerable.Range(0, 15).Select(_ => dispatcherGenN.DispatchAsync(envelope, new[] { "Dest_A" }));
            var tasksGenNPlus1 = Enumerable.Range(0, 15).Select(_ => dispatcherGenNPlus1.DispatchAsync(envelope, new[] { "Dest_B" }));
            var tasksFallback = Enumerable.Range(0, 15).Select(_ => fallbackDispatcher.DispatchAsync(envelope, fallbackChain));

            var allTasks = tasksGenN.Cast<Task>()
                .Concat(tasksGenNPlus1.Cast<Task>())
                .Concat(tasksFallback.Cast<Task>())
                .ToArray();
            await Task.WhenAll(allTasks);

            // Verification: Total concurrent executions across ALL dispatchers/generations never exceeded the global limit (3)
            Assert.True(
                maxCombinedObserved <= globalLimit,
                $"Max combined concurrency observed ({maxCombinedObserved}) exceeded global limit ({globalLimit})");
        }

        #endregion
    }
}
