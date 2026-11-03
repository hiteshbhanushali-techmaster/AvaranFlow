using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Configuration;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Alerting.Models;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    [Collection("AlertingStaticLifecycle")]
    public class AlertingLifecycleTests : IDisposable
    {
        public AlertingLifecycleTests()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        public void Dispose()
        {
            Alerting.Shutdown();
            Alerting.ResetMetrics();
        }

        private static ExceptionContext CreateTestContext(string key = "LIFECYCLE_TEST_EXC", AlertSeverity? severity = null)
        {
            var ex = new InvalidOperationException("Test exception for alerting lifecycle");
            ex.Data["Account"] = "ACC-9999";
            ex.Data["SecretToken"] = "SUPER_SECRET_VALUE";
            if (severity.HasValue)
            {
                ex.Data["Severity"] = severity.Value.ToString();
            }

            return new ExceptionContext(
                exception: ex,
                message: "Test lifecycle alert",
                exceptionType: ex.GetType().FullName,
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow.AddMinutes(-1),
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "OrderEngine.Submit",
                category: "Trading",
                machineName: "SRV-TEST-01",
                processName: "TradingProc",
                processId: 4420,
                threadId: 12,
                appVersion: "3.0.0",
                correlationId: "CORR-LIFECYCLE-01",
                isFatal: false,
                exceptionKey: key,
                environmentName: "Staging",
                source: "OrderEngine",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public void Initialize_SetsIsInitialized_AndCallingTwiceThrows()
        {
            Assert.False(Alerting.IsInitialized);

            var dest = TestDestination.CreateSuccessful("Dest1");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "Dest1" }));
            });

            Assert.True(Alerting.IsInitialized);

            // Calling Initialize again must throw
            Assert.Throws<InvalidOperationException>(() => Alerting.Initialize(_ => { }));
        }

        [Fact]
        public void Ingest_BeforeInitialize_DropsEventSafelyWithoutThrowing()
        {
            Assert.False(Alerting.IsInitialized);
            var ctx = CreateTestContext();

            // Must not throw
            Alerting.Ingest(ctx);

            var metrics = Alerting.GetMetrics();
            Assert.Equal(1, metrics.EventsDropped);
        }

        [Fact]
        public void Ingest_DeliversToDestinations_AndAppliesSafeProjection()
        {
            var dest = TestDestination.CreateSuccessful("DestSafe");

            Alerting.Initialize(b =>
            {
                b.ConfigureOptions(opt =>
                {
                    opt.SafeMetadataKeys = new[] { "Account" }; // Only Account is permitted, SecretToken is redacted
                    opt.IncludeSafeStackTrace = false;
                });
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestSafe" }));
            });

            var ctx = CreateTestContext();
            Alerting.Ingest(ctx);
            Alerting.Flush(TimeSpan.FromSeconds(2));

            Assert.Equal(1, dest.Invocations);
            Assert.True(dest.DeliveredEnvelopes.TryPeek(out var envelope));

            Assert.NotNull(envelope);
            Assert.Equal(ctx.ExceptionKey, envelope!.ExceptionKey);
            Assert.Equal("Trading", envelope.Category);

            // Verify safe metadata filtering
            Assert.True(envelope.SafeMetadata.ContainsKey("Account"));
            Assert.Equal("ACC-9999", envelope.SafeMetadata["Account"]);
            Assert.False(envelope.SafeMetadata.ContainsKey("SecretToken"), "Unwhitelisted metadata key must be redacted.");
            Assert.Null(envelope.SafeStackTrace);
        }

        [Fact]
        public void Shutdown_IsIdempotent_StopsAdmissions_AndDisposesResources()
        {
            var dest = TestDestination.CreateSuccessful("DestShutdown");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestShutdown" }));
            });

            Assert.True(Alerting.IsInitialized);

            // Shutdown 1
            Alerting.Shutdown(TimeSpan.FromSeconds(2));
            Assert.False(Alerting.IsInitialized);

            // Shutdown 2 (idempotent, must not throw)
            Alerting.Shutdown(TimeSpan.FromSeconds(2));
            Assert.False(Alerting.IsInitialized);

            // Ingest after shutdown drops event
            Alerting.Ingest(CreateTestContext());
            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.EventsDropped >= 1);
        }

        [Fact]
        public void Reconfigure_BeforeInitialize_ThrowsInvalidOperationException()
        {
            Assert.False(Alerting.IsInitialized);
            Assert.Throws<InvalidOperationException>(() => Alerting.Reconfigure(_ => { }));
        }

        [Fact]
        public void Reconfigure_UpdatesRulesAndDestinations_AndIncrementsMetric()
        {
            var destOld = TestDestination.CreateSuccessful("DestOld");
            var destNew = TestDestination.CreateSuccessful("DestNew");

            Alerting.Initialize(b =>
            {
                b.RegisterDestination(destOld);
                b.AddRule(new AlertRule("RuleOld", "RuleOld", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestOld" }));
            });

            var ctx1 = CreateTestContext("KEY_GEN1");
            Alerting.Ingest(ctx1);
            Alerting.Flush(TimeSpan.FromSeconds(2));
            Assert.Equal(1, destOld.Invocations);

            // Reconfigure to use DestNew
            Alerting.Reconfigure(b =>
            {
                b.RegisterDestination(destNew);
                b.AddRule(new AlertRule("RuleNew", "RuleNew", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestNew" }));
            });

            var ctx2 = CreateTestContext("KEY_GEN2");
            Alerting.Ingest(ctx2);
            Alerting.Flush(TimeSpan.FromSeconds(2));

            Assert.Equal(1, destNew.Invocations);

            var metrics = Alerting.GetMetrics();
            Assert.True(metrics.ReconfigurationCount >= 1);
        }

        [Fact]
        public void Metrics_TracksCountersAccurately()
        {
            var dest = TestDestination.CreateSuccessful("DestMetrics");
            Alerting.Initialize(b =>
            {
                b.RegisterDestination(dest);
                b.AddRule(new AlertRule("Rule1", "Rule1", RuleCondition.Category(PredicateOperator.Equals, "Trading"), targetDestinations: new[] { "DestMetrics" }));
            });

            for (int i = 0; i < 5; i++)
            {
                Alerting.Ingest(CreateTestContext($"KEY_{i}"));
            }

            Alerting.Flush(TimeSpan.FromSeconds(3));

            var metrics = Alerting.GetMetrics();
            Assert.Equal(5, metrics.EventsReceived);
            Assert.Equal(5, metrics.EventsAdmitted);
            Assert.Equal(0, metrics.EventsDropped);
            Assert.Equal(5, metrics.RulesMatched);
            Assert.Equal(5, metrics.DestinationSuccesses);
        }
    }
}
