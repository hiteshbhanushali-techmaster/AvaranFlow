namespace AvaranFlow.Alerting.Enums
{
    /// <summary>
    /// Represents the lifecycle state of an immutable pipeline generation.
    /// </summary>
    public enum GenerationState
    {
        /// <summary>
        /// The generation is actively accepting and processing admitted events.
        /// </summary>
        Active = 0,

        /// <summary>
        /// The generation has been superseded by a newer generation and is draining remaining admitted events.
        /// No newly incoming events may be admitted to a draining generation.
        /// </summary>
        Draining = 1,

        /// <summary>
        /// The generation has finished draining or timed out and its resources are disposed.
        /// </summary>
        Disposed = 2
    }
}
