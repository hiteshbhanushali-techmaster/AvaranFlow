using System;
using AvaranFlow.Alerting.Suppression;
using Xunit;

namespace AvaranFlow.Alerting.Routing.Tests
{
    public class BoundedSuppressionCacheTests
    {
        [Fact]
        public void TryAcquireDelivery_FirstEvent_PermitsAndStartsCooldown()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(100, clock);
            var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc1");

            bool acquired = cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out var remaining);

            Assert.True(acquired);
            Assert.Equal(TimeSpan.Zero, remaining);
            Assert.True(cache.IsInCooldown(key, TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public void TryAcquireDelivery_RepeatedEventWithinCooldown_IsSuppressed()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(100, clock);
            var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc1");

            cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out _);

            clock.Advance(TimeSpan.FromMinutes(2));

            bool acquired = cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out var remaining);

            Assert.False(acquired);
            Assert.True(remaining > TimeSpan.Zero);
            Assert.True(remaining <= TimeSpan.FromMinutes(3));
        }

        [Fact]
        public void TryAcquireDelivery_AfterCooldownExpires_PermitsDeliveryAgain()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(100, clock);
            var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc1");

            cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out _);

            clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

            Assert.False(cache.IsInCooldown(key, TimeSpan.FromMinutes(5)));

            bool acquired = cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out var remaining);

            Assert.True(acquired);
            Assert.Equal(TimeSpan.Zero, remaining);
        }

        [Fact]
        public void ResetCooldown_UnconditionallyStartsNewCooldown()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(100, clock);
            var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc1");

            cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out _);
            clock.Advance(TimeSpan.FromMinutes(4)); // 1 min left

            // Reset cooldown with 10 min cooldown window
            cache.ResetCooldown(key, TimeSpan.FromMinutes(10));

            clock.Advance(TimeSpan.FromMinutes(6)); // Would have expired under original 5 min, but new 10 min window active!
            Assert.True(cache.IsInCooldown(key, TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void SuppressionCache_IsolatesDifferentKeys()
        {
            var clock = new TestClock();
            var cache = new BoundedSuppressionCache(100, clock);
            var key1 = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc1");
            var key2 = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", "Exc2");
            var keyRuleB = new SuppressionKey("RuleB", "Policy1", "ExceptionKey", "Exc1");

            cache.TryAcquireDelivery(key1, TimeSpan.FromMinutes(5), out _);

            Assert.True(cache.IsInCooldown(key1, TimeSpan.FromMinutes(5)));
            Assert.False(cache.IsInCooldown(key2, TimeSpan.FromMinutes(5)));
            Assert.False(cache.IsInCooldown(keyRuleB, TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public void BoundedSuppressionCache_EnforcesBoundedCapacity()
        {
            var clock = new TestClock();
            const int maxCapacity = 20;
            var cache = new BoundedSuppressionCache(maxCapacity, clock);

            for (int i = 0; i < 50; i++)
            {
                var key = new SuppressionKey("RuleA", "Policy1", "ExceptionKey", $"Exc_{i}");
                cache.TryAcquireDelivery(key, TimeSpan.FromMinutes(5), out _);
            }

            Assert.True(cache.Count <= maxCapacity + 5, $"Cache count ({cache.Count}) should not significantly exceed max capacity ({maxCapacity}).");
        }
    }
}
