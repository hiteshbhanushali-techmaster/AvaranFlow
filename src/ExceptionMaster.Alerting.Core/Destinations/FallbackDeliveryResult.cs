using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExceptionMaster.Alerting.Models;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Represents the immutable aggregate outcome of executing a sequential fallback chain.
    /// Captures the chronological attempt sequence, the successful destination (if any), and total elapsed duration.
    /// </summary>
    public sealed class FallbackDeliveryResult
    {
        private static readonly IReadOnlyList<AlertDeliveryResult> EmptyAttempts =
            new ReadOnlyCollection<AlertDeliveryResult>(new AlertDeliveryResult[0]);

        public bool Succeeded { get; }
        public string? SuccessfulDestination { get; }
        public IReadOnlyList<AlertDeliveryResult> AttemptResults { get; }
        public TimeSpan TotalElapsed { get; }

        public FallbackDeliveryResult(
            bool succeeded,
            string? successfulDestination,
            IEnumerable<AlertDeliveryResult>? attemptResults,
            TimeSpan totalElapsed)
        {
            Succeeded = succeeded;
            SuccessfulDestination = string.IsNullOrWhiteSpace(successfulDestination) ? null : successfulDestination!.Trim();
            TotalElapsed = totalElapsed >= TimeSpan.Zero ? totalElapsed : TimeSpan.Zero;

            AttemptResults = attemptResults != null
                ? new ReadOnlyCollection<AlertDeliveryResult>(attemptResults.Where(r => r != null).ToList())
                : EmptyAttempts;
        }

        public override string ToString()
        {
            return Succeeded
                ? $"FallbackDeliveryResult: Succeeded on '{SuccessfulDestination}' after {AttemptResults.Count} attempt(s) in {TotalElapsed.TotalMilliseconds:F1}ms"
                : $"FallbackDeliveryResult: Failed across all {AttemptResults.Count} attempt(s) in {TotalElapsed.TotalMilliseconds:F1}ms";
        }
    }
}
