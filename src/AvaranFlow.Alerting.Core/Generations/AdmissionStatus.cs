namespace AvaranFlow.Alerting.Generations
{
    /// <summary>
    /// Internal status returned by generation admission attempts to distinguish overflow rejection
    /// from generation retirement during concurrent reconfiguration.
    /// </summary>
    internal enum AdmissionStatus
    {
        /// <summary>
        /// The event was successfully admitted into the generation.
        /// </summary>
        Admitted,

        /// <summary>
        /// The event was rejected and dropped due to the generation's configured queue overflow policy.
        /// </summary>
        RejectedOverflow,

        /// <summary>
        /// The generation has stopped accepting admissions (e.g. superseded by a new generation).
        /// Admission should be retried against the newly active generation without recording a drop.
        /// </summary>
        GenerationNotActive
    }
}
