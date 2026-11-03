using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AvaranFlow.Core
{
    /// <summary>
    /// Manages bounded asynchronous fire-and-forget notification broadcasts to ExceptionRaised subscribers
    /// with subscriber isolation and execution-context reentrancy protection.
    /// </summary>
    public sealed class EventBroadcaster : IDisposable
    {
        private static readonly AsyncLocal<bool> IsInsideSubscriberContext = new AsyncLocal<bool>();

        private readonly Func<Action<ExceptionContext>> _subscriberProvider;
        private readonly ConcurrentQueue<ExceptionContext> _notificationQueue;
        private readonly SemaphoreSlim _signal;
        private readonly CancellationTokenSource _cts;
        private readonly Task _workerTask;
        private readonly int _maxQueueCapacity;
        private int _isDisposed;
        private long _droppedNotifications;

        public static bool IsCurrentContextInSubscriber => IsInsideSubscriberContext.Value;

        public long DroppedNotifications => Volatile.Read(ref _droppedNotifications);

        public EventBroadcaster(Func<Action<ExceptionContext>> subscriberProvider, int maxQueueCapacity = 2000)
        {
            _subscriberProvider = subscriberProvider;
            _maxQueueCapacity = maxQueueCapacity > 0 ? maxQueueCapacity : 2000;
            _notificationQueue = new ConcurrentQueue<ExceptionContext>();
            _signal = new SemaphoreSlim(0);
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(ProcessNotificationQueueAsync);
        }

        /// <summary>
        /// Enqueues an exception context for asynchronous notification broadcast.
        /// If called from within an active subscriber, event publication is suppressed to prevent cycles.
        /// </summary>
        public bool TryEnqueue(ExceptionContext context)
        {
            if (context == null || context.IsFromEventSubscriber || IsCurrentContextInSubscriber)
            {
                // Suppress recursive event publication
                return false;
            }

            if (Volatile.Read(ref _isDisposed) == 1 || _cts.IsCancellationRequested)
            {
                return false;
            }

            var handlers = _subscriberProvider?.Invoke();
            if (handlers == null)
            {
                return false; // No subscribers registered
            }

            if (_notificationQueue.Count >= _maxQueueCapacity)
            {
                // Bounded overflow: drop newest notification to prevent unbounded memory growth
                Interlocked.Increment(ref _droppedNotifications);
                return false;
            }

            _notificationQueue.Enqueue(context);
            _signal.Release();
            return true;
        }

        private int _inFlightBroadcasts;

        private async Task ProcessNotificationQueueAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_notificationQueue.TryDequeue(out var context))
                {
                    Interlocked.Increment(ref _inFlightBroadcasts);
                    try
                    {
                        BroadcastToSubscribers(context);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _inFlightBroadcasts);
                    }
                }
            }
        }

        private void BroadcastToSubscribers(ExceptionContext context)
        {
            var handlers = _subscriberProvider?.Invoke();
            if (handlers == null) return;

            var invocationList = handlers.GetInvocationList();
            foreach (var handler in invocationList)
            {
                if (handler is Action<ExceptionContext> action)
                {
                    IsInsideSubscriberContext.Value = true;
                    try
                    {
                        action(context);
                    }
                    catch (Exception ex)
                    {
                        // Subscriber isolation: an individual subscriber failure never affects other subscribers
                        SafeEmergencyWrite($"[ExceptionMaster EventSubscriber Error]: {ex.Message}");
                    }
                    finally
                    {
                        IsInsideSubscriberContext.Value = false;
                    }
                }
            }
        }

        private static void SafeEmergencyWrite(string message)
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
                // Safe swallow
            }
        }

        public void Drain(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while ((!_notificationQueue.IsEmpty || Volatile.Read(ref _inFlightBroadcasts) > 0) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            try
            {
                _cts.Cancel();
                _signal.Release();
                _workerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Safe swallow
            }
            finally
            {
                _cts.Dispose();
                _signal.Dispose();
            }
        }
    }
}
