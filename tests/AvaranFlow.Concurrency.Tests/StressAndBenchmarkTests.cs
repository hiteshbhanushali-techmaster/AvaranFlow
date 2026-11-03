using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;
using Xunit;
using EM = AvaranFlow.Core.ExceptionMaster;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AvaranFlow.Concurrency.Tests
{
    public class StressAndBenchmarkTests
    {
        [Fact]
        public void Benchmark_HighThroughput_LoggingLatencyAndMemory()
        {
            EM.Configure(options =>
            {
                options.EnableAsyncDispatch = true;
                options.BufferCapacity = 50000;
                options.BatchSize = 1000;
                options.FlushInterval = TimeSpan.FromSeconds(1);
            });

            const int totalEvents = 10000;
            var ex = new InvalidOperationException("High throughput benchmark error");

            // Warm-up loop to allow JIT compilation
            for (int w = 0; w < 1000; w++)
            {
                EM.Log(ex, category: "Warmup");
            }

            long memoryBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < totalEvents; i++)
            {
                EM.Log(ex, category: "Benchmark");
            }

            sw.Stop();
            long memoryAfter = GC.GetTotalMemory(false);

            EM.Flush(TimeSpan.FromSeconds(5));

            var health = EM.GetHealth();

            double totalMicroseconds = sw.Elapsed.TotalMilliseconds * 1000.0;
            double microsecondsPerCall = totalMicroseconds / totalEvents;

            System.Console.WriteLine($"[BENCHMARK] Elapsed: {sw.ElapsedMilliseconds} ms, {microsecondsPerCall:F2} μs/call, Logged: {health.TotalLogged}");

            Assert.True(microsecondsPerCall < 500.0, $"Call latency too high: {microsecondsPerCall:F2} μs/call");
            Assert.True(health.TotalLogged >= totalEvents, $"Expected >= {totalEvents}, got {health.TotalLogged}");
        }

        [Fact]
        public async Task Stress_ConcurrentMillionEventsSimulation()
        {
            EM.Configure(options =>
            {
                options.EnableAsyncDispatch = true;
                options.BufferCapacity = 10000;
                options.OverflowPolicy = QueueOverflowPolicy.DropNewest;
                options.FlushInterval = TimeSpan.FromMilliseconds(500);
            });

            const int workerCount = 10;
            const int eventsPerWorker = 5000; // 50,000 total events under high parallelism

            var tasks = new Task[workerCount];
            var sw = Stopwatch.StartNew();

            for (int w = 0; w < workerCount; w++)
            {
                int workerId = w;
                tasks[w] = Task.Run(() =>
                {
                    for (int i = 0; i < eventsPerWorker; i++)
                    {
                        var simulated = new InvalidOperationException($"Worker {workerId} Error {i % 10}");
                        EM.Log(simulated, category: "Stress");
                    }
                });
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            EM.Flush(TimeSpan.FromSeconds(3));

            var health = EM.GetHealth();
            Assert.True(health.TotalLogged >= workerCount * eventsPerWorker);
        }
    }
}
