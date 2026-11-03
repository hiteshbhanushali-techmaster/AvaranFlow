using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;

namespace AvaranFlow.Sinks.Console
{
    public sealed class ConsoleSinkOptions
    {
        public bool UseColors { get; set; } = true;
        public bool IncludeStackTrace { get; set; } = true;
        public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";
    }

    /// <summary>
    /// Thread-safe console sink that safely catches console disconnects and headless environment errors.
    /// </summary>
    public sealed class ConsoleSink : IExceptionSink
    {
        private static readonly object ConsoleLock = new object();
        private readonly ConsoleSinkOptions _options;

        public string Name => "ConsoleSink";

        public ConsoleSink(ConsoleSinkOptions options = null)
        {
            _options = options ?? new ConsoleSinkOptions();
        }

        public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
        {
            if (context == null || cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            WriteContextToConsole(context);
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
                WriteContextToConsole(context);
            }

            return Task.CompletedTask;
        }

        public void Flush()
        {
            try
            {
                System.Console.Out?.Flush();
            }
            catch
            {
                // Safe swallow
            }
        }

        private void WriteContextToConsole(ExceptionContext context)
        {
            try
            {
                lock (ConsoleLock)
                {
                    var prevColor = System.Console.ForegroundColor;
                    try
                    {
                        if (_options.UseColors)
                        {
                            System.Console.ForegroundColor = context.IsFatal ? ConsoleColor.DarkRed : ConsoleColor.Red;
                        }

                        string header = $"[{context.LastSeenUtc.ToString(_options.TimestampFormat)} UTC] " +
                                        $"[{context.Category}] [T:{context.ThreadId}] " +
                                        $"{context.ExceptionType}: {context.Message} (Count: {context.Count})";
                        System.Console.WriteLine(header);

                        if (_options.IncludeStackTrace && !string.IsNullOrEmpty(context.StackTrace))
                        {
                            if (_options.UseColors) System.Console.ForegroundColor = ConsoleColor.DarkGray;
                            System.Console.WriteLine(context.StackTrace);
                        }

                        if (!string.IsNullOrEmpty(context.InnerExceptionDetails))
                        {
                            if (_options.UseColors) System.Console.ForegroundColor = ConsoleColor.Yellow;
                            System.Console.WriteLine(context.InnerExceptionDetails);
                        }
                    }
                    finally
                    {
                        if (_options.UseColors)
                        {
                            System.Console.ForegroundColor = prevColor;
                        }
                    }
                }
            }
            catch
            {
                // Safe swallow: do not let a missing console or I/O failure propagate
            }
        }

        public void Dispose()
        {
            Flush();
        }
    }
}
