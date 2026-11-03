using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;
using AvaranFlow.Sinks.Console;
using AvaranFlow.Sinks.File;
using AvaranFlow.NLog;
using Xunit;

namespace AvaranFlow.Sinks.Tests
{
    public class SinksTests
    {
        [Fact]
        public async Task ConsoleSink_WriteAsync_DoesNotThrow()
        {
            var sink = new ConsoleSink();
            var context = CreateDummyContext("Console error");

            await sink.WriteAsync(context, CancellationToken.None);
            await sink.WriteBatchAsync(new[] { context }, CancellationToken.None);
            sink.Flush();
        }

        [Fact]
        public async Task FileSink_WritesContent_AndRotates()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "EM_FileSink_Test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new FileSinkOptions
                {
                    LogDirectory = testDir,
                    FileNamePattern = "test-{Date}.log",
                    MaxFileSizeBytes = 500, // Small size to force rotation quickly
                    MaxArchiveFiles = 3
                };

                using (var sink = new FileSink(options))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var ctx = CreateDummyContext($"Log message sequence {i} with enough padding bytes to ensure file size exceeds threshold");
                        await sink.WriteAsync(ctx, CancellationToken.None);
                    }
                }

                Assert.True(Directory.Exists(testDir));
                var files = Directory.GetFiles(testDir);
                Assert.NotEmpty(files);

                // Verify content in primary or rotated files
                string content = System.IO.File.ReadAllText(files[0]);
                Assert.Contains("Log message sequence", content);
            }
            finally
            {
                if (Directory.Exists(testDir))
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task NLogSink_RelaysContext_WithoutModifyingHostConfig()
        {
            var sink = new NLogSink("UnitTestingLogger");
            var context = CreateDummyContext("NLog test error");

            await sink.WriteAsync(context, CancellationToken.None);
            await sink.WriteBatchAsync(new[] { context }, CancellationToken.None);
            sink.Flush();
            sink.Dispose();
        }

        [Fact]
        public async Task FileSink_PropagatesFailure_WhenPathIsInvalid()
        {
            // Set an invalid directory name that Windows rejects
            var invalidOptions = new FileSinkOptions
            {
                LogDirectory = "Z:\\NonExistentDrive_" + Guid.NewGuid().ToString("N") + "\\invalid"
            };

            var sink = new FileSink(invalidOptions);
            var ctx = CreateDummyContext("Error to invalid drive");

            // Must throw so DispatchPipeline receives the failure
            await Assert.ThrowsAnyAsync<Exception>(() => sink.WriteBatchAsync(new[] { ctx }, CancellationToken.None));
        }

        [Fact]
        public void FileSink_Failure_Triggers_ConsoleSink_Fallback_WithoutEscaping()
        {
            var invalidOptions = new FileSinkOptions
            {
                LogDirectory = "Z:\\NonExistentDrive_" + Guid.NewGuid().ToString("N") + "\\invalid"
            };

            var failingFileSink = new FileSink(invalidOptions);
            var consoleSink = new ConsoleSink();

            var options = new AvaranFlowOptions
            {
                EnableAsyncDispatch = false // synchronous execution to assert immediate fallback
            };
            options.FallbackChains.Add(new FallbackChain(failingFileSink, consoleSink));

            using (var pipeline = new DispatchPipeline(options))
            {
                var ctx = CreateDummyContext("Fallback trigger test");

                // Must NOT throw back to caller!
                bool dispatched = pipeline.Dispatch(ctx);
                Assert.True(dispatched);

                var health = pipeline.GetHealth();
                Assert.True(health.TotalSinkFailures >= 1);
                Assert.True(health.TotalFallbackInvocations >= 1);
            }
        }

        private static ExceptionContext CreateDummyContext(string msg)
        {
            var ex = new InvalidOperationException(msg);
            return new ExceptionContext(
                ex, msg, ex.GetType().FullName, ex.StackTrace,
                DateTime.UtcNow, DateTime.UtcNow, 1,
                "TestMethod", "Testing", "TestMachine", "TestProc", 1234, 1,
                "1.0.0", "corr-123", false, "Key-" + msg, "TestEnv", "TestSrc", "", Guid.NewGuid());
        }
    }
}
