using System;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using ExceptionMaster.Core;
using ExceptionMaster.Alerting;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Models;
using ExceptionMaster.Alerting.Rules;
using ExceptionMaster.Alerting.Suppression;

namespace AvaranFlow.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--benchmark")
            {
                BenchmarkRunner.Run<AvaranFlowBenchmarkSuite>();
            }
            else
            {
                Console.WriteLine("================================================================");
                Console.WriteLine("       AVARANFLOW BENCHMARK SUITE - INGESTION & PIPELINE       ");
                Console.WriteLine("================================================================");
                Console.WriteLine($"Runtime: .NET {Environment.Version}");
                Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
                Console.WriteLine($"64-Bit Process: {Environment.Is64BitProcess}");
                Console.WriteLine();

                var suite = new AvaranFlowBenchmarkSuite();
                suite.GlobalSetup();

                Console.WriteLine("--- Benchmark 1: Ingestion Latency (10,000 Repeated Logs) ---");
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++)
                {
                    suite.Log_Repeated_Exception_CAS();
                }
                sw.Stop();
                Console.WriteLine($"[RESULT] 10,000 CAS Logs: {sw.ElapsedMilliseconds} ms ({(double)sw.Elapsed.TotalMicroseconds / 10000:F2} us/op)");

                Console.WriteLine();
                Console.WriteLine("--- Benchmark 2: Alerting Rule Evaluation (10,000 Evaluations) ---");
                sw.Restart();
                for (int i = 0; i < 10000; i++)
                {
                    suite.Alerting_Rule_Evaluation();
                }
                sw.Stop();
                Console.WriteLine($"[RESULT] 10,000 Rule Evaluations: {sw.ElapsedMilliseconds} ms ({(double)sw.Elapsed.TotalMicroseconds / 10000:F2} us/op)");

                Console.WriteLine();
                Console.WriteLine("Run with '--benchmark' to execute full BenchmarkDotNet profiling.");
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 5)]
    public class AvaranFlowBenchmarkSuite
    {
        private Exception _sampleException;
        private AlertEnvelope _sampleEnvelope;
        private AlertRule _sampleRule;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _sampleException = new InvalidOperationException("Simulated trading engine execution breach.");

            // Initialize ExceptionMaster Core
            var options = ExceptionMasterOptions.CreateDefault();
            options.EnableAsyncDispatch = true;
            options.BufferCapacity = 10000;
            ExceptionMaster.Core.ExceptionMaster.Configure(options);

            // Setup sample alert envelope
            _sampleEnvelope = new AlertEnvelope(
                alertId: Guid.NewGuid(),
                occurrenceId: 1,
                exceptionKey: "OrderBreach#01",
                exceptionType: typeof(InvalidOperationException).FullName,
                source: "ExecutionEngine",
                severity: AlertSeverity.Error,
                isFatal: false,
                count: 1,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                generatedUtc: DateTime.UtcNow,
                safeMessage: "Order price breached volatility band.",
                sanitizedStackTrace: "at TradeProcessor.Execute() in C:\\src\\TradeProcessor.cs:line 42",
                customProperties: null);

            _sampleRule = new AlertRule(
                ruleId: "order-breach-rule",
                name: "Order Breach Alert Rule",
                condition: RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error),
                order: 10,
                targetDestinations: new[] { "NullDestination" });
        }

        [Benchmark]
        public void Log_Repeated_Exception_CAS()
        {
            ExceptionMaster.Core.ExceptionMaster.Log(_sampleException);
        }

        [Benchmark]
        public bool Alerting_Rule_Evaluation()
        {
            return _sampleRule.Condition.Evaluate(_sampleEnvelope);
        }
    }
}
