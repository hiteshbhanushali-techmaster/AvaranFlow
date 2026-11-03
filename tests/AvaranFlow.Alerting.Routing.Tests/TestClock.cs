using System;
using AvaranFlow.Alerting.Infrastructure;

namespace AvaranFlow.Alerting.Routing.Tests
{
    /// <summary>
    /// Controllable clock implementation for deterministic testing of cooldown, TTL, and elapsed time triggers.
    /// </summary>
    public sealed class TestClock : ISystemClock
    {
        private DateTime _utcNow;
        private readonly object _lock = new object();

        public DateTime UtcNow
        {
            get
            {
                lock (_lock)
                {
                    return _utcNow;
                }
            }
        }

        public TestClock(DateTime? initialTime = null)
        {
            _utcNow = initialTime ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        public void Advance(TimeSpan duration)
        {
            lock (_lock)
            {
                _utcNow = _utcNow.Add(duration);
            }
        }

        public void Set(DateTime time)
        {
            lock (_lock)
            {
                _utcNow = time.Kind == DateTimeKind.Utc ? time : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            }
        }
    }
}
