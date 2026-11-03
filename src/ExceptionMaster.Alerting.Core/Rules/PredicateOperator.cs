namespace ExceptionMaster.Alerting.Rules
{
    /// <summary>
    /// Specifies the comparison operation applied in a predicate condition.
    /// </summary>
    public enum PredicateOperator
    {
        Equals = 0,
        NotEquals = 1,
        Contains = 2,
        StartsWith = 3,
        EndsWith = 4,
        Exists = 5,
        GreaterThan = 6,
        GreaterThanOrEqual = 7,
        LessThan = 8,
        LessThanOrEqual = 9
    }
}
