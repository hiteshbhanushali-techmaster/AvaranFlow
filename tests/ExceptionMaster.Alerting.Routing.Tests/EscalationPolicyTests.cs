using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Escalation;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class EscalationPolicyTests
    {
        [Fact]
        public void EscalationPolicy_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new EscalationPolicy("", new[] { EscalationStep.Initial() }));
            Assert.Throws<ArgumentNullException>(() => new EscalationPolicy("P1", null!));
            Assert.Throws<ArgumentException>(() => new EscalationPolicy("P1", new EscalationStep[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EscalationPolicy("P1", new[] { EscalationStep.Initial() }, ttl: TimeSpan.Zero));

            // Duplicate level
            var duplicateSteps = new[]
            {
                EscalationStep.Initial(),
                EscalationStep.ForCount(1, 10)
            };
            Assert.Throws<ArgumentException>(() => new EscalationPolicy("P1", duplicateSteps));
        }

        [Fact]
        public void EscalationStep_TriggerEvaluations_WorkAsExpected()
        {
            var countStep = EscalationStep.ForCount(2, 10);
            var timeStep = EscalationStep.ForElapsedTime(2, TimeSpan.FromMinutes(15));
            var sevStep = EscalationStep.ForSeverity(2, AlertSeverity.Critical);

            // Count trigger
            Assert.False(countStep.IsTriggered(9, TimeSpan.Zero, AlertSeverity.Info));
            Assert.True(countStep.IsTriggered(10, TimeSpan.Zero, AlertSeverity.Info));
            Assert.True(countStep.IsTriggered(15, TimeSpan.Zero, AlertSeverity.Info));

            // Time trigger
            Assert.False(timeStep.IsTriggered(100, TimeSpan.FromMinutes(14), AlertSeverity.Info));
            Assert.True(timeStep.IsTriggered(1, TimeSpan.FromMinutes(15), AlertSeverity.Info));
            Assert.True(timeStep.IsTriggered(1, TimeSpan.FromMinutes(20), AlertSeverity.Info));

            // Severity trigger
            Assert.False(sevStep.IsTriggered(100, TimeSpan.FromHours(1), AlertSeverity.Error));
            Assert.True(sevStep.IsTriggered(1, TimeSpan.Zero, AlertSeverity.Critical));
        }

        [Fact]
        public void EscalationPolicyRegistry_RegisterAndRetrieve_BehavesCorrectly()
        {
            var registry = new EscalationPolicyRegistry();
            var policy = new EscalationPolicy("TradingEscalation", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 10, targetDestinations: new[] { "Notifier", "Email" })
            });

            registry.Register(policy);

            Assert.Equal(1, registry.Count);
            Assert.True(registry.Contains("tradingescalation"));
            Assert.True(registry.TryGet("TRADINGESCALATION", out var retrieved));
            Assert.Same(policy, retrieved);

            // Duplicate throws
            Assert.Throws<InvalidOperationException>(() => registry.Register(new EscalationPolicy("tradingescalation", new[] { EscalationStep.Initial() })));

            // Missing returns false
            Assert.False(registry.TryGet("Unknown", out var missing));
            Assert.Null(missing);
        }
    }
}
