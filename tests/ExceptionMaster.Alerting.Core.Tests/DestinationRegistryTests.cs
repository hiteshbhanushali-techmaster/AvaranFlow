using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Destinations;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class DestinationRegistryTests
    {
        [Fact]
        public void Registry_CaseInsensitiveLookup_AndDeterministicEnumeration()
        {
            var d1 = TestDestination.CreateSuccessful("Notifier");
            var d2 = TestDestination.CreateSuccessful("Email");
            var d3 = TestDestination.CreateSuccessful("SMS");

            var registry = new DestinationRegistry(new[] { d1, d2, d3 });

            Assert.True(registry.Contains("notifier"));
            Assert.True(registry.Contains("EMAIL"));
            Assert.True(registry.Contains("Sms"));

            Assert.Same(d1, registry.FindDestination("NOTIFIER"));
            Assert.Same(d2, registry.FindDestination("email"));
            Assert.Same(d3, registry.FindDestination("sms"));

            Assert.Null(registry.FindDestination("NonExistent"));
            Assert.False(registry.Contains("NonExistent"));

            var all = registry.GetAll();
            Assert.Equal(3, all.Count);
        }

        [Fact]
        public void Registry_DuplicateDestinationNames_ThrowsArgumentException()
        {
            var d1 = TestDestination.CreateSuccessful("DuplicateName");
            var d2 = TestDestination.CreateSuccessful("duplicatename"); // Case-insensitive duplicate

            var ex = Assert.Throws<ArgumentException>(() => new DestinationRegistry(new[] { d1, d2 }));
            Assert.Contains("Duplicate destination name", ex.Message);
        }

        [Fact]
        public void Registry_NullEntriesOrEmptyNames_ThrowsExpectedException()
        {
            Assert.Throws<ArgumentNullException>(() => new DestinationRegistry(new IAlertDestination[] { null! }));
            Assert.Throws<ArgumentException>(() => new DestinationRegistry(new[] { new TestDestination("") }));
            Assert.Throws<ArgumentException>(() => new DestinationRegistry(new[] { new TestDestination("   ") }));
        }

        [Fact]
        public void Registry_Dispose_DisposesAllDestinations_WithFailureIsolation()
        {
            var d1 = TestDestination.CreateSuccessful("NormalDest");
            var d2 = new CrashingDisposeDestination("CrashingDest");
            var d3 = TestDestination.CreateSuccessful("PostCrashDest");

            var registry = new DestinationRegistry(new IAlertDestination[] { d1, d2, d3 });

            // Disposing registry must not throw even if d2 throws during Dispose
            registry.Dispose();

            Assert.True(d1.IsDisposed);
            Assert.True(d3.IsDisposed);
        }

        [Fact]
        public void Groups_ValidGroupCreation_AndDuplicateEnforcement()
        {
            var group = new DestinationGroup("CriticalTrading", "Critical Trading Group", new[] { "Notifier", "Email", "SMS" });

            Assert.Equal("CriticalTrading", group.GroupId);
            Assert.Equal("Critical Trading Group", group.Name);
            Assert.Equal(3, group.DestinationNames.Count);

            // Duplicate destination names within the same group are rejected
            Assert.Throws<ArgumentException>(() => new DestinationGroup("G1", "G1", new[] { "Notifier", "notifier" }));

            // Empty destinations in a group are rejected
            Assert.Throws<ArgumentException>(() => new DestinationGroup("G2", "G2", new string[0]));
        }

        [Fact]
        public void GroupRegistry_DuplicateGroupIds_ThrowsArgumentException()
        {
            var g1 = new DestinationGroup("TradingAlerts", "Group 1", new[] { "Notifier" });
            var g2 = new DestinationGroup("tradingalerts", "Group 2", new[] { "Email" }); // Duplicate case-insensitive ID

            var ex = Assert.Throws<ArgumentException>(() => new DestinationGroupRegistry(new[] { g1, g2 }));
            Assert.Contains("Duplicate destination group ID", ex.Message);
        }

        [Fact]
        public void DestinationValidator_DetectsUnregisteredDestinationReferencesInGroupsAndChains()
        {
            var destRegistry = new DestinationRegistry(new[]
            {
                TestDestination.CreateSuccessful("Notifier"),
                TestDestination.CreateSuccessful("Email")
            });

            var groupRegistry = new DestinationGroupRegistry(new[]
            {
                new DestinationGroup("Group_Valid", "Valid", new[] { "Notifier", "Email" }),
                new DestinationGroup("Group_Invalid", "Invalid", new[] { "Notifier", "UnregisteredSMS" })
            });

            var chainRegistry = new FallbackChainRegistry(new[]
            {
                new FallbackChain("Chain_Valid", "Valid", new[] { "Email", "Notifier" }),
                new FallbackChain("Chain_Invalid", "Invalid", new[] { "Notifier", "UnregisteredWebhook" })
            });

            var validation = DestinationValidator.Validate(destRegistry, groupRegistry, chainRegistry);

            Assert.False(validation.IsValid);
            Assert.Equal(2, validation.Errors.Count);
            Assert.Contains(validation.Errors, e => e.Contains("UnregisteredSMS"));
            Assert.Contains(validation.Errors, e => e.Contains("UnregisteredWebhook"));
        }

        [Fact]
        public void FallbackChain_EnforcesAtLeastTwoDestinations_AndNoConsecutiveIdenticalDestinations()
        {
            // Fewer than 2 destinations
            Assert.Throws<ArgumentException>(() => new FallbackChain("Chain1", "One", new[] { "Notifier" }));

            // Consecutive identical destinations
            Assert.Throws<ArgumentException>(() => new FallbackChain("Chain2", "Consecutive", new[] { "SMS", "SMS" }));

            // Valid non-consecutive
            var validChain = new FallbackChain("Chain3", "Alternating", new[] { "SMS", "Email", "SMS" });
            Assert.Equal(3, validChain.DestinationNames.Count);
        }

        private sealed class CrashingDisposeDestination : IAlertDestination
        {
            public string Name { get; }
            public CrashingDisposeDestination(string name) => Name = name;
            public System.Threading.Tasks.Task<Models.AlertDeliveryResult> DeliverAsync(Models.AlertEnvelope envelope, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(Models.AlertDeliveryResult.Succeeded(Name, TimeSpan.Zero));
            public void Dispose() => throw new InvalidOperationException("Simulated destination disposal failure");
        }
    }
}
