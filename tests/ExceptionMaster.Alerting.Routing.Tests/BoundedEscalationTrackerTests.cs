using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Escalation;
using ExceptionMaster.Alerting.Rules;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class BoundedEscalationTrackerTests
    {
        private static RuleEvaluationContext CreateContext(string key, AlertSeverity severity = AlertSeverity.Error)
        {
            return new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "TestException",
                exceptionKey: key,
                severity: severity,
                category: "General",
                source: "App",
                environmentName: "Test",
                machineName: "Host",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                isFatal: false,
                count: 1,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "123",
                caller: "Method",
                message: "Test message");
        }

        [Fact]
        public void FirstOccurrence_InitializesBaselineState()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 5, targetDestinations: new[] { "Email" })
            });

            var key = new EscalationKey("Rule1", "P1", "Exc1");
            var result = tracker.UpdateAndEvaluate(key, policy, CreateContext("Exc1"));

            Assert.Equal(1, result.CurrentLevel);
            Assert.Equal(1, result.PreviousLevel);
            Assert.Equal(1, result.OccurrenceCount);
            Assert.False(result.ThresholdCrossed);
            Assert.Contains("Notifier", result.EffectiveDestinations);
        }

        [Fact]
        public void OccurrenceCountThreshold_TransitionsLevelAndFlagsThresholdCrossed()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 5, targetDestinations: new[] { "Notifier", "Email" })
            });

            var key = new EscalationKey("Rule1", "P1", "Exc1");
            var ctx = CreateContext("Exc1");

            // Events 1 to 4
            for (int i = 1; i <= 4; i++)
            {
                var r = tracker.UpdateAndEvaluate(key, policy, ctx);
                Assert.Equal(1, r.CurrentLevel);
                Assert.Equal(i, r.OccurrenceCount);
                Assert.False(r.ThresholdCrossed);
            }

            // Event 5: crosses threshold!
            var r5 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(2, r5.CurrentLevel);
            Assert.Equal(1, r5.PreviousLevel);
            Assert.Equal(5, r5.OccurrenceCount);
            Assert.True(r5.ThresholdCrossed);
            Assert.Contains("Email", r5.EffectiveDestinations);

            // Event 6: remains at Level 2, ThresholdCrossed is false!
            var r6 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(2, r6.CurrentLevel);
            Assert.Equal(2, r6.PreviousLevel);
            Assert.Equal(6, r6.OccurrenceCount);
            Assert.False(r6.ThresholdCrossed);
        }

        [Fact]
        public void ElapsedTimeThreshold_TransitionsLevelWhenTimeExceeded()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForElapsedTime(2, TimeSpan.FromMinutes(10), targetDestinations: new[] { "PagerDuty" })
            });

            var key = new EscalationKey("Rule1", "P1", "Exc1");
            var ctx = CreateContext("Exc1");

            tracker.UpdateAndEvaluate(key, policy, ctx);

            clock.Advance(TimeSpan.FromMinutes(5));
            var r2 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(1, r2.CurrentLevel);
            Assert.False(r2.ThresholdCrossed);

            clock.Advance(TimeSpan.FromMinutes(6)); // Total elapsed 11 min
            var r3 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(2, r3.CurrentLevel);
            Assert.True(r3.ThresholdCrossed);
            Assert.Contains("PagerDuty", r3.EffectiveDestinations);
        }

        [Fact]
        public void SeverityThreshold_TransitionsLevelWhenCriticalSeverityArrives()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForSeverity(2, AlertSeverity.Critical, targetDestinations: new[] { "ExecutiveDesk" })
            });

            var key = new EscalationKey("Rule1", "P1", "Exc1");

            var r1 = tracker.UpdateAndEvaluate(key, policy, CreateContext("Exc1", AlertSeverity.Error));
            Assert.Equal(1, r1.CurrentLevel);
            Assert.False(r1.ThresholdCrossed);

            var r2 = tracker.UpdateAndEvaluate(key, policy, CreateContext("Exc1", AlertSeverity.Critical));
            Assert.Equal(2, r2.CurrentLevel);
            Assert.True(r2.ThresholdCrossed);
            Assert.Contains("ExecutiveDesk", r2.EffectiveDestinations);
        }

        [Fact]
        public void TtlExpiration_ResetsEscalationStateToBaseline()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 5, targetDestinations: new[] { "Email" })
            }, ttl: TimeSpan.FromMinutes(15));

            var key = new EscalationKey("Rule1", "P1", "Exc1");
            var ctx = CreateContext("Exc1");

            // Reach level 2
            for (int i = 0; i < 5; i++)
            {
                tracker.UpdateAndEvaluate(key, policy, ctx);
            }

            Assert.True(tracker.TryGetState(key, out var state));
            Assert.Equal(2, state!.CurrentLevel);
            Assert.Equal(5, state.OccurrenceCount);

            // Advance clock beyond 15 min TTL (e.g. 16 minutes of inactivity)
            clock.Advance(TimeSpan.FromMinutes(16));

            // Next event resets to Level 1, Count = 1
            var resetResult = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(1, resetResult.CurrentLevel);
            Assert.Equal(1, resetResult.OccurrenceCount);
            Assert.False(resetResult.ThresholdCrossed);
        }

        [Fact]
        public void MultipleLevels_ProgressSequentially()
        {
            var clock = new TestClock();
            var tracker = new BoundedEscalationTracker(100, clock);
            var policy = new EscalationPolicy("P1", new[]
            {
                EscalationStep.Initial(targetDestinations: new[] { "Notifier" }),
                EscalationStep.ForCount(2, 3, targetDestinations: new[] { "Notifier", "Email" }),
                EscalationStep.ForCount(3, 6, targetDestinations: new[] { "Notifier", "Email", "SMS" })
            });

            var key = new EscalationKey("Rule1", "P1", "Exc1");
            var ctx = CreateContext("Exc1");

            // Event 1, 2 -> Level 1
            tracker.UpdateAndEvaluate(key, policy, ctx);
            var r2 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(1, r2.CurrentLevel);

            // Event 3 -> Level 2
            var r3 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(2, r3.CurrentLevel);
            Assert.True(r3.ThresholdCrossed);

            // Event 4, 5 -> Level 2
            tracker.UpdateAndEvaluate(key, policy, ctx);
            var r5 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(2, r5.CurrentLevel);
            Assert.False(r5.ThresholdCrossed);

            // Event 6 -> Level 3
            var r6 = tracker.UpdateAndEvaluate(key, policy, ctx);
            Assert.Equal(3, r6.CurrentLevel);
            Assert.True(r6.ThresholdCrossed);
            Assert.Contains("SMS", r6.EffectiveDestinations);
        }

        [Fact]
        public void BoundedEscalationTracker_EnforcesCapacityBound()
        {
            var clock = new TestClock();
            const int maxCapacity = 20;
            var tracker = new BoundedEscalationTracker(maxCapacity, clock);
            var policy = new EscalationPolicy("P1", new[] { EscalationStep.Initial() });

            for (int i = 0; i < 50; i++)
            {
                var key = new EscalationKey("Rule1", "P1", $"Key_{i}");
                tracker.UpdateAndEvaluate(key, policy, CreateContext($"Key_{i}"));
            }

            Assert.True(tracker.Count <= maxCapacity + 5, $"Tracker count ({tracker.Count}) should not significantly exceed max capacity ({maxCapacity}).");
        }
    }
}
