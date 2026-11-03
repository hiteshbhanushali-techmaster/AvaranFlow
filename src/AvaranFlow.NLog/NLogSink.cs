using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;
using NLog;

namespace AvaranFlow.NLog
{
    public sealed class NLogSinkOptions
    {
        public string LoggerName { get; set; } = "ExceptionMaster";
    }

    /// <summary>
    /// Optional adapter that relays ExceptionMaster events to the host's existing NLog configuration.
    /// Does not alter the host's NLog targets, configuration, or lifecycle.
    /// </summary>
    public sealed class NLogSink : IExceptionSink
    {
        private readonly Logger _logger;
        public string Name => "NLogSink";

        public NLogSink(string loggerName = "ExceptionMaster")
        {
            _logger = LogManager.GetLogger(string.IsNullOrWhiteSpace(loggerName) ? "ExceptionMaster" : loggerName);
        }

        public NLogSink(NLogSinkOptions options)
            : this(options?.LoggerName)
        {
        }

        public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
        {
            if (context == null || cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            LogToNLog(context);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
        {
            if (contexts == null || contexts.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            foreach (var context in contexts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                LogToNLog(context);
            }

            return Task.CompletedTask;
        }

        private void LogToNLog(ExceptionContext context)
        {
            var level = context.IsFatal ? LogLevel.Fatal : LogLevel.Error;
            var eventInfo = new LogEventInfo(level, _logger.Name, context.Message)
            {
                Exception = context.Exception,
                TimeStamp = context.LastSeenUtc.ToLocalTime()
            };

            eventInfo.Properties["ExceptionKey"] = context.ExceptionKey;
            eventInfo.Properties["OccurrenceId"] = context.OccurrenceId;
            eventInfo.Properties["Count"] = context.Count;
            eventInfo.Properties["Category"] = context.Category;
            eventInfo.Properties["Caller"] = context.Caller;
            eventInfo.Properties["CorrelationId"] = context.CorrelationId;
            eventInfo.Properties["MachineName"] = context.MachineName;
            eventInfo.Properties["ProcessName"] = context.ProcessName;
            eventInfo.Properties["ProcessId"] = context.ProcessId;
            eventInfo.Properties["ThreadId"] = context.ThreadId;
            eventInfo.Properties["IsFatal"] = context.IsFatal;
            eventInfo.Properties["FirstSeenUtc"] = context.FirstSeenUtc;
            eventInfo.Properties["LastSeenUtc"] = context.LastSeenUtc;

            _logger.Log(eventInfo);
        }

        public void Flush()
        {
            // Do not call LogManager.Flush() or LogManager.Shutdown() as host owns the lifecycle
        }

        public void Dispose()
        {
            // Host owns NLog instance lifecycle; do not dispose or shut down global LogManager
        }
    }
}
