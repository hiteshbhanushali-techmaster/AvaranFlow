using System;
using System.Collections.Generic;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;
using Xunit;

namespace AvaranFlow.Alerting.Routing.Tests
{
    public class SuppressionPolicyTests
    {
        [Fact]
        public void SuppressionPolicy_Constructor_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new SuppressionPolicy("", TimeSpan.FromMinutes(1)));
            Assert.Throws<ArgumentException>(() => new SuppressionPolicy("   ", TimeSpan.FromMinutes(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuppressionPolicy("P1", TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuppressionPolicy("P1", TimeSpan.FromSeconds(-5)));
            Assert.Throws<ArgumentException>(() => new SuppressionPolicy("P1", TimeSpan.FromMinutes(1), SuppressionDimension.Custom, customDimensionKey: ""));
        }

        [Fact]
        public void SuppressionPolicy_ExtractsDimensionValues_Correctly()
        {
            var metadata = new Dictionary<string, string>
            {
                { "CustomRegion", "US-East" }
            };

            var context = new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "TimeoutException",
                exceptionKey: "Key_123",
                severity: AlertSeverity.Error,
                category: "Trading",
                source: "OrderService",
                environmentName: "Prod",
                machineName: "SRV-01",
                processName: "Proc",
                processId: 100,
                threadId: 1,
                appVersion: "1.0",
                isFatal: false,
                count: 5,
                firstSeenUtc: DateTime.UtcNow.AddMinutes(-5),
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "CORR-999",
                caller: "SubmitOrder",
                message: "Connection timed out",
                metadata: metadata);

            var keyPolicy = new SuppressionPolicy("P1", TimeSpan.FromMinutes(1), SuppressionDimension.ExceptionKey);
            var catPolicy = new SuppressionPolicy("P2", TimeSpan.FromMinutes(1), SuppressionDimension.Category);
            var srcPolicy = new SuppressionPolicy("P3", TimeSpan.FromMinutes(1), SuppressionDimension.Source);
            var typePolicy = new SuppressionPolicy("P4", TimeSpan.FromMinutes(1), SuppressionDimension.ExceptionType);
            var corrPolicy = new SuppressionPolicy("P5", TimeSpan.FromMinutes(1), SuppressionDimension.CorrelationId);
            var msgPolicy = new SuppressionPolicy("P6", TimeSpan.FromMinutes(1), SuppressionDimension.Message);
            var rulePolicy = new SuppressionPolicy("P7", TimeSpan.FromMinutes(1), SuppressionDimension.Rule);
            var customPolicy = new SuppressionPolicy("P8", TimeSpan.FromMinutes(1), SuppressionDimension.Custom, customDimensionKey: "CustomRegion");
            var missingCustomPolicy = new SuppressionPolicy("P9", TimeSpan.FromMinutes(1), SuppressionDimension.Custom, customDimensionKey: "MissingKey");

            Assert.Equal("Key_123", keyPolicy.ExtractDimensionValue(context));
            Assert.Equal("Trading", catPolicy.ExtractDimensionValue(context));
            Assert.Equal("OrderService", srcPolicy.ExtractDimensionValue(context));
            Assert.Equal("TimeoutException", typePolicy.ExtractDimensionValue(context));
            Assert.Equal("CORR-999", corrPolicy.ExtractDimensionValue(context));
            Assert.Equal("Connection timed out", msgPolicy.ExtractDimensionValue(context));
            Assert.Equal(string.Empty, rulePolicy.ExtractDimensionValue(context));
            Assert.Equal("US-East", customPolicy.ExtractDimensionValue(context));
            Assert.Equal(string.Empty, missingCustomPolicy.ExtractDimensionValue(context));
        }

        [Fact]
        public void SuppressionPolicyRegistry_RegisterAndRetrieve_BehavesCorrectly()
        {
            var registry = new SuppressionPolicyRegistry();
            var policy = new SuppressionPolicy("TradingCooldown", TimeSpan.FromMinutes(5));

            registry.Register(policy);

            Assert.Equal(1, registry.Count);
            Assert.True(registry.Contains("tradingcooldown"));
            Assert.True(registry.TryGet("TRADINGCOOLDOWN", out var retrieved));
            Assert.Same(policy, retrieved);

            // Duplicate registration throws
            Assert.Throws<InvalidOperationException>(() => registry.Register(new SuppressionPolicy("tradingcooldown", TimeSpan.FromMinutes(1))));

            // Missing returns false
            Assert.False(registry.TryGet("NonExistent", out var missing));
            Assert.Null(missing);
        }
    }
}
