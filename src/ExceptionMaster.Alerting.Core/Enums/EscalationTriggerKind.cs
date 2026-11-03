namespace ExceptionMaster.Alerting.Enums
{
    /// <summary>
    /// Identifies the criteria used to evaluate whether an escalation threshold is met.
    /// </summary>
    public enum EscalationTriggerKind
    {
        /// <summary>
        /// Escalates when cumulative occurrences reach or exceed a specified count threshold.
        /// </summary>
        OccurrenceCount = 0,

        /// <summary>
        /// Escalates when elapsed time since the first seen occurrence exceeds a threshold.
        /// </summary>
        ElapsedTime = 1,

        /// <summary>
        /// Escalates when severity matches or exceeds a critical severity threshold.
        /// </summary>
        Severity = 2
    }
}
