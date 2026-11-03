using System;

namespace AvaranFlow.Alerting.Models
{
    /// <summary>
    /// Represents the immutable result of an alert delivery attempt to an alert destination.
    /// Captures execution duration, success status, caller cancellation, and failure diagnostics safely.
    /// </summary>
    public sealed class AlertDeliveryResult
    {
        public bool Success { get; }
        public bool IsCancelled { get; }
        public string DestinationName { get; }
        public TimeSpan Elapsed { get; }
        public Exception? FailureException { get; }
        public string? DiagnosticMessage { get; }

        public AlertDeliveryResult(
            bool success,
            string destinationName,
            TimeSpan elapsed,
            Exception? failureException = null,
            string? diagnosticMessage = null,
            bool isCancelled = false)
        {
            Success = success;
            IsCancelled = isCancelled;
            DestinationName = destinationName ?? "Unknown";
            Elapsed = elapsed >= TimeSpan.Zero ? elapsed : TimeSpan.Zero;
            FailureException = failureException;
            DiagnosticMessage = diagnosticMessage;
        }

        public static AlertDeliveryResult Succeeded(string destinationName, TimeSpan elapsed, string? diagnosticMessage = null)
        {
            return new AlertDeliveryResult(true, destinationName, elapsed, null, diagnosticMessage, isCancelled: false);
        }

        public static AlertDeliveryResult Failed(string destinationName, TimeSpan elapsed, Exception? ex = null, string? diagnosticMessage = null)
        {
            return new AlertDeliveryResult(false, destinationName, elapsed, ex, diagnosticMessage ?? ex?.Message, isCancelled: false);
        }

        public static AlertDeliveryResult Cancelled(string destinationName, TimeSpan elapsed, string? diagnosticMessage = null)
        {
            var msg = diagnosticMessage ?? $"Destination '{destinationName}' delivery was cancelled by caller/host request.";
            return new AlertDeliveryResult(false, destinationName, elapsed, new OperationCanceledException(msg), msg, isCancelled: true);
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"[Success] {DestinationName} in {Elapsed.TotalMilliseconds:F1}ms";
            }
            if (IsCancelled)
            {
                return $"[Cancelled] {DestinationName} in {Elapsed.TotalMilliseconds:F1}ms: {DiagnosticMessage}";
            }
            return $"[Failed] {DestinationName} in {Elapsed.TotalMilliseconds:F1}ms: {DiagnosticMessage ?? FailureException?.Message}";
        }
    }
}
