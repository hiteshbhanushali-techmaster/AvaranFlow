using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;

namespace ExceptionMaster.Sample.CustomSinks
{
    /// <summary>
    /// Sample custom sink storing telemetry in-memory for live dashboards or external forwarding.
    /// </summary>
    public class InMemoryTelemetrySink : IExceptionSink
    {
        private readonly ConcurrentBag<ExceptionContext> _records = new ConcurrentBag<ExceptionContext>();

        public string Name => "InMemoryTelemetrySink";
        public int Count => _records.Count;

        public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
        {
            if (context != null) _records.Add(context);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
        {
            if (contexts != null)
            {
                foreach (var ctx in contexts) _records.Add(ctx);
            }
            return Task.CompletedTask;
        }

        public void Flush() { }
        public void Dispose() { }
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            System.Console.WriteLine("=== ExceptionMaster Custom Sink Sample ===");

            var customSink = new InMemoryTelemetrySink();

            AvaranFlow.Core.ExceptionMaster.Configure(options =>
            {
                options.EnableAsyncDispatch = false;
                options.Sinks.Add(customSink);
            });

            System.Console.WriteLine("Logging exceptions to custom sink...");
            AvaranFlow.Core.ExceptionMaster.Log(new InvalidOperationException("Custom sink event 1"), category: "CustomMetrics");
            AvaranFlow.Core.ExceptionMaster.Log(new ArgumentNullException("parameter", "Custom sink event 2"), category: "CustomMetrics");

            System.Console.WriteLine($"Total items collected in custom sink: {customSink.Count}");
            System.Console.WriteLine("Custom sink sample completed successfully!");
        }
    }
}
