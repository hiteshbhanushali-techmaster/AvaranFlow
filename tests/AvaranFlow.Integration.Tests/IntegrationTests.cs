using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Core;
using AvaranFlow.Sinks.Console;
using AvaranFlow.Sinks.Database;
using AvaranFlow.Sinks.File;
using Xunit;
using EM = AvaranFlow.Core.ExceptionMaster;

namespace AvaranFlow.Integration.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void EndToEnd_FanOut_ToFileAndConsole()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "EM_Integration_FanOut_" + Guid.NewGuid().ToString("N"));
            try
            {
                var fileSink = new FileSink(new FileSinkOptions
                {
                    LogDirectory = testDir,
                    FileNamePattern = "fanout-{Date}.log"
                });

                var consoleSink = new ConsoleSink();

                EM.Configure(opts =>
                {
                    opts.EnableAsyncDispatch = true;
                    opts.BatchSize = 10;
                    opts.FlushInterval = TimeSpan.FromSeconds(1);

                    opts.Sinks.Add(fileSink);
                    opts.Sinks.Add(consoleSink);
                });

                for (int i = 0; i < 5; i++)
                {
                    EM.Log(new InvalidOperationException($"FanOut test exception {i}"), category: "Integration");
                }

                EM.Flush(TimeSpan.FromSeconds(2));

                Assert.True(Directory.Exists(testDir));
                var files = Directory.GetFiles(testDir);
                Assert.NotEmpty(files);
                string text = ReadAllTextShared(files[0]);
                Assert.Contains("FanOut test exception 0", text);
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
        public void EndToEnd_DatabaseOffline_FallsBackToFile()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "EM_Integration_Fallback_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Connect to a non-existent port to simulate unreachable DB
                var offlineSqlSink = new SqlServerDatabaseSink(new DatabaseSinkOptions
                {
                    ConnectionString = "Server=127.0.0.1,65530;Database=NonExistent;Connection Timeout=1;",
                    CommandTimeoutSeconds = 1,
                    MaxRetries = 1,
                    AutoCreateSchema = false
                });

                var fileSink = new FileSink(new FileSinkOptions
                {
                    LogDirectory = testDir,
                    FileNamePattern = "fallback-{Date}.log"
                });

                EM.Configure(opts =>
                {
                    opts.EnableAsyncDispatch = false; // direct dispatch to trigger fallback immediately
                    var chain = new FallbackChain(offlineSqlSink, fileSink);
                    opts.FallbackChains.Add(chain);
                });

                EM.Log(new InvalidOperationException("DB offline fallback test"));

                Assert.True(Directory.Exists(testDir));
                var files = Directory.GetFiles(testDir);
                Assert.NotEmpty(files);
                string text = ReadAllTextShared(files[0]);
                Assert.Contains("DB offline fallback test", text);

                var health = EM.GetHealth();
                Assert.True(health.TotalFallbackInvocations >= 1);
            }
            finally
            {
                if (Directory.Exists(testDir))
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            }
        }

        private static string ReadAllTextShared(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
