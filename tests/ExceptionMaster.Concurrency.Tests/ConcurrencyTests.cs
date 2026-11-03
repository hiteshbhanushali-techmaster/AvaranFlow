using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExceptionMaster.Core;
using Xunit;
using EM = ExceptionMaster.Core.ExceptionMaster;

namespace ExceptionMaster.Concurrency.Tests
{
    public class ConcurrencyTests
    {
        [Fact]
        public async Task AtomicAggregation_ConcurrentIncrements_SumIsExact()
        {
            var engine = new AggregationEngine(TimeSpan.FromMinutes(10));
            string key = "ConcurrentTestKey";

            int threadCount = 20;
            int iterationsPerThread = 100;
            long expectedTotal = threadCount * iterationsPerThread;

            using (var countdown = new CountdownEvent(threadCount))
            {
                var tasks = new Task[threadCount];
                DateTime utcNow = DateTime.UtcNow;

                for (int i = 0; i < threadCount; i++)
                {
                    tasks[i] = Task.Run(() =>
                    {
                        countdown.Signal();
                        countdown.Wait();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            engine.RecordOccurrence(key, utcNow, false, out _);
                        }
                    });
                }

                await Task.WhenAll(tasks);

                engine.RecordOccurrence(key, utcNow, false, out var finalSnapshot);
                Assert.Equal(expectedTotal + 1, finalSnapshot.Count);
            }
        }

        [Fact]
        public async Task ConcurrentLogging_AndReconfiguration_DoesNotThrow()
        {
            EM.ConfigureDefaults();

            bool keepLogging = true;
            var logTasks = new List<Task>();

            for (int i = 0; i < 5; i++)
            {
                logTasks.Add(Task.Run(() =>
                {
                    int counter = 0;
                    while (Volatile.Read(ref keepLogging))
                    {
                        EM.Log(new InvalidOperationException($"Error {counter++}"));
                        Thread.Sleep(1);
                    }
                }));
            }

            // Perform repeated reconfigurations while logging threads are active
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(20);
                EM.Configure(opts =>
                {
                    opts.BufferCapacity = 500 + i * 50;
                    opts.FlushInterval = TimeSpan.FromSeconds(2);
                });
            }

            Volatile.Write(ref keepLogging, false);
            await Task.WhenAll(logTasks);

            EM.Flush(TimeSpan.FromSeconds(2));
            var health = EM.GetHealth();
            Assert.True(health.TotalLogged > 0);
        }
    }
}
