using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Models;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class ImmutableDtoBoundaryTests
    {
        [Fact]
        public void AlertEnvelope_HasNoPublicSetters()
        {
            var properties = typeof(AlertEnvelope).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                Assert.Null(prop.GetSetMethod());
            }
        }

        [Fact]
        public void AlertEnvelope_ContainsNoRawExceptionOrDiagnosticContextProperties()
        {
            var properties = typeof(AlertEnvelope).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyTypes = properties.Select(p => p.PropertyType).ToList();

            Assert.DoesNotContain(typeof(Exception), propertyTypes);
            Assert.DoesNotContain(typeof(System.Collections.IDictionary), propertyTypes);
            Assert.DoesNotContain(propertyTypes, t => t.FullName != null && t.FullName.Contains("ExceptionContext"));
        }

        [Fact]
        public void AlertEnvelope_DefensivelyCopiesSafeMetadata()
        {
            var inputMetadata = new Dictionary<string, string>
            {
                { "OrderId", "ORD-123" },
                { "Symbol", "MSFT" }
            };

            var envelope = new AlertEnvelope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Key123",
                "TimeoutException",
                "Gateway timeout",
                AlertSeverity.Error,
                "Trading",
                "FixGateway",
                "ORD-123",
                "Production",
                "PROD-SRV-01",
                "TradingHost",
                1234,
                false,
                5,
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow,
                DateTime.UtcNow,
                inputMetadata);

            // Mutate input dictionary
            inputMetadata["OrderId"] = "TAMPERED";
            inputMetadata["NewKey"] = "ILLEGAL";

            // Verify envelope was not mutated
            Assert.Equal("ORD-123", envelope.SafeMetadata["OrderId"]);
            Assert.False(envelope.SafeMetadata.ContainsKey("NewKey"));
        }

        [Fact]
        public void AlertEnvelope_SafeMetadataIsReadOnly()
        {
            var inputMetadata = new Dictionary<string, string> { { "Key", "Val" } };
            var envelope = new AlertEnvelope(
                Guid.NewGuid(), Guid.NewGuid(), "K", "T", "M", AlertSeverity.Info,
                "Cat", "Src", "Corr", "Prod", "Mach", "Proc", 1, false, 1,
                DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, inputMetadata);

            var dict = envelope.SafeMetadata as IDictionary<string, string>;
            Assert.NotNull(dict);
            Assert.Throws<NotSupportedException>(() => dict["Key"] = "NewVal");
        }

        [Fact]
        public void AlertEnvelope_NullArguments_NormalizedSafely()
        {
            var envelope = new AlertEnvelope(
                Guid.Empty,
                Guid.Empty,
                null!,
                null!,
                null!,
                AlertSeverity.Warning,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                0,
                false,
                0,
                default,
                default,
                default,
                null);

            Assert.NotEqual(Guid.Empty, envelope.AlertId);
            Assert.Equal(Guid.Empty, envelope.OccurrenceId);
            Assert.Equal(string.Empty, envelope.ExceptionKey);
            Assert.Equal("Unknown", envelope.ExceptionType);
            Assert.Equal(string.Empty, envelope.SafeMessage);
            Assert.Equal("General", envelope.Category);
            Assert.Equal("Production", envelope.EnvironmentName);
            Assert.Equal(1, envelope.Count);
            Assert.NotNull(envelope.SafeMetadata);
            Assert.Empty(envelope.SafeMetadata);
        }

        [Fact]
        public void AlertActionToken_RoundTripSerialization_Succeeds()
        {
            var original = new AlertActionToken(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "BrokerTimeoutException:SendOrder",
                "ORD-987654",
                DateTime.UtcNow);

            string payload = original.ToPayload();
            Assert.False(string.IsNullOrWhiteSpace(payload));

            bool parsed = AlertActionToken.TryParse(payload, out var reconstructed);
            Assert.True(parsed);
            Assert.NotNull(reconstructed);
            Assert.Equal(original, reconstructed);
            Assert.Equal(original.OccurrenceId, reconstructed!.OccurrenceId);
            Assert.Equal(original.ExceptionKey, reconstructed.ExceptionKey);
            Assert.Equal(original.CorrelationId, reconstructed.CorrelationId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid|payload")]
        [InlineData("not-a-guid|not-a-guid|key|corr|12345")]
        [InlineData("00000000-0000-0000-0000-000000000001|00000000-0000-0000-0000-000000000002|key|corr|not-a-number")]
        public void AlertActionToken_TryParse_MalformedInput_ReturnsFalseSafely(string? malformed)
        {
            bool result = AlertActionToken.TryParse(malformed, out var token);
            Assert.False(result);
            Assert.Null(token);
        }

        [Fact]
        public void AlertDeliveryResult_FactoryMethods_ProduceExpectedProperties()
        {
            var success = AlertDeliveryResult.Succeeded("Notifier", TimeSpan.FromMilliseconds(42.5), "Delivered");
            Assert.True(success.Success);
            Assert.Equal("Notifier", success.DestinationName);
            Assert.Equal(TimeSpan.FromMilliseconds(42.5), success.Elapsed);
            Assert.Null(success.FailureException);
            Assert.Equal("Delivered", success.DiagnosticMessage);

            var ex = new InvalidOperationException("Connection refused");
            var failure = AlertDeliveryResult.Failed("Email", TimeSpan.FromMilliseconds(105), ex);
            Assert.False(failure.Success);
            Assert.Equal("Email", failure.DestinationName);
            Assert.Equal(TimeSpan.FromMilliseconds(105), failure.Elapsed);
            Assert.Same(ex, failure.FailureException);
            Assert.Equal("Connection refused", failure.DiagnosticMessage);
        }

        [Fact]
        public void AlertingMetrics_IsImmutable_AndReflectsAllCounters()
        {
            var now = DateTime.UtcNow;
            var metrics = new AlertingMetrics(
                now, 100, 95, 5, 200, 50, 10, 5, 45, 40, 5, 5, 4, 1, 2, 0, 0, 3, 0);

            Assert.Equal(now, metrics.TimestampUtc);
            Assert.Equal(100, metrics.EventsReceived);
            Assert.Equal(95, metrics.EventsAdmitted);
            Assert.Equal(5, metrics.EventsDropped);
            Assert.Equal(200, metrics.RulesEvaluated);
            Assert.Equal(50, metrics.RulesMatched);
            Assert.Equal(10, metrics.AlertsSuppressed);
            Assert.Equal(5, metrics.AlertsEscalated);
            Assert.Equal(45, metrics.DestinationAttempts);
            Assert.Equal(40, metrics.DestinationSuccesses);
            Assert.Equal(5, metrics.DestinationFailures);
            Assert.Equal(5, metrics.FallbackAttempts);
            Assert.Equal(4, metrics.FallbackSuccesses);
            Assert.Equal(1, metrics.FallbackFailures);
            Assert.Equal(2, metrics.ReconfigurationCount);
            Assert.Equal(3, metrics.QueueDepth);
        }
    }
}
