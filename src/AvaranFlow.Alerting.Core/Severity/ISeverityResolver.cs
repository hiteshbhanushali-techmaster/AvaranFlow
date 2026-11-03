using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Severity
{
    /// <summary>
    /// Deterministically resolves the AlertSeverity for an incoming ExceptionContext.
    /// </summary>
    public interface ISeverityResolver
    {
        /// <summary>
        /// Resolves the effective AlertSeverity following the deterministic 4-step precedence.
        /// </summary>
        /// <param name="context">The source ExceptionContext.</param>
        /// <param name="explicitSeverity">Optional explicitly provided severity override.</param>
        /// <param name="defaultSeverity">Optional fallback default severity.</param>
        /// <returns>The resolved AlertSeverity.</returns>
        AlertSeverity Resolve(
            ExceptionContext context,
            AlertSeverity? explicitSeverity = null,
            AlertSeverity? defaultSeverity = null);
    }
}
