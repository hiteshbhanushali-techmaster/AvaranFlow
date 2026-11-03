using System;

namespace ExceptionMaster.Alerting.Infrastructure
{
    /// <summary>
    /// Provides an abstraction for retrieving the current UTC system time.
    /// Enables deterministic time advancement in unit, concurrency, and stress testing.
    /// </summary>
    public interface ISystemClock
    {
        /// <summary>
        /// Gets the current UTC date and time.
        /// </summary>
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// Default production implementation of <see cref="ISystemClock"/> using <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public sealed class DefaultSystemClock : ISystemClock
    {
        /// <summary>
        /// Shared singleton instance.
        /// </summary>
        public static readonly DefaultSystemClock Instance = new DefaultSystemClock();

        public DateTime UtcNow => DateTime.UtcNow;
    }
}
