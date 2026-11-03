namespace AvaranFlow.Alerting.Enums
{
    /// <summary>
    /// Governs producer behavior and event admission when the ingestion queue reaches capacity.
    /// </summary>
    public enum QueueOverflowPolicy
    {
        /// <summary>
        /// Drops newly incoming events when the queue is saturated.
        /// Producer returns immediately with bounded zero blocking impact.
        /// </summary>
        DropNewest = 0,

        /// <summary>
        /// Evicts the oldest buffered event to accommodate the newest event.
        /// Producer returns immediately with bounded zero blocking impact.
        /// </summary>
        DropOldest = 1,

        /// <summary>
        /// Blocks the caller thread for a bounded timeout waiting for capacity.
        /// If timeout expires without capacity, the event is dropped.
        /// </summary>
        BlockWithTimeout = 2
    }
}
