using System;
using System.IO;
using ExceptionMaster.Core;
using Xunit;

namespace ExceptionMaster.Core.Tests
{
    public class DefaultsAndMetadataTests
    {
        [Fact]
        public void Log_WithNull_DoesNotThrow()
        {
            ExceptionMaster.Log(null);
        }

        [Fact]
        public void Log_BeforeConfigure_UsesSafeDefaultsAndDoesNotThrow()
        {
            var ex = new InvalidOperationException("Test before configure");
            ExceptionMaster.Log(ex);

            var health = ExceptionMaster.GetHealth();
            Assert.True(health.TotalLogged >= 1);
        }

        [Fact]
        public void ConfigureDefaults_RestoresExpectedDefaults()
        {
            ExceptionMaster.ConfigureDefaults();
            var health = ExceptionMaster.GetHealth();
            Assert.False(health.IsShutdown);
        }

        [Fact]
        public void MetadataExtractor_CapturesProcessAndMachineInformation()
        {
            Assert.False(string.IsNullOrWhiteSpace(MetadataExtractor.MachineName));
            Assert.False(string.IsNullOrWhiteSpace(MetadataExtractor.ProcessName));
            Assert.True(MetadataExtractor.ProcessId > 0);
            Assert.True(MetadataExtractor.CurrentThreadId > 0);
            Assert.False(string.IsNullOrWhiteSpace(MetadataExtractor.AppVersion));
        }

        [Fact]
        public void MetadataExtractor_FormatsInnerExceptions_Recursively()
        {
            var inner2 = new ArgumentNullException("param", "Deep exception");
            var inner1 = new InvalidOperationException("Middle exception", inner2);
            var top = new Exception("Top exception", inner1);

            string formatted = MetadataExtractor.FormatInnerExceptions(top);

            Assert.Contains("Middle exception", formatted);
            Assert.Contains("Deep exception", formatted);
            Assert.Contains("ArgumentNullException", formatted);
        }

        [Fact]
        public void MetadataExtractor_FormatsAggregateException_Flattened()
        {
            var agg = new AggregateException("Aggregate error",
                new IOException("File error"),
                new UnauthorizedAccessException("Access denied"));

            string formatted = MetadataExtractor.FormatInnerExceptions(agg);

            Assert.Contains("File error", formatted);
            Assert.Contains("Access denied", formatted);
        }

        [Fact]
        public void AggregationEngine_NormalizesGuidsAndPointers()
        {
            string raw = "Error accessing entity 550e8400-e29b-41d4-a716-446655440000 at address 0x00007FF6B93F1234";
            string normalized = AggregationEngine.NormalizeMessage(raw);

            Assert.DoesNotContain("550e8400-e29b-41d4-a716-446655440000", normalized);
            Assert.DoesNotContain("0x00007FF6B93F1234", normalized);
            Assert.Contains("{GUID}", normalized);
            Assert.Contains("{PTR}", normalized);
        }

        [Fact]
        public void AggregationEngine_TracksOccurrenceCountAndTimestamps()
        {
            var engine = new AggregationEngine(TimeSpan.FromMinutes(5));
            var ex = new InvalidOperationException("Test aggregation");
            string key = engine.GenerateKey(ex);

            DateTime t1 = DateTime.UtcNow;
            bool isNew = engine.RecordOccurrence(key, t1, false, out var s1);
            Assert.True(isNew);
            Assert.Equal(1, s1.Count);
            Assert.Equal(t1, s1.FirstSeenUtc);

            DateTime t2 = t1.AddSeconds(10);
            bool isNew2 = engine.RecordOccurrence(key, t2, true, out var s2);
            Assert.False(isNew2);
            Assert.Equal(2, s2.Count);
            Assert.Equal(t1, s2.FirstSeenUtc);
            Assert.Equal(t2, s2.LastSeenUtc);
            Assert.True(s2.IsFatal);
        }

        [Fact]
        public void AggregationEngine_DifferentExceptions_DoNotCollide()
        {
            var engine = new AggregationEngine(TimeSpan.FromMinutes(5));
            var ex1 = new InvalidOperationException("Exception A");
            var ex2 = new ArgumentException("Exception B");

            string key1 = engine.GenerateKey(ex1);
            string key2 = engine.GenerateKey(ex2);

            Assert.NotEqual(key1, key2);

            engine.RecordOccurrence(key1, DateTime.UtcNow, false, out var s1);
            engine.RecordOccurrence(key2, DateTime.UtcNow, false, out var s2);

            Assert.Equal(1, s1.Count);
            Assert.Equal(1, s2.Count);
        }
    }
}
