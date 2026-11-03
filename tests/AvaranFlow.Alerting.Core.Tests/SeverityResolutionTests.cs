using System;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Severity;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class SeverityResolutionTests
    {
        private readonly DefaultSeverityResolver _resolver = DefaultSeverityResolver.Instance;

        private static ExceptionContext CreateContext(bool isFatal, Exception? ex = null)
        {
            var exception = ex ?? new InvalidOperationException("Test exception");
            return new ExceptionContext(
                exception: exception,
                message: exception.Message,
                exceptionType: exception.GetType().FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "TestMethod",
                category: "TestCategory",
                machineName: "TestMachine",
                processName: "TestProcess",
                processId: 100,
                threadId: 1,
                appVersion: "1.0.0",
                correlationId: "CORR-1",
                isFatal: isFatal,
                exceptionKey: "TestKey",
                environmentName: "TestEnv",
                source: "TestSource",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public void PrecedenceStep1_IsFatal_AlwaysResolvesToCritical()
        {
            var ex = new Exception("Fatal Error");
            ex.Data["Severity"] = "Info"; // Attempted downgrade in metadata

            var context = CreateContext(isFatal: true, ex: ex);

            // Even if explicit severity is Debug, IsFatal CANNOT be downgraded
            var resolvedWithExplicit = _resolver.Resolve(context, explicitSeverity: AlertSeverity.Debug);
            var resolvedDefault = _resolver.Resolve(context);

            Assert.Equal(AlertSeverity.Critical, resolvedWithExplicit);
            Assert.Equal(AlertSeverity.Critical, resolvedDefault);
        }

        [Fact]
        public void PrecedenceStep2_ExplicitSeverity_TakesPrecedenceWhenNotFatal()
        {
            var ex = new Exception("Non-fatal");
            ex.Data["Severity"] = "Error"; // Metadata says Error

            var context = CreateContext(isFatal: false, ex: ex);

            // Explicit says Warning -> overrides metadata because it's Step 2
            var resolved = _resolver.Resolve(context, explicitSeverity: AlertSeverity.Warning);
            Assert.Equal(AlertSeverity.Warning, resolved);
        }

        [Theory]
        [InlineData("Debug", AlertSeverity.Debug)]
        [InlineData("info", AlertSeverity.Info)]
        [InlineData("WARNING", AlertSeverity.Warning)]
        [InlineData("Error", AlertSeverity.Error)]
        [InlineData("critical", AlertSeverity.Critical)]
        public void PrecedenceStep3_MetadataString_ResolvesCaseInsensitively(string metadataValue, AlertSeverity expected)
        {
            var ex = new Exception("Test");
            ex.Data["Severity"] = metadataValue;

            var context = CreateContext(isFatal: false, ex: ex);
            var resolved = _resolver.Resolve(context);

            Assert.Equal(expected, resolved);
        }

        [Theory]
        [InlineData(0, AlertSeverity.Debug)]
        [InlineData(1, AlertSeverity.Info)]
        [InlineData(2, AlertSeverity.Warning)]
        [InlineData(3, AlertSeverity.Error)]
        [InlineData(4, AlertSeverity.Critical)]
        public void PrecedenceStep3_MetadataNumeric_ResolvesCorrectly(int numericValue, AlertSeverity expected)
        {
            var ex = new Exception("Test");
            ex.Data["Severity"] = numericValue;

            var context = CreateContext(isFatal: false, ex: ex);
            var resolved = _resolver.Resolve(context);

            Assert.Equal(expected, resolved);
        }

        [Theory]
        [InlineData("NotAValidSeverity")]
        [InlineData(999)]
        [InlineData(-1)]
        [InlineData("")]
        [InlineData("   ")]
        public void PrecedenceStep3_InvalidMetadata_FallsThroughToDefaultWithoutThrowing(object invalidMetadata)
        {
            var ex = new Exception("Test");
            ex.Data["Severity"] = invalidMetadata;

            var context = CreateContext(isFatal: false, ex: ex);
            var resolved = _resolver.Resolve(context, defaultSeverity: AlertSeverity.Warning);

            Assert.Equal(AlertSeverity.Warning, resolved);
        }

        [Fact]
        public void PrecedenceStep4_DefaultFallback_AppliesWhenNoOtherSourcePresent()
        {
            var context = CreateContext(isFatal: false);

            var resolvedDefault = _resolver.Resolve(context);
            var resolvedCustomDefault = _resolver.Resolve(context, defaultSeverity: AlertSeverity.Info);

            Assert.Equal(AlertSeverity.Error, resolvedDefault);
            Assert.Equal(AlertSeverity.Info, resolvedCustomDefault);
        }

        [Fact]
        public void NullContext_SafelyReturnsDefault()
        {
            var resolved = _resolver.Resolve(null, defaultSeverity: AlertSeverity.Warning);
            Assert.Equal(AlertSeverity.Warning, resolved);
        }
    }
}
