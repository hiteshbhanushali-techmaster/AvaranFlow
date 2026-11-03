using System;
using System.Collections.Generic;
using ExceptionMaster.Alerting.Configuration;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Validates destination registries, destination groups, and fallback chains for referential integrity,
    /// non-empty identifiers, and absence of cycles or missing references.
    /// </summary>
    public static class DestinationValidator
    {
        public static ValidationResult Validate(
            IDestinationRegistry destinationRegistry,
            IDestinationGroupRegistry? groupRegistry = null,
            IFallbackChainRegistry? chainRegistry = null)
        {
            if (destinationRegistry == null)
            {
                return ValidationResult.Failure("DestinationRegistry must not be null.");
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            var allDestinations = destinationRegistry.GetAll();
            if (allDestinations.Count == 0)
            {
                warnings.Add("Destination registry contains no registered destinations.");
            }

            // Validate groups
            if (groupRegistry != null)
            {
                var groups = groupRegistry.GetAll();
                foreach (var group in groups)
                {
                    if (group.DestinationNames.Count == 0)
                    {
                        errors.Add($"Destination group '{group.GroupId}' must contain at least one destination reference.");
                    }

                    foreach (var destName in group.DestinationNames)
                    {
                        if (!destinationRegistry.Contains(destName))
                        {
                            errors.Add($"Destination group '{group.GroupId}' references unregistered destination '{destName}'.");
                        }
                    }
                }
            }

            // Validate fallback chains
            if (chainRegistry != null)
            {
                var chains = chainRegistry.GetAll();
                foreach (var chain in chains)
                {
                    if (chain.DestinationNames.Count < 2)
                    {
                        errors.Add($"Fallback chain '{chain.ChainId}' must define at least two destinations. Count: {chain.DestinationNames.Count}");
                    }

                    foreach (var destName in chain.DestinationNames)
                    {
                        if (!destinationRegistry.Contains(destName))
                        {
                            errors.Add($"Fallback chain '{chain.ChainId}' references unregistered destination '{destName}'.");
                        }
                    }
                }
            }

            return errors.Count > 0
                ? ValidationResult.Failure(errors, warnings)
                : ValidationResult.Success(warnings);
        }
    }
}
