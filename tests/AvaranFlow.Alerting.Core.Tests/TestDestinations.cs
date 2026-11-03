using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Models;

namespace AvaranFlow.Alerting.Core.Tests
{
    public sealed class TestDestination : IAlertDestination
    {
        private readonly Func<AlertEnvelope, CancellationToken, Task<AlertDeliveryResult>>? _handler;
        public string Name { get; }
        public ConcurrentBag<AlertEnvelope> DeliveredEnvelopes { get; } = new ConcurrentBag<AlertEnvelope>();
        public int Invocations => DeliveredEnvelopes.Count;
        public int ActiveConcurrency => Volatile.Read(ref _activeConcurrency);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);
        public bool IsDisposed => Volatile.Read(ref _isDisposed) == 1;

        private int _activeConcurrency;
        private int _maxObservedConcurrency;
        private int _isDisposed;

        public TestDestination(string name, Func<AlertEnvelope, CancellationToken, Task<AlertDeliveryResult>>? handler = null)
        {
            Name = name;
            _handler = handler;
        }

        public async Task<AlertDeliveryResult> DeliverAsync(AlertEnvelope envelope, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref _activeConcurrency);
            UpdateMaxConcurrency(current);

            try
            {
                DeliveredEnvelopes.Add(envelope);

                if (_handler != null)
                {
                    return await _handler(envelope, cancellationToken).ConfigureAwait(false);
                }

                return AlertDeliveryResult.Succeeded(Name, TimeSpan.FromMilliseconds(5));
            }
            finally
            {
                Interlocked.Decrement(ref _activeConcurrency);
            }
        }

        private void UpdateMaxConcurrency(int current)
        {
            int initial;
            do
            {
                initial = Volatile.Read(ref _maxObservedConcurrency);
                if (current <= initial) break;
            } while (Interlocked.CompareExchange(ref _maxObservedConcurrency, current, initial) != initial);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _isDisposed, 1);
        }

        public static TestDestination CreateSuccessful(string name, TimeSpan? latency = null)
        {
            return new TestDestination(name, async (env, ct) =>
            {
                if (latency.HasValue && latency.Value > TimeSpan.Zero)
                {
                    await Task.Delay(latency.Value, ct).ConfigureAwait(false);
                }
                return AlertDeliveryResult.Succeeded(name, latency ?? TimeSpan.FromMilliseconds(1));
            });
        }

        public static TestDestination CreateFailing(string name, string message = "Delivery failed")
        {
            return new TestDestination(name, (env, ct) =>
            {
                return Task.FromResult(AlertDeliveryResult.Failed(name, TimeSpan.FromMilliseconds(1), new InvalidOperationException(message)));
            });
        }

        public static TestDestination CreateThrowing(string name, string message = "Catastrophic destination crash")
        {
            return new TestDestination(name, (env, ct) =>
            {
                throw new InvalidOperationException(message);
            });
        }

        public static TestDestination CreateTimingOut(string name, TimeSpan delay)
        {
            return new TestDestination(name, async (env, ct) =>
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                return AlertDeliveryResult.Succeeded(name, delay);
            });
        }
    }
}
