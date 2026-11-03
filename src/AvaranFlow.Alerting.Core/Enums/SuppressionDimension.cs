namespace AvaranFlow.Alerting.Enums
{
    /// <summary>
    /// Identifies the context attribute used to partition suppression / cooldown state.
    /// </summary>
    public enum SuppressionDimension
    {
        /// <summary>
        /// Suppresses events matching the same ExceptionKey.
        /// </summary>
        ExceptionKey = 0,

        /// <summary>
        /// Suppresses events matching the same Category.
        /// </summary>
        Category = 1,

        /// <summary>
        /// Suppresses events matching the same Source.
        /// </summary>
        Source = 2,

        /// <summary>
        /// Suppresses events matching the same ExceptionType.
        /// </summary>
        ExceptionType = 3,

        /// <summary>
        /// Suppresses events matching the same CorrelationId.
        /// </summary>
        CorrelationId = 4,

        /// <summary>
        /// Suppresses events matching the same Message.
        /// </summary>
        Message = 5,

        /// <summary>
        /// Suppresses all events matching the rule regardless of context fields (rule-wide cooldown).
        /// </summary>
        Rule = 6,

        /// <summary>
        /// Suppresses events matching a custom key from the context's SafeMetadata dictionary.
        /// </summary>
        Custom = 7
    }
}
