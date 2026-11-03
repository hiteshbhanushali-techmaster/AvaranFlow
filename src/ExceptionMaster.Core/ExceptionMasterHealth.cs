namespace ExceptionMaster.Core
{
    /// <summary>
    /// Thread-safe diagnostic snapshot reflecting runtime logging activity, queue pressure, and sink health.
    /// </summary>
    public sealed class ExceptionMasterHealth
    {
        public long TotalLogged { get; }
        public long TotalAggregated { get; }
        public long TotalDropped { get; }
        public long TotalDispatched { get; }
        public long TotalSinkFailures { get; }
        public long TotalFallbackInvocations { get; }
        public int PendingQueueDepth { get; }
        public bool IsShutdown { get; }

        public ExceptionMasterHealth(
            long totalLogged,
            long totalAggregated,
            long totalDropped,
            long totalDispatched,
            long totalSinkFailures,
            long totalFallbackInvocations,
            int pendingQueueDepth,
            bool isShutdown)
        {
            TotalLogged = totalLogged;
            TotalAggregated = totalAggregated;
            TotalDropped = totalDropped;
            TotalDispatched = totalDispatched;
            TotalSinkFailures = totalSinkFailures;
            TotalFallbackInvocations = totalFallbackInvocations;
            PendingQueueDepth = pendingQueueDepth;
            IsShutdown = isShutdown;
        }

        public override string ToString()
        {
            return $"Logged: {TotalLogged}, Aggregated: {TotalAggregated}, Dropped: {TotalDropped}, " +
                   $"Dispatched: {TotalDispatched}, SinkFailures: {TotalSinkFailures}, " +
                   $"Fallbacks: {TotalFallbackInvocations}, QueueDepth: {PendingQueueDepth}, Shutdown: {IsShutdown}";
        }
    }
}
