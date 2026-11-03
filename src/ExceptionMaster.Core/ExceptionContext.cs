using System;

namespace ExceptionMaster.Core
{
    /// <summary>
    /// Represents an immutable diagnostic context snapshot for an observed or aggregated exception.
    /// </summary>
    public sealed class ExceptionContext
    {
        public Exception Exception { get; }
        public string Message { get; }
        public string ExceptionType { get; }
        public string StackTrace { get; }
        public DateTime FirstSeenUtc { get; }
        public DateTime LastSeenUtc { get; }
        public long Count { get; }
        public string Caller { get; }
        public string Category { get; }
        public string MachineName { get; }
        public string ProcessName { get; }
        public int ProcessId { get; }
        public int ThreadId { get; }
        public string AppVersion { get; }
        public string CorrelationId { get; }
        public bool IsFatal { get; }
        public string ExceptionKey { get; }
        public string EnvironmentName { get; }
        public string Source { get; }
        public string InnerExceptionDetails { get; }
        public Guid OccurrenceId { get; }
        public int SchemaVersion { get; }
        public DateTime WindowStartUtc { get; }
        public long DeltaCount { get; }

        internal bool IsFromEventSubscriber { get; }

        public ExceptionContext(
            Exception exception,
            string message,
            string exceptionType,
            string stackTrace,
            DateTime firstSeenUtc,
            DateTime lastSeenUtc,
            long count,
            string caller,
            string category,
            string machineName,
            string processName,
            int processId,
            int threadId,
            string appVersion,
            string correlationId,
            bool isFatal,
            string exceptionKey,
            string environmentName,
            string source,
            string innerExceptionDetails,
            Guid occurrenceId,
            int schemaVersion = 1,
            bool isFromEventSubscriber = false,
            DateTime? windowStartUtc = null,
            long? deltaCount = null)
        {
            Exception = exception;
            Message = message ?? string.Empty;
            ExceptionType = exceptionType ?? (exception != null ? exception.GetType().FullName : "Unknown");
            StackTrace = stackTrace ?? string.Empty;
            FirstSeenUtc = firstSeenUtc.Kind == DateTimeKind.Utc ? firstSeenUtc : DateTime.SpecifyKind(firstSeenUtc, DateTimeKind.Utc);
            LastSeenUtc = lastSeenUtc.Kind == DateTimeKind.Utc ? lastSeenUtc : DateTime.SpecifyKind(lastSeenUtc, DateTimeKind.Utc);
            Count = count > 0 ? count : 1;
            DeltaCount = deltaCount.HasValue && deltaCount.Value > 0 ? deltaCount.Value : Count;
            Caller = caller ?? string.Empty;
            Category = category ?? "General";
            MachineName = machineName ?? string.Empty;
            ProcessName = processName ?? string.Empty;
            ProcessId = processId;
            ThreadId = threadId;
            AppVersion = appVersion ?? "1.0.0.0";
            CorrelationId = correlationId ?? string.Empty;
            IsFatal = isFatal;
            ExceptionKey = exceptionKey ?? string.Empty;
            EnvironmentName = environmentName ?? "Production";
            Source = source ?? (exception != null ? exception.Source ?? string.Empty : string.Empty);
            InnerExceptionDetails = innerExceptionDetails ?? string.Empty;
            OccurrenceId = occurrenceId != Guid.Empty ? occurrenceId : Guid.NewGuid();
            SchemaVersion = schemaVersion;
            IsFromEventSubscriber = isFromEventSubscriber;

            var ws = windowStartUtc.GetValueOrDefault(FirstSeenUtc);
            WindowStartUtc = ws.Kind == DateTimeKind.Utc ? ws : DateTime.SpecifyKind(ws, DateTimeKind.Utc);
        }

        /// <summary>
        /// Creates a new snapshot with updated count and last seen timestamp.
        /// </summary>
        public ExceptionContext WithUpdatedOccurrence(long newCount, DateTime newLastSeenUtc, bool isFatal)
        {
            return WithUpdatedOccurrence(newCount, 0, newLastSeenUtc, isFatal);
        }

        /// <summary>
        /// Creates a new snapshot with updated cumulative count, delta batch count, and last seen timestamp.
        /// </summary>
        public ExceptionContext WithUpdatedOccurrence(long newCount, long deltaCount, DateTime newLastSeenUtc, bool isFatal)
        {
            return new ExceptionContext(
                Exception,
                Message,
                ExceptionType,
                StackTrace,
                FirstSeenUtc,
                newLastSeenUtc,
                newCount,
                Caller,
                Category,
                MachineName,
                ProcessName,
                ProcessId,
                ThreadId,
                AppVersion,
                CorrelationId,
                IsFatal || isFatal,
                ExceptionKey,
                EnvironmentName,
                Source,
                InnerExceptionDetails,
                Guid.NewGuid(),
                SchemaVersion,
                IsFromEventSubscriber,
                WindowStartUtc,
                deltaCount > 0 ? deltaCount : (newCount > Count ? newCount - Count : newCount));
        }

        public override string ToString()
        {
            return $"[{LastSeenUtc:yyyy-MM-dd HH:mm:ss} UTC] [{Category}] {ExceptionType}: {Message} (Count: {Count}, Key: {ExceptionKey})";
        }
    }
}
