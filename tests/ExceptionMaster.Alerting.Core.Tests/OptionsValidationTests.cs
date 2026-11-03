using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Configuration;
using ExceptionMaster.Alerting.Enums;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class OptionsValidationTests
    {
        private readonly DefaultAlertingOptionsValidator _validator = DefaultAlertingOptionsValidator.Instance;

        [Fact]
        public void DefaultOptions_AreValid()
        {
            var options = new AlertingOptions();
            var result = _validator.Validate(options);

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void NullOptions_ReturnsFailure()
        {
            var result = _validator.Validate(null);
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-500)]
        public void InvalidQueueCapacity_FailsValidation(int capacity)
        {
            var options = new AlertingOptions { MaxQueueCapacity = capacity };
            var result = _validator.Validate(options);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("MaxQueueCapacity"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidWorkerConcurrency_FailsValidation(int concurrency)
        {
            var options = new AlertingOptions { WorkerConcurrency = concurrency };
            var result = _validator.Validate(options);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("WorkerConcurrency"));
        }

        [Fact]
        public void NonPositiveTimeouts_FailValidation()
        {
            var options = new AlertingOptions
            {
                DefaultDestinationTimeout = TimeSpan.Zero,
                EnqueueBlockTimeout = TimeSpan.FromSeconds(-1),
                ShutdownTimeout = TimeSpan.Zero,
                ReconfigureDrainTimeout = TimeSpan.FromSeconds(-2)
            };

            var result = _validator.Validate(options);
            Assert.False(result.IsValid);
            Assert.Equal(4, result.Errors.Count);
        }

        [Fact]
        public void UndefinedEnums_FailValidation()
        {
            var options = new AlertingOptions
            {
                DefaultSeverity = (AlertSeverity)99,
                OverflowPolicy = (QueueOverflowPolicy)88,
                FanOutMode = (FanOutDispatchMode)77
            };

            var result = _validator.Validate(options);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("DefaultSeverity"));
            Assert.Contains(result.Errors, e => e.Contains("OverflowPolicy"));
            Assert.Contains(result.Errors, e => e.Contains("FanOutMode"));
        }

        [Fact]
        public void Warnings_GeneratedForSuboptimalConfigurations()
        {
            var options = new AlertingOptions
            {
                MaxQueueCapacity = 50, // Low capacity warning
                WorkerConcurrency = 40, // High concurrency warning
                ReconfigureDrainTimeout = TimeSpan.FromSeconds(10), // Exceeds shutdown timeout
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            };

            var result = _validator.Validate(options);
            Assert.True(result.IsValid); // Still valid, but has warnings
            Assert.Equal(3, result.Warnings.Count);
        }

        [Fact]
        public void Clone_CreatesIndependentDeepCopy()
        {
            var original = new AlertingOptions
            {
                MaxQueueCapacity = 1000,
                OverflowPolicy = QueueOverflowPolicy.BlockWithTimeout,
                WorkerConcurrency = 4,
                DefaultSeverity = AlertSeverity.Critical,
                DefaultDestinationTimeout = TimeSpan.FromSeconds(10),
                GlobalMaxDestinationConcurrency = 8,
                FanOutMode = FanOutDispatchMode.Sequential,
                EnqueueBlockTimeout = TimeSpan.FromMilliseconds(500),
                ShutdownTimeout = TimeSpan.FromSeconds(15),
                ReconfigureDrainTimeout = TimeSpan.FromSeconds(7),
                SubscribeToExceptionMaster = false,
                MaxSuppressionEntries = 20000,
                MaxEscalationEntries = 8000,
                SafeMetadataKeys = new List<string> { "UserId", "OrderId" },
                IncludeSafeStackTrace = true
            };

            var clone = original.Clone();

            Assert.Equal(original.MaxQueueCapacity, clone.MaxQueueCapacity);
            Assert.Equal(original.OverflowPolicy, clone.OverflowPolicy);
            Assert.Equal(original.WorkerConcurrency, clone.WorkerConcurrency);
            Assert.Equal(original.DefaultSeverity, clone.DefaultSeverity);
            Assert.Equal(original.DefaultDestinationTimeout, clone.DefaultDestinationTimeout);
            Assert.Equal(original.GlobalMaxDestinationConcurrency, clone.GlobalMaxDestinationConcurrency);
            Assert.Equal(original.FanOutMode, clone.FanOutMode);
            Assert.Equal(original.EnqueueBlockTimeout, clone.EnqueueBlockTimeout);
            Assert.Equal(original.ShutdownTimeout, clone.ShutdownTimeout);
            Assert.Equal(original.ReconfigureDrainTimeout, clone.ReconfigureDrainTimeout);
            Assert.Equal(original.SubscribeToExceptionMaster, clone.SubscribeToExceptionMaster);
            Assert.Equal(original.MaxSuppressionEntries, clone.MaxSuppressionEntries);
            Assert.Equal(original.MaxEscalationEntries, clone.MaxEscalationEntries);
            Assert.Equal(original.IncludeSafeStackTrace, clone.IncludeSafeStackTrace);
            Assert.Equal(original.SafeMetadataKeys, clone.SafeMetadataKeys);

            // Mutate clone; original remains untouched
            clone.MaxQueueCapacity = 9999;
            Assert.Equal(1000, original.MaxQueueCapacity);
        }
    }
}
