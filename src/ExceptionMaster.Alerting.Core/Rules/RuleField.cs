namespace ExceptionMaster.Alerting.Rules
{
    /// <summary>
    /// Identifies the target context field evaluated by a rule predicate.
    /// </summary>
    public enum RuleField
    {
        ExceptionType = 0,
        ExceptionKey = 1,
        Severity = 2,
        Category = 3,
        Source = 4,
        EnvironmentName = 5,
        MachineName = 6,
        ProcessName = 7,
        ProcessId = 8,
        ThreadId = 9,
        AppVersion = 10,
        IsFatal = 11,
        Count = 12,
        FirstSeenUtc = 13,
        LastSeenUtc = 14,
        CorrelationId = 15,
        Caller = 16,
        Message = 17,
        Metadata = 18
    }
}
