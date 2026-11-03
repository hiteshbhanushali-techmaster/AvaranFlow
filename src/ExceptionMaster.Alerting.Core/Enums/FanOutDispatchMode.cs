namespace ExceptionMaster.Alerting.Enums
{
    /// <summary>
    /// Specifies the execution strategy for fan-out destinations in a destination group.
    /// </summary>
    public enum FanOutDispatchMode
    {
        /// <summary>
        /// Fan-out destinations are dispatched concurrently, bounded by the global destination semaphore.
        /// </summary>
        Concurrent = 0,

        /// <summary>
        /// Fan-out destinations are dispatched sequentially in configured order.
        /// </summary>
        Sequential = 1
    }
}
