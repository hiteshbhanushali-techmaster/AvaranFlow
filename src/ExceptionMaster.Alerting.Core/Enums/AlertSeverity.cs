namespace ExceptionMaster.Alerting.Enums
{
    /// <summary>
    /// Represents the deterministic severity levels for an alert.
    /// Ordered strictly by ascending severity.
    /// </summary>
    public enum AlertSeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }
}
