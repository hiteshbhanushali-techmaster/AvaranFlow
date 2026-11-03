using System;
using System.Collections.Generic;
using AvaranFlow.Alerting.Enums;

namespace AvaranFlow.Alerting.Configuration
{
    /// <summary>
    /// Default validator for AlertingOptions enforcing non-negotiable boundaries, positive capacities,
    /// valid enum values, and sensible timeouts.
    /// </summary>
    public sealed class DefaultAlertingOptionsValidator : IAlertingOptionsValidator
    {
        public static readonly DefaultAlertingOptionsValidator Instance = new DefaultAlertingOptionsValidator();

        public ValidationResult Validate(AlertingOptions? options)
        {
            if (options == null)
            {
                return ValidationResult.Failure("AlertingOptions must not be null.");
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            // Queue capacity
            if (options.MaxQueueCapacity <= 0)
            {
                errors.Add($"MaxQueueCapacity must be positive. Provided: {options.MaxQueueCapacity}");
            }
            else if (options.MaxQueueCapacity < 100)
            {
                warnings.Add($"MaxQueueCapacity ({options.MaxQueueCapacity}) is very low and may easily saturate under burst conditions.");
            }

            // Concurrency
            if (options.WorkerConcurrency <= 0)
            {
                errors.Add($"WorkerConcurrency must be positive. Provided: {options.WorkerConcurrency}");
            }
            else if (options.WorkerConcurrency > 32)
            {
                warnings.Add($"WorkerConcurrency ({options.WorkerConcurrency}) is high and may cause excessive thread pool contention.");
            }

            if (options.GlobalMaxDestinationConcurrency <= 0)
            {
                errors.Add($"GlobalMaxDestinationConcurrency must be positive. Provided: {options.GlobalMaxDestinationConcurrency}");
            }

            // Timeouts
            if (options.DefaultDestinationTimeout <= TimeSpan.Zero)
            {
                errors.Add($"DefaultDestinationTimeout must be positive. Provided: {options.DefaultDestinationTimeout}");
            }

            if (options.EnqueueBlockTimeout <= TimeSpan.Zero)
            {
                errors.Add($"EnqueueBlockTimeout must be positive. Provided: {options.EnqueueBlockTimeout}");
            }

            if (options.ShutdownTimeout <= TimeSpan.Zero)
            {
                errors.Add($"ShutdownTimeout must be positive. Provided: {options.ShutdownTimeout}");
            }

            if (options.ReconfigureDrainTimeout <= TimeSpan.Zero)
            {
                errors.Add($"ReconfigureDrainTimeout must be positive. Provided: {options.ReconfigureDrainTimeout}");
            }
            else if (options.ReconfigureDrainTimeout > options.ShutdownTimeout)
            {
                warnings.Add($"ReconfigureDrainTimeout ({options.ReconfigureDrainTimeout}) exceeds ShutdownTimeout ({options.ShutdownTimeout}).");
            }

            // Cache boundaries
            if (options.MaxSuppressionEntries <= 0)
            {
                errors.Add($"MaxSuppressionEntries must be positive. Provided: {options.MaxSuppressionEntries}");
            }

            if (options.MaxEscalationEntries <= 0)
            {
                errors.Add($"MaxEscalationEntries must be positive. Provided: {options.MaxEscalationEntries}");
            }

            // Enum validations
            if (!Enum.IsDefined(typeof(AlertSeverity), options.DefaultSeverity))
            {
                errors.Add($"DefaultSeverity value ({(int)options.DefaultSeverity}) is not a defined AlertSeverity enum.");
            }

            if (!Enum.IsDefined(typeof(QueueOverflowPolicy), options.OverflowPolicy))
            {
                errors.Add($"OverflowPolicy value ({(int)options.OverflowPolicy}) is not a defined QueueOverflowPolicy enum.");
            }

            if (!Enum.IsDefined(typeof(FanOutDispatchMode), options.FanOutMode))
            {
                errors.Add($"FanOutMode value ({(int)options.FanOutMode}) is not a defined FanOutDispatchMode enum.");
            }

            return errors.Count > 0
                ? ValidationResult.Failure(errors, warnings)
                : ValidationResult.Success(warnings);
        }
    }
}
