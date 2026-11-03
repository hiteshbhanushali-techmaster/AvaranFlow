using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ExceptionMaster.Alerting.Enums;

namespace ExceptionMaster.Alerting.Models
{
    /// <summary>
    /// Represents an immutable, normalized, and destination-safe alert payload.
    /// Strictly excludes raw Exception instances and unredacted diagnostic dictionaries
    /// to guarantee safe distribution across external channels.
    /// </summary>
    public sealed class AlertEnvelope
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));

        public Guid AlertId { get; }
        public Guid OccurrenceId { get; }
        public string ExceptionKey { get; }
        public string ExceptionType { get; }
        public string SafeMessage { get; }
        public AlertSeverity Severity { get; }
        public string Category { get; }
        public string Source { get; }
        public string CorrelationId { get; }
        public string EnvironmentName { get; }
        public string MachineName { get; }
        public string ProcessName { get; }
        public int ProcessId { get; }
        public bool IsFatal { get; }
        public long Count { get; }
        public DateTime FirstSeenUtc { get; }
        public DateTime LastSeenUtc { get; }
        public DateTime GeneratedUtc { get; }
        public IReadOnlyDictionary<string, string> SafeMetadata { get; }
        public string? SafeStackTrace { get; }

        public AlertEnvelope(
            Guid alertId,
            Guid occurrenceId,
            string exceptionKey,
            string exceptionType,
            string safeMessage,
            AlertSeverity severity,
            string category,
            string source,
            string correlationId,
            string environmentName,
            string machineName,
            string processName,
            int processId,
            bool isFatal,
            long count,
            DateTime firstSeenUtc,
            DateTime lastSeenUtc,
            DateTime generatedUtc,
            IReadOnlyDictionary<string, string>? safeMetadata = null,
            string? safeStackTrace = null)
        {
            AlertId = alertId != Guid.Empty ? alertId : Guid.NewGuid();
            OccurrenceId = occurrenceId;
            ExceptionKey = exceptionKey ?? string.Empty;
            ExceptionType = exceptionType ?? "Unknown";
            SafeMessage = safeMessage ?? string.Empty;
            Severity = severity;
            Category = category ?? "General";
            Source = source ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            EnvironmentName = environmentName ?? "Production";
            MachineName = machineName ?? string.Empty;
            ProcessName = processName ?? string.Empty;
            ProcessId = processId;
            IsFatal = isFatal;
            Count = count > 0 ? count : 1;
            FirstSeenUtc = firstSeenUtc.Kind == DateTimeKind.Utc ? firstSeenUtc : DateTime.SpecifyKind(firstSeenUtc, DateTimeKind.Utc);
            LastSeenUtc = lastSeenUtc.Kind == DateTimeKind.Utc ? lastSeenUtc : DateTime.SpecifyKind(lastSeenUtc, DateTimeKind.Utc);
            GeneratedUtc = generatedUtc.Kind == DateTimeKind.Utc ? generatedUtc : DateTime.SpecifyKind(generatedUtc, DateTimeKind.Utc);

            if (safeMetadata != null && safeMetadata.Count > 0)
            {
                var copy = new Dictionary<string, string>(safeMetadata.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in safeMetadata)
                {
                    copy[kvp.Key] = kvp.Value ?? string.Empty;
                }
                SafeMetadata = new ReadOnlyDictionary<string, string>(copy);
            }
            else
            {
                SafeMetadata = EmptyMetadata;
            }

            SafeStackTrace = safeStackTrace;
        }

        public override string ToString()
        {
            return $"[{GeneratedUtc:yyyy-MM-dd HH:mm:ss} UTC] [{Severity}] {Category}/{ExceptionType}: {SafeMessage} (Count: {Count}, Key: {ExceptionKey})";
        }
    }
}
