using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Core.Tests
{
    public class BufferOverflowAndLifecycleTests
    {
        private class TestSink : IExceptionSink
        {
            public List<ExceptionContext> Received = new List<ExceptionContext>();
            public string Name => "TestSink";
            public void Dispose() { }
            public void Flush() { }

            public Task WriteAsync(ExceptionContext context, CancellationToken cancellationToken)
            {
                lock (Received) { Received.Add(context); }
                return Task.CompletedTask;
            }

            public Task WriteBatchAsync(IReadOnlyList<ExceptionContext> contexts, CancellationToken cancellationToken)
            {
                lock (Received) { Received.AddRange(contexts); }
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void DispatchPipeline_DropNewest_DropsWhenSaturated()
        {
            var options = new AvaranFlowOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 5,
                OverflowPolicy = QueueOverflowPolicy.DropNewest,
                FlushInterval = TimeSpan.FromHours(1) // Keep worker idle
            };

            using (var pipeline = new DispatchPipeline(options))
            {
                var dummyContext = new ExceptionContext(
                    new Exception("test"), "test", "Exception", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "k", "", "", "", Guid.NewGuid());

                for (int i = 0; i < 10; i++)
                {
                    pipeline.Dispatch(dummyContext);
                }

                var health = pipeline.GetHealth();
                Assert.True(health.TotalDropped >= 5);
            }
        }

        [Fact]
        public void DispatchPipeline_FatalEvent_BypassesDrop()
        {
            var options = new AvaranFlowOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 3,
                OverflowPolicy = QueueOverflowPolicy.DropNewest,
                FlushInterval = TimeSpan.FromHours(1)
            };

            using (var pipeline = new DispatchPipeline(options))
            {
                var normalContext = new ExceptionContext(
                    new Exception("normal"), "normal", "Exception", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "k", "", "", "", Guid.NewGuid());

                var fatalContext = new ExceptionContext(
                    new Exception("fatal"), "fatal", "Exception", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", true, "k_fatal", "", "", "", Guid.NewGuid());

                for (int i = 0; i < 5; i++)
                {
                    pipeline.Dispatch(normalContext);
                }

                // Saturated! Now dispatch fatal
                bool fatalAccepted = pipeline.Dispatch(fatalContext);
                Assert.True(fatalAccepted);
            }
        }

        [Fact]
        public void Flush_DrainsPendingItems()
        {
            var sink = new TestSink();
            var options = new AvaranFlowOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 100,
                BatchSize = 10,
                FlushInterval = TimeSpan.FromHours(1)
            };
            options.Sinks.Add(sink);

            using (var pipeline = new DispatchPipeline(options))
            {
                var context = new ExceptionContext(
                    new Exception("test"), "test", "Exception", "",
                    DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "k", "", "", "", Guid.NewGuid());

                for (int i = 0; i < 15; i++)
                {
                    pipeline.Dispatch(context);
                }

                pipeline.Flush(TimeSpan.FromSeconds(2));

                lock (sink.Received)
                {
                    Assert.Equal(15, sink.Received.Count);
                }
            }
        }

        [Fact]
        public void Shutdown_DrainsAndPreventsNewDispatches()
        {
            var sink = new TestSink();
            var options = new AvaranFlowOptions
            {
                EnableAsyncDispatch = true,
                BufferCapacity = 50,
                ShutdownTimeout = TimeSpan.FromSeconds(2)
            };
            options.Sinks.Add(sink);

            var pipeline = new DispatchPipeline(options);
            var context = new ExceptionContext(
                new Exception("test"), "test", "Exception", "",
                DateTime.UtcNow, DateTime.UtcNow, 1, "", "", "", "", 1, 1, "", "", false, "k", "", "", "", Guid.NewGuid());

            pipeline.Dispatch(context);
            pipeline.Shutdown(TimeSpan.FromSeconds(2));

            Assert.True(pipeline.IsShutdown);
            bool afterShutdown = pipeline.Dispatch(context);
            Assert.False(afterShutdown);
        }
    }
}
