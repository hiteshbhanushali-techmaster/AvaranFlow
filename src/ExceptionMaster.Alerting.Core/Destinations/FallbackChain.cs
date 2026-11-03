using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Destinations
{
    /// <summary>
    /// Represents an immutable ordered fallback chain of destinations.
    /// Execution proceeds sequentially: Primary is attempted first; subsequent destinations are
    /// attempted ONLY if the preceding destination fails.
    /// Requires at least two destinations.
    /// </summary>
    public sealed class FallbackChain
    {
        public string ChainId { get; }
        public string Name { get; }
        public IReadOnlyList<string> DestinationNames { get; }

        public FallbackChain(string chainId, string? name, IEnumerable<string> destinationNames)
        {
            if (string.IsNullOrWhiteSpace(chainId))
                throw new ArgumentException("ChainId must not be null or whitespace.", nameof(chainId));

            ChainId = chainId.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? ChainId : name!.Trim();

            if (destinationNames == null)
                throw new ArgumentNullException(nameof(destinationNames), "DestinationNames cannot be null.");

            var list = new List<string>();
            foreach (var dest in destinationNames)
            {
                if (string.IsNullOrWhiteSpace(dest))
                    throw new ArgumentException("Fallback destination name cannot be null or whitespace.", nameof(destinationNames));

                string clean = dest.Trim();
                list.Add(clean);
            }

            if (list.Count < 2)
            {
                throw new ArgumentException(
                    $"Fallback chain '{ChainId}' must define at least two destinations (Primary and at least one Fallback). Count: {list.Count}",
                    nameof(destinationNames));
            }

            // Verify no adjacent identical destinations (e.g. SMS -> SMS)
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (string.Equals(list[i], list[i + 1], StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Fallback chain '{ChainId}' contains consecutive identical destinations at indices {i} and {i + 1}: '{list[i]}'.",
                        nameof(destinationNames));
                }
            }

            DestinationNames = new ReadOnlyCollection<string>(list);
        }

        public override string ToString()
        {
            return $"FallbackChain [{ChainId}: {string.Join(" -> ", DestinationNames)}]";
        }
    }
}
