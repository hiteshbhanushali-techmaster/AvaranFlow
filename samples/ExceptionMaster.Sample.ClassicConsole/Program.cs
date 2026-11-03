using System;
using System.IO;
using ExceptionMaster.Core;
using ExceptionMaster.Sinks.Console;
using ExceptionMaster.Sinks.File;

namespace ExceptionMaster.Sample.ClassicConsole
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            System.Console.WriteLine("=== ExceptionMaster Classic Console Sample ===");

            // 1. Zero-configuration logging (safe defaults, console output)
            System.Console.WriteLine("\n[1] Demonstrating Zero-Config Log...");
            try
            {
                throw new InvalidOperationException("First error using zero-configuration defaults");
            }
            catch (Exception ex)
            {
                ExceptionMaster.Core.ExceptionMaster.Log(ex, category: "Startup");
            }

            // 2. Explicit configuration with File and Console sinks
            System.Console.WriteLine("\n[2] Demonstrating Explicit Configuration with File Sink...");
            string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample_logs");

            ExceptionMaster.Core.ExceptionMaster.Configure(options =>
            {
                options.EnableAsyncDispatch = true;
                options.FlushInterval = TimeSpan.FromSeconds(1);

                options.Sinks.Add(new ConsoleSink());
                options.Sinks.Add(new FileSink(new FileSinkOptions
                {
                    LogDirectory = logFolder,
                    FileNamePattern = "console-sample-{Date}.log"
                }));
            });

            // 3. Repeated exception aggregation demonstration
            System.Console.WriteLine("\n[3] Logging 10 repeated exceptions (in-memory aggregation test)...");
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    SimulateDatabaseQueryError();
                }
                catch (Exception ex)
                {
                    ExceptionMaster.Core.ExceptionMaster.Log(ex, category: "DatabaseQuery", correlationId: "req-99901");
                }
            }

            // 4. Fire-and-forget event subscription
            System.Console.WriteLine("\n[4] Subscribing to ExceptionRaised event...");
            ExceptionMaster.Core.ExceptionMaster.ExceptionRaised += context =>
            {
                System.Console.WriteLine($"[EVENT SUBSCRIBER NOTIFIED]: {context.ExceptionType}: {context.Message} (Count: {context.Count})");
            };

            ExceptionMaster.Core.ExceptionMaster.Log(new ArgumentException("Subscribed event test exception"));

            // 5. Bounded Flush & Shutdown
            System.Console.WriteLine("\n[5] Flushing and shutting down ExceptionMaster pipeline...");
            ExceptionMaster.Core.ExceptionMaster.Flush(TimeSpan.FromSeconds(2));
            ExceptionMaster.Core.ExceptionMaster.Shutdown(TimeSpan.FromSeconds(2));

            var health = ExceptionMaster.Core.ExceptionMaster.GetHealth();
            System.Console.WriteLine($"\nFinal Health Report: {health}");
            System.Console.WriteLine("Sample finished successfully!");
        }

        private static void SimulateDatabaseQueryError()
        {
            throw new TimeoutException("Database query exceeded timeout limit of 30 seconds");
        }
    }
}
