using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Core;

namespace ExceptionMaster.Alerting.Rules
{
    /// <summary>
    /// Represents an immutable, normalized snapshot of event data specifically projected for rule condition evaluation.
    /// Strictly excludes raw Exception references and unredacted reflection structures to guarantee deterministic,
    /// safe, and sandboxed predicate evaluation without side effects.
    /// </summary>
    public sealed class RuleEvaluationContext
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));

        public Guid OccurrenceId { get; }
        public string ExceptionType { get; }
        public string ExceptionKey { get; }
        public AlertSeverity Severity { get; }
        public string Category { get; }
        public string Source { get; }
        public string EnvironmentName { get; }
        public string MachineName { get; }
        public string ProcessName { get; }
        public int ProcessId { get; }
        public int ThreadId { get; }
        public string AppVersion { get; }
        public bool IsFatal { get; }
        public long Count { get; }
        public DateTime FirstSeenUtc { get; }
        public DateTime LastSeenUtc { get; }
        public string CorrelationId { get; }
        public string Caller { get; }
        public string Message { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public RuleEvaluationContext(
            Guid occurrenceId,
            string exceptionType,
            string exceptionKey,
            AlertSeverity severity,
            string category,
            string source,
            string environmentName,
            string machineName,
            string processName,
            int processId,
            int threadId,
            string appVersion,
            bool isFatal,
            long count,
            DateTime firstSeenUtc,
            DateTime lastSeenUtc,
            string correlationId,
            string caller,
            string message,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            OccurrenceId = occurrenceId;
            ExceptionType = exceptionType ?? string.Empty;
            ExceptionKey = exceptionKey ?? string.Empty;
            Severity = severity;
            Category = category ?? string.Empty;
            Source = source ?? string.Empty;
            EnvironmentName = environmentName ?? string.Empty;
            MachineName = machineName ?? string.Empty;
            ProcessName = processName ?? string.Empty;
            ProcessId = processId;
            ThreadId = threadId;
            AppVersion = appVersion ?? string.Empty;
            IsFatal = isFatal;
            Count = count > 0 ? count : 1;
            FirstSeenUtc = firstSeenUtc.Kind == DateTimeKind.Utc ? firstSeenUtc : DateTime.SpecifyKind(firstSeenUtc, DateTimeKind.Utc);
            LastSeenUtc = lastSeenUtc.Kind == DateTimeKind.Utc ? lastSeenUtc : DateTime.SpecifyKind(lastSeenUtc, DateTimeKind.Utc);
            CorrelationId = correlationId ?? string.Empty;
            Caller = caller ?? string.Empty;
            Message = message ?? string.Empty;

            if (metadata != null && metadata.Count > 0)
            {
                var copy = new Dictionary<string, string>(metadata.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in metadata)
                {
                    copy[kvp.Key] = kvp.Value ?? string.Empty;
                }
                Metadata = new ReadOnlyDictionary<string, string>(copy);
            }
            else
            {
                Metadata = EmptyMetadata;
            }
        }

        /// <summary>
        /// Creates a normalized RuleEvaluationContext from an ExceptionMaster ExceptionContext and resolved severity.
        /// Safely extracts stringified key-values from Exception.Data without throwing.
        /// </summary>
        public static RuleEvaluationContext FromExceptionContext(ExceptionContext context, AlertSeverity resolvedSeverity)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Dictionary<string, string>? metadata = null;
            if (context.Exception?.Data != null && context.Exception.Data.Count > 0)
            {
                try
                {
                    metadata = new Dictionary<string, string>(context.Exception.Data.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (System.Collections.DictionaryEntry entry in context.Exception.Data)
                    {
                        if (entry.Key != null)
                        {
                            string key = entry.Key.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(key))
                            {
                                metadata[key] = entry.Value?.ToString() ?? string.Empty;
                            }
                        }
                    }
                }
                catch
                {
                    // Safe containment: never throw if IDictionary traversal fails
                }
            }

            return new RuleEvaluationContext(
                occurrenceId: context.OccurrenceId,
                exceptionType: context.ExceptionType,
                exceptionKey: context.ExceptionKey,
                severity: resolvedSeverity,
                category: context.Category,
                source: context.Source,
                environmentName: context.EnvironmentName,
                machineName: context.MachineName,
                processName: context.ProcessName,
                processId: context.ProcessId,
                threadId: context.ThreadId,
                appVersion: context.AppVersion,
                isFatal: context.IsFatal,
                count: context.Count,
                firstSeenUtc: context.FirstSeenUtc,
                lastSeenUtc: context.LastSeenUtc,
                correlationId: context.CorrelationId,
                caller: context.Caller,
                message: context.Message,
                metadata: metadata);
        }
    }
}
